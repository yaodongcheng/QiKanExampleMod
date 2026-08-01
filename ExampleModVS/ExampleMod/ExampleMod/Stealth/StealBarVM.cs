using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>偷窃条小游戏模式：扒窃（偷人）/ 撬锁（保管箱）/ 抓动物。</summary>
    public enum StealBarMode { Pickpocket, Lockpick, Animal }

    /// <summary>偷窃条关闭原因，View 轮询消费。</summary>
    public enum StealBarCloseReason
    {
        None,           // 未请求关闭
        TargetGone,     // 目标走开/死亡 → 强制收手（不算被发现）
        Alarmed,        // 受害者警觉拉满/质问锁被占用 → 质问机器接管
        Completed,      // 撬锁全部 pin 解开 → View 接开箱 Inquiry
        NothingLeft,    // 摸空了（无装备也无钱袋）→ View 自动收口 + 提示
        AnimalCaught,   // 抓动物命中 → View 接 CompleteAnimalSteal
        AnimalFled,     // 抓动物手滑 → VM 内已惊叫逃跑，View 只收口
    }

    /// <summary>撬锁进度点 VM：Locked/Unlocked 两态。</summary>
    public class StealPinVM : ViewModel
    {
        private bool _isUnlocked;

        [DataSourceProperty]
        public bool IsUnlocked
        {
            get => _isUnlocked;
            set
            {
                if (value != _isUnlocked)
                {
                    _isUnlocked = value;
                    OnPropertyChangedWithValue(value, nameof(IsUnlocked));
                    OnPropertyChangedWithValue(PinColor, nameof(PinColor));
                }
            }
        }

        [DataSourceProperty]
        public string PinColor => _isUnlocked ? "#FFD75EFF" : "#555555FF";
    }

    /// <summary>
    /// 进度条偷窃小游戏（大侠立志传式）—— 一个 VM 双模式。
    /// 父条内子横条（目标区）+ 浮标左右往复，空格/出手按钮时机判定。
    /// 纯动画状态在 C# 侧，由 View 每帧 <see cref="UpdateFrame"/> 驱动；
    /// 出手走 <see cref="ExecuteAttempt"/>，命中即执行真实偷窃。
    /// 结果文本走 DisplayMessage，子横条颜色闪烁做条上即时反馈。
    /// 数值全部集中在顶部常量区，实机手感后再调。
    /// </summary>
    public class StealBarVM : ViewModel
    {
        // ═══════════════════════ 手感常量区（调手感只动这里） ═══════════════════════
        private const float BarWidth = 640f;                    // 父条像素宽（与 XML 一致）
        private const float BaseZoneWidthPickpocket = 90f;     // 偷人基础子横条宽 px（发布前平衡：110→90，缩短~18%）
        private const float BaseZoneWidthLockpick = 80f;        // 撬锁基础子横条宽 px
        private const float PerfectHalfWidth = 6f;              // 金区半宽（全宽 12px，兼作有效区下限）
        private const float CursorBaseSpeed = 260f;             // 浮标速度 px/s（恒定，不挂钩警戒）
        private const float PinWidthDecay = 0.85f;              // 撬锁第 n pin 宽度 ×0.85ⁿ
        private const float PinSpeedGrowth = 1.15f;             // 撬锁第 n pin 浮标速度 ×1.15ⁿ
        private const float AlertWidthMax = 1.8f;               // 警戒归一化分母 clamp(alert/1.8)（发布前平衡：2.2→1.8，警戒更快达最大左扣）
        private const float AlertWidthImpact = 0.75f;           // 警戒对基础宽的最大左扣比例 75%
        private const float RoguerySkillCap = 300f;             // 流氓技能满值（减速归一化分母）
        private const float RogueryCursorSlowMax = 0.25f;       // 满技能浮标减速 25%（技能走"手"通道，不污染宽度域）// lwn-ignore: A (comment)
        private const float CursorDriftMinRatio = 2.5f;         // 铁律：浮标速度 ≥ 游动 2.5 倍（游动动态封顶保证）
        private const float DriftAmplitudeNorm = 0.06f;         // Cautious 游动幅度 ±6% 条宽
        private const float DriftBaseSpeed = 55f;               // 游动峰值速度 @alert=1.0（px/s）
        private const float DriftAlertSpeed = 45f;              // 警戒每 +1（>1 部分）游动 +45px/s
        private const float DriftMaxSpeed = 100f;               // 游动峰值速度上限 ≈浮标 0.38×
        private const float MaxInteractDistance = 4.5f;         // 目标走开强制收手距离
        private const float ResultFlashSeconds = 0.1f;          // 结果闪现时长（缩放秒——慢动作 0.35× 下 ≈0.3 真实秒；勿超 1，否则染色而非闪烁）
        private const float NoisePulseCooldown = 0.5f;          // 撬锁噪音脉冲节流（防连按刷爆）
        private const float NormalHitVictimAlert = 0.35f;       // 普通命中：受害者警戒脉冲
        private const float MissVictimAlert = 3.0f;             // 失误：受害者警戒脉冲（红区手滑 → 立刻 Alarmed）
        private const float NoiseWitnessAlert = 1.5f;           // 撬锁失误：目击者警戒脉冲（发布前平衡：0.5→1.5）
        private const float PurseChance = 0.35f;                // 扒窃盲盒摸到钱袋的概率（身上有钱时）
        private const float AnimalLargeTierFactor = 0.6f;       // 大动物（猪/羊/牛）判定区定价：右扣 40%（小动物 1.0 不扣）

        // 子横条闪烁颜色（绑定 XML 用 hex 字符串）
        // 二元色相分离：黄/绿=安全可偷，红族=危险不可偷。闪烁 = 所中区域变亮报结果：成功亮金 / 失败红。
        // 完美用白闪不用绿闪——绿闪会把整个黄区染成"全是绿芯"，摧毁区域语义（且慢动作下闪光被拉长、还会带入下一回合）
        private const string ZoneColorNormal = "#D4AF37FF";     // 常态琥珀黄（可偷区）
        private const string ZoneColorSuccess = "#FFE97FFF";    // 成功亮金脉冲
        private const string ZoneColorPerfect = "#FFFFFFFF";    // 完美白闪
        private const string ZoneColorFail = "#DD4444FF";       // 失败红闪

        // DisplayMessage 文本色（与闪烁同色系）
        private static readonly Color MsgColorSuccess = new Color(1.00f, 0.91f, 0.37f);   // #FFE97F（与成功金闪同色）
        private static readonly Color MsgColorPerfect = new Color(0.27f, 0.87f, 0.27f);   // #44DD44（与绿芯同色）
        private static readonly Color MsgColorFail = new Color(0.87f, 0.27f, 0.27f);      // #DD4444

        // 动态区域提示行（规则行②）文本色：与色块同族略提亮，保证深色面板上可读
        private const string HintColorPerfect = "#55CC55FF";    // 绿
        private const string HintColorEffective = "#E8C55AFF";  // 琥珀黄
        private const string HintColorAlert = "#E06055FF";      // 血红（提亮）
        private const string HintColorItem = "#D08050FF";       // 橙红（提亮）
        private const string HintColorOutside = "#999999FF";    // 灰

        private readonly StealBarMode _mode;
        private readonly Agent _target;             // Pickpocket 受害者；Lockpick 为 null
        private readonly Action _closeAction;
        private readonly Random _random = new Random();

        // ── 运动状态（纯 C#，非绑定）──
        private float _cursorPos = 0f;      // 0~1
        private float _cursorDir = 1f;
        private float _zoneCenter;          // 0~1（含游动偏移）
        private float _baseZoneCenter;      // 0~1（本回合基准位）
        private float _baseWidthPx;         // 基础宽（潜在判定区，含技能加成）
        private float _alertLossPx;         // 左扣：警戒损失
        private float _itemLossPx;          // 右扣：物品损失（撬锁恒 0）
        private float _effWidthPx;          // 有效判定区 = 基础 − 两扣，下限 = 完美区宽
        private float _driftPhase;

        // ── Pickpocket 状态 ──
        private EquipmentIndex? _pendingSlot;   // 本回合预摸的槽位（钱袋回合为 null）
        private bool _pendingIsPurse;           // 本回合预摸的是钱袋（金钱独立偷窃目标）
        private float _itemTierFactor = 1f;     // 预摸物品的双维定价（重量×价值）；Animal 模式复用为体型定价
        private float _stealSessionPeakAlert = 0f; // 本次偷窃会话受害者的最高警戒值（黏性用）

        // ── Animal 状态 ──
        private string _animalName;

        // ── Lockpick 状态 ──
        private int _pinCount;
        private int _currentPin;
        private float _lastNoiseTime = -999f;

        // ── 结果闪现 ──
        private float _resultFlashTimer;

        /// <summary>View 每帧轮询的关闭请求。</summary>
        public StealBarCloseReason CloseReason { get; private set; } = StealBarCloseReason.None;

        // ═══════════════════════ 构造 ═══════════════════════

        /// <summary>Pickpocket：偷 <paramref name="target"/>。</summary>
        public StealBarVM(StealBarMode mode, Agent target, Action closeAction)
        {
            _mode = mode;
            _target = target;
            _closeAction = closeAction;

            Pins = new MBBindingList<StealPinVM>();
            IsPickpocketMode = true;
            IsLockpickMode = false;
            // 本地化：扒窃条标题
            TitleText = LWNTextHelper.ResolveCompound("LWN_ui_steal_title_pickpocket", ("NAME", target?.Name ?? "?"));
            RefreshButtonTexts();
            // 本地化：扒窃规则行（区域语义总述）
            RuleText = LWNTextHelper.ResolveText("LWN_ui_steal_rule_pickpocket", "<span style=\"Perfect\">[Perfect] Steal in the green zone.</span><span style=\"Normal\">[Normal] Steal in the yellow zone.</span><span style=\"Fail\">[Fail] Steal in the red zone.</span>");

            NextPickpocketRound();
            RecalcZoneSize();
            NewZonePosition();
        }

        /// <summary>Lockpick：撬 <paramref name="pinCount"/> 个簧片的锁。</summary>
        public StealBarVM(StealBarMode mode, int pinCount, string title, Action closeAction)
        {
            _mode = mode;
            _closeAction = closeAction;
            _pinCount = Math.Max(1, pinCount);
            _currentPin = 0;

            Pins = new MBBindingList<StealPinVM>();
            for (int i = 0; i < _pinCount; i++)
                Pins.Add(new StealPinVM());

            IsPickpocketMode = false;
            IsLockpickMode = true;
            TitleText = title;
            RefreshButtonTexts();
            // 本地化：撬锁规则行（区域语义总述）
            RuleText = LWNTextHelper.ResolveText("LWN_ui_steal_rule_lockpick", "<span style=\"Perfect\">[Perfect] Pick in the green zone.</span><span style=\"Normal\">[Normal] Pick in the yellow zone.</span><span style=\"Fail\">[Fail] The crowbar slips in the red zone.</span>");
            PreviewText = "";

            RecalcZoneSize();
            NewZonePosition();
        }

        /// <summary>Animal：抓 <paramref name="animal"/>。大动物（猪/羊/牛）判定区右扣 40%，小动物全宽。一次出手定胜负。</summary>
        public StealBarVM(StealBarMode mode, Agent animal, string animalName, bool isLarge, Action closeAction)
        {
            _mode = mode;
            _target = animal;
            // 本地化：动物名兜底
            _animalName = string.IsNullOrWhiteSpace(animalName) ? LWNTextHelper.ResolveText("LWN_ui_name_animal", "animal") : animalName;
            _closeAction = closeAction;

            Pins = new MBBindingList<StealPinVM>();
            IsPickpocketMode = true;    // 显示上复用扒窃布局（预览行、无簧片）
            IsLockpickMode = false;
            // 本地化：抓动物条标题
            TitleText = LWNTextHelper.ResolveCompound("LWN_ui_steal_title_animal", ("NAME", _animalName));
            RefreshButtonTexts();
            // 本地化：抓动物规则行（区域语义总述）
            RuleText = LWNTextHelper.ResolveText("LWN_ui_steal_rule_animal", "<span style=\"Perfect\">[Perfect] Act in the green zone.</span><span style=\"Normal\">[Normal] Act in the yellow zone.</span><span style=\"Fail\">[Fail] You'll be shaken off in the red zone.</span>");
            _itemTierFactor = isLarge ? AnimalLargeTierFactor : 1f;
            // 本地化：抓动物预览行（大动物/小动物）
            PreviewText = isLarge ? LWNTextHelper.ResolveText("LWN_ui_steal_preview_animal_large", "It will struggle wildly — wait for the right moment and grab it!") : LWNTextHelper.ResolveText("LWN_ui_steal_preview_animal_small", "It's alert — wait for the right moment and grab it!");

            RecalcZoneSize();
            NewZonePosition();
        }

        // ═══════════════════════ 每帧驱动（View 调用） ═══════════════════════

        public void UpdateFrame(float dt)
        {
            if (dt <= 0f) dt = 0.0001f;

            // 结果闪现计时 → 子横条恢复常态金（结果文本走 DisplayMessage，无需清文本）
            if (_resultFlashTimer > 0f)
            {
                _resultFlashTimer -= dt;
                if (_resultFlashTimer <= 0f)
                    ZoneColor = ZoneColorNormal;
            }

            if (CloseReason != StealBarCloseReason.None) return;

            // 强制关闭条件轮询
            PollForceClose();
            if (CloseReason != StealBarCloseReason.None) return;

            // 子横条宽度（警戒主通道）+ 游动（Cautious 辅通道）
            RecalcZoneSize();
            UpdateDrift(dt);

            // 浮标 ping-pong（匀速撞墙折返；速度 = 基准 ×技能减速 ×撬锁 1.15ⁿ）
            float speed = GetCursorSpeed();
            _cursorPos += _cursorDir * speed * dt / BarWidth;
            if (_cursorPos >= 1f) { _cursorPos = 1f; _cursorDir = -1f; }
            else if (_cursorPos <= 0f) { _cursorPos = 0f; _cursorDir = 1f; }

            // 同步绑定（五层：基础底色 → 左扣警戒 → 右扣物品 → 有效区 → 完美区）
            float zoneCenterPx = _zoneCenter * BarWidth;
            float baseLeft = zoneCenterPx - _baseWidthPx / 2f;
            CursorMarginLeft = _cursorPos * BarWidth - 3f;      // 浮标半宽 3px
            BaseMarginLeft = baseLeft;
            BaseWidth = _baseWidthPx;
            AlertLossWidth = _alertLossPx;                       // 左扣与基础区同起点
            ItemLossMarginLeft = baseLeft + _baseWidthPx - _itemLossPx;
            ItemLossWidth = _itemLossPx;
            ZoneMarginLeft = baseLeft + _alertLossPx;
            ZoneWidth = _effWidthPx;
            PerfectMarginLeft = baseLeft + _alertLossPx + _effWidthPx / 2f - PerfectHalfWidth;

            // 规则行②（动态）：浮标脚下区域的含义+可否出手
            UpdateCursorZoneHint();
        }

        /// <summary>
        /// 动态区域提示行：按浮标当前位置判定所在区域，输出含义+可否出手与对应颜色。
        /// 判定顺序 = 视觉层级：完美 ⊂ 有效 ⊂ 基础 − 左右两扣，其余为界外。
        /// </summary>
        private void UpdateCursorZoneHint()
        {
            float cursorPx = _cursorPos * BarWidth;
            float baseLeft = _zoneCenter * BarWidth - _baseWidthPx / 2f;
            float effLeft = baseLeft + _alertLossPx;
            float effRight = effLeft + _effWidthPx;
            float effCenterPx = (effLeft + effRight) / 2f;

            string text, color;
            if (MathF.Abs(cursorPx - effCenterPx) <= PerfectHalfWidth)
            {
                // 完美区（有效区正中 12px）
                // 本地化：完美区提示（按模式区分）
                text = _mode == StealBarMode.Lockpick ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_perfect_lockpick", "[Perfect] The pick makes no sound")
                     // 【完美】此时出手不会惊吓到动物
                     : _mode == StealBarMode.Animal ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_perfect_animal", "[Perfect] The animal won't be startled")
                     // 【完美】此时下手，不会让对方察觉
                     : LWNTextHelper.ResolveText("LWN_ui_steal_hint_perfect_pickpocket", "[Perfect] Your target won't notice");
                color = HintColorPerfect;
            }
            else if (cursorPx >= effLeft && cursorPx <= effRight)
            {
                // 有效判定区
                // 本地化：有效区提示（按模式区分）
                text = _mode == StealBarMode.Lockpick ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_effective_lockpick", "[Can Pick] You'll open it, but it makes a sound")
                     // 【可抓】此时出手能抓住但是会有点动静
                     : _mode == StealBarMode.Animal ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_effective_animal", "[Can Catch] You'll grab it, but it causes a stir")
                     // 【可偷】能偷到，但是对方可能会更加警惕
                     : LWNTextHelper.ResolveText("LWN_ui_steal_hint_effective_pickpocket", "[Can Steal] You'll get it, but your target grows wary");
                color = HintColorEffective;
            }
            else if (cursorPx >= baseLeft && cursorPx < effLeft)
            {
                // 左扣：警戒损失（仅人有；宽度为 0 时区间为空，天然不命中）
                // 本地化：左扣警戒区提示
                text = LWNTextHelper.ResolveText("LWN_ui_steal_hint_careful", "[Careful] Your target will notice you");
                color = HintColorAlert;
            }
            else if (cursorPx > effRight && cursorPx <= baseLeft + _baseWidthPx && _itemLossPx > 0.5f)
            {
                // 右扣：物品/体型损失
                // 本地化：右扣区提示（物品/体型损失）
                text = _mode == StealBarMode.Animal ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_nomove_animal", "[Don't Move] Stay still")
                     // 【勿动】对方正接触此物品，不可出手
                     : LWNTextHelper.ResolveText("LWN_ui_steal_hint_nomove_item", "[Don't Move] They're touching that item — don't act");
                color = HintColorItem;
            }
            else
            {
                // 界外（父条背景）
                // 本地化：界外区提示（按模式区分）
                text = _mode == StealBarMode.Lockpick ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_outside_lockpick", "[Don't Move] Not yet — you won't open it")
                     // 【勿动】时机不对，必被吓跑
                     : _mode == StealBarMode.Animal ? LWNTextHelper.ResolveText("LWN_ui_steal_hint_outside_animal", "[Don't Move] Wrong timing — it will flee")
                     // 【勿动】此时出手必被发现
                     : LWNTextHelper.ResolveText("LWN_ui_steal_hint_outside_pickpocket", "[Don't Move] You'll be caught");
                color = HintColorOutside;
            }
            CursorZoneText = text;
            CursorZoneColor = color;
        }

        /// <summary>目标走开/死亡/警觉拉满/质问锁 → 请求关闭。</summary>
        private void PollForceClose()
        {
            // 质问锁被占用（任何人进入 L3 质问）→ 所有模式都收手，质问机器接管
            if (AgentBrain.ConfrontingBrain != null)
            {
                CloseReason = StealBarCloseReason.Alarmed;
                return;
            }

            if (_mode == StealBarMode.Lockpick) return;

            // Pickpocket / Animal：目标消失或走开 → 强制收手（动物无 Brain，警觉检查天然跳过）
            if (_target == null || !_target.IsActive())
            {
                CloseReason = StealBarCloseReason.TargetGone;
                return;
            }
            if (Agent.Main == null || !Agent.Main.IsActive()
                || _target.Position.Distance(Agent.Main.Position) > MaxInteractDistance)
            {
                CloseReason = StealBarCloseReason.TargetGone;
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(_target);
            if (brain != null && brain.AlertPhase >= AlarmPhase.Alarmed)
                CloseReason = StealBarCloseReason.Alarmed;
        }

        /// <summary>扒窃读受害者警戒；撬锁无受害者——锁难度固定，目击压力由 NPC 自身警戒系统承担（IsUIOpen 累积 → Alarmed → 质问机器强制收手）。
        /// 引入会话峰值黏性：取 max(当前值, 峰值×0.8)，防止受害者看不到玩家时警戒衰减导致黄区越偷越宽。</summary>
        private float GetCurrentAlert()
        {
            if (_mode == StealBarMode.Lockpick || _target == null) return 0f;
            float current = AgentAIController.GetBrainForAgent(_target)?.AlertValue ?? 0f;
            _stealSessionPeakAlert = MathF.Max(_stealSessionPeakAlert, current);
            // 黏性：取当前与峰值 80% 的较大值——衰减 20% 后不再下降
            return MathF.Max(current, _stealSessionPeakAlert * 0.8f);
        }

        /// <summary>
        /// 浮标线速度：基准 260 ×(1 − Roguery/300×25%)（老手手稳，纯"手"通道，不动宽度域）
        /// ×撬锁 pin 递进 1.15ⁿ。恒定不挂钩警戒——保住肌肉记忆。
        /// </summary>
        private float GetCursorSpeed()
        {
            float rog = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0f;
            float speed = CursorBaseSpeed * (1f - MathF.Clamp(rog / RoguerySkillCap, 0f, 1f) * RogueryCursorSlowMax);
            if (_mode == StealBarMode.Lockpick)
                speed *= MathF.Pow(PinSpeedGrowth, _currentPin);
            return speed;
        }

        /// <summary>
        /// 减法宽度模型（贡献量可视化）：基础宽（撬锁 ×0.85ⁿ）
        /// 左端扣警戒损失、右端扣物品损失，剩余为有效判定区。
        /// 有效区下限 = 完美区宽（12px）——钳满时每次命中即完美（极限难度 = 全或无）。
        /// </summary>
        private void RecalcZoneSize()
        {
            float alert = GetCurrentAlert();
            // 扒窃/抓动物共用基础宽（动物体型定价复用物品右扣通道）；撬锁独立基础宽 ×0.85ⁿ
            float baseW = _mode == StealBarMode.Lockpick ? BaseZoneWidthLockpick : BaseZoneWidthPickpocket;
            if (_mode == StealBarMode.Lockpick)
                baseW *= MathF.Pow(PinWidthDecay, _currentPin);
            _baseWidthPx = baseW;
            _alertLossPx = baseW * AlertWidthImpact * MathF.Clamp(alert / AlertWidthMax, 0f, 1f);
            _itemLossPx = _mode == StealBarMode.Lockpick
                ? 0f
                : baseW * MathF.Max(0f, 1f - _itemTierFactor);
            _effWidthPx = MathF.Max(baseW - _alertLossPx - _itemLossPx, PerfectHalfWidth * 2f);
        }

        /// <summary>Cautious(alert≥1.0) 起子横条正弦游弋：缓入缓出，端点减速=可抓节奏窗口。</summary>
        private void UpdateDrift(float dt)
        {
            float alert = GetCurrentAlert();
            if (alert >= 1.0f)
            {
                // 铁律动态封顶：技能减速浮标后，游动同步压到 浮标/2.5 以下，双动体追踪不退化成运气
                float driftCap = MathF.Min(DriftMaxSpeed, GetCursorSpeed() / CursorDriftMinRatio);
                float peakSpeed = MathF.Min(DriftBaseSpeed + (alert - 1f) * DriftAlertSpeed, driftCap);
                float ampPx = DriftAmplitudeNorm * BarWidth;
                _driftPhase += dt * (peakSpeed / ampPx); // 角频率 = 峰值线速度 / 幅度
                _zoneCenter = _baseZoneCenter + MathF.Sin(_driftPhase) * DriftAmplitudeNorm;
            }
            else
            {
                _zoneCenter = _baseZoneCenter;
                _driftPhase = 0f;
            }
            // 整个组合条（基础+两扣+有效+完美）不出父条边界
            float halfW = _baseWidthPx / BarWidth / 2f;
            _zoneCenter = MathF.Clamp(_zoneCenter, halfW, 1f - halfW);
        }

        /// <summary>新回合/新 pin 时给目标区换个位置（条内随机，留出边距）。</summary>
        private void NewZonePosition()
        {
            float halfW = _baseWidthPx / BarWidth / 2f;
            float margin = halfW + 0.03f;
            _baseZoneCenter = margin + (float)_random.NextDouble() * (1f - 2f * margin);
            _zoneCenter = _baseZoneCenter;
            _driftPhase = 0f;
        }

        // ═══════════════════════ 出手（空格回调） ═══════════════════════

        public void ExecuteAttempt()
        {
            if (CloseReason != StealBarCloseReason.None) return;
            // 闪现期间不吞输入——允许连续出手的快手感，闪现直接被打断重计
            if (_mode == StealBarMode.Pickpocket) AttemptPickpocket();
            else if (_mode == StealBarMode.Lockpick) AttemptLockpick();
            else AttemptAnimal();
        }

        /// <summary>命中判定以「有效判定区」为准：中心 = 基础区左缘 + 左扣 + 有效半宽。</summary>
        private bool HitTest(out bool perfect)
        {
            float cursorPx = _cursorPos * BarWidth;
            float effCenterPx = _zoneCenter * BarWidth - _baseWidthPx / 2f + _alertLossPx + _effWidthPx / 2f;
            float deltaPx = MathF.Abs(cursorPx - effCenterPx);
            perfect = deltaPx <= PerfectHalfWidth;
            return deltaPx <= _effWidthPx / 2f;
        }

        private void AttemptPickpocket()
        {
            if ((!_pendingSlot.HasValue && !_pendingIsPurse) || _target == null)
            {
                // 本地化：目标被摸空提示
                FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_nothing_left", "There's nothing left to steal from them."), ZoneColorFail, MsgColorFail);
                return;
            }

            var brain = AgentAIController.GetBrainForAgent(_target);

            if (HitTest(out bool perfect))
            {
                // 负重检查：物品入袋前确认 party 装得下（发布前新增）
                if (MobileParty.MainParty != null
                    && MobileParty.MainParty.ItemRoster.TotalWeight >= MobileParty.MainParty.InventoryCapacity)
                {
                    // 本地化：party 负重已满，无法再偷
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_ui_steal_msg_overburdened",
                        "Your party is overburdened and cannot carry any more."), Colors.Red));
                    return; // 不转移物品，等玩家下次出手
                }

                if (_pendingIsPurse)
                {
                    // 钱袋独立偷窃：命中才偷到钱（金钱=特殊物品，不再顺手白摸）
                    int gold = StealManager.StealPurseGold(_target);
                    if (gold <= 0)
                    {
                        // 本地化：摸空提示
                        FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_grabbed_empty", "You grab nothing."), ZoneColorFail, MsgColorFail);
                    }
                    else if (perfect)
                    {
                        // 完美窃取：微量脉冲——NPC "隐约觉得不对"（发布前平衡：0→0.1）
                        brain?.AddAlert(PlayerActionType.Steal, 0.1f);
                        // 本地化：完美窃取钱袋消息
                        FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_perfect_gold", ("GOLD", gold.ToString())), ZoneColorPerfect, MsgColorPerfect);
                        DebugLogger.Log($"[StealBar] 完美窃取钱袋 {gold} 第纳尔 ← {_target.Name}（微量脉冲 0.1）");
                    }
                    else
                    {
                        brain?.SetPulseTarget(PlayerActionType.Steal, _target?.Name?.ToString(), $"{gold} 第纳尔", _target?.Index ?? -1); // lwn-ignore: A (debug label)
                        brain?.AddAlert(PlayerActionType.Steal, NormalHitVictimAlert);
                        PulsePickpocketWitnesses("gold", $"{gold} 第纳尔"); // lwn-ignore: A (debug label)
                        // 本地化：窃取钱袋得手消息
                        FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_gold_short", ("GOLD", gold.ToString())), ZoneColorSuccess, MsgColorSuccess);
                        DebugLogger.Log($"[StealBar] 窃取钱袋 {gold} 第纳尔 ← {_target.Name}（受害者+{NormalHitVictimAlert}，目击者+3.0）");
                    }
                }
                else
                {
                    // 预取物品 ID：StealSpecificItem 会从 agent 身上移除物品
                    string itemId = _target.SpawnEquipment[_pendingSlot.Value].Item?.StringId;
                    string itemName = StealManager.StealSpecificItem(_target, _pendingSlot.Value);
                    if (string.IsNullOrEmpty(itemName))
                    {
                        // 本地化：摸空提示
                        FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_grabbed_empty", "You grab nothing."), ZoneColorFail, MsgColorFail);
                    }
                    else if (perfect)
                    {
                        // 完美窃取：微量脉冲——NPC "隐约觉得不对"（发布前平衡：0→0.1）
                        brain?.AddAlert(PlayerActionType.Steal, 0.1f);
                        // 本地化：完美窃取物品消息
                        FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_perfect_item", ("ITEM", itemName)), ZoneColorPerfect, MsgColorPerfect);
                        DebugLogger.Log($"[StealBar] 完美窃取 {itemName} ← {_target.Name}（微量脉冲 0.1）");
                    }
                    else
                    {
                        brain?.SetPulseTarget(PlayerActionType.Steal, _target?.Name?.ToString(), itemName, _target?.Index ?? -1);
                        brain?.AddAlert(PlayerActionType.Steal, NormalHitVictimAlert);
                        PulsePickpocketWitnesses(itemId ?? itemName, itemName);
                        // 本地化：窃取物品得手消息
                        FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_item", ("ITEM", itemName)), ZoneColorSuccess, MsgColorSuccess);
                        DebugLogger.Log($"[StealBar] 窃取 {itemName} ← {_target.Name}（受害者+{NormalHitVictimAlert}，目击者+3.0）");
                    }
                }
                NextPickpocketRound();
            }
            else
            {
                brain?.AddAlert(PlayerActionType.Steal, MissVictimAlert);
                // 本地化：出手失误提示
                FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_slipped", "Your hand slipped!"), ZoneColorFail, MsgColorFail);
                DebugLogger.Log($"[StealBar] 扒窃失误 ← {_target.Name}（+{MissVictimAlert} 警戒）");
            }
        }

        /// <summary>抓动物：命中 → View 接 CompleteAnimalSteal；手滑 → 惊叫逃跑（目击者脉冲/围堵走 StealManager）。一次出手定胜负。</summary>
        private void AttemptAnimal()
        {
            if (_target == null || !_target.IsActive())
            {
                CloseReason = StealBarCloseReason.TargetGone;
                return;
            }

            if (HitTest(out bool perfect))
            {
                // 本地化：抓住动物提示（完美/普通）
                FlashResult(perfect ? LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_caught_animal_perfect", ("NAME", _animalName)) : LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_caught_animal", ("NAME", _animalName)),
                    perfect ? ZoneColorPerfect : ZoneColorSuccess,
                    perfect ? MsgColorPerfect : MsgColorSuccess);
                DebugLogger.Log($"[StealBar] 抓住 {_animalName}{(perfect ? "（完美）" : "")}");
                CloseReason = StealBarCloseReason.AnimalCaught;
            }
            else
            {
                StealManager.OnAnimalStruggleFlee(_target, _animalName);
                // 本地化：抓动物手滑提示
                FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_animal_slipped", ("NAME", _animalName)), ZoneColorFail, MsgColorFail);
                DebugLogger.Log($"[StealBar] 抓 {_animalName} 手滑 → 惊叫逃跑");
                CloseReason = StealBarCloseReason.AnimalFled;
            }
        }

        private void AttemptLockpick()
        {
            if (HitTest(out _))
            {
                if (_currentPin < Pins.Count)
                    Pins[_currentPin].IsUnlocked = true;
                _currentPin++;

                if (_currentPin >= _pinCount)
                {
                    // 本地化：撬锁完成提示
                    FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_lock_open", "Click — the lock opens!"), ZoneColorSuccess, MsgColorSuccess);
                    DebugLogger.Log("[StealBar] 撬锁完成");
                    CloseReason = StealBarCloseReason.Completed;
                    return;
                }
                // 本地化：簧片解开提示
                FlashResult(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_pin_open", ("PIN", _currentPin.ToString())), ZoneColorSuccess, MsgColorSuccess);
                NewZonePosition(); // 宽度/速度在 UpdateFrame 按新 pin 重算
            }
            else
            {
                // 本地化：撬棍滑脱提示
                FlashResult(LWNTextHelper.ResolveText("LWN_ui_steal_msg_crowbar_slip", "The crowbar slips!"), ZoneColorFail, MsgColorFail);
                // 噪音脉冲：当前能看见玩家的观察者警戒 +0.5（节流防连按刷爆）
                float now = Mission.Current?.CurrentTime ?? 0f;
                if (now - _lastNoiseTime >= NoisePulseCooldown)
                {
                    _lastNoiseTime = now;
                    var witnesses = StealManager.GetWitnesses(Agent.Main, null, 15f);
                    foreach (var w in witnesses)
                        AgentAIController.GetBrainForAgent(w)?.AddAlert(PlayerActionType.Steal, NoiseWitnessAlert);
                    if (witnesses.Count > 0)
                        DebugLogger.Log($"[StealBar] 撬锁噪音 → {witnesses.Count} 名目击者警戒脉冲");
                }
            }
        }

        // ═══════════════════════ Pickpocket 回合管理 ═══════════════════════

        /// <summary>随机预摸下一件：盲盒预览（类型+重量档，不给确切名字）+ 风险定价。钱袋作为独立目标入池。</summary>
        private void NextPickpocketRound()
        {
            _pendingSlot = StealManager.GetRandomStealableItemIndex(_target);
            _pendingIsPurse = false;

            // 钱袋进盲盒：身上有钱时有概率摸到（装备摸空后有钱袋必摸钱袋）——金钱独立偷窃目标
            if (StealManager.HasPurseGold(_target)
                && (!_pendingSlot.HasValue || _random.NextDouble() < PurseChance))
            {
                _pendingIsPurse = true;
                _pendingSlot = null;
                _itemTierFactor = 1f;   // 钱袋轻巧但系在腰间，标准难度
                // 本地化：摸到钱袋预览
                PreviewText = LWNTextHelper.ResolveText("LWN_ui_steal_preview_purse", "You feel a heavy purse.");
                return;
            }

            if (!_pendingSlot.HasValue)
            {
                // 本地化：目标被摸空预览
                PreviewText = LWNTextHelper.ResolveText("LWN_ui_steal_msg_nothing_left", "There's nothing left to steal from them.");
                _itemTierFactor = 1f;
                CloseReason = StealBarCloseReason.NothingLeft;  // 摸空 → View 自动收口 + 提示，不再让玩家对着空口袋
                return;
            }

            var element = _target.SpawnEquipment[_pendingSlot.Value];
            var item = element.Item;
            if (item == null)
            {
                // 本地化：摸到说不清物品预览
                PreviewText = LWNTextHelper.ResolveText("LWN_ui_steal_preview_strange", "You touch something you can't quite make out.");
                _itemTierFactor = 1f;
                return;
            }

            // 重量 = 物理难度；价值 = 贪欲旋钮（都只影响宽度，不影响任何速度）
            float weightFactor = item.Weight < 2f ? 1.10f : item.Weight > 8f ? 0.60f : 0.90f;
            float valueFactor = item.Value < 50 ? 1.10f : item.Value > 500 ? 0.65f : 0.90f;
            _itemTierFactor = weightFactor * valueFactor;

            // 本地化：盲盒物品重量描述
            string weightDesc = item.Weight < 2f ? LWNTextHelper.ResolveText("LWN_ui_steal_weight_light", "light") : item.Weight > 8f ? LWNTextHelper.ResolveText("LWN_ui_steal_weight_heavy", "heavy") : LWNTextHelper.ResolveText("LWN_ui_steal_weight_somewhat", "weighty");
            string typeDesc = GetTouchTypeDesc(item.Type);
            // 本地化：盲盒物品预览（重量+类型）
            PreviewText = LWNTextHelper.ResolveCompound("LWN_ui_steal_preview_weigh", ("WEIGHT", weightDesc), ("TYPE", typeDesc));
        }

        private static string GetTouchTypeDesc(ItemObject.ItemTypeEnum type)
        {
            switch (type)
            {
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    // 本地化：盲盒物品类型名（武器）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_weapon", "weapon");
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                    // 本地化：盲盒物品类型名（远程武器）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_ranged", "ranged weapon");
                case ItemObject.ItemTypeEnum.HeadArmor:
                    // 本地化：盲盒物品类型名（头盔）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_helmet", "helmet");
                case ItemObject.ItemTypeEnum.BodyArmor:
                    // 本地化：盲盒物品类型名（上身衣着）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_body", "garment");
                case ItemObject.ItemTypeEnum.LegArmor:
                    // 本地化：盲盒物品类型名（下身着装）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_leg", "legwear");
                case ItemObject.ItemTypeEnum.HandArmor:
                    // 本地化：盲盒物品类型名（手部护具）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_hand", "gloves");
                case ItemObject.ItemTypeEnum.Cape:
                    // 本地化：盲盒物品类型名（披风）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_cape", "cape");
                case ItemObject.ItemTypeEnum.Shield:
                    // 本地化：盲盒物品类型名（盾牌）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_shield", "shield");
                case ItemObject.ItemTypeEnum.Horse:
                    // 本地化：盲盒物品类型名（坐骑）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_mount", "mount");
                case ItemObject.ItemTypeEnum.Goods:
                    // 本地化：盲盒物品类型名（值钱货）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_goods", "valuables");
                default:
                    // 本地化：盲盒物品类型名（小物件）
                    return LWNTextHelper.ResolveText("LWN_ui_steal_type_small", "small trinket");
            }
        }

        /// <summary>结果反馈双通道：子横条颜色闪烁（条上即时，1.2s 回金）+ DisplayMessage 文本（信息详情）。</summary>
        private void FlashResult(string text, string zoneColor, Color msgColor)
        {
            ZoneColor = zoneColor;
            _resultFlashTimer = ResultFlashSeconds;
            InformationManager.DisplayMessage(new InformationMessage(text, msgColor));
        }

        /// <summary>
        /// 黄区偷窃得手后，向周围目击者发送警觉脉冲（+3.0）。
        /// 受害者（在身后看不见玩家）仍只受 NormalHitVictimAlert(0.35)。
        /// 目击者能亲眼看见玩家的手伸进别人口袋 → 立即确认偷窃行为 → 记录证词进 PendingWorldEvent。
        /// </summary>
        private void PulsePickpocketWitnesses(string itemId, string itemName)
        {
            var witnesses = StealManager.GetWitnesses(Agent.Main, _target, maxDistance: 15f);
            if (witnesses.Count == 0) return;

            string victimName = _target?.Name?.ToString();
            var heroIds = new List<string>();
            var templateCounts = new Dictionary<string, int>();

            foreach (var w in witnesses)
            {
                var wBrain = AgentAIController.GetBrainForAgent(w);
                if (wBrain == null) continue;

                wBrain.SetPulseTarget(PlayerActionType.Steal, victimName, itemName);
                wBrain.AddAlert(PlayerActionType.Steal, 3.0f);

                var hero = (w.Character as CharacterObject)?.HeroObject;
                if (hero != null)
                    heroIds.Add(hero.StringId);
                else if (w.Character != null)
                {
                    string tid = w.Character.StringId;
                    templateCounts.TryGetValue(tid, out int cnt);
                    templateCounts[tid] = cnt + 1;
                }
            }

            if (heroIds.Count > 0 || templateCounts.Count > 0)
            {
                AgentAIController.Instance?.RegisterTheftWitnesses(
                    heroIds, templateCounts, itemId, itemName, targetName: victimName);
                DebugLogger.Log($"[StealBar] 目击者脉冲: {heroIds.Count}H + {templateCounts.Values.Sum()}T 看见偷窃 {itemName}，+3.0 警戒");
            }
        }

        // ═══════════════════════ 命令 ═══════════════════════

        /// <summary>收手按钮 → 静默关闭（无失败提示）。</summary>
        public void ExecuteLeave()
        {
            _closeAction?.Invoke();
        }

        // ═══════════════════════ 数据绑定属性 ═══════════════════════

        private float _cursorMarginLeft;
        private float _baseMarginLeft;
        private float _baseWidth;
        private float _alertLossWidth;
        private float _itemLossMarginLeft;
        private float _itemLossWidth;
        private float _zoneMarginLeft;
        private float _zoneWidth;
        private float _perfectMarginLeft;
        private string _zoneColor = ZoneColorNormal;
        private string _titleText;
        private string _attemptButtonText;
        private string _leaveButtonText;
        private string _previewText;
        private string _ruleText = "";
        private string _cursorZoneText = "";
        private string _cursorZoneColor = HintColorOutside;
        private bool _isPickpocketMode;
        private bool _isLockpickMode;
        private MBBindingList<StealPinVM> _pins;

        [DataSourceProperty]
        public float CursorMarginLeft
        {
            get => _cursorMarginLeft;
            set { if (value != _cursorMarginLeft) { _cursorMarginLeft = value; OnPropertyChangedWithValue(value, nameof(CursorMarginLeft)); } }
        }

        [DataSourceProperty]
        public float BaseMarginLeft
        {
            get => _baseMarginLeft;
            set { if (value != _baseMarginLeft) { _baseMarginLeft = value; OnPropertyChangedWithValue(value, nameof(BaseMarginLeft)); } }
        }

        [DataSourceProperty]
        public float BaseWidth
        {
            get => _baseWidth;
            set { if (value != _baseWidth) { _baseWidth = value; OnPropertyChangedWithValue(value, nameof(BaseWidth)); } }
        }

        [DataSourceProperty]
        public float AlertLossWidth
        {
            get => _alertLossWidth;
            set { if (value != _alertLossWidth) { _alertLossWidth = value; OnPropertyChangedWithValue(value, nameof(AlertLossWidth)); } }
        }

        [DataSourceProperty]
        public float ItemLossMarginLeft
        {
            get => _itemLossMarginLeft;
            set { if (value != _itemLossMarginLeft) { _itemLossMarginLeft = value; OnPropertyChangedWithValue(value, nameof(ItemLossMarginLeft)); } }
        }

        [DataSourceProperty]
        public float ItemLossWidth
        {
            get => _itemLossWidth;
            set { if (value != _itemLossWidth) { _itemLossWidth = value; OnPropertyChangedWithValue(value, nameof(ItemLossWidth)); } }
        }

        [DataSourceProperty]
        public float ZoneMarginLeft
        {
            get => _zoneMarginLeft;
            set { if (value != _zoneMarginLeft) { _zoneMarginLeft = value; OnPropertyChangedWithValue(value, nameof(ZoneMarginLeft)); } }
        }

        [DataSourceProperty]
        public float ZoneWidth
        {
            get => _zoneWidth;
            set { if (value != _zoneWidth) { _zoneWidth = value; OnPropertyChangedWithValue(value, nameof(ZoneWidth)); } }
        }

        [DataSourceProperty]
        public float PerfectMarginLeft
        {
            get => _perfectMarginLeft;
            set { if (value != _perfectMarginLeft) { _perfectMarginLeft = value; OnPropertyChangedWithValue(value, nameof(PerfectMarginLeft)); } }
        }

        [DataSourceProperty]
        public string ZoneColor
        {
            get => _zoneColor;
            set { if (value != _zoneColor) { _zoneColor = value; OnPropertyChangedWithValue(value, nameof(ZoneColor)); } }
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, nameof(TitleText)); } }
        }

        [DataSourceProperty]
        public string AttemptButtonText
        {
            get => _attemptButtonText;
            set { if (value != _attemptButtonText) { _attemptButtonText = value; OnPropertyChangedWithValue(value, nameof(AttemptButtonText)); } }
        }

        [DataSourceProperty]
        public string LeaveButtonText
        {
            get => _leaveButtonText;
            set { if (value != _leaveButtonText) { _leaveButtonText = value; OnPropertyChangedWithValue(value, nameof(LeaveButtonText)); } }
        }

        /// <summary>按当前输入设备刷新按钮文本（构造时 + 键盘↔手柄切换时由 View 调用）。</summary>
        public void RefreshButtonTexts()
        {
            // 本地化：偷窃条出手按钮动词（撬锁/抓动物/出手）
            string verb = _mode == StealBarMode.Lockpick ? LWNTextHelper.ResolveText("LWN_ui_steal_verb_lockpick", "Pick") : _mode == StealBarMode.Animal ? LWNTextHelper.ResolveText("LWN_ui_steal_verb_catch", "Catch") : LWNTextHelper.ResolveText("LWN_ui_steal_verb_attempt", "Act");
            // 本地化：偷窃条出手按钮文本
            AttemptButtonText = LWNTextHelper.ResolveCompound("LWN_ui_steal_btn_attempt", ("KEY", ModInput.Glyph(ModInputAction.StealAttempt)), ("VERB", verb));
            // 本地化：偷窃条收手按钮文本
            LeaveButtonText = LWNTextHelper.ResolveCompound("LWN_ui_steal_btn_leave", ("KEY", ModInput.Glyph(ModInputAction.StealLeave)));
        }

        [DataSourceProperty]
        public string PreviewText
        {
            get => _previewText;
            set { if (value != _previewText) { _previewText = value; OnPropertyChangedWithValue(value, nameof(PreviewText)); } }
        }

        /// <summary>规则行①（固定）：区域语义总述，构造时按模式设一次。</summary>
        [DataSourceProperty]
        public string RuleText
        {
            get => _ruleText;
            set { if (value != _ruleText) { _ruleText = value; OnPropertyChangedWithValue(value, nameof(RuleText)); } }
        }

        /// <summary>规则行②（动态）：浮标脚下区域的含义+可否出手。</summary>
        [DataSourceProperty]
        public string CursorZoneText
        {
            get => _cursorZoneText;
            set { if (value != _cursorZoneText) { _cursorZoneText = value; OnPropertyChangedWithValue(value, nameof(CursorZoneText)); } }
        }

        /// <summary>规则行②文本色（hex），跟随浮标所在区域。</summary>
        [DataSourceProperty]
        public string CursorZoneColor
        {
            get => _cursorZoneColor;
            set { if (value != _cursorZoneColor) { _cursorZoneColor = value; OnPropertyChangedWithValue(value, nameof(CursorZoneColor)); } }
        }

        [DataSourceProperty]
        public bool IsPickpocketMode
        {
            get => _isPickpocketMode;
            set { if (value != _isPickpocketMode) { _isPickpocketMode = value; OnPropertyChangedWithValue(value, nameof(IsPickpocketMode)); } }
        }

        [DataSourceProperty]
        public bool IsLockpickMode
        {
            get => _isLockpickMode;
            set { if (value != _isLockpickMode) { _isLockpickMode = value; OnPropertyChangedWithValue(value, nameof(IsLockpickMode)); } }
        }

        [DataSourceProperty]
        public MBBindingList<StealPinVM> Pins
        {
            get => _pins;
            set { if (value != _pins) { _pins = value; OnPropertyChangedWithValue(value, nameof(Pins)); } }
        }
    }
}
