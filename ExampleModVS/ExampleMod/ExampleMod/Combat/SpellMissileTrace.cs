// ═══════════════════════════════════════════════════════════════════════════
// 法术弹飞行诊断 —— 调试脚手架（2026-09-22 立）
//
// 解决什么问题：实机上「月牙飞出去一段就没了」到底是
//   ①**真的没了**（native 把它删了）——那就要问：在哪一刻、因为什么没的？
//   ②**还在飞，只是渲染看不见**——那是网格/材质/可见性的事。
//
// 判据三条（全部来自 1.2.12 反编译实证，不是猜的）：
//
//   ① 存活 = `Mission.Missiles` 字典里还有这个 index。
//      native 摘条目走 `Mission.OnMissileRemoved(index)`（internal [MBCallback]，Mission.cs:5519）
//      → `_missiles.Remove(index)`。⚠️ 公开行为层**没有**"导弹被移除"钩子，所以本件用
//      **每帧 diff 字典**自己判：上一帧在、这一帧不在 = native 已摘除。
//
//   ② 结局 = `MissionBehavior.OnMissileCollisionReaction(reaction, ...)`
//      （`Mission.HandleMissileCollisionReaction` 对每个 behavior 派发，Mission.cs:5775 起）。
//      四值语义（源码原文）：
//        · Stick           → 插住，转成地上的掉落物（月牙撞地**正常应该走这条**）
//        · BounceBack      → 弹开，同样转成掉落物
//        · PassThrough     → 穿过去了，继续飞
//        · BecomeInvisible → 🔴 `missile.Entity.Remove(81)` —— **凭空消失就是它**
//      什么情况下 BecomeInvisible（`Mission.MissileHitCallback` 原文判定）：
//        · 弹药带 `WeaponFlags.Burning`
//        · `collisionData.MissileGoneOutOfBorder`（飞出战场边界）/ `MissileGoneUnderWater`
//        · 命中的目标处于无敌状态
//      ⚠️ 该钩子**不带导弹 index** ⇒ 本件用「同帧唯一消失的那一发」来归属（单发场景 100% 准；
//      同时有多发在跟踪时日志会注明"无法归属"）。
//
//   ③ 命中 = `OnMissileHit(attacker, victim, isCanceled, collisionData)`，**打地形也会调**
//      （`victim == null`）；导弹身份认 `collisionData.AffectorWeaponSlotOrMissileIndex`。
//     （Mission.cs 全文都是拿这个字段去 `_missiles[...]` 取导弹的，见 :5310 / :5528）
//
// 示踪（肉眼判据）：把一发粒子系统**挂在导弹自己的实体上**（`ParticleSystem
//   .CreateParticleSystemAttachedToEntity(id, missile.Entity, ...)`）——这正是原版
//   `trail_particle_name` 的工作方式，所以引擎会**自动带着它走**，不需要我们每帧挪。
//   看得见火线 = 这发弹在引擎里确实还在、还在动；火线断了/弹没了 = native 真的处理掉它了。
//   粒子用**原版现成件**（默认 `psys_game_missile_flame`），不依赖我们自己的粒子资产（那还没接）。
//
// 用法（游戏内 `~` 控制台；返回文本纯英文，诊断详情走 DebugLogger 中文）：
//   custom.spell_trace                   # 开关（不填参数 = 翻转）
//   custom.spell_trace on|off            # 显式开关
//   custom.spell_trace status            # 当前配置
//   custom.spell_trace ammo <item_id>    # 换认领的弹药 StringId（默认 taikou_spell_crescent）
//   custom.spell_trace tracer on|off     # 只留日志、关掉火线
//   custom.spell_trace particle <name>   # 换示踪粒子名（原版件）
//   custom.spell_trace interval <sec>    # 状态行间隔，默认 0.1（调小 = 更密，日志更大）
//   custom.spell_trace lod0 on|off       # A/B 实验：强制导弹实体只显示 LOD0（见下）
//   custom.spell_trace alt_mesh <名|off> # 陪飞对照组：在真弹旁边平行放一个指定网格的实体（见下）
//   custom.spell_trace alt_offset <米>   # 陪飞实体的横向偏移（默认 2.5m，必须错开才看得出谁是谁）
//   custom.spell_trace alt_scale <倍率>  # 陪飞实体放大（默认 1；原版霰弹碎片只有 0.22m，不放大没得比）
//   custom.spell_trace alt_particle <名|off> # 陪飞实体的拖尾粒子（默认 psys_game_burning_jar_trail）
//   custom.spell_trace speed <倍率>      # 试手感：弩类导弹速度倍率（0.5 = 半速；1 = 原样）
//   custom.spell_trace scale <倍率>      # A/B 实验：把法术弹放大 N 倍（5 = 五倍大；1 = 原样）
//
// 放大实验（2026-09-22 用户要求，用来切开两个假设）：
//   放大 5 倍 = 屏幕覆盖面积 25 倍。
//   · 放大后就看得远得多 ⇒ "看不见"是因为**太小**（薄刃在远处不足一像素），跟 LOD 无关；
//   · 放大完全没用     ⇒ 是**硬性距离剔除**（LOD/剔除），与大小无关。
//   实现 = 钩 `MissionWeapon.OnGetWeaponDataHandler` 改 `WeaponData.ScaleFactor`（逐发、不动数据）。
//   ⚠️ 定稿写法：`spells.xml` 的 `taikou_spell_crescent` 加 `scale_factor="5"`
//      （物品级该属性是**纯倍率**，默认 1；别照抄 `<Piece scale_factor="100">` 那套百分比约定）。
//
// 让弹飞得慢一点（2026-09-22 用户要求）：
//   物理事实：**同一瞄准角度下"慢"和"远"是对立的** —— 慢 = 重力先把它按到地上 = 飞得近。
//   两条路：
//   · 试手感（当场生效、不动数据）：`custom.spell_trace speed 0.5`
//     → 走 `Mission.SetCrossbowMissileSpeedModifier`（原版天气就用它给雨天减速，影响**实际**飞行速度）。
//     ⚠️ 全局：同场景所有弩（含 NPC 弩手）一起变；且天气系统会重置，所以本件每帧重刷。
//   · 定稿（写进数据）：改 `Modules/Taikou/ModuleData/taikou_items/spells.xml` 里
//     `taikou_spell_seal` 的 `missile_speed`（现在 180，原版弩是 60）。改完要重开局。
//   ⚠️ 之前那次"60 就没飞多远"的判断**已被日志推翻**：真因是撞到石头（材质号 7）触发碎裂消失，
//     不是初速低。所以 60~120 段完全值得重试。
//
// 陪飞对照组（2026-09-22 加，回答"换个网格会不会一样看不见"）：
//   在真弹旁边 2.5 米平行放一个**场景实体**，网格由 `alt_mesh` 指定，每帧跟着真弹走 ——
//   同一条路径、同一个速度，两个东西并排飞，**谁先消失一眼可判**。
//   · 默认建议值（BattleArtillery 炮弹用的就是它）：`projectile_grapeshot_piece_a_fire`
//     —— 它本身是**原版资产**（Native/AssetPackages/meshes_shared_7.tpac，原版投石机霰弹），
//     1.2.12 上直接可用，**不需要**装 BattleArtillery（那 mod 只装在 1.5.3 那台）。
//   · 拖尾也照抄了人家炮弹（`psys_game_burning_jar_trail`），且**可单独关**
//     （`alt_particle off`）—— 🔴 必须能分清"远处看见的是网格"还是"只是拖尾"，那是两条结论。
//   · 顺带一条对照事实：人家炮弹带 `WeaponFlags Burning="true"`，命中即 `BecomeInvisible`
//     （Mission.cs:5551）—— 它撞到东西**也是凭空消失**，靠的是"拖尾在飞"而不是"炮弹一直在"。
//   · 若陪飞的还在、月牙没了 ⇒ 问题在月牙这个网格资产（LOD/几何/材质）；
//     若陪飞的也一样没 ⇒ 是"远距离渲染"的普遍行为，不是月牙独有的毛病。
//   ⚠️ 它是场景实体、不是真导弹：能回答"换个网格会怎样"，但不能代表导弹渲染路径完全一致。
//
// LOD 诊断（2026-09-22 加，回答"飞远了 mesh 看不见"）：
//   · 认领时打「网格家底」：MetaMesh 数 + 每个 MetaMesh 的 `HasAnyLods()` / LOD 掩码。
//     `有任意LOD=False` ⇒ 这个网格只有 LOD0（程序生成的网格典型）。
//   · 每条状态行打「相机距 + 该选LOD」：`GameEntity.GetLodLevelForDistanceSq()` 直接问引擎
//     在这个相机距离上打算用哪一档。
//   · 官方 LOD 距离表（docs: Asset Management/Asset Types/meshes.md「LOD System」）：
//     **15 / 22.5 / 30 / 50 / 70 / 130 / 210 米**，超出最后一级 = 剔除。
//   · `custom.spell_trace lod0 on` = 认领时对实体调 `SetEnforcedMaximumLodLevel(0)`：
//     若开了它在远处就能看见月牙 ⇒ LOD 剔除确认，且这个调用就是现成的修法（逐发、不动资产）。
//
// 日志（Debug/StoryEngine_RuntimeLog.txt，标签 [SpellTrace]）：
//   🔴 **关键事件永远记录**（不需要开总开关）：发射 / 认领 / 网格家底 / 命中 / 结局 / 消失。
//      总开关（`custom.spell_trace on`）只管**每 0.1 秒一行的高频状态**（速度/已飞/相机距/LOD 档）。
//      —— 2026-09-22 改：总开关不跨会话保留，已两次出现"敲了 lod0 on 以为生效、其实没开"的白跑。
//   发射 → 认领 → 每 interval 秒一条状态（速度/已飞/离射手/高度/刚体/实体是否还在）
//   → 命中（含地形）→ 结局（Stick/BounceBack/PassThrough/BecomeInvisible）
//   → 🔴 从导弹表消失（最后位置 + 飞了多远 + 用时 + 最后速度 + 同帧结局）
//
// 开销：关着时每帧只判一个 bool；开着时每帧扫一遍 `Mission.Missiles`（纯字段读 + 字符串比对）。
//
// 🔴 这是脚手架，验完即删：本文件 + `ExampleModVS/.../ExampleMod.csproj` 里那一行
//    + `Core/MySubModule.cs` 里的挂载行，三处一起删。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using MissionMissile = TaleWorlds.MountAndBlade.Mission.Missile;
using MissileReaction = TaleWorlds.MountAndBlade.Mission.MissileCollisionReaction;

namespace LivingWorldNpcs
{
    /// <summary>诊断开关与配置（控制台可改；静态存活，与 Mission 生命周期无关）。</summary>
    public static class SpellMissileTraceState
    {
        /// <summary>总开关。默认关 —— 关着时每帧只判这一个 bool。</summary>
        public static bool Enabled;

        /// <summary>示踪火线开关（只留日志时关掉）。</summary>
        public static bool TracerEnabled = true;

        /// <summary>认领哪一发的判据 = 弹药物品的 StringId（铁律 20：一律 StringId，不用显示名）。</summary>
        public static string AmmoFilter = "taikou_spell_crescent";

        /// <summary>示踪粒子名（原版现成件；查不到 → 只在日志里报一次，不影响其余功能）。</summary>
        public static string TracerParticle = "psys_game_missile_flame";

        /// <summary>状态行间隔（秒）。</summary>
        public static float LogInterval = 0.1f;

        /// <summary>🔴 A/B 实验开关：认领时对导弹实体强制"最高只到 LOD0"
        /// （<c>GameEntity.SetEnforcedMaximumLodLevel(0)</c>）。
        /// 原版 LOD 距离表（官方文档 meshes.md）：15 / 22.5 / 30 / 50 / 70 / 130 / 210 米，
        /// **超出最后一级 = 剔除**。月牙是程序生成网格、很可能只有 LOD0（本件会把家底打出来）；
        /// 若开了这个开关后在远处就能看见月牙 → 就是 LOD 剔除干的，且这就是现成的修法。</summary>
        public static bool ForceLod0;

        /// <summary>
        /// 陪飞对照组：在真弹旁边**平行**放一个指定网格的实体，同一路径同一速度飞 ——
        /// 一眼看出"谁先消失"（2026-09-22 用户要求：拿别人的炮弹当对照，看看它能看到飞多远）。
        /// 🔴 它是**场景实体**，不是真导弹：用来回答"换个网格会不会一样消失"，
        /// 不代表导弹的渲染路径完全相同（这点差异要在结论里说清）。
        /// 默认值 = BattleArtillery 炮弹用的那个网格，而它本身就是**原版资产**
        /// （`Native/AssetPackages/meshes_shared_7.tpac`，原版投石机霰弹），所以 1.2.12 上直接可用，
        /// **不需要**装 BattleArtillery（那个 mod 只在 1.5.3 那台客户端）。
        /// 空 = 关。
        /// </summary>
        public static string AltMesh = "";

        /// <summary>陪飞实体相对真弹的横向偏移（米）—— 必须错开，否则两个重叠看不出是谁。</summary>
        public static float AltOffsetMeters = 2.5f;

        /// <summary>
        /// 陪飞实体的放大倍数（2026-09-22 用户要求"改大十倍"）。
        /// 🔴 **需要它是因为陪飞用的原版霰弹碎片只有 0.22 m** —— 200 米外本来就是几个像素，
        ///    不放大的话"它也没了"什么都证明不了。放大到与月牙同量级，比较才成立。
        /// 实现 = 实体的 <c>MatrixFrame</c> 基向量乘倍数（`Mat3` 的基向量长度 = 缩放，见 `Mat3.GetScaleVector`）。
        /// </summary>
        public static float AltScale = 1f;

        /// <summary>陪飞实体是否也挂拖尾粒子（默认开 = 照抄人家炮弹：它自己带
        /// <c>trail_particle_name</c> + <c>LeavesTrail</c>）。🔴 做成可关，因为必须能分清
        /// "远处看见的是**网格**"还是"只是**拖尾**"——这是两条完全不同的结论。</summary>
        public static bool AltParticleEnabled = true;

        /// <summary>陪飞实体的拖尾粒子名 —— 默认就是 BattleArtillery 炮弹用的那个（原版件）。</summary>
        public static string AltParticle = "psys_game_burning_jar_trail";

        /// <summary>
        /// 调试用：**弩类**导弹速度倍率（1 = 原样）。走 <c>Mission.SetCrossbowMissileSpeedModifier</c>
        /// —— 原版天气系统就是用它给雨天减速的（`CustomBattleApplyWeatherEffectsModel`），
        /// 所以这是引擎认的正规旋钮，会影响**实际飞行速度**（不只是 AI 瞄准）。
        /// 用法 = 先在这里试出手感（当场生效、不用重开局），定了值再写进
        /// `Modules/Taikou/ModuleData/taikou_items/spells.xml` 的 `missile_speed`。
        /// ⚠️ 全局生效：同一场景里**所有弩**（含 NPC 弩手）一起变 —— 调试可以，别当成品。
        /// </summary>
        public static float SpeedModifier = 1f;

        /// <summary>
        /// 🔴 A/B 实验：把法术弹**放大 N 倍**（1 = 原样）。回答"飞远了看不见是不是因为薄刃太小"：
        /// 放大 5 倍 = 屏幕覆盖面积 25 倍 ⇒ 若"看不见"是小到看不见，放大后应该能看得远得多；
        /// 若放大完全没用 ⇒ 是硬性距离剔除（LOD/剔除），与大小无关。
        ///
        /// 实现 = 钩 <c>MissionWeapon.OnGetWeaponDataHandler</c>，把 <c>WeaponData.ScaleFactor</c> 改掉
        /// （该字段的**物品级**约定是纯倍率：`ItemObject.cs:566` 默认 1f；XML 属性 `scale_factor`）。
        /// 逐发生效、不碰共享资产、不动数据文件、不用重开局。
        /// ⚠️ 定稿要写进数据时 = `Modules/Taikou/ModuleData/taikou_items/spells.xml` 里
        ///    `taikou_spell_crescent` 加 `scale_factor="5"`（注意：`<CraftedItem><Piece>` 里那个
        ///    `scale_factor="100"` 是**铸剑零件的百分比刻度**，另一套约定，别照抄）。
        /// </summary>
        public static float ItemScale = 1f;
    }

    /// <summary>
    /// 法术弹飞行诊断。挂在 <c>MySubModule.OnMissionBehaviorInitialize</c> 玩法闸门**之前**
    /// （照 NavMeshDebugMissionView / FirearmFxLogic 的做派）—— 要测的正是战场里的弹道。
    /// </summary>
    public class SpellMissileTrace : MissionLogic
    {
        /// <summary>一发的跟踪记录。</summary>
        private sealed class Tracked
        {
            public int Index;
            public float BornTime;
            public Vec3 BornPos;        // 首次被看到的弹位置（≈ 出膛点）
            public Vec3 ShooterPos;     // 认领时射手站的位置
            public string Shooter;
            public string AmmoId;
            public float LastSeenTime;
            public Vec3 LastPos;
            public float LastLogTime;
            public int HitCount;
            public float TotalDamage;
            public GameEntity TracerHost;
            public ParticleSystem Tracer;
            /// <summary>陪飞对照组实体（<see cref="SpellMissileTraceState.AltMesh"/> 非空时才建）。</summary>
            public GameEntity AltEntity;
            /// <summary>陪飞实体的拖尾粒子（照抄人家炮弹的 trail_particle_name）。</summary>
            public ParticleSystem AltParticlePs;
            /// <summary>引擎已通知该实体被移除（<c>OnEntityRemoved</c>）—— 之后不再碰它。
            /// ⚠️ 不用 <c>WeakEntity.IsValid</c>（那是 1.5.x 专有，1.2.12 无此 API），
            /// 也**不**拿读属性去试探存活：悬空指针上读 native 可能直接 AccessViolation（不是托管异常）。</summary>
            public bool EntityRemoved;
        }

        private readonly Dictionary<int, Tracked> _tracked = new Dictionary<int, Tracked>();
        private readonly List<int> _goneBuffer = new List<int>(8);
        private readonly HashSet<string> _loggedOnce = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>同帧内最后一次收到的结局（用于把「消失」与「结局」对上号）。</summary>
        private MissileReaction _lastReaction = MissileReaction.Invalid;
        private bool _lastReactionFresh;

        private bool _tracerNameBroken;

        // ─────────────────────────── 发射 ───────────────────────────

        public override void OnAgentShootMissile(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position,
            Vec3 velocity, Mat3 orientation, bool hasRigidBody, int forcedMissileIndex)
        {
            base.OnAgentShootMissile(shooterAgent, weaponIndex, position, velocity, orientation, hasRigidBody, forcedMissileIndex);
            try
            {
                if (shooterAgent == null || Mission == null)
                {
                    return;
                }

                // 引擎自己就是这么取"这一发打的是哪种弹药"的（Mission.OnAgentShootMissile:4683-4694）：
                // 手持的是可消耗远程武器 → 就是它；否则取它的 AmmoWeapon。逐字照抄，免得口径不同。
                string ammoId = null;
                try
                {
                    MissionWeapon held = shooterAgent.Equipment[weaponIndex];
                    MissionWeapon missileWeapon = (held.CurrentUsageItem != null
                        && held.CurrentUsageItem.IsRangedWeapon
                        && held.CurrentUsageItem.IsConsumable)
                        ? held
                        : held.AmmoWeapon;
                    ammoId = missileWeapon.Item?.StringId;
                }
                catch (Exception)
                {
                    // 装备槽异常（forced missile 等）→ 弹药名留空，下一帧认领时会补上
                }

                bool isPlayer = shooterAgent == Mission.MainAgent;
                if (!isPlayer && !MatchAmmo(ammoId))
                {
                    return;
                }

                DebugLogger.Log($"[SpellTrace] 发射: {(isPlayer ? "玩家" : "NPC")} 射手={AgentLabel(shooterAgent)} "
                    + $"弹药={ammoId ?? "?"} 出膛位置={Pos(position)} 初速={velocity.Length:F1} "
                    + $"有刚体={hasRigidBody} 槽位={weaponIndex} forcedIndex={forcedMissileIndex}");
            }
            catch (Exception ex)
            {
                LogOnce("发射回调异常", ex);
            }
        }

        // ─────────────────────────── 每帧 ───────────────────────────

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            // 🔴 2026-09-22 改：**不再受总开关管辖** —— 关键事件（认领 / 网格家底 / 命中 / 结局 /
            //    消失）永远记录，总开关只管每 0.1 秒的高频状态行。
            //    原因：总开关不跨会话保留，已经两次出现"敲了 lod0 on 就以为生效、其实没开"的白跑。
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                LogOnce("每帧异常", ex);
            }
        }

        private void Tick()
        {
            if (Mission == null || Mission.Missiles == null)
            {
                return;
            }
            ApplySpeedModifier();
            float now = Mission.CurrentTime;

            foreach (MissionMissile missile in Mission.Missiles)
            {
                Tracked tracked;
                if (!_tracked.TryGetValue(missile.Index, out tracked))
                {
                    TryClaim(missile, now);
                    continue;
                }
                tracked.LastSeenTime = now;
                tracked.LastPos = SafePosition(missile, tracked.LastPos);
                ApplyEnforcedLod0(missile);
                MoveAltStandIn(tracked, missile);
                if (SpellMissileTraceState.Enabled
                    && now - tracked.LastLogTime >= SpellMissileTraceState.LogInterval)
                {
                    tracked.LastLogTime = now;
                    LogState(tracked, missile, now);
                }
            }

            // 摘除检测：上一帧在跟踪、这一帧没在字典里出现 = native 已摘除
            _goneBuffer.Clear();
            foreach (KeyValuePair<int, Tracked> pair in _tracked)
            {
                if (pair.Value.LastSeenTime < now)
                {
                    _goneBuffer.Add(pair.Key);
                }
            }
            for (int i = 0; i < _goneBuffer.Count; i++)
            {
                int index = _goneBuffer[i];
                Tracked tracked = _tracked[index];
                _tracked.Remove(index);
                LogGone(tracked, now);
                DestroyTracer(tracked);
                DestroyAltStandIn(tracked);
            }

            _lastReactionFresh = false;
        }

        /// <summary>
        /// 每帧维持弩类导弹速度倍率（<see cref="SpellMissileTraceState.SpeedModifier"/>）。
        /// 🔴 必须每帧刷：原版天气系统（`CustomBattleApplyWeatherEffectsModel`）会在场景加载/天气变化时
        /// 把它重置回 1（或雨天 0.9），只设一次会被它盖掉。
        /// </summary>
        private void ApplySpeedModifier()
        {
            if (Math.Abs(SpellMissileTraceState.SpeedModifier - 1f) < 0.001f)
            {
                return;
            }
            try
            {
                Mission.SetCrossbowMissileSpeedModifier(SpellMissileTraceState.SpeedModifier);
            }
            catch (Exception ex)
            {
                LogOnce("导弹速度倍率异常", ex);
            }
        }

        private void TryClaim(MissionMissile missile, float now)
        {
            if (missile == null)
            {
                return;
            }
            string ammoId = null;
            try
            {
                ammoId = missile.Weapon.Item?.StringId;
            }
            catch (Exception)
            {
                // Weapon 可能已失效 —— 忽略，不认领
            }
            bool matched = MatchAmmo(ammoId);
            bool playerShot = missile.ShooterAgent != null && missile.ShooterAgent == Mission.MainAgent;
            if (!matched && !playerShot)
            {
                return;
            }

            Vec3 pos = SafePosition(missile, Vec3.Zero);
            Agent shooter = missile.ShooterAgent;
            Tracked tracked = new Tracked
            {
                Index = missile.Index,
                BornTime = now,
                BornPos = pos,
                ShooterPos = shooter != null ? shooter.Position : pos,
                Shooter = shooter != null ? AgentLabel(shooter) : "?",
                AmmoId = ammoId ?? "?",
                LastSeenTime = now,
                LastPos = pos,
                LastLogTime = now,
                TotalDamage = 0f
            };
            _tracked.Add(missile.Index, tracked);

            DebugLogger.Log($"[SpellTrace] 认领: idx={missile.Index} 弹药={tracked.AmmoId} 射手={tracked.Shooter} "
                + $"位置={Pos(pos)} 判据={(matched ? "弹药匹配" : "玩家发射")} 刚体={SafeHasRigidBody(missile)}");
            LogMeshInventory(missile);
            AttachTracer(tracked, missile);
            SpawnAltStandIn(tracked, missile, pos);
        }

        // ─────────────────── 陪飞对照组（换个网格看会不会一样消失）───────────────────

        /// <summary>认领时按 <see cref="SpellMissileTraceState.AltMesh"/> 建一个陪飞实体（挂在真弹旁边）。</summary>
        private void SpawnAltStandIn(Tracked tracked, MissionMissile missile, Vec3 pos)
        {
            if (string.IsNullOrEmpty(SpellMissileTraceState.AltMesh))
            {
                return;
            }
            try
            {
                MetaMesh metaMesh = ResolveMetaMesh(SpellMissileTraceState.AltMesh);
                if (metaMesh == null)
                {
                    return;
                }
                GameEntity entity = GameEntity.CreateEmpty(Mission.Scene, true);
                if (entity == null)
                {
                    return;
                }
                entity.AddMultiMesh(metaMesh, true);
                entity.SetGlobalFrame(new MatrixFrame(ScaledIdentity(SpellMissileTraceState.AltScale), pos));
                tracked.AltEntity = entity;

                string particleNote = "拖尾=关";
                if (SpellMissileTraceState.AltParticleEnabled
                    && !string.IsNullOrEmpty(SpellMissileTraceState.AltParticle))
                {
                    int particleId = ResolveParticleId(SpellMissileTraceState.AltParticle);
                    if (particleId >= 0)
                    {
                        MatrixFrame localFrame = MatrixFrame.Identity;
                        ParticleSystem ps = ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, entity, ref localFrame);
                        if (ps != null)
                        {
                            tracked.AltParticlePs = ps;
                            particleNote = $"拖尾={SpellMissileTraceState.AltParticle}";
                        }
                    }
                    else
                    {
                        particleNote = $"拖尾={SpellMissileTraceState.AltParticle}(查不到)";
                    }
                }

                DebugLogger.Log($"[SpellTrace] 陪飞对照组已生成: 网格='{SpellMissileTraceState.AltMesh}' "
                    + $"偏移={SpellMissileTraceState.AltOffsetMeters:F1}m 放大={SpellMissileTraceState.AltScale:F1}x "
                    + $"名字读出={metaMesh.GetName()} "
                    + $"有任意LOD={metaMesh.HasAnyLods()} 有自动生成LOD={metaMesh.HasAnyGeneratedLods()} {particleNote}");
            }
            catch (Exception ex)
            {
                LogOnce("陪飞实体生成异常", ex);
            }
        }

        /// <summary>每帧把陪飞实体挪到真弹旁边（沿速度的横向偏移，免得两个重叠）。</summary>
        private void MoveAltStandIn(Tracked tracked, MissionMissile missile)
        {
            if (tracked.AltEntity == null)
            {
                return;
            }
            try
            {
                Vec3 pos = tracked.LastPos;
                Vec3 velocity;
                try
                {
                    velocity = missile.GetVelocity();
                }
                catch (Exception)
                {
                    velocity = Vec3.Zero;
                }
                Vec3 side = Vec3.CrossProduct(velocity, Vec3.Up);
                float sideLen = side.Length;
                if (sideLen > 0.01f)
                {
                    pos += side * (SpellMissileTraceState.AltOffsetMeters / sideLen);
                }
                tracked.AltEntity.SetGlobalFrame(new MatrixFrame(ScaledIdentity(SpellMissileTraceState.AltScale), pos));
            }
            catch (Exception ex)
            {
                LogOnce("陪飞实体移动异常", ex);
            }
        }

        /// <summary>单位旋转矩阵 × 倍数 —— 矩阵基向量的长度就是缩放（<c>Mat3</c> 的约定）。</summary>
        private static Mat3 ScaledIdentity(float scale)
        {
            if (Math.Abs(scale - 1f) < 0.001f)
            {
                return Mat3.Identity;
            }
            Mat3 m = Mat3.Identity;
            m.s = m.s * scale;
            m.f = m.f * scale;
            m.u = m.u * scale;
            return m;
        }

        private void DestroyAltStandIn(Tracked tracked)
        {
            if (tracked.AltEntity == null)
            {
                if (tracked.AltParticlePs == null)
                {
                    return;
                }
            }
            try
            {
                if (tracked.AltEntity != null)
                {
                    if (tracked.AltParticlePs != null)
                    {
                        tracked.AltEntity.RemoveComponent(tracked.AltParticlePs);
                    }
                    tracked.AltEntity.Remove(0);
                }
            }
            catch (Exception)
            {
                // 引擎可能已回收 —— 正常
            }
            tracked.AltEntity = null;
            tracked.AltParticlePs = null;
        }

        /// <summary>按名取网格：先 <c>GetMultiMesh</c>（已加载的资源），失败再 <c>GetCopy</c>（可现取）。</summary>
        private MetaMesh ResolveMetaMesh(string name)
        {
            try
            {
                MetaMesh metaMesh = MetaMesh.GetMultiMesh(name);
                if (metaMesh != null)
                {
                    return metaMesh;
                }
            }
            catch (Exception)
            {
                // 落到 GetCopy 再试
            }
            try
            {
                MetaMesh metaMesh = MetaMesh.GetCopy(name, showErrors: false, mayReturnNull: true);
                if (metaMesh != null)
                {
                    return metaMesh;
                }
            }
            catch (Exception ex)
            {
                LogOnce("陪飞网格解析异常 " + name, ex);
            }
            LogOnce("陪飞网格查不到 " + name,
                new InvalidOperationException("MetaMesh.GetMultiMesh/GetCopy 都返回空 —— 网格名不存在或该资产未加载"));
            return null;
        }

        /// <summary>
        /// A/B 实验：把这一发实体允许的 LOD 上限压到 0（不许因为距离远换粗模/剔除）。
        /// 🔴 **每帧都调**，不是只在认领时调一次 —— 这样可以在弹飞到一半时敲
        /// <c>custom.spell_trace lod0 on</c>，肉眼看着它"重新出现"，比打两发对比更有说服力
        /// （也排除"只是时机凑巧"）。逐实体生效，不碰共享的 MetaMesh 资产。
        /// </summary>
        private void ApplyEnforcedLod0(MissionMissile missile)
        {
            if (!SpellMissileTraceState.ForceLod0)
            {
                return;
            }
            try
            {
                GameEntity entity = missile.Entity;
                if (entity != null)
                {
                    entity.SetEnforcedMaximumLodLevel(0);
                }
            }
            catch (Exception ex)
            {
                LogOnce("强制LOD0异常", ex);
            }
        }

        /// <summary>
        /// 认领时把飞行网格的 LOD 家底打出来 —— 「飞远了 mesh 看不见是不是 LOD」的第一手证据。
        /// 判读：<c>有任意LOD=False</c> ⇒ 这个网格**只有 LOD0**（程序生成的网格典型）；
        ///       原版 LOD 距离表是 15/22.5/30/50/70/130/210 米，**超出最后一级就剔除**
        ///       （官方 docs：Asset Management/Asset Types/meshes.md「LOD System」）。
        /// </summary>
        private void LogMeshInventory(MissionMissile missile)
        {
            try
            {
                GameEntity entity = missile.Entity;
                if (entity == null)
                {
                    DebugLogger.Log("[SpellTrace] 网格家底: 实体=null");
                    return;
                }
                int count = entity.MultiMeshComponentCount;
                StringBuilder sb = new StringBuilder();
                sb.Append($"[SpellTrace] 网格家底: MetaMesh 数={count}");
                for (int i = 0; i < count; i++)
                {
                    MetaMesh metaMesh = entity.GetMetaMesh(i);
                    if (metaMesh == null)
                    {
                        sb.Append($" | #{i}=null");
                        continue;
                    }
                    string mask0 = "?";
                    string mask1 = "?";
                    try { mask0 = metaMesh.GetLodMaskForMeshAtIndex(0).ToString(); } catch (Exception) { }
                    try { mask1 = metaMesh.GetLodMaskForMeshAtIndex(1).ToString(); } catch (Exception) { }
                    sb.Append($" | #{i} 有任意LOD={metaMesh.HasAnyLods()} 有自动生成LOD={metaMesh.HasAnyGeneratedLods()} "
                        + $"lod0掩码={mask0} lod1掩码={mask1}");
                }
                DebugLogger.Log(sb.ToString());

                if (SpellMissileTraceState.ForceLod0)
                {
                    entity.SetEnforcedMaximumLodLevel(0);
                    DebugLogger.Log("[SpellTrace] 已对本实体强制 LOD0（SetEnforcedMaximumLodLevel(0)）"
                        + " —— A/B 实验；此后每帧维持（中途敲 lod0 on/off 会立即生效）");
                }
            }
            catch (Exception ex)
            {
                LogOnce("网格家底读取异常", ex);
            }
        }

        /// <summary>相机距离 + 引擎在这个距离上"打算用哪一档 LOD"（分辨率 = 剔除距离的探针）。</summary>
        private string LodInfo(MissionMissile missile, Vec3 pos)
        {
            try
            {
                Vec3 camera = Mission.GetCameraFrame().origin;
                float distSq = camera.DistanceSquared(pos);
                GameEntity entity = missile.Entity;
                float level = entity != null ? entity.GetLodLevelForDistanceSq(distSq) : -1f;
                return $"相机距={(float)System.Math.Sqrt(distSq):F1}m 该选LOD={level:F1}";
            }
            catch (Exception)
            {
                return "相机距=? 该选LOD=?";
            }
        }

        private void LogState(Tracked tracked, MissionMissile missile, float now)
        {
            Vec3 pos = tracked.LastPos;
            float flownFromMuzzle = tracked.BornPos.Distance(pos);
            float fromShooter = tracked.ShooterPos.Distance(pos);
            string entityInfo = EntityInfo(tracked, missile);
            DebugLogger.Log($"[SpellTrace] idx={tracked.Index} t={now - tracked.BornTime:F2}s "
                + $"速度={SafeSpeed(missile):F1} 已飞={flownFromMuzzle:F1}m 离射手={fromShooter:F1}m 高度={pos.z:F2} "
                + $"{LodInfo(missile, pos)} "
                + $"刚体={SafeHasRigidBody(missile)} 命中次数={tracked.HitCount} 累计伤害={tracked.TotalDamage:F0} {entityInfo}");
        }

        private void LogGone(Tracked tracked, float now)
        {
            float flightTime = tracked.LastSeenTime - tracked.BornTime;
            float flown = tracked.BornPos.Distance(tracked.LastPos);
            string reaction = _lastReactionFresh && _lastReaction != MissileReaction.Invalid
                ? $" 同帧结局={ReactionName(_lastReaction)}"
                : (_tracked.Count > 0 ? " 同帧结局=?（同时有多发在跟踪，无法归属）" : " 同帧结局=无（未收到 OnMissileCollisionReaction）");
            DebugLogger.Log($"🔴 [SpellTrace] idx={tracked.Index} 从导弹表消失: 最后位置={Pos(tracked.LastPos)} "
                + $"飞了={flown:F1}m 用时={flightTime:F2}s 命中次数={tracked.HitCount} 累计伤害={tracked.TotalDamage:F0}{reaction} "
                + "→ 判定：native 已摘除（**不是**渲染看不见）");
        }

        // ─────────────────────────── 命中 / 结局 / 实体 ───────────────────────────

        public override void OnMissileHit(Agent attacker, Agent victim, bool isCanceled, AttackCollisionData collisionData)
        {
            base.OnMissileHit(attacker, victim, isCanceled, collisionData);
            try
            {
                int index = collisionData.AffectorWeaponSlotOrMissileIndex;
                Tracked tracked;
                if (!_tracked.TryGetValue(index, out tracked))
                {
                    return;
                }
                tracked.HitCount++;
                // ⚠️ 没打到 agent 时（victim == null，打地形/物件）引擎给的 InflictedDamage 是
                //    int.MinValue（未初始化哨兵值），直接累加会把"累计伤害"带成 -21 亿。
                int damage = collisionData.InflictedDamage > 0 ? collisionData.InflictedDamage : 0;
                tracked.TotalDamage += damage;

                string target = victim != null
                    ? $"agent={AgentLabel(victim)} 部位={collisionData.VictimHitBodyPart}"
                    : "地形/物件（victim=null）";
                DebugLogger.Log($"[SpellTrace] idx={index} 命中: {target} 本次伤害={damage} "
                    + $"累计伤害={tracked.TotalDamage:F0} 弹总伤={collisionData.MissileTotalDamage:F1} "
                    + $"命中点={Pos(collisionData.CollisionGlobalPosition)} 物理材质号={collisionData.PhysicsMaterialIndex} "
                    + $"出界={collisionData.MissileGoneOutOfBorder} 落水={collisionData.MissileGoneUnderWater} "
                    + $"被取消={isCanceled} 命中刚体={collisionData.EntityExists}");
            }
            catch (Exception ex)
            {
                LogOnce("命中回调异常", ex);
            }
        }

        public override void OnMissileCollisionReaction(MissileReaction collisionReaction, Agent attackerAgent,
            Agent attachedAgent, sbyte attachedBoneIndex)
        {
            base.OnMissileCollisionReaction(collisionReaction, attackerAgent, attachedAgent, attachedBoneIndex);
            try
            {
                // 🔴 这个钩子对**全场每一发**导弹（含 NPC 的箭）都会派发 —— 没在跟踪任何目标时
                //    必须直接返回，否则战场上每支箭落地都会打一行日志（实测口径：几百发/场）。
                if (_tracked.Count == 0)
                {
                    return;
                }

                _lastReaction = collisionReaction;
                _lastReactionFresh = true;

                // ⚠️ 这个钩子不带导弹 index（见文件头 ②）→ 有多发在跟踪时无法归属，日志注明。
                string owner = _tracked.Count == 1
                    ? $"idx={FirstTrackedIndex()}"
                    : $"idx=?（当前跟踪 {_tracked.Count} 发，无法归属）";
                DebugLogger.Log($"[SpellTrace] {owner} 结局: {ReactionName(collisionReaction)} "
                    + $"附着到={(attachedAgent != null ? AgentLabel(attachedAgent) : "无")} 骨={attachedBoneIndex} "
                    + $"攻击者={(attackerAgent != null ? AgentLabel(attackerAgent) : "无")}"
                    + (collisionReaction == MissileReaction.BecomeInvisible ? " ← 凭空消失（Entity.Remove）" : ""));
            }
            catch (Exception ex)
            {
                LogOnce("结局回调异常", ex);
            }
        }

        public override void OnEntityRemoved(GameEntity entity)
        {
            base.OnEntityRemoved(entity);
            if (entity == null)
            {
                return;
            }
            try
            {
                foreach (KeyValuePair<int, Tracked> pair in _tracked)
                {
                    Tracked tracked = pair.Value;
                    if (tracked.TracerHost != null && ReferenceEquals(tracked.TracerHost, entity))
                    {
                        tracked.EntityRemoved = true;
                        DebugLogger.Log($"[SpellTrace] idx={tracked.Index} 实体被引擎移除（OnEntityRemoved）"
                            + $" —— BecomeInvisible / 掉落物转换都会走这里");
                        tracked.TracerHost = null;
                        tracked.Tracer = null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce("实体移除回调异常", ex);
            }
        }

        public override void OnEndMissionInternal()
        {
            base.OnEndMissionInternal();
            StopAll("mission ended");
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            try
            {
                // 🔴 必须"先减后加"：这个静态委托游戏自己的 View 模块用 `=` 赋值
                //    （ViewSubModule.cs:290），直接 `=` 会把它的顶掉。先减后加 = 幂等、不重复订阅。
                MissionWeapon.OnGetWeaponDataHandler -= OnGetWeaponData;
                MissionWeapon.OnGetWeaponDataHandler += OnGetWeaponData;
            }
            catch (Exception ex)
            {
                LogOnce("武器数据钩子安装异常", ex);
            }
        }

        /// <summary>逐发改武器数据：把法术弹的 <c>ScaleFactor</c> 换成实验倍率（其余武器一律不碰）。</summary>
        private void OnGetWeaponData(ref WeaponData weaponData, MissionWeapon weapon, bool isFemale, Banner banner,
            bool needBatchedVersion)
        {
            if (Math.Abs(SpellMissileTraceState.ItemScale - 1f) < 0.001f)
            {
                return;
            }
            try
            {
                ItemObject item = weapon.Item;
                if (item != null && string.Equals(item.StringId, SpellMissileTraceState.AmmoFilter, StringComparison.Ordinal))
                {
                    weaponData.ScaleFactor = SpellMissileTraceState.ItemScale;
                }
            }
            catch (Exception)
            {
                // 这个回调在渲染/UI 路径上也会被调 —— 绝不能抛
            }
        }

        public override void OnRemoveBehavior()
        {
            try
            {
                MissionWeapon.OnGetWeaponDataHandler -= OnGetWeaponData;
            }
            catch (Exception)
            {
                // 卸载期异常忽略
            }
            base.OnRemoveBehavior();
            StopAll("behavior removed");
        }

        // ─────────────────────────── 示踪 ───────────────────────────

        /// <summary>把示踪粒子**挂到导弹自己的实体上** —— 引擎会带着它走（原版 trail 就是这么做的）。</summary>
        private void AttachTracer(Tracked tracked, MissionMissile missile)
        {
            if (!SpellMissileTraceState.TracerEnabled || _tracerNameBroken)
            {
                return;
            }
            try
            {
                GameEntity host = missile.Entity;
                if (host == null)
                {
                    return;
                }
                int particleId = ResolveParticleId(SpellMissileTraceState.TracerParticle);
                if (particleId < 0)
                {
                    _tracerNameBroken = true;
                    return;
                }
                MatrixFrame localFrame = MatrixFrame.Identity;
                ParticleSystem ps = ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, host, ref localFrame);
                if (ps == null)
                {
                    return;
                }
                tracked.TracerHost = host;
                tracked.Tracer = ps;
            }
            catch (Exception ex)
            {
                _tracerNameBroken = true;
                LogOnce("示踪粒子挂载异常（后续不再尝试）", ex);
            }
        }

        private void DestroyTracer(Tracked tracked)
        {
            if (tracked.Tracer == null)
            {
                return;
            }
            try
            {
                // 弹已消失时实体多半已被引擎回收 → 这一句会失败，忽略即可；
                // 弹还活着（关掉开关的情况）时才真的需要摘掉，否则火线会一直挂着。
                if (tracked.TracerHost != null && !tracked.EntityRemoved)
                {
                    tracked.TracerHost.RemoveComponent(tracked.Tracer);
                }
            }
            catch (Exception)
            {
                // 引擎已回收 —— 正常
            }
            tracked.Tracer = null;
            tracked.TracerHost = null;
        }

        private void StopAll(string reason)
        {
            foreach (KeyValuePair<int, Tracked> pair in _tracked)
            {
                DestroyTracer(pair.Value);
                DestroyAltStandIn(pair.Value);
            }
            if (_tracked.Count > 0)
            {
                DebugLogger.Log($"[SpellTrace] 停止跟踪（{reason}），清掉 {_tracked.Count} 发记录");
            }
            _tracked.Clear();
        }

        private int ResolveParticleId(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }
            try
            {
                int id = ParticleSystemManager.GetRuntimeIdByName(name);
                if (id < 0)
                {
                    DebugLogger.Log($"[SpellTrace] 示踪粒子 '{name}' 查不到 —— 只出日志、不画火线"
                        + "（换一个：custom.spell_trace particle <name>）");
                }
                return id;
            }
            catch (Exception ex)
            {
                LogOnce("粒子名解析异常 " + name, ex);
                return -1;
            }
        }

        // ─────────────────────────── 小工具 ───────────────────────────

        private static bool MatchAmmo(string ammoId)
        {
            return !string.IsNullOrEmpty(ammoId)
                && string.Equals(ammoId, SpellMissileTraceState.AmmoFilter, StringComparison.Ordinal);
        }

        private Vec3 SafePosition(MissionMissile missile, Vec3 fallback)
        {
            try
            {
                return missile.GetPosition();
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private float SafeSpeed(MissionMissile missile)
        {
            try
            {
                return missile.GetVelocity().Length;
            }
            catch (Exception)
            {
                return -1f;
            }
        }

        private bool SafeHasRigidBody(MissionMissile missile)
        {
            try
            {
                return missile.GetHasRigidBody();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>实体侧状态：还在不在（推送式判据）、有没有网格、可见性标志位（判"渲染看不见"用）。</summary>
        private string EntityInfo(Tracked tracked, MissionMissile missile)
        {
            if (tracked.EntityRemoved)
            {
                return "实体=已被引擎移除";
            }
            try
            {
                GameEntity entity = missile.Entity;
                if (entity == null)
                {
                    return "实体=null";
                }
                return $"实体=在 网格数={entity.MultiMeshComponentCount} 可见性标志={entity.EntityVisibilityFlags}"
                    + $" 包围盒={Pos(entity.GlobalBoxMin)}~{Pos(entity.GlobalBoxMax)}";
            }
            catch (Exception)
            {
                return "实体=?（读取异常）";
            }
        }

        private int FirstTrackedIndex()
        {
            foreach (KeyValuePair<int, Tracked> pair in _tracked)
            {
                return pair.Key;
            }
            return -1;
        }

        private static string ReactionName(MissileReaction reaction)
        {
            switch (reaction)
            {
                case MissileReaction.Stick: return "Stick(插住→掉落物)";
                case MissileReaction.BounceBack: return "BounceBack(弹开→掉落物)";
                case MissileReaction.PassThrough: return "PassThrough(穿过去)";
                case MissileReaction.BecomeInvisible: return "BecomeInvisible(凭空消失)";
                default: return reaction.ToString();
            }
        }

        private static string AgentLabel(Agent agent)
        {
            if (agent == null)
            {
                return "null";
            }
            string name = null;
            try
            {
                name = agent.Name;
            }
            catch (Exception)
            {
                // 名字读不到就算了
            }
            return $"{name ?? "?"}(idx={agent.Index}{(agent.IsPlayerControlled ? ",玩家" : "")})";
        }

        private static string Pos(Vec3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F1},{1:F1},{2:F1})", v.x, v.y, v.z);
        }

        private void LogOnce(string key, Exception ex)
        {
            if (!_loggedOnce.Add(key))
            {
                return;
            }
            DebugLogger.Log($"[SpellTrace] {key}：{ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>控制台命令（返回文本纯英文；诊断详情走 DebugLogger 中文）。</summary>
    public static class SpellMissileTraceCommands
    {
        /* 🔴 控制台命令注册纪律：委托签名 = public static string F(List<string>)，
           签名/可见性不符 = 启动时绑定失败 ArgumentException。
           🔴 首参一律"可弃"：解析不出（如 custom.spell_trace 1）→ 回落到"切换开关"并注明，
           禁止直接报错（工作流约定，2026-09-14）。 */
        [CommandLineFunctionality.CommandLineArgumentFunction("spell_trace", "custom")]
        public static string SpellTrace(List<string> args)
        {
            try
            {
                if (args == null || args.Count == 0)
                {
                    SpellMissileTraceState.Enabled = !SpellMissileTraceState.Enabled;
                    return Status(SpellMissileTraceState.Enabled ? "trace on" : "trace off");
                }

                switch (args[0].ToLowerInvariant())
                {
                    case "on":
                        SpellMissileTraceState.Enabled = true;
                        return Status("trace on");
                    case "off":
                        SpellMissileTraceState.Enabled = false;
                        return Status("trace off");
                    case "status":
                        return Status("status");
                    case "ammo":
                        if (args.Count < 2 || string.IsNullOrEmpty(args[1]))
                        {
                            return "usage: spell_trace ammo <item_id>";
                        }
                        SpellMissileTraceState.AmmoFilter = args[1];
                        return Status("ammo set" + EnsureEnabledNote());
                    case "tracer":
                        if (args.Count < 2)
                        {
                            return "usage: spell_trace tracer on|off";
                        }
                        SpellMissileTraceState.TracerEnabled = string.Equals(args[1], "on", StringComparison.OrdinalIgnoreCase);
                        return Status("tracer set" + EnsureEnabledNote());
                    case "particle":
                        if (args.Count < 2 || string.IsNullOrEmpty(args[1]))
                        {
                            return "usage: spell_trace particle <particle_name>";
                        }
                        SpellMissileTraceState.TracerParticle = args[1];
                        return Status("particle set" + EnsureEnabledNote());
                    case "interval":
                        float seconds;
                        if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out seconds))
                        {
                            return "usage: spell_trace interval <seconds>";
                        }
                        SpellMissileTraceState.LogInterval = Math.Max(0.02f, Math.Min(5f, seconds));
                        return Status("interval set" + EnsureEnabledNote());
                    case "lod0":
                        if (args.Count < 2)
                        {
                            return "usage: spell_trace lod0 on|off";
                        }
                        SpellMissileTraceState.ForceLod0 = string.Equals(args[1], "on", StringComparison.OrdinalIgnoreCase);
                        return Status("lod0 set" + EnsureEnabledNote());
                    case "alt_mesh":
                        if (args.Count < 2 || string.IsNullOrEmpty(args[1]))
                        {
                            return "usage: spell_trace alt_mesh <mesh_name|off>";
                        }
                        SpellMissileTraceState.AltMesh =
                            string.Equals(args[1], "off", StringComparison.OrdinalIgnoreCase) ? "" : args[1];
                        return Status("alt_mesh set" + EnsureEnabledNote());
                    case "alt_offset":
                        float offset;
                        if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out offset))
                        {
                            return "usage: spell_trace alt_offset <meters>";
                        }
                        SpellMissileTraceState.AltOffsetMeters = Math.Max(0f, Math.Min(30f, offset));
                        return Status("alt_offset set" + EnsureEnabledNote());
                    case "alt_scale":
                        float altScale;
                        if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out altScale))
                        {
                            return "usage: spell_trace alt_scale <multiplier>  (e.g. 10 = ten times bigger)";
                        }
                        SpellMissileTraceState.AltScale = Math.Max(0.05f, Math.Min(50f, altScale));
                        return Status("alt_scale set" + EnsureEnabledNote());
                    case "alt_particle":
                        if (args.Count < 2 || string.IsNullOrEmpty(args[1]))
                        {
                            return "usage: spell_trace alt_particle <particle_name|off>";
                        }
                        if (string.Equals(args[1], "off", StringComparison.OrdinalIgnoreCase))
                        {
                            SpellMissileTraceState.AltParticleEnabled = false;
                        }
                        else
                        {
                            SpellMissileTraceState.AltParticleEnabled = true;
                            SpellMissileTraceState.AltParticle = args[1];
                        }
                        return Status("alt_particle set" + EnsureEnabledNote());
                    case "speed":
                        float multiplier;
                        if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out multiplier))
                        {
                            return "usage: spell_trace speed <multiplier>  (e.g. 0.5 = half speed, 1 = vanilla)";
                        }
                        SpellMissileTraceState.SpeedModifier = Math.Max(0.05f, Math.Min(5f, multiplier));
                        return Status("speed set" + EnsureEnabledNote());
                    case "scale":
                        float scale;
                        if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out scale))
                        {
                            return "usage: spell_trace scale <multiplier>  (e.g. 5 = five times bigger, 1 = vanilla)";
                        }
                        SpellMissileTraceState.ItemScale = Math.Max(0.05f, Math.Min(20f, scale));
                        return Status("scale set" + EnsureEnabledNote());
                    default:
                        // 可弃占位（如 `spell_trace 1`）→ 当作"切换开关"，不报错
                        SpellMissileTraceState.Enabled = !SpellMissileTraceState.Enabled;
                        return Status($"trace {(SpellMissileTraceState.Enabled ? "on" : "off")}"
                            + $" [note: '{args[0]}' is not a subcommand -> toggled instead]");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SpellTrace] spell_trace 异常: {ex}");
                return "spell_trace_failed: " + ex.Message;
            }
        }

        /// <summary>
        /// 设置类子命令一律"顺手把总开关打开"。
        /// 🔴 2026-09-22 实机踩到的坑：总开关是**静态字段、重启游戏即复位成关**，
        /// 用户只敲 <c>custom.spell_trace lod0 on</c> 就发射 → 整件没跑（日志里只有一行 lod0 set，
        /// 连"发射/认领"都没有），白打一趟。返回文本里显式注明，免得再误判成"LOD0 没用"。
        /// </summary>
        private static string EnsureEnabledNote()
        {
            if (SpellMissileTraceState.Enabled)
            {
                return "";
            }
            SpellMissileTraceState.Enabled = true;
            DebugLogger.Log("[SpellTrace] 总开关原本是关的 → 已自动打开（设置类子命令一律带上总开关；"
                + "总开关不跨游戏会话保留）");
            return " [auto: trace was OFF -> turned ON]";
        }

        private static string Status(string headline)
        {
            DebugLogger.Log($"[SpellTrace] {headline}｜开关={SpellMissileTraceState.Enabled} "
                + $"示踪={SpellMissileTraceState.TracerEnabled}({SpellMissileTraceState.TracerParticle}) "
                + $"弹药过滤={SpellMissileTraceState.AmmoFilter} 间隔={SpellMissileTraceState.LogInterval:F2}s "
                + $"强制LOD0={SpellMissileTraceState.ForceLod0} 陪飞网格={(string.IsNullOrEmpty(SpellMissileTraceState.AltMesh) ? "关" : SpellMissileTraceState.AltMesh)}"
                + $" 陪飞拖尾={(SpellMissileTraceState.AltParticleEnabled ? SpellMissileTraceState.AltParticle : "关")}"
                + $" 弩速倍率={SpellMissileTraceState.SpeedModifier:F2} 弹体放大={SpellMissileTraceState.ItemScale:F2}x");
            return $"spell_trace {headline} enabled={SpellMissileTraceState.Enabled} "
                + $"tracer={SpellMissileTraceState.TracerEnabled} particle={SpellMissileTraceState.TracerParticle} "
                + $"ammo={SpellMissileTraceState.AmmoFilter} interval={SpellMissileTraceState.LogInterval:F2} "
                + $"lod0={SpellMissileTraceState.ForceLod0} "
                + $"alt_mesh={(string.IsNullOrEmpty(SpellMissileTraceState.AltMesh) ? "off" : SpellMissileTraceState.AltMesh)} "
                + $"alt_offset={SpellMissileTraceState.AltOffsetMeters:F1} "
                + $"alt_scale={SpellMissileTraceState.AltScale:F1}";
        }
    }
}
