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
            // 兜底：目标名称（未指定时显示“目标”）
            string targetName = TargetHero != null ? TargetHero.Name.ToString() : (TargetId ?? LWNTextHelper.ResolveText("LWN_quest_manager_target", "target"));
            // 兜底：目标地点名称
            string locationName = TargetSettlementId ?? LWNTextHelper.ResolveText("LWN_quest_manager_target_location", "target location");
            // 兜底：物资名称
            string itemName = TargetId ?? LWNTextHelper.ResolveText("LWN_quest_manager_supplies", "supplies");

            return Type switch
            {
                // 任务描述：筹集指定物资 ×数量
                QuestType.DeliverItem_Food => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_gather_supplies", "Gather {ITEM} x{COUNT}.", ("ITEM", itemName), ("COUNT", TargetCount.ToString())),
                // 任务描述：筹集军马 ×数量
                QuestType.DeliverItem_Horse => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_gather_horses", "Gather war horses: {ITEM} x{COUNT}.", ("ITEM", itemName), ("COUNT", TargetCount.ToString())),
                // 任务描述：筹集装备 ×数量
                QuestType.DeliverItem_Gun => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_gather_equipment", "Gather equipment: {ITEM} x{COUNT}.", ("ITEM", itemName), ("COUNT", TargetCount.ToString())),
                // 任务描述：寻找贵重品
                QuestType.DeliverItem_Special => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_find_valuables", "Find a valuable item: {ITEM}.", ("ITEM", itemName)),
                // 任务描述：筹集资金数额
                QuestType.EarnMoney => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_raise_funds", "Raise {GOLD} gold.", ("GOLD", TargetCount.ToString())),
                // 任务描述：卖出军粮获利数额
                QuestType.EarnMoney_SellFood => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_sell_food_profit", "Profit {GOLD} gold from selling grain.", ("GOLD", TargetCount.ToString())),
                // 任务描述：向目标人物收回欠款
                QuestType.CollectDebt => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_collect_debt", "Collect the debt from {NAME}.", ("NAME", targetName)),
                // 任务描述：讨伐贼寇队伍数
                QuestType.HuntBandits => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_hunt_bandits", "Defeat {COUNT} bandit bands.", ("COUNT", TargetCount.ToString())),
                // 任务描述：招募士兵数量
                QuestType.RecruitTroops => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_recruit_soldiers", "Recruit {COUNT} soldiers.", ("COUNT", TargetCount.ToString())),
                // 任务描述：训练部队达到标准
                QuestType.TrainTroops => LWNTextHelper.ResolveText("LWN_quest_manager_desc_train_troops", "Train your troops to the required standard."),
                // 任务描述：劫掠目标地点
                QuestType.RaidVillage => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_raid_village", "Raid {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：袭击目标人物
                QuestType.Assault => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_assault_target", "Attack {NAME}.", ("NAME", targetName)),
                // 任务描述：攻占目标地点
                QuestType.CaptureSetlement => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_capture_settlement", "Capture {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：开发目标地点粮食产量
                QuestType.DevelopSettlement_Food => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_develop_food", "Develop food production in {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：提升目标地点繁荣度
                QuestType.DevelopSettlement_Prosperity => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_develop_prosperity", "Increase prosperity in {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：提升目标地点治安
                QuestType.DevelopSettlement_Security => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_develop_security", "Improve security in {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：向目标势力宣战
                QuestType.DiplomacyTalk_War => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_declare_war", "Declare war on {NAME}.", ("NAME", targetName)),
                // 任务描述：与目标势力结盟
                QuestType.DiplomacyTalk_Alliance => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_form_alliance", "Form an alliance with {NAME}.", ("NAME", targetName)),
                // 任务描述：与目标势力媾和
                QuestType.DiplomacyTalk_Peace => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_make_peace", "Make peace with {NAME}.", ("NAME", targetName)),
                // 任务描述：使目标势力从属
                QuestType.DiplomacyTalk_SubOrdination => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_subjugate", "Bring {NAME} under your sway.", ("NAME", targetName)),
                // 任务描述：支配目标势力
                QuestType.DiplomacyTalk_Dominate => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_dominate", "Dominate {NAME}.", ("NAME", targetName)),
                // 任务描述：侦查目标地点
                QuestType.ScoutSettlement => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_scout_settlement", "Scout out {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：破坏目标地点
                QuestType.Sabotage => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_sabotage", "Sabotage {LOCATION}.", ("LOCATION", locationName)),
                // 任务描述：招募目标人物
                QuestType.RecruitHero => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_recruit_hero", "Recruit {NAME}.", ("NAME", targetName)),
                // 任务描述：劝诱目标人物
                QuestType.PersuadeLord => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_persuade_lord", "Persuade {NAME} to defect.", ("NAME", targetName)),
                // 任务描述：提升技能到目标等级
                QuestType.ImproveSkill => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_improve_skill", "Raise the {SKILL} skill to level {LEVEL}.", ("SKILL", itemName), ("LEVEL", TargetCount.ToString())),
                // 任务描述：在竞技场获胜次数
                QuestType.WinArena => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_win_arena", "Win the arena {COUNT} times.", ("COUNT", TargetCount.ToString())),
                // 任务描述：护送商队到目的地
                QuestType.EscortCaravan => LWNTextHelper.ResolveText("LWN_quest_manager_desc_escort_caravan", "Escort the caravan to its destination."),
                // 任务描述：履行对目标人物的承诺
                QuestType.Promise => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_keep_promise", "Keep your promise to {NAME}.", ("NAME", targetName)),
                // 兜底：未知任务类型的描述
                _ => LWNTextHelper.ResolveCompound("LWN_quest_manager_desc_unknown", "{TYPE}: {COUNT}.", ("TYPE", Type.ToString()), ("COUNT", TargetCount.ToString())),
            };
        }

        public string GetQuestTitle()
        {
            return Type switch
            {
                // 任务标题：筹集军粮
                QuestType.DeliverItem_Food => LWNTextHelper.ResolveText("LWN_quest_manager_title_food_supply", "Gather Grain"),
                // 任务标题：筹集军马
                QuestType.DeliverItem_Horse => LWNTextHelper.ResolveText("LWN_quest_manager_title_gather_horses", "Gather War Horses"),
                // 任务标题：筹集装备
                QuestType.DeliverItem_Gun => LWNTextHelper.ResolveText("LWN_quest_manager_title_gather_equipment", "Gather Equipment"),
                // 任务标题：筹集资金
                QuestType.EarnMoney => LWNTextHelper.ResolveText("LWN_quest_manager_title_raise_funds", "Raise Funds"),
                // 任务标题：卖出军粮
                QuestType.EarnMoney_SellFood => LWNTextHelper.ResolveText("LWN_quest_manager_title_sell_food", "Sell Grain"),
                // 任务标题：收回欠款
                QuestType.CollectDebt => LWNTextHelper.ResolveText("LWN_quest_manager_title_collect_debt", "Collect Debts"),
                // 任务标题：讨伐贼寇
                QuestType.HuntBandits => LWNTextHelper.ResolveText("LWN_quest_manager_title_hunt_bandits", "Hunt Bandits"),
                // 任务标题：征兵
                QuestType.RecruitTroops => LWNTextHelper.ResolveText("LWN_quest_manager_title_recruit_troops", "Recruit Troops"),
                // 任务标题：训练
                QuestType.TrainTroops => LWNTextHelper.ResolveText("LWN_quest_manager_title_train_troops", "Train"),
                // 任务标题：劫掠
                QuestType.RaidVillage => LWNTextHelper.ResolveText("LWN_quest_manager_title_raid", "Raid"),
                // 任务标题：占领据点
                QuestType.CaptureSetlement => LWNTextHelper.ResolveText("LWN_quest_manager_title_capture_settlement", "Capture Stronghold"),
                // 任务标题：开发粮食
                QuestType.DevelopSettlement_Food => LWNTextHelper.ResolveText("LWN_quest_manager_title_develop_food", "Develop Food Production"),
                // 任务标题：增筑
                QuestType.DevelopSettlement_Prosperity => LWNTextHelper.ResolveText("LWN_quest_manager_title_develop_prosperity", "Expand Construction"),
                // 任务标题：提升治安
                QuestType.DevelopSettlement_Security => LWNTextHelper.ResolveText("LWN_quest_manager_title_develop_security", "Improve Security"),
                // 任务标题：宣战
                QuestType.DiplomacyTalk_War => LWNTextHelper.ResolveText("LWN_quest_manager_title_declare_war", "Declare War"),
                // 任务标题：结盟
                QuestType.DiplomacyTalk_Alliance => LWNTextHelper.ResolveText("LWN_quest_manager_title_form_alliance", "Form Alliance"),
                // 任务标题：媾和
                QuestType.DiplomacyTalk_Peace => LWNTextHelper.ResolveText("LWN_quest_manager_title_make_peace", "Make Peace"),
                // 任务标题：从属
                QuestType.DiplomacyTalk_SubOrdination => LWNTextHelper.ResolveText("LWN_quest_manager_title_subjugate", "Subjugate"),
                // 任务标题：支配
                QuestType.DiplomacyTalk_Dominate => LWNTextHelper.ResolveText("LWN_quest_manager_title_dominate", "Dominate"),
                // 任务标题：侦查情报
                QuestType.ScoutSettlement => LWNTextHelper.ResolveText("LWN_quest_manager_title_scout", "Scout for Intelligence"),
                // 任务标题：破坏
                QuestType.Sabotage => LWNTextHelper.ResolveText("LWN_quest_manager_title_sabotage", "Sabotage"),
                // 任务标题：人才招募
                QuestType.RecruitHero => LWNTextHelper.ResolveText("LWN_quest_manager_title_recruit_hero", "Recruit Talent"),
                // 任务标题：劝诱
                QuestType.PersuadeLord => LWNTextHelper.ResolveText("LWN_quest_manager_title_persuade_lord", "Persuade"),
                // 任务标题：修业
                QuestType.ImproveSkill => LWNTextHelper.ResolveText("LWN_quest_manager_title_improve_skill", "Skill Training"),
                // 任务标题：竞技场优胜
                QuestType.WinArena => LWNTextHelper.ResolveText("LWN_quest_manager_title_win_arena", "Arena Victory"),
                // 任务标题：护送
                QuestType.EscortCaravan => LWNTextHelper.ResolveText("LWN_quest_manager_title_escort", "Escort"),
                // 任务标题：履行承诺
                QuestType.Promise => LWNTextHelper.ResolveText("LWN_quest_manager_title_keep_promise", "Keep Promise"),
                // 任务标题：袭击
                QuestType.Assault => LWNTextHelper.ResolveText("LWN_quest_manager_title_assault", "Attack"),
                // 任务标题：寻找宝物
                QuestType.DeliverItem_Special => LWNTextHelper.ResolveText("LWN_quest_manager_title_find_valuables", "Find Valuables"),
                // 兜底：主命（默认任务标题）
                _ => LWNTextHelper.ResolveText("LWN_quest_manager_title_master_order", "Master's Order"),
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
            TextObject description = new TextObject("{=LWN_quest_manager_briefing}Mission Briefing: {TASK_DESC}\nCommissioned by: {TASK_GIVEN}\nOriginal Orders: {TASK_MSG}\n");
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
                // 日志：主公赐予起始资金（两为旧制货币单位）
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_manager_gift_gold_log", "The lord granted you {GOLD} gold as starting funds.", ("GOLD", _data.GivenGold.ToString()))));
            }

            if (!string.IsNullOrEmpty(_data.GivenItemId) && _data.GivenItemCount > 0)
            {
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(_data.GivenItemId);
                if (item != null)
                {
                    AgentControlHelper.TransferItems(null, Hero.MainHero, item, _data.GivenItemCount);
                    // 日志：主公赐予物资数量与名称
                    AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_manager_gift_item_log", "The lord granted you {COUNT} {ITEM}.", ("COUNT", _data.GivenItemCount.ToString()), ("ITEM", item.Name.ToString()))));
                }
            }

            // 进度条逻辑
            if (_data.TargetCount > 0)
            {
                // 进度条标题：当前进度
                TextObject progressTitle = new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_progress_title", "Current Progress"));
                // 进度条详情：完成度
                TextObject progressDetail = new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_progress_detail", "Completion"));
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
            // 失败描述：失去主公信任、声望与关系下降；挪用军费后果更严重
            return LWNTextHelper.ResolveText("LWN_quest_manager_failure_desc", "You have lost the lord's trust; your renown and relations decline. If you embezzled military funds, the consequences are far worse.");
        }

        private string GetQuestDescription()
        {
            // 兜底：目标地点名称
            string sName = !string.IsNullOrEmpty(_data.TargetSettlementId) ? Settlement.Find(_data.TargetSettlementId)?.Name.ToString() : LWNTextHelper.ResolveText("LWN_quest_manager_target_location", "target location");
            // 兜底：目标人物名称
            string hName = _data.TargetHero != null ? _data.TargetHero.Name.ToString() : LWNTextHelper.ResolveText("LWN_quest_manager_target_person", "target person");
            // 兜底：物品名称
            string itemName = !string.IsNullOrEmpty(_data.TargetId) && !IsSkill(_data.TargetId) ? MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetId)?.Name.ToString() ?? _data.TargetId : LWNTextHelper.ResolveText("LWN_quest_manager_item", "item");
            // 兜底：技能名称
            string skillName = !string.IsNullOrEmpty(_data.TargetId) && IsSkill(_data.TargetId) ? MBObjectManager.Instance.GetObject<SkillObject>(_data.TargetId)?.Name.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_manager_skill", "skill") : LWNTextHelper.ResolveText("LWN_quest_manager_skill", "skill");

            switch (_data.Type)
            {
                // 物资类
                // 详细描述：寻找并带回贵重品
                case QuestType.DeliverItem_Special: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_find_valuables", "Find and bring back a valuable item: {ITEM}.", ("ITEM", itemName));
                // 详细描述：为主家筹集军粮数量
                case QuestType.DeliverItem_Food: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_gather_food", "Gather {COUNT} {ITEM} (grain) for your lord.", ("ITEM", itemName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：购入军马数量
                case QuestType.DeliverItem_Horse: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_gather_horses", "Purchase {COUNT} war horses: {ITEM}.", ("ITEM", itemName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：筹集铁炮/火器数量
                case QuestType.DeliverItem_Gun: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_gather_firearms", "Gather {COUNT} firearms: {ITEM}.", ("ITEM", itemName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：开展商业活动上缴军资金
                case QuestType.EarnMoney: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_earn_money", "Engage in commerce and pay {GOLD} war funds.", ("GOLD", _data.TargetCount.ToString()));
                // 详细描述：前往高价区卖出军粮获利
                case QuestType.EarnMoney_SellFood: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_sell_food", "Sell grain in {LOCATION} or other high-price markets, profiting {GOLD}.", ("LOCATION", sName), ("GOLD", _data.TargetCount.ToString()));
                // 详细描述：前往寻找目标人物追回欠款
                case QuestType.CollectDebt: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_collect_debt", "Seek out {NAME} and recover the debt of {GOLD}.", ("NAME", hName), ("GOLD", _data.TargetCount.ToString()));

                // 军事类
                // 详细描述：讨伐附近强盗/山贼
                case QuestType.HuntBandits: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_hunt_bandits", "Hunt down nearby bandits and defeat {COUNT} bands.", ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：征召士兵增加部队人数
                case QuestType.RecruitTroops: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_recruit_troops", "Recruit soldiers so your party gains {COUNT} new troops.", ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：训练部队获得经验或晋升
                case QuestType.TrainTroops: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_train_troops", "Train your troops, gaining {COUNT} XP or promotions.", ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：袭击敌方村庄
                case QuestType.RaidVillage: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_raid_village", "Raid the enemy village of {LOCATION}.", ("LOCATION", sName));
                // 详细描述：袭击目标人物的部队
                case QuestType.Assault: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_assault", "Attack {NAME}'s forces.", ("NAME", hName));
                // 详细描述：攻陷敌方据点
                case QuestType.CaptureSetlement: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_capture_settlement", "Capture the enemy stronghold {LOCATION}.", ("LOCATION", sName));

                // 内政类
                // 详细描述：新田开发提升粮食产量
                case QuestType.DevelopSettlement_Food: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_develop_food", "Go to {LOCATION} to develop new fields, raising food production by {COUNT} points.", ("LOCATION", sName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：增筑提升繁荣度
                case QuestType.DevelopSettlement_Prosperity: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_develop_prosperity", "Go to {LOCATION} to expand construction, raising prosperity by {COUNT} points.", ("LOCATION", sName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：巡逻提升治安度
                case QuestType.DevelopSettlement_Security: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_develop_security", "Go to {LOCATION} to patrol, raising security by {COUNT} points.", ("LOCATION", sName), ("COUNT", _data.TargetCount.ToString()));

                // 外交类
                // 详细描述：运作外交促成宣战
                case QuestType.DiplomacyTalk_War: return LWNTextHelper.ResolveText("LWN_quest_manager_desc2_declare_war", "Work diplomacy to bring about a declaration of war on the target faction.");
                // 详细描述：出使促成同盟
                case QuestType.DiplomacyTalk_Alliance: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_form_alliance", "Travel to {LOCATION} as envoy and forge an alliance with that faction.", ("LOCATION", sName));
                // 详细描述：出使促成停战/媾和
                case QuestType.DiplomacyTalk_Peace: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_make_peace", "Travel to {LOCATION} and broker an armistice.", ("LOCATION", sName));
                // 详细描述：迫使/劝说目标家族从属
                case QuestType.DiplomacyTalk_SubOrdination: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_subjugate", "Force or persuade {NAME}'s clan to submit to us.", ("NAME", hName));
                // 详细描述：外交上支配目标势力
                case QuestType.DiplomacyTalk_Dominate: return LWNTextHelper.ResolveText("LWN_quest_manager_desc2_dominate", "Dominate the target faction diplomatically.");
                // 详细描述：潜入/侦查获取情报
                case QuestType.ScoutSettlement: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_scout", "Infiltrate or scout {LOCATION} and gather intelligence.", ("LOCATION", sName));
                // 详细描述：破坏/流言降低城防或忠诚度
                case QuestType.Sabotage: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_sabotage", "Sabotage or spread rumors in {LOCATION}, lowering its defenses or loyalty.", ("LOCATION", sName));
                // 详细描述：延揽浪人加入我方家族
                case QuestType.RecruitHero: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_recruit_hero", "Recruit the ronin {NAME} into our clan.", ("NAME", hName));
                // 详细描述：劝诱敌方领主倒戈
                case QuestType.PersuadeLord: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_persuade_lord", "Persuade the enemy lord {NAME} to defect.", ("NAME", hName));

                // 个人类
                // 详细描述：修业提升技能点数
                case QuestType.ImproveSkill: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_improve_skill", "Undertake training to raise the {SKILL} skill by {COUNT} points.", ("SKILL", skillName), ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：竞技大会获胜次数
                case QuestType.WinArena: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_win_arena", "Win the tournament {COUNT} times.", ("COUNT", _data.TargetCount.ToString()));
                // 详细描述：护送商队/人物到达目的地
                case QuestType.EscortCaravan: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_escort_caravan", "Escort the caravan or person to {LOCATION}.", ("LOCATION", sName));
                // 详细描述：履行对目标人物的承诺
                case QuestType.Promise: return LWNTextHelper.ResolveCompound("LWN_quest_manager_desc2_keep_promise", "Keep your promise to {NAME}.", ("NAME", hName));

                // 兜底：完成主公的主命
                default: return LWNTextHelper.ResolveText("LWN_quest_manager_desc2_master_order", "Complete your lord's directive.");
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
                    // 日志：资金筹集完毕，提示向主公汇报
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_funds_ready_log", "The funds have been gathered. Report back to your lord.")));
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
                        // 日志：任务目标已达成，提示返回复命
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_target_reached_log", "The quest objective is complete. Return to report to your lord.")));
                        _hasInteractedWithTarget = true;
                    }
                }
            }
        }

        protected override void OnCompleteWithSuccess()
        {
            // 日志：任务完成，主公赞赏
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_success_log", "Quest complete! Your lord is pleased.")));

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
                    // 日志：已到达侦查地点
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_manager_arrived_log", "You have arrived at the scouting location.")));
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

            if (Settings.Instance.ShowDebugMessages)
                // 本地化：LWN_quest_debug_progress_added（玩家可见文本）
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_quest_debug_progress_added",
                    ("PROGRESS", _currentProgress.ToString()), ("TARGET", _data.TargetCount.ToString()))));
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
                // 信件弹窗标题：{NAME} 的来信
                string letterTitle = LWNTextHelper.ResolveCompound("LWN_quest_letter_title", ("NAME", leader.Name.ToString()));
                // 信件弹窗正文：{PLAYER}:{DESC}（委托人留言 + 任务说明）
                string letterBody = LWNTextHelper.ResolveCompound("LWN_quest_letter_body",
                    ("PLAYER", player.Name.ToString()), ("DESC", missionData.GetQuestDescription()));
                // 信件弹窗按钮：遵命
                InformationManager.ShowInquiry(new InquiryData(letterTitle, letterBody, true, false, LWNTextHelper.ResolveText("LWN_quest_letter_obey_btn", "As you command"), "", createQuest, null));
            };
            // 弹窗提示：主公大人来信了
            NinjaNotificationManager.Show(LWNTextHelper.ResolveCompound("LWN_quest_manager_letter_arrived", "A letter has arrived from Lord {NAME}.", ("NAME", leader.Name.ToString())), action);

           
            return "quest create success";
        }
    }


   
}
