using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
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

        // 计数器
        private int _tickCounter = 0;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            Instance = this;

            _dataSource = new AgentHudCollectionVM();
            _layer = V.NewLayer(100);
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
                Vec3 agentPos = agent.Position;
                agentPos.z += agent.GetEyeGlobalHeight() + 0.1f;
                var screenPos = currentMissionScreen.SceneLayer.WorldPointToScreenPoint(agentPos);
                float pixelX = screenPos.x * screenWidth;
                float pixelY = screenPos.y * screenHeight;

                bool inFov = NpcSightSystem.IsPlayerSeeing(agent);

                // ── 第五层：警戒值更新（FOV 豁免，距离内始终追踪） ──
                // 🆕 从 AgentBrain 读警戒值（Phase 1 迁移：状态从 NpcSightSystem → AgentBrain）
                var brain = AgentAIController.GetBrainForAgent(agent);
                // 战场下警戒眼睛不显示（alertValue 强置 0）
                float alertValue = Settings.Instance.IsSightDisabled()
                    ? 0f
                    : (brain?.AlertValue ?? 0f);
                hud.AlertValue = alertValue;  // VM 内部 UpdateAlertVisuals 自决 ShowAlert

                // 警戒眼睛的屏幕位置（FOV 豁免：屏幕外 clamp 到边缘）
                if (hud.ShowAlert)
                {
                    if (!inFov)
                    {
                        hud.PosX = ClampToEdgeX(pixelX, screenWidth, uiScale, hud.BubbleWidth);
                        hud.PosY = ClampToEdgeY(pixelY, screenHeight, uiScale, hud.BubbleHeight);
                    }
                    else
                    {
                        hud.PosX = (pixelX * invUiScale) - (hud.BubbleWidth * 0.5f);
                        hud.PosY = (pixelY * invUiScale) - hud.BubbleHeight;
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
                            hud.PosX = (pixelX * invUiScale) - (hud.BubbleWidth * 0.5f);
                            hud.PosY = (pixelY * invUiScale) - hud.BubbleHeight;
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
                    hud.ShowName = false;   // 屏幕外不显示名字（防残留）

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

        /// <summary>静态快捷方法：让指定 Agent 说话</summary>
        public static void AgentSay(Agent agent, string text)
        {
            if (Mission.Current == null) return;
            if (agent == null) return;
            if (Instance == null) return;
            Instance.AddSpeech(agent, text);
        }

        /// <summary>静态快捷方法：按 StringId 让 Agent 说话</summary>
        public static void AgentSay(string agentStringId, string text)
        {
            if (Mission.Current == null) return;

            if (agentStringId == "player")
            {
                Instance.AddSpeech(Mission.Current.MainAgent, text);
                InformationManager.DisplayMessage(new InformationMessage($"让 {agentStringId} 说话了{text}"));
                return;
            }

            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.IsActive() && agent.Character != null && agent.Character.StringId == agentStringId)
                {
                    Instance.AddSpeech(agent, text);
                    InformationManager.DisplayMessage(new InformationMessage($"让 {agentStringId} 说话了{text}"));
                    return;
                }
            }

            InformationManager.DisplayMessage(new InformationMessage($"未找到 ID 为 {agentStringId} 的 NPC"));
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
