using System;

namespace LivingWorldNpcs
{
    public class RecentMemory
    {
        public string Content { get; set; }
        public double TimeStamp_Start { get; set; }
        public double TimeStamp_End { get; set; }

        public RecentMemory(string content, double timeStamp_Start, double timeStamp_End)
        {
            Content = content;
            TimeStamp_Start = timeStamp_Start;
            TimeStamp_End = timeStamp_End;
        }
    }
}
