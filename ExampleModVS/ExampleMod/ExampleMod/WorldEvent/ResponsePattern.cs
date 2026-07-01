namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 对一个 WorldEvent 可采取的行动模板。
    /// 和玩家的 PlayerGeneratedOption 是同一抽象——"行为空间"——只是 actor 不同。
    /// </summary>
    public enum ResponsePattern
    {
        DemandRestitution,     // 要求赔偿
        GoEasy,                // 宽容/包庇
        ExtortBribe,           // 索贿封口
        IssueBounty,           // 发布悬赏
        ReportToLord,          // 上报领主
        SendThugs,             // 派打手教训
        LeadRetaliation,       // 组织报复队
        AmplifyPunishment,     // 加码追责（信号：赔偿/赏金翻倍）
        Intimidate,            // 被威胁后忍气吞声
        Indifferent            // 冷漠/不了了之
    }
}
