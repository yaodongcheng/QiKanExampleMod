using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using static System.Net.Mime.MediaTypeNames;

namespace LivingWorldNpcs
{
    public class AgentHudMissionView : MissionView
    {
        private AgentHudCollectionVM _dataSource;
        private GauntletLayer _layer;
        public static AgentHudMissionView Instance { get; private set; }

        // 性能设置：最大显示距离（米）
        private const float MaxDisplayDistance = 50f;
        private const float NearDistance = 15f;
        // 远处"听到"兜底：超过此距离且视野外 → 屏幕消息显示台词（AgentSay 内判断）
        private const float FarHearDistance = 30f;

        // 计数器
        private int _tickCounter = 0;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            Instance = this;

            _dataSource = new AgentHudCollectionVM();
            _layer = V.NewLayer(5); // 低层级，确保系统菜单（ESC/选项）等覆盖在上面
            _layer.LoadMovie("AgentHudNearby", _dataSource);
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            missionScreen.AddLayer(_layer);

            // 初始扫描延迟 3 秒
            _pendingInitialScan = true;
            _initialScanTimer = 0f;
        }

        // 延迟扫描标志
        private bool _pendingInitialScan = true;
        private float _initialScanTimer = 0f;
        private const float InitialScanDelay = 3f;

        private void ScanForNewAgents()
        {
            if (Mission.Current == null) return;

            foreach (var agent in Mission.Current.Agents)
            {
                if (agent.IsActive() && agent.Character != null && agent.IsHuman)
                {
                    EnsureHud(agent);
                }
            }
        }

        /// <summary>确保 Agent 有 HUD（延迟创建策略：只在有内容要显示时才创建 VM）</summary>
        public void EnsureHud(Agent agent)
        {
            if (agent == null) return;

            bool exists = false;
            foreach (var hud in _dataSource.Huds)
            {
                if (hud.TargetAgent == agent)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                var newHud = new AgentHudVM(agent);
                _dataSource.AddHud(newHud);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 初始扫描延迟
            if (_pendingInitialScan)
            {
                _initialScanTimer += dt;
                if (_initialScanTimer >= InitialScanDelay)
                {
                    _pendingInitialScan = false;
                    ScanForNewAgents();
                }
            }

            if (_dataSource == null || _dataSource.Huds.Count == 0) return;

            MissionScreen currentMissionScreen = ScreenManager.TopScreen as MissionScreen;
            if (currentMissionScreen == null) return;

            _tickCounter++;

            float screenWidth = Screen.RealScreenResolutionWidth;
            float screenHeight = Screen.RealScreenResolutionHeight;

            float uiScale = _layer.UIContext.Scale;
            float invUiScale = 1.0f / uiScale;

            List<int> removeIndices = null;

            for (int i = _dataSource.Huds.Count - 1; i >= 0; i--)
            {
                var hud = _dataSource.Huds[i];
                var agent = hud.TargetAgent;

                // ── 第一层：基础校验 ──
                if (agent == null || !agent.IsActive())
                {
                    if (removeIndices == null) removeIndices = new List<int>();
                    removeIndices.Add(i);
                    continue;
                }

                // ── 第二层：距离硬裁剪（50m） ──
                Vec3 cameraPos = currentMissionScreen.CombatCamera?.Position ?? agent.Position;
                float dist = agent.Position.Distance(cameraPos);
                if (dist > MaxDisplayDistance)
                {
                    if (hud.IsVisible) hud.IsVisible = false;
                    continue;
                }

                // ── 第三层：屏幕坐标计算 ──
                // XY 取 agent.Position（身体中心，不受头部晃动影响）
                // Z  取 GetEyeGlobalPosition()（动画骨骼高度，上楼时平滑无台阶跳跃）
                Vec3 agentPos = agent.Position;
                agentPos.z = agent.GetEyeGlobalPosition().z + 0.1f;
                var screenPos = currentMissionScreen.SceneLayer.WorldPointToScreenPoint(agentPos);
                float pixelX = screenPos.x * screenWidth;
                float pixelY = screenPos.y * screenHeight;

                bool inFov = NpcSightSystem.IsPlayerSeeing(agent);

                // ── 第五层：警戒值更新（FOV 豁免，距离内始终追踪） ──
                // 🆕 从 AgentBrain 读警戒值（Phase 1 迁移：状态从 NpcSightSystem → AgentBrain）
                var brain = AgentAIController.GetBrainForAgent(agent);
                // 警戒眼不显示（alertValue 强置 0）：
                //   战场 = IsInteractionDisabled()（Mission 级，Mode 在禁用列表）；
                //   战斗中 = brain.IsInCombat（个体级）——城镇等可互动场景里打起来的 NPC
                //   已不需要警戒指示（血条已表达敌意），警戒值等战斗结束再恢复
                bool inCombat = brain?.IsInCombat ?? false;
                float alertValue = (Settings.Instance.IsInteractionDisabled() || inCombat)
                    ? 0f
                    : (brain?.AlertValue ?? 0f);
                // 🔴 注入顺序纪律：先 AlertTargetIsPlayer 后 AlertValue（AlertValue setter 内部触发
                // UpdateAlertVisuals 读色系）——顺序反了警戒眼会晚一帧变错颜色
                hud.AlertTargetIsPlayer = brain?.AlertTargetIsPlayer ?? true;
                hud.AlertValue = alertValue;  // VM 内部 UpdateAlertVisuals 自决 ShowAlert

                // 警戒眼睛的屏幕位置（FOV 豁免：屏幕外 clamp 到边缘）
                if (hud.ShowAlert)
                {
                    if (!inFov)
                    {
                        hud.SnapPosition(
                            ClampToEdgeX(pixelX, screenWidth, uiScale, hud.BubbleWidth),
                            ClampToEdgeY(pixelY, screenHeight, uiScale, hud.BubbleHeight));
                    }
                    else
                    {
                        hud.SetTargetPosition(
                            (pixelX * invUiScale) - (hud.BubbleWidth * 0.5f),
                            (pixelY * invUiScale) - hud.BubbleHeight);
                    }
                }

                // 屏幕外且没有警戒值 → 跳过后续处理
                if (!inFov && !hud.ShowAlert)
                {
                    if (hud.IsVisible) hud.IsVisible = false;
                    continue;
                }

                // ── 第六层：FOV 内的常规元素（血条/说话/名字） ──
                if (inFov)
                {
                    // 分频更新
                    bool isClose = dist <= NearDistance;
                    int updateInterval = isClose ? 10 : 30;

                    if ((i + _tickCounter) % updateInterval == 0)
                    {
                        hud.UpdateLogic();
                    }

                    if (hud.IsVisible)
                    {
                        hud.UpdateFrame(dt);

                        if (!hud.ShowAlert)
                        {
                            hud.SetTargetPosition(
                                (pixelX * invUiScale) - (hud.BubbleWidth * 0.5f),
                                (pixelY * invUiScale) - hud.BubbleHeight);
                        }

                        float scale = MBMath.ClampFloat(50f / (dist + 5f), 0.5f, 1.5f);
                        hud.Scale = scale;
                    }
                }
                else
                {
                    // FOV 外 / 屏幕外：常规元素全部隐藏，警戒眼独立显示
                    hud.ShowHealth = false;
                    hud.ShowSpeech = false;
                    hud.ShowDamage = false;
                    hud.ShowName = false;           // 屏幕外不显示名字（防残留）
                    hud.ShowIntentDebug = false;    // 意图文本同样防残留（回 FOV 后由 UpdateLogic 重算）

                    if (!hud.ShowAlert)
                    {
                        if (hud.IsVisible) hud.IsVisible = false;
                    }
                    else
                    {
                        // 警戒眼 FOV 豁免：强制容器可见 + 默认缩放
                        hud.IsVisible = true;
                        hud.Scale = 1.0f;
                    }
                }
            }

            // 延迟批量移除
            if (removeIndices != null && removeIndices.Count > 0)
            {
                foreach (int idx in removeIndices)
                {
                    if (idx < _dataSource.Huds.Count)
                        _dataSource.Huds.RemoveAt(idx);
                }
            }
        }

        // ── 屏幕边缘 clamp 辅助 ──
        private float ClampToEdgeX(float pixelX, float screenWidth, float uiScale, float bubbleWidth)
        {
            float margin = 20f;
            float clampedX = MBMath.ClampFloat(pixelX, margin, screenWidth - margin);
            float uiX = (clampedX * (1.0f / uiScale)) - (bubbleWidth * 0.5f);
            // 最终保护：widget 不超出 margin 边界
            float minUiX = margin * (1.0f / uiScale);
            float maxUiX = (screenWidth - margin) * (1.0f / uiScale) - bubbleWidth;
            return MBMath.ClampFloat(uiX, minUiX, maxUiX);
        }

        private float ClampToEdgeY(float pixelY, float screenHeight, float uiScale, float bubbleHeight)
        {
            float margin = 20f;
            float clampedY = MBMath.ClampFloat(pixelY, margin, screenHeight - margin);
            float uiY = (clampedY * (1.0f / uiScale)) - (bubbleHeight * 0.5f);
            // 最终保护：widget 不超出 margin 边界
            float minUiY = margin * (1.0f / uiScale);
            float maxUiY = (screenHeight - margin) * (1.0f / uiScale) - bubbleHeight;
            return MBMath.ClampFloat(uiY, minUiY, maxUiY);
        }

        // ============================================================
        // 公开 API
        // ============================================================

        /// <summary>让 Agent 说话（原 AddSpeechBubble）</summary>
        public void AddSpeech(Agent agent, string text)
        {
            if (agent == null) return;

            AgentHudVM hud = null;
            foreach (var h in _dataSource.Huds)
            {
                if (h.TargetAgent == agent)
                {
                    hud = h;
                    break;
                }
            }

            if (hud != null)
            {
                hud.Speak(text);
            }
            else
            {
                var newHud = new AgentHudVM(agent);
                newHud.Speak(text);
                _dataSource.AddHud(newHud);
            }
        }

        /// <summary>静态快捷方法：让指定 Agent 说话。
        /// 🔴 2026-08-11 距离分层（用户裁定）：**远处说话根本不触发播放**——3D 冒泡挂在 agent 头上，
        /// 距玩家 &gt; FarHearDistance（30m）玩家看不见，创建 HUD + 逐帧更新纯浪费；
        /// nearby 频道同步只收可听半径内的冒泡（转发天然受限，无需二次过滤）。
        /// 分层：
        ///   ≤30m            → 3D 冒泡 + nearby 频道（视觉 + 频道）
        ///   &gt;30m 且视野外  → 只弹屏幕消息「远处传来声音」（听觉语义，既有兜底保留）
        ///   &gt;30m 但视野内  → 无声（原版语义：远处看得见但听不见，无字幕）
        /// 玩家自己的冒泡恒播放（距离 0）。远处跳过不影响逻辑——记忆写入/执行器在调用方（respond/计划）侧。</summary>
        /// <summary>
        /// 说话统一出口（所有路径汇聚：SpeechChannel 并联通道 / 对话回应 / 密令开场 / 招募 / 投降…）。
        /// 🔴 前因日志（2026-08-11）：入口处记录"谁在什么时候说了什么"——覆盖全部说话路径，
        /// 包括未走 SpeechChannel 的旧调用点（那些路径无 SpeechContext，前因为空属正常；
        /// 新体系 SpeechChannel 会传序列化好的前因串）。
        /// </summary>
        /// <param name="reason">前因（可空；SpeechChannel 传入，旧调用点省略）</param>
        public static void AgentSay(Agent agent, string text, string reason = null)
        {
            if (Mission.Current == null) return;
            if (agent == null) return;
            if (Instance == null) return;

            // 🔴 统一说话日志（近处/远处都打；前因可空）
            try { DebugLogger.Log($"[Say] {agent.Name}: {text}{(string.IsNullOrEmpty(reason) ? "" : " ← " + reason)}"); } catch { }

            // 🔴 距离分层前置：远处（> FarHearDistance）不冒泡（3D 冒泡玩家看不见，创建 HUD 纯浪费）
            bool isFar = Agent.Main != null && agent != Agent.Main && Agent.Main.IsActive()
                && agent.Position.Distance(Agent.Main.Position) > FarHearDistance;
            if (isFar)
            {
                // 🔴 2026-08-12（用户裁定）：远处说话不再弹屏幕消息（LWN_hud_far_say 退役）——
                // 直接进附近频道（消息流可回看，不打断当前操作）。视野内远处 = 原版无声语义（看得见听不见）。
                try { NearbyFeed.Forward(agent, text, force: true); } catch { }
                DebugLogger.Log($"[AgentSay] {agent.Name}（远处 {agent.Position.Distance(Agent.Main.Position):F0}m）: {text}");
                return;
            }

            Instance.AddSpeech(agent, text);
            // 🔴 §5.7 附近频道转发（场景内真实冒泡流入玩家 IM；同 sender 200ms 合并防刷屏）
            try { NearbyFeed.Forward(agent, text); } catch { }
        }

        /// <summary>静态快捷方法：按 StringId 让 Agent 说话（入口同样打 [Say] 统一说话日志）</summary>
        public static void AgentSay(string agentStringId, string text, string reason = null)
        {
            if (Mission.Current == null) return;

            if (agentStringId == "player")
            {
                try { DebugLogger.Log($"[Say] {agentStringId}: {text}{(string.IsNullOrEmpty(reason) ? "" : " ← " + reason)}"); } catch { }
                Instance.AddSpeech(Mission.Current.MainAgent, text);
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_hud_agent_said",
                        ("ID", agentStringId), ("TEXT", text))));
                return;
            }

            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.IsActive() && agent.Character != null && agent.Character.StringId == agentStringId)
                {
                    try { DebugLogger.Log($"[Say] {agent.Name}: {text}{(string.IsNullOrEmpty(reason) ? "" : " ← " + reason)}"); } catch { }
                    Instance.AddSpeech(agent, text);
                    if (Settings.Instance.ShowDebugMessages)
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_hud_agent_said",
                            ("ID", agentStringId), ("TEXT", text))));
                    return;
                }
            }

            if (Settings.Instance.ShowDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_hud_agent_not_found",
                    ("ID", agentStringId))));
        }

        // ============================================================
        // 控制台指令
        // ============================================================

        [CommandLineFunctionality.CommandLineArgumentFunction("agentHud_say", "custom")]
        public static string ExecuteAgentHudSay(List<string> args)
        {
            if (args.Count < 2)
            {
                return "param num not enough";
            }
            string agentStringId = args[0];
            string text = args[1];
            AgentSay(agentStringId, text);
            return "";
        }

        public override void OnMissionScreenFinalize()
        {
            MissionScreen.RemoveLayer(_layer);
            _layer = null;
            _dataSource = null;
            Instance = null;
            base.OnMissionScreenFinalize();
        }
    }
}
