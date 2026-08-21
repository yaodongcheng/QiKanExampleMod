using Newtonsoft.Json;
using System;
using System.Threading;

namespace LivingWorldNpcs
{
    public class ChatMessage
    {
        /// <summary>
        /// 会话内唯一调试编号（🔴 2026-08-21）：构造时自增分配；存档随 JSON 保留；
        /// 读档恢复后调 <see cref="EnsureSeqCounterAbove"/> 钳制计数器防进程重启后与新条目撞号。
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

        public string Role { get; set; } // "user" or "assistant"（自由字符串；NPC-NPC 对话 = 对方/自己）
        public string Content { get; set; }  // 惯例拼"说话人名字: 台词"（prompt 全量输出，LLM 自行理解说话人）

        /// <summary>说话人标识（§八 任意人对话泛化）：Hero StringId / TEMP_AGENT 键 / "player"。
        /// 可选——玩家对话可不传（Content 已有名字，向后兼容）；respond 按它过滤"与当前对方相关"的历史。</summary>
        public string SpeakerId { get; set; }

        //时间戳
        public double TimeStamp { get; set; }

        /// <summary>游戏内日（🔴 2026-08-16 方案 I5）：写入时同步记录 CampaignTime.Now.ToDays——
        /// 墙钟毫秒（TimeStamp）对游戏对话无意义（游戏内 1 天 ≈ 墙钟几分钟），不可转换；
        /// 输出时转相对词（[3天前]）。0 = 旧存档条目（无游戏内日）→ 不标时间戳（契约兜底，宁模糊不编数）。</summary>
        public float CampaignDay { get; set; }

        public ChatMessage(string role, string content, string speakerId = null)
        {
            Role = role;
            Content = content;
            SpeakerId = speakerId;
            TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SeqId = Interlocked.Increment(ref _seqCounter);
            try { CampaignDay = TaleWorlds.CampaignSystem.Campaign.Current != null ? (float)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays : 0f; } catch { CampaignDay = 0f; }
        }
    }
}
