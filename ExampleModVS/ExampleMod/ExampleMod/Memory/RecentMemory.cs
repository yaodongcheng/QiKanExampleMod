using System;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    public class RecentMemory
    {
        public string Content { get; set; }
        public double TimeStamp_Start { get; set; }
        public double TimeStamp_End { get; set; }

        /// <summary>游戏内日（🔴 2026-08-16 方案 I5）：写入时同步记录游戏内日（CampaignTime.Now.ToDays），
        /// 输出时转相对词（[3天前]）；墙钟毫秒（TimeStamp_*）对游戏对话无意义，不可转换。
        /// -1 = 自动取当前游戏内日；0 = 旧存档条目 → 不标时间戳（契约兜底，宁模糊不编数）。</summary>
        public float CampaignDay { get; set; }

        public RecentMemory(string content, double timeStamp_Start, double timeStamp_End, float campaignDay = -1f)
        {
            Content = content;
            TimeStamp_Start = timeStamp_Start;
            TimeStamp_End = timeStamp_End;
            if (campaignDay < 0f)
            {
                try { CampaignDay = Campaign.Current != null ? (float)CampaignTime.Now.ToDays : 0f; } catch { CampaignDay = 0f; }
            }
            else
            {
                CampaignDay = campaignDay;
            }
        }
    }
}
