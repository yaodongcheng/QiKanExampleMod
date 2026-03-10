using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ExampleMod.StoryEngineBag
{
    public static class VisualCommands
    {
        // 处理对话，这是"阻塞"指令
        public class ScriptActorInfo
        {
            public string EngineName;          // 引擎显示的名字 (如 "Alice")
            public string ScriptName;     //剧本中的名字
            public string HeroId;        // 数据库ID (如 "hero_alice")
            public bool IsPlayer;        // 是否是玩家

            // 时间轴信息
            public int FirstLineIndex;   // 第一次说话的行号（用于判断是否一开始就在场，还是后来走入）
            public int LastLineIndex;    // 最后一次说话的行号（用于判断是否提前离场）

            public ScriptActorInfo(string engineName, string id, string scriptName,int currentIndex, bool isPlayer = false)
            {
                EngineName = engineName;
                ScriptName = scriptName;
                HeroId = id;
                IsPlayer = isPlayer;
                FirstLineIndex = currentIndex;
                LastLineIndex = currentIndex;
            }
        }
        public static Dictionary<string, ScriptActorInfo> GetCurrentSceneParticipants(List<ScriptNode> script, int startIndex, out int newIndex, out int firstDialog, out int finalDialog)
        {

            var participants = new Dictionary<string, ScriptActorInfo>();

            string selfStringId = Agent.Main.Character.StringId;
            string selfName = Agent.Main.Name;

            newIndex = startIndex;
            int i = startIndex;
            firstDialog = -1;
            finalDialog = startIndex;
            for (; i < script.Count; i++)
            {
                var node = script[i];

                // --- 1. 边界检测：遇到切换场景的命令，立即停止 ---
                if (node.Cmd == "進入設施" || node.Cmd == "離開設施" || node.Cmd == "停止時間" )
                {
                    newIndex = i + 1; // 指向下一段的开始
                    break;
                }
                /*
                //不能直接让分歧来分段，因为会让参与者计算出问题
                if(node.Cmd == "分歧")
                {

                    for(int j = i+1;j< script.Count;j++)
                    {
                        if(script[j].Cmd != "分歧")
                        {
                            newIndex = j;
                            break;
                        }
                    }
                    break;
                }
                */
                // --- 2. 解析参与者 ---
                List<string> actorsInLine = new List<string>();

                if (node.Cmd == "對話" || node.Cmd == "對話選擇")
                {
                    if (node.Params != null && node.Params.Count > 0)
                    {
                        string speakerAndListener = node.Params[0];
                        string[] parts = speakerAndListener.Split(new char[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length > 0) actorsInLine.Add(parts[0].Trim()); // Speaker
                        if (parts.Length > 1) actorsInLine.Add(parts[1].Trim()); // Listener
                    }
                    finalDialog = i;
                    if(firstDialog == -1)
                    {
                        firstDialog = i;
                    }
                }
                else if (node.Cmd == "自語")
                {
                    actorsInLine.Add(selfName); // 假设自语就是主人公
                    finalDialog = i;
                    if (firstDialog == -1)
                    {
                        firstDialog = i;
                    }
                }

                // --- 3. 更新时间轴信息 ---
                foreach (var rawName in actorsInLine)
                {
                    string scriptName = rawName;
                    string engineName = "";
                    string heroId = "";
                    bool isPlayer = false;

                    // 处理“主人公”关键字
                    if (rawName == "主人公" || rawName== selfName)
                    {
                        engineName = selfName;
                        scriptName = selfName;
                        heroId = selfStringId;
                        isPlayer = true;

                    }
                    else
                    {
                        // 尝试解析 NPC
                        var record = GameDatabase.Heroes.GetByScriptName(scriptName);
                        if(record!=null)
                            heroId = record.GetString("ID");
                        Hero heroObj = Campaign.Current.CampaignObjectManager.Find<Hero>(heroId);
                        if (heroObj != null)
                        {
                            engineName = heroObj.Name.ToString();
                        }
                        else if (record != null)
                        {
                            engineName = record.GetString("EnglishName");                            
                        }

                        


                    }

                    // 更新字典
                    if (participants.ContainsKey(engineName))
                    {
                        // 如果已经存在，更新“最后一次说话”的行号
                        participants[engineName].LastLineIndex = i;
                    }
                    else
                    {
                        // 如果是新出现的，创建记录，标记“第一次说话”的行号
                        participants.Add(engineName, new ScriptActorInfo(engineName, heroId,scriptName, i, isPlayer));
                    }
                }
            }

            // 遍历结束
            if (i == script.Count)
            {
                newIndex = i + 1;
            }

            return participants;
        }

        public static Dictionary<string,StageSlot> AssignSlots()
        {

            var participants = new List<string>(StoryEngine.Instance.currentSceneParticipants.Keys.ToList());
            
            var result = new Dictionary<string, StageSlot>();
            HashSet<StageSlot> occupiedSlots = new HashSet<StageSlot>();
            Agent player = Mission.Current.MainAgent;
            //先分配城主
            string ownerAgentName = participants.FirstOrDefault(a => IsSettlementOwner(a));
            if(ownerAgentName != null && ownerAgentName != "")
            {
                result[ownerAgentName] = StageSlot.Chair;
                occupiedSlots.Add(StageSlot.Chair);
                participants.Remove(ownerAgentName);
            }
            //再分配玩家
            string playerAgentName = participants.FirstOrDefault(a => a == player.Name);
            if(playerAgentName != null && playerAgentName !="")
            {
                result[playerAgentName] = StageSlot.Guest;
                occupiedSlots.Add(StageSlot.Guest); 
                participants.Remove(playerAgentName);
            }
            //剩余人员填空，这里有问题是因为Chair没利用上，所以None了

            Queue<StageSlot> freeSlotsQueue = new Queue<StageSlot>();
            if (!occupiedSlots.Contains(StageSlot.Chair) && GetCurrentLocationId() != "lordshall")
                freeSlotsQueue.Enqueue(StageSlot.Chair);
            if (!occupiedSlots.Contains(StageSlot.Guest))
                freeSlotsQueue.Enqueue(StageSlot.Guest);
            freeSlotsQueue.Enqueue(StageSlot.Accompany1);
            freeSlotsQueue.Enqueue(StageSlot.Accompany2);
            //遍历剩下的人
            foreach(var agentName in participants)
            {


                if(freeSlotsQueue.Count > 0)
                {
                    StageSlot slot = freeSlotsQueue.Dequeue();
                    result[agentName] = slot;
                }
                else
                {
                    //没座位了
                    result[agentName] = StageSlot.None;
                }
            }
            return result;
        }

        public static string GetCurrentSettlementId()
        {
            //获取当前的Settlement
            Settlement currentSettlement = Hero.MainHero.CurrentSettlement;
            if (currentSettlement != null)
            {
                return currentSettlement.StringId;
            }

            return "Unknown";
        }

        public static string GetCurrentLocationId()
        {
            // 1. 优先从 Mission (3D场景) 获取
            // CampaignMission.Current 只有在进入场景后才不为空
            if (CampaignMission.Current != null && CampaignMission.Current.Location != null)
            {
                return CampaignMission.Current.Location.StringId;
            }
            // 2. 如果不在 Mission 中，或者数据未同步，尝试从 LocationComplex 查找
            // 但通常 StageDirector 肯定是在 Mission 里的，所以上面那个就够了
            return "unknown";
        }

        // 处理 "选择"
        public static bool HandleChoice(ScriptNode node, StoryEngine engine)
        {
            /*
            string choice1 = "";
            string choice2 = "";
            if (node.Options.Count > 0)
                choice1 = node.Options[0].Text;
            if (node.Options.Count > 1)
                choice2 = node.Options[1].Text;
            Action action0 = () => { engine.LastChoiceIndex = 0; engine.Resume(); };
            Action action1 = () => { engine.LastChoiceIndex = 1; engine.Resume(); };

            InformationManager.ShowInquiry(new InquiryData("请选择", "选择文本", true, true, choice1, choice2, action0, action1));


            engine.ViewModel.Show(node.Cmd, $"{choice1} vs {choice2}");
            return true;
            */
            // 1. 准备选项数据

            //打一个玩家正脸镜头，类似自语
            SpringArmCameraView.UseCameraTemlate("xm_Self_Side30_Mid_R", Agent.Main, null, Vec3.Zero);

            var vmOptions = new List<StoryOptionVM>();

            for (int i = 0; i < node.Options.Count; i++)
            {
                string text = node.Options[i].Text;
                int currentIndex = i; // 闭包捕获索引

                // 创建 Action：玩家点击此选项后要做什么
                Action onClickAction = () =>
                {
                    // 隐藏选项
                    engine.ViewModel.AreOptionsVisible = false;
                    engine.ViewModel.OptionList.Clear();

                    // 告诉引擎选了谁
                    engine.LastChoiceIndex = currentIndex;
                    engine.Resume();
                };

                vmOptions.Add(new StoryOptionVM(text, onClickAction));
            }

            // 2. 将数据推送到 VM，并在界面上显示
            // 假设 node.Cmd 是这一段的对话文本，也一并显示出来，让玩家知道针对什么在做选择
            engine.ViewModel.SpeakerName = ""; // 或者保留上一句话的人名
            engine.ViewModel.DialogueContent = "请做出你的选择..."; // 或者 node.Text
            engine.ViewModel.IsVisible = true;

            // 显示选项列表
            engine.ViewModel.ShowOptions(vmOptions.ToArray());

            return true; // 暂停执行，等待 UI 回调 Resume

        }
        public static bool HandleDialog(ScriptNode node, StoryEngine engine)
        {
            string text = node.Text; // 或者从 Params 解析
            string speaker = "EmptySpeaker";
            string listener = "EmptyListener";
            // 假设 params[0] 是说话人ID，需要转换成名字
            string selfStringId = Agent.Main.Character.StringId;
            var selfData = GameDatabase.Heroes.GetByID(selfStringId);
            string selfName = selfData.GetString("ScriptName");
            string selfSecondName = selfData.GetString("SecondName");
            string speakerSecondName = selfSecondName;
            string listenerSecondName = selfSecondName;
            DynamicRecord speakerData;
            DynamicRecord listenerData;
            if (node.Cmd == "對話" || node.Cmd == "對話選擇")
            {

                if (node.Params != null && node.Params.Count > 0)
                {
                    //在cmd值为對話 Params[0]的格式一般为 "主人公,寧寧" 中间有逗号分隔
                    string speakerAndListener = node.Params[0];
                    //用","分隔字符串
                    string[] parts = speakerAndListener.Split(new char[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
                    speaker = parts[0].Trim();

                    if (speaker == "主人公")
                    {
                        speaker = selfName;
                        speakerData = selfData;
                        speakerSecondName = selfSecondName;
                    }
                    else
                    {
                        speakerData = GameDatabase.Heroes.GetByScriptName(speaker);
                        speakerSecondName = speakerData?.GetString("SecondName");
                    }

                    listener = parts.Length > 1 ? parts[1].Trim() : "主人公";

                    if (listener == "主人公")
                    {
                        listener = selfName;
                        listenerData = selfData;
                        listenerSecondName = selfSecondName;
                    }
                    else
                    {
                        listenerData = GameDatabase.Heroes.GetByScriptName(listener);
                        listenerSecondName = listenerData?.GetString("SecondName");
                    }

                }
            }
            else if (node.Cmd == "自語")
            {
                //获取剧本中自己的名字？还是直接用自己的名字
                speaker = selfName;
                listener = selfName;
                speakerSecondName = selfSecondName;
                listenerSecondName = selfSecondName;

            }
            else if (node.Cmd == "旁白" || node.Cmd == "ＳＥ開始")
            {
                speaker = "";
                listener = selfName;
                speakerSecondName = "";
            }
            else if(node.Cmd == "選擇")
            {

            }
            //处理文本中的{二人稱}、{二人稱名前}、(豐臣秀吉.名)、{一人稱}、(寧寧) %%LEFT%%二人稱%%RIGHT%%、



            //待处理，处理主人公
            
            //主人公

            


            Agent speakerAgent = null;
            Agent listenerAgent = null;
            if (node.Cmd != "旁白" )
            {
                //待处理 更新说话者和听话者的站位
                //如果说话者和听话者本身不存在 就需要召唤 
                string speakerId = GameDatabase.Heroes.GetIdByName(speaker);
                speakerAgent = GetAgentInMission(speakerId, speaker);

                string listenerId = GameDatabase.Heroes.GetIdByName(listener);

                listenerAgent = GetAgentInMission(listenerId, listener);

                //说话者和听话者空间位置调整，转向
                //AdjustSpeakerAndListenerTransform(speakerAgent, listenerAgent);



                DebugLogger.Log($"StageShow: {node}");

                StageShow(speakerAgent, listenerAgent);

            }
            else
            {
                //旁白就用拉高镜头，但是可以来几个随机
                SpringArmCameraView.UseCameraTemlate("xm_Any_Side45_Far_R", null, null, Vec3.Zero);
            }


            if (speakerAgent != null)
                speaker = speakerAgent.Name;

            if (listenerAgent != null)
            {
                listener = listenerAgent.Name;
                //名字处理 
                if (listener.Contains("藤吉郎"))
                    listenerSecondName = "藤吉郎";
            }
            string showText = ParseDialogue(text, speaker, listener, speakerSecondName, listenerSecondName);

            // 更新 UI
            engine.ViewModel.Show(speaker, showText);

            //node.Emotion

            if (speakerAgent != null &&  VariableManager.GetV(speakerAgent.Character.StringId, "IsSit") != "True")
            {   //角色没坐下的时候用动作
                string emotion  = node.Emotion;
                if (emotion == null)
                    emotion = engine._matcher.GetEmotionByDialogue(showText);
                AgentControlHelper.SetPose(speakerAgent, emotion);                
            }



            // 返回 true，告诉引擎暂停，等待玩家点击鼠标
            return true;
        }

        public static bool HandleAppear(ScriptNode node, StoryEngine engine)
        {
            return false;
        }
        public static bool HandleExit(ScriptNode node, StoryEngine engine)
        {
            return false; 
        }

        public static string StageShow(Agent speakerAgent, Agent listenerAgent)
        {
            if (speakerAgent == null || listenerAgent == null)
            {
                return "Error: Agent not found.";
            }

            var stageDirector = StoryEngine.Instance.stageDirector;
            string speakerName = speakerAgent.Name;
            string listenerName = listenerAgent.Name;

            

            var speakerFrame = stageDirector.GetTargetPosition(speakerName);
            var listenerFrame = stageDirector.GetTargetPosition(listenerName);

            Vec3 speakerPos = speakerFrame.origin;
            Vec3 listenerPos = listenerFrame.origin;
            Vec3 speakerEyePos = speakerAgent.GetEyeGlobalPosition();
            Vec3 listenerEyePos = listenerAgent.GetEyeGlobalPosition();


            float speaakerAdjustZ = speakerPos.z;
            if (Mission.Current.Scene != null)
            {
                speakerPos.z = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(speakerPos.x, speakerPos.y, speaakerAdjustZ));
            }
            float listenerAdjustZ = listenerPos.z;
            if (Mission.Current.Scene != null)
            {
                listenerPos.z = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(listenerPos.x, listenerPos.y, listenerAdjustZ));
            }



            Vec3 dirToListener = (listenerPos - speakerPos).NormalizedCopy();

            speakerAgent.TeleportToPosition(speakerPos);

            if(speakerAgent == listenerAgent)
            {
                //Camera以后再说
                SmartCamera(speakerAgent, listenerAgent);
                return "success";
            }

            // 强制说话者看向听者
            speakerAgent.SetMovementDirection(dirToListener.AsVec2);
            //speakerAgent.SetMovementDirection(speakerFrame.rotation.f.AsVec2);
            
          //  speakerAgent.SetLookToPointOfInterest(listenerEyePos);
            
            //打印listenerAgent传送之前的位置
            
            
            var listenerPosBefore = listenerAgent.Frame.origin;
            


            listenerAgent.TeleportToPosition(listenerPos);
            listenerAgent.SetMovementDirection(-1* dirToListener.AsVec2);
            var listenerPosAfter = listenerAgent.Frame.origin;
            
            //DebugLogger.Log($"{listenerAgent.Name}传送之前位置{listenerPosBefore}传送之后位置{listenerPosAfter}。");


            //如果对话中有领主，则领主坐下，另一个跪下
            //基于真实的距离计算distance
            float targetDistance = speakerFrame.origin.Distance(listenerFrame.origin);

            string speakerAnim = "act_sitting_pose3";
            string listenerAnim = "act_sitting_pose3";
            if (IsSettlementOwner(listenerAgent.Name) || IsSettlementOwner(speakerAgent.Name))
            {
                ApplySitAnimationSafe(speakerAgent, speakerAnim);

                ApplySitAnimationSafe(listenerAgent, listenerAnim);
            }

            SmartCamera(speakerAgent, listenerAgent);

            return "success";
        }



        public static string AdjustSpeakerAndListenerTransform(Agent speakerAgent, Agent listenerAgent)
        {
            //基础站位，2个人面对面，距离2米，互相转向对方
            // 1. 安全检查
            if (speakerAgent == null || listenerAgent == null)
            {
                return "Error: Agent not found.";
            }

            // 获取 Campaign 系统中的角色对象，用于判断身份
            CharacterObject speakerChar = speakerAgent.Character as CharacterObject;
            CharacterObject listenerChar = listenerAgent.Character as CharacterObject;
            // 默认配置
            Agent movingAgent = speakerAgent; // 默认 Speaker 移动
            Agent anchorAgent = listenerAgent; // 默认 Listener 是锚点
            float targetDistance = 2.0f;
            bool needsToSit = false; // 是否需要坐下

            string speakerAnim = "";
            string listenerAnim = "";

            // 检查是否在城主大厅 (lordshall)，临时规则，在城主大厅就让城主不动
            bool isInLordsHall = false;
            Location currentLocation = CampaignMission.Current?.Location;
            if (currentLocation != null)
            {
                if (currentLocation.StringId.Contains("lordshall"))
                {
                    isInLordsHall = true;
                }
            }





            //如果其中一方是城主，就让他不动。最好他是当前城市的城主
            if (isInLordsHall)
            {
                targetDistance = 4.0f;
                needsToSit = true; // 标记需要坐下，后续处理动画


                // 获取身份状态
                bool isSpeakerOwner = IsSettlementOwner(speakerChar);
                bool isListenerOwner = IsSettlementOwner(listenerChar);
                bool isSpeakerLord = IsLord(speakerChar);
                bool isListenerLord = IsLord(listenerChar);

                // 只要有一方是地主，或者有一方是领主，就触发特殊站位
                if (isSpeakerOwner || isListenerOwner || isSpeakerLord || isListenerLord)
                {
                    targetDistance = 4.0f;
                    needsToSit = true;

                    // --- 锚点判断逻辑 (谁不动) ---

                    // 情况 1: 听话的人是地主 (最高优先级)
                    // 场景：玩家(Speaker) 进大厅找 城主(Listener) 说话 -> 城主不动
                    if (isListenerOwner)
                    {
                        anchorAgent = listenerAgent;
                        movingAgent = speakerAgent;
                        speakerAnim = "act_sitting_pose3";
                        listenerAnim = "act_sitting_pose3";
                    }
                    // 情况 2: 说话的人是地主
                    // 场景：城主(Speaker) 坐在王座上召见 玩家(Listener) -> 城主不动
                    else if (isSpeakerOwner)
                    {
                        anchorAgent = speakerAgent;
                        movingAgent = listenerAgent;
                        speakerAnim = "act_sitting_pose3";
                        listenerAnim = "act_sitting_pose3";
                    }
                    // 情况 3: 没人是地主，但听话的人是领主
                    // 场景：玩家(平民) 找 某个做客的领主(Listener) -> 领主不动
                    else if (isListenerLord)
                    {
                        anchorAgent = listenerAgent;
                        movingAgent = speakerAgent;
                    }
                    // 情况 4: 没人是地主，说话者是领主，且听话者不是领主
                    // 场景：领主(Speaker) 找 卫兵/平民(Listener) -> 领主不动
                    else if (isSpeakerLord && !isListenerLord)
                    {
                        anchorAgent = speakerAgent;
                        movingAgent = listenerAgent;
                    }

                    // 默认情况 (例如两个都不是领主，或者两个都是普通领主且都不是主人)：
                    // 维持开头的默认值：speaker 移动，listener 不动
                }
            }



            //修正锚点的位置，如果是城主则移动到城主座位上
            if (isInLordsHall && needsToSit)
            {

                //计算位置
                var tagPointData = GameDatabase.TagPoint.GetByID("lordshall");
                string ChairTrans = tagPointData.GetString("ChairTrans");
                SimpleTrans trans;
                StageConfig.TryParseSimpleTrans(ChairTrans, out trans);


                Vec3 lordPosition = new Vec3(trans.Offset.x, trans.Offset.y, trans.Offset.z);
                Vec2 lordDirection = new Vec2(-MathF.Sin(trans.YawDeg), MathF.Cos(trans.YawDeg));
                // 移动者瞬移
                anchorAgent.TeleportToPosition(lordPosition);
                anchorAgent.SetMovementDirection(lordDirection);
               // anchorAgent.LookDirection = lordDirection.ToVec3();
            }


            Vec3 anchorPos = anchorAgent.Position;
            Vec2 anchorLookDir = anchorAgent.GetWorldFrame().Rotation.f.AsVec2.Normalized();

            Vec2 targetPos2D = anchorPos.AsVec2 + (anchorLookDir * targetDistance);
            // 获取地面高度，防止穿模或悬空
            float targetZ = anchorPos.z;
            if (Mission.Current.Scene != null)
            {
                targetZ = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(targetPos2D.x, targetPos2D.y, targetZ));
            }
            Vec3 finalTargetPos = new Vec3(targetPos2D.x, targetPos2D.y, targetZ);

            // 移动者瞬移
            movingAgent.TeleportToPosition(finalTargetPos);
            // 计算互相面对的方向
            Vec3 dirToAnchor = (anchorPos - finalTargetPos).NormalizedCopy();
            // 强制移动者面向锚点
            //movingAgent.LookDirection = dirToAnchor;
            movingAgent.SetMovementDirection(dirToAnchor.AsVec2);
            //movingAgent.SetLookToPointOfInterest(anchorPos);
            if (needsToSit)
            {
                ApplySitAnimationSafe(speakerAgent, speakerAnim);

                ApplySitAnimationSafe(listenerAgent, listenerAnim);
            }

            SmartCamera(speakerAgent, listenerAgent);

            return "success";
        }
        private static void ApplySitAnimationSafe(Agent agent, string animName)
        {
            if (agent == null || string.IsNullOrEmpty(animName)) return;

            // 1. 如果角色正在使用物体（例如坐在王座椅子上），绝对不要播放地板坐动画
            // 否则会发生严重的穿模
            if (agent.IsUsingGameObject) return;

            // 2. 检查当前动作是否已经是目标动作
            ActionIndexCache targetAction = ActionIndexCache.Create(animName);
            //  ActionIndexCache targetAction = ActionIndexCache.Create(animName);
            ActionIndexValueCache currentAction = agent.GetCurrentActionValue(0);


            // 如果当前动作不等于目标动作，才播放
            // 注意：有些动画有由 idle 和 progress 组成的动画链，这里主要防止完全重复触发

         //   InformationManager.DisplayMessage(new InformationMessage($"尝试应用动作{targetAction.Name},当前动作{currentAction.Name}。", Colors.Red));
            if (currentAction.Name != targetAction.Name)
            {
                // 额外检查：有些坐下动作可能是持续态，如果名字包含 "sit" 且正在进行，也可以选择跳过
                // 但这里用严格匹配通常更安全，防止还没坐下就被打断
                agent.SetActionChannel(0, targetAction, ignorePriority: true, blendInPeriod: 0.2f);


                VariableManager.SetV(agent.Character.StringId, "IsSit", "True");
            }
        }
        // 判断是否为领主
        private static bool IsLord(CharacterObject character)
        {
            return character != null && character.IsHero && character.HeroObject.IsLord;
        }
        // 判断是否是当前定居点的主人
        public static bool IsSettlementOwner(CharacterObject character)
        {
            if (character == null || !character.IsHero) return false;

            // 获取当前定居点
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return false;

            // 1. 检查是否是该定居点所属家族的族长 (最常见的情况)
            if (settlement.OwnerClan != null && settlement.OwnerClan.Leader.Name == character.Name)
            {
                return true;
            }

            // 2. 检查是否是该定居点的总督 (Governor) (总督通常也会坐在王座上)
            if (settlement.Town != null && settlement.Town.Governor?.Name == character.Name)
            {
                return true;
            }

            return false;
        }
        public static bool IsSettlementOwner(string agentName)
        {

            // 获取当前定居点
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return false;

            // 1. 检查是否是该定居点所属家族的族长 (最常见的情况)
            if (settlement.OwnerClan != null && settlement.OwnerClan.Leader.Name.ToString() == agentName)
            {
                return true;
            }

            // 2. 检查是否是该定居点的总督 (Governor) (总督通常也会坐在王座上)
            if (settlement.Town != null && settlement.Town.Governor?.Name.ToString() == agentName)
            {
                return true;
            }

            return false;
        }
        public static string SmartCamera(Agent speakerAgent, Agent listenerAgent)
        {

            Agent playerAgent = Agent.Main;
            //默认拉远景
            string camTemplate = "xm_Any_Side45_Far_R"; 

            if(speakerAgent == playerAgent)
            {
                camTemplate = "2m_Self_Shoulder_Mid_R";
            }
            else if(listenerAgent == playerAgent)             
            {
                camTemplate = "2m_Npc_Shoulder_Mid_R";

            }
            //如果是Npc对Npc说话，直接拉远镜头得了
            else
            {
                camTemplate = "xm_Npc_Side30_Far_R";
            }
            

            //城主间专用 并且其中一个人是城主
            if (GetCurrentLocationId() == "lordshall" )
            {
                if (IsSettlementOwner(playerAgent.Name) || IsSettlementOwner(speakerAgent.Name) || IsSettlementOwner(listenerAgent.Name))
                {
                    camTemplate = "sp_lordshall";
                }
            }
            //自语
            if(speakerAgent == listenerAgent )
            {
                camTemplate = "xm_Self_Side30_Mid_R";
            }
            
            //playerAgent.SetAgentFacialAnimation(Agent.FacialAnimChannel.High, "face_angry", false);




            return SpringArmCameraView.UseCameraTemlate(camTemplate,speakerAgent,listenerAgent,Vec3.Zero);
          
        }
        public static Agent GetAgentInMission(string stringId, string scriptName)
        {
            //先检查是不是玩家自己
            Agent playerAgent = Mission.Current.MainAgent;
            string engineName = "";
            CharacterObject template = CharacterObject.Find(stringId);

            
            if (template != null)
            {
                engineName = template.Name.ToString();
            }
            else
            {
                //如果找不到，就用脚本名来查找
                //Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.Name.ToString() == scriptName);
                var record = GameDatabase.Heroes.GetByID(stringId);
                if(record == null)
                    record = GameDatabase.Heroes.GetByScriptName(scriptName);
                if (record != null) 
                    {
                        engineName = record.GetString("EnglishName");
                    }
                else
                {
                    engineName = scriptName;
                }
                       
            }
            

            if (playerAgent.Name == engineName || scriptName == "主人公"  || playerAgent.Character.StringId == stringId)
            {

                return playerAgent;
            }
            Agent targetAgent;
            //场景里面找是不是已经有了
            foreach (Agent a in Mission.Current.Agents)
            {

                if (a.IsHuman && (a.Character.StringId == stringId || a.Name == engineName))
                {
                    targetAgent = a;
                    return targetAgent;
                }
            }

            
            MatrixFrame playerFrame = playerAgent.Frame;
            Vec3 direction = playerFrame.rotation.f;
            float randomOffset = MBRandom.RandomFloatRanged(1.5f, 3f);
            Vec3 spawnPos = playerFrame.origin + (direction * randomOffset);
            float dir = MathF.Atan2(playerAgent.LookDirection.y, playerAgent.LookDirection.x);
            //默认生成在玩家前方，之后在看怎么修改出场合适
            targetAgent = SummonAgent_New(stringId, spawnPos, dir, engineName);

         //   InformationManager.DisplayMessage(new InformationMessage($"EngineName {engineName} 没找到 尝试重新召唤新的{targetAgent.Name}！"));

            return targetAgent;
        }



        public static Agent SummonAgent_New(string stringId, Vec3 spawnPos, float dir, string engineName)
        {
            CharacterObject template = CharacterObject.Find(stringId);
            Agent playerAgent = Mission.Current.MainAgent;
            if (template == null)
            {
                template = CharacterObject.Find("spc_katsurayama_shu_leader_0");
            }
            if (template != null)
            {
                var agentOrigin = new SimpleAgentOrigin(template, -1, null);

                // 构建 Agent 数据
                AgentBuildData agentBuildData = new AgentBuildData(agentOrigin)
                    .InitialPosition(spawnPos)
                    .InitialDirection(Vec2.FromRotation(dir))
                    .Team(playerAgent.Team)
                    .NoHorses(true);

                // 处理装备 (平民/战斗)
                if (template.IsFemale)
                {
                    agentBuildData = agentBuildData.CivilianEquipment(true);
                }
                else
                {
                    agentBuildData = agentBuildData.CivilianEquipment(false);
                }

                // 生成 Agent
                Agent newAgent = Mission.Current.SpawnAgent(agentBuildData);
                
          //      InformationManager.DisplayMessage(new InformationMessage($"Name: {newAgent.Name} (ID: {newAgent.Character.StringId} CName: {newAgent.Character.Name.ToString()}) 已召唤！"));
                return newAgent;
            }
            return null;

        }

        public static Agent SummonAgent(string stringId, Vec3 spawnPos, float dir, string engineName)
        {
            CharacterObject template = CharacterObject.Find(stringId);
            Settlement currentSettlement = Hero.MainHero.CurrentSettlement;
            Agent playerAgent = Mission.Current.MainAgent;

            Settlement initialSettlement = currentSettlement ?? Hero.MainHero.HomeSettlement;
            TextObject agentName = TextObject.Empty;
            //如果模版没找到，就找大众脸，然后改名
            if (template == null)
            {
                template = CharacterObject.Find("spc_katsurayama_shu_leader_0");                
                agentName = new TextObject(engineName);
            }
            else
            {
                agentName = template.Name;
            }


            if (template != null)
            {
                Hero newHero = HeroCreator.CreateSpecialHero(template, initialSettlement, null, null, -1);
                newHero.ChangeState(Hero.CharacterStates.Active);
                newHero.SetName(agentName, agentName);
                

                if (Mission.Current != null && Mission.Current.MainAgent != null)
                {
                    var agentOrigin = new SimpleAgentOrigin(newHero.CharacterObject, -1, null);
                    AgentBuildData agentBuildData = new AgentBuildData(agentOrigin).InitialPosition(spawnPos).InitialDirection(Vec2.FromRotation(dir)).Team(playerAgent.Team).NoHorses(true);
                    if (template.IsFemale)
                    {
                        //女性默认日常着装
                        agentBuildData = agentBuildData.CivilianEquipment(true);
                    }
                    else
                    {
                        agentBuildData = agentBuildData.CivilianEquipment(false);
                    }

                    Agent newAgent = Mission.Current.SpawnAgent(agentBuildData);
               //     InformationManager.DisplayMessage(new InformationMessage($"{newAgent.Name} 已新召唤出现在你面前！"));
                    return newAgent;
                }
            }


            return null;
        }

        public static string ParseDialogue(string rawText, string speaker, string listener, string speakerSecondName, string listenerSecondName)
        {
            string processedText = rawText;

            //处理类似  這樣一來，{二人稱名前}也算是一個武士了啊！不錯啊，你的夢想終於實現了呢 的话
            processedText = Regex.Replace(processedText, @"\{(.*?)\}", match =>
            {
                string key = match.Groups[1].Value; // 拿到 "一人稱" 或 "二人稱名前"

                switch (key)
                {
                    case "一人稱":
                        return GetFirstPersonPronoun(null);

                    case "二人稱":
                        return GetSecondPersonPronoun(null, null);

                    case "二人稱名前":
                        // 对方的名字
                        return listenerSecondName;

                    default:
                        return match.Value; // 未知标签，保留原样
                }
            });
            //处理类似 出生在尾張．中村農家的(豐臣秀吉)，由於與養父不合，因此少年時代就離家出奔。 的话，提取括号中的内容
            processedText = Regex.Replace(processedText, @"\((.*?)\)", match =>
            {
                string key = match.Groups[1].Value; // 拿到 豐臣秀吉
                //检查是否包含.
                string[] parts = key.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                string scriptName = parts[0];
                if (parts.Length > 1)
                {

                    var data = GameDatabase.Heroes.GetByScriptName(scriptName);
                    if (parts[1] == "名")
                    {
                        string secondName = data?.GetString("SecondName");
                        return secondName;
                    }
                    else
                    {
                        string firstName = data?.GetString("FirstName");
                        return firstName;
                    }
                }
                else
                {
                    return scriptName;
                }

            });


            return processedText;
        }
        private static string GetFirstPersonPronoun(Agent speaker)
        {
            if (speaker == null)
                return "我";

            if (speaker.IsFemale)
            {
                return "妾身";
            }
            else
            {
                return "在下";
            }

        }
        private static string GetSecondPersonPronoun(Agent speaker, Agent listener)
        {
            if (listener == null)
                return "你";

            return "你";
        }

        public static bool HandleNarration(ScriptNode node, StoryEngine engine)
        {
            engine.ViewModel.Show("System", node.Cmd);
            return true; // 阻塞
        }
        public static bool HandlePortraitChange(ScriptNode node, StoryEngine engine)
        {
            // 切换立绘逻辑...
            engine.ViewModel.Show("System", node.Cmd);
            return true;

        }


    }
}
