using Newtonsoft.Json;

namespace LivingWorldNpcs
{
    public class PlayerGeneratedOption
    {
        [JsonProperty("tactic")]
        public string Tactic { get; set; } // 策略标签：[威慑] [欺骗] [哀求]

        //关联属性
        [JsonProperty("attribute")]
        public string Attribute { get; set; } // 关联属性：[魅力] [智慧] [勇气]

        [JsonProperty("text")]
        public string Text { get; set; } // 具体的台词内容

        [JsonProperty("outcome_prediction")]
        public string OutComePrediction { get; set; } // 预判后果描述：(成功率极低)

        [JsonProperty("player_emotion")]
        public string PlayerEmotion { get; set; } // 选项对应的情绪

    }
}
