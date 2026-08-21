using System;
using System.Threading;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    public class RecentMemory
    {
        /// <summary>
        /// 会话内唯一调试编号（🔴 2026-08-21）：构造时自增分配；存档随 JSON 保留（NpcMemorySaveEntry
        /// 拷贝时显式带过）；读档恢复后调 <see cref="EnsureSeqCounterAbove"/> 钳制计数器防进程重启后撞号。
        /// 用途：区分"同一编号 UI 显示多次"（UI bug）vs "不同编号内容重复"（重复写入）。
        /// 旧档条目无此字段 → 0（显示 #0）。
        /// </summary>
        public long SeqId { get; set; }

        private static long _seqCounter;

        public static void EnsureSeqCounterAbove(long id)
        {
            while (true)
            {
                long cur = Interlocked.Read(ref _seqCounter);
                if (id < cur) return;
                if (Interlocked.CompareExchange(ref _seqCounter, id + 1, cur) == cur) return;
            }
        }

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
            SeqId = Interlocked.Increment(ref _seqCounter);
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
