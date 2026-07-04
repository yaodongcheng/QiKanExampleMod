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

namespace LivingWorldNpcs
{
    [Obsolete("Use CommissionCategory + CommissionQuest instead. Quest types are being unified under CommissionQuest.")]
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
    [Obsolete("Use CommissionData instead. Quest types are being unified under CommissionQuest.")]
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
            // 叙事文本已迁移到 QuestNarratives.csv + NarrativeResolver 管道。
            // 此方法仅作为存档兼容兜底，返回简化格式（不含世界观 flavor）。
            string targetName = TargetHero != null ? TargetHero.Name.ToString() : (TargetId ?? "目标");
            string locationName = TargetSettlementId ?? "目标地点";
            string itemName = TargetId ?? "物资";

            return Type switch
            {
                QuestType.DeliverItem_Food => $"筹集 {itemName} ×{TargetCount}。",
                QuestType.DeliverItem_Horse => $"筹集军马 {itemName} ×{TargetCount}。",
                QuestType.DeliverItem_Gun => $"筹集装备 {itemName} ×{TargetCount}。",
                QuestType.DeliverItem_Special => $"寻找贵重品：{itemName}。",
                QuestType.EarnMoney => $"筹集资金 {TargetCount}。",
                QuestType.EarnMoney_SellFood => $"卖出军粮获利 {TargetCount}。",
                QuestType.CollectDebt => $"向 {targetName} 收回欠款。",
                QuestType.HuntBandits => $"讨伐贼寇 {TargetCount} 队。",
                QuestType.RecruitTroops => $"招募 {TargetCount} 名士兵。",
                QuestType.TrainTroops => $"训练部队达到标准。",
                QuestType.RaidVillage => $"劫掠 {locationName}。",
                QuestType.Assault => $"袭击 {targetName}。",
                QuestType.CaptureSetlement => $"攻占 {locationName}。",
                QuestType.DevelopSettlement_Food => $"开发 {locationName} 粮食产量。",
                QuestType.DevelopSettlement_Prosperity => $"提升 {locationName} 繁荣度。",
                QuestType.DevelopSettlement_Security => $"提升 {locationName} 治安。",
                QuestType.DiplomacyTalk_War => $"向 {targetName} 宣战。",
                QuestType.DiplomacyTalk_Alliance => $"与 {targetName} 结盟。",
                QuestType.DiplomacyTalk_Peace => $"与 {targetName} 媾和。",
                QuestType.DiplomacyTalk_SubOrdination => $"使 {targetName} 从属。",
                QuestType.DiplomacyTalk_Dominate => $"支配 {targetName}。",
                QuestType.ScoutSettlement => $"侦查 {locationName}。",
                QuestType.Sabotage => $"破坏 {locationName}。",
                QuestType.RecruitHero => $"招募 {targetName}。",
                QuestType.PersuadeLord => $"劝诱 {targetName}。",
                QuestType.ImproveSkill => $"提升 {itemName} 技能到 {TargetCount} 级。",
                QuestType.WinArena => $"在竞技场获胜 {TargetCount} 次。",
                QuestType.EscortCaravan => $"护送商队到目的地。",
                QuestType.Promise => $"履行对 {targetName} 的承诺。",
                _ => $"{Type}：{TargetCount}。",
            };
        }

        public string GetQuestTitle()
        {
            return Type switch
            {
                QuestType.DeliverItem_Food => "筹集军粮",
                QuestType.DeliverItem_Horse => "筹集军马",
                QuestType.DeliverItem_Gun => "筹集装备",
                QuestType.EarnMoney => "筹集资金",
                QuestType.EarnMoney_SellFood => "卖出军粮",
                QuestType.CollectDebt => "收回欠款",
                QuestType.HuntBandits => "讨伐贼寇",
                QuestType.RecruitTroops => "征兵",
                QuestType.TrainTroops => "训练",
                QuestType.RaidVillage => "劫掠",
                QuestType.CaptureSetlement => "占领据点",
                QuestType.DevelopSettlement_Food => "开发粮食",
                QuestType.DevelopSettlement_Prosperity => "增筑",
                QuestType.DevelopSettlement_Security => "提升治安",
                QuestType.DiplomacyTalk_War => "宣战",
                QuestType.DiplomacyTalk_Alliance => "结盟",
                QuestType.DiplomacyTalk_Peace => "媾和",
                QuestType.DiplomacyTalk_SubOrdination => "从属",
                QuestType.DiplomacyTalk_Dominate => "支配",
                QuestType.ScoutSettlement => "侦查情报",
                QuestType.Sabotage => "破坏",
                QuestType.RecruitHero => "人才招募",
                QuestType.PersuadeLord => "劝诱",
                QuestType.ImproveSkill => "修业",
                QuestType.WinArena => "竞技场优胜",
                QuestType.EscortCaravan => "护送",
                QuestType.Promise => "履行承诺",
                QuestType.Assault => "袭击",
                QuestType.DeliverItem_Special => "寻找宝物",
                _ => "主命",
            };
        }
    }
    /// <summary>
    /// 【旧主命系统残留】Quest 直接 new 出来，不经过 Issue 管道，与当前架构（Quest 只能从 Issue 生）不一致。
    /// 旧存档里可能有进行中的 GenericQuest，反序列化需要类壳子存在，暂不删。
    /// 等主命系统正式重构（Phase C）时，全部迁移到 CommissionQuest + CommissionHubIssue 后再清理。
    /// </summary>
    [Obsolete("Use CommissionQuest instead. Quest types are being unified under CommissionQuest.")]
    public class GenericQuest : QuestBase
    {
        [SaveableField(10)] private QuestData _data;
        [SaveableField(11)] private int _currentProgress;
        [SaveableField(12)] private JournalLog _progressLog;
        [SaveableField(13)] private bool _hasInteractedWithTarget; // 用于收债或对话任务的标记
        // 必须实现的属性
        public override bool IsRemainingTimeHidden => false;
        public override TextObject Title => GetQuestTitle();
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
                AgentControlHelper.TransferGold(QuestGiver, Hero.MainHero, _data.GivenGold);
                AddLog(new TextObject($"主公赐予了 {_data.GivenGold} 两作为起始资金。"));
            }

            if (!string.IsNullOrEmpty(_data.GivenItemId) && _data.GivenItemCount > 0)
            {
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(_data.GivenItemId);
                if (item != null)
                {
                    AgentControlHelper.TransferItems(null, Hero.MainHero, item, _data.GivenItemCount);
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
            AgentControlHelper.TransferGold(QuestGiver, Hero.MainHero, RewardGold);
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, 5);
            GainRenownAction.Apply(Hero.MainHero, 2);

            // 如果是赚钱任务，需要把本金+利润上交 (模拟扣除)
            if (_data.Type == QuestType.EarnMoney)
            {
                AgentControlHelper.TransferGold(Hero.MainHero, QuestGiver, _data.TargetCount);
            }
            // 如果是物资任务，扣除物品
            if (_data.Type.ToString().StartsWith("DeliverItem"))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetId);
                if (item != null)
                {
                    AgentControlHelper.TransferItems(Hero.MainHero, null, item, _data.TargetCount);
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
