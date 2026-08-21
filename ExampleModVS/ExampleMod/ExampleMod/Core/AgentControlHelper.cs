using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using static TaleWorlds.Library.VirtualFolders.Win64_Shipping_Client;
using static TaleWorlds.MountAndBlade.Agent;

namespace LivingWorldNpcs
{
    public static class AgentControlHelper
    {
        /// <summary>
        /// 🔴 2026-08-15（目标唯一标记方案，用户裁定）：从目标文本解析 `[#N]` index 标记——LLM 基于场景
        /// 语义指认目标（「酒馆老板」→ 场景里标着 [#3] 的「酒馆店主」），C# 用 Agent.Index 精确查 Agent，
        /// 不依赖脆弱的字符串匹配。
        /// 边界（用户裁定 2026-08-15）：仅 InScene 有意义——非场景（Mission 为 null）直接未命中；
        /// 解析失败/agent 失效 → cleanName 输出剥离后的纯名字，调用方回退名字匹配（归一化兜底）。
        /// </summary>
        /// <param name="text">目标文本（可能含 "#N" 后缀，如 "酒馆店主#3"）。</param>
        /// <param name="agent">命中：Mission 内 Index == N 的活跃 Agent；未命中：null。</param>
        /// <param name="cleanName">剥离 "#N" 后的纯名字（未命中时供名字匹配回退）。</param>
        public static bool TryResolveIndexedTarget(string text, out Agent agent, out string cleanName)
        {
            agent = null;
            cleanName = text;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int hash = text.LastIndexOf('#');
            if (hash < 0 || hash == text.Length - 1) return false;
            string tail = text.Substring(hash + 1).Trim();
            // 🔴 2026-08-19（统一标记格式：GetDisplayName 的「名字#Index」无空格，全 Mod 同构）：
            // 候选/选项文本可带方位尾巴（如 帝国步兵#42（你西侧47米））——数字前缀后允许
            // 标点/空白/括号等分隔符，但**字母/CJK 文字尾随 = 不是 index 标记**（防「#47号房间」
            // 这类把房间号误解析成 Agent.Index）。
            int end = 0;
            while (end < tail.Length && char.IsDigit(tail[end])) end++;
            if (end == 0) return false;
            if (end < tail.Length)
            {
                char c = tail[end];
                if (char.IsLetter(c) || (c >= 0x3000 && c <= 0x9FFF)) return false;
            }
            if (!int.TryParse(tail.Substring(0, end), out int idx)) return false;
            cleanName = text.Substring(0, hash).Trim();
            // 仅 InScene 有意义（用户裁定 2026-08-15）；不在场对象没有 index
            if (Mission.Current == null) return false;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive()) continue;
                    if (a.Index == idx) { agent = a; return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 🔴 2026-08-21（M4 风险评估 + 铁律 18 共享管线）：目标背后可站立点探测——
        /// 四候选逐级（正后 2.2m → 后左 45° 2.5m → 后右 45° 2.5m → 正后 3.5m），返回首个
        /// V.NavMesh 可站立点。true = 存在可站立点（spot = 第一个）；false = 四候选均不可站
        ///（spot = 未验证的默认正后 2.2m，仅作调用方兜底参考）。
        /// 源实现迁自 Planner/InlineSteps.cs StealAttemptInlineState Behind 阶段——
        /// 判定（TargetRiskEvaluator 风险评估）与结算（绕后执行）共享同一探测，禁止复制逻辑。
        /// 主线程调用（引擎 Scene 只读主线程）。
        /// </summary>
        public static bool TryFindBehindSpot(Agent target, out Vec3 spot)
        {
            spot = Vec3.Zero;
            if (target == null) return false;
            try
            {
                Vec3 look = new Vec3(target.LookDirection.X, target.LookDirection.Y, 0f);
                Vec3 back = -look;
                back.z = 0f;
                if (back.LengthSquared < 0.0001f) back = new Vec3(1f, 0f, 0f);
                back = back.NormalizedCopy();
                Vec3 targetPos = target.Position;
                spot = targetPos + back * 2.2f;   // 兜底默认：正后方（不验证；调用方 8s 超时诚实报告）
                var scene = Mission.Current?.Scene;
                var candidates = new[]
                {
                    (back, 2.2f),
                    (RotateDir(back, 45f), 2.5f),
                    (RotateDir(back, -45f), 2.5f),
                    (back, 3.5f),
                };
                foreach (var (dir, d) in candidates)
                {
                    Vec3 p = targetPos + dir * d;
                    if (scene != null && !V.NavMesh(scene, p, out _)) continue;
                    spot = p;
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>2D 平面旋转（绕 Z 轴，角度制）——绕后候选点偏转（后左/后右 45°）用。
        /// 自 Planner/InlineSteps.cs 迁入共享（铁律 18）。</summary>
        public static Vec3 RotateDir(Vec3 dir, float degrees)
        {
            float rad = MathF.PI * degrees / 180f;
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            return new Vec3(dir.x * c - dir.y * s, dir.x * s + dir.y * c, 0f);
        }

        public static void SetPose(Agent agent, string actionId)
        {
            if (agent == null || string.IsNullOrEmpty(actionId))
                return;
            if (GetPose(agent) == actionId)
                return;
            ActionIndexCache actionCache = ActionIndexCache.Create(actionId);
            if (actionCache != ActionIndexCache.act_none)
            {
                agent.SetActionChannel(0, actionCache, ignorePriority: false, blendInPeriod: 0.2f);
            }
        }

        /// <summary>
        /// 强制在任何 Agent 上播放指定动作，无视其当前 action_set。
        /// 原理：临时切换到 as_human_warrior（所有人类动作的根 action_set），
        /// 播完动画后恢复原始 action_set。
        ///
        /// 用于村民/平民等非战斗 NPC 播放战斗动作（如击倒、死亡倒地等）。
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        public static void ForcePlayAction(Agent agent, string actionId, bool restoreAfter = false)
        {
            if (agent == null || string.IsNullOrEmpty(actionId) || !agent.IsActive())
                return;

            ActionIndexCache actionCache = ActionIndexCache.Create(actionId);
            if (actionCache == ActionIndexCache.act_none) return;

            // 提前取 agentName，供日志和 catch 块使用（避免在 native 崩溃后还访问 agent.Name）
            string agentName = "?";
            try { agentName = agent.Name?.ToString() ?? "?"; }
            catch { agentName = "<error>"; }

            try
            {
                // 0. 打断任何进行中的交互（坐椅子、跟人对话等），
                //    否则引擎每帧会覆盖我们的动画
                bool wasUsingObj = agent.IsUsingGameObject;
                var scriptedFlags = agent.GetScriptedFlags();
                if (wasUsingObj)
                {
                    agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
                }

                // 获取当前 action_set 信息用于日志（防御性：native 属性可能抛异常）
                MBActionSet originalSet = agent.ActionSet;
                string originalSetName = "?";
                try { originalSetName = originalSet.IsValid ? originalSet.GetName() : "<invalid>"; }
                catch { originalSetName = "<error>"; }

                DebugLogger.Log($"[ForcePlayAction] {agentName} '{actionId}' UsingObj={wasUsingObj} flags={scriptedFlags} action_set:'{originalSetName}'→'as_human_warrior'");

                // 1. 获取战士 action_set（所有人类动作的根）
                MBActionSet warriorSet = MBActionSet.GetActionSet("as_human_warrior");
                if (!warriorSet.IsValid) return;

                // 如果 agent 已经是 warrior action_set，跳过 SetActionSet 以避免
                // 不必要的 native AnimationSystemData 替换（可能触发异步 AI tick 竞态 → AccessViolation）
                bool alreadyWarrior = originalSetName == "as_human_warrior";

                if (!alreadyWarrior)
                {
                    // 2. 构造临时 AnimationSystemData
                    AnimationSystemData warriorData = agent.Monster.FillAnimationSystemData(
                        warriorSet, agent.Character.GetStepSize(), hasClippingPlane: false);

                    // 3. 切到战士 action_set
                    agent.SetActionSet(ref warriorData);
                }

                // 4. 播放动画
                agent.SetActionChannel(0, actionCache, ignorePriority: true, blendInPeriod: 0.15f);

                // 5. 恢复原始 action_set（如有需要；alreadyWarrior 时无需恢复）
                if (restoreAfter && originalSet.IsValid && !alreadyWarrior)
                {
                    AnimationSystemData originalData = agent.Monster.FillAnimationSystemData(
                        originalSet, agent.Character.GetStepSize(), hasClippingPlane: false);
                    // 延迟一帧恢复，等动画开始播放
                    _ = RestoreActionSetAsync(agent, originalData);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ForcePlayAction] Error playing '{actionId}' on {agentName}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async Task RestoreActionSetAsync(Agent agent, AnimationSystemData data)
        {
            await Task.Delay(100); // 等动画开始播放
            if (agent != null && agent.IsActive())
            {
                agent.SetActionSet(ref data);
            }
        }
        public static string GetPose(Agent agent)
        {
            if (agent == null) return "";
            return V.ActName(agent, 0);
        }
        public static bool IsPlayingPose(Agent agent,string actionId)
        {
            if (agent == null || string.IsNullOrEmpty(actionId))
                return false;
            return GetPose(agent) == actionId;
        }

        // 【修改后】只负责发号施令，不负责等待

       
        public static void ScriptedMoveToPoint(Agent agent, Vec3 targetVec, bool isRun = false,bool hasNav = false)
        {
            if (agent == null || !agent.IsActive()) return;

            // 1. 清理状态 (原 MoveTo 的前置逻辑)
            if (agent.IsUsingGameObject)
            {
                agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
            }
            agent.ClearTargetFrame();
            // 清注视锁：跟随停驻态 SetLookAgent(目标) 不会随移动自动解除——
            // 计划接管后不移除会导致"身体走向目标、头锁着玩家"的倒着走路（实机 badcase）。
            agent.SetLookAgent(null);
            V.SetAgentAI(agent);
            // 恢复速度上限：MoveEndAndInteractPrepare 会 SetMaximumSpeedLimit(0) 原地钉死 Agent
            // （对话结束/到达站定路径），若不解除，后续所有移动指令都会被钳到速度 0 = 原地不动。
            // 该钳制是独立 native 状态，不随 SetScriptedPosition 重置——必须在此显式恢复（-1 = 默认）。
            agent.SetMaximumSpeedLimit(-1f, false);

            // 2. 修正导航网格
            WorldPosition targetPos = new WorldPosition(agent.Mission.Scene, UIntPtr.Zero, targetVec, false);
            if (!hasNav)
            {
                if (targetPos.GetNavMesh() == UIntPtr.Zero)
                {
                V.NavMeshSnap(agent.Mission.Scene, ref targetVec);
                    targetPos = new WorldPosition(agent.Mission.Scene, targetVec);
                }
            }
            // 3. 设置移动参数
            var moveFlags = Agent.AIScriptedFrameFlags.GoToPosition |
                            Agent.AIScriptedFrameFlags.NoAttack |
                            Agent.AIScriptedFrameFlags.NeverSlowDown;

            if (!isRun)
                moveFlags |= Agent.AIScriptedFrameFlags.DoNotRun;
            // 4. 下达指令 (只执行一次)
            agent.SetScriptedPosition(ref targetPos, false, moveFlags);
        }
        public static void ScriptedMoveToAgent(Agent agent, Agent targetAgent, bool isRun)
        {
            if (agent == null || targetAgent == null) return;

            // 复用上面的逻辑，只是目标点变成了动态获取
            // 注意：这里不需要每帧做 NavMesh 修正，因为 GetWorldPosition 通常是合法的，
            // 但如果目标跳崖了，这里可能需要额外判断，暂时保持简单。
            ScriptedMoveToPoint(agent, targetAgent.Position, isRun);
        }

        /// <summary>
        /// 🔴 2026-08-13（用户裁定：通用接近语义）：接近目标——距离 &gt; walkRadius 跑、≤ walkRadius 走。
        /// 近身放慢脚步（偷袭/搭话接近的通用观感：长距离小跑赶路，到位前收势），击晕/偷窃等接近型
        /// 动作统一走本方法，禁止各自硬编码走/跑（实机踩坑：全程走速 → 50 米目标撞 30s 超时中止）。
        /// </summary>
        public static void ApproachAgent(Agent agent, Agent target, float walkRadius = 5f)
        {
            if (agent == null || target == null || !agent.IsActive() || !target.IsActive()) return;
            float dist = agent.Position.Distance(target.Position);
            ScriptedMoveToPoint(agent, target.Position, dist > walkRadius);
        }

        private static string GetAmmoIdForWeaponClass(TaleWorlds.Core.WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case TaleWorlds.Core.WeaponClass.Bow:
                    return "sho_practice_arrow";
                case TaleWorlds.Core.WeaponClass.Crossbow:
                    return "teppo_ammo";
                case TaleWorlds.Core.WeaponClass.Musket:
                case TaleWorlds.Core.WeaponClass.Pistol:
                    return "teppo_ammo";
                default:
                    return null;
            }
        }
        public static string GiveWeaponToAgent(Agent agent, string itemId)
        {
            var itemObject = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(itemId);
            if (itemObject == null)
            {
                return $"can not find Item :{itemId}";
            }
            // === 新增：先把熟练度拉满，防止 AI 判定自己太菜只肯肉搏 ===
            if (agent.Character != null)
            {

                //    agent.SetSkillValue(TaleWorlds.Core.DefaultSkills.Crossbow, 300); // 火枪通常吃 Crossbow 技能
            }
            var missionWeapon = new TaleWorlds.MountAndBlade.MissionWeapon(itemObject, null, agent.Origin?.Banner);
            agent.EquipWeaponWithNewEntity(TaleWorlds.Core.EquipmentIndex.Weapon0, ref missionWeapon);
            //检查是否需要弹药
            if (itemObject.PrimaryWeapon != null)
            {
                string ammoId = GetAmmoIdForWeaponClass(itemObject.PrimaryWeapon.WeaponClass);
                if (!string.IsNullOrEmpty(ammoId))
                {
                    var ammoObject = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(ammoId);
                    if (ammoObject != null)
                    {
                        var ammoWeapon = new TaleWorlds.MountAndBlade.MissionWeapon(ammoObject, null, agent.Origin?.Banner);
                        agent.EquipWeaponWithNewEntity(TaleWorlds.Core.EquipmentIndex.Weapon1, ref ammoWeapon);
                    }
                }
            }
            agent.UpdateAgentStats();

            return "";
        }

        /// <summary>
        /// 人类或儿童（human_child）。引擎把 human_child 排除在 IsHuman 外（无 IsHumanoid 标志，
        /// 非战斗人员设定），但玩家认知里小孩也是人：对话/警戒/感知/战斗事件一律与大人同等对待。
        /// 凡原本判定 <c>agent.IsHuman</c> 且语义为「人形角色」的地方，统一改用它。
        /// </summary>
        public static bool IsHumanOrChild(Agent agent)
        {
            if (agent == null) return false;
            if (agent.IsHuman) return true;
            return agent.Monster != null && agent.Monster.StringId?.Contains("child") == true;
        }

        /// <summary>
        /// 动态查找并发放一把近战武器。先试预设 ID，找不到则遍历内存中已注册的所有 ItemObject，
        /// 取第一把符合条件的单手/双手近战（排除盾牌、远程、弹药、投掷）。
        /// 适配任意 mod 组合（织丰/Shokuho 等屏蔽原版武器后也能工作）。
        /// </summary>
        /// <returns>成功发放返回 true，内存中完全无近战武器返回 false</returns>
        public static bool TryGiveAnyMeleeWeapon(Agent agent)
        {
            // 第一轮：尝试预设的常用村民武器（从 XML 核实过的 ID）
            string[] preferredIds = { "peasant_hatchet_1_t1", "peasant_pickaxe_1_t1", "peasant_sickle_1_t1" };
            foreach (string id in preferredIds)
            {
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(id);
                if (item != null && item.PrimaryWeapon != null && item.PrimaryWeapon.IsMeleeWeapon)
                {
                    GiveWeaponToAgent(agent, id);
                    return true;
                }
            }

            // 第二轮：内存动态搜索 — 遍历已注册 ItemObject，找任意一把近战武器
            var fallback = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(item =>
                item.PrimaryWeapon != null
                && item.PrimaryWeapon.IsMeleeWeapon
                && !item.PrimaryWeapon.IsShield);

            if (fallback != null)
            {
                GiveWeaponToAgent(agent, fallback.StringId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取 Agent 当前手持武器的物品名（优先主手 → 副手）。
        /// 未持武器或取不到时返回 null，调用方自行兜底。
        /// </summary>
        public static string GetWieldedWeaponName(Agent agent)
        {
            if (agent == null) return null;
            try
            {
                EquipmentIndex mainIdx = V.MainWpn(agent);
                if (mainIdx != EquipmentIndex.None)
                {
                    var eq = agent.SpawnEquipment[mainIdx];
                    if (!eq.IsEmpty && eq.Item != null)
                        return eq.Item.Name?.ToString();
                }

                EquipmentIndex offIdx = V.OffWpn(agent);
                if (offIdx != EquipmentIndex.None)
                {
                    var eq = agent.SpawnEquipment[offIdx];
                    if (!eq.IsEmpty && eq.Item != null)
                        return eq.Item.Name?.ToString();
                }
            }
            catch { return null; }
            return null;
        }


        public static async Task MoveToActor(Agent npcAgent, Agent actor, float stopDistance = 0.5f)
        {
            if (npcAgent == null || !npcAgent.IsActive() || actor == null) return;
            Mat3 playerRot = actor.LookFrame.rotation;
            //npcAgent的目标rotation需要和actor的rot相反

          //  Vec3 direction = npcAgent.Position - turnAgent.Position;

            Vec2 targetDir = (-playerRot.f).AsVec2;

            npcAgent.SetLookAgent(actor); // 走路时盯着actor
            await MoveTo(npcAgent, actor.Position + (playerRot.f * 2.0f), targetDir, stopDistance);
            npcAgent.SetLookAgent(actor); // 到终点仍盯着actor
        }

        public static void StopAndReset(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return;
            // agent.ClearTargetFrame();
            ForceUnlockAgent(agent);
            // 可以根据需要决定是否要重置 Flags
            //agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            // agent.SetScriptedFlags(agent.GetScriptedFlags() & ~Agent.AIScriptedFrameFlags.DoNotRun);
        }
        public static void ForceUnlockAgent(Agent agent)
        {
            if (agent == null) return;
            // 🔴 2026-08-21（实机）：Agent.IsActive() 走 native 指针（State → GetAgentState(_statePointer)），
            // agent 已移除/场景结束时指针释放——对象非 null 但调用即抛 NRE。共享管线入口必须永不抛。
            try
            {
                if (!agent.IsActive()) return;
            }
            catch { return; }

            // 1. 禁用之前 MoveTo 设置的脚本化移动 (解除 SetScriptedPosition 的锁定)
            agent.DisableScriptedMovement();
            // 2. 解除速度限制 (-1f 表示恢复默认最大速度)
            agent.SetMaximumSpeedLimit(-1f, false);
            agent.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);

            // 3. 清除强制盯人 (解除 SetLookAgent)
            agent.SetLookAgent(null);

            // 4. 确保控制器回归 AI
            if (!V.IsAgentAI(agent))
            {
                V.SetAgentAI(agent);
            }

            // 5. 如果之前在使用物体（比如椅子），完整起身序列
            if (agent.IsUsingGameObject)
            {
                agent.StopUsingGameObject(false, Agent.StopUsingGameObjectFlags.None);
                agent.SetInteractionAgent(null);
                agent.StopUsingGameObject(true);
            }
        }

        /// <summary>
        /// 挂原版持续跟随（FollowAgentBehavior 三连，2026-08-13 用户裁定）：
        /// 跟随目标=玩家时走原版行为接管——Brain 队列清空/计划结束后 NPC 依然跟随，
        /// 解除靠 stop_following 调 StopVanillaFollow。三连挂在 DailyBehaviorGroup 内，
        /// 组级 Suspend/Resume（Brain 接管）不摘行为，天然实现"插队做事、做完继续跟"。
        /// 反编译参照：SandBox.dll ClanMemberRolesCampaignBehavior.FollowMainAgent。
        /// 已挂则幂等改目标 + 重发 SetScriptedBehavior 断言标志。
        /// </summary>
        public static bool StartVanillaFollow(Agent follower, Agent target)
        {
            if (follower == null || target == null || !follower.IsActive() || !target.IsActive())
                return false;
            if (follower == Agent.Main)
                return false; // 永不接管玩家（对齐 SuspendVanillaAI 纪律）

            // 清残留脚本帧/速度钳/LookAgent/控制器——自研 FollowAgentAction.OnEnd 的
            // MoveEndAndInteractPrepare 会留 SetMaximumSpeedLimit(0f) + DoNotRun|NoAttack 锁，
            // 原版 FollowAgentBehavior 不主动清，不清理会把跟随者钉死。
            ForceUnlockAgent(follower);

            var nav = follower.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (nav == null) return false;
            var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
            if (daily == null) return false;

            if (daily.GetBehavior<FollowAgentBehavior>() == null)
            {
                daily.AddBehavior<FollowAgentBehavior>();
            }
            daily.SetScriptedBehavior<FollowAgentBehavior>();   // 幂等断言（防外部覆盖）
            daily.GetBehavior<FollowAgentBehavior>().SetTargetAgent(target);

            DebugLogger.Log($"[VanillaFollow] {follower.Name}(Idx={follower.Index}) → 目标 {target.Name}");
            return true;
        }

        /// <summary>
        /// 解挂原版持续跟随 + 恢复原版日常行为（2026-08-13 用户裁定：回岗闲逛）。
        /// 对齐原版 AdjustTheBehaviorsOfTheAgent 的解除语义：RemoveBehavior 会把
        /// ScriptedBehavior 自动置 null 并 ForceThink（反编译 AgentBehaviorGroup 验证），
        /// 行为组回到自由竞争（GetAvailability）——恢复 Script/Walking 的 IsActive 后
        /// NPC 自然回岗闲逛（Walking 权重最高），无需也不存在"DailyBehavior"类型。
        /// 不碰 daily.IsActive：组级开关归 AgentBrain Suspend/Resume 管。
        /// </summary>
        public static bool StopVanillaFollow(Agent follower)
        {
            if (follower == null || !follower.IsActive())
                return false;
            if (follower == Agent.Main)
                return false;

            var nav = follower.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (nav == null) return false;
            var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
            if (daily == null) return false;

            daily.RemoveBehavior<FollowAgentBehavior>();   // 未挂载时返回 false，安全；ScriptedBehavior 自动置 null

            var script = daily.GetBehavior<ScriptBehavior>();
            if (script != null) script.IsActive = true;
            var walking = daily.GetBehavior<WalkingBehavior>();
            if (walking == null) walking = daily.AddBehavior<WalkingBehavior>();
            walking.IsActive = true;

            DebugLogger.Log($"[VanillaFollow-Stop] {follower.Name}(Idx={follower.Index}) 解挂原版跟随，回岗");
            return true;
        }

        // ===================================================================
        public static async Task MovePrepare(Agent npcAgent)
        {
            if (npcAgent == null || !npcAgent.IsActive()) return;

            // 1. 停止当前交互 (如坐在椅子上)
            if (npcAgent.IsUsingGameObject)
            {
                npcAgent.StopUsingGameObject(false, Agent.StopUsingGameObjectFlags.None);
                npcAgent.SetInteractionAgent(null);
                await Task.Delay(2000); // 给动画一点混合时间
                if (!npcAgent.IsActive()) return; // 检查存活
                npcAgent.StopUsingGameObject(true);
            }

            // 2. 清除 AI 状态，准备接管
            npcAgent.Formation = null;
            npcAgent.ClearTargetFrame();
            npcAgent.SetTargetAgent(null);
            V.SetAgentAI(npcAgent);
            npcAgent.SetActionChannel(0, ActionIndexCache.act_none, true, 0UL, 0f, 1f, 0.5f);
            npcAgent.SetActionChannel(1, ActionIndexCache.act_none, true, 0UL, 0f, 1f, 0.5f);
        }
        public static void MoveEndAndInteractPrepare(Agent npcAgent)
        {
            MoveEndAndInteractPrepare(npcAgent, npcAgent.Position);
        }
        public static void MoveEndAndInteractPrepare(Agent npcAgent, Vec3 initPos)
        {
            if (npcAgent == null || !npcAgent.IsActive()) return;
            WorldPosition currentPos = new WorldPosition(npcAgent.Mission.Scene, initPos);
            var lockFlags = AIScriptedFrameFlags.DoNotRun |
                            AIScriptedFrameFlags.NoAttack |
                            AIScriptedFrameFlags.InConversation;

            npcAgent.SetScriptedPosition(ref currentPos, false, lockFlags);
            npcAgent.SetMaximumSpeedLimit(0f, false);
        }
        public static async Task MoveTo(Agent npcAgent, Vec3 targetVec, Vec2  targetDir, float stopDistance = 0.5f)
        {

            await MovePrepare(npcAgent);

            // 修正到导航网格上 (防止点在墙里)
            WorldPosition targetPos = new WorldPosition(npcAgent.Mission.Scene, UIntPtr.Zero, targetVec, false);
            // 如果点无效，尝试获取最近的导航网格
            if (targetPos.GetNavMesh() == UIntPtr.Zero)
            {
                V.NavMeshSnap(npcAgent.Mission.Scene, ref targetVec);
                targetPos = new WorldPosition(npcAgent.Mission.Scene, targetVec);
            }

            // 4. 设置移动参数
            var moveFlags = AIScriptedFrameFlags.GoToPosition |
                            AIScriptedFrameFlags.DoNotRun |     // 走路，显着优雅
                            AIScriptedFrameFlags.NoAttack |
                            AIScriptedFrameFlags.NeverSlowDown;


            // 5. 循环检查距离 (移动过程)
            float timeElapsed = 0f;
            float timeout = 8f; // 8秒超时

            while (npcAgent.Position.Distance(targetVec) > stopDistance && timeElapsed < timeout)
            {
                // 持续更新目标点 (防止玩家移动后NPC去错地方，或者每帧刷新确保不掉队)
                npcAgent.SetScriptedPosition(ref targetPos, false, moveFlags);

                await Task.Delay(200); // 没必要太频繁
                timeElapsed += 0.2f;

                if (!npcAgent.IsActive()) return;
            }

            // 6. 超时处理 (如果卡住了，瞬移最后一段距离)
            // 🔴 2026-08-21（在押守卫）：在押随从禁止瞬移（移动卡死 → TeleportToPosition = 越狱路径）

            // 保持朝向瞬移
            Vec3 finalPos = targetVec;
            if (!CompanionDetentionBehavior.IsDetained(npcAgent))
                npcAgent.TeleportToPosition(finalPos);
            npcAgent.SetMovementDirection(targetDir);


            MoveEndAndInteractPrepare(npcAgent);
        }

        public static void LookAtAgent(Agent agent, Agent target)
        {
            if (agent == null) return;
            agent.SetLookAgent(target);
        }
        public static void StopLooking(Agent agent)
        {
            if (agent == null) return;
            agent.SetLookAgent(null);
        }
        public static void FaceToActor(Agent turnAgent, Agent targetAgent)
        {
            //计算两个人的位置差向量
            Vec3 direction = targetAgent.Position - turnAgent.Position;
            direction.Normalize();
            turnAgent.SetMovementDirection(direction.AsVec2);

        }
        public static string GetPartyInfo(Hero targetHero)
        {
            StringBuilder sb = new StringBuilder();

            MobileParty party = targetHero.PartyBelongedTo;
            if (party != null)
            {
                // 情报：所属部队名
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_party_belongs_to",
                    "Belongs to party: {NAME}", ("NAME", (object)(party.Name ?? V.EmptyText()))));
                // 情报：部队成员区块标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_member_header", "\n--- Party members (troops) ---"));

                TroopRoster memberRoster = party.MemberRoster;
                if (memberRoster != null && memberRoster.Count > 0)
                {
                    int totalMen = memberRoster.TotalManCount;
                    int totalWounded = memberRoster.TotalWounded;
                    int totalHeroes = memberRoster.TotalHeroes;

                    // 情报：部队人数概览（总人数/英雄/伤员）
                    sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_overview",
                        "  [Overview] Total: {TOTAL} (Heroes: {HEROES}, Wounded: {WOUNDED})",
                        ("TOTAL", totalMen.ToString()), ("HEROES", totalHeroes.ToString()), ("WOUNDED", totalWounded.ToString())));

                    // 获取具体的部队列表
                    var troops = memberRoster.GetTroopRoster();

                    foreach (var element in troops)
                    {
                        if (element.Character == null) continue;

                        string charName = element.Character.Name.ToString();
                        int count = element.Number; // 健康数量 + 受伤数量
                        int wounded = element.WoundedNumber;
                        string tier = element.Character.IsHero ? "Hero" : $"T{element.Character.Tier}";
                        string id = element.Character.StringId;

                        // 情报：伤员后缀
                        string woundInfo = wounded > 0
                            // (含伤员: {WOUNDED})
                            ? LWNTextHelper.ResolveCompoundMixed("LWN_info_wounded_suffix",
                                "(incl. wounded: {WOUNDED})", ("WOUNDED", wounded.ToString()))
                            : "";

                        // 情报：单条士兵行（等级/名字/人数/伤员/ID）
                        sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_troop_line",
                            "  - [{TIER}] {NAME} : {COUNT} men {WOUND} [ID:{ID}]",
                            ("TIER", tier), ("NAME", charName), ("COUNT", count.ToString()),
                            ("WOUND", woundInfo), ("ID", id)));
                    }
                }
                else
                {
                    // 情报：无士兵结果行
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_no_soldiers", "  [Result] Lone commander, no soldiers."));
                }
            }
            else
            {
                // 情报：英雄没有所属部队
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_no_party",
                    " {NAME} has no party right now.",
                    ("NAME", (object)(targetHero.Name ?? V.EmptyText()))));
            }
            return sb.ToString();
        }
        public static string GetBagInfo(Hero targetHero,bool IsPrompt= false)
        {
            StringBuilder sb = new StringBuilder();

            if (targetHero == null)
            {
                // 情报：目标 Hero 为空错误
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_error_target_null", "Error: Target Hero is null!"));
                return sb.ToString();
            }

            // 情报：背包报告标题
            sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_bag_report_header",
                "========== [{NAME}] Bag Info Report ==========",
                ("NAME", (object)(targetHero.Name ?? V.EmptyText()))));

            // ---------------------------------------------------------
            // 第一部分：装备检查 (优先检查 Agent 实体，否则检查 Hero 配置)
            // ---------------------------------------------------------
            // 情报：贴身装备区块标题
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_bag_equipment_header", "--- [1] Equipped Gear ---"));

            Equipment equipmentToInspect = null;

            // 1. 尝试获取场景内的 Agent
            Agent agent = null;
            if (Mission.Current != null && Mission.Current.Agents != null)
            {
                agent = Mission.Current.Agents.FirstOrDefault(a => a.Character == targetHero.CharacterObject);
            }

            // 2. 决定查看哪套装备
            if (agent != null)
            {
                // 如果人在场景里，看他实际身上穿的（包含临时捡起的武器等）
                equipmentToInspect = agent.SpawnEquipment;
            }
            else
            {
                // 如果人不在场景里，看他数据层面的战斗装备
                equipmentToInspect = targetHero.BattleEquipment;
            }
            //sb.AppendLine($"状态: {(agent != null ? "在场景中" : "未在场景中/大地图模式")} {sourceInfo}");

            // 情报：持有金钱
            sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_held_gold",
                "-Gold held: {GOLD}", ("GOLD", targetHero.Gold.ToString())));

            // 3. 遍历打印装备
            if (equipmentToInspect != null)
            {
                bool hasItem = false;
                // EquipmentIndex 0-3 是武器，4-9 是防具，10-11 是马匹/马具

                // 情报：所骑马匹
                if(!equipmentToInspect.Horse.IsEmpty)
                    // -所骑马匹: {NAME}
                    sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_horse",
                        "-Riding horse: {NAME}", ("NAME", equipmentToInspect.Horse.Item.Name?.ToString() ?? "")));

                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
                {
                    EquipmentIndex slotIndex = i;
                    EquipmentElement element = equipmentToInspect[slotIndex];

                    if (!element.IsEmpty && element.Item != null)
                    {
                        hasItem = true;
                        string slotName = slotIndex.ToString(); // Get Enum name (e.g., Head, Body, Leg)
                        string itemId = element.Item.StringId; // The ID you need
                        string itemName = element.Item.Name.ToString();
                        string modifyName = element.ItemModifier?.Name.ToString();
                        // 情报：品质修饰后缀（如"精良的"）
                        modifyName = string.IsNullOrEmpty(modifyName) ? "" :
                            // [{MODIFY}的]
                            LWNTextHelper.ResolveCompoundMixed("LWN_info_modifier_suffix", "[{MODIFY}]", ("MODIFY", modifyName));
                        // Format: [SlotName] ItemID
                        if (!IsPrompt)
                        {
                            // 情报：单条装备槽位行
                            sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_slot_line",
                                "  - Slot[{SLOT}]: {MODIFIED}{NAME} (ID: {ID})",
                                ("SLOT", slotName.PadRight(12)), ("MODIFIED", modifyName),
                                ("NAME", itemName), ("ID", itemId)));
                        }
                        else
                            sb.Append($"{modifyName}{itemName} ");
                    }
                }
                
                // 情报：身上无任何装备
                if (!hasItem) sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_no_equipment", "  [Result] No equipment worn at all."));
            }
            else
            {
                // 情报：装备数据获取失败
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_equipment_error", "  [Error] Could not retrieve equipment data."));
            }


            // ---------------------------------------------------------
            // 第二部分：打印 Party 里的物品 (背包里的)
            // ---------------------------------------------------------
            // 情报：部队辎重区块标题
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_info_luggage_header", "\n--- [2] Party luggage (item inventory) ---"));

            MobileParty party = targetHero.PartyBelongedTo;

            if (party != null)
            {
                // 情报：所属部队名
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_party_belongs_to",
                    "Belongs to party: {NAME}", ("NAME", (object)(party.Name ?? V.EmptyText()))));

                // 检查物品
                var itemRoster = party.ItemRoster;
                if (itemRoster != null && itemRoster.Count > 0)
                {
                    int totalValue = 0;

                    // 为了美观，可以按价值排序
                  
                    var sortedItems = itemRoster
            .Where(x => !x.IsEmpty && x.EquipmentElement.Item != null)
            .OrderByDescending(x => x.EquipmentElement.Item.Value * x.Amount)
            .ToList(); // 转换为 List 方便处理
                    //三个一行
                    int colIndex = 0;
                    int columnWidth = 300; // 设定每列的固定宽度（根据实际显示区域调整）
                    foreach (var element in sortedItems)
                    {
                        var item = element.EquipmentElement.Item;
                        string itemName = item.Name.ToString();
                        int amount = element.Amount;
                        int valuePerItem = item.Value;
                        int subTotal = valuePerItem * amount;

                        totalValue += subTotal;
                        string displayName = itemName.Length > 10 ? itemName.Substring(0, 9) + ".." : itemName;
                        // 情报：单条物品行（数量/单价/总价）
                        string itemDesc = LWNTextHelper.ResolveCompoundMixed("LWN_info_item_line",
                            "- {NAME} x{AMOUNT} (unit:{UNIT}|total:{TOTAL})",
                            ("NAME", displayName), ("AMOUNT", amount.ToString()),
                            ("UNIT", valuePerItem.ToString()), ("TOTAL", subTotal.ToString()));

                        // 赃物来源标注（查 TheftLedger，按背包主人 + 物品精确匹配）
                        string sourceTag = TheftLedger.GetSourceTag(item.StringId, Hero.MainHero.StringId);
                        if (!string.IsNullOrEmpty(sourceTag))
                            itemDesc += $" {sourceTag}";

                        if (itemDesc.Length < columnWidth)
                        {
                            itemDesc = itemDesc.PadRight(columnWidth);
                        }
                        sb.Append(itemDesc);
                        colIndex++;
                        colIndex = colIndex % 3;
                        if (colIndex % 3 == 0)
                        {
                            sb.AppendLine();
                        }
                    }
                    if (colIndex % 3 != 0)
                    {
                        sb.AppendLine();
                    }
                    // 情报：物品统计行（种类数/总估值）
                    sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_stats",
                        "  [Stats] Item kinds: {KINDS} | Total value: {VALUE}",
                        ("KINDS", itemRoster.Count.ToString()), ("VALUE", totalValue.ToString())));
                }
                else
                {
                    // 情报：辎重为空
                    sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_empty_luggage",
                        "{NAME}'s luggage is empty.",
                        ("NAME", (object)(targetHero.Name ?? V.EmptyText()))));
                }               
            }
            else
            {
                // 情报：没有部队辎重
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_info_no_luggage",
                    "{NAME} currently has no party luggage.",
                    ("NAME", (object)(targetHero.Name ?? V.EmptyText()))));
            }

            return sb.ToString();
        }

        // ===================================================================
        //  资源操作（铁律 4）—— 凡「看上去像资源进出」的地方都走这里，禁止业务层
        //  裸调 Hero.ChangeHeroGold / ItemRoster.AddToCounts 等单边 API。
        //  金钱视为「特殊物品」(Item==null)，三类操作各有纪律：
        //   ① 转移 Transfer（守恒）：A→B，一方扣一方加，禁止半截。TransferGold / TransferItems
        //   ② 收发 Grant/Sink（单边对接「世界」）：from/to 传 null 即虚空来源 / 去向，合法
        //   ③ 转换 Convert（按配方非守恒）：守卫后 consume 输入 + grant 输出，整体原子。TryConvert
        // ===================================================================

        /// <summary>
        /// 金钱守恒转移：amount 从 <paramref name="from"/> 的钱袋转移到 <paramref name="to"/> 的钱袋。
        /// 内部封装引擎成对接口 GiveGoldAction.ApplyBetweenCharacters，保证总量守恒。
        /// </summary>
        /// <param name="from">付出方。传 null = 引擎认可的「虚空来源」（战利品 / 系统奖励，凭空发放）。</param>
        /// <param name="to">接收方。传 null = 付给「虚空」（罚没 / 消耗，无人接收）。</param>
        /// <param name="amount">期望转移的金额（&lt;=0 直接返回）。</param>
        /// <param name="notify">是否弹引擎默认提示。</param>
        /// <returns>实际转移的金额（付出方余额不足时会被截断为其全部余额）。</returns>
        public static int TransferGold(Hero from, Hero to, int amount, bool notify = true)
        {
            if (amount <= 0) return 0;
            // 余额保护：付出方钱不够时只转移其全部余额，绝不让其变负
            if (from != null && from.Gold < amount)
                amount = from.Gold;
            if (amount <= 0) return 0;

            GiveGoldAction.ApplyBetweenCharacters(from, to, amount, disableNotification: !notify);
            return amount;
        }

        /// <summary>
        /// 金钱守恒转移：amount 从 <paramref name="from"/> 定居点金库转移到 <paramref name="to"/> 英雄。
        /// Village 和 Town 都继承 SettlementComponent，各自有独立的 Gold 池。
        /// 内部封装 GiveGoldAction.ApplyForSettlementToCharacter，保证总量守恒。
        /// </summary>
        public static int TransferGold(Settlement from, Hero to, int amount, bool notify = true)
        {
            if (from == null || to == null || amount <= 0) return 0;
            // Village 和 Town 都继承 SettlementComponent，都有 Gold + ChangeGold
            var component = (SettlementComponent)from.Town ?? from.Village;
            if (component == null) return 0;
            int available = component.Gold;
            int actual = Math.Min(amount, available);
            if (actual <= 0) return 0;
            GiveGoldAction.ApplyForSettlementToCharacter(from, to, actual, disableNotification: !notify);
            return actual;
        }

        /// <summary>
        /// 绝对设置某英雄持有金为指定值（剧本 / 调试用的「上帝指令」，非守恒）。
        /// 仅供 Story 脚本、调试指令调用；正常玩法的给钱 / 收钱请用 <see cref="TransferGold"/>。
        /// 内部仍走 GiveGoldAction（增钱从虚空来、减钱往虚空去），保证 gold 变更全部归口本类。
        /// </summary>
        public static void SetGold(Hero hero, int targetGold, bool notify = false)
        {
            if (hero == null) return;
            int delta = targetGold - hero.Gold;
            if (delta == 0) return;

            // delta > 0：从虚空发放给 hero；delta < 0：hero 付给虚空
            TransferGold(delta > 0 ? (Hero)null : hero,
                         delta > 0 ? hero : null,
                         Math.Abs(delta), notify);
        }

        /// <summary>
        /// 物品守恒转移：count 件 <paramref name="item"/> 从 <paramref name="from"/> 的辎重转移到
        /// <paramref name="to"/> 的辎重。取 <see cref="EquipmentElement"/> 以保留品质修正(ItemModifier)。
        /// </summary>
        /// <param name="from">付出方。null = 虚空来源（战利品 / 偷窃 / 任务凭空奖励）。</param>
        /// <param name="to">接收方。null = 付给虚空（上交 / 消耗）。</param>
        /// <returns>实际转移数量（付出方库存不足时截断）。</returns>
        public static int TransferItems(Hero from, Hero to, EquipmentElement item, int count)
        {
            if (item.IsEmpty || count <= 0) return 0;
            ItemRoster fromRoster = from?.PartyBelongedTo?.ItemRoster;
            ItemRoster toRoster = to?.PartyBelongedTo?.ItemRoster;

            // 库存保护：付出方不够时截断（按基础物品计数）
            if (fromRoster != null)
            {
                int have = fromRoster.GetItemNumber(item.Item);
                if (have < count) count = have;
            }
            if (count <= 0) return 0;

            fromRoster?.AddToCounts(item, -count);  // null = 虚空来源，不扣谁
            toRoster?.AddToCounts(item, count);      // null = 虚空去向，不给谁
            return count;
        }

        /// <summary>便捷重载：无品质修正的普通物品。</summary>
        public static int TransferItems(Hero from, Hero to, ItemObject item, int count)
            => item == null ? 0 : TransferItems(from, to, new EquipmentElement(item, null), count);

        /// <summary>
        /// 转换配方里的一项资源。金钱即「特殊物品」：<see cref="Item"/> 为 null 表示金钱。
        /// </summary>
        public struct ResourceCost
        {
            public ItemObject Item;   // null = 金钱
            public int Count;
            public static ResourceCost Gold(int n) => new ResourceCost { Item = null, Count = n };
            public static ResourceCost Of(ItemObject item, int n) => new ResourceCost { Item = item, Count = n };
        }

        /// <summary>
        /// 某英雄是否持有足够的某项资源（金钱或物品）。
        /// </summary>
        public static bool HasResource(Hero owner, ResourceCost cost)
        {
            if (cost.Count <= 0) return true;
            if (owner == null) return false;
            if (cost.Item == null) return owner.Gold >= cost.Count;
            ItemRoster roster = owner.PartyBelongedTo?.ItemRoster;
            return roster != null && roster.GetItemNumber(cost.Item) >= cost.Count;
        }

        /// <summary>
        /// 转换器（铁律 4 第③类，按配方非守恒）：<paramref name="owner"/> 消耗 <paramref name="inputs"/>
        /// 产出 <paramref name="outputs"/>。**守卫 + 原子**：任一输入不足则整体不发生，返回 false。
        /// 引擎外的自定义资源（如饱腹值 / 疲劳：吃苹果→饱腹+10）由调用方在 <paramref name="onConverted"/>
        /// 里施加——它仅在输入扣除成功后执行，从而与配方保持原子。
        /// </summary>
        /// <example>
        /// // 吃一个苹果，饱腹 +10
        /// TryConvert(player,
        ///     new[] { ResourceCost.Of(appleItem, 1) },
        ///     null,
        ///     onConverted: () => satiety += 10);
        /// </example>
        public static bool TryConvert(Hero owner,
                                      IList<ResourceCost> inputs,
                                      IList<ResourceCost> outputs,
                                      Action onConverted = null)
        {
            if (owner == null) return false;

            // 1. 守卫：所有输入都得够，否则整体放弃
            if (inputs != null)
                foreach (ResourceCost c in inputs)
                    if (!HasResource(owner, c)) return false;

            // 2. 消耗输入（sink 到世界）
            if (inputs != null)
                foreach (ResourceCost c in inputs)
                {
                    if (c.Item == null) TransferGold(owner, null, c.Count, notify: false);
                    else TransferItems(owner, null, c.Item, c.Count);
                }

            // 3. 产出（从世界 grant）
            if (outputs != null)
                foreach (ResourceCost c in outputs)
                {
                    if (c.Item == null) TransferGold((Hero)null, owner, c.Count, notify: false);
                    else TransferItems(null, owner, c.Item, c.Count);
                }

            // 4. 引擎外自定义资源（饱腹 / 疲劳…）由调用方原子施加
            onConverted?.Invoke();
            return true;
        }

        public static void ApplyDivorceMarriage(Hero targetHero)
        {
            Hero targetHeroSpouse = targetHero.Spouse;
            if(targetHeroSpouse!= null)
            {
                targetHero.Spouse = null;
                targetHeroSpouse.Spouse = null;
                // 好感变化
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetHero, targetHeroSpouse, -30);
                // 🔴 2026-08-14 停用（用户裁定）：ActionTransactionSystem 婚姻新闻广播管线久未维护
                // （见 deprecated.md）。注释仅停新闻广播；离婚本身/好感/公告照常。
                // // 事务记录：强制离婚事件标签
                // ActionTransactionSystem.RecordAction(SocialEventType.Divorce, targetHero, targetHeroSpouse,
                //     // 强制离婚
                //     LWNTextHelper.ResolveText("LWN_info_action_divorce", "Forced divorce"));
                // 离婚公告：报出双方名字
                InformationManager.DisplayMessage(new InformationMessage(
                    // {NAME1} 与 {NAME2} 离婚了。
                    LWNTextHelper.ResolveCompoundMixed("LWN_info_divorce_announcement",
                        "{NAME1} and {NAME2} are divorced.",
                        ("NAME1", targetHero.Name?.ToString() ?? ""),
                        ("NAME2", targetHeroSpouse.Name?.ToString() ?? ""))));

            }
        }
        public static void ApplyMarriageLogic(Hero hero1, Hero hero2)
        {
            // ... 执行原版 MarriageAction.Apply ...
            MarriageAction.Apply(hero1, hero2);

            // 🔴 2026-08-14 停用（用户裁定）：ActionTransactionSystem 婚姻新闻广播管线久未维护
            // （见 deprecated.md）。注释仅停新闻广播；结婚本身照常。
            // // 事务记录：结婚事件标签
            // ActionTransactionSystem.RecordAction(SocialEventType.Marriage, hero1, hero2,
            //     // 结婚
            //     LWNTextHelper.ResolveText("LWN_info_action_marriage", "Marriage"));
        }

        public static void OnPlayerSelect_MarryNewLover(Hero newLover)
        {
            Hero player = Hero.MainHero;
            Hero exSpouse = player.Spouse;
            Hero loverSpouse = newLover.Spouse;

            // 🔴 2026-08-14 停用（用户裁定）：ActionTransactionSystem 婚姻新闻广播管线久未维护
            // （见 deprecated.md）。事务 Begin/Commit 只为新闻广播服务，随管线一并停用。
            // // 1. 开启事务：告诉系统接下来的一系列操作是一个整体
            // ActionTransactionSystem.BeginTransaction();

            try
            {
                // 2. 如果玩家已婚，先离
                if (exSpouse != null)
                {
                    ApplyDivorceMarriage(player);
                }

                // 3. 如果对方已婚，让对方离 (甚至可能触发决斗事件，这里简化为离婚)
                if (loverSpouse != null)
                {
                    // 这里也可以记录一个离婚事件，如果系统足够复杂，
                    // 事务管理器可以把 "玩家离婚" 和 "新欢离婚" 合并成 "双重出轨"
                    ApplyDivorceMarriage(newLover);
                }

                // 4. 结婚
                ApplyMarriageLogic(player, newLover);
            }
            catch (Exception)
            {
                // 错误处理
            }
            finally
            {
                // 🔴 2026-08-14 停用（用户裁定）：见上方 BeginTransaction 注释，Commit 随管线一并停用。
                // // 5. 提交事务：系统分析刚才发生的 3 件事，发现符合 "ScandalousRemarriage" 模式
                // // 于是只广播一条 "玩家为了新欢抛弃发妻" 的重磅新闻，而不是三条零散新闻
                // ActionTransactionSystem.CommitTransaction();
            }
        }

        // 🔴 2026-08-12（模板 NPC 统一显示名）：Hero → 原名；模板 NPC（无 Hero）→ 「名字#Index」——
        // 场景内唯一标识。HUD 头顶 / 交互区标题 / IM 附近频道 / @预填 四处共用，保证"场景里谁在和我说话"
        // 始终一致。格式：名字与 # 之间**无空格**（用户裁定 2026-08-12）；@提及正则已兼容无空格形式。

        /// <summary>统一显示名：Hero → 原名；模板 NPC → 「名字#Index」。null/空名 → 空串（调用方兜底）。</summary>
        public static string GetDisplayName(Agent agent)
        {
            if (agent == null) return "";
            if (agent.Character is CharacterObject co && co.HeroObject != null)
                return agent.Name?.ToString() ?? "";
            string name = agent.Name?.ToString() ?? "";
            return string.IsNullOrWhiteSpace(name) ? "" : $"{name}#{agent.Index}";
        }
    }
}