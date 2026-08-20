using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 顶部罗盘控制器（一期：Mission 内，老滚5 风格）。
    /// 用户裁定 2026-08-20：挂载进 InteractionMissionView（层序 8，AgentHud=5 之上、InteractArea=10 之下）；
    /// OnTick 由 InteractionMissionView 在 IsInteractionDisabled() 门控【之前】调用——战场/竞技场等场景罗盘照常工作。
    ///
    /// 行为：
    ///  - 刻度带：N/E/S/W 字母 + 45° 刻度线随相机 yaw 滚动（位置注入，窗口 ±100° 显示、超出隐藏，ClipContents 裁边）；
    ///  - 中心金色指针固定；
    ///  - 图标：视野 ±90° 内带任务的人物（原版 Alt 标记同判定 CampaignUIHelper.GetQuestStateOfHero + QuestMarkerBrushWidget 金色 !）
    ///    + 距离文本；范围外不显示不贴边（用户裁定）；最多 8 个按距离近优先防挤；
    ///  - 隐藏纪律：IM 完整模式打开（ImChatView.IsOpen && !IsCompactMode；缩略面板不隐藏，
    ///    2026-08-20 实机修正）或系统模态（ModInput.IsSystemModalActive()）→ 整个罗盘隐藏。
    ///  - 分频：agents 扫描每 30 帧、图标位置/距离每 2 帧、刻度/字母注入每帧。
    ///
    /// 角度约定（0° = 北 = +Y，顺时针为正，东 = +90°；与引擎世界坐标一致——vanilla
    /// MapCameraView PartyMoveUpKey: X+=sinθ, Y+=cosθ，θ=0 时北=+Y，反编译验证 2026-08-20）。
    /// 🔴 禁止用 Vec2.RotationInRadians 直接算方位角：引擎实现是 atan2(-x, y)（符号相反），
    /// 必须用 atan2(dx, dy)；yaw 同理用 atan2(fwd.X, fwd.Y)。实机 2026-08-20 镜像坑实录见 OnTick。
    /// 🔴 2026-08-20 勘误（反编译验证）：MissionScreen 无 CameraBearing（那是大地图 MapCameraView 的 protected 属性）；
    /// yaw 用 Mission.GetCameraFrame().rotation.f（forward）水平投影计算。
    /// </summary>
    public class CompassHud
    {
        // ── 带几何（UI 坐标，与 Compass.xml 对齐）──
        private const float BandHalfWidth = 500f;          // 带半宽（总宽 1000）
        private const float DegreesPerHalfBand = 90f;      // 半带 = 90°
        private const float ScaleShowWindowDeg = 100f;     // 刻度/字母显示窗口（±100°）
        private const float IconShowWindowDeg = 90f;       // 图标显示窗口（±90°，用户裁定）
        private const float LetterHalfWidth = 20f;         // 字母 TextWidget 40 宽 / 2
        private const float TickHalfWidth = 1f;            // 刻度线 2 宽 / 2
        private const float IconGroupHalfWidth = 30f;      // 图标组估算半宽（! 图标 25 + 间距 + 距离文本 ~30）
        private const int MaxIcons = 8;                    // 最多图标数（防挤）
        private const int ScanInterval = 30;               // agents 扫描分频（帧）
        private const int IconUpdateInterval = 2;          // 图标位置/距离分频（帧）
        private const int LayerOrder = 8;                  // 层序：AgentHud(5) 之上、InteractArea(10) 之下

        private CompassVM _vm;
        private GauntletLayer _layer;
        private MissionScreen _missionScreen;

        private int _scanCounter;
        private int _iconCounter;
        private int _letterRefreshCounter;
        private int _lastScanCount = -1;   // 扫描日志防刷屏：数量变化才打（2026-08-20）

        /// <summary>当前图标目标（排序后的 quest 人物，扫描时重建）。</summary>
        private readonly List<(Agent agent, int iconType)> _targets = new List<(Agent, int)>();

        /// <summary>距离文本格式串（本地化一次缓存；"%DIST%m"，刷新时手动替换占位符，免每帧 TextObject）。
        /// 🔴 占位符不能用 {DIST}：花括号会被 TextObject 解析器当变量吃掉（查全局变量表无果 → 空串），
        /// 显示成 "空的m"（实机 2026-08-20 踩坑）；%DIST% 形式 TextObject 不解析，原样透传。</summary>
        private string _distFormat = "%DIST%m";

        public bool IsInitialized => _layer != null;

        /// <summary>由 InteractionMissionView.OnMissionScreenInitialize 调用（建层 + LoadMovie + AddLayer）。</summary>
        public void OnInitialize(MissionScreen missionScreen)
        {
            if (missionScreen == null || _layer != null) return;
            _missionScreen = missionScreen;

            _vm = new CompassVM();
            _layer = V.NewLayer(LayerOrder, "CompassLayer");
            V.LoadMov(_layer, "Compass", _vm);
            missionScreen.AddLayer(_layer);

            // 本地化：LWN_compass_dist（双桶）
            _distFormat = LWNTextHelper.ResolveText("LWN_compass_dist", "%DIST%m");

            _scanCounter = 0;
            _iconCounter = 0;
            _letterRefreshCounter = 0;
            _targets.Clear();
            DebugLogger.Log("[Compass] initialized (order 8)");
        }

        /// <summary>由 InteractionMissionView.OnMissionTick 在互动门控【之前】每帧调用（战场可用）。</summary>
        public void OnTick(float dt)
        {
            if (_layer == null || _vm == null) return;
            var mission = Mission.Current;
            if (mission == null) return;

            // ── 隐藏纪律：MCM 总开关 + IM **完整模式**打开 / 系统模态（菜单、对话层等）→ 整个罗盘隐藏。
            // 🔴 2026-08-20（实机：缩略面板开启时头顶罗盘消失）：缩略模式是底部小面板、相机仍可控、
            // 不遮挡顶部罗盘 → 不隐藏；只有完整模式（大面板半模态）才隐藏。
            bool visible = Settings.Instance.ShowCompass
                && !(ImChatView.IsOpen && !ImChatView.IsCompactMode)
                && !ModInput.IsSystemModalActive();
            if (visible != _vm.IsVisible) _vm.IsVisible = visible;
            if (!visible) return;

            // ── 相机 yaw（forward 水平投影；世界方位角：0°=北=+Y，顺时针为正，东=+90°）──
            // 🔴 2026-08-20 勘误（反编译验证）：①引擎世界坐标 北=+Y（vanilla MapCameraView
            // PartyMoveUpKey: X+=sinθ, Y+=cosθ，θ=0 时北=+Y）；②Vec2.RotationInRadians 是
            // atan2(-x, y)（负号！），与罗盘字母角度约定（东=+90）符号相反——旧公式
            // yaw=atan2(-fwd.X, fwd.Y) + bearing=RotationInRadians 双符号错 → 面朝正北时
            // 图标左右镜像（西侧目标显示在东侧，实机 2026-08-20：NPC 说西、罗盘指东）。
            Vec3 fwd;
            try { fwd = mission.GetCameraFrame().rotation.f; }
            catch { return; }
            float yawDeg = (float)(Math.Atan2(fwd.X, fwd.Y) * 180.0 / Math.PI);

            // ── 每帧：刻度 + 字母位置注入 ──
            UpdateScale(yawDeg);

            // ── 分频：图标位置/距离每 2 帧 ──
            _iconCounter++;
            if (_iconCounter % IconUpdateInterval == 0)
                UpdateIcons(yawDeg);

            // ── 分频：agents 扫描每 30 帧 ──
            _scanCounter++;
            if (_scanCounter % ScanInterval == 0)
                ScanAgents(yawDeg);

            // ── 分频：方向字母每 60 帧（1 秒）重解析本地化文本 ──
            //（游戏内切语言不重建 VM，构造时缓存会滞留旧语言——实机 2026-08-20 英文版显示中文）
            _letterRefreshCounter++;
            if (_letterRefreshCounter % 60 == 0)
                _vm.RefreshLetterTexts();
        }

        /// <summary>由 InteractionMissionView.OnMissionScreenFinalize 调用。</summary>
        public void OnFinalize()
        {
            if (_layer != null)
            {
                _missionScreen?.RemoveLayer(_layer);
                _layer = null;
            }
            _vm = null;
            _missionScreen = null;
            _targets.Clear();
        }

        // ═══════════════════════════ 刻度/字母 ═══════════════════════════

        private void UpdateScale(float yawDeg)
        {
            // 刻度 8 根 + 字母 4 个，同一套 relAngle 计算
            foreach (var tick in _vm.TickItems)
                SetCompassTick(tick, yawDeg);
            foreach (var letter in _vm.LetterItems)
                SetCompassTick(letter, yawDeg);
        }

        private void SetCompassTick(CompassTickVM item, float yawDeg)
        {
            float rel = NormalizeDeg(item.AngleDeg - yawDeg);
            if (Math.Abs(rel) > ScaleShowWindowDeg)
            {
                item.SetCompass(item.PosX, false);
                return;
            }
            float x = BandHalfWidth + rel * (BandHalfWidth / DegreesPerHalfBand);
            // 中心对齐偏移（字母 40 宽 / 刻度 2 宽，由 Compass.xml 的 widget 尺寸决定）
            float half = item.Text.Length > 0 ? LetterHalfWidth : TickHalfWidth;
            item.SetCompass(x - half, true);
        }

        // ═══════════════════════════ 图标 ═══════════════════════════

        /// <summary>每 30 帧：扫描 quest 人物 → 按距离近优先取 8 → 重建图标列表。</summary>
        private void ScanAgents(float yawDeg)
        {
            var mission = Mission.Current;
            if (mission == null) return;
            if (!Settings.Instance.ShowCompassIcons)
            {
                if (_vm.IconItems.Count > 0) _vm.IconItems.Clear();
                return;
            }

            Vec3 camPos = _missionScreen?.CombatCamera?.Position ?? (Agent.Main?.Position ?? Vec3.Zero);

            _targets.Clear();
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsActive()) continue;
                if (agent == Agent.Main) continue;
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null) continue;

                // 原版 Alt 标记同判定：GetQuestStateOfHero 非空 → 有任务标记
                int flags = 0;
                try
                {
                    var quests = CampaignUIHelper.GetQuestStateOfHero(hero);
                    if (quests == null || quests.Count == 0) continue;
                    foreach (var q in quests)
                        flags |= (int)q.Item1;
                }
                catch { continue; }   // 铁律 3：LLM/campaign 层异常不崩（防御兜底）
                if (flags == 0) continue;

                // 单一位图标类型（QuestMarkerBrushWidget switch 只认精确值 1/2/4/8/16；优先级从高到低）
                int iconType = 0;
                if ((flags & 16) != 0) iconType = 16;
                else if ((flags & 8) != 0) iconType = 8;
                else if ((flags & 4) != 0) iconType = 4;
                else if ((flags & 2) != 0) iconType = 2;
                else if ((flags & 1) != 0) iconType = 1;

                _targets.Add((agent, iconType));
            }

            // 距离近优先，最多 8
            if (_targets.Count > 1)
                _targets.Sort((a, b) =>
                    a.agent.Position.DistanceSquared(camPos).CompareTo(b.agent.Position.DistanceSquared(camPos)));
            if (_targets.Count > MaxIcons)
                _targets.RemoveRange(MaxIcons, _targets.Count - MaxIcons);

            // 🔴 diff 增量同步（2026-08-20 修复感叹号闪烁：全量 Clear+Add 会让 Gauntlet 每 30 帧
            // 销毁重建全部图标 widget → 视觉上每 0.5s 闪一次）。只做：失效移除 / 新目标添加 /
            // 图标类型变更更新；列表稳定时零通知零重建。
            var icons = _vm.IconItems;

            // ① 移除失效条目：agent 消失 / 不再有任务标记（不在新扫描集）
            for (int i = icons.Count - 1; i >= 0; i--)
            {
                var icon = icons[i];
                bool stillTargeted = icon.TargetAgent != null && icon.TargetAgent.IsActive();
                if (stillTargeted)
                {
                    foreach (var (agent, _) in _targets)
                    {
                        if (agent == icon.TargetAgent) { stillTargeted = false; break; }
                    }
                }
                if (stillTargeted)
                    icons.RemoveAt(i);
            }

            // ② 添加新目标 + 复用条目更新 IconType（任务标记变化时）
            foreach (var (agent, iconType) in _targets)
            {
                CompassIconVM existing = null;
                foreach (var icon in icons)
                {
                    if (icon.TargetAgent == agent) { existing = icon; break; }
                }
                if (existing != null)
                {
                    if (existing.IconType != iconType) existing.IconType = iconType;
                }
                else
                {
                    icons.Add(new CompassIconVM(agent, iconType));
                }
            }

            // 🔴 2026-08-20（日志刷屏修复）：只打数量变化——每 30 帧全量打 = 0.5s 一条刷屏
            //（实机 18:33:34-35 连续 100+ 条）。数量不变 = 扫描无新变化，无需日志。
            if (_targets.Count != _lastScanCount)
            {
                DebugLogger.Log($"[Compass] scan: {_targets.Count} quest hero(es) on compass");
                _lastScanCount = _targets.Count;
            }
        }

        /// <summary>每 2 帧：刷新图标位置（relAngle → x）+ 距离文本；±90° 外隐藏。</summary>
        private void UpdateIcons(float yawDeg)
        {
            if (!Settings.Instance.ShowCompassIcons)
            {
                if (_vm.IconItems.Count > 0) _vm.IconItems.Clear();
                return;
            }

            Vec3 camPos = _missionScreen?.CombatCamera?.Position ?? (Agent.Main?.Position ?? Vec3.Zero);

            foreach (var icon in _vm.IconItems)
            {
                var agent = icon.TargetAgent;
                if (agent == null || !agent.IsActive())
                {
                    icon.SetCompass(icon.PosX, false, icon.DistanceText);
                    continue;
                }

                Vec2 delta = agent.Position.AsVec2 - camPos.AsVec2;
                // 世界方位角（0°=北，东=+90°）：atan2(dx,dy)——⚠️ 禁止用 delta.RotationInRadians，
                // 引擎实现是 atan2(-x, y)（符号与罗盘约定相反，实机 2026-08-20 镜像坑）
                float targetBearingDeg = (float)(Math.Atan2(delta.X, delta.Y) * 180.0 / Math.PI);
                float rel = NormalizeDeg(targetBearingDeg - yawDeg);

                if (Math.Abs(rel) > IconShowWindowDeg)
                {
                    icon.SetCompass(icon.PosX, false, icon.DistanceText);
                    continue;
                }

                float x = BandHalfWidth + rel * (BandHalfWidth / DegreesPerHalfBand);
                int dist = (int)agent.Position.Distance(camPos);
                string distText = _distFormat.Replace("%DIST%", dist.ToString());
                icon.SetCompass(x - IconGroupHalfWidth, true, distText);
            }
        }

        // ═══════════════════════════ 工具 ═══════════════════════════

        /// <summary>角度归一化到 [-180, 180]。</summary>
        private static float NormalizeDeg(float angle)
        {
            angle = angle % 360f;
            if (angle > 180f) angle -= 360f;
            else if (angle < -180f) angle += 360f;
            return angle;
        }

        // ═══════════════════════════ 控制台调试 ═══════════════════════════

        /// <summary>custom.compass_debug：打印当前 yaw / 图标数 / 各图标 relAngle 与距离（配合 Mission 实测）。</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("compass_debug", "custom")]
        public static string ExecuteCompassDebug(List<string> args)
        {
            var hud = InteractionMissionView.Instance?.CompassHud;
            if (hud == null || hud._vm == null)
                return "[Compass] not initialized (no mission)";

            var mission = Mission.Current;
            if (mission == null)
                return "[Compass] no mission";

            var sb = new System.Text.StringBuilder();
            try
            {
                Vec3 fwd = mission.GetCameraFrame().rotation.f;
                float yawDeg = (float)(Math.Atan2(fwd.X, fwd.Y) * 180.0 / Math.PI);
                sb.AppendLine($"[Compass] yaw={yawDeg:F1} (0=北=+Y 顺时针为正) visible={hud._vm.IsVisible} " +
                    $"setting={Settings.Instance.ShowCompass} icons={hud._vm.IconItems.Count}"); // lwn-ignore: A
                Vec3 camPos = hud._missionScreen?.CombatCamera?.Position ?? Vec3.Zero;
                foreach (var icon in hud._vm.IconItems)
                {
                    var agent = icon.TargetAgent;
                    if (agent == null) continue;
                    Vec2 delta = agent.Position.AsVec2 - camPos.AsVec2;
                    float bearingDeg = (float)(Math.Atan2(delta.X, delta.Y) * 180.0 / Math.PI);
                    float rel = NormalizeDeg(bearingDeg - yawDeg);
                    int dist = (int)agent.Position.Distance(camPos);
                    sb.AppendLine($"[Compass]   {agent.Name} bearing={bearingDeg:F1} rel={rel:F1} dist={dist}m visible={icon.IsVisible}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Compass] error: {ex.Message}");
            }
            DebugLogger.Log(sb.ToString());
            if (Settings.Instance.ShowDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(sb.ToString().Replace("\n", " | "), Colors.Yellow));
            return "";
        }
    }
}
