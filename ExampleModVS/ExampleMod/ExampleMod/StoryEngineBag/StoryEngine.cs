using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using static ExampleMod.StoryEngineBag.VisualCommands;

namespace ExampleMod.StoryEngineBag
{

    public class ExecutionContext
    {
        public List<ScriptNode> Script;
        public int Index;

        // --- 新增：场景/舞台元数据 ---
        public Dictionary<string, ScriptActorInfo> Participants; // 当前场景需要的演员
        public int SceneBeginIndex; // 当前场景在Script中的起始行
        public int SceneEndIndex;   // 当前场景在Script中的结束行
        public int FirstDialogIndex;
        public int FinalDialogIndex;

        public HashSet<string> EnteredActors = new HashSet<string>();
        public HashSet<string> LeavedActors = new HashSet<string>();

        public ExecutionContext(List<ScriptNode> script, int startIndex = 0)
        {
            Script = script;
            Index = startIndex;

            // 初始化时，分析第一段数据
            RefreshSceneScope();
        }

        // --- 新增：核心方法，用于分析当前 Index 之后的场景范围 ---
        public void RefreshSceneScope()
        {
            // 场景开始就是当前的进度
            SceneBeginIndex = Index;

            // 调用你原有的 VisualCommands 逻辑分析后续内容
            // 假设 GetCurrentSceneParticipants 会从传入的 Index 开始往后看，直到遇到下一幕
            Participants = GetCurrentSceneParticipants(
                Script,
                Index,
                out int nextBeginIndex,
                out int firstDialog,
                out int finalDialog
            );
            SceneEndIndex = nextBeginIndex-1;
            FirstDialogIndex = firstDialog;
            FinalDialogIndex = finalDialog;
        }

    }
    public class StoryEngine
    {
        private GauntletLayer _layer;
        public StoryDialogVM ViewModel { get; private set; }

        public DialogueActionMatcher _matcher;
        // 数据相关
        public StoryContext Context { get; private set; }


        public MissionScreen CurrentMissionScreen { get; private set; }
        //场景加载结束恢复故事流程
        private bool _isWaitingForSceneChange = false;

        // --- 新增：Wait/Delay 功能变量 ---
        private bool _isWaitingForTimer = false;
        private float _waitTimerEndTime = 0f;


        private List<ScriptNode> _currentScript;
        public int _currentIndex;
        private bool _isRunning;
        // 使用栈来管理嵌套
        private Stack<ExecutionContext> _stack = new Stack<ExecutionContext>();
        //比如是否在分歧的分支里
        // 获取栈顶的辅助方法
        public ExecutionContext GetCurrentContext()
        {
            return _stack.Count > 0 ? _stack.Peek() : null;
        }
        public int currentSceneBeginScriptIndex
        {
            get { return GetCurrentContext()?.SceneBeginIndex ?? 0; }
        }
        public int currentSceneEndScriptIndex
        {
            get { return GetCurrentContext()?.SceneEndIndex ?? 0; }
        }
        public int currentSceneFirstDialogIndex
        {
            get { return GetCurrentContext()?.FirstDialogIndex ?? 0; }
        }
        public int currentSceneFinalDialogIndex
        {
            get { return GetCurrentContext()?.FinalDialogIndex ?? 0; }
        }

        public Dictionary<string, ScriptActorInfo> currentSceneParticipants
        {
            get { return GetCurrentContext()?.Participants ?? null; }
        }
        Dictionary<string, ScriptActorInfo> prevParticipants = null;

        public bool GetIsRunning()
        {
            return _isRunning;
        }

        public static StoryEngine Instance { get; private set;}
        public static SoundEvent currentBgm;

        public StageDirector stageDirector;

        public int LastChoiceIndex = -1;

        public List<Agent> _movingAgentsForEntering = new List<Agent>();
        public List<Agent> _movingAgentsForLeaving = new List<Agent>();

        public bool _isWaitingForMovement_Entering = false;

        public bool _isWaitingForMovement_Leaving = false;

        public void StartWait(float seconds)
        {
            _waitTimerEndTime = MBCommon.GetApplicationTime() + seconds;
            DebugLogger.Log($"StartWait: Now-{MBCommon.GetApplicationTime()}  endTime-{_waitTimerEndTime} during: {seconds} seconds");
            _isWaitingForTimer = true;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("testAIStoryEvent", "custom")]
        public static string ExecuteAIStoryEvent(List<string> args)
        {
            var result = AIStoryGeneratorBehavior.Instance.GetCurrentResult();
            if(result == null)
            {
                return "No AI story generated yet!";
            }
            if (Instance == null)
            {
                Instance = new StoryEngine();
            }            
            Instance.StartEvent(result.ScriptNodes);
            return $"AI story begin success";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("testStoryEvent", "custom")]
        public static string ExecuteStoryEvent(List<string> args)
        {

            string eventFile = "EP120500";
            string eventId = "0";
            if (args.Count == 2)
            {
                eventFile = args[0];
                eventId = args[1];
            }
            StoryEvent testEvent = StoryDataLoader.FindEvent("EP120500", "0");
           // string eventContent = StoryDebugHelper.PrintEventInfo(testEvent);
           if(testEvent == null)
            {
                return $"{eventFile} event {eventId} can not find";
            }
            if (Instance == null)
            {
                Instance = new StoryEngine();
            }
            Instance.StartEvent(testEvent.Script);
            
            
            return $"{eventFile} event {eventId} success";
        }


        public StoryEngine()
        {
            Context = new StoryContext();
            CommandManager.RegisterAll(); // 确保指令已注册

            stageDirector = new StageDirector();            
            

            Instance = this;
            _matcher = new DialogueActionMatcher();


        }

        public static void ChangeNameBasedOnHistory()
        {
            foreach (var hero in Hero.AllAliveHeroes)
            {
                string stringId = hero.StringId;     
            
                DynamicRecord record = GameDatabase.Heroes.GetByID(stringId);
                if (record == null)
                    continue;
                string historyName = record.GetString("Name_1568");
                if (historyName == string.Empty)
                    continue;
                TextObject nameTxt = new TextObject(historyName);
                hero.SetName(nameTxt, nameTxt);
            }
        }

        // 启动一个事件序列
        public void StartEvent(List<ScriptNode> scripts)
        {
            if (scripts == null || scripts.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage($"scripts == null || scripts.Count == 0 {scripts.Count}!"));
                return; 
            }
            _stack.Clear();
            // 推入主脚本作为第一层上下文
            _stack.Push(new ExecutionContext ( scripts, 0 ));

            _currentScript = scripts;
            _currentIndex = 0;
            _isRunning = true;

            // 初始化 UI
            SetupUI();

            //禁用主角移动
            Agent.Main.SetMovementDirection(Vec2.Zero);
            if (Agent.Main.Controller == Agent.ControllerType.Player)
            {
                Agent.Main.Controller = Agent.ControllerType.AI;
            }

            // 开始循环
            RunLoop();
        }
        public void PushSubScript(List<ScriptNode> children)
        {
            if (children != null && children.Count > 0)
            {
                // 推入新的一层，Index 从 0 开始
                _stack.Push(new ExecutionContext(children, 0));
            }
        }

        public void SetupUI()
        {
            CurrentMissionScreen = ScreenManager.TopScreen as MissionScreen;
            if (CurrentMissionScreen == null) return; // 如果当前不在任务场景中，直接返回

            if (_layer == null)
            {
                ViewModel = new StoryDialogVM();
                // 如果需要恢复之前的文本，可以在这里把 Context 里的数据塞回 ViewModel
            }
            else
            {
                CloseUI();
                if(ViewModel == null)
                {
                    ViewModel = new StoryDialogVM();
                }
               
            }
            
            _layer = new GauntletLayer(1000); // 高优先级
                                              // 加载你的 XML (假设文件名是 "MyStoryDialog")
            _layer.LoadMovie("DialogChoice", ViewModel);
            CurrentMissionScreen.AddLayer(_layer);
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);


        }

        private void CloseUI()
        {
            if (_layer != null && CurrentMissionScreen != null)
            {
                CurrentMissionScreen.RemoveLayer(_layer);
                _layer = null;
                // 注意：这里不要把 ViewModel 设为 null，因为换场景后还要用它
            }
        }

        public void TriggerSceneChange(string locationId)
        {
            // 1. 标记正在等待
            _isWaitingForSceneChange = true;

            // 2. 关闭当前 UI (防止 UI 报错，反正切场景 UI 也会没)
            CloseUI();
        }

        public void OnTick()
        {
            if (!_isRunning)
            {
                return;
            }

            //这部分，不需要触发等待，只需要进行就行了
            if (_isWaitingForMovement_Leaving && !_isWaitingForSceneChange)
            {
                for (int i = _movingAgentsForLeaving.Count - 1; i >= 0; i--)
                {
                    Agent agent = _movingAgentsForLeaving[i];
                    if (agent == null)
                    {
                        continue;
                    }
                    if (agent.Position == null)
                    {
                        //说明切换场景中了
                        _isWaitingForMovement_Leaving = false;
                        continue;
                    }

                    var targetFrame = stageDirector.GetTargetPosition(agent.Name,agent.Character.StringId);
                    var ctx = GetCurrentContext();
                    float distance = agent.Position.AsVec2.Distance(targetFrame.origin.AsVec2);
                 //   InformationManager.DisplayMessage(new InformationMessage($" {agent.Name}距离目的地还有{distance} 目的地坐标{targetFrame.origin}。", Colors.Red));
                    if (distance < 1.0f)
                    {
                       
                            //彻底移除
                            string desc = $" {agent.Name}已经到了销毁地。";
                       //     InformationManager.DisplayMessage(new InformationMessage(desc, Colors.Red));
                            _movingAgentsForLeaving.RemoveAt(i);
                            agent.FadeOut(true, false);
                            DebugLogger.Log(desc);
                        
                       
                    }
                }

                if (_movingAgentsForLeaving.Count == 0)
                {
                    _isWaitingForMovement_Leaving = false;
                }
            }




            bool wasWaitingAtStart = _isWaitingForTimer || _isWaitingForMovement_Entering || _isWaitingForSceneChange;

            if (!wasWaitingAtStart)
            {
                return;
            }



            if (_isWaitingForTimer)
            {
                // 使用 MBCommon 获取全局时间
                if (MBCommon.GetApplicationTime() >= _waitTimerEndTime)
                {
                    _isWaitingForTimer = false;
                }
                // 注意：这里不 return，允许等待期间背景逻辑（如 Agent 移动）继续运行
            }


            if (_isWaitingForMovement_Entering && !_isWaitingForSceneChange) 
            {
                for (int i = _movingAgentsForEntering.Count - 1; i >= 0; i--)
                {
                    Agent agent = _movingAgentsForEntering[i];
                    if (agent == null)
                    {
                        continue;
                    }
                    if(agent.Position == null)
                    {
                        //说明切换场景中了
                        _isWaitingForMovement_Entering = false;
                        continue;
                    }

                    var targetFrame = stageDirector.GetTargetPosition(agent.Name);

                    // 计算距离 (只计算平面距离，忽略高度差)
                    float distance = agent.Position.AsVec2.Distance(targetFrame.origin.AsVec2);

                    if (distance < 1.0f)
                    {                        

                        // 2. 强制修正最终朝向和位置 (避免误差)
                        agent.TeleportToPosition(targetFrame.origin);
                        agent.SetMovementDirection(targetFrame.rotation.f.AsVec2);

                        // 3. 从列表中移除
                        _movingAgentsForEntering.RemoveAt(i);
                    }
                }

                // 如果所有人都到了，恢复剧情循环
                if (_movingAgentsForEntering.Count == 0)
                {
                    _isWaitingForMovement_Entering = false;
                    //Resume(); // 恢复 RunLoop
                }
            }
            
            if (_isWaitingForSceneChange)
            {
                // 获取当前的 TopScreen，并尝试转换为 MissionScreen
                MissionScreen newScreen = ScreenManager.TopScreen as MissionScreen;

                // 1. 必须确保当前屏幕是 MissionScreen
                // 2. 必须确保这是一个新的屏幕实例（避免还是旧的引用，虽然旧的通常会被销毁）
                if (newScreen != null && newScreen != CurrentMissionScreen)
                {
                    // 获取该屏幕对应的 Mission 对象
                    // 注意：MissionScreen 通常有一个 public Mission Mission { get; } 属性
                    Mission currentMission = newScreen.Mission;

                    // 3. 关键检查：检查 Mission 是否存在，且状态是否为 Continuing
                    // 根据反编译代码，Continuing 意味着加载完成，正在进行 Tick (游戏逻辑在跑了)
                    if (currentMission != null && currentMission.CurrentState == Mission.State.Continuing)
                    {
                        // --- 场景加载完毕且逻辑已开始 ---
                        _movingAgentsForEntering.Clear();
                        _movingAgentsForLeaving.Clear();

                        _isWaitingForSceneChange = false;

                        // 更新引用
                        CurrentMissionScreen = newScreen;

                        // 重建 UI (GauntletLayer)
                        SetupUI();

                        // 恢复执行脚本
                        

                       // SystemCommands.ExecuteWait(5.0f);
                    }
                }
            }
            bool isStillWaiting = _isWaitingForTimer || _isWaitingForMovement_Entering || _isWaitingForSceneChange;

            // 只有当“之前在等” 且 “现在彻底不等了” 时，才恢复运行
            if (!isStillWaiting)
            {
                Resume();
            }
        }

        private void FinishEvent()
        {
            _isRunning = false;
            CloseUI();
            // 可以在这里恢复游戏时间流逝等

            //镜头重置
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            missionScreen.CustomCamera = null;
            CameraDebuggerView.targetAgent = Mission.Current.MainAgent;

            //恢复主角移动
            if (Agent.Main != null && Agent.Main.IsActive())
            {
                // 切回玩家控制
                Agent.Main.Controller = Agent.ControllerType.Player;
            }
        }
        public void Resume()
        {
            if (_isRunning)
            {
                RunLoop();
            }
        }
        public void OnPlayerClick()
        {
            // 只有在显示对话时，点击才有效
            if (_layer != null && ViewModel.IsVisible && !_isWaitingForMovement_Entering && !_isWaitingForSceneChange)
            {
                Resume();
            }
        }
        public string GetMovingAgentsNames(bool Entering)
        {

            string names = "";
            if(Entering)
                names= string.Join(",", _movingAgentsForEntering.Select(a => a.Name));
            else
                names =string.Join(",", _movingAgentsForLeaving.Select(a => a.Name));
            return names;
        }
        public void PrepareActors()
        {
            // 1. 获取当前上下文
            var ctx = GetCurrentContext();
            if (ctx == null) return;

            

            //计算当前舞台的点位信息
            string currentLocation = VisualCommands.GetCurrentLocationId();
            stageDirector.Initialize(StageConfig.FromRecord(GameDatabase.TagPoint.GetByID(currentLocation)));
            stageDirector.CalculateAllPositions();
            //计算当前幕的演员信息
            ctx.RefreshSceneScope();
            ctx.LeavedActors.Clear();


            //分配本幕演员的表演站位
            string actorList = "";
            var name2slot = VisualCommands.AssignSlots();

            foreach (var kvp in ctx.Participants)
            {
                var engineName = kvp.Key;
                var actorInfo = kvp.Value;
                string stringId = actorInfo.HeroId;
                StageSlot thisSlot = name2slot[engineName];
                // 在舞台管理员处注册演员
                stageDirector.RegisterActor(engineName, thisSlot);
                actorList = actorList + engineName + $"( {thisSlot} {actorInfo.FirstLineIndex}-{actorInfo.LastLineIndex})";

                VariableManager.SetV(stringId, "IsSit", "False");
            }

          //  InformationManager.DisplayMessage(new InformationMessage($"下一情景的演员有 {actorList},场景持续({ctx.SceneBeginIndex}-{ctx.SceneEndIndex})。", Colors.Red));

            // 新的一幕开始了，重置当前 Context 的进出场记录
            ctx.EnteredActors.Clear();
            ctx.LeavedActors.Clear();
        }
        public bool CheckActorLeaving()
        {
            var ctx = GetCurrentContext();
            if (ctx == null) return false;
            bool someoneIsLeaving = false;
            //为什么是-1，因为当前幕的最后一行是场景切换指令，真正的最后一句话是在前一行
            

            if (ctx.Index == ctx.SceneEndIndex - 1)
            {
                //上一幕的角色都退场吧
                prevParticipants = new Dictionary<string, ScriptActorInfo>(ctx.Participants);

                ctx.EnteredActors.Clear();

                // 注意：这里我们只是“偷看”下一幕的数据，不更新 Context
                int peekIndex = ctx.SceneEndIndex + 1;

                // 防止数组越界（如果这是脚本最后一行）
                Dictionary<string, ScriptActorInfo> nextParticipants = null;
                if (peekIndex < ctx.Script.Count)
                {
                    nextParticipants = VisualCommands.GetCurrentSceneParticipants(
                       ctx.Script,
                       peekIndex,
                       out int _, out int _, out int _ // 不需要关心下一幕的结束点
                   );
                }
                else
                {
                    //这里其实得偷偷看Pop之后，父脚本的下一幕
                    var parentCtx = _stack.Count > 1 ? _stack.ToArray()[1] : null;
                    if (parentCtx != null)
                    {
                        //应该从之前进入分歧的下一节点开始看
                        int parentCurrentIndex = parentCtx.Index;
                        if (parentCurrentIndex + 1 < parentCtx.Script.Count)
                        {
                            nextParticipants = VisualCommands.GetCurrentSceneParticipants(
                               parentCtx.Script,
                               parentCurrentIndex + 1,
                               out int _, out int _, out int _ // 不需要关心下一幕的结束点
                           );
                        }
                    }
                }
                foreach (var kvp in ctx.Participants)
                {
                    var actorName = kvp.Key;
                    var actorInfo = kvp.Value;

                    // 如果下一幕的名单里没有他，且不是主角(主角通常常驻)，则退场
                    if ((nextParticipants == null || !nextParticipants.ContainsKey(actorName)) && actorInfo.EngineName != Agent.Main.Name)
                    {
                        ctx.LeavedActors.Add(actorInfo.EngineName);
                        bool isWalking = stageDirector.HideActor(actorInfo);
                        if (isWalking)
                        {
                            someoneIsLeaving = true;
                        }
                    }
                }               
            }
            return someoneIsLeaving;
        }
        public bool CheckActorEntering(ScriptNode node)
        {
            var ctx = GetCurrentContext();
            if (ctx == null) return false;
            bool someoneIsEntering = false;

            //这里不对，可能上一段脚本已经被pop出去了

            //找到父脚本的参与者，如果父脚本中已经有这个人，则不需重复入场

                //每一行指令，检查是否有人应该入场
                if ((node.Cmd == "對話" || node.Cmd == "對話選擇") && ctx.Participants != null)
            {
                foreach (var kvp in currentSceneParticipants)
                {
                    var info = kvp.Value;
                
                    if (info.FirstLineIndex == ctx.Index  &&   !ctx.EnteredActors.Contains(info.EngineName) && (prevParticipants == null || !prevParticipants.ContainsKey(info.EngineName))  && !IsSettlementOwner(info.EngineName))
                    {
                        // 标记为已入场
                        ctx.EnteredActors.Add(info.EngineName);

                        
                        
                        
                        // 执行入场动画
                        bool isWalking = stageDirector.ShowActor(info);                        

                        if (isWalking)
                        {
                            someoneIsEntering = true;                            
                        }
                    }
                   

                }
            }

            if (someoneIsEntering)
            { 
                SpringArmCameraView.UseCameraTemlate("xm_Any_Side45_Far_R", null, null, Vec3.Zero);
                var oneMovingAgent = _movingAgentsForEntering[0];
                Agent.Main.SetLookAgent(oneMovingAgent);            
            }
            return someoneIsEntering;
        }
        
        private void RunLoop()
        {
            if (!_isRunning) return;

            // 1. 获取当前栈顶
            

            //因为脚本中存在嵌套逻辑，所以需要用栈来管理执行上下文
            while (_stack.Count > 0)
            {
                var ctx = GetCurrentContext();
                            // 如果栈空了，结束
                            if (ctx == null)
                            {
                                _isRunning = false;
                                FinishEvent();
                                return;
                            }

                //检查栈是否还在分支里
                if (ctx.Index >= ctx.Script.Count) // 当前层脚本执行完毕
                {
                    _stack.Pop(); // 弹出当前层，返回上一层
                    ctx = GetCurrentContext();
                    if(ctx != null)
                    {
                        ctx.Index++; // 上一层继续下一行
                    }
                    continue;     // 继续循环，处理上一层的下一行
                }

                //判断是否剧情进入了新的一幕
                //注意，这里只有场景加载好了之后才有意义，不然坐标计算会有问题，所以不能在上一幕的末尾执行（因为场景还没改变），而是得新一幕的开头执行（因为场景变好了）
                //还需要补充一个，如果是刚刚从分歧里面出来的，也需要准备演员

                if (ctx.Index == 0 || (ctx.Index == ctx.SceneEndIndex+1))
                {
                    //最开始第一幕
                    PrepareActors();
                }
                else if (ctx.Script[ctx.Index].Cmd != "分歧" && ctx.Script[ctx.Index-1].Cmd == "分歧")
                {
                    PrepareActors();
                }


                ScriptNode node = ctx.Script[ctx.Index];
                bool someoneIsEntering = false;
                someoneIsEntering = CheckActorEntering(node);
                if (someoneIsEntering)
                {
                    ViewModel.Show("旁白", $"不远处{GetMovingAgentsNames(true)}来了。");

                    //让已经在场的人注视新来的人

                    return;
                }
                // 记录栈深度，用于判断指令是否导致了 Push 操作
                int stackCountBefore = _stack.Count;
                //执行到node.Cmd=="分歧"的时候，可能会压入新的一层上下文，
                bool isBlocking = CommandManager.Execute(node, this, ctx.Index);
                // 只有当栈顶还是当前上下文时（没进入子分支），才移动指针
                bool someoneIsLeaving = false;
                someoneIsLeaving = CheckActorLeaving();

                // 5. 索引管理逻辑
                if (_stack.Count > stackCountBefore)
                {
                    //不处理，通过Push那边Index+1
                    if(node.Cmd == "分歧")
                    {
                        //得确保这个是上一层还是当前层
                        //ctx.Index++;
                    }
                    if (isBlocking)
                    {
                        return; // 遇到对话等阻塞指令，交出控制权
                    }
                }
                else if (_stack.Count < stackCountBefore)
                {
                }
                else
                {

                    ctx.Index++;
                    if (someoneIsLeaving)
                    {
                        ViewModel.Show("旁白", $"{GetMovingAgentsNames(false)}准备离开。");
                        //退场的情况，感觉不用等走完，2秒后就可以执行下一个指令了
                        SystemCommands.ExecuteWait(2);

                        return;
                    }
                    if (isBlocking)
                    {
                        return; // 遇到对话等阻塞指令，交出控制权
                    }

                }


                
                //执行退场
                
            }
            


            // 脚本跑完了
            FinishEvent();
        }
    }


}
