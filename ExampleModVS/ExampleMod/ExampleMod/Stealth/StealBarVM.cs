using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>偷窃条小游戏模式：扒窃（偷人）/ 撬锁（保管箱）。</summary>
    public enum StealBarMode { Pickpocket, Lockpick }

    /// <summary>偷窃条关闭原因，View 轮询消费。</summary>
    public enum StealBarCloseReason
    {
        None,           // 未请求关闭
        TargetGone,     // 目标走开/死亡 → 强制收手（不算被发现）
        Alarmed,        // 受害者警觉拉满/质问锁被占用 → 质问机器接管
        Completed,      // 撬锁全部 pin 解开 → View 接开箱 Inquiry
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
        private const float BaseZoneWidthPickpocket = 110f;     // 偷人基础子横条宽 px
        private const float BaseZoneWidthLockpick = 80f;        // 撬锁基础子横条宽 px
        private const float PerfectHalfWidth = 6f;              // 金区半宽（全宽 12px，兼作有效区下限）
        private const float CursorBaseSpeed = 260f;             // 浮标速度 px/s（恒定，不挂钩警戒）
        private const float PinWidthDecay = 0.85f;              // 撬锁第 n pin 宽度 ×0.85ⁿ
        private const float PinSpeedGrowth = 1.15f;             // 撬锁第 n pin 浮标速度 ×1.15ⁿ
        private const float AlertWidthMax = 2.2f;               // 警戒归一化分母 clamp(alert/2.2)
        private const float AlertWidthImpact = 0.75f;           // 警戒对基础宽的最大左扣比例 75%
        private const float RoguerySkillCap = 300f;             // 流氓技能满值（减速归一化分母）
        private const float RogueryCursorSlowMax = 0.25f;       // 满技能浮标减速 25%（技能走"手"通道，不污染宽度域）
        private const float CursorDriftMinRatio = 2.5f;         // 铁律：浮标速度 ≥ 游动 2.5 倍（游动动态封顶保证）
        private const float DriftAmplitudeNorm = 0.06f;         // Cautious 游动幅度 ±6% 条宽
        private const float DriftBaseSpeed = 55f;               // 游动峰值速度 @alert=1.0（px/s）
        private const float DriftAlertSpeed = 45f;              // 警戒每 +1（>1 部分）游动 +45px/s
        private const float DriftMaxSpeed = 100f;               // 游动峰值速度上限 ≈浮标 0.38×
        private const float MaxInteractDistance = 4.5f;         // 目标走开强制收手距离
        private const float ResultFlashSeconds = 1.2f;          // 结果闪现时长
        private const float NoisePulseCooldown = 0.5f;          // 撬锁噪音脉冲节流（防连按刷爆）
        private const float NormalHitVictimAlert = 0.35f;       // 普通命中：受害者警戒脉冲
        private const float MissVictimAlert = 1.0f;             // 失误：受害者警戒脉冲
        private const float NoiseWitnessAlert = 0.5f;           // 撬锁失误：目击者警戒脉冲

        // 子横条闪烁颜色（绑定 XML 用 hex 字符串）
        private const string ZoneColorNormal = "#D4AF37FF";     // 常态金
        private const string ZoneColorSuccess = "#44DD44FF";    // 成功绿闪
        private const string ZoneColorPerfect = "#7FFFD4FF";    // 金区命中青闪
        private const string ZoneColorFail = "#DD4444FF";       // 失败红闪

        // DisplayMessage 文本色（与闪烁同色系）
        private static readonly Color MsgColorSuccess = new Color(0.27f, 0.87f, 0.27f);   // #44DD44
        private static readonly Color MsgColorPerfect = new Color(0.50f, 1.00f, 0.84f);   // #7FFFD4
        private static readonly Color MsgColorFail = new Color(0.87f, 0.27f, 0.27f);      // #DD4444

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
        private EquipmentIndex? _pendingSlot;   // 本回合预摸的槽位
        private float _itemTierFactor = 1f;     // 预摸物品的双维定价（重量×价值）

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
            TitleText = $"正在偷:{target?.Name ?? "?"}";
            AttemptButtonText = "[空格] 出手";

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
            AttemptButtonText = "[空格] 撬";
            PreviewText = "";

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
        }

        /// <summary>目标走开/死亡/警觉拉满/质问锁 → 请求关闭。</summary>
        private void PollForceClose()
        {
            // 质问锁被占用（任何人进入 L3 质问）→ 两模式都收手，质问机器接管
            if (AgentBrain.ConfrontingBrain != null)
            {
                CloseReason = StealBarCloseReason.Alarmed;
                return;
            }

            if (_mode != StealBarMode.Pickpocket) return;

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

        /// <summary>扒窃读受害者警戒；撬锁无受害者——锁难度固定，目击压力由 NPC 自身警戒系统承担（IsUIOpen 累积 → Alarmed → 质问机器强制收手）。</summary>
        private float GetCurrentAlert()
        {
            if (_mode == StealBarMode.Lockpick || _target == null) return 0f;
            return AgentAIController.GetBrainForAgent(_target)?.AlertValue ?? 0f;
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
            float baseW = _mode == StealBarMode.Pickpocket ? BaseZoneWidthPickpocket : BaseZoneWidthLockpick;
            if (_mode == StealBarMode.Lockpick)
                baseW *= MathF.Pow(PinWidthDecay, _currentPin);
            _baseWidthPx = baseW;
            _alertLossPx = baseW * AlertWidthImpact * MathF.Clamp(alert / AlertWidthMax, 0f, 1f);
            _itemLossPx = _mode == StealBarMode.Pickpocket
                ? baseW * MathF.Max(0f, 1f - _itemTierFactor)
                : 0f;
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
            else AttemptLockpick();
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
            if (!_pendingSlot.HasValue || _target == null)
            {
                FlashResult("他身上已经没什么可偷的了。", ZoneColorFail, MsgColorFail);
                return;
            }

            var brain = AgentAIController.GetBrainForAgent(_target);

            if (HitTest(out bool perfect))
            {
                string itemName = StealManager.StealSpecificItem(_target, _pendingSlot.Value);
                if (string.IsNullOrEmpty(itemName))
                {
                    FlashResult("摸了个空。", ZoneColorFail, MsgColorFail);
                }
                else if (perfect)
                {
                    // 完美窃取：受害者零警戒脉冲（目击者 IsUIOpen 累积不受影响）
                    FlashResult($"神不知鬼不觉:{itemName}", ZoneColorPerfect, MsgColorPerfect);
                    DebugLogger.Log($"[StealBar] 完美窃取 {itemName} ← {_target.Name}（零警戒脉冲）");
                }
                else
                {
                    brain?.AddAlert(PlayerActionType.Steal, NormalHitVictimAlert);
                    FlashResult($"得手了:{itemName}", ZoneColorSuccess, MsgColorSuccess);
                    DebugLogger.Log($"[StealBar] 窃取 {itemName} ← {_target.Name}（+{NormalHitVictimAlert} 警戒）");
                }
                NextPickpocketRound();
            }
            else
            {
                brain?.AddAlert(PlayerActionType.Steal, MissVictimAlert);
                FlashResult("手滑了！", ZoneColorFail, MsgColorFail);
                DebugLogger.Log($"[StealBar] 扒窃失误 ← {_target.Name}（+{MissVictimAlert} 警戒）");
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
                    FlashResult("咔哒——锁开了！", ZoneColorSuccess, MsgColorSuccess);
                    DebugLogger.Log("[StealBar] 撬锁完成");
                    CloseReason = StealBarCloseReason.Completed;
                    return;
                }
                FlashResult($"第 {_currentPin} 个簧片开了…", ZoneColorSuccess, MsgColorSuccess);
                NewZonePosition(); // 宽度/速度在 UpdateFrame 按新 pin 重算
            }
            else
            {
                FlashResult("撬棍滑了！", ZoneColorFail, MsgColorFail);
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

        /// <summary>随机预摸下一件：盲盒预览（类型+重量档，不给确切名字）+ 风险定价。</summary>
        private void NextPickpocketRound()
        {
            _pendingSlot = StealManager.GetRandomStealableItemIndex(_target);
            if (!_pendingSlot.HasValue)
            {
                PreviewText = "他身上已经没什么可偷的了。";
                _itemTierFactor = 1f;
                return;
            }

            var element = _target.SpawnEquipment[_pendingSlot.Value];
            var item = element.Item;
            if (item == null)
            {
                PreviewText = "摸到一件说不清的东西。";
                _itemTierFactor = 1f;
                return;
            }

            // 重量 = 物理难度；价值 = 贪欲旋钮（都只影响宽度，不影响任何速度）
            float weightFactor = item.Weight < 2f ? 1.10f : item.Weight > 8f ? 0.60f : 0.90f;
            float valueFactor = item.Value < 50 ? 1.10f : item.Value > 500 ? 0.65f : 0.90f;
            _itemTierFactor = weightFactor * valueFactor;

            string weightDesc = item.Weight < 2f ? "轻巧" : item.Weight > 8f ? "沉甸甸" : "有些分量";
            string typeDesc = GetTouchTypeDesc(item.Type);
            PreviewText = $"摸到一件{weightDesc}的物件（像是{typeDesc}）";
        }

        private static string GetTouchTypeDesc(ItemObject.ItemTypeEnum type)
        {
            switch (type)
            {
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    return "武器";
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                    return "远程家伙";
                case ItemObject.ItemTypeEnum.HeadArmor:
                    return "头盔";
                case ItemObject.ItemTypeEnum.BodyArmor:
                    return "上身衣着";
                case ItemObject.ItemTypeEnum.LegArmor:
                    return "下身着装";
                case ItemObject.ItemTypeEnum.HandArmor:
                    return "手部护具";
                case ItemObject.ItemTypeEnum.Cape:
                    return "披风";
                case ItemObject.ItemTypeEnum.Shield:
                    return "盾牌";
                case ItemObject.ItemTypeEnum.Horse:
                    return "坐骑";
                case ItemObject.ItemTypeEnum.Goods:
                    return "值钱货";
                default:
                    return "小物件";
            }
        }

        /// <summary>结果反馈双通道：子横条颜色闪烁（条上即时，1.2s 回金）+ DisplayMessage 文本（信息详情）。</summary>
        private void FlashResult(string text, string zoneColor, Color msgColor)
        {
            ZoneColor = zoneColor;
            _resultFlashTimer = ResultFlashSeconds;
            InformationManager.DisplayMessage(new InformationMessage(text, msgColor));
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
        private string _previewText;
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
        public string PreviewText
        {
            get => _previewText;
            set { if (value != _previewText) { _previewText = value; OnPropertyChangedWithValue(value, nameof(PreviewText)); } }
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
