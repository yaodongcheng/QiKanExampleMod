using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LivingWorldNpcs
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

        // Instance
        // OpenAI 兼容端点 = {LLMBaseUrl}/chat/completions（MCM 配置；缺省回落 DeepSeek 官方）
        private static string ApiUrl
        {
            get
            {
                var baseUrl = Settings.Instance?.LLMBaseUrl;
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    return baseUrl.TrimEnd('/') + "/chat/completions";
                return "https://api.deepseek.com/v1/chat/completions";
            }
        }

        /// <summary>当前模型（MCM 配置 LLMModel；缺省回落 deepseek-chat）。</summary>
        private static string CurrentModel => Settings.Instance?.LLMModel ?? "deepseek-chat";

        private static LLMService _instance;
        private static readonly object _instanceLock = new object();

        /// <summary>懒初始化单例：从 Settings（MCM 配置）读 API key。
        /// 历史遗留：Initialize(apiKey) 全库无调用点，旧代码直接抛 "not initialized!" 被调用方 try-catch 吞掉静默降级
        /// ——LLM 功能实际从未工作。现在首次访问时用 Settings.Instance.LLMApiKey 自动初始化。</summary>
        public static LLMService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            var key = Settings.Instance?.LLMApiKey;
                            if (string.IsNullOrWhiteSpace(key))
                                throw new Exception("LLMService not initialized: LLMApiKey 未配置（请在 Mod 选项中填写）");
                            _instance = new LLMService(key);
                        }
                    }
                }
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

        // ═══════════════════════════════════════════════════════════
        // 连接测试（交付：Plot 玩法行显示/触发前验证 LLM 服务可达 + key 有效）
        // ═══════════════════════════════════════════════════════════

        public enum LLMConnectionState { Unknown, Ok, Failed }

        private static LLMConnectionState _connState = LLMConnectionState.Unknown;
        private static DateTime _connTestedAt = DateTime.MinValue;
        private const double ConnCacheSeconds = 300;   // 连接状态缓存 5 分钟（避免每帧/每次检查都发请求）

        /// <summary>连接状态查询（玩法行显示/触发门控用）：Unknown 放行（首次未测不误杀），
        /// Failed 且在缓存期内拒绝；Ok/过期自动重测由 <see cref="TestConnection"/> 或按钮刷新。</summary>
        public static bool IsConnectionOk()
        {
            if (_connState == LLMConnectionState.Failed
                && (DateTime.Now - _connTestedAt).TotalSeconds < ConnCacheSeconds)
                return false;
            return true;
        }

        /// <summary>配置变更后失效缓存（MCM setter 调用）——下次查询/测试重新验证。
        /// ⚠️ 2026-08-08 曾尝试顺带重建 LLMService 实例（_instance = null 换新 key），
        /// 实机出现新异常（非法 key 时 new LLMService 的 header Add 可能抛）→ 已回滚，只清连接状态缓存。</summary>
        public static void InvalidateConnectionCache()
        {
            _connState = LLMConnectionState.Unknown;
            _connTestedAt = DateTime.MinValue;
        }

        /// <summary>发最小请求验证连接（BaseUrl 可达 + key 有效 + 模型存在）。
        /// 用 1 token 的 chat/completions 而非 /models——OpenAI 兼容端点多支持前者，通用性最好。
        /// 🔴 同步实现（HttpWebRequest）：MCM 按钮回调在 UI 线程——async + GetResult 会死锁
        ///    （await continuation 回不了被阻塞的 UI 线程 → 10s 超时假失败，2026-08-08 实测）。
        ///    同步版无 async 死锁概念：UI 冻结最长 10s（超时），结果进缓存，返回本次测试结果。</summary>
        public static bool TestConnection()
        {
            // 调试：打印 LLM 设置（key 掩码——前 4 位 + 长度，明文不落日志；
            // Url/Model 打实际请求值 ApiUrl/CurrentModel——含缺省回落，能看出"以为填了其实没填"）
            try
            {
                var cfg = Settings.Instance;
                string keyMask = !string.IsNullOrWhiteSpace(cfg?.LLMApiKey)
                    ? cfg.LLMApiKey.Substring(0, Math.Min(4, cfg.LLMApiKey.Length)) + "…(" + cfg.LLMApiKey.Length + ")"
                    : "(空)";
                DebugLogger.Log($"[LLMTest] 设置检查: Ready={cfg?.IsLLMReady} Url={ApiUrl} Model={CurrentModel} Key={keyMask}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[LLMTest] 设置打印失败: {ex.Message}");
            }
            try
            {
                var cfg = Settings.Instance;
                if (!cfg.IsLLMReady)
                {
                    _connState = LLMConnectionState.Failed;
                    _connTestedAt = DateTime.Now;
                    return false;
                }
                var body = JsonConvert.SerializeObject(new
                {
                    model = CurrentModel,
                    messages = new object[] { new { role = "user", content = "ping" } },
                    max_tokens = 1,
                });
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["Authorization"] = "Bearer " + cfg.LLMApiKey;
                req.Timeout = 10000;          // 连接超时 10s（UI 冻结上限）
                req.ReadWriteTimeout = 10000; // 读写超时 10s
                using (var stream = req.GetRequestStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(body);
                }
                bool ok;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
                }
                _connState = ok ? LLMConnectionState.Ok : LLMConnectionState.Failed;
                _connTestedAt = DateTime.Now;
                return ok;
            }
            catch (Exception ex)
            {
                // 完整异常落日志（类型+消息+栈）——HTTP 错误状态（401/404）也走这里（WebException）
                DebugLogger.Log($"[LLMTest] 连接测试异常: {ex}");
                _connState = LLMConnectionState.Failed;
                _connTestedAt = DateTime.Now;
                return false;
            }
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
        /// <param name="disableReasoning">关闭思考模式（reasoning_effort=none）：deepseek-v4-flash 默认可能进思考模式，
        /// reasoning_content 占 output 配额 60% 且慢 6-8 倍——计划生成调用必须关（实测 25s→3.5s、推理 token 归零）。</param>
        public async Task<string> ChatAsync(string systemPrompt, int max_tokens = 150, bool needJson = true, float temperature = 0.7f, bool disableReasoning = false)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            //考虑是否需要json格式
            if (needJson)
            {
                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = CurrentModel,
                    ["messages"] = messages,
                    ["temperature"] = temperature,
                    ["max_tokens"] = max_tokens,
                    ["response_format"] = new { type = "json_object" },
                };
                if (disableReasoning) requestBody["reasoning_effort"] = "none";
                return await CallApiAsync(requestBody);
            }
            else
            {
                var requestBody = new
                {
                    model = CurrentModel,
                    messages = messages,
                    temperature = temperature,
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
                model = CurrentModel,
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
                model = CurrentModel,
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
