using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace ExampleMod
{
    public enum QuestType
    {
        DeliverItem_Special = 1,    // 获取贵重品 -寻

        // --- 经济/物资类 ---
        DeliverItem_Food = 2,        // 筹集军粮 -寻
        DeliverItem_Horse = 3,        // 筹集军马 -寻
        DeliverItem_Gun = 4, // 筹集铁炮 -寻
        EarnMoney = 5,          // 筹措军资金 -寻
        EarnMoney_SellFood,  // 卖出军粮 -寻
        CollectDebt,        // 收回欠款 -寻

        // --- 军事/战斗类 ---
        HuntBandits,        // 讨伐山贼 (野外杀敌) -杀
        RecruitTroops,      // 征兵 (带回特定兵种) -修
        TrainTroops,        // 训练 (提升部队等级或获得XP) -修
        RaidVillage,        // 掠夺 (袭击敌方村庄) - 杀
        Assault, //袭击某人
        CaptureSetlement,   // 占领据点 (占领敌方据点) -杀

        // --- 内政/建设类 ---
        DevelopSettlement_Food,  // 开发新田  -修
        DevelopSettlement_Prosperity, //增筑 -修
        DevelopSettlement_Security, //提升治安

        // --- 外交/谍报类 ---
        DiplomacyTalk_War,      // 外交-宣战 - 修
        DiplomacyTalk_Alliance, // 外交-结盟 - 修
        DiplomacyTalk_Peace,    // 外交-媾和 - 修
        DiplomacyTalk_SubOrdination,    // 外交-从属 - 修
        DiplomacyTalk_Dominate,    // 外交-支配 - 修
        ScoutSettlement,    // 侦查情报(去敌方据点) - 寻
        Sabotage,           // 破坏/放火/流言 (降低敌方据点属性) - 杀
        RecruitHero, //人才调查（浪人） - 修
        PersuadeLord,       // 劝诱（其他阵营） - 修  

        // --- 个人成长类 ---
        ImproveSkill,       // 修业 (提升特定技能) -修
        WinArena,           // 道场比试 (赢得竞技场) 

        EscortCaravan, //护送 -护

        Promise,  // 承诺 -修
    }
    public class QuestData
    {
        [SaveableField(1)] public QuestType Type;

        // 通用目标ID：可以是物品ID("grain")，也可以是技能ID("OneHanded")，或者属性类型("Prosperity")
        [SaveableField(2)] public string TargetId;

        // 通用目标数值：数量、金额、目标等级、或者需要降低的数值
        [SaveableField(3)] public int TargetCount;

        // 目标对象
        [SaveableField(4)] public Hero TargetHero;
        [SaveableField(5)] public string TargetSettlementId;

        // [新增] 辅助字段：用于记录任务开始时的状态（例如：开发任务需要记录初始繁荣度）
        [SaveableField(6)] public float StartValue;


        // --- 新增：本金/物资支持 ---
        [SaveableField(7)] public int GivenGold; // 领导给的本金
        [SaveableField(8)] public string GivenItemId; // 领导给的物资ID（如卖米任务给的米）
        [SaveableField(9)] public int GivenItemCount; // 领导给的物资数量

        public string GetQuestDescription()
        {
            StringBuilder sb = new StringBuilder();

            // --- 1. 开场白：大名的威严 ---
            sb.Append("听好了，这是当下的主命。\n");

            // --- 2. 物资/资金交接：恩威并施 ---
            bool hasGiven = false;
            if (GivenGold > 0)
            {
                sb.Append($"这里是 {GivenGold} 两的军资金，拿去活用吧。");
                hasGiven = true;
            }

            if (GivenItemCount > 0 && !string.IsNullOrEmpty(GivenItemId))
            {
                if (hasGiven) sb.Append("还有，");
                sb.Append($"这 {GivenItemCount} 个 {GivenItemId} 也拨给你调度。");
            }

            if (hasGiven) sb.Append("\n\n");

            // --- 3. 辅助变量处理 ---
            // 如果对象为空，提供一个通用称呼防止报错
            string targetName = TargetHero != null ? TargetHero.Name.ToString() : (TargetId ?? "那个人");
            string locationName = TargetSettlementId ?? "那座城";
            string itemName = TargetId ?? "物资";
            string countStr = TargetCount.ToString();

            // --- 4. 核心指令：具体的太阁风格任务描述 ---
            switch (Type)
            {
                // --- 经济/物资类 ---
                case QuestType.DeliverItem_Food:
                    sb.Append($"兵马未动，粮草先行。你去市井之中筹集 {countStr} 个 {itemName}。\n无论去买还是去征收，务必填满本家的粮仓。");
                    break;
                case QuestType.DeliverItem_Horse:
                    sb.Append($"为了组建赤备突击队，我们需要良马。你去搜罗 {countStr} 匹 {itemName} 回来。\n没有马，武士就无法驰骋沙场，这就交给你了。");
                    break;
                case QuestType.DeliverItem_Gun:
                    sb.Append($"旧时代的战法已经落伍了。现在是铁炮的时代。\n去给我弄到 {countStr} 挺 {itemName}！这将是制霸天下的关键。");
                    break;
                case QuestType.DeliverItem_Special:
                    sb.Append($"我听说世间流传着名为 {itemName} 的稀世珍宝。\n此等宝物理应由我来收藏。去把它找来献给我，必有重赏。");
                    break;
                case QuestType.EarnMoney:
                    sb.Append($"打仗就是烧钱。本家的库房需要充实。\n发挥你的才干，去赚取 {countStr} 两献上来。经商也好，倒卖也好，我要的是结果。");
                    break;
                case QuestType.EarnMoney_SellFood:
                    sb.Append($"现在粮价正好。带上这些军粮去高价变卖。\n目标是获利 {countStr} 两。别让奸商把你给骗了。");
                    break;
                case QuestType.CollectDebt:
                    sb.Append($"{targetName} 那家伙欠了本家的钱太久了。\n你去替我把这笔烂账收回来。如果他不给……哼，你自己看着办。");
                    break;

                // --- 军事/战斗类 ---
                case QuestType.HuntBandits:
                    sb.Append($"领内的盗匪甚是猖狂，竟敢阻碍商路。\n带兵去讨伐 {countStr} 队贼寇！用他们的首级来扬名立万吧！");
                    break;
                case QuestType.RecruitTroops:
                    sb.Append($"为了接下来的合战，我们需要新鲜血液。\n去领地招募 {countStr} 名壮丁带回来。要能拿得动长枪的汉子。");
                    break;
                case QuestType.TrainTroops:
                    sb.Append($"光有人数没有用，我要的是精锐。\n去训练你的部队，让我看到 {countStr} 名独当一面的战士。别让他们在战场上白白送死。");
                    break;
                case QuestType.RaidVillage:
                    sb.Append($"敌方的 {locationName} 是个麻烦的补给点。\n去掠夺那里，烧光他们的物资，断绝敌人的后勤！这是为了大义。");
                    break;
                case QuestType.Assault:
                    sb.Append($"{targetName} 这个人很碍眼。\n去伏击他，给他一个终身难忘的教训。做的干净点。");
                    break;
                case QuestType.CaptureSetlement:
                    sb.Append($"时机成熟了。向 {locationName} 进军！\n无论付出多少代价，都要把那座城池攻下来，插上本家的旗帜！");
                    break;

                // --- 内政/建设类 ---
                case QuestType.DevelopSettlement_Food:
                    sb.Append($"国富才能兵强。你去 {locationName} 指导新田开发。\n要把那里的粮食产量提升到 {countStr} 以上。别让百姓饿肚子，那是本家的基石。");
                    break;
                case QuestType.DevelopSettlement_Prosperity:
                    sb.Append($"{locationName} 的城下町太萧条了。\n去想办法搞活那里的商业，把繁荣度提升 {countStr} 点。让那里成为黄金之城。");
                    break;
                case QuestType.DevelopSettlement_Security:
                    sb.Append($"{locationName} 的治安令人担忧。\n去整顿风纪，巡逻街道。我要看到那里的治安度恢复到 {countStr} 以上。");
                    break;

                // --- 外交/谍报类 ---
                case QuestType.DiplomacyTalk_War:
                    sb.Append($"忍耐已经到了极限。你去向 {targetName} 下达最后通牒。\n这是宣战布告！让他们洗干净脖子等着吧！");
                    break;
                case QuestType.DiplomacyTalk_Alliance:
                    sb.Append($"为了对抗强敌，我们需要盟友。\n做为本家的使者，去说服 {targetName} 结盟。这需要三寸不烂之舌，但我相信你。");
                    break;
                case QuestType.DiplomacyTalk_Peace:
                    sb.Append($"战线拉得太长了，我们需要休养生息。\n去和 {targetName} 谈判，达成停战协定。即使是暂时的和平也是必要的。");
                    break;
                case QuestType.DiplomacyTalk_SubOrdination:
                    sb.Append($"{targetName} 也是时候认清形势了。\n去劝说他们归顺本家。告诉他们，投降者既往不咎。");
                    break;
                case QuestType.DiplomacyTalk_Dominate:
                    sb.Append($"去告诉 {targetName}，若想活命，就成为本家的附庸。\n这是慈悲，不是谈判。");
                    break;
                case QuestType.ScoutSettlement:
                    sb.Append($"知己知彼，百战不殆。\n潜入 {locationName} 进行侦查。摸清他们的虚实，然后把情报带回来。");
                    break;
                case QuestType.Sabotage:
                    sb.Append($"正面进攻伤亡太大。你去 {locationName} 搞些破坏。\n放火也好，流言也罢，我要看到他们的力量衰弱下去。");
                    break;
                case QuestType.RecruitHero:
                    sb.Append($"听说 {targetName} 是一员猛将，却流落野外。\n带上金银和诚意，去把他招揽到本家麾下。人才就是力量。");
                    break;
                case QuestType.PersuadeLord:
                    sb.Append($"{targetName} 在敌营中郁郁不得志。\n去策反他！让他倒戈加入我们。从内部瓦解敌人是最上策。");
                    break;

                // --- 个人/其他类 ---
                case QuestType.ImproveSkill:
                    sb.Append($"你的 {itemName} 技艺还不够精湛。\n去修行吧，把这项技能提升到 {countStr} 级。不要给我丢脸。");
                    break;
                case QuestType.WinArena:
                    sb.Append($"最近军中士气低落。去 {locationName} 的竞技场拿下冠军！\n用你的武勇来振奋全军的士气！");
                    break;
                case QuestType.EscortCaravan:
                    sb.Append($"这支商队对本家的财政至关重要。\n你亲自护送他们到达目的地。路上的苍蝇，直接拍死。");
                    break;
                case QuestType.Promise:
                    sb.Append($"既然你夸下了海口，那就去兑现你的承诺。\n是个男人就说到做到，别让我失望。");
                    break;

                default:
                    sb.Append($"这是特别的任务：{Type}。\n具体的细节你应该清楚，速去速回。");
                    break;
            }

            // --- 5. 结语：激励与施压 ---
            sb.Append("\n\n切勿怠慢。期待你的好消息。");

            return sb.ToString();
        }

        public string GetQuestTitle()
        {
            switch (Type)
            {
                case QuestType.DeliverItem_Food: return ("筹集军粮"); // 兵粮购入
                case QuestType.DeliverItem_Horse: return ("筹集军马"); // 马匹购入
                case QuestType.DeliverItem_Gun: return ("筹集铁炮"); // 铁炮购入
                case QuestType.EarnMoney: return ("筹集军资"); // 筹集军资
                case QuestType.EarnMoney_SellFood: return ("卖出军粮"); // 兵粮出售
                case QuestType.CollectDebt: return ("收回借款"); // 收回借款
                case QuestType.HuntBandits: return ("讨伐山贼"); // 讨伐山贼
                case QuestType.RecruitTroops: return ("征兵"); // 征兵
                case QuestType.TrainTroops: return ("训练"); // 训练
                case QuestType.RaidVillage: return ("掠夺"); // 掠夺
                case QuestType.CaptureSetlement: return ("占领据点"); // 占领据点
                case QuestType.DevelopSettlement_Food: return ("开发新田"); // 新田开发/矿山
                case QuestType.DevelopSettlement_Prosperity: return ("增筑"); // 新田开发/矿山
                case QuestType.DevelopSettlement_Security: return ("提升治安"); // 新田开发/矿山
                case QuestType.DiplomacyTalk_War: return ("宣战"); // 宣战
                case QuestType.DiplomacyTalk_Alliance: return ("结盟"); // 结盟
                case QuestType.DiplomacyTalk_Peace: return ("媾和"); // 媾和
                case QuestType.DiplomacyTalk_SubOrdination: return ("从属"); // 从属
                case QuestType.DiplomacyTalk_Dominate: return ("支配"); // 支配
                case QuestType.ScoutSettlement: return ("侦查情报"); // 侦查情报
                case QuestType.Sabotage: return ("破坏/放火"); // 破坏/放火
                case QuestType.RecruitHero: return ("人才调查"); // 人才调查
                case QuestType.PersuadeLord: return ("劝诱"); // 劝诱
                case QuestType.ImproveSkill: return ("修业"); // 修业
                case QuestType.WinArena: return ("道场比试"); // 道场比试
                case QuestType.EscortCaravan: return ("护送"); // 护送
                case QuestType.Promise: return ("履行承诺"); // 承诺

                default: return ("Lord's Mission");
            }
        }
    }
    public class GenericQuest : QuestBase
    {
        [SaveableField(10)] private QuestData _data;
        [SaveableField(11)] private int _currentProgress;
        [SaveableField(12)] private JournalLog _progressLog;
        [SaveableField(13)] private bool _hasInteractedWithTarget; // 用于收债或对话任务的标记
        // 必须实现的属性
        public override bool IsRemainingTimeHidden => false;
        public override TextObject Title => GetQuestTitle();
        public override bool IsSpecialQuest => false;
        public bool bMustReportToFinish => true; // 是否需要回报完成

        public static bool IsHeroInvolvedInActiveQuest(Hero heroToCheck, out GenericQuest foundQuest, out bool isGiver)
        {
            foundQuest = null;
            isGiver = false;

            if (heroToCheck == null) return false;

            // 遍历战役中所有正在进行的任务
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                // 1. 筛选：只检查我们这个 Mod 生成的 GenericQuest 类型
                if (quest is GenericQuest myQuest)
                {
                    // 2. 检查身份 A：是否是“主公” (任务发布者)
                    // QuestGiver 是 QuestBase 自带的属性
                    if (myQuest.QuestGiver == heroToCheck)
                    {
                        foundQuest = myQuest;
                        isGiver = true; // 是发布任务的老板
                        return true;
                    }

                    // 3. 检查身份 B：是否是“目标” (比如要去劝诱/暗杀/送礼的对象)
                    // 需要访问我们在 QuestData 里定义的 TargetHero
                    if (myQuest._data != null && myQuest._data.TargetHero == heroToCheck)
                    {
                        foundQuest = myQuest;
                        isGiver = false; // 是被执行的目标
                        return true;
                    }
                }
            }

            // 既不是发布者，也不是目标
            return false;
        }


        public GenericQuest(string questId, Hero questGiver, CampaignTime duration, int rewardGold, QuestData data)
            : base(questId, questGiver, CampaignTime.Now + duration, rewardGold)
        {
            _data = data;
            _currentProgress = 0;
            _hasInteractedWithTarget = false;

            InitializeStartValues();

            SetDialogs();
        }

        // 初始化基准值（用于开发类任务计算增量）
        private void InitializeStartValues()
        {
            if (string.IsNullOrEmpty(_data.TargetSettlementId)) return;

            var settlement = Settlement.Find(_data.TargetSettlementId);
            if (settlement == null) return;

            switch (_data.Type)
            {
                case QuestType.DevelopSettlement_Food:
                    _data.StartValue = settlement.Town.FoodStocks;
                    break;
                case QuestType.DevelopSettlement_Prosperity:
                    _data.StartValue = settlement.Town.Prosperity;
                    break;
                case QuestType.DevelopSettlement_Security:
                    _data.StartValue = settlement.Town.Security;
                    break;
                case QuestType.TrainTroops:
                    // 简单起见，这里可以记录当前总经验，或者在StartQuest里做
                    break;
                case QuestType.ImproveSkill:
                    // 获取对应技能的初始值
                    var skill = MBObjectManager.Instance.GetObject<SkillObject>(_data.TargetId);
                    if (skill != null)
                        _data.StartValue = Hero.MainHero.GetSkillValue(skill);
                    break;
            }
        }
        private TextObject GetQuestTitle()
        {
            return new TextObject(_data.GetQuestTitle());           
        }

  
        protected override void OnStartQuest()
        {
            SetDialogs();

            // --- 核心逻辑：发放本金/物资 ---
          




            // 生成任务描述
            TextObject description = new TextObject("任务简报: {TASK_DESC}\n任务发起：{TASK_GIVEN}\n任务原文：{TASK_MSG}\n");
            description.SetTextVariable("TASK_GIVEN", QuestGiver.Name);
            description.SetTextVariable("TASK_MSG", _data.GetQuestDescription());
            description.SetTextVariable("TASK_DESC", GetQuestDescription());
            description.SetTextVariable("TARGET", GetTargetText());
            description.SetTextVariable("REWARD", RewardGold);
            description.SetTextVariable("FAILURE_DESC", GetFailureDesc());
            AddLog(description);

            if (_data.GivenGold > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(QuestGiver, Hero.MainHero, _data.GivenGold);
                AddLog(new TextObject($"主公赐予了 {_data.GivenGold} 两作为起始资金。"));
            }

            if (!string.IsNullOrEmpty(_data.GivenItemId) && _data.GivenItemCount > 0)
            {
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(_data.GivenItemId);
                if (item != null)
                {
                    PartyBase.MainParty.ItemRoster.AddToCounts(item, _data.GivenItemCount);
                    AddLog(new TextObject($"主公赐予了 {_data.GivenItemCount} 个 {item.Name}。"));
                }
            }

            // 进度条逻辑
            if (_data.TargetCount > 0)
            {
                TextObject progressTitle = new TextObject("{=q_progress}当前进度");
                TextObject progressDetail = new TextObject("{=q_detail}完成度");
                _progressLog = AddDiscreteLog(progressTitle, progressDetail, _currentProgress, _data.TargetCount);
            }
        }
        private TextObject GetTargetText()
        {
            // 简单的目标文本格式化
            return new TextObject(_data.TargetCount.ToString());
        }
        public string GetFailureDesc()
        {
            return "失去了主公的信任，声望与关系下降。如果挪用了军费，后果更严重。";
        }

        private string GetQuestDescription()
        {
            string sName = !string.IsNullOrEmpty(_data.TargetSettlementId) ? Settlement.Find(_data.TargetSettlementId)?.Name.ToString() : "目标地点";
            string hName = _data.TargetHero != null ? _data.TargetHero.Name.ToString() : "目标人物";
            string itemName = !string.IsNullOrEmpty(_data.TargetId) && !IsSkill(_data.TargetId) ? MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetId)?.Name.ToString() ?? _data.TargetId : "物品";
            string skillName = !string.IsNullOrEmpty(_data.TargetId) && IsSkill(_data.TargetId) ? MBObjectManager.Instance.GetObject<SkillObject>(_data.TargetId)?.Name.ToString() ?? "技能" : "技能";

            switch (_data.Type)
            {
                // 物资类
                case QuestType.DeliverItem_Special: return $"寻找并带回贵重品：{itemName}。";
                case QuestType.DeliverItem_Food: return $"为主家筹集 {itemName} (军粮) 共 {_data.TargetCount} 个。";
                case QuestType.DeliverItem_Horse: return $"购入军马 {itemName} 共 {_data.TargetCount} 匹。";
                case QuestType.DeliverItem_Gun: return $"筹集铁炮/火器 {itemName} 共 {_data.TargetCount} 挺。";
                case QuestType.EarnMoney: return $"开展商业活动，上缴 {_data.TargetCount} 军资金。";
                case QuestType.EarnMoney_SellFood: return $"前往 {sName} 或其他高价区卖出军粮，获利 {_data.TargetCount}。";
                case QuestType.CollectDebt: return $"前往寻找 {hName}，追回欠款 {_data.TargetCount}。";

                // 军事类
                case QuestType.HuntBandits: return $"讨伐附近的强盗/山贼，击败 {_data.TargetCount} 队/人。";
                case QuestType.RecruitTroops: return $"征召士兵，使部队中增加 {_data.TargetCount} 名新兵/指定兵种。";
                case QuestType.TrainTroops: return $"训练部队，获得 {_data.TargetCount} 经验值或晋升士兵。";
                case QuestType.RaidVillage: return $"袭击敌方村庄 {sName}。";
                case QuestType.Assault: return $"袭击某人 {hName} 的部队。";
                case QuestType.CaptureSetlement: return $"攻陷敌方据点 {sName}。";

                // 内政类
                case QuestType.DevelopSettlement_Food: return $"前往 {sName} 进行新田开发，提升粮食产量/库存 {_data.TargetCount} 点。";
                case QuestType.DevelopSettlement_Prosperity: return $"前往 {sName} 增筑，提升繁荣度 {_data.TargetCount} 点。";
                case QuestType.DevelopSettlement_Security: return $"前往 {sName} 巡逻，提升治安度 {_data.TargetCount} 点。";

                // 外交类
                case QuestType.DiplomacyTalk_War: return $"运作外交，促成对目标势力的宣战。";
                case QuestType.DiplomacyTalk_Alliance: return $"出使 {sName}，促成与该势力的同盟。";
                case QuestType.DiplomacyTalk_Peace: return $"出使 {sName}，促成停战/媾和。";
                case QuestType.DiplomacyTalk_SubOrdination: return $"迫使/劝说 {hName} 的家族从属我方。";
                case QuestType.DiplomacyTalk_Dominate: return $"在外交上支配目标势力。";
                case QuestType.ScoutSettlement: return $"潜入/侦查 {sName}，获取情报。";
                case QuestType.Sabotage: return $"对 {sName} 进行破坏/流言，降低其城防或忠诚度。";
                case QuestType.RecruitHero: return $"延揽浪人 {hName} 加入我方家族。";
                case QuestType.PersuadeLord: return $"劝诱敌方领主 {hName} 倒戈。";

                // 个人类
                case QuestType.ImproveSkill: return $"进行修业，提升 {skillName} 技能 {_data.TargetCount} 点。";
                case QuestType.WinArena: return $"在竞技大会中获得 {_data.TargetCount} 次优胜。";
                case QuestType.EscortCaravan: return $"护送商队/人物到达 {sName}。";
                case QuestType.Promise: return $"履行你对 {hName} 的承诺。";

                default: return "完成主公的主命。";
            }
        }
        private bool IsSkill(string id) => MBObjectManager.Instance.GetObject<SkillObject>(id) != null;
        // 根据类型生成描述文本


        protected override void RegisterEvents()
        {
            // 1. 通用每日检查（用于状态类任务：内政、外交状态、技能检测）
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);

            // 2. 针对性事件监听
            switch (_data.Type)
            {
                // 物资类：通过每日检查背包，或者监听物品交换（这里选物品交换更实时）
                case QuestType.DeliverItem_Special:
                case QuestType.DeliverItem_Food:
                case QuestType.DeliverItem_Horse:
                case QuestType.DeliverItem_Gun:
                case QuestType.EarnMoney: // 赚钱通常需要玩家主动上交，但也可以检测玩家携带金钱
                    CampaignEvents.PlayerInventoryExchangeEvent.AddNonSerializedListener(this, OnInventoryExchange);
                    break;

                // 战斗类
                case QuestType.HuntBandits:
                case QuestType.Assault:
                    CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
                    break;

                case QuestType.RaidVillage:
                    CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
                    break;

                case QuestType.CaptureSetlement:
                //    CampaignEvents.SettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
                    break;

                case QuestType.RecruitTroops:
                    // 监听招募不太准，不如监听每日兵员检查或部队改变
                    CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
                    break;

                // 竞技场
                case QuestType.WinArena:
                    CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
                    break;

                // 侦查/访问
                case QuestType.ScoutSettlement:
                case QuestType.CollectDebt: // 简单处理：进城即触发（或进城后通过菜单触发）
                case QuestType.EscortCaravan:
                    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
                    break;

                    // 破坏/外交/人才 
                    // 这些通常很复杂，可以通过 DailyTick 检查状态，或者监听特定的 Dialog Event
                    // 为简化，这里主要依靠 DailyTick 检查结果
            }
        }
        // --- 事件处理逻辑 ---

        private void OnDailyTick()
        {
            // 处理内政开发类 (每天检查数值)
            if (_data.Type.ToString().StartsWith("DevelopSettlement"))
            {
                var settlement = Settlement.Find(_data.TargetSettlementId);
                if (settlement != null)
                {
                    float currentVal = 0;
                    if (_data.Type == QuestType.DevelopSettlement_Food) currentVal = settlement.Town.FoodStocks;
                    if (_data.Type == QuestType.DevelopSettlement_Prosperity) currentVal = settlement.Town.Prosperity;
                    if (_data.Type == QuestType.DevelopSettlement_Security) currentVal = settlement.Town.Security;

                    int progress = (int)(currentVal - _data.StartValue);
                    UpdateProgress(progress);
                }
            }

            // 处理修业 (技能提升)
            if (_data.Type == QuestType.ImproveSkill)
            {
                var skill = MBObjectManager.Instance.GetObject<SkillObject>(_data.TargetId);
                if (skill != null)
                {
                    int currentVal = Hero.MainHero.GetSkillValue(skill);
                    int progress = (int)(currentVal - _data.StartValue);
                    UpdateProgress(progress);
                }
            }

            // 处理训练 (简单处理：检查已有XP或等级，较难量化每日增量，这里假设TargetCount是目标等级兵种的数量)
            if (_data.Type == QuestType.TrainTroops)
            {
                // 假设逻辑：队伍里有多少个Tier >= TargetCount 的士兵
                int count = 0;
                foreach (var element in PartyBase.MainParty.MemberRoster.GetTroopRoster())
                {
                    if (element.Character.Tier >= _data.TargetCount) // 这里复用TargetCount作为目标等级
                    {
                        count += element.Number;
                    }
                }
                // 这里不算进度累加，而是看是否达标
                if (count >= 10) // 假设需要10个高等级兵
                {
                    CompleteQuestWithSuccess();
                }
            }

            // 处理外交状态检查
            CheckDiplomacyStatus();
        }

        private void CheckDiplomacyStatus()
        {
            // 简单检查状态是否达成
            IFaction myFaction = QuestGiver.Clan.MapFaction;
            IFaction targetFaction = null;
            if (!string.IsNullOrEmpty(_data.TargetSettlementId))
                targetFaction = Settlement.Find(_data.TargetSettlementId)?.MapFaction;
            else if (_data.TargetHero != null)
                targetFaction = _data.TargetHero.MapFaction;

            if (myFaction == null || targetFaction == null) return;

            switch (_data.Type)
            {
                case QuestType.DiplomacyTalk_War:
                    if (myFaction.IsAtWarWith(targetFaction)) CompleteQuestWithSuccess();
                    break;
                case QuestType.DiplomacyTalk_Alliance:
                 //   if (myFaction.IsAlliedWith(targetFaction)) CompleteQuestWithSuccess(); // Bannerlord原生无同盟，需Mod支持
                    break;
                case QuestType.DiplomacyTalk_Peace:
                    if (!myFaction.IsAtWarWith(targetFaction)) CompleteQuestWithSuccess();
                    break;
                case QuestType.RecruitHero:
                    if (_data.TargetHero.Clan == Clan.PlayerClan) CompleteQuestWithSuccess();
                    break;
                case QuestType.PersuadeLord:
                    if (_data.TargetHero.MapFaction == myFaction) CompleteQuestWithSuccess();
                    break;
            }
        }

        private void OnInventoryExchange(List<ValueTuple<ItemRosterElement, int>> purchasedItems, List<ValueTuple<ItemRosterElement, int>> soldItems, bool isTrading)
        {
            if (_data.Type == QuestType.EarnMoney)
            {
                // 赚钱任务：检查当前金币是否达到目标（或者用利润计算，这里简化为持有金币）
                if (Hero.MainHero.Gold >= _data.TargetCount)
                {
                    // 注意：通常需要对话上交，这里简化为达成条件提示
                    AddLog(new TextObject("{=info}资金已筹集完毕，请向主公汇报。"));
                    // 可以设置一个标志位 _readyToReport = true;
                }
            }
            else if (_data.Type.ToString().StartsWith("DeliverItem"))
            {
                var itemObj = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetId);
                if (itemObj != null)
                {
                    int count = PartyBase.MainParty.ItemRoster.GetItemNumber(itemObj);
                    UpdateProgress(count, true); // 覆盖式更新进度
                }
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!mapEvent.IsPlayerMapEvent || mapEvent.WinningSide != mapEvent.PlayerSide) return;

            if (_data.Type == QuestType.HuntBandits)
            {
                // 检查敌方是否为强盗
                bool foughtBandits = mapEvent.InvolvedParties.Any(p => p.Side != mapEvent.PlayerSide && p.MobileParty != null && p.MobileParty.IsBandit);
                if (foughtBandits)
                {
                    UpdateProgress(_currentProgress + 1);
                }
            }
            else if (_data.Type == QuestType.Assault && _data.TargetHero != null)
            {
                // 检查是否击败了特定Hero
             //   bool foughtTarget = mapEvent.InvolvedParties.Any(p => p.Side != mapEvent.PlayerSide && p.Party.Owner == _data.TargetHero);
             //   if (foughtTarget) CompleteQuestWithSuccess();
            }
        }
        private void UpdateProgress(int newProgress, bool isOverride = false)
        {
            if (isOverride) _currentProgress = newProgress;
            else _currentProgress = newProgress;

            // 限制
            // if (_currentProgress > _data.TargetCount) _currentProgress = _data.TargetCount;

            if (_progressLog != null)
            {
                UpdateQuestTaskStage(_progressLog, _currentProgress);
            }

            if (_currentProgress >= _data.TargetCount)
            {
                // 对于收集类任务，通常满了不自动完成，需要回去交任务
                // 但对于杀敌、开发类，满了可以自动完成
                if (_data.Type != QuestType.DeliverItem_Food &&
                    _data.Type != QuestType.DeliverItem_Horse &&
                    _data.Type != QuestType.DeliverItem_Gun &&
                    _data.Type != QuestType.EarnMoney)
                {
                    CompleteQuestWithSuccess();
                }
                else
                {
                    if (!_hasInteractedWithTarget) // 防止重复弹窗
                    {
                        AddLog(new TextObject("{=ready}任务目标已达成，请返回向主公复命。"));
                        _hasInteractedWithTarget = true;
                    }
                }
            }
        }

        protected override void OnCompleteWithSuccess()
        {
            AddLog(new TextObject("{=success}任务完成！主公对此表示赞赏。"));

            // 奖励结算
            GiveGoldAction.ApplyBetweenCharacters(QuestGiver, Hero.MainHero, RewardGold);
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, 5);
            GainRenownAction.Apply(Hero.MainHero, 2);

            // 如果是赚钱任务，需要把本金+利润上交 (模拟扣除)
            if (_data.Type == QuestType.EarnMoney)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, QuestGiver, _data.TargetCount);
            }
            // 如果是物资任务，扣除物品
            if (_data.Type.ToString().StartsWith("DeliverItem"))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetId);
                if (item != null)
                {
                    PartyBase.MainParty.ItemRoster.AddToCounts(item, -_data.TargetCount);
                }
            }
        }
        private void OnVillageLooted(Village village)
        {
            if (_data.Type == QuestType.RaidVillage && village.Settlement.StringId == _data.TargetSettlementId)
            {
                // 需要确认是玩家烧的
                if (village.Settlement.LastAttackerParty == MobileParty.MainParty)
                    CompleteQuestWithSuccess();
            }
        }
        /*
        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeSettlementOwnerEvent.CType cause)
        {
            if (_data.Type == QuestType.CaptureSetlement && settlement.StringId == _data.TargetSettlementId)
            {
                // 如果被玩家所在势力占领
                if (newOwner.MapFaction == Hero.MainHero.MapFaction)
                    CompleteQuestWithSuccess();
            }
        }
        */

        private void OnTroopRecruited(Hero hero, Settlement settlement, Hero troopSource, CharacterObject troop, int count)
        {
            if (hero == Hero.MainHero && _data.Type == QuestType.RecruitTroops)
            {
                // 简单累加招募数量
                UpdateProgress(_currentProgress + count);
            }
        }

        private void OnTournamentFinished(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            if (_data.Type == QuestType.WinArena && winner == Hero.MainHero.CharacterObject)
            {
                UpdateProgress(_currentProgress + 1);
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty) return;

            if (settlement.StringId == _data.TargetSettlementId)
            {
                if (_data.Type == QuestType.ScoutSettlement)
                {
                    AddLog(new TextObject("已到达侦查地点。"));
                    CompleteQuestWithSuccess();
                }
                else if (_data.Type == QuestType.CollectDebt)
                {
                    // 这里应该触发Dialog，简化为直接完成
                    // 实际建议：设置 _hasInteractedWithTarget = true; 然后弹出菜单
                }
            }
        }
        protected override void OnTimedOut()
        {
            // 失败逻辑：扣关系，如果有本金没还，关系扣更多
            int relationPenalty = -5;
            if (_data.GivenGold > 0) relationPenalty = -15; // 贪污军费后果严重

            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, relationPenalty);
            AddLog(new TextObject(GetFailureDesc()));
        }
        protected override void SetDialogs()
        {

        }

        // 必须实现的初始化方法
        protected override void InitializeQuestOnGameLoad()
        {
            SetDialogs();
        }
        
        // 3. 侦查/访问逻辑
      


      

        protected override void HourlyTick()
        {
            // 必须实现，留空
        }

        
        public void Debug_ForceSuccess()
        {
            CompleteQuestWithSuccess();
        }
        public void Debug_ForceTimeout()
        {
            // 你可以使用 CompleteQuestWithTimeOut() 或者 CompleteQuestWithFail()
            CompleteQuestWithTimeOut();
        }
        public void Debug_AddProgress(int amount)
        {
            _currentProgress += amount;

            // 实时更新日志（如果有进度条日志的话需要在这里更新，这里仅弹个提示）
            InformationManager.DisplayMessage(new InformationMessage($"[Debug] 进度已更新: {_currentProgress}/{_data.TargetCount}"));
            if (_progressLog != null)
            {
                UpdateQuestTaskStage(_progressLog, _currentProgress);
            }
            // 检查是否满足完成条件
            if (_currentProgress >= _data.TargetCount)
            {
                //
                if(!bMustReportToFinish)
                    CompleteQuestWithSuccess();
            }
        }
        public bool IsReadyToReport()
        {
            // 假设 TargetCount 达成即为可提交
            // 注意：如果是 DeliverItem，通常需要玩家背包里有东西，这里简单判定进度
            return _currentProgress >= _data.TargetCount;
        }
        public string GetDiscussionTopic()
        {
            return _data.GetQuestTitle();
        }
        public bool IsRelatedTo(Hero hero)
        {
            if (hero == null) return false;
            // 是发布者？
            if (QuestGiver == hero) return true;
            // 是目标人物？(比如收债任务，要找债主对话)
            if (_data.TargetHero == hero) return true;

            return false;
        }


        public override void OnFailed()
        {
            // 惩罚：大幅扣除好感度 (-10)
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -10);

            // 惩罚：扣除声望 (-10)
            GainRenownAction.Apply(Hero.MainHero, -10);

            AddLog(new TextObject("{=q_failed}You have failed the mission disgracefully."));
        }



        [CommandLineFunctionality.CommandLineArgumentFunction("quest_add_progress", "custom")]
        public static string ExecuteAddProgress(List<string> args)
        {
            // 1. 找到当前正在进行的 GenericQuest
            // 注意：如果你同时接了多个 GenericQuest，这里默认取第一个
            var activeQuest = Campaign.Current.QuestManager.Quests
                                .FirstOrDefault(q => q is GenericQuest) as GenericQuest;

            if (activeQuest == null)
            {
                return "do not find GenericQuest。";
            }

            int amount = 1;
            if (args.Count > 0)
            {
                int.TryParse(args[0], out amount);
            }

            // 调用实例方法修改数据
            activeQuest.Debug_AddProgress(amount);

            return $"add {amount} progress  success for '{activeQuest.Title}' 。";
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("quest_finish", "custom")]
        public static string ExecuteForceFinish(List<string> args)
        {
            var activeQuest = Campaign.Current.QuestManager.Quests
                                .FirstOrDefault(q => q is GenericQuest) as GenericQuest;

            if (activeQuest == null) return "do not find quest.";

            activeQuest.Debug_ForceSuccess();
            return "quest force success.";
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("quest_timeout", "custom")]
        public static string ExecuteForceTimeout(List<string> args)
        {
            var activeQuest = Campaign.Current.QuestManager.Quests
                                .FirstOrDefault(q => q is GenericQuest) as GenericQuest;

            if (activeQuest == null) return "do not find quest。";

            activeQuest.Debug_ForceTimeout();
            return "quest force failure.";
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("quest_create_test", "custom")]
        public static string ExecuteCreateQuest(List<string> args)
        {
            QuestType type = QuestType.EarnMoney;
            string targetId = "";
            int targetCount = 3000;
            if (args.Count >=2)
            {
                Enum.TryParse(args[0], out type);
                targetId = args[1];
                int.TryParse(args[2], out targetCount);
            }


            QuestData missionData = new QuestData();
            missionData.Type = type;
            missionData.TargetId = targetId;
            missionData.TargetCount = targetCount;
            missionData.GivenGold = 1000; // 例如：给500金币的本金

            Action createQuest = () =>
            {
                var quest = new GenericQuest("lord_mission_grain", Hero.MainHero.Clan.Kingdom.Leader, CampaignTime.Days(30), 1000, missionData);
                quest.StartQuest();
            };

            Hero leader = Hero.MainHero.Clan.Kingdom.Leader;
            Hero player = Hero.MainHero;
            Action action = () => {
                InformationManager.ShowInquiry(new InquiryData($"{leader.Name}的信件", $"{player.Name}:{missionData.GetQuestDescription()}", true, false, "遵命", "", createQuest, null));               
            };
            NinjaNotificationManager.Show($"{leader.Name}大人来信了", action);

           
            return "quest create success";
        }
    }


   
}
