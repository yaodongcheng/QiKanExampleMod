using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 的委托/Quest 历史记录。独立于对话历史（RecentHistory），专门存储
    /// Issue 接取、Quest 完成、因果链上下文等与委托系统相关的事件。
    ///
    /// 存储于 SingNpcMemorySystem.QuestHistory，最大 20 条。
    /// UI 通过 NPCInfoVM 的"委托记录"Tab 查看。
    /// </summary>
    public class QuestRecord
    {
        /// <summary>VANILLA_* 或 LWNPCS_* Quest ID（因果链引擎用）</summary>
        public string QuestId;

        /// <summary>人类可读的委托名称（如 "清剿匪穴"）</summary>
        public string QuestName;

        /// <summary>
        /// 记录类型：
        /// "Issued"    — 玩家接取了这个委托
        /// "Completed" — 玩家完成了这个委托
        /// "Failed"    — 委托失败
        /// "Betrayed"  — 玩家叛变
        /// "Causality" — 因果链上下文（完成后产生的后续影响）
        /// </summary>
        public string RecordType;

        /// <summary>委托人姓名</summary>
        public string GiverName;

        /// <summary>发生地（定居点名）</summary>
        public string SettlementName;

        /// <summary>发生时的游戏天数</summary>
        public float CampaignDay;

        /// <summary>人类可读的一行摘要，UI 直接展示</summary>
        public string Summary;

        /// <summary>
        /// 因果链专属字段（仅 RecordType="Causality" 时有效）：
        /// 上一个完成的 QuestId，用于叙事变量 {PREVIOUS_QUEST}
        /// </summary>
        public string PreviousQuestId;

        /// <summary>
        /// 因果链专属字段：引发当前局面的关键人物名，用于叙事变量 {CAUSE_HERO}
        /// </summary>
        public string CauseHeroName;

        /// <summary>
        /// 因果链深度，用于叙事变量 {CHAIN_DEPTH}
        /// </summary>
        public int ChainDepth;

        public QuestRecord()
        {
            CampaignDay = (float)CampaignTime.Now.ToDays;
        }

        /// <summary>生成 UI 展示用的单行摘要。</summary>
        public string GetDisplaySummary()
        {
            string timeStr = $"第{(int)CampaignDay}天";
            string typeTag = RecordType switch
            {
                "Issued" => "接取",
                "Completed" => "完成",
                "Failed" => "失败",
                "Betrayed" => "叛变",
                "Causality" => "因果",
                _ => ""
            };

            if (!string.IsNullOrEmpty(Summary))
                return $"[{timeStr}] {typeTag} {Summary}";

            return $"[{timeStr}] {typeTag} {QuestName} — {GiverName}@{SettlementName}";
        }

        /// <summary>是否有因果上下文（供叙事变量注入）</summary>
        public bool HasCausalityContext =>
            RecordType == "Causality" && !string.IsNullOrEmpty(PreviousQuestId);
    }
}
