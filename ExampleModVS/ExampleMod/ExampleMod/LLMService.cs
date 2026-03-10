using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ExampleMod
{
    public class LLMResponse
    {
        public string NpcReply { get; set; }           // NPC 的台词
        public List<string> PlayerNextOptions { get; set; }   // 建议玩家回复的选项 (3-4个)
        public string NpcAction { get; set; }          // 触发的动作代码，如 "MARRY_SUCCESS", "ATTACK", "NONE"
        public string ActionParam { get; set; }     // 动作参数（可选）

        public LLMResponse(string reply) 
        {
            NpcReply = reply;
            PlayerNextOptions = new List<string>();
            NpcAction = "NONE";
            ActionParam = "";
        }
    }
    public class LLSSummaryResponse
    {
        public string Summary { get; set; }         // 对话总结内容
    }

    public class LLMResponse_SceneConflict
    {
        // NPC 的演出
        [JsonProperty("npc_reply")]
        public string NpcReply { get; set; }

        [JsonProperty("npc_emotion")]
        public string NpcEmotion { get; set; } // 用于前端切换表情

        //Npc执行的Action
        [JsonProperty("npc_action")]
        public string NpcAction { get; set; }

        // 数值裁判结果 (本轮对话对进度的影响)
        [JsonProperty("conflict_progress_delta")]
        public float ProgressDelta { get; set; } // 例: +20

        [JsonProperty("npc_patience_delta")]
        public float PatienceDelta { get; set; } // 例: -10

        // 下一轮玩家可选的对话列表
        [JsonProperty("player_next_options")]
        public List<PlayerGeneratedOption> PlayerNextOptions { get; set; }

        public LLMResponse_SceneConflict(string reply)
        {
            NpcReply = reply;
            NpcEmotion = "normal";
            NpcAction = "NONE";
            ProgressDelta = 0;
            PatienceDelta = 0;
            PlayerNextOptions = new List<PlayerGeneratedOption>();
        }
    }



    public class LLMService
    {
        private string _apiKey;
        private readonly HttpClient _httpClient;

        //Instance
        // DeepSeek API 地址 (如果是豆包，需替换为豆包的Endpoint)
        private const string ApiUrl = "https://api.deepseek.com/v1/chat/completions";

        private static LLMService _instance;
        public static LLMService Instance
        {
            get
            {
                if (_instance == null) throw new Exception("LLMService not initialized!");
                return _instance;
            }
        }

        public static string CleanJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "{}";
            // 简单清洗 markdown 符号
            string clean = raw.Replace("```json", "").Replace("```", "").Trim();
            int firstBrace = clean.IndexOf('{');
            int lastBrace = clean.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                clean = clean.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            return clean;


        }


        public static void Initialize(string apiKey)
        {
            _instance = new LLMService(apiKey);
        }
        public LLMService(string apiKey)
        {

            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            // 注意：Header 只需添加一次
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        }

        // 通用的聊天请求
        public async Task<string> ChatAsync(string systemPrompt,int max_tokens = 150,bool needJson=true)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            //考虑是否需要json格式
            if (needJson)
            {
                var requestBody = new
                {
                    model = "deepseek-chat",
                    messages = messages,
                    temperature = 0.7,
                    max_tokens = max_tokens,
                    response_format = new { type = "json_object" }
                };
                return await CallApiAsync(requestBody);
            }
            else
            {
                var requestBody = new
                {
                    model = "deepseek-chat",
                    messages = messages,
                    temperature = 0.7,
                    max_tokens = max_tokens
                };
                return await CallApiAsync(requestBody);
            }
        }

        // 总结功能 (将对话压缩为30字记忆)
        public async Task<string> SummarizeAsync(string systemPrompt)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt},
            };

            var requestBody = new
            {
                model = "deepseek-chat",
                messages = messages,
                temperature = 0.7,
                max_tokens = 50,
                response_format = new { type = "json_object" }
            };

            return await CallApiAsync(requestBody);
        }

        public async Task<string> MergeMemoryAsync(string systemPrompt)
        {
            var messages = new List<object>
            {
                    new { role = "system", content = systemPrompt },
                  
            };

            var requestBody = new
            {
                model = "deepseek-chat",
                messages = messages,
                temperature = 0.7,
                max_tokens = 300,
                response_format = new { type = "json_object" }
            };
            
            return await CallApiAsync(requestBody);
        }

        private async Task<string> CallApiAsync(object requestBody)
        {
            //可能是对话服务，也可能是总结短期、长期记忆服务
            int maxRetries = 3;
            int currentRetry = 0;
            string errorString = "";

            while (currentRetry < maxRetries)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(requestBody);

                    if (currentRetry == 0)
                    {
                        DebugLogger.Log($"大模型请求结构 (尝试 {currentRetry + 1})\n{json}");
                    }
                    else
                    {
                        DebugLogger.Log($"大模型请求重试中... ({currentRetry + 1}/{maxRetries})");
                    }

                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(ApiUrl, content);

                    // 检查 HTTP 状态码
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                        {
                            throw new HttpRequestException($"Server Error: {response.StatusCode}");
                        }

                        errorString = $"[NPC正在思考...] (Error: {response.StatusCode})";
                        DebugLogger.Log($"大模型API报错: {errorString}");
                        return errorString;
                    }

                    string responseString = await response.Content.ReadAsStringAsync();
                    DebugLogger.Log($"大模型生成结果\n{responseString}");

                    dynamic result = JsonConvert.DeserializeObject(responseString);
                    string finalContent = result.choices[0].message.content.ToString().Trim();

                    // 【核心修复】检查 Content 是否为空白
                    if (string.IsNullOrWhiteSpace(finalContent))
                    {
                        // 如果是大模型抽风回了空格，我们抛出异常，强迫它进入 catch 块重试
                        DebugLogger.Log($"Model returned empty whitespace.");
                        throw new Exception("Model returned empty whitespace.");
                    }

                    return finalContent; // 成功拿到结果，直接返回
                }
                catch (Exception ex)
                {
                    currentRetry++;
                    DebugLogger.Log($"请求发生异常: {ex.Message}");

                    if (currentRetry >= maxRetries)
                    {
                        errorString = $"[NPC似乎走神了...] ({ex.Message})";
                        DebugLogger.Log($"大模型请求最终失败: {errorString}");
                        throw new Exception($"连接失败，请检查网络或 Key ({ex.Message})");
                    }

                    // 失败后等待 1 秒再重试 (指数退避会更好，但这里简单处理)
                    await Task.Delay(1000);
                }
            }

            throw new Exception("Unknown Error"); // 理论上不会走到这
        }
    }
}
