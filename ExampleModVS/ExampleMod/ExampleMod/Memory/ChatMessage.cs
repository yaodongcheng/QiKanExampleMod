using Newtonsoft.Json;
using System;

namespace LivingWorldNpcs
{
    public class ChatMessage
    {
        public string Role { get; set; } // "user" or "assistant"（自由字符串；NPC-NPC 对话 = 对方/自己）
        public string Content { get; set; }  // 惯例拼"说话人名字: 台词"（prompt 全量输出，LLM 自行理解说话人）

        /// <summary>说话人标识（§八 任意人对话泛化）：Hero StringId / TEMP_AGENT 键 / "player"。
        /// 可选——玩家对话可不传（Content 已有名字，向后兼容）；respond 按它过滤"与当前对方相关"的历史。</summary>
        public string SpeakerId { get; set; }

        //时间戳
        public double TimeStamp { get; set; }

        public ChatMessage(string role, string content, string speakerId = null)
        {
            Role = role;
            Content = content;
            SpeakerId = speakerId;
            TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
