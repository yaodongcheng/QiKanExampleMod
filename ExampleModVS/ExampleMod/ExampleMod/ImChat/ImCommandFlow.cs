using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 命令模式（需求 7：接入密令系统，用户决策 2：模式切换 + 批准卡片）：
    /// 命令文本 → 复用密令 LLM 管线（SceneSnapshot + BuildPlanPrompt + ChatAsync → PlanResponse）
    /// → IM 消息流插入计划卡片（同意/拒绝按钮）→ 批准 → SendEventToAgent("order_execute_plan") 确定性执行
    /// → PlanExecutor 事件回报回 IM（系统消息，双渠道：原密信 DisplayMessage 保留）。
    ///
    /// 与 PlanCommandFlow 的关系：复用其 LLM 管线与执行入口（public/internal 静态 API），
    /// 但 IM 是独立状态机（不抢 PlanCommandFlow 的 _isActive——互斥用 IsActive 检查）。
    /// 密令是 Mission 级瞬态：命令消息不写 NPC 记忆（避免污染对话漏斗），但群聊 store 里的
    /// 命令文本/计划卡片/系统回报会随 lwn_im_group_* 存档（执行状态 ExecutorId 读档后保留——
    /// 执行器已不在场，卡片显示为「执行中」且无法中止，属已知取舍）。
    /// </summary>
    public static class ImCommandFlow
    {
        /// <summary>卡片 ExecutorId 语义常量：已拒绝。</summary>
        public const string Rejected = "rejected";
        /// <summary>卡片 ExecutorId 语义常量：已了结（完成/中止）。</summary>
        public const string Done = "done";
        /// <summary>卡片 ExecutorId 语义常量：🔴 2026-08-10（Q2）已修改（玩家点了修改，旧卡作废，等待新版计划）。</summary>
        public const string Superseded = "superseded";
        /// <summary>修改额度上限（Q2：复用 PlanReplan 的 ≤2 次语义，防玩家无限修改刷 LLM）。</summary>
        public const int MaxModifyCount = 2;

        // 🔴 2026-08-20（用户裁定：玩家说"换个人再偷"，随从仍偷原目标）：换目标意图词表（检测词典，
        // 豁免本地化，同 ChatClaimChecker 词表模式）——命令命中 → 剥离已锁定目标（含 #N 的括号尾巴）
        // 重采候选注入计划轮，不靠 LLM 自觉换人（实机：原命令并入带 #54，LLM 只能沿用）。
        private static readonly string[] RetargetIntentWords =
        {
            "换个人", "换个", "换一个", "换人", "换目标", "换别人", "换别的", "换其他人", "换个人选", // lwn-ignore: A
            "换别的目标", "换下一个人", "重新挑", "再挑一个", "另找一个", "另挑", "挑别的", // lwn-ignore: A
            "switch to", "another", "different target", "change target", "someone else", "other target",
        };
        private static readonly string[] RetargetGuardWords = { "不", "别", "没", "等", "算", "免" }; // lwn-ignore: A

        /// <summary>换目标意图判定（匹配处前 4 字符否定守卫防"别换人了"误伤）。</summary>
        private static bool HasRetargetIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var w in RetargetIntentWords)
            {
                int idx = text.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                int from = Math.Max(0, idx - 4);
                string ctx = text.Substring(from, idx - from);
                bool guarded = false;
                foreach (var g in RetargetGuardWords)
                {
                    if (ctx.IndexOf(g, StringComparison.Ordinal) >= 0) { guarded = true; break; }
                }
                if (!guarded) return true;
            }
            return false;
        }

        /// <summary>剥离已锁定目标尾巴（模板 NPC 的「名字#N（方位）」括号片段，含内层方位括号），
        /// 恢复目标类型词供重采候选。例：那换个人再偷（原命令：去偷士兵的东西（帝国弩手#54（你东侧17米）））
        /// → 那换个人再偷（原命令：去偷士兵的东西）。#N 不在括号内 → 原样返回（无剥离语义）。
        /// 根因（2026-08-20）：合并命令残留 #N 触发 FindAgentCandidates 的 TryResolveIndexedTarget
        /// 唯一化短路（SceneSnapshot.cs:314）→ 候选注入与后置澄清检查全被绕过。</summary>
        private static string StripResolvedTargetSuffix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text;
            while (true)
            {
                int hash = -1;
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '#' && i + 1 < s.Length && char.IsDigit(s[i + 1])) { hash = i; break; }
                }
                if (hash < 0) return s;
                int open = -1;
                for (int i = hash - 1; i >= 0; i--)
                {
                    if (s[i] == '（' || s[i] == '(') { open = i; break; }
                }
                if (open < 0) return s;
                int depth = 0;
                int close = -1;
                for (int i = open; i < s.Length; i++)
                {
                    if (s[i] == '（' || s[i] == '(') depth++;
                    else if (s[i] == '）' || s[i] == ')')
                    {
                        depth--;
                        if (depth == 0) { close = i; break; }
                    }
                }
                if (close < 0) return s;
                s = s.Substring(0, open) + s.Substring(close + 1);
            }
        }

        private class PendingRequest
        {
            public ImConversation Conv;
            public string Command;
            public bool IsModify;       // 🔴 Q2：修改管线（新卡片带「修改版」标记）
            public int ModifyCount;     // 🔴 Q2：修改版计数
            // 🔴 2026-08-14（M4 风险审视 plan_needed）：随从战术方向（risk_analysis 第一人称）——
            // BuildPlanPrompt 单独成段【随从的打算】，不混入【命令】段（防"谁的命令"语义混淆）
            public string CompanionIntention;
            // 🔴 2026-08-15（目标唯一标记）：回复轮已解析目标（含 #N）——计划轮【目标指认】段直接引用
            public string ResolvedTargetText;
        }

        /// <summary>
        /// 澄清轮状态（🔴 Q1 IM 化：替代 vanilla 澄清输入框）：LLM 返回 questions 后挂起，
        /// 玩家在密令模式的下一句回复并入命令上下文继续生成计划（≤2 轮，与当面 Plot 语义一致）。
        /// </summary>
        private class PendingClarify
        {
            public ImConversation Conv;
            public string Command;   // 已合并的当前命令文本
            public int Round;        // 已完成澄清轮数（≥2 时再收到 questions → 诚实放弃）
        }

        // 🔴 2026-08-12（合并闲聊/计划模式）：执行期说话 → 计划调整（方案 A，用户裁定）。
        // 玩家在计划执行期间发消息，主线程捕获执行上下文（字符串快照），随闲聊回复管线
        // 传到后台 prompt 注入 → LLM 回包判定 adjust_plan → 主线程投递点转 RequestModify。
        // 只存字符串，无 Agent/native 句柄——后台线程安全。
        public class ImExecutionContext
        {
            public string ConvId;          // 执行中卡片所在会话
            public string ExecutorHeroId;  // 执行者（执行中卡片 ExecutorId）
            public string PlanSummary;     // 卡片 PlanSummary（计划摘要）
            public string CurrentStep;     // PlanExecutor.CurrentSummary（当前步骤摘要）
            public string Intent;          // 卡片 PlanIntent
        }

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：会话计划状态（模式指示文本派生 + 输入路由）。
        /// 建议按钮待决不改变 phase（闲聊层）。优先级：Generating > Executing > PendingPlan > Chat。</summary>
        public enum ImSessionPhase { Chat, Generating, PendingPlan, Executing }

        // LLM 结果回主线程消费（PlanCommandFlow 同款 _pendingResult/_resultReady 模式）
        private static PendingRequest _pending;
        private static ImConversation _lastConv;   // 结果归属会话（FinishWith 清 _pending 后仍可定位）
        private static bool _resultReady;
        private static PlanResponse _pendingResult;
        private static PendingClarify _pendingClarify;
        private static string _lastCommand;        // 上一条玩家命令（澄清轮挂起基准）
        private static int _lastModifyCount;       // 本请求的修改版计数（新卡片标记用）

        // 🔴 2026-08-19（目标纪律硬兜底）：计划轮后置检查用（Tick 消费 _pendingResult 时重建全量快照
        // 采集候选——命令文本 + 回复轮是否有目标声明）。FinishWith 随每次计划响应刷新。
        private static string _lastTargetCheckCommand;
        private static bool _lastTargetClaimed;
        private static string _lastTargetClaimedText;

        // 等待执行器出现后补挂回报事件（executor 由 AgentBrain 异步 Create）
        private class PendingWire
        {
            public ImConversation Conv;
            public ImMessage Card;
            public string HeroId;
            public long StartMs;         // 墙钟起点（超时判定，帧数会随 fps/双 tick 漂移）
        }

        private static readonly List<PendingWire> _pendingWires = new List<PendingWire>();

        /// <summary>是否有请求在途（互斥：一次只处理一条命令）。</summary>
        public static bool IsBusy => _pending != null || _resultReady;

        // ───────────────────────── 下达 ─────────────────────────

        /// <summary>玩家在密令模式发送命令文本 → 追加消息 + 计划生成（Mission）/ 规则解析计划（Campaign，Q5b）。
        /// 🔴 Q1：若存在本会话的澄清轮（_pendingClarify），本条消息作为澄清回答并入命令上下文重新生成。</summary>
        /// <param name="companionIntention">🔴 2026-08-14（M4）：随从战术方向（risk_analysis 第一人称，
        /// 随从自己说的话）——计划轮 prompt 单独成段【随从的打算】，计划由计划轮 LLM 决定（他说的不算）。
        /// 普通命令/「制定计划」按钮传 null（零行为变化）。</param>
        /// <param name="resolvedTargetText">🔴 2026-08-15（目标唯一标记）：回复轮已解析的目标（LLM
        /// action_target 原文，含 #N 如 "酒馆店主#3"）——计划轮【目标指认】段直接引用，不再二次解析
        /// 玩家原话（「酒馆老板」→「酒馆店主#3」映射固定）。</param>
        public static void RequestCommand(ImConversation conv, string command, string companionIntention = null,
            string resolvedTargetText = null)
        {
            if (conv == null || string.IsNullOrWhiteSpace(command)) return;
            // 门控复查（UI 已查，铁律 2 风格双保险）
            if (!ImChatView.IsCommandModeAvailable(conv))
            {
                // 提示：密令不可用
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                return;
            }
            if (Mission.Current != null && PlanCommandFlow.IsActiveForOtherConv(conv))
            {
                // 提示：另有密谋进行中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_plot_active", "Another secret order is already being discussed."));
                return;
            }
            if (Mission.Current != null && IsBusy)
            {
                // 提示：上一条命令处理中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_cmd_busy", "Still thinking about your previous order..."));
                return;
            }
            // 澄清轮：玩家回复并入命令上下文（替代 vanilla 澄清输入框，≤2 轮）
            string cmd = command.Trim();
            if (_pendingClarify != null && _pendingClarify.Conv?.Id == conv.Id)
            {
                cmd = $"{_pendingClarify.Command}（{cmd}）";  // lwn-ignore: A
                _pendingClarify.Command = cmd;
                _pendingClarify.Round++;
                // 🔴 2026-08-19（澄清轮选项按钮化）：回复已并入命令上下文 → 同会话未了结澄清卡作废
                //（按钮随锚点重算消失，防玩家手打回复后旧选项按钮误触发新命令）
                try
                {
                    foreach (var m in ImChatStore.GetGroupMessages(conv.Id))
                    {
                        if (m != null && m.IsClarifyCard && string.IsNullOrEmpty(m.ExecutorId))
                            m.ExecutorId = Superseded;
                    }
                }
                catch { }
            }
            // 命令文本入会话（store；不写 NPC 记忆——密令是 Mission 级瞬态）
            // 🔴 2026-08-15（私聊消息顺序修复配套）：玩家消息已由 SendPlayerMessage 发送时写入 store
            //（私聊修复后同一命令会先经发送路径再走本方法）——写前查重防双写（同发送者同内容已有 → 跳过，
            // 含自动触发时序：store 末条可能是刚投递的随从台词，必须全列表扫描）；
            // 密令输入框/提议批准/needPlan 按钮等直通路径（消息未经发送路径）仍需写入。
            bool alreadyStored = ImChatStore.GetGroupMessages(conv.Id).Any(m => m != null
                && m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text
                && string.Equals(m.Content?.Trim(), cmd.Trim(), StringComparison.Ordinal));
            if (!alreadyStored)
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                    Hero.MainHero?.Name?.ToString() ?? "You", cmd, ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                });
            if (Mission.Current == null)
            {
                // Campaign 大地图：规则解析计划（零 LLM；私聊有 party 的 Hero）
                _pendingClarify = null;   // Campaign 计划无澄清轮
                ImMarchOrder.RequestMarchOrder(conv, cmd);
                return;
            }
            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                CompanionIntention = companionIntention,
                ResolvedTargetText = resolvedTargetText,
            };
            // 生成中占位行（思考中文案，与「正在输入」同款）
            AppendGenerating(conv);
            _ = CallPlanAsync(_pending);
        }
        /// <summary>LLM 一次调用（与 PlanCommandFlow.CallPlanAsync 同管线，意图/词表复用其 internal API）。</summary>
        private static async Task CallPlanAsync(PendingRequest req)
        {
            PlanResponse response = null;
            try
            {
                if (!Settings.Instance.IsLLMConfigured) { FinishWith(req, null); return; }
                var snapshot = SceneSnapshot.Build(Mission.Current, agentLimit: 30);
                string persona = BuildPersona(req.Conv);
                // 世界观段切片（blob 单段，主线程现取——CallPlanAsync 在 async 上下文，GetWorldSection 纯查表线程安全；
                // Direct 会话 PartnerHeroId 有值，群聊降级 null——全民同段，id 不参与裁剪）
                string worldSection = WorldBackgroundProvider.GetWorldSection(
                    req.Conv?.Type == ImConversationType.Direct ? req.Conv.PartnerHeroId : null);
                // 🔴 2026-08-19（目标纪律）：命令对应多人 → 【目标候选】段注入（LLM 必须 questions 让主公
                // 挑，禁止自行指定一个）；回复轮目标类型在本快照无匹配 → 诚实声明（人目标必须澄清，物件照常）。
                // 不靠 LLM 自觉——Tick 后置检查（_lastTargetCheckCommand）兜底自动澄清卡。
                string targetCandidatesText = null;
                try
                {
                    // 🔴 2026-08-20（用户裁定：换个人再偷不照办）：换目标意图 → 剥离已锁定目标尾巴
                    //（含 #N）重采候选——否则残留 #54 触发 FindAgentCandidates 唯一化短路（Count=1），
                    // 候选注入条件（>1）不触发，LLM 只能沿用原目标。
                    string candQuery = req.Command;
                    if (HasRetargetIntent(req.Command))
                    {
                        string stripped = StripResolvedTargetSuffix(req.Command);
                        if (!string.IsNullOrWhiteSpace(stripped)) candQuery = stripped;
                        DebugLogger.Log($"[ImCommandFlow] 换目标意图 → 剥离锁定目标重采候选: 「{candQuery}」");
                    }
                    var cands = snapshot.FindAgentCandidates(candQuery);
                    if (cands != null && cands.Count > 1)
                    {
                        var lines = new List<string>();
                        foreach (var ci in cands)
                        {
                            if (ci?.Agent == null) continue;
                            // 🔴 2026-08-19（统一标记格式）：GetDisplayName（Hero 原名 / 模板「名字#Index」），
                            // 与 HUD/交互区/附近频道同构——候选文本 = 显示名 + 方位（编号在前，方位可解析丢弃）
                            string cl = AgentControlHelper.GetDisplayName(ci.Agent); // lwn-ignore: A
                            if (string.IsNullOrWhiteSpace(cl)) cl = ci.DisplayName ?? "某人"; // lwn-ignore: A
                            if (!string.IsNullOrWhiteSpace(ci.PositionDesc)) cl += $"（{ci.PositionDesc}）";   // lwn-ignore: A
                            lines.Add(cl);
                        }
                        if (lines.Count > 1)
                        {
                            // 🔴 2026-08-19（实机：LLM 把 questions 的 options 写成对象数组 [{label,target}]
                            // → 解析抛异常 → 计划生成失败）：明确 options 必须是字符串数组（解析层已容错，
                            // 双保险）。候选文本 = 每行一个候选（行首编号可解析丢弃）。
                            // 🔴 2026-08-20（实机：LLM options 只写名字没方位，玩家没法挑）：要求 options
                            // 直接照抄候选行原文（含编号与方位）——解析层还有按候选列表附加方位的兜底。
                            targetCandidatesText = string.Join("\n", lines)
                                + "\n（以上为候选——必须用 questions 让主公挑选；questions 的 options 必须是字符串数组如 [\"候选1\",\"候选2\"]，禁止写对象数组；options 直接照抄上面候选行原文，含编号与方位（如 \"帝国军团步兵#50（你西侧48米）\"），禁止精简成光秃秃的名字）";
                        }
                    }
                    else if (!string.IsNullOrEmpty(req.ResolvedTargetText)
                        && snapshot.FindAgentCandidates(req.ResolvedTargetText).Count == 0)
                    {
                        // 本地化：目标无匹配诚实段正文（XML LWN_plan_section_target_none_body，{TARGET} 变量）
                        targetCandidatesText = LWNTextHelper.ResolveCompound("LWN_plan_section_target_none_body",
                            "(\"{TARGET}\" has no match in the current scene — if the target is an object (chest/door), plan normally; if it is a person, you MUST ask via questions so the lord can name them — do NOT swap in a different person on your own)",
                            ("TARGET", req.ResolvedTargetText ?? ""));
                    }
                }
                catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 目标候选注入失败: {ex.Message}"); }
                // 🔴 2026-08-19（实机：玩家催促「你怎么不去计划了」→ 计划轮只见催促词、不见原命令
                // → LLM 误判 CUSTOM → plan null → 系统消息「随从想不出主意」）：澄清挂起/上一条命令
                // 语境下把原命令并入命令段（「当前话（原命令：…）」），LLM 才能关联回偷窃/击晕意图。
                string commandForPrompt = req.Command;
                string origCommand = _pendingClarify?.Command ?? _lastCommand;
                if (!string.IsNullOrEmpty(origCommand) && origCommand != req.Command)
                {
                    // 🔴 2026-08-20（换目标重采配套）：换目标意图时原命令的锁定目标尾巴（含 #N）
                    // 剥掉再并入——防 LLM 看到原命令里的 #54 继续沿用（实机：计划轮仍偷帝国弩手#54）
                    string origForPrompt = HasRetargetIntent(req.Command)
                        ? StripResolvedTargetSuffix(origCommand)
                        : origCommand;
                    // 本地化：原命令并入段（玩家可见文本）——命令段内括号注释，LLM 视角 = 追问语境
                    commandForPrompt = $"{req.Command}（原命令：{origForPrompt}）";  // lwn-ignore: A
                    DebugLogger.Log($"[ImCommandFlow] 计划轮并入原命令: 「{req.Command}」→「{commandForPrompt}」");
                }
                string prompt = PromptBuilder.BuildPlanPrompt(
                    snapshot.ToPromptText(), commandForPrompt, persona, "",
                    PlanCommandFlow.IntentTableForPrompt(), PlanCommandFlow.GrammarForPrompt(),
                    companionIntention: req.CompanionIntention,
                    resolvedTargetText: req.ResolvedTargetText,
                    worldSection: worldSection,
                    targetCandidatesText: targetCandidatesText);
                string json = await LLMService.Instance.ChatAsync(prompt, 4000, true, 0.4f, disableReasoning: true);
                string cleaned = LLMService.CleanJson(json);
                try { response = JsonConvert.DeserializeObject<PlanResponse>(cleaned); }
                catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 计划 JSON 解析失败: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] LLM 调用失败: {ex.Message}");
            }
            FinishWith(req, response);
        }
        private static void FinishWith(PendingRequest req, PlanResponse response)
        {
            _lastConv = req?.Conv;
            _lastCommand = req?.Command;   // 澄清轮挂起用（原命令基准）
            _lastModifyCount = req?.IsModify == true ? req.ModifyCount : 0;   // 修改版计数（新卡片标记）
            // 🔴 2026-08-19（目标纪律兜底）：后置检查素材随响应刷新（Tick 消费时重建快照采集候选）
            _lastTargetCheckCommand = req?.Command;
            _lastTargetClaimed = !string.IsNullOrEmpty(req?.ResolvedTargetText);
            _lastTargetClaimedText = req?.ResolvedTargetText;
            _pending = null;
            _pendingResult = response;
            _resultReady = true;
        }
        /// <summary>主线程消费（ImChatManager.Tick → 本方法）。</summary>
        public static void Tick()
        {
            if (_resultReady)
            {
                _resultReady = false;
                var response = _pendingResult;
                _pendingResult = null;
                var conv = _lastConv;
                _lastConv = null;
                int modifyCount = _lastModifyCount;
                _lastModifyCount = 0;
                // 占位行替换（无论成败：卡片上屏 / 失败系统消息）
                RemoveGenerating(conv);
                if (response == null)
                {
                    // 密令失败：计划生成失败
                    _pendingClarify = null;
                    // 本地化：LWN_im_cmd_fail（玩家可见文本）
                    PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
                    return;
                }
                // 澄清轮 IM 化（🔴 Q1）：NPC 问句 = 一条 NPC 消息，玩家回复并入命令上下文（RequestCommand 合并路径）。
                // 铁律 2：needs_clarification 标志位不可信——只有 questions 真的带候选才进入澄清轮。
                // 🔴 2026-08-19（澄清轮选项按钮化，用户裁定）：LLM 候选选项 = 消息底部锚定按钮行
                //（复用 ask_player 卡的 IsAskPlayer + AskPlayerOptions 渲染管线——卡片气泡 + 通用按钮行，
                // 与「制定战术/先不用」同构）——不再拼平铺文本。点击回调 = ImChatView.HandleClarifyOption
                //（选项文本入命令上下文），区别于执行期决策卡（事件回投执行器）。
                if (response.Questions != null && response.Questions.Count > 0)
                {
                    // 澄清超轮（Round ≥ 2）→ 诚实放弃
                    if (_pendingClarify != null && _pendingClarify.Conv?.Id == conv?.Id && _pendingClarify.Round >= 2)
                    {
                        _pendingClarify = null;
                        // 澄清超轮放弃
                        PostNpcMessage(conv, LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                        return;
                    }
                    // 首轮澄清：挂起状态（原命令 = 上一条玩家命令），等玩家回复
                    if (_pendingClarify == null || _pendingClarify.Conv?.Id != conv?.Id)
                        _pendingClarify = new PendingClarify { Conv = conv, Command = _lastCommand, Round = 0 };
                    var q = response.Questions[0];
                    // 澄清轮问句（🔴 2026-08-20：LLM 可能输出 q/question 任一字段名——QText 双兼容）
                    string qText = q?.QText ?? LWNTextHelper.ResolveText("LWN_plan_clarify_default", "What do you mean exactly?");
                    // 🔴 2026-08-20（实机：options 只写名字没方位——「帝国军团步兵#50」和「#57」光看名字
                    // 分不清谁是谁，玩家没法挑）：程序侧兜底（铁律 2，不靠 LLM 自觉）——按原命令采集
                    // 带方位的候选行（目标纪律澄清卡同款管线），LLM 选项缺方位时替换为完整行。
                    List<string> optionCandidates = null;
                    try
                    {
                        string pendingCmd = _pendingClarify?.Command ?? _lastCommand;
                        if (!string.IsNullOrEmpty(pendingCmd))
                            optionCandidates = CollectTargetCandidates(pendingCmd);
                    }
                    catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 澄清选项方位兜底失败: {ex.Message}"); }
                    ResolveSpeaker(conv, out string clarifyHeroId, out string clarifyName);
                    var clarify = new ImMessage(clarifyHeroId, clarifyName, qText, ImMessageKind.Text)
                    {
                        ConvId = conv.Id,
                        // 按钮卡片标记：渲染 = 卡片气泡 + 消息底部锚定按钮行（ask_player 卡管线）
                        IsAskPlayer = true,
                        IsClarifyCard = true,
                        // 选项文本 = 按钮文案 + 事件码（点击后选项文本作为玩家回复入命令上下文）
                        AskPlayerOptions = (q?.Options ?? new List<string>())
                            .Where(o => !string.IsNullOrWhiteSpace(o))
                            .Select(o => {
                                string opt = o.Trim();
                                // 🔴 2026-08-20（实机：options 只写名字没方位，玩家没法挑）：程序侧兜底——
                                // 选项为「名字#编号」且未带方位时，从候选行（带方位）匹配并替换为完整行。
                                if (optionCandidates != null && !opt.Contains("（") && !opt.Contains("("))
                                {
                                    var full = optionCandidates.FirstOrDefault(c => c.StartsWith(opt + "（", StringComparison.Ordinal));
                                    if (!string.IsNullOrEmpty(full)) opt = full;
                                }
                                return new AskPlayerOption(opt, opt);
                            })
                            .ToList(),
                    };
                    ImChatStore.AppendGroupMessage(conv.Id, clarify);
                    ImChatStore.IncUnread(conv.Id);
                    ImChatManager.BroadcastMessageArrived(conv);
                    return;
                }
                // 澄清轮数先捕获（目标纪律兜底的超轮检查要用；与 questions 分支同口径）
                int clarifyRound = _pendingClarify?.Round ?? 0;
                // 🔴 2026-08-19（目标纪律硬兜底，实机：命令「偷士兵的东西」→ 计划轮 LLM 自行指定
                // 帝国资深步兵#41、questions 空 → 玩家全程没机会挑）：
                // 命令对应多人 / 回复轮目标类型在场上无人匹配，而计划轮 LLM 没问（questions 空）
                // → 自动投澄清卡（候选按钮，复用澄清轮卡片管线），禁止计划带病上屏。
                if (response.Plan != null && response.Intent != null
                    && !string.IsNullOrEmpty(response.Intent.IntentType)
                    && IsPersonTargetingIntent(response.Intent.IntentType)
                    && !string.IsNullOrEmpty(_lastTargetCheckCommand)
                    && (response.Questions == null || response.Questions.Count == 0))
                {
                    var clarifyCandidates = CollectTargetCandidates(HasRetargetIntent(_lastTargetCheckCommand)
                        ? StripResolvedTargetSuffix(_lastTargetCheckCommand)
                        : _lastTargetCheckCommand, maxCount: ClarifyCandidateMax);
                    // 🔴 2026-08-19（澄清卡误循环，实机：玩家点选候选后命令含 [#42] 标记 → 唯一解析 →
                    // 但超轮检查在候选判定之前 → 第二轮就把有效计划误杀成「改日再说」）：
                    // 先判候选——命令已唯一解析（含 #N）直接放行；只有仍歧义（多候选/无匹配+有声明）
                    // 才走澄清，且只有澄清路径才受轮数上限约束。
                    if (clarifyCandidates.Count > 1)
                    {
                        // 澄清超轮（Round ≥ 2）→ 诚实放弃（与 LLM questions 分支同款，防无限澄清）
                        if (clarifyRound >= 2)
                        {
                            _pendingClarify = null;
                            PostNpcMessage(conv, LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                            return;
                        }
                        // 多候选 → 澄清卡（候选按钮；点选并入命令上下文重拟计划）
                        // 🔴 不清 _pendingClarify：PostTargetClarify 复用既有 pending，轮数 Round 持续累计
                        PostTargetClarify(conv, clarifyCandidates, multi: true);
                        return;
                    }
                    if (clarifyCandidates.Count == 0 && _lastTargetClaimed)
                    {
                        if (clarifyRound >= 2)
                        {
                            _pendingClarify = null;
                            PostNpcMessage(conv, LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                            return;
                        }
                        // 回复轮声明了目标类型但场上无人匹配 → 诚实澄清（无按钮，玩家自由回答）
                        PostTargetClarify(conv, clarifyCandidates, multi: false);
                        return;
                    }
                }
                _pendingClarify = null;
                // 词表外/无计划 → 诚实拒绝（与 PlanCommandFlow 同语义）
                // 🔴 2026-08-20（实机：LLM 误判 CUSTOM 拒绝「偷士兵」→ reply 走 PostSystem 显示成
                // 居中灰字系统行，玩家误以为系统日志顶替了随从台词）：拒绝台词 = 随从说的话，
                // 必须以随从身份发言（PostNpcMessage），禁止 PostSystem 系统播报形态。
                if (response.Plan == null || response.Intent == null
                    || string.IsNullOrEmpty(response.Intent.IntentType) || response.Intent.IntentType == "CUSTOM")
                {
                    PostNpcMessage(conv, string.IsNullOrEmpty(response.Reply)
                        // 密令被拒：词表外/无计划
                        ? LWNTextHelper.ResolveText("LWN_im_cmd_rejected", "The companion does not understand this order.")
                        : response.Reply);
                    return;
                }
                // 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡）：计划消息 = NPC 自述消息——
                // Sender = 随从（私聊）/通用发言人（群聊），Content = LLM 简述（原独立 narration 消息并入本条），
                // 按钮行渲染在气泡内、锚点跟随链最新消息（讲解后按钮移动）。
                // 🔴 2026-08-12（原「卡片去描述」）：陈述缺省 → 摘要兜底，保证上屏时总有一条 NPC 简述。
                string narration = !string.IsNullOrWhiteSpace(response.Narration)
                    ? response.Narration
                    // 本地化：LWN_plan_default_summary（玩家可见文本）
                    : (response.Plan?.Summary ?? LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?"));
                string summary = !string.IsNullOrEmpty(response.Plan.Summary) ? response.Plan.Summary
                    // 计划摘要缺省文案
                    : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?");
                ResolveSpeaker(conv, out string heroId, out string senderName);
                var card = new ImMessage(heroId, senderName, narration, ImMessageKind.PlanCard)
                {
                    ConvId = conv?.Id ?? "",
                    ResponseJson = JsonConvert.SerializeObject(response,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    PlanJson = JsonConvert.SerializeObject(response.Plan,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    PlanSummary = summary,
                    PlanIntent = response.Intent.IntentType,
                    // 🔴 Q2/Q3：修改版标记 + 陈述 + C# 确定性详情（步骤/应急/安全网）
                    PlanModifyCount = modifyCount,
                    Narration = narration,
                    PlanDetailText = BuildPlanDetail(response.Plan),
                    // 🔴 2026-08-12：计划链锚点（按钮跟随链最新消息；讲解消息复制同 id）
                    ChainId = Guid.NewGuid().ToString(),
                };
                ImChatStore.AppendGroupMessage(card.ConvId, card);
                ImChatStore.IncUnread(card.ConvId);
                ImChatManager.BroadcastMessageArrived(conv);
            }
            // 执行器补挂回报事件（executor 异步创建，轮询重试 ~5s 墙钟超时）
            if (_pendingWires.Count > 0)
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                for (int i = _pendingWires.Count - 1; i >= 0; i--)
                {
                    var wire = _pendingWires[i];
                    if (Mission.Current == null || nowMs - wire.StartMs > 5000)
                    {
                        _pendingWires.RemoveAt(i);
                        continue;
                    }
                    var agent = FindAgentByHeroId(wire.HeroId);
                    var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                    if (executor == null) continue;
                    SubscribeExecutor(executor, wire.Conv, wire.Card);
                    _pendingWires.RemoveAt(i);
                }
            }
        }

        // 🔴 2026-08-19（目标纪律硬兜底三件套）：IsPersonTargetingIntent（意图白名单）/
        // CollectTargetCandidates（全量快照候选采集）/ PostTargetClarify（澄清卡投递，复用澄清轮卡片管线）
        /// <summary>意图是否以「具体的人」为目标（目标纪律兜底只拦人目标——物件/区域/批量语义不适用）。
        /// 词表外/CUSTOM/物件类（FETCH/INTERACT/COMMOTION/ANNIHILATE 等）不拦截。</summary>
        private static bool IsPersonTargetingIntent(string intentType)
        {
            string norm = (intentType ?? "").ToUpperInvariant().Replace("_", "");
            switch (norm)
            {
                case "STEAL": case "KNOCKOUT": case "ATTACK": case "DUEL": case "SPAR":
                case "ENGAGE": case "DRIVEAWAY": case "FOLLOW": case "GUARD": case "BRING":
                case "GUIDE": case "SHADOW": case "COLLECT": case "DISTRACT": case "TALKTO":
                case "DELIVER":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>🔴 2026-08-20（缩略选人卡撑爆面板）：澄清卡候选按钮上限——
        /// 全量候选（12+ 士兵）会撑满缩略面板高度；截断到最近 N 个，远处候选玩家可手打「名字#N」指名。</summary>
        private const int ClarifyCandidateMax = 8;

        /// <summary>目标纪律兜底候选采集（主线程 Tick 调用；全量快照保证与玩家所见一致）。
        /// 返回候选显示文本（名字#N + 方位），空 = 无匹配。
        /// 🔴 2026-08-20（缩略选人卡 12 候选撑爆面板）：候选按距玩家近→远排序；
        /// maxCount &gt; 0 截断到最近 N 个（澄清卡按钮上限；0 = 全量——方位兜底替换需完整清单）。</summary>
        private static List<string> CollectTargetCandidates(string command, int maxCount = 0)
        {
            var result = new List<string>();
            try
            {
                if (Mission.Current == null) return result;
                var snap = SceneSnapshot.Build(Mission.Current);
                var cands = snap.FindAgentCandidates(command);
                if (cands == null) return result;
                // 🔴 2026-08-20：近→远排序（FindAgentCandidates 返回场景序，93 米外的候选排前面会误导挑选）
                if (cands.Count > 1)
                {
                    var playerPos = Agent.Main?.Position ?? Vec3.Zero;
                    cands.Sort((x, y) =>
                    {
                        if (x?.Agent == null || y?.Agent == null) return 0;
                        return x.Agent.Position.DistanceSquared(playerPos)
                            .CompareTo(y.Agent.Position.DistanceSquared(playerPos));
                    });
                }
                foreach (var ci in cands)
                {
                    if (ci?.Agent == null) continue;
                    // 🔴 2026-08-19（候选按钮文本）：GetDisplayName（Hero 原名 / 模板「名字#Index」）+
                    // 相对方位（你西侧47米）——玩家据远近挑目标（选近的/远的）；点选后并入命令上下文，
                    // TryResolveIndexedTarget 数字前缀解析（方位尾巴安全丢弃）。按钮长文本溢出已由
                    // 竖排按钮 XML 加固兜底（MaxWidth + WordWrapping + 高度 CoverChildren）。
                    string label = AgentControlHelper.GetDisplayName(ci.Agent); // lwn-ignore: A
                    if (string.IsNullOrWhiteSpace(label)) label = ci.DisplayName ?? "某人"; // lwn-ignore: A
                    if (!string.IsNullOrWhiteSpace(ci.PositionDesc)) label += $"（{ci.PositionDesc}）";   // lwn-ignore: A
                    if (!result.Contains(label)) result.Add(label);
                    // 🔴 2026-08-20（缩略选人卡撑爆面板）：截断到最近 N 个——12 候选 × 40px 仍超缩略面板
                    // 高度预算，60m+ 的远处候选对「挑就近目标」类命令无意义；玩家仍可手打「名字#N」指名远处
                    // 候选（澄清卡接受手打回复），自由感不损。
                    if (maxCount > 0 && result.Count >= maxCount) break;
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 目标候选采集失败: {ex.Message}"); }
            return result;
        }

        /// <summary>目标纪律澄清卡（复用澄清轮卡片管线：IsClarifyCard + AskPlayerOptions 按钮行；
        /// 点选/手打回复 → HandleClarifyOption/RequestCommand 并入命令上下文重拟计划）。
        /// multi=true → 候选按钮让玩家挑；multi=false → 诚实提问（无按钮，玩家自由回答）。</summary>
        private static void PostTargetClarify(ImConversation conv, List<string> candidates, bool multi)
        {
            try
            {
                if (_pendingClarify == null || _pendingClarify.Conv?.Id != conv?.Id)
                    _pendingClarify = new PendingClarify { Conv = conv, Command = _lastCommand, Round = 0 };
                ResolveSpeaker(conv, out string heroId, out string name);
                string qText = multi
                    // 本地化：目标多候选澄清问句（LWN_plan_clarify_target_multi）
                    ? LWNTextHelper.ResolveText("LWN_plan_clarify_target_multi",
                        "Which one do you mean? Several people match what you said.")
                    // 本地化：目标无匹配澄清问句（LWN_plan_clarify_target_none，{TARGET} 变量）
                    : LWNTextHelper.ResolveCompound("LWN_plan_clarify_target_none",
                        "There is no one matching \"{TARGET}\" here — who did you mean?",
                        ("TARGET", _lastTargetClaimedText ?? ""));
                var clarify = new ImMessage(heroId, name, qText, ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                    IsAskPlayer = true,
                    IsClarifyCard = true,
                    AskPlayerOptions = (candidates ?? new List<string>())
                        .Where(o => !string.IsNullOrWhiteSpace(o))
                        .Select(o => new AskPlayerOption(o.Trim(), o.Trim()))
                        .ToList(),
                };
                ImChatStore.AppendGroupMessage(conv.Id, clarify);
                ImChatStore.IncUnread(conv.Id);
                ImChatManager.BroadcastMessageArrived(conv);
                DebugLogger.Log($"[ImCommandFlow] 目标纪律兜底 → 澄清卡已投递（候选 {candidates?.Count ?? 0} 个）: {qText}");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 目标澄清卡投递失败: {ex.Message}"); }
        }
        // ───────────────────────── 批准/拒绝/中止 ─────────────────────────
        /// <summary>批准/拒绝计划卡片（用户决策 2：批准在 IM 内完成）。</summary>
        public static void Resolve(ImMessage msg, bool approve)
        {
            if (msg == null || !msg.IsPlanCard || !string.IsNullOrEmpty(msg.ExecutorId)) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            if (!approve)
            {
                msg.ExecutorId = Rejected;
                // 密令已撤回
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_cancelled", "Order cancelled."));
                // 🔴 Q1：拒绝 = 密谋输入阶段结束（玩家可立即在该会话重发新命令）
                PlanCommandFlow.End();
                // 🔴 2026-08-12（用户裁定）：拒绝 = 计划彻底抛弃——命令/陈述/卡片从 store 抹除，
                // 不再进入后续上下文（群聊【频道近期消息】）与 UI；私聊命令本就"不写 NPC 记忆"（Mission 级瞬态）。
                ScrubRejectedPlan(conv, msg);
                return;
            }
            PlanResponse response = null;
            try { response = JsonConvert.DeserializeObject<PlanResponse>(msg.ResponseJson); }
            catch (Exception ex) { DebugLogger.Log($"[ImCommandFlow] 卡片响应解析失败: {ex.Message}"); }
            if (response == null || response.Plan == null)
            {
                msg.ExecutorId = Rejected;
                // 密令失败：卡片响应解析失败
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
                return;
            }
            // 执行者解析（Q5 多人协作）：PlanExecutor 原生一带多（subjects → 多 ActorCursor），
            // owner = 第一个执行者（SendEventToAgent 目标），回报消息列出全部执行者
            var executors = ResolveExecutors(conv, response);
            if (executors.Count == 0)
            {
                // 密令失败：无随从在场
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_no_companion", "No companion is present to carry out the order."));
                return;
            }
            Agent executor = executors[0];
            try
            {
                ApplyPlan(conv, executor, response, msg);
                var hero = (executor.Character as CharacterObject)?.HeroObject;
                msg.ExecutorId = hero?.StringId ?? executor.Name;
                string names = string.Join("、", executors.Select(a => a.Name?.ToString() ?? "?"));
                // 密令开始执行（{NAME} 支持多人："张三、李四"）
                PostSystem(conv, LWNTextHelper.ResolveCompound("LWN_im_cmd_started",
                    "{NAME} has begun carrying out the order.", ("NAME", names)));
                // 🔴 Q1：批准 = 密谋输入阶段结束（执行阶段独立，StopPlan 行按执行器状态显示）
                PlanCommandFlow.End();
                // 🔴 2026-08-12（用户反馈）：批准后关闭 IM 面板——玩家直接观察执行，
                // 不再被面板挡着；执行进度走执行摘要 HUD（AgentHud）与当面/密信回报。
                // HandlePlanAction 在 Resolve 返回后对 _vm 判空，Close 置空 _vm 安全。
                ImChatView.Close();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] 计划下发失败: {ex.Message}");
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_fail", "The companion could not form a plan. Rephrase your order."));
            }
        }
        /// <summary>中止执行中的计划（R3 停止键语义，远距离 IM 通道）。</summary>
        public static void Abort(ImMessage msg)
        {
            if (msg == null || !msg.IsPlanCard) return;
            if (string.IsNullOrEmpty(msg.ExecutorId) || msg.ExecutorId == Rejected || msg.ExecutorId == Done || msg.ExecutorId == Superseded) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            Agent agent = FindAgentByHeroId(msg.ExecutorId);
            var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
            if (executor != null)
            {
                executor.CancelByPlayer();
            }
            msg.ExecutorId = Done;
            // 密令中止
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_aborted", "The order has been called off."));
            // 🔴 Q1：中止 = 密谋输入阶段结束（幂等保险）
            PlanCommandFlow.End();
        }
        // ───────────────────────── 修改计划（🔴 Q2，2026-08-10）─────────────────────────
        /// <summary>
        /// 玩家修改计划（Q2）：卡片【修改】→ 输入框 → 修改意见 → 原命令 + 修改意见拼成新命令
        /// → 走同一条 LLM 计划管线 → 新 PlanCard（「修改版 vN」徽标）→ 再批准。
        /// <summary>修改额度：修改 ≤ MaxModifyCount（2 次，成功产出才消耗——复用 Replan 语义，防无限修改刷 LLM）。
        /// 覆盖两态：批准前（卡片待批）与执行中（先中止当前执行，CancelByPlayer 不触发 Replan 自动重入）。
        /// 🔴 2026-08-12（执行期调整 appendPlayerText）：群聊路径玩家消息已被 SendPlayerMessage 写入 store
        /// （公区事实源），adjust 触发时再追加会重复——传 false；私聊路径玩家消息走记忆层不在 store，传 true。</summary>
        public static void RequestModify(ImMessage msg, string text, bool appendPlayerText = true)
        {
            if (msg == null || !msg.IsPlanCard || string.IsNullOrWhiteSpace(text)) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            if (msg.PlanModifyCount >= MaxModifyCount)
            {
                // 修改额度用尽
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_exhausted", "The plan has been revised too many times. Approve it or start over."));
                return;
            }
            if (!ImChatView.IsCommandModeAvailable(conv))
            {
                // 提示：密令不可用
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                return;
            }
            if (Mission.Current != null && IsBusy)
            {
                // 提示：上一条命令处理中
                PostHint(conv, LWNTextHelper.ResolveText("LWN_im_cmd_busy", "Still thinking about your previous order..."));
                return;
            }
            // 执行中 → 中止当前执行（玩家中止路径不触发 Replan 自动重入——OnAborted 仅内部 allowReplan 触发）
            if (IsExecuting(msg))
            {
                Agent agent = FindAgentByHeroId(msg.ExecutorId);
                var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                if (executor != null) executor.CancelByPlayer();
            }
            // 旧卡片标记已修改（按钮全部消失；执行中的中止报告不覆盖此状态）
            msg.ExecutorId = Superseded;
            // 原命令：卡片前最近一条玩家命令消息（PlanCard 摘要不足语义，命令文本在 store 里）
            string original = FindOriginalCommand(conv, msg);
            string cmd = $"{original}（修改：{text.Trim()}）";  // lwn-ignore: A
            // 玩家修改意见入会话（store；不写 NPC 记忆——密令瞬态）
            if (appendPlayerText)
            {
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId,
                    Hero.MainHero?.Name?.ToString() ?? "You", text.Trim(), ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                });
            }
            // 本地化：修改重拟提示
            PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_pending", "The companion is working out a revised plan."));
            if (Mission.Current == null)
            {
                // Campaign 计划无修改（Campaign 侧规则解析，零 LLM）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_cmd_modify_need_mission", "You can only revise a plan while in the field."));
                return;
            }
            _pending = new PendingRequest
            {
                Conv = conv,
                Command = cmd,
                IsModify = true,
                ModifyCount = msg.PlanModifyCount + 1,
            };
            AppendGenerating(conv);
            _ = CallPlanAsync(_pending);
        }
        /// <summary>原命令文本：store 中卡片前最近一条玩家 Text 消息（命令文本确实入 store，RequestCommand 写入）。</summary>
        private static string FindOriginalCommand(ImConversation conv, ImMessage card)
        {
            if (conv == null || card == null) return "";
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m == card) break;
                if (m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text && !string.IsNullOrWhiteSpace(m.Content))
                    return m.Content;
            }
            return card.PlanSummary ?? "";
        }
        /// <summary>会话内最新一张待批计划卡片（ExecutorId 空）。🔴 2026-08-12（用户裁定：
        /// 修改按钮废除 → 输入框发送即修改）：待批卡片存在时，命令模式的发送走 RequestModify 而非新命令。</summary>
        public static ImMessage FindLatestPendingCard(ImConversation conv)
        {
            if (conv == null) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && string.IsNullOrEmpty(m.ExecutorId))
                    return m;
            }
            return null;
        }
        // ───────────────────────── 🔴 2026-08-12（合并闲聊/计划模式）：派生状态 + needPlan 建议 + 执行期调整 ─────────────────────────
        /// <summary>会话是否有挂起的澄清轮（计划生成在等玩家澄清回答——该会话的发送走 RequestCommand 合并，不掺建议）。</summary>
        public static bool HasPendingClarify(ImConversation conv)
        {
            return _pendingClarify != null && _pendingClarify.Conv?.Id == conv?.Id;
        }
        /// <summary>会话内最新一张执行中计划卡片（ExecutorId = 执行者 heroId）。</summary>
        public static ImMessage FindLatestExecutingCard(ImConversation conv)
        {
            if (conv == null) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && IsExecuting(m))
                    return m;
            }
            return null;
        }
        /// <summary>会话内最新一张**指定执行者**的执行中卡片（执行期调整定位用——ctx 带 heroId 无歧义，
        /// 防群聊多人协作时找错执行者；执行已了结/回报在途 → 返回 null，只回台词不产生孤儿修改链）。</summary>
        public static ImMessage FindLatestExecutingCardByHeroId(ImConversation conv, string heroId)
        {
            if (conv == null || string.IsNullOrEmpty(heroId)) return null;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m != null && m.IsPlanCard && IsExecuting(m) && m.ExecutorId == heroId)
                    return m;
            }
            return null;
        }
        /// <summary>会话是否有执行中计划（needPlan 建议抑制规则之一：执行期只走 adjustPlan 通道）。</summary>
        public static bool HasExecutingCard(ImConversation conv) => FindLatestExecutingCard(conv) != null;
        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：会话计划状态派生（模式指示文本 + 输入路由）。
        /// 优先级：Generating（最瞬态）> Executing > PendingPlan > Chat。建议按钮待决不改变 phase（闲聊层）。
        /// 旧卡（rejected/done/superseded）跳过——只有最新活动状态才反映到指示文本。</summary>
        public static ImSessionPhase GetPhase(ImConversation conv)
        {
            if (conv == null) return ImSessionPhase.Chat;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m == null) continue;
                if (m.IsGenerating) return ImSessionPhase.Generating;
                if (m.IsPlanCard && IsExecuting(m)) return ImSessionPhase.Executing;
                if (m.IsPlanCard && string.IsNullOrEmpty(m.ExecutorId)) return ImSessionPhase.PendingPlan;
            }
            return ImSessionPhase.Chat;
        }
        /// <summary>玩家发新消息 → 同会话全部待决建议按钮作废（ExecutorId="superseded"，按钮随锚点重算消失）。</summary>
        public static void InvalidateSuggestions(ImConversation conv)
        {
            if (conv == null) return;
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                foreach (var m in msgs)
                {
                    if (m != null && m.IsPlanSuggest && string.IsNullOrEmpty(m.ExecutorId))
                        m.ExecutorId = Superseded;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] InvalidateSuggestions 异常: {ex.Message}");
            }
        }
        /// <summary>🔴 2026-08-12（needPlan 建议 → 通用消息底部按钮，用户裁定不用计划卡片）：
        /// LLM 判定 need_plan 后，给**刚投递的 NPC 回复消息**打标（IsPlanSuggest + CommandText），
        /// 渲染复用既有 ShowCardBubble + 通用按钮行（制定计划/先不用）。主线程投递点调用
        /// （投递循环内顺序执行，store 最后一条 = 刚投递的 NPC 消息，无竞态）。
        /// 抑制规则（任一命中不打标，NPC 台词照常投递）：
        /// ① !IsCommandModeAvailable —— 一网打尽：附近/家族/王国频道、Plot 总闸、无 LLM、Campaign 无 party Hero
        /// ② IsBusy —— 计划生成中防并发（全局锁，保守）
        /// ③ HasPendingClarify —— 澄清轮挂起（回答走 RequestCommand 合并，不掺建议）
        /// ④ FindLatestExecutingCard —— 执行期只走 adjustPlan 通道</summary>
        /// <summary>needPlan 建议打标：给 NPC 回复消息挂「制定计划/先不用」按钮（玩家确认后才生成计划）。
        /// 🔴 2026-08-15（plan_needed 全手动裁定）：riskAnalysis 参数 = 随从战术方向（risk_analysis 原文）——
        /// plan_needed 场景由 RiskAssessor 调本方法挂按钮并随带战术方向，玩家点「制定计划」后
        /// RequestCommand(companionIntention) 进计划轮【随从的打算】段；普通 need_plan（无风险段）传 null。
        /// 🔴 2026-08-15（目标唯一标记）：resolvedTargetText = 回复轮 LLM 的 action_target（含 #N，如
        /// "酒馆店主#3"）——随按钮存储，计划轮【目标指认】段直接引用（不再二次解析玩家原话）。</summary>
        public static void TryAttachSuggestion(ImConversation conv, string heroId, string heroName, string playerText,
            string riskAnalysis = null, string resolvedTargetText = null)
        {
            try
            {
                if (conv == null || string.IsNullOrEmpty(heroId)) return;
                if (!ImChatView.IsCommandModeAvailable(conv)) return;
                if (IsBusy) return;
                if (HasPendingClarify(conv)) return;
                if (FindLatestExecutingCard(conv) != null) return;
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                ImMessage target = null;
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    var m = msgs[i];
                    if (m != null && m.Kind == ImMessageKind.Text && m.SenderHeroId == heroId
                        && !string.IsNullOrWhiteSpace(m.Content))
                    {
                        target = m;
                        break;
                    }
                }
                if (target == null)
                {
                    DebugLogger.Log($"[ImCommandFlow] 建议打标失败: 找不到 {heroName} 刚投递的消息");
                    return;
                }
                InvalidateSuggestions(conv);   // 同会话旧待决建议作废，只留一张
                target.IsPlanSuggest = true;
                target.CommandText = string.IsNullOrWhiteSpace(playerText) ? target.Content : playerText.Trim();
                target.RiskAnalysisText = riskAnalysis;          // 🔴 2026-08-15：战术方向随按钮存储（全手动裁定）
                target.ResolvedTargetText = resolvedTargetText;  // 🔴 2026-08-15：已解析目标（含 #N）随按钮存储
                AutonomyProposal.Suppress(heroId);   // 本轮互斥：已有建议，不再投自主提议（防双卡）
                // 🔴 2026-08-15（按钮不显示根因修复）：打标发生在消息上屏之后——消息 VM 的
                // ShowCardBubble（卡片气泡容器）是计算属性，直接改字段不触发绑定刷新，按钮行
                //（容器内）不可见，直到切面板重开（全量重建）。这里通知 UI 立即重算锚点 + 广播形态。
                ImChatView.NotifyMessageShapeChanged(target);
                DebugLogger.Log($"[ImCommandFlow] needPlan 建议已打标: {heroName} → \"{target.Content}\"");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] TryAttachSuggestion 异常: {ex.Message}");
            }
        }
        /// <summary>🔴 2026-08-12（执行期说话 → 计划调整，方案 A）：主线程捕获执行上下文
        /// （纯字符串快照，后台线程安全）。无执行中计划返回 null。</summary>
        public static ImExecutionContext BuildExecutionContext(ImConversation conv)
        {
            if (conv == null || Mission.Current == null) return null;
            var card = FindLatestExecutingCard(conv);
            if (card == null) return null;
            string currentStep = "";
            try
            {
                Agent agent = FindAgentByHeroId(card.ExecutorId);
                var executor = agent != null ? PlanExecutor.GetExecutorFor(agent) : null;
                currentStep = executor?.CurrentSummary ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] BuildExecutionContext 异常: {ex.Message}");
            }
            return new ImExecutionContext
            {
                ConvId = conv.Id,
                ExecutorHeroId = card.ExecutorId,
                PlanSummary = card.PlanSummary,
                CurrentStep = currentStep,
                Intent = card.PlanIntent,
            };
        }
        /// <summary>🔴 2026-08-12（执行期说话 → 计划调整）：LLM 判定 adjust_plan 后，主线程投递点调用。
        /// 全部前置复查（铁律 2 双保险）：Mission 才有执行器；IsBusy 防并发生成；按 heroId 复查执行中
        /// （执行已了结/回报在途 → 只回台词）；修改额度 ≤2（成功产出才消耗）。通过 → RequestModify
        /// 出「修改版」卡片 → 玩家批准 → 重执行（全程复用既有管线，CancelByPlayer 不触发 Replan 自动重入）。</summary>
        public static void TryAdjustFromExecution(ImExecutionContext ctx, string playerText)
        {
            try
            {
                if (ctx == null || string.IsNullOrWhiteSpace(playerText)) return;
                if (Mission.Current == null || IsBusy) return;
                var conv = ConversationOf(ctx.ConvId);
                if (conv == null) return;
                var card = FindLatestExecutingCardByHeroId(conv, ctx.ExecutorHeroId);
                if (card == null || !IsExecuting(card)) return;
                if (card.PlanModifyCount >= MaxModifyCount) return;
                RequestModify(card, playerText, appendPlayerText: conv.Type != ImConversationType.Direct);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] TryAdjustFromExecution 异常: {ex.Message}");
            }
        }
        /// <summary>
        /// 拒绝 = 抛弃计划（2026-08-12 用户裁定）：把本次计划交易从 store 整段抹除——
        /// 玩家命令 → NPC 陈述 → 计划卡片（含修改链：命令1→陈述1→卡片1(superseded)→…→当前卡片）。
        /// 群聊：不再进后续 LLM 的【频道近期消息】；私聊：UI 同步清掉（命令本就瞬态，不写 NPC 记忆）。
        /// 「密令已撤回」系统消息（在卡片之后）保留——通用通知，非计划内容。
        /// 🔴 追溯安全边界：只跨 superseded 卡片（同一条计划的修改链）；遇到「另一张待批卡片」
        /// （叠放命令）或「已执行卡片」（ExecutorId=随从）即停——那些不是被拒的这条计划。
        /// </summary>
        private static void ScrubRejectedPlan(ImConversation conv, ImMessage card)
        {
            if (conv == null || card == null) return;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            int cardIdx = msgs.FindIndex(m => m == card);
            if (cardIdx < 0) return;
            // 锚点 = 卡片前最近一条玩家命令（FindOriginalCommand 同源语义）
            int anchor = -1;
            for (int i = cardIdx - 1; i >= 0; i--)
            {
                var m = msgs[i];
                if (m.SenderHeroId == ImChatManager.PlayerId && m.Kind == ImMessageKind.Text
                    && !string.IsNullOrWhiteSpace(m.Content))
                {
                    anchor = i;
                    break;
                }
            }
            if (anchor < 0) { ImChatStore.RemoveMessageRange(conv.Id, cardIdx, 1); return; }   // 防御：只删卡片
            // 向后追溯修改链（严格边界见方法注释）：
            // 新结构（2026-08-12）：命令 → 计划卡片（NPC 简述自述）→ [讲解消息]；修改链 =
            // 命令1 → 卡片1(superseded) → [讲解1] → 命令2(修改) → 卡片2 → [讲解2] …
            int start = anchor;
            while (start > 0)
            {
                var m = msgs[start - 1];
                if (m.IsGenerating) { start--; continue; }
                if (m.IsSystem) { start--; continue; }
                if (m.IsPlanCard && m.ExecutorId == Superseded) { start--; continue; }
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId != ImChatManager.PlayerId
                    && msgs[start].IsPlanCard && msgs[start].ExecutorId == Superseded) { start--; continue; }   // 旧结构：陈述在卡片前
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId == ImChatManager.PlayerId
                    && msgs[start].Kind == ImMessageKind.Text && msgs[start].SenderHeroId != ImChatManager.PlayerId
                    && start + 1 < msgs.Count && msgs[start + 1].IsPlanCard
                    && msgs[start + 1].ExecutorId == Superseded) { start--; continue; }                         // 旧结构：命令→陈述→卡片
                if (m.Kind == ImMessageKind.Text && m.SenderHeroId == ImChatManager.PlayerId
                    && msgs[start].IsPlanCard && msgs[start].ExecutorId == Superseded) { start--; continue; }   // 新结构：命令直接在卡片前
                // 🔴 2026-08-12：讲解消息（带 ChainId 的 NPC 文本）——仅当所属卡片已 superseded 才并入抹除
                //（叠放命令场景：讲解属于仍待批的卡片 → 保留，break 停在这里）
                if (m.Kind == ImMessageKind.Text && !string.IsNullOrEmpty(m.ChainId))
                {
                    ImMessage owner = null;
                    foreach (var x in msgs)
                    {
                        if (x != null && x.IsPlanCard && x.ChainId == m.ChainId) { owner = x; break; }
                    }
                    if (owner != null && owner.ExecutorId == Superseded) { start--; continue; }
                }
                break;
            }
            // 🔴 2026-08-12：向前追溯讲解消息（卡片后同链消息：详解正文）——拒绝 = 整条计划交易抹除。
            // 只扫连续段：中间插入其他消息（叠放命令的新链）即停——那些不是被拒的这条计划
            //（此时被拒卡片的按钮已因锚点规则隐藏，叠放 + 拒旧卡组合在 UI 上不可达，防御即可）。
            // 🔴 旧格式卡片无 ChainId（null == null 会误伤全表）——必须跳过。
            int end = cardIdx;
            if (!string.IsNullOrEmpty(card.ChainId))
            {
                for (int i = cardIdx + 1; i < msgs.Count; i++)
                {
                    if (msgs[i] != null && msgs[i].ChainId == card.ChainId) end = i;
                    else break;
                }
            }
            ImChatStore.RemoveMessageRange(conv.Id, start, end - start + 1);
            DebugLogger.Log($"[ImCommandFlow] 拒绝抛弃计划：抹除 store 消息 {end - start + 1} 条（会话 {conv.Id}）");
        }
        // ───────────────────────── 生成中占位行（🔴 2026-08-12：删进度条，文案与「正在输入」统一）─────────────────────────
        /// <summary>占位行：消息流内 NPC 思考气泡（🔴 2026-08-12：删进度条，文案与输入栏「正在输入」统一；
        /// 🔴 2026-08-12（用户裁定：融入 NPC 气泡）：Sender = 随从/通用发言人；
        /// 🔴 2026-08-12（用户反馈）：思考气泡不带名字前缀——正文纯「正在思考中…」（新 key），
        /// 名字行在 XML 中按 IsGenerating 隐藏；GenerateText 保留给旧存档渲染兜底）。</summary>
        private static void AppendGenerating(ImConversation conv)
        {
            if (conv == null) return;
            ResolveSpeaker(conv, out string heroId, out string senderName);
            // 思考中文案：无名字前缀（用户反馈——名字行/正文都去掉，微信「对方正在输入」语义）
            string thinkingText = LWNTextHelper.ResolveText("LWN_im_generating_plain", "Thinking...");
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(heroId, senderName, thinkingText, ImMessageKind.Generating)
            {
                ConvId = conv.Id,
                GenerateText = thinkingText,
            });
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>移除会话最后一条生成中占位（卡片上屏/失败时替换）。
        /// 🔴 2026-08-12 修复：GetGroupMessages 返回副本，直接 RemoveAt 只改副本——store 占位行残留，
        /// 导致「思考气泡 + 计划气泡」双显（hasGenerating 恒 true，转态重建不触发）。走 RemoveMessageRange 真删。</summary>
        private static void RemoveGenerating(ImConversation conv)
        {
            if (conv == null) return;
            var msgs = ImChatStore.GetGroupMessages(conv.Id);
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i].IsGenerating)
                {
                    ImChatStore.RemoveMessageRange(conv.Id, i, 1);
                    return;
                }
            }
        }
        // ───────────────────────── 卡片详情（🔴 §3.2，C# 确定性渲染，不信任 LLM 文案）─────────────────────────
            // 本地化：动作名标签表（plan_step 记忆/详情渲染）
        /// <summary>动作名 → 本地化标签（LWN_plan_action_*；渲染的是实际会被执行的逻辑——PlanExecutor 同一份 JSON）。
        /// 🔴 2026-08-13：改为 internal static——ActionHandler 决策播报复用同表（闲聊动作码一致，防两份标签漂移）。
        /// 2026-08-13 重构：查 ActionRegistry 主表（FindByLabelCode，ByCode 优先回落别名；
        /// increase_relation→relation_up 由 LabelKey 承载，order_attack→"attack" 统一标签）；
        /// 未知码兜底 key 名 = 码本身（原行为保留）。</summary>
        internal static string PlanActionLabel(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            var spec = ActionRegistry.FindByLabelCode(action);
            if (spec != null && !string.IsNullOrEmpty(spec.LabelKey))
                // 本地化：LWN_plan_action_（玩家可见文本）
                return LWNTextHelper.ResolveText("LWN_plan_action_" + spec.LabelKey, spec.LabelFallback);
            // 未知码兜底（原行为保留：key 名 = 码本身）
            return LWNTextHelper.ResolveText("LWN_plan_action_" + action, action);
        }
        /// <summary>C# 确定性详情渲染：步骤列表 + 应急行（contingencies/on_timeout）+ 安全网（guardrails 摘要）。
        /// 玩家看到的 = 执行器跑的（杜绝演示计划 ≠ 执行计划）。</summary>
        private static string BuildPlanDetail(Plan plan)
        {
            if (plan == null) return "";
            var sb = new StringBuilder();
            // 步骤列表
            if (plan.Steps != null && plan.Steps.Count > 0)
            {
                // 详情标题：计划步骤
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_steps", "Steps:"));
                for (int i = 0; i < plan.Steps.Count; i++)
                {
                    var s = plan.Steps[i];
                    if (s == null) continue;
                    string target = RenderStepTargetText(s);
                    string line = string.IsNullOrEmpty(target)
                        ? PlanActionLabel(s.Action)
                        : PlanActionLabel(s.Action) + " " + target;
                    sb.AppendLine($"  {i + 1}. {line}");
                    if (!string.IsNullOrEmpty(s.OnTimeout) && s.OnTimeout != "@abort_gracefully")
            // 本地化：详情超时行
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_plan_detail_timeout", "    timeout → {TARGET}", ("TARGET", s.OnTimeout)));
                }
            }
            // 应急行（contingencies）
            if (plan.Contingencies != null && plan.Contingencies.Count > 0)
            {
            // 本地化：详情应急安排标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_contingencies", "Contingencies:"));
                foreach (var c in plan.Contingencies)
                {
                    if (c?.When == null || string.IsNullOrEmpty(c.Then)) continue;
                    string cond = RenderCondition(c.When);
                    if (string.IsNullOrEmpty(cond)) continue;
                    // 应急行：若 {COND} 则 {THEN}
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_plan_detail_contingency_line",
                        "  if {COND} then {THEN}", ("COND", cond), ("THEN", c.Then)));
                }
            }
            // 安全网摘要（guardrails：既有 R1-R7 由执行器内建，这里只提示关键语义）
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_detail_guardrail",
                "  Safety: unexpected threats pause the plan; the stop key recalls the companion."));
            return sb.ToString();
        }
        private static string RenderStepTargetText(PlanStep s)
        {
            if (s == null || s.Target == null) return "";
            if (s.Target.Type == Newtonsoft.Json.Linq.JTokenType.String)
                return (string)s.Target ?? "";
            if (s.Target.Type == Newtonsoft.Json.Linq.JTokenType.Object && s.Target["query"] != null)
                return (string)s.Target["query"] ?? "";
            return "";
        }
        private static string RenderCondition(Condition c)
        {
            if (c == null || string.IsNullOrEmpty(c.Type)) return "";
            if (c.Type == "and" || c.Type == "or")
            {
                if (c.Conditions == null || c.Conditions.Count == 0) return "";
                return "(" + string.Join(c.Type == "and" ? " & " : " | ",
                    c.Conditions.Select(x => RenderCondition(x)).Where(x => !string.IsNullOrEmpty(x))) + ")";
            }
            return $"{c.Type}({c.A ?? ""},{c.B ?? ""}) {c.Op ?? ""} {c.Value}";
        }
        // ───────────────────────── 计划执行记忆纪律（🔴 §2.1 单向链条，2026-08-10）─────────────────────────
        /// <summary>
        /// 步骤执行完成 → 写执行者记忆（唯一写入点）。记忆只记录「实际发生过的事」：
        /// 计划生成/批准/修改/中止等元数据一律不写（树/网）；步骤完成按执行顺序逐条追加 = 单向链条。
        /// 渲染纯 C#（动作标签表 + 目标文本），不信任 LLM。role = "plan_step" 独立前缀，
        /// 私聊 UI 按 im_user/im_npc 过滤天然隔离；行进 LLM 上下文 → 后续对话 NPC 接得住。
        /// </summary>
        private static void WritePlanStepMemory(PlanExecutor executor, Agent agent, PlanStep step)
        {
            if (executor == null || agent == null || step == null) return;
            try
            {
                string action = PlanActionLabel(step.Action);
                string target = RenderStepTargetText(step);
                // 🔴 2026-08-20（实机：偷窃被目击中断却记成「已完成」——NPC 以为自己偷成了）：
                // 判定型步骤（steal_attempt/steal_equipment 等）按 result key 路由记忆文案——
                // interrupted/empty/impossible 如实记「没办成」，其余（含普通步骤）才记「已完成」。
                // result key 由 OnStepCompleted 回调在 CompleteStep 清空前读取（executor.StepResultKey）。
                string resultKey = executor?.StepResultKey;
                string content;
                switch (resultKey)
                {
                    case "interrupted":
                        content = LWNTextHelper.ResolveCompound("LWN_plan_step_memory_interrupted",
                            "By my lord's order, {ACTION} {TARGET} - I was seen and could not go through with it.", ("ACTION", action), ("TARGET", target)).Trim();
                        break;
                    case "empty":
                        content = LWNTextHelper.ResolveCompound("LWN_plan_step_memory_empty",
                            "By my lord's order, {ACTION} {TARGET} - there was nothing to take.", ("ACTION", action), ("TARGET", target)).Trim();
                        break;
                    case "impossible":
                        content = LWNTextHelper.ResolveCompound("LWN_plan_step_memory_impossible",
                            "By my lord's order, {ACTION} {TARGET} - there was no way to do it.", ("ACTION", action), ("TARGET", target)).Trim();
                        break;
                    default:
                        // 本地化：plan_step 记忆渲染（成功/普通完成）
                        content = LWNTextHelper.ResolveCompound("LWN_plan_step_memory",
                            "By my lord's order, {ACTION} {TARGET}, done.", ("ACTION", action), ("TARGET", target)).Trim();
                        break;
                }
                if (string.IsNullOrWhiteSpace(content)) return;
                var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                if (memory == null) return;
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                memory.AddHistory("plan_step", content, hero?.StringId ?? agent.Name);
                DebugLogger.Log($"[ImCommandFlow] plan_step 记忆写入: {agent.Name}: {content}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImCommandFlow] plan_step 记忆写入异常: {ex.Message}");
            }
        }
        /// <summary>卡片是否执行中（中止按钮可见性；Superseded 已修改 = 非执行中）。</summary>
        public static bool IsExecuting(ImMessage msg)
        {
            return msg != null && msg.IsPlanCard
                && !string.IsNullOrEmpty(msg.ExecutorId)
                && msg.ExecutorId != Rejected && msg.ExecutorId != Done && msg.ExecutorId != Superseded;
        }
        /// <summary>执行器回报回 IM：executor 由 AgentBrain 异步 Create，走补挂队列（Tick 轮询）。</summary>
        private static void WireExecutorReports(ImConversation conv, Agent executorAgent, ImMessage card)
        {
            if (executorAgent == null) return;
            var hero = (executorAgent.Character as CharacterObject)?.HeroObject;
            string heroId = hero?.StringId ?? executorAgent.Name;
            _pendingWires.Add(new PendingWire
            {
                Conv = conv,
                Card = card,
                HeroId = heroId,
                StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }
        /// <summary>挂一次回报事件（OnFinished/OnAborted → IM 系统消息 + 卡片了结；原密信 DisplayMessage 保留双渠道）。
        /// 🔴 §2.1：OnStepCompleted 挂接——步骤完成写执行者记忆（plan_step 单向链条）。</summary>
        private static void SubscribeExecutor(PlanExecutor executor, ImConversation conv, ImMessage card)
        {
            executor.OnStepCompleted += (ex, agent, step) => WritePlanStepMemory(ex, agent, step);
            executor.OnFinished += ex =>
            {
                try
                {
                    card.ExecutorId = Done;
                    string report = !string.IsNullOrEmpty(ex?.EndMessage)
                        ? ex.EndMessage
                        // 密令完成回报
                        : LWNTextHelper.ResolveText("LWN_im_cmd_done", "The order has been carried out.");
                    PostSystem(conv, report);
                }
                catch (Exception e) { DebugLogger.Log($"[ImCommandFlow] 完成回报异常: {e.Message}"); }
            };
            executor.OnAborted += (ex, reason) =>
            {
                try
                {
                    card.ExecutorId = Done;
                    PostSystem(conv, string.IsNullOrEmpty(reason)
                        // 密令中止（执行器中止回报）
                        ? LWNTextHelper.ResolveText("LWN_im_cmd_aborted", "The order has been called off.")
                        : reason);
                }
                catch (Exception e) { DebugLogger.Log($"[ImCommandFlow] 中止回报异常: {e.Message}"); }
            };
        }
        /// <summary>下发执行（PlanCommandFlow.ApplyPlan 同款：反应计划 + 意图 target 解析 + SendEventToAgent）。</summary>
        private static void ApplyPlan(ImConversation conv, Agent companion, PlanResponse response, ImMessage card)
        {
            // 反应计划（ReactiveAgent 覆盖默认模板）
            if (response.Reactions != null)
            {
                foreach (var rp in response.Reactions)
                {
                    if (rp == null || string.IsNullOrEmpty(rp.Role)) continue;
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(rp.Role);
                    if (info?.Agent != null)
                        ReactiveAgent.ApplyPlan(info.Agent, rp);
                }
            }
            // 意图 target 解析（角色表注入）
            Agent target = null;
            var intent = response.Intent;
            if (intent != null)
            {
                string t = intent.GetTargetRef(out string _);
                if (!string.IsNullOrEmpty(t) && t != "player" && t != "self")
                {
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(t);
                    target = info?.Agent;
                }
            }
            string planJson = JsonConvert.SerializeObject(response.Plan,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            string intentType = intent?.IntentType;
            AgentAIController.Instance?.SendEventToAgent(companion, "order_execute_plan",
                planJson, intentType, target, card.Content);
            WireExecutorReports(conv, companion, card);
        }
        // ───────────────────────── 辅助 ─────────────────────────
        /// <summary>
        /// 执行者解析（Q5 多人协作）：私聊 = 该随从；队伍频道 = 意图 subjects（"你们/你俩"）逐个解析，
        /// 兜底第一个玩家队伍成员。返回列表（[0] = owner = SendEventToAgent 目标；其余 = 协作执行者，
        /// PlanExecutor.BuildCursors 会为每个 subjects 角色建独立 ActorCursor 一带多并行）。
        /// </summary>
        private static List<Agent> ResolveExecutors(ImConversation conv, PlanResponse response)
        {
            var list = new List<Agent>();
            if (Mission.Current == null) return list;
            if (conv.Type == ImConversationType.Direct)
            {
                var a = FindAgentByHeroId(conv.PartnerHeroId);
                if (a != null) list.Add(a);
                return list;
            }
            // 队伍频道：subjects 优先，兜底第一个玩家队伍成员
            Agent owner = null;
            if (response?.Intent?.Subjects != null)
            {
                foreach (var sub in response.Intent.Subjects)
                {
                    if (string.IsNullOrEmpty(sub)) continue;
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(sub);
                    if (info?.Agent != null && FriendlinessHelper.IsPlayerPartyMember(info.Agent))
                    {
                        if (owner == null) owner = info.Agent;
                        if (!list.Contains(info.Agent)) list.Add(info.Agent);
                    }
                }
            }
            if (owner == null)
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (FriendlinessHelper.IsPlayerPartyMember(a)) { owner = a; break; }
                }
            }
            if (owner != null && !list.Contains(owner)) list.Insert(0, owner);
            return list;
        }
        private static Agent FindAgentByHeroId(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return null;
            foreach (var a in Mission.Current.Agents)
            {
                var hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero != null && hero.StringId == heroId)
                    return a;
            }
            return null;
        }
        private static ImConversation ConversationOf(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return null;
            if (convId.StartsWith("direct_"))
                return ImChatManager.GetDirectConversation(convId.Substring("direct_".Length));
            return ImChatManager.GetGroupConversation(convId == ImChatStore.ChannelClan
                ? ImConversationType.Clan
                : convId == ImChatStore.ChannelKingdom ? ImConversationType.Kingdom : ImConversationType.Party);
        }
        private static string BuildPersona(ImConversation conv)
        {
            try
            {
                string masterName = Hero.MainHero?.Name?.ToString() ?? "";
                string heroName = "";
                if (conv.Type == ImConversationType.Direct)
                {
                    var hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                    heroName = hero?.Name?.ToString() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(heroName))
                    return $"你是 {heroName}，{masterName} 的随从。说话简短、恭敬、务实，像游戏里的随从。";  // lwn-ignore: A
                return $"你是 {masterName} 的随从。说话简短、恭敬、务实，像游戏里的随从。";  // lwn-ignore: A
            }
            catch { return "你是一名随从。说话简短、务实，像游戏里的随从。"; }  // lwn-ignore: A
        }
        private static void PostSystem(ImConversation conv, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId, "System", content, ImMessageKind.System)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>NPC 消息入会话（🔴 Q1 澄清轮问句用：带发言人的普通消息，走消息流管道）。
        /// 私聊 = 随从 Hero 名义；群聊 = 无 Hero 语义的通用发言人名义（当前密令只走私聊/队伍频道）。</summary>
        private static void PostNpcMessage(ImConversation conv, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            ResolveSpeaker(conv, out string heroId, out string senderName);
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(heroId, senderName, content, ImMessageKind.Text)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }
        /// <summary>会话发言人解析（🔴 2026-08-12 抽取，计划消息/占位/讲解/澄清共用）：
        /// 私聊 = 随从 Hero（Id + 名）；群聊 = 无 Hero 语义 → 通用发言人兜底（LWN_im_npc_companion）。</summary>
        private static void ResolveSpeaker(ImConversation conv, out string heroId, out string senderName)
        {
            heroId = conv?.Type == ImConversationType.Direct ? conv.PartnerHeroId : "";
            senderName = "";
            if (conv?.Type == ImConversationType.Direct)
            {
                try
                {
                    senderName = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId)?.Name?.ToString() ?? "";
                }
                catch { }
            }
            if (string.IsNullOrEmpty(senderName))
                // 本地化：通用发言人兜底名
                senderName = LWNTextHelper.ResolveText("LWN_im_npc_companion", "Companion");
        }
        private static void PostHint(ImConversation conv, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            InformationManager.DisplayMessage(new InformationMessage(content));
            PostSystem(conv, content);
        }
    }
}