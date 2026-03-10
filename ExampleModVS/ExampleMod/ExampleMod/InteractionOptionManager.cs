using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ExampleMod.StoryEngineBag;
using ExampleMod.AI;

namespace ExampleMod
{
    public enum InteractionOptionType
    {
        // 通用
        Chat,               // 寒暄/闲聊
        Leave,              // 离开
        Info,   //查看情报

        // 个人/社交

        ProposalMarriage,           // 求婚 
        Gift,               // 送礼
        TeaCeremony,        // 茶席
        Spar,               // 手合 (切磋)
        StudySkill,         // 修业 (学习技能)

        // 公务 (对上级)
        ReportMission,      // 任务汇报 (提交主命)
        RequestFunds,       // 请求军资
        Resign,             // 请辞/下野

        //公务（还未入仕）
        RequestWork,     // 仕官

        // 公务 (对同僚/下下属)
        Slander,            // 流言
        SolicitSupport,     // 拉拢 (请求支持)

        // 外交/谍报 (对外部)
        RecruitHero,        // 劝诱 (招募)
        Betrayal,           // 策反

        //特殊
        Assault,            // 斩杀 (街头袭击)
        Order,             // 命令（询问士兵状态，触发士兵对话）
        Order_Follow,        // 跟随（命令士兵跟随自己）
    }

    /// <summary>
    /// 选项分类 (用于UI分组显示)
    /// </summary>
    public enum InteractionCategory
    {
        General,    // 基础
        Social,     // 社交/个人
        Official,   // 公务/主命
        Diplomacy,  // 外交/谋略
        Hostile     // 敌对/暴力
    }
    public class InteractionDefinition
    {
        public InteractionOptionType Type { get; set; }
        public string DisplayName { get; set; }
        public InteractionCategory Category { get; set; }

        // 核心：判断该选项当前是否可见的委托
        // 参数：target (谈话对象)
        public Func<Hero, Agent, bool> Condition { get; set; }

        // 核心：点击后执行的逻辑
        // 参数：target (谈话对象), controller (为了回调LLM或其他系统)
        public Action<Hero, Agent> OnAction { get; set; }

        public string ToolTip = "";

        // 修改 InteractionDefinition 构造函数中的 Condition 默认实现，避免使用丢弃参数的 lambda
        public InteractionDefinition(InteractionOptionType type, string text, InteractionCategory category,string toolTip)
        {
            Type = type;
            DisplayName = (text);
            Category = category;
            Condition = (target, agent) => true;  // 显式声明所有参数，兼容 C# 7.3
            OnAction = (target, agent) => { };
            ToolTip = toolTip;
        }
    }

    public class InteractionOptionManager
    {

        // 存储所有注册的选项
        private readonly List<InteractionDefinition> _allOptions;
        private readonly InteractionController _controller;

        // 引用外部控制器（如果需要回调触发LLM对话，可通过事件或单例，这里简化处理）
        // 实际使用中，你可能需要传入 InteractionController 的引用来触发 startNegotiation

        public InteractionOptionManager(InteractionController ctrl)
        {
            _allOptions = new List<InteractionDefinition>();
            _controller = ctrl;
            RegisterAllOptions();
        }

        /// <summary>
        /// 获取针对特定目标的所有可用选项
        /// </summary>
        public List<InteractionDefinition> GetAvailableOptions(Hero targetHero, Agent targetAgent)
        {
            // 过滤掉不满足 Condition 的选项
            return _allOptions.Where(opt => opt.Condition(targetHero, targetAgent)).ToList();
        }

        /// <summary>
        /// 在这里集中注册所有太阁V风格的选项逻辑
        /// </summary>
        private void RegisterAllOptions()
        {
            if (_controller == null)
                return;
            //求婚
            var proposalMarriage = new InteractionDefinition(InteractionOptionType.ProposalMarriage, "【求婚】 表达爱意", InteractionCategory.Social, "表达你的爱意，希望对方能接受你的求婚");
            proposalMarriage.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 原逻辑：单身(暂时允许已婚) & 异性
                bool isSingle = true; // target.Spouse == null && Hero.MainHero.Spouse == null;
                bool isOppositeSex = target.IsFemale != Hero.MainHero.IsFemale;
                return isSingle && isOppositeSex;
            };
            proposalMarriage.OnAction = (target, agent) =>
            {
                _controller.SendIntent("求婚", "我仰慕你很久了，可以嫁给我吗？");
            };
            _allOptions.Add(proposalMarriage);

           


           


            // [查看情报] (新增)
            var info = new InteractionDefinition(InteractionOptionType.Info, "【情报】 查看信息", InteractionCategory.General, "查看对方的人物属性和关系");
            info.Condition = (target, agent) => target != null;
            info.OnAction = (target, agent) =>
            {
                // 打开百科全书对应页面
                if (target != null)
                    Campaign.Current.EncyclopediaManager.GoToLink(target.EncyclopediaLink);
            };
            _allOptions.Add(info);


            // [送礼] 
            // 修正：添加 Tooltip 参数，Lambda 改为 (target, agent)
            var gift = new InteractionDefinition(InteractionOptionType.Gift, "送礼", InteractionCategory.Social, "赠送物品以提升关系");
            gift.Condition = (target, agent) => target != null; // 简单检查：对方必须是Hero
            gift.OnAction = (target, agent) =>
            {
                // 逻辑：打开物品交换界面 (Barter) 或者特殊的送礼弹窗
                InformationManager.DisplayMessage(new InformationMessage("打开行囊准备礼物..."));
            };
            _allOptions.Add(gift);

            // [茶席] 
            // 修正：添加 Tooltip 参数，Lambda 改为 (target, agent)，增加 target null 检查
            var tea = new InteractionDefinition(InteractionOptionType.TeaCeremony, "茶席", InteractionCategory.Social, "邀请对方共饮茶水");
            tea.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 示例条件：如果是敌人，不能喝茶
                return target.Clan != Clan.PlayerClan && !target.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction);
            };
            tea.OnAction = (target, agent) =>
            {
                InformationManager.DisplayMessage(new InformationMessage("开始点茶... (播放动画/消耗茶具)"));
                ChangeRelationAction.ApplyPlayerRelation(target, 5);
            };
            _allOptions.Add(tea);

            // [手合/切磋] 
            // 修正：添加 Tooltip 参数，Lambda 改为 (target, agent)
            var spar = new InteractionDefinition(InteractionOptionType.Spar, "【交手】切磋武艺", InteractionCategory.Social, "点到为止，不伤性命");
            spar.Condition = (target, agent) =>
            {
                if (agent == null) return false;
                if (target == null) return true;
                return !target.IsWounded && target.IsLord;
            };
            spar.OnAction = (target, agent) =>
            {
                AgentAIController.Instance.SendEventToAgent(agent, "order_attack", Agent.Main);
                InteractionController.Instance._vm.Close();

            };
            _allOptions.Add(spar);

            // ===========================
            // 3. 修正后的 公务类 (Official - 对上级)
            // ===========================

            // [任务汇报]
            // 修正：添加 Tooltip 参数，Lambda 改为 (target, agent)
            var report = new InteractionDefinition(InteractionOptionType.ReportMission, "任务汇报", InteractionCategory.Official, "汇报当前任务进度");
            report.Condition = (target, agent) =>
            {
                if (target == null) return false;
                if(GenericQuest.IsHeroInvolvedInActiveQuest(Hero.OneToOneConversationHero, out GenericQuest quest, out bool isGiver))
                {
                    return isGiver;
                }

                return false;
            };
            report.OnAction = (target, agent) =>
            {
                InformationManager.DisplayMessage(new InformationMessage("在下已完成使命..."));
            };
            _allOptions.Add(report);

            // [请求军资]
            var funds = new InteractionDefinition(InteractionOptionType.RequestFunds, "【索要】 请求军资", InteractionCategory.Official, "请求调拨资金或粮草");
            funds.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 只有主君或者国王可以请求
                return target == Clan.PlayerClan.Leader || (target.MapFaction == Hero.MainHero.MapFaction && target.IsFactionLeader);
            };
            funds.OnAction = (target, agent) =>  _controller.SendIntent("Strategy", "主公，我们需要更多支援。");
            _allOptions.Add(funds);


            // [仕官] (新增)
            var requestWork = new InteractionDefinition(InteractionOptionType.RequestWork, "【仕官】 请求奉公", InteractionCategory.Official, "请求加入对方的家族或王国");
            requestWork.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 玩家是自由身，对方是有领地或家族的领主
                return Clan.PlayerClan.Kingdom == null && target.Clan != null && target.Clan.Leader == target;
            };
            requestWork.OnAction = (target, agent) =>  _controller.SendIntent("RequestWork", "大人，请允许我为您效力。");
            _allOptions.Add(requestWork);

            // [命令] (新增 - 迁移自Controller)
            var order = new InteractionDefinition(InteractionOptionType.Order, "【命令】 询问状况", InteractionCategory.Official, "询问士兵当前的状态");
            order.Condition = (target, agent) =>
            {
                // 目标不是 Hero (target == null) 或者是 Hero 但也是下属
                if (target == null && agent.Character != null && agent.Character.IsSoldier) return true;
                return false;
            };
            order.OnAction = (target, agent) =>  _controller.SendIntent("Order", "汇报你的情况，士兵！");
            _allOptions.Add(order);

            // [登庸/招募流浪者] (新增 - 迁移自Controller)
            var joinClan = new InteractionDefinition(InteractionOptionType.RecruitHero, "【登庸】 招入麾下", InteractionCategory.Diplomacy, "邀请对方加入你的家族");
            joinClan.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 对方是流浪者，且还没加入玩家家族
                return target.IsWanderer && target.Clan != Clan.PlayerClan;
            };
            joinClan.OnAction = (target, agent) =>  _controller.SendIntent("Persuade", "你的才华不应被埋没，加入我吧。");
            _allOptions.Add(joinClan);
            // [劝诱/招募敌将]
            var recruit = new InteractionDefinition(InteractionOptionType.RecruitHero, "【劝诱】 劝说倒戈", InteractionCategory.Diplomacy, "劝说对方背叛当前阵营加入我方");
            recruit.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 对方属于其他阵营，且处于战争状态或单纯挖角
                return target.MapFaction != Hero.MainHero.MapFaction && target != target.MapFaction.Leader;
            };
            recruit.OnAction = (target, agent) =>  _controller.SendIntent("Persuade", "良禽择木而栖，阁下何不弃暗投明？");
            _allOptions.Add(recruit);

            // [策反]
            var betray = new InteractionDefinition(InteractionOptionType.Betrayal, "【策反】 密谋造反", InteractionCategory.Diplomacy, "密谋推翻现有统治");
            betray.Condition = (target, agent) =>
            {
                if (target == null) return false;
                // 同阵营，但想造反
                return target.MapFaction == Hero.MainHero.MapFaction && target != Hero.MainHero && target.IsLord;
            };
            betray.OnAction = (target, agent) => InformationManager.DisplayMessage(new InformationMessage("密谋中..."));
            _allOptions.Add(betray);

            //跟着我
            var follow = new InteractionDefinition(InteractionOptionType.Order_Follow, "【跟随】 跟随我", InteractionCategory.General, "跟随我");
            follow.Condition = (target, agent) =>
            {
                if (target == null) return false;
                return true; //其实正常人需要说服，但是这里先简化，
            };
            follow.OnAction = (target, agent) =>
            {
                _controller._vm.Close();
                var brain = AgentAIController.GetBrainForAgent(agent);
                if(brain == null) return;
                brain.SetLeader(Agent.Main);
                brain.SetGuardMode(true);
                AgentAIController.Instance.SendEventToAgent(agent,"order_follow",Agent.Main);
            };
            _allOptions.Add(follow);



            // [寒暄] - 永远排在倒数第二个
            var chat = new InteractionDefinition(InteractionOptionType.Chat, "【寒暄】随便说两句...", InteractionCategory.General, "自由输入你想说的内容");
            chat.OnAction = (target, agent) =>
            {
                string name = agent.Name.ToString();
                InformationManager.ShowTextInquiry(new TextInquiryData(
                  "寒暄", $"你想对{name}说什么?：", true, true, "发送", "取消",
                  async (text) =>
                  {
                      _controller._vm.LockPrediction();
                      await _controller.HandlePlayerInputAsync(text, null);
                  }, null));
            };
            _allOptions.Add(chat);

            // [离开] 永远要排在最后一个
            var leave = new InteractionDefinition(InteractionOptionType.Leave, "【离开】 告辞", InteractionCategory.General, "离开对话");
            leave.OnAction = (target, agent) =>
            {
                //InformationManager.DisplayMessage(new InformationMessage($"与{agent.Name}的对话结束,即将发事件"));
                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position,15.0f,"EndInteraction",Agent.Main);
                GroupStageManager.Reset(Agent.Main);
                //InformationManager.DisplayMessage(new InformationMessage($"与{agent.Name}的对话结束,完成发事件"));
                _controller._vm.Close();

            };
            _allOptions.Add(leave);

        }

    }
}
