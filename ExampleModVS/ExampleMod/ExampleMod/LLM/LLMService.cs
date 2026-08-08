using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

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

        /// <summary>懒初始化单例：首次访问时检查 Settings（MCM 配置）的 API key 非空（门控），
        /// 无 key 抛异常 → 调用方 try-catch 静默降级（铁律 1）。
        /// 🔴 key 不在构造时固化：Authorization 头每次请求从 Settings.Instance.LLMApiKey 现读（CallApiAsync），
        /// 玩家在 MCM 修改 key 后下一个请求立即生效，无需重建实例（重建有竞态 + 非法 key 构造异常，2026-08-08 踩坑回滚）。</summary>
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
                            _instance = new LLMService();
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
        // 连接测试与失败诊断（交付：MCM「测试连接」按钮手动验证 LLM 服务可达 + key 有效；
        // 连不上时按原因给出明确反馈，铁律 6/设计哲学①）
        // ═══════════════════════════════════════════════════════════

        /// <summary>连接失败原因分类（玩家可理解的 5 种 + 兜底）。</summary>
        public enum LLMFailureReason
        {
            None,               // 无故障
            NotConfigured,      // ① 三个配置项有未填的
            BadBaseUrl,         // ② Base URL 不对（DNS/拒连/超时/端点不存在）
            ModelNotFound,      // ③ 模型不存在
            BadApiKey,          // ④ API 密钥不对（401/403 且响应体有明确 key 错误关键字）
            BadUrlOrKey,        // ④' 401/403 但响应体无 key 关键字——无法区分 URL 错还是 key 错（如网关对不存在的路径统一回 401）
            InsufficientFunds,  // ⑤ 账户没钱了（402/insufficient_quota/balance）
            Other               // 兜底：服务器错误、限流、未知异常
        }

        /// <summary>连接测试/失败诊断的返回结果：Success + 失败原因 + 详情（详情仅落日志，不展示给玩家）。</summary>
        public sealed class LLMConnectionResult
        {
            public bool Success;
            public LLMFailureReason Reason;
            public string Detail;
        }

        /// <summary>失败结果工厂。</summary>
        private static LLMConnectionResult Fail(LLMFailureReason reason, string detail)
        {
            return new LLMConnectionResult { Success = false, Reason = reason, Detail = detail };
        }

        /// <summary>发最小请求验证连接（BaseUrl 可达 + key 有效 + 模型存在），返回分类诊断结果。
        /// 用 1 token 的 chat/completions 而非 /models——OpenAI 兼容端点多支持前者，通用性最好。
        /// 🔴 同步实现（HttpWebRequest）：MCM 按钮回调在 UI 线程——async + GetResult 会死锁
        ///    （await continuation 回不了被阻塞的 UI 线程 → 10s 超时假失败，2026-08-08 实测）。
        ///    同步版无 async 死锁概念：UI 冻结最长 10s（超时），返回本次测试结果。
        /// 不负责展示——展示统一走 <see cref="ShowConnectionMessage"/>（MCM 按钮 showSuccess:true）。</summary>
        public static LLMConnectionResult TestConnection()
        {
            // 调试：打印 LLM 设置（key 掩码——前 4 位 + 长度，明文不落日志；
            // Base=玩家配置的原始 baseurl（空则打 (空)）；Url=实际请求值 ApiUrl（含缺省回落 + /chat/completions 后缀，
            // 两者对照能看出"以为填了其实没填"以及后缀拼接是否正确）
            try
            {
                var cfg = Settings.Instance;
                string keyMask = !string.IsNullOrWhiteSpace(cfg?.LLMApiKey)
                    ? cfg.LLMApiKey.Substring(0, Math.Min(4, cfg.LLMApiKey.Length)) + "…(" + cfg.LLMApiKey.Length + ")"
                    : "(空)";
                string baseForLog = string.IsNullOrWhiteSpace(cfg?.LLMBaseUrl) ? "(空)" : cfg.LLMBaseUrl;
                DebugLogger.Log($"[LLMTest] 设置检查: Ready={cfg?.IsLLMConfigured} Base={baseForLog} Url={ApiUrl} Model={CurrentModel} Key={keyMask}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[LLMTest] 设置打印失败: {ex.Message}");
            }
            // ① 未配置完整：明确告诉玩家缺哪个字段（三字段任一为空都算）
            var cfg2 = Settings.Instance;
            if (cfg2 == null || !cfg2.IsLLMConfigured)
            {
                DebugLogger.Log("[LLMTest] LLM 未配置完整，拒绝发起请求");
                return Fail(LLMFailureReason.NotConfigured, BuildMissingFieldsText());
            }
            try
            {
                var body = JsonConvert.SerializeObject(new
                {
                    model = CurrentModel,
                    messages = new object[] { new { role = "user", content = "ping" } },
                    max_tokens = 1,
                });
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["Authorization"] = "Bearer " + cfg2.LLMApiKey;
                // 🔴 必须显式设 ContentLength：HttpWebRequest 不设时默认 chunked 传输，
                // 雷火等 OpenAI 兼容网关的 nginx 前置直接 400（2026-08-08 实机复现验证）
                req.ContentLength = bodyBytes.Length;
                req.Timeout = 10000;          // 连接超时 10s（UI 冻结上限）
                req.ReadWriteTimeout = 10000; // 读写超时 10s
                using (var stream = req.GetRequestStream())
                {
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    bool ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
                    // HttpWebRequest 对 4xx/5xx 抛 WebException，正常返回即 2xx（3xx 自动跟随）
                    if (!ok)
                        return Fail(LLMFailureReason.Other, $"Unexpected status {(int)resp.StatusCode}");
                    return new LLMConnectionResult { Success = true, Reason = LLMFailureReason.None };
                }
            }
            catch (WebException ex)
            {
                // HTTP 错误状态（401/404/402…）也走这里（ProtocolError）
                HttpStatusCode? status = (ex.Response as HttpWebResponse)?.StatusCode;
                string respBody = null;
                if (ex.Response != null)
                {
                    try
                    {
                        using (var s = ex.Response.GetResponseStream())
                        using (var reader = new StreamReader(s))
                            respBody = reader.ReadToEnd();
                    }
                    catch { /* 读不到错误体不影响分类 */ }
                }
                DebugLogger.Log($"[LLMTest] 连接测试异常(HTTP {(status.HasValue ? (int)status.Value : 0)}): {ex.Message}\n{respBody}");
                var r = ClassifyFailure(ex, status, respBody);
                return r;
            }
            catch (Exception ex)
            {
                // 网络层/DNS/超时/证书等非 HTTP 异常（完整异常落日志——类型+消息+栈）
                DebugLogger.Log($"[LLMTest] 连接测试异常: {ex}");
                var r = ClassifyFailure(ex, null, null);
                return r;
            }
        }

        /// <summary>失败分类器：按「响应体关键字 → HTTP 状态码 → 网络层异常 → 兜底」判级。
        /// 5 种玩家可理解的原因见 <see cref="LLMFailureReason"/>；Detail 只落日志不展示。</summary>
        private static LLMConnectionResult ClassifyFailure(Exception ex, HttpStatusCode? status, string body)
        {
            string text = body ?? "";
            string lower = text.ToLowerInvariant();

            // ① 响应体关键字最具体，优先于状态码（同一状态码可能对应多种原因，如 404 = 端点 or 模型）
            if (lower.Contains("invalid_api_key") || lower.Contains("authentication_error")
                || lower.Contains("incorrect api key") || lower.Contains("invalid key"))
                return Fail(LLMFailureReason.BadApiKey, text);
            if (lower.Contains("insufficient_quota") || lower.Contains("insufficient balance")
                || lower.Contains("insufficient funds") || lower.Contains("balance"))
                return Fail(LLMFailureReason.InsufficientFunds, text);
            if (lower.Contains("model_not_found") || lower.Contains("unknown model")
                || lower.Contains("does not exist")
                || (lower.Contains("model") && (lower.Contains("not exist") || lower.Contains("not found"))))
                return Fail(LLMFailureReason.ModelNotFound, text);
            if (lower.Contains("not found") || lower.Contains("no such"))
                return Fail(LLMFailureReason.BadBaseUrl, text);

            // ② HTTP 状态码
            if (status.HasValue)
            {
                switch ((int)status.Value)
                {
                    case 401:
                    case 403:
                        // 走到这里说明 ① 的关键字检查未命中（无 invalid_api_key/authentication_error 等）——
                        // 服务端并未明确说是 key 错。很多网关对不存在的路径（如 baseurl 的 v11 写成 v1）
                        // 在认证/路由层统一回 401/403，此时归因给 key 是误导 → 归 BadUrlOrKey，提示同时指向 URL 与密钥
                        return Fail(LLMFailureReason.BadUrlOrKey, text);
                    case 402: return Fail(LLMFailureReason.InsufficientFunds, text);   // 余额不足（DeepSeek 402）
                    case 404: return Fail(LLMFailureReason.BadBaseUrl, text);          // 无模型关键字 → 端点不存在
                    default: return Fail(LLMFailureReason.Other, text);                // 400/429/5xx 等
                }
            }

            // ③ 网络层异常（DNS 失败/拒连/超时/TLS）→ Base URL 或网络问题
            if (ex is WebException wex && wex.Status != WebExceptionStatus.ProtocolError)
                return Fail(LLMFailureReason.BadBaseUrl, ex.Message);
            if (ex is TaskCanceledException || ex is HttpRequestException)
                return Fail(LLMFailureReason.BadBaseUrl, ex.Message);

            // ④ 未配置（key 空等配置类异常，Message 含「未配置」字样）
            if (ex != null && !string.IsNullOrEmpty(ex.Message) && ex.Message.Contains("未配置"))
                return Fail(LLMFailureReason.NotConfigured, ex.Message);

            // ⑤ 兜底
            return Fail(LLMFailureReason.Other, ex?.Message);
        }

        /// <summary>拼接缺失配置字段的本地化名称列表（"LLM API Base URL, LLM API Key"）。</summary>
        private static string BuildMissingFieldsText()
        {
            var cfg = Settings.Instance;
            var missing = new List<string>();
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.LLMBaseUrl))
                // 地址没填：取本地化字段名
                missing.Add(LWNTextHelper.ResolveText("LWN_mcm_llm_base_url", "LLM API Base URL"));
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.LLMApiKey))
                // 密钥没填：取本地化字段名
                missing.Add(LWNTextHelper.ResolveText("LWN_mcm_llm_api_key", "LLM API Key"));
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.LLMModel))
                // 模型没填：取本地化字段名
                missing.Add(LWNTextHelper.ResolveText("LWN_mcm_llm_model", "LLM Model"));
            return string.Join(", ", missing);
        }

        // 玩法路径失败提示防刷屏：同一失败原因 5 分钟内最多飘一次（后台自动触发场景——记忆总结/事件分析等，
        // 否则每次失败都弹一条红字）。测试按钮路径（showSuccess:true）不受限，每次测试都给结果。
        // 🔴 按原因区分冷却：原因变化（如先 URL/密钥被拒 401，后网络连不上）→ 立即提示，
        // 不被上一条冷却吞掉——不同问题值得各自提示一次；同问题反复重试才抑制。
        private static DateTime _lastFailureShownAt = DateTime.MinValue;
        private static LLMFailureReason _lastShownReason = LLMFailureReason.None;
        private const double FailureShowCooldownSeconds = 300;

        /// <summary>通用连接结果展示（测试连接与正式服务共用）。
        /// 成功：仅 showSuccess=true（MCM 测试按钮）时显示"连接正常"，正式玩法服务不打扰；
        /// 失败：按 <see cref="LLMFailureReason"/> 显示对应原因红字；玩法路径带 5 分钟防刷屏。</summary>
        public static void ShowConnectionMessage(LLMConnectionResult result, bool showSuccess)
        {
            if (result == null) return;
            if (result.Success)
            {
                if (showSuccess)
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 测试按钮路径：连接正常要明确反馈（正式玩法服务成功不打扰）
                        LWNTextHelper.ResolveText("LWN_mcm_llm_test_ok", "LLM connection OK."), Colors.Green));
                return;
            }
            // 玩法路径防刷屏：同原因 5 分钟冷却（MCM 按钮每次测试都显示，不走冷却）
            if (!showSuccess)
            {
                if (result.Reason == _lastShownReason && (DateTime.Now - _lastFailureShownAt).TotalSeconds < FailureShowCooldownSeconds)
                    return;
                _lastFailureShownAt = DateTime.Now;
                _lastShownReason = result.Reason;
            }

            string msg;
            switch (result.Reason)
            {
                case LLMFailureReason.NotConfigured:
                    // ① 有字段没填：点名缺哪个（MCM 测试按钮场景最多）
                    msg = LWNTextHelper.ResolveCompound("LWN_llm_fail_not_configured",
                        ("MISSING", result.Detail));
                    break;
                case LLMFailureReason.BadBaseUrl:
                    var baseForMsg = Settings.Instance?.LLMBaseUrl;
                    // ② Base URL 不对：显示玩家在 MCM 填的原始 baseurl（不带 /chat/completions 后缀，
                    //    跟玩家输入一字不差；完整请求 URL 留 Debug 日志，配置为空（理论不会）时退回 ApiUrl）
                    msg = LWNTextHelper.ResolveCompound("LWN_llm_fail_bad_base_url",
                        ("URL", string.IsNullOrWhiteSpace(baseForMsg) ? ApiUrl : baseForMsg.TrimEnd('/')));
                    break;
                case LLMFailureReason.ModelNotFound:
                    // ③ 模型不存在：带上配置的模型名
                    msg = LWNTextHelper.ResolveCompound("LWN_llm_fail_model_not_found",
                        ("MODEL", CurrentModel));
                    break;
                case LLMFailureReason.BadApiKey:
                    // ④ 密钥被拒：提示检查 API 密钥
                    msg = LWNTextHelper.ResolveText("LWN_llm_fail_bad_api_key",
                        "The API key was rejected by the service. Check that the API key is correct.");
                    break;
                case LLMFailureReason.BadUrlOrKey:
                    // ④' 401/403 无明确 key 错误信息：URL 或密钥都可能错（网关对错误路径也回 401）。
                    // 把玩家填的原始 baseurl 亮出来——v1 写成 v11 这种一眼就能看出来
                    var baseForMsg2 = Settings.Instance?.LLMBaseUrl;
                    msg = LWNTextHelper.ResolveCompound("LWN_llm_fail_bad_url_or_key",
                        ("URL", string.IsNullOrWhiteSpace(baseForMsg2) ? ApiUrl : baseForMsg2.TrimEnd('/')));
                    break;
                case LLMFailureReason.InsufficientFunds:
                    // ⑤ 账户没钱：提示充值
                    msg = LWNTextHelper.ResolveText("LWN_llm_fail_insufficient_funds",
                        "The LLM account has insufficient balance. Recharge the account and try again.");
                    break;
                default:
                    // 兜底：服务器错误/限流/未知
                    msg = LWNTextHelper.ResolveText("LWN_llm_fail_other",
                        "The LLM service is temporarily unavailable. Please try again later.");
                    break;
            }
            InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Red));
        }


        public LLMService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            // 🔴 不在构造时固化 Authorization 头：key 每次请求现读（CallApiAsync），
            // MCM 修改即时生效；且非法 key 在构造时 Add header 可能抛（2026-08-08 实机踩坑）。
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

        /// <summary>轻量单次请求（ReactiveAgent 实时回应专用）：无重试、短超时、失败静默返回 null。
        /// 区别于 ChatAsync（3 次重试 + 失败弹连接提示）——实时对话必须在 2s 预算内返回，
        /// 超时/失败由调用方降级（职业模板台词），不打扰玩家（BC-006）。
        /// 429 限流 → 触发全局冷却（RespondRateLimitCooldownS 内所有回应请求直接降级，防连发撞限流）。</summary>
        public async Task<string> ChatOnceAsync(string systemPrompt, int maxTokens = 80, float temperature = 0.7f, bool disableReasoning = true, int timeoutMs = 2000)
        {
            if (!Settings.Instance.IsLLMConfigured) return null;
            if (IsRespondRateLimited()) return null;
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = CurrentModel,
                ["messages"] = messages,
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
            };
            if (disableReasoning) requestBody["reasoning_effort"] = "none";
            try
            {
                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var key = Settings.Instance?.LLMApiKey;
                if (string.IsNullOrWhiteSpace(key)) return null;
                using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                {
                    request.Content = content;
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    // 请求日志（摘要，防刷屏）：确认实时请求确实发出（与 CallApiAsync 全量日志分工）
                    DebugLogger.Log($"[ReactiveRespond] 请求发出: {(json.Length > 300 ? json.Substring(0, 300) + "…" : json)}");
                    using (var cts = new CancellationTokenSource(timeoutMs))
                    {
                        var response = await _httpClient.SendAsync(request, cts.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            DebugLogger.Log($"[ReactiveRespond] 回应请求失败: {response.StatusCode}（降级模板）");
                            // 429 限流：进入冷却，避免连发撞限流（2026-08-08 实测网关 429；老 .NET 枚举无 TooManyRequests，用数字）
                            if ((int)response.StatusCode == 429)
                                _respondRateLimitBlockedUntil = Mission.Current != null ? Mission.Current.CurrentTime + RespondRateLimitCooldownS : float.MaxValue;
                            return null;
                        }
                        string responseString = await response.Content.ReadAsStringAsync();
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string finalContent = result.choices[0].message.content.ToString().Trim();
                        // 回包日志：确认实时回应内容（降级排查关键证据）
                        DebugLogger.Log($"[ReactiveRespond] 回包: {finalContent}");
                        if (string.IsNullOrWhiteSpace(finalContent)) return null;
                        return finalContent;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ReactiveRespond] 回应请求失败（降级模板）: {ex.Message}");
                return null;
            }
        }

        // ── 429 限流冷却（回应专用；冷却期内直接降级模板，不发请求）──
        private static float _respondRateLimitBlockedUntil;
        private const float RespondRateLimitCooldownS = 10f;

        private static bool IsRespondRateLimited()
        {
            return Mission.Current != null && Mission.Current.CurrentTime < _respondRateLimitBlockedUntil;
        }

        // 总结功能 (将对话压缩为30字记忆)
        /// <param name="showFailureAlert">失败弹玩家红字（玩家对话默认 true；随从对话触发的记忆维护传 false 静默，D4）</param>
        public async Task<string> SummarizeAsync(string systemPrompt, bool showFailureAlert = true)
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

            return await CallApiAsync(requestBody, showFailureAlert);
        }

        public async Task<string> MergeMemoryAsync(string systemPrompt, bool showFailureAlert = true)
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

            return await CallApiAsync(requestBody, showFailureAlert);
        }

        /// <summary>安全读错误响应体（截断防超大错误页），失败返回 null——只影响诊断精度，不影响主流程。</summary>
        private static async Task<string> TryReadErrorBodyAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body != null && body.Length > 2000)
                    body = body.Substring(0, 2000);
                return body;
            }
            catch { return null; }
        }

        private async Task<string> CallApiAsync(object requestBody, bool showFailureAlert = true)
        {
            //可能是对话服务，也可能是总结短期、长期记忆服务
            int maxRetries = 3;
            int currentRetry = 0;
            string errorString = "";
            // 最近一次非 2xx 的状态码/响应体——最终失败时交给 ClassifyFailure 分类提示玩家（5 种原因）
            HttpStatusCode? lastStatus = null;
            string lastErrorBody = null;

            while (currentRetry < maxRetries)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(requestBody);

                    if (currentRetry == 0)
                    {
                        // 设置检查：key 掩码（前 4 位 + 长度，明文不落日志）；
                        // Base=玩家配置的原始 baseurl（空则打 (空)）；Url=实际请求值 ApiUrl（含缺省回落 + /chat/completions 后缀），
                        // 两者对照能看出"以为填了其实没填"以及后缀拼接是否正确——与 TestConnection 同格式
                        try
                        {
                            var cfg = Settings.Instance;
                            string keyMask = !string.IsNullOrWhiteSpace(cfg?.LLMApiKey)
                                ? cfg.LLMApiKey.Substring(0, Math.Min(4, cfg.LLMApiKey.Length)) + "…(" + cfg.LLMApiKey.Length + ")"
                                : "(空)";
                            string baseForLog = string.IsNullOrWhiteSpace(cfg?.LLMBaseUrl) ? "(空)" : cfg.LLMBaseUrl;
                            DebugLogger.Log($"[LLMRequest] 设置检查: Ready={cfg?.IsLLMConfigured} Base={baseForLog} Url={ApiUrl} Model={CurrentModel} Key={keyMask}");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[LLMRequest] 设置打印失败: {ex.Message}");
                        }
                        DebugLogger.Log($"大模型请求结构 (尝试 {currentRetry + 1})\n{json}");
                    }
                    else
                    {
                        DebugLogger.Log($"大模型请求重试中... ({currentRetry + 1}/{maxRetries})");
                    }

                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    // 🔴 key 每次请求现读（MCM 修改即时生效）：Authorization 头随请求动态构造，
                    // 不依赖构造时固化的 DefaultRequestHeaders（旧 key 永不更新——正是玩家改 key 后 401 的根源）。
                    // 空 key 在此 throw → 走下方 catch 重试 → 最终降级，与 getter 门控双保险。
                    var key = Settings.Instance?.LLMApiKey;
                    if (string.IsNullOrWhiteSpace(key))
                        throw new Exception("LLMApiKey 未配置");
                    using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                    {
                        request.Content = content;
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                        var response = await _httpClient.SendAsync(request);

                        // 检查 HTTP 状态码
                        if (!response.IsSuccessStatusCode)
                        {
                            lastStatus = response.StatusCode;
                            lastErrorBody = await TryReadErrorBodyAsync(response);

                            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                            {
                                throw new HttpRequestException($"Server Error: {response.StatusCode}");
                            }

                            errorString = $"[NPC正在思考...] (Error: {response.StatusCode})";
                            DebugLogger.Log($"大模型API报错: {errorString} (body: {lastErrorBody})");
                            // 不可重试的 4xx 终局：分类并提示玩家（连不上必须给出明确原因）；
                            // showFailureAlert=false（随从对话记忆维护）→ 静默（D4）
                            if (showFailureAlert)
                                ShowConnectionMessage(ClassifyFailure(null, lastStatus, lastErrorBody), showSuccess: false);
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
                }
                catch (Exception ex)
                {
                    currentRetry++;
                    DebugLogger.Log($"请求发生异常: {ex.Message}");

                    if (currentRetry >= maxRetries)
                    {
                        errorString = $"[NPC似乎走神了...] ({ex.Message})";
                        DebugLogger.Log($"大模型请求最终失败: {errorString}");
                        // 重试耗尽终局：分类并提示玩家（连不上必须给出明确原因）
                        if (showFailureAlert)
                            ShowConnectionMessage(ClassifyFailure(ex, lastStatus, lastErrorBody), showSuccess: false);
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
