using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;

namespace ExampleMod.StoryEngineBag
{
    public class AIScriptRoot
    {
        [JsonProperty("script")]
        public List<AiScriptItem> Script { get; set; }
    }

    public class AiScriptItem
    {
        [JsonProperty("cmd")]
        public string Cmd { get; set; } // DIALOG, CHOICE, APPEAR, EXIT

        // --- DIALOG 字段 ---
        [JsonProperty("speaker_name")]
        public string SpeakerName { get; set; }

        [JsonProperty("listener_name")]
        public string ListenerName { get; set; }

        [JsonProperty("speaker_text")]
        public string SpeakerText { get; set; }

        [JsonProperty("speaker_emotion")]
        public string SpeakerEmotion { get; set; }

        // --- APPEAR/EXIT 字段 ---
        [JsonProperty("params")]
        public string Params { get; set; }

        // --- CHOICE 字段 ---
        [JsonProperty("options")]
        public List<PlayerGeneratedOption> Options { get; set; }
    }
    public class AIStoryAdapt
    {
        public static void ParseJson_AIStory(string jsonResponse, out AIScriptRoot response)
        {
            // 解析阶段
            jsonResponse = LLMService.CleanJson(jsonResponse);
            try
            {
                response = JsonConvert.DeserializeObject<AIScriptRoot>(jsonResponse);
                if (response == null) throw new Exception("Empty JSON");
            }
            catch (Exception) // 捕获 JsonReaderException 或其他解析错误
            {
                response = null;
            }
        }
        public static List<ScriptNode> ConvertToEngineScript(List<AiScriptItem> aiItems)
        {
            var engineScript = new List<ScriptNode>();
            string playerEngineName = Hero.MainHero.Name.ToString();

            foreach (AiScriptItem item in aiItems)
            {
                // 1. 预处理：如果是对话，先进行拆分；如果是其他指令，当作单元素列表处理
                List<string> textSegments = new List<string>();

                if (item.Cmd == "DIALOG" && !string.IsNullOrEmpty(item.SpeakerText))
                {
                    // 使用正则拆分句子，同时保留标点符号
                    // 匹配 。！？ 或 .!? 后面进行拆分
                    textSegments = SplitSentences(item.SpeakerText);
                }
                else
                {
                    // 非对话或无文本，占位即可
                    textSegments.Add(item.SpeakerText ?? "");
                }

                // 2. 遍历拆分后的文本片段（非对话指令只会循环一次）
                foreach (var segment in textSegments)
                {
                    // 跳过空行（防止正则拆分出空字符串）
                    if (item.Cmd == "DIALOG" && string.IsNullOrWhiteSpace(segment)) continue;

                    var node = new ScriptNode();
                    node.Params = new List<string>();

                    switch (item.Cmd)
                    {
                        case "DIALOG":
                            // --- 核心修改：这里使用拆分后的 segment ---
                            node.Text = segment;

                            string speakerEngineName = item.SpeakerName;
                            string listenerEngineName = item.ListenerName;

                            if (item.SpeakerName == "旁白")
                            {
                                node.Cmd = "旁白";
                            }
                            else
                            {
                                if (item.SpeakerName == item.ListenerName && item.SpeakerName == playerEngineName)
                                {
                                    node.Cmd = "自語";
                                }
                                else
                                {
                                    node.Cmd = "對話";
                                }

                                // 依然使用原本的说话人/听话人配置
                                node.Params.Add($"{speakerEngineName},{listenerEngineName}");
                                // 每一句都继承同样的情绪（或者你也可以写逻辑只让第一句有情绪）
                                node.Emotion = item.SpeakerEmotion;
                            }
                            break;

                        case "APPEAR":
                            node.Cmd = "人物登场";
                            node.Params.Add(item.Params);
                            break;

                        case "EXIT":
                            node.Cmd = "人物退场";
                            node.Params.Add(item.Params);
                            break;

                        case "CHOICE":
                            node.Cmd = "選擇";
                            node.Options = new List<ScriptOption>();
                            foreach (var aiOpt in item.Options)
                            {
                                node.Options.Add(new ScriptOption
                                {
                                    Text = $"[{aiOpt.Tactic}] {aiOpt.Text}"
                                });
                            }
                            break;
                    }

                    engineScript.Add(node);
                }
            }

            return engineScript;
        }

        private static List<string> SplitSentences(string input)
        {
            // 正则解释：
            // (?<=[。！？.!?])  -> 正向后顾：查找前面是标点符号的位置进行分割
            // 这样可以保留标点符号在句尾，而不是被吃掉
            string pattern = @"(?<=[。！？.!?])";

            var parts = Regex.Split(input, pattern);

            // 过滤掉纯空白的分割结果，并去除首尾空格
            return parts.Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim())
                        .ToList();
        }
    }

}
