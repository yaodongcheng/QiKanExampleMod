using Newtonsoft.Json;
using System;

namespace LivingWorldNpcs
{
    public class ChatMessage
    {
        public string Role { get; set; } // "user" or "assistant"
        public string Content { get; set; }

        //时间戳
        public double TimeStamp { get; set; }

        public ChatMessage(string role,string content)
        {
            Role = role;
            Content = content;
            TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
