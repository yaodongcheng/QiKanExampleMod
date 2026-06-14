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
    public class BubbleSayMissionView : MissionView
    {
        private BubbleSayNeaybyVM _dataSource;
        private GauntletLayer _layer;
        public static BubbleSayMissionView Instance { get; private set; }

        // 性能设置：最大显示距离（米）
        private const float MaxDisplayDistance = 30f;

        // 在类中定义一个计数器
        private int _tickCounter = 0;

        // 缓存 NpcSightSystem 引用，避免每帧 GetMissionBehavior
        private NpcSightSystem _sightSystem;


        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            Instance = this;

            _dataSource = new BubbleSayNeaybyVM();
            _layer = new GauntletLayer(100); // Layer 优先级
            _layer.LoadMovie("BubbleSayNearby", _dataSource);
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            missionScreen.AddLayer(_layer);

            // 初始扫描延迟 3 秒，由 OnMissionTick 在主线程执行，避免线程池操作 MBBindingList 导致 GauntletUI StackLayout 崩溃
            _pendingInitialScan = true;
            _initialScanTimer = 0f;
        }

        // 线程安全：DelayScan 的回调在线程池执行，不能直接操作 MBBindingList。
        // 改为设置标志位，由 OnMissionTick 在主线程执行实际的 ScanForNewAgents。
        private bool _pendingInitialScan = true;
        private float _initialScanTimer = 0f;
        private const float InitialScanDelay = 3f;

        private void ScanForNewAgents()
        {
            if (Mission.Current == null) return;

            // 遍历所有 Agent
            foreach (var agent in Mission.Current.Agents)
            {
                // 过滤条件：活着的、有人物信息的、是人类的（排除马匹）
                if (agent.IsActive() && agent.Character != null && agent.IsHuman)
                {
                    AddHealthBar(agent);
                }
            }
        }


        public void AddHealthBar(Agent agent)
        {
            if (agent == null) return;

           

            // 性能优化：使用 Any 替代 FirstOrDefault 防止不必要的对象创建，检查是否已存在
            // 注意：这里需要确保你的 BubbleSayVM 有 TargetAgent 属性供比对
            bool exists = false;
            foreach (var bubble in _dataSource.Bubbles)
            {
                if (bubble.TargetAgent == agent)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                var newBubble = new BubbleSayVM(agent);
                _dataSource.AddBubble(newBubble);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 初始扫描延迟（主线程安全，避免线程池操作 MBBindingList 与 GauntletUI ParallelUpdateLayouts 竞态）
            if (_pendingInitialScan)
            {
                _initialScanTimer += dt;
                if (_initialScanTimer >= InitialScanDelay)
                {
                    _pendingInitialScan = false;
                    ScanForNewAgents();
                }
            }

            if (_dataSource == null || _dataSource.Bubbles.Count == 0) return;

            MissionScreen currentMissionScreen = ScreenManager.TopScreen as MissionScreen;
            if (currentMissionScreen == null) return;

            // ----------------- 【优化1】循环外缓存常量 -----------------
            _tickCounter++;

            // 缓存屏幕参数
            float screenWidth = Screen.RealScreenResolutionWidth;
            float screenHeight = Screen.RealScreenResolutionHeight;

            // 缓存 UI 缩放的倒数 (乘法比除法快)
            float uiScale = _layer.UIContext.Scale;
            float invUiScale = 1.0f / uiScale;

            // 预计算边界容差 (像素)
            float screenPadding = 100f;

            // 延迟移除列表：不在遍历中直接 RemoveAt，避免与 GauntletUI 并行布局竞态
            List<int> removeIndices = null;

            // ----------------- 【优化2】倒序遍历 -----------------
            for (int i = _dataSource.Bubbles.Count - 1; i >= 0; i--)
            {
                var bubble = _dataSource.Bubbles[i];
                var agent = bubble.TargetAgent;

                // 基础校验
                if (agent == null || !agent.IsActive())
                {
                    if (removeIndices == null) removeIndices = new List<int>();
                    removeIndices.Add(i);
                    continue;
                }

                // ----------------- 【优化3】视野查询：统一走 NpcSightSystem 缓存 -----------------
                // 延迟获取（第一次 tick 时 Instance 可能尚未赋值）
                if (_sightSystem == null)
                    _sightSystem = NpcSightSystem.Instance;

                if (_sightSystem != null && !_sightSystem.IsPlayerSeeing(agent))
                {
                    if (bubble.IsVisible) bubble.IsVisible = false;
                    continue;
                }

                // ----------------- 【优化4】世界转屏幕 (仅对前方物体执行) -----------------
                Vec3 agentPos = agent.Position;
                // 修正高度：通常眼睛高度 + 0.3~0.5 米浮动在头顶
                agentPos.z += agent.GetEyeGlobalHeight() + 0.1f;
                var screenPos = currentMissionScreen.SceneLayer.WorldPointToScreenPoint(agentPos);
                float screenX = screenPos.x;
                float screenY = screenPos.y;
                // WorldPointToScreenPoint 返回的是 0~1 的比例坐标，转换为像素
                float pixelX = screenX * screenWidth;
                float pixelY = screenY * screenHeight;

                // ----------------- 【优化6】屏幕边缘剔除 -----------------
                if (pixelX < -screenPadding || pixelX > screenWidth + screenPadding ||
                    pixelY < -screenPadding || pixelY > screenHeight + screenPadding)
                {
                    if (bubble.IsVisible) bubble.IsVisible = false;
                    continue;
                }

                // ----------------- 【优化7】计算结果赋值 -----------------
                // 只有真正要显示时，才去设置 UI 的属性

                // 缩放计算：距离越远越小。Clamp 限制防止过大或过小
                float distSq = agent.Position.DistanceSquared(currentMissionScreen.CombatCamera?.Position ?? agent.Position);
                float distance = MathF.Sqrt(distSq);
                float scale = MBMath.ClampFloat(50f / (distance + 5f), 0.5f, 1.5f);

                if ((i + _tickCounter) % 10 == 0)
                {
                    bubble.UpdateLogic();
                }
                if (bubble.IsVisible)
                {
                    bubble.UpdateFrame(dt);

                    bubble.PosX = (pixelX * invUiScale) - (bubble.BubbleWidth * 0.5f);
                    bubble.PosY = (pixelY * invUiScale) - bubble.BubbleHeight;
                    bubble.Scale = scale;
                }
            }

            // 延迟批量移除：遍历结束后统一移除，避免在 ParallelUpdateLayouts 运行期间修改 MBBindingList
            if (removeIndices != null && removeIndices.Count > 0)
            {
                foreach (int idx in removeIndices)
                {
                    if (idx < _dataSource.Bubbles.Count)
                        _dataSource.Bubbles.RemoveAt(idx);
                }
            }

        }
        // 对外接口：让某人说话
        
        public void AddSpeechBubble(Agent agent, string text)
        {
            if (agent == null) return;

            // 1. 查找列表里是否已经有这个人的气泡
            // (因为 ScanForNewAgents 可能已经为他创建了血条气泡)
            var bubble = _dataSource.Bubbles.FirstOrDefault(b => b.TargetAgent == agent);

            if (bubble != null)
            {
                // 情况 A: 气泡已存在 (比如已经在显示血条了) -> 直接激活说话状态
                bubble.Speak($"{text}");
            }
            else
            {
                // 情况 B: 气泡不存在 -> 创建新气泡并激活说话
                var newBubble = new BubbleSayVM(agent);
                newBubble.Speak($"{text}");
                _dataSource.AddBubble(newBubble);
            }
        }
        public override void OnMissionScreenFinalize()
        {
            MissionScreen.RemoveLayer(_layer);
            _layer = null;
            _dataSource = null;
            Instance = null;
            base.OnMissionScreenFinalize();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("bubbleSay", "custom")]
        public static string ExecuteBubbleSay(List<string> args)
        {
            if(args.Count < 2)
            {
                return "param num not enough";
            }
            string agentStringId = args[0];
            string text = args[1];
            AgentBubbleSay(agentStringId, text);

            return "";
        }

        public static void AgentBubbleSay(Agent agent, string text)
        {
            if (Mission.Current == null) return;
            if (agent == null) return;
            if (Instance == null) return;   // MissionView 未注册/已销毁（如大地图），静默跳过，别崩
            Instance.AddSpeechBubble(agent, text);

        }
        public static void AgentBubbleSay(string agentStringId, string text)
        {
            if (Mission.Current == null) return;

            //玩家说话
            if(agentStringId == "player")
            {
                Instance.AddSpeechBubble(Mission.Current.MainAgent, text);
                InformationManager.DisplayMessage(new InformationMessage($"让 {agentStringId} 说话了{text}"));
                return;
            }

            // 遍历场景中所有 Agent，寻找 StringId 匹配的 NPC
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.IsActive() && agent.Character != null && agent.Character.StringId == agentStringId)
                {
                    // 调用 ViewModel 中的方法添加气泡
                    Instance.AddSpeechBubble(agent, text);
                    InformationManager.DisplayMessage(new InformationMessage($"让 {agentStringId} 说话了{text}"));
                    return;
                }
            }

            // 如果没找到，可以在控制台输出警告
            InformationManager.DisplayMessage(new InformationMessage($"未找到 ID 为 {agentStringId} 的 NPC"));

        }


    }
}
