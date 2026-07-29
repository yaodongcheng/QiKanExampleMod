using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 动态对话注入器 — 从 JSON 构建对话图，直接注册到 ConversationManager。
    ///
    /// 核心模型：每轮 = NPC 说一句 → 多个玩家选项 → 每个选项对应一句 NPC 回应。
    /// 不依赖 DialogFlow 建造者，直接用 ConversationManager.AddDialogLine / AddPlayerLine 注册。
    /// </summary>
    public static class DialogueInjector
    {
        // ═══════════════════════════════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 从 JSON 文件注入对话到当前 NPC 对话树。
        /// </summary>
        /// <param name="jsonPath">JSON 文件的完整路径</param>
        /// <returns>注入结果描述</returns>
        public static string InjectFromJson(string jsonPath)
        {
            if (Campaign.Current == null)
                return "Error: Campaign not loaded.";

            var cm = Campaign.Current.ConversationManager;

            // 1. 读取并解析 JSON
            //读取
            string json;
            try { json = File.ReadAllText(jsonPath, Encoding.UTF8); }
            catch (Exception ex) { return $"Error: Failed to read file.\n{ex.Message}"; }
            //格式化解析
            DialogueInjectScript script;
            try { script = JsonConvert.DeserializeObject<DialogueInjectScript>(json); }
            catch (Exception ex) { return $"Error: JSON parse failed.\n{ex.Message}"; }

            if (script == null || script.Nodes == null || script.Nodes.Count == 0)
                return "Error: JSON is empty or missing 'turns' array.";

            // 2. 确定注入起始 token
            string startToken;
            if (!string.IsNullOrEmpty(script.InjectAtToken))
            {
                startToken = script.InjectAtToken;
            }
            else
            {
                // 默认挂在 NPC 主菜单：关闭当前对话后重新跟 NPC 说话即可看到注入的选项。
                startToken = "hero_main_options";
            }

            // 3. 创建 owner 哨兵（用于 cleanup），加版本号防 token 碰撞。
            string jsonName = Path.GetFileNameWithoutExtension(jsonPath);
            string baseLabel = jsonName;
            string fileTag = $"{jsonName}_v{_injectionCounter++}";
            var owner = new InjectOwner { FileName = fileTag, BaseLabel = baseLabel };
            _injectedOwners.Add(owner);

            // 4. 注入：node 的 Id 直接用作 ConversationManager token，加文件前缀防跨文件碰撞。
            _tokenCounter = 0;
            int nodeCount = 0;

            try
            {
                // —— 网关 ———
                string entryNodeToken = NodeToken(fileTag, script.EntryNode);
                string entryText = !string.IsNullOrEmpty(script.EntryOption)
                    ? script.EntryOption
                    : $"「{Path.GetFileNameWithoutExtension(jsonPath)}」";
                var gateDf = DialogFlow.CreateDialogFlow(startToken, 125);
                gateDf.AddPlayerLine(
                    "inj_gateway", startToken, entryNodeToken,
                    entryText,
                    () => true, null, owner, 125);
                cm.AddDialogFlow(gateDf, owner);
                nodeCount++;

                // —— 逐 node 注册 ——
                foreach (var node in script.Nodes)
                {
                    string nodeEntryToken = NodeToken(fileTag, node.Id);

                    if (node.Transitions == null || node.Transitions.Count == 0)
                    {
                        // Terminal node: NPC 说话 → 关窗
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, "close_window", node);
                        nodeCount++;
                    }
                    else
                    {
                        string afterNpcLine = NextToken(fileTag);
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, afterNpcLine, node);
                        nodeCount++;

                        foreach (var transition in node.Transitions)
                        {
                            nodeCount += RegisterTransition(cm, node, transition, afterNpcLine, fileTag, owner);
                        }
                    }
                }

                string atTokenDesc = startToken == "hero_main_options"
                    ? "hero_main_options (will appear when talking to any NPC)"
                    : $"token '{startToken}' (in active conversation)";

                return $"SUCCESS: Injected '{Path.GetFileName(jsonPath)}'\n" +
                       $"  Turns: {script.Nodes.Count}, Nodes: {nodeCount}\n" +
                       $"  Anchor: {atTokenDesc}\n" +
                       $"  Owner count: {_injectedOwners.Count}\n" +
                       $"  Use 'custom.inject_dialogue clear' to remove all injections.";
            }
            catch (Exception ex)
            {
                return $"Error during injection:\n{ex.Message}\n{ex.StackTrace}";
            }
        }

        /// <summary>
        /// 清除所有通过 DialogueInjector 注入的对话节点。
        /// </summary>
        public static string ClearAll()
        {
            if (Campaign.Current == null)
                return "Error: Campaign not loaded.";

            if (_injectedOwners.Count == 0)
                return "No injected dialogues to clear.";

            int cleared = 0;
            foreach (var owner in _injectedOwners)
            {
                Campaign.Current.ConversationManager.RemoveRelatedLines(owner);
                cleared++;
            }
            _injectedOwners.Clear();
            _tokenCounter = 0;
            return $"Cleared {cleared} injected dialogue batches.";
        }

        /// <summary>
        /// 按标签清除注入的对话节点（如 "crime_EVENTID"）。
        /// </summary>
        public static void RemoveRelatedLines(string label)
        {
            if (Campaign.Current == null) return;
            var toRemove = _injectedOwners.Where(o => o.BaseLabel == label).ToList();
            foreach (var owner in toRemove)
            {
                try { Campaign.Current.ConversationManager.RemoveRelatedLines(owner); }
                catch { }
                _injectedOwners.Remove(owner);
            }
        }

        /// <summary>
        /// 按文件名查找 JSON 测试文件。
        /// 搜索顺序：本 mod 的 ModuleData/DesignData/Dialogues/ → 游戏 Configs/
        /// </summary>
        public static string FindJsonFile(string fileName)
        {
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                fileName += ".json";

            // 从本 DLL 所在位置反推 mod 根目录
            var dllPath = typeof(DialogueInjector).Assembly.Location;
            // dllPath = ...\Modules\LivingWorldNpcs\bin\Win64_Shipping_Client\LivingWorldNpcs.dll
            // 上溯两级到 mod 根: ...\Modules\LivingWorldNpcs\
            var modDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dllPath)));

            var searchPaths = new[]
            {
                Path.Combine(modDir ?? "", "ModuleData", "DesignData", "Dialogues", fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Mount and Blade II Bannerlord", "Configs", fileName)
            };

            foreach (var p in searchPaths)
                if (File.Exists(p))
                    return p;

            return null;
        }

        public static string GetSearchPathsDescription(string fileName)
        {
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                fileName += ".json";

            var sb = new StringBuilder();
            sb.AppendLine($"File '{fileName}' not found. Tried:");
            sb.AppendLine($"  Modules/LivingWorldNpcs/ModuleData/DesignData/Dialogues/{fileName}");
            sb.AppendLine($"  Documents/Mount and Blade II Bannerlord/Configs/{fileName}");
            return sb.ToString();
        }

        /// <summary>
        /// 调试日志：打印 DialogueInjectScript 完整结构（每个 node 的 NPC 台词 + 选项）。
        /// 供各调用方（CrimeDialogueBuilder、AtomicAction 等）在注入前/后排查对话图。
        ///
        /// 打印顺序 = 对话流向（从 EntryNode 出发沿 Transition 边 DFS），而非 nodes 列表的构建顺序。
        /// 构建顺序是"子树先建、入口后 Add"（如 BuildDiscoveryNode 先 BuildConfessSubtree 再 Add injectedStart），
        /// 直接按下标打印会把入口节点埋到列表中间。功能上注入按 Id 链接，与列表顺序无关。
        /// 不可达的孤儿节点按原顺序附在最后，不静默丢弃。
        /// </summary>
        /// <param name="script">要打印的脚本</param>
        /// <param name="label">日志前缀，如 "[CrimeDialog]" / "[AlertForceConv]"</param>
        public static void LogScript(DialogueInjectScript script, string label)
        {
            if (script?.Nodes == null) return;

            // ── 从 EntryNode 沿 Transition 边 DFS，排出对话流向顺序 ──
            var byId = new Dictionary<string, DialogueNode>();
            foreach (var n in script.Nodes)
                if (n?.Id != null && !byId.ContainsKey(n.Id))
                    byId[n.Id] = n;

            var ordered = new List<DialogueNode>();
            var visited = new HashSet<string>();
            var stack = new Stack<DialogueNode>();
            DialogueNode entry = null;
            if (!string.IsNullOrEmpty(script.EntryNode))
                byId.TryGetValue(script.EntryNode, out entry);
            stack.Push(entry ?? (script.Nodes.Count > 0 ? script.Nodes[0] : null));

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node?.Id == null || !visited.Add(node.Id)) continue;
                ordered.Add(node);
                if (node.Transitions == null) continue;
                // 逆序压栈 → 弹栈时选项按列表原序展开；同一选项内失败边先压、成功边后压 → 成功分支先打印
                for (int i = node.Transitions.Count - 1; i >= 0; i--)
                {
                    var tr = node.Transitions[i];
                    if (tr.CheckType == TransitionCheckType.SkillCheck
                        && !string.IsNullOrEmpty(tr.NextNodeOnFail)
                        && byId.TryGetValue(tr.NextNodeOnFail, out var failNode))
                        stack.Push(failNode);
                    if (!string.IsNullOrEmpty(tr.NextNodeOnSuccess)
                        && byId.TryGetValue(tr.NextNodeOnSuccess, out var okNode))
                        stack.Push(okNode);
                }
            }
            // 不可达节点（孤儿）附最后
            foreach (var n in script.Nodes)
                if (n != null && (n.Id == null || !visited.Contains(n.Id)))
                    ordered.Add(n);

            for (int ti = 0; ti < ordered.Count; ti++)
            {
                var t = ordered[ti];
                string lazyTag = t.LazyNpcLine != null ? " [Lazy]" : "";
                DebugLogger.Log($"{label} Turn[{ti}] id={t.Id} NpcLine=\"{t.NpcLine}\"{lazyTag}");
                if (t.Transitions == null) continue;
                for (int oi = 0; oi < t.Transitions.Count; oi++)
                {
                    var transition = t.Transitions[oi];
                    string action = transition.Action ?? "NONE";
                    string checkInfo = transition.CheckType == TransitionCheckType.SkillCheck
                        ? $" [SkillCheck]"
                        : "";
                    string next = !string.IsNullOrEmpty(transition.NextNodeOnSuccess) ? transition.NextNodeOnSuccess : "(关闭)";
                    string nextFail = transition.CheckType == TransitionCheckType.SkillCheck
                        ? (!string.IsNullOrEmpty(transition.NextNodeOnFail) ? transition.NextNodeOnFail : "(同Success)")
                        : "";
                    string actionParam = !string.IsNullOrEmpty(transition.ActionParam) ? $" Param={transition.ActionParam}" : "";
                    string routeInfo = transition.CheckType == TransitionCheckType.SkillCheck
                        ? $" | NextNodeOnSuccess={next} | NextNodeOnFail={nextFail}"
                        : $" | NextNodeOnSuccess={next}";
                    DebugLogger.Log($"{label}   Transition[{oi}] \"{transition.PlayerLine}\" → {action}{actionParam}{checkInfo}{routeInfo}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部状态
        // ═══════════════════════════════════════════════════════════════

        private static int _tokenCounter = 0;
        private static int _injectionCounter = 0;
        private static readonly List<InjectOwner> _injectedOwners = new List<InjectOwner>();

        /// <summary>检定结果回写表：afterPlayer token → 是否通过。InjectScriptGateway 注册双线 NPC 回应时查此表。</summary>
        private static readonly Dictionary<string, bool> _intentResults = new Dictionary<string, bool>();

        private class InjectOwner { public string FileName; public string BaseLabel; }

        private static string NextToken(string fileTag) => $"lwnpc_{fileTag}_atk_{_tokenCounter++}";

        /// <summary>Turn 的 Id → ConversationManager token。加文件前缀，不同 JSON 的同名 Id 互不冲突。</summary>
        private static string NodeToken(string fileTag, string turnId) => $"lwnpc_{fileTag}_{turnId}";

        // ═══════════════════════════════════════════════════════════════
        // 反射工具
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// [暂未使用] 反射获取 ConversationManager 当前 ActiveToken 对应的字符串。
        /// 可用于高级场景：在对话中途精确注入到当前状态（需配合 CurOptions 刷新）。
        /// </summary>
        private static string GetCurrentConversationTokenString(object cm)
        {
            try
            {
                var cmType = cm.GetType();

                var stateMapField = cmType.GetField("stateMap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (stateMapField == null) return null;
                var stateMap = stateMapField.GetValue(cm) as Dictionary<string, int>;
                if (stateMap == null) return null;

                var activeTokenField = cmType.GetField("ActiveToken",
                    BindingFlags.Public | BindingFlags.Instance);
                if (activeTokenField == null) return null;
                int activeToken = (int)activeTokenField.GetValue(cm);

                foreach (var kv in stateMap)
                    if (kv.Value == activeToken)
                        return kv.Key;
            }
            catch { }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // 动作执行
        // ═══════════════════════════════════════════════════════════════

        private static void ExecuteAction(DialogueTransition transition)
        {
            if (string.IsNullOrEmpty(transition.Action) || transition.Action == "NONE")
                return;

            try
            {
                var oneToOne = Campaign.Current.ConversationManager.OneToOneConversationHero;

                switch (transition.Action.ToUpperInvariant())
                {
                    case "CLOSE_DIALOG":
                        // no-op marker: Transition 仅用于关窗，无副作用
                        break;
                    default:
                        // ── INTENT:xxx 委托 ──
                        if (transition.Action.StartsWith("INTENT:", StringComparison.OrdinalIgnoreCase))
                        {
                            string intentSpec = transition.Action.Substring(7);
                            ExecuteIntentAction(intentSpec, oneToOne, transition.ActionParam, transition.ResultKey);
                        }
                        else
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage($"[DialogueInjector] Unknown action: {transition.Action}"));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"[DialogueInjector] Action '{transition.Action}' failed: {ex.Message}"));
            }
        }

        /// <summary>
        /// 执行 INTENT:xxx 动作：查 IntentRegistry → Evaluate → OnInstant/OnSuccess/OnFail
        /// </summary>
        private static void ExecuteIntentAction(string intentName, Hero npc, string actionParam = null, string resultKey = null)
        {
            try
            {
                var intent = LivingWorldNpcs.IntentRegistry.FindByName(intentName);
                if (intent == null)
                {
                    DebugLogger.Log($"[DialogueInjector] Intent not found: {intentName}");
                    return;
                }

                // 从 ConversationManager 获取当前对话的 Agent（非 Agent.Main）
                Agent partnerAgent = null;
                try
                {
                    var cm = Campaign.Current?.ConversationManager;
                    if (cm != null)
                    {
                        partnerAgent = cm.OneToOneConversationAgent as Agent;
                    }
                }
                catch { }

                // 构建上下文：Agent 可能为 null（大地图无 Mission 时），降级处理。
                // 模板 NPC（HeroObject==null）时 npc?.CurrentSettlement 为 null ——
                // 必须与 BuildTransitionCondition 显示路径一样回退 Settlement.CurrentSettlement，
                // 否则村民（无 Hero）强制对话里所有 INTENT 都会因 ActiveEvent==null 被 Evaluate 静默 Hidden：
                // 选项照显示、点了没效果（拔剑/赔偿/认罚全部失效）。
                var settlement = npc?.CurrentSettlement ?? Settlement.CurrentSettlement;
                var worldEvt = settlement != null ? WorldEventStore.FindActive(settlement.StringId) : null;
                var ctx = new IntentContext(partnerAgent, speaker: npc, worldEvent: worldEvt, actionParam: actionParam);

                var eligibility = intent.Evaluate(ctx);
                if (eligibility.State == EligState.Hidden)
                {
                    DebugLogger.Log($"[DialogueInjector] Intent {intentName} hidden by Evaluate");
                    return;
                }
                if (eligibility.State == EligState.Disabled)
                {
                    string reason = !string.IsNullOrEmpty(eligibility.Reason)
                        ? eligibility.Reason : "现在不行。";
                    InformationManager.DisplayMessage(new InformationMessage(reason));
                    DebugLogger.Log($"[DialogueInjector] Intent {intentName} disabled: {eligibility.Reason}");
                    return;
                }
                //不需要检定
                if (intent.Goal == null)
                {
                    intent.OnInstant(ctx);
                    if (resultKey != null) _intentResults[resultKey] = true;
                }
                //需要检定
                else
                {
                    var roll = SingleRollResolver.SimpleCompute(ctx, intent.Tactic, intent.GetOfferValue(ctx));
                    bool passed = SingleRollResolver.Roll(roll.Chance);
                    DebugLogger.Log($"[SkillCheck] {intentName} | {roll.Log} | 掷骰={(passed ? "通过" : "失败")} (chance={roll.Chance:P0})");
                    if (resultKey != null) _intentResults[resultKey] = passed;
                    if (passed)
                    {
                        // 屏幕上方弹出检定成功提示
                        try
                        {
                            SkillObject skill = SkillCheckSystem.MapTacticToSkill(intent.Tactic);
                            MBInformationManager.AddQuickInformation(new TextObject($"{skill.Name}检定成功"));
                        }
                        catch { }
                        intent.OnSuccess(ctx);
                    }
                    else
                    {
                        // 屏幕上方弹出检定失败提示：技能、等级差距、成功率
                        try
                        {
                            SkillObject skill = SkillCheckSystem.MapTacticToSkill(intent.Tactic);
                            float myLevel = Hero.MainHero.GetSkillValue(skill);
                            float npcLevel = ctx.Speaker?.GetSkillValue(skill) ?? 50f;
                            string npcName = ctx.Speaker?.Name?.ToString()
                                          ?? ctx.Agent?.Name?.ToString()
                                          ?? "对方";
                            float gap = npcLevel - myLevel;
                            string msg = $"{skill.Name}检定失败: 你的{skill.Name}({myLevel:F0}) vs {npcName}({npcLevel:F0})，差{gap:F0}点，成功率仅{roll.Chance:P0}";
                            MBInformationManager.AddQuickInformation(new TextObject(msg));
                        }
                        catch { }

                        intent.OnFail(ctx);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] ExecuteIntentAction({intentName}) error: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接注入 DialogueInjectScript 对象（不经过 JSON 文件）。
        /// 根据 script.SkipVanillaOpening 选择注入路径：
        ///   false（默认）: Gateway 模式 — 在 hero_main_options 挂 PlayerLine，保留原版开场白
        ///   true:          直挂模式 — NPC 台词挂在 start token（优先级 200），跳过原版开场白
        /// </summary>
        public static string InjectScript(DialogueInjectScript script, string debugLabel = null)
        {
            if (script == null || script.Nodes == null || script.Nodes.Count == 0)
                return "Empty script";

            var fileTag = debugLabel ?? $"dyn_{_injectedOwners.Count}";

            if (script.SkipVanillaOpening)
                InjectScriptNoOpening(script, fileTag);
            else
                InjectScriptGateway(script, fileTag);

            return $"Injected dynamic script [{fileTag}] ({script.Nodes.Count} nodes)";
        }

        /// <summary>
        /// 直挂注入模式：NPC 台词直接挂在 start token（优先级 200），不经过 gateway PlayerLine。
        /// 适用场景：NPC 主动锁定玩家 — 警戒质问、战斗认输、对峙等。
        /// </summary>
        private static void InjectScriptNoOpening(DialogueInjectScript script, string fileTag)
        {
            fileTag = $"{fileTag}_v{_injectionCounter++}";

            var cm = Campaign.Current.ConversationManager;
            var owner = new InjectOwner { FileName = fileTag, BaseLabel = fileTag };
            _injectedOwners.Add(owner);
            _tokenCounter = 0;

            string startToken = !string.IsNullOrEmpty(script.InjectAtToken)
                ? script.InjectAtToken : "start";

            var entryNode = script.Nodes.FirstOrDefault(t => t.Id == script.EntryNode);
            if (entryNode == null)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptNoOpening 异常: entry node '{script.EntryNode}' not found");
                return;
            }

            try
            {
                // NPC 台词直接挂在 startToken，优先级 200 碾压原版开场白
                string afterNpcLine = NextToken(fileTag);
                AddNodeNpcLine(cm, $"inj_open_{entryNode.Id}", startToken, afterNpcLine, entryNode, 200);
                DebugLogger.Log($"[DialogueInjector] 跳过原版Opening: NPC line at '{startToken}' → '{afterNpcLine}' | priority=200 | owner={owner.FileName}");

                // 注册入口 node 的玩家选项
                RegisterNodeTransitions(cm, entryNode, afterNpcLine, fileTag, owner);

                // 注册剩余 node
                foreach (var node in script.Nodes)
                {
                    if (node.Id == script.EntryNode) continue;

                    string nodeEntryToken = NodeToken(fileTag, node.Id);

                    if (node.Transitions == null || node.Transitions.Count == 0)
                    {
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, "close_window", node);
                    }
                    else
                    {
                        string nodeAfterNpc = NextToken(fileTag);
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, nodeAfterNpc, node);
                        RegisterNodeTransitions(cm, node, nodeAfterNpc, fileTag, owner);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptNoOpening error: {ex.Message}");
            }
        }

        /// <summary>
        /// 为一个 node 注册全部玩家选项（挂在 afterNpcLine token 上）。
        /// 按 CheckType 分两条路径：None → 直连目标 Node；SkillCheck → 条件桥接路由。
        /// </summary>
        private static void RegisterNodeTransitions(
            ConversationManager cm, DialogueNode node,
            string afterNpcLine, string fileTag, InjectOwner owner)
        {
            if (node.Transitions == null || node.Transitions.Count == 0) return;

            foreach (var transition in node.Transitions)
            {
                RegisterTransition(cm, node, transition, afterNpcLine, fileTag, owner);
            }
        }

        /// <summary>
        /// 注册单个 Transition。返回注册的行数。
        /// </summary>
        private static int RegisterTransition(
            ConversationManager cm, DialogueNode node, DialogueTransition transition,
            string afterNpcLine, string fileTag, InjectOwner owner)
        {
            if (transition.CheckType == TransitionCheckType.SkillCheck)
            {
                return RegisterSkillCheckTransition(cm, node, transition, afterNpcLine, fileTag, owner);
            }
            else
            {
                return RegisterDirectTransition(cm, node, transition, afterNpcLine, fileTag, owner);
            }
        }

        /// <summary>CheckType.None：直连目标 Node（或 close_window）。</summary>
        private static int RegisterDirectTransition(
            ConversationManager cm, DialogueNode node, DialogueTransition transition,
            string afterNpcLine, string fileTag, InjectOwner owner)
        {
            string afterPlayer = !string.IsNullOrEmpty(transition.NextNodeOnSuccess)
                ? NodeToken(fileTag, transition.NextNodeOnSuccess)
                : "close_window";

            var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
            pdf.AddPlayerLine($"inj_opt_{node.Id}", afterNpcLine, afterPlayer,
                ResolveTransitionText(transition), BuildTransitionCondition(transition),
                () => ExecuteAction(transition), owner, 125);
            cm.AddDialogFlow(pdf, owner);
            return 1;
        }

        /// <summary>CheckType.SkillCheck：在 consequence 中直接覆写 ActiveToken 跳转，无需静默路由行。</summary>
        private static int RegisterSkillCheckTransition(
            ConversationManager cm, DialogueNode node, DialogueTransition transition,
            string afterNpcLine, string fileTag, InjectOwner owner)
        {
            string afterPlayer = NextToken(fileTag);
            string capturedKey = afterPlayer;
            transition.ResultKey = capturedKey;

            // 玩家选项 — consequence 中执行检定 + 直接覆写 ActiveToken 跳到目标 node
            var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
            pdf.AddPlayerLine($"inj_opt_{node.Id}", afterNpcLine, afterPlayer,
                ResolveTransitionText(transition), BuildTransitionCondition(transition),
                () =>
                {
                    ExecuteAction(transition);

                    // ★ 直接在 consequence 中覆写 ActiveToken，跳过静默路由行。
                    // ProcessSentence 先设 ActiveToken = outputToken，再跑 consequence，
                    // 所以这里覆写后，引擎随即从目标 token 找下一条 NPC 台词，不会出现空白。
                    string dest;
                    if (_intentResults.TryGetValue(capturedKey, out bool passed))
                    {
                        dest = passed
                            ? transition.NextNodeOnSuccess
                            : (!string.IsNullOrEmpty(transition.NextNodeOnFail)
                                ? transition.NextNodeOnFail
                                : transition.NextNodeOnSuccess); // fallback：未配 NextNodeOnFail 则走成功线
                    }
                    else
                    {
                        // 安全网：Intent 被 Disabled，_intentResults 无此 key → 关窗
                        dest = null;
                    }

                    if (!string.IsNullOrEmpty(dest))
                        cm.ActiveToken = cm.GetStateIndex(NodeToken(fileTag, dest));
                    else
                        cm.ActiveToken = cm.GetStateIndex("close_window");
                }, owner, 125);
            cm.AddDialogFlow(pdf, owner);

            // 不再需要静默路由行 — ActiveToken 覆写已替代路由职责。
            // 之前这里注册了 3 条 AddDialogLineMultiAgent(empty text) 作为成功/失败/安全网路由，
            // 空 TextObject 被引擎当作正常 NPC 对话回合渲染，导致玩家看到空白对话框。
            // 铁律 6（KCD2 品质）：禁止出现空白对话框。

            return 1; // 仅 PlayerLine
        }

        /// <summary>
        /// 注册一个 Node 的 NPC 台词行。支持 LazyNpcLine 惰性求值。
        /// </summary>
        private static void AddNodeNpcLine(ConversationManager cm, string id,
            string inputToken, string outputToken, DialogueNode node, int priority = 125)
        {
            if (node.LazyNpcLine != null)
            {
                var textObj = new TextObject("…");
                cm.AddDialogLineMultiAgent(id, inputToken, outputToken, textObj,
                    () =>
                    {
                        textObj.Value = node.LazyNpcLine();
                        // 清除内部缓存，确保 GetCachedTokens() 从新 Value 重新 tokenize
                        var tokensField = typeof(TextObject).GetField("cachedTokens",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var langField = typeof(TextObject).GetField("cachedTextLanguageId",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        tokensField?.SetValue(textObj, null);
                        langField?.SetValue(textObj, -1);
                        return true;
                    },
                    null, 0, -1, priority);
            }
            else
            {
                cm.AddDialogLineMultiAgent(id, inputToken, outputToken,
                    new TextObject(node.NpcLine ?? ""),
                    () => true, null, 0, -1, priority);
            }
        }

        /// <summary>
        /// Gateway 注入模式：在 hero_main_options 挂 PlayerLine 入口，保留原版开场白。
        /// 适用场景：NPC 等玩家主动来找 — 打听消息、接任务、路人闲聊等。
        /// </summary>
        private static void InjectScriptGateway(DialogueInjectScript script, string fileTag)
        {
            string baseLabel = fileTag;
            // 每次注入用唯一版本号，防止旧线未清理导致的 token 碰撞
            fileTag = $"{fileTag}_v{_injectionCounter++}";

            var cm = Campaign.Current.ConversationManager;
            var owner = new InjectOwner { FileName = fileTag, BaseLabel = baseLabel };
            _injectedOwners.Add(owner);
            _tokenCounter = 0;

            string startToken = !string.IsNullOrEmpty(script.InjectAtToken)
                ? script.InjectAtToken : "hero_main_options";

            try
            {
                // 网关：入口选项
                string entryNodeToken = NodeToken(fileTag, script.EntryNode);
                string entryText = !string.IsNullOrEmpty(script.EntryOption)
                    ? script.EntryOption : $"「{fileTag}」";
                var gateDf = DialogFlow.CreateDialogFlow(startToken, 125);
                gateDf.AddPlayerLine("inj_gateway", startToken, entryNodeToken,
                    entryText, () => true, null, owner, 125);
                cm.AddDialogFlow(gateDf, owner);
                DebugLogger.Log($"[DialogueInjector] Gateway: added PlayerLine at '{startToken}' → '{entryNodeToken}' | priority=125 | owner={owner.FileName}");

                foreach (var node in script.Nodes)
                {
                    string nodeEntryToken = NodeToken(fileTag, node.Id);

                    if (node.Transitions == null || node.Transitions.Count == 0)
                    {
                        // Terminal node: NPC 说话 → 关窗
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, "close_window", node);
                    }
                    else
                    {
                        string afterNpcLine = NextToken(fileTag);
                        AddNodeNpcLine(cm, $"inj_npc_{node.Id}", nodeEntryToken, afterNpcLine, node);

                        RegisterNodeTransitions(cm, node, afterNpcLine, fileTag, owner);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptGateway error: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析选项显示文本：JSON PlayerLine 优先 → INTENT:xxx 的 DisplayName 兜底 → "…"
        /// </summary>
        private static string ResolveTransitionText(DialogueTransition transition)
        {
            if (!string.IsNullOrEmpty(transition.PlayerLine))
                return transition.PlayerLine;

            if (!string.IsNullOrEmpty(transition.Action) && transition.Action.StartsWith("INTENT:"))
            {
                string intentName = transition.Action.Substring("INTENT:".Length);
                var intent = LivingWorldNpcs.IntentRegistry.FindByName(intentName);
                if (intent != null && !string.IsNullOrEmpty(intent.DisplayName))
                    return intent.DisplayName;
            }

            return "…";
        }

        /// <summary>
        /// 构建 AddPlayerLine 的条件委托：对 INTENT:xxx 选项，显示时跑 Evaluate 决定可见性。
        /// Hidden → 不显示；Disabled/Enabled → 显示（Disabled 的反馈在 ExecuteIntentAction 中给）。
        /// 非 INTENT 选项（NONE 等）→ 始终显示。
        /// </summary>
        private static ConversationSentence.OnConditionDelegate BuildTransitionCondition(DialogueTransition transition)
        {
            if (string.IsNullOrEmpty(transition.Action) || !transition.Action.StartsWith("INTENT:"))
                return () => true;

            string intentName = transition.Action.Substring("INTENT:".Length);
            var intent = LivingWorldNpcs.IntentRegistry.FindByName(intentName);
            if (intent == null) return () => true;

            return () =>
            {
                try
                {
                    var npc = Hero.OneToOneConversationHero;
                    // 模板 NPC（HeroObject==null）的回退路径：
                    // OneToOneConversationHero 为 null，但 Settlement.CurrentSettlement
                    // 在 Mission 场景中会被正确设置。
                    var settlement = npc?.CurrentSettlement ?? Settlement.CurrentSettlement;
                    var evt = settlement != null ? WorldEventStore.FindActive(settlement.StringId) : null;

                    // 🆕 从 ConversationManager 获取当前对话的 Agent（与 ExecuteIntentAction 同模式）
                    Agent partnerAgent = null;
                    try
                    {
                        var cm = Campaign.Current?.ConversationManager;
                        if (cm != null)
                            partnerAgent = cm.OneToOneConversationAgent as Agent;
                    }
                    catch { }

                    var ctx = new IntentContext(partnerAgent, speaker: npc, worldEvent: evt, actionParam: transition.ActionParam);

                    var eligibility = intent.Evaluate(ctx);
                    // 对话中只显示完全可用的选项，Disabled 也隐藏。
                    // Disabled 选项被点击后 ExecuteIntentAction 无法写入 _intentResults，
                    // 会导致条件路由行全部不匹配 → 对话死锁。
                    return eligibility.State == EligState.Enabled;
                }
                catch { return true; } // 出错时兜底显示
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // JSON 模型类型（public — LLM 集成时外部需要引用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Transition 是否有检定分支。</summary>
        public enum TransitionCheckType
        {
            /// <summary>无检定。路由走 NextNodeOnSuccess。</summary>
            None,
            /// <summary>单次技能检定。Intent.Goal != null。路由走 NextNodeOnSuccess / NextNodeOnFail。</summary>
            SkillCheck,
        }

        public class DialogueInjectScript
        {
            /// <summary>
            /// 对话挂载点 — 你的对话从 NPC 对话树的哪个 token 节点插入。
            /// null (默认): 自动检测当前对话状态，不在对话中时回退到 "hero_main_options"。
            /// "hero_main_options": 跟任何 NPC 说话都会出现（最泛用，适合测试）。
            /// "issue_offer": 只对有未接任务的 NPC 出现。
            /// "quest_offer": 只对有进行中任务的 NPC 出现。
            /// </summary>
            public string InjectAtToken = null;
            /// <summary>入口选项文本 — 挂在 NPC 主菜单上，玩家点这个选项进入对话图。缺省用文件名。</summary>
            public string EntryOption = null;
            /// <summary>对话从哪个 node 开始（对应 DialogueNode.Id）。默认 "injectedStart"。 </summary>
            public string EntryNode = "injectedStart";
            /// <summary>是否跳过原版开场白。true = NPC 台词直接挂在 start token（优先级 200），原版问候不播放。</summary>
            public bool SkipVanillaOpening = false;
            public List<DialogueNode> Nodes;
        }

        public class DialogueNode
        {
            /// <summary>唯一标识。其他 node 的 transition 通过 NextNodeOnSuccess/OnFail 引用此 ID 来跳转。</summary>
            public string Id = "injectedStart";
            /// <summary>NPC 的台词。所有 NPC 说话的唯一入口。</summary>
            public string NpcLine;
            /// <summary>延迟求值：引擎展示此行前才调 delegate 拿最新文本。设置后覆盖 NpcLine。</summary>
            [Newtonsoft.Json.JsonIgnore]
            public Func<string> LazyNpcLine;
            /// <summary>玩家可选的回应。空列表 [] = terminal（NPC 说完直接关窗）。null = 未初始化（非法）。</summary>
            public List<DialogueTransition> Transitions;
        }

        public class DialogueTransition
        {
            /// <summary>玩家选项的显示文本。</summary>
            public string PlayerLine;
            /// <summary>此选项是否有技能检定分支。</summary>
            public TransitionCheckType CheckType = TransitionCheckType.None;
            /// <summary>动作标识。NONE / INTENT:xxx。</summary>
            public string Action = "NONE";
            /// <summary>字符串参数。INTENT:xxx 执行时注入 IntentContext.ActionParam。
            /// 对于系统 Intent（IncreaseRelation 等），承载数值的字符串表示（如 "5"、"100"）。</summary>
            public string ActionParam = null;
            /// <summary>成功（或无检定）后的目标 Node Id。"" 或 null = 关闭对话。</summary>
            public string NextNodeOnSuccess;
            /// <summary>检定失败后的目标 Node Id。仅 CheckType.SkillCheck 时有效。
            /// 不设则 fallback 到 NextNodeOnSuccess。</summary>
            public string NextNodeOnFail;
            /// <summary>[内部] 注入时分配的 afterPlayer token，用作检定结果回写 key。外部不需要设置。</summary>
            internal string ResultKey = null;
        }
    }
}
