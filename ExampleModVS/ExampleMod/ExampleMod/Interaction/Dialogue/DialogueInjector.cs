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

            if (script == null || script.Turns == null || script.Turns.Count == 0)
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

            // 4. 注入：turn 的 Id 直接用作 ConversationManager token，加文件前缀防跨文件碰撞。
            _tokenCounter = 0;
            int nodeCount = 0;

            try
            {
                // —— 网关 ———
                string entryTurnToken = TurnToken(fileTag, script.EntryTurn);
                string entryText = !string.IsNullOrEmpty(script.EntryOption)
                    ? script.EntryOption
                    : $"「{Path.GetFileNameWithoutExtension(jsonPath)}」";
                var gateDf = DialogFlow.CreateDialogFlow(startToken, 125);
                gateDf.AddPlayerLine(
                    "inj_gateway", startToken, entryTurnToken,
                    entryText,
                    () => true, null, owner, 125);
                cm.AddDialogFlow(gateDf, owner);
                nodeCount++;

                // —— 逐 turn 注册 ——
                foreach (var turn in script.Turns)
                {
                    if (turn.Options == null || turn.Options.Count == 0) continue;

                    string turnEntryToken = TurnToken(fileTag, turn.Id);
                    string afterNpcLine = NextToken(fileTag);

                    cm.AddDialogLineMultiAgent(
                        $"inj_npc_{turn.Id}", turnEntryToken, afterNpcLine,
                        new TextObject(turn.NpcLine ?? ""),
                        () => true, null,
                        turn.SpeakerIndex, -1, 125);
                    nodeCount++;

                    foreach (var opt in turn.Options)
                    {
                        string afterPlayer = NextToken(fileTag);
                        opt.ResultKey = afterPlayer;

                        // NPC 回应的出口 = NextTurn 对应的入口 token，null → 关闭
                        string afterNpcResponse = !string.IsNullOrEmpty(opt.NextTurn)
                            ? TurnToken(fileTag, opt.NextTurn)
                            : "close_window";

                        // 玩家选项
                        var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
                        pdf.AddPlayerLine(
                            $"inj_opt_{Guid.NewGuid():N}", afterNpcLine, afterPlayer,
                            ResolveOptionText(opt),
                            () => true,
                            () => ExecuteAction(opt),
                            owner, 125);
                        cm.AddDialogFlow(pdf, owner);
                        nodeCount++;

                        // ── NPC 回应：支持成败双线 + 兜底直连 ──
                        nodeCount += RegisterNpcResponseLines(cm, turn, opt, afterPlayer, afterNpcResponse, fileTag);
                    }
                }

                string atTokenDesc = startToken == "hero_main_options"
                    ? "hero_main_options (will appear when talking to any NPC)"
                    : $"token '{startToken}' (in active conversation)";

                return $"SUCCESS: Injected '{Path.GetFileName(jsonPath)}'\n" +
                       $"  Turns: {script.Turns.Count}, Nodes: {nodeCount}\n" +
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

        // ═══════════════════════════════════════════════════════════════
        // 内部状态
        // ═══════════════════════════════════════════════════════════════

        private static int _tokenCounter = 0;
        private static int _injectionCounter = 0;
        private static readonly List<InjectOwner> _injectedOwners = new List<InjectOwner>();

        /// <summary>检定结果回写表：afterPlayer token → 是否通过。InjectScriptInternal 注册双线 NPC 回应时查此表。</summary>
        private static readonly Dictionary<string, bool> _intentResults = new Dictionary<string, bool>();

        private class InjectOwner { public string FileName; public string BaseLabel; }

        private static string NextToken(string fileTag) => $"lwnpc_{fileTag}_atk_{_tokenCounter++}";

        /// <summary>Turn 的 Id → ConversationManager token。加文件前缀，不同 JSON 的同名 Id 互不冲突。</summary>
        private static string TurnToken(string fileTag, string turnId) => $"lwnpc_{fileTag}_{turnId}";

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

        private static void ExecuteAction(DialogueInjectOption opt)
        {
            if (string.IsNullOrEmpty(opt.Action) || opt.Action == "NONE")
                return;

            try
            {
                var oneToOne = Campaign.Current.ConversationManager.OneToOneConversationHero;

                switch (opt.Action.ToUpperInvariant())
                {
                    case "INCREASE_RELATION":
                        if (oneToOne is Hero npc)
                            ChangeRelationAction.ApplyPlayerRelation(npc,
                                opt.ActionValue != 0 ? opt.ActionValue : 5);
                        break;
                    case "DECREASE_RELATION":
                        if (oneToOne is Hero npc2)
                            ChangeRelationAction.ApplyPlayerRelation(npc2,
                                opt.ActionValue != 0 ? -opt.ActionValue : -5);
                        break;
                    case "GIVE_GOLD":
                        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero,
                            opt.ActionValue > 0 ? opt.ActionValue : 100);
                        break;
                    case "TAKE_GOLD":
                        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null,
                            opt.ActionValue > 0 ? opt.ActionValue : 100);
                        break;
                    case "CLOSE_DIALOG":
                        break;
                    default:
                        // ── INTENT:xxx 委托 ──
                        if (opt.Action.StartsWith("INTENT:", StringComparison.OrdinalIgnoreCase))
                        {
                            string intentSpec = opt.Action.Substring(7);
                            ExecuteIntentAction(intentSpec, oneToOne, opt.ActionParam, opt.ResultKey);
                        }
                        else
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage($"[DialogueInjector] Unknown action: {opt.Action}"));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"[DialogueInjector] Action '{opt.Action}' failed: {ex.Message}"));
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

                // 构建上下文：Agent 可能为 null（大地图无 Mission 时），降级处理
                var ctx = IntentContext.Build(partnerAgent, null);

                // 当 Agent 不可用时（如 CampaignMapConversation），从 npc Hero 回填上下文
                if (ctx.Agent == null && npc != null)
                {
                    ctx.Hero = npc;
                }

                // 注入犯罪事件上下文
                var settlement = npc?.CurrentSettlement;
                if (settlement != null)
                {
                    ctx.ActiveEvent = WorldEventStore.FindActive(settlement.StringId);
                }

                // 注入 ActionParam（JSON → IntentContext，各 Intent 自行解析）
                if (!string.IsNullOrEmpty(actionParam))
                    ctx.ActionParam = actionParam;

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
                        intent.OnSuccess(ctx);
                    else
                        intent.OnFail(ctx);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] ExecuteIntentAction({intentName}) error: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接注入 DialogueInjectScript 对象（不经过 JSON 文件）。
        /// 与 InjectFromJson 共享同一套 ConversationManager 注册逻辑。
        /// </summary>
        public static string InjectScript(DialogueInjectScript script, string debugLabel = null)
        {
            if (script == null || script.Turns == null || script.Turns.Count == 0)
                return "Empty script";

            var fileTag = debugLabel ?? $"dyn_{_injectedOwners.Count}";
            InjectScriptInternal(script, fileTag);
            return $"Injected dynamic script [{fileTag}] ({script.Turns.Count} turns)";
        }

        /// <summary>
        /// "替换开场白"注入模式：不创建 gateway PlayerLine，直接把第一个 turn 的
        /// NPC 台词挂在 InjectAtToken（默认 "start"）上，优先级 200 碾压原版开场白。
        ///
        /// 适用场景：NPC 主动找上门的警戒质问 — 不应该先说原版友好问候再转折，
        /// 而应该一开口就是我们设计的台词。
        ///
        /// 与 InjectScript 的区别：
        ///   - InjectScript: InjectAtToken → [gateway PlayerLine] → [NPC line] → [options]
        ///   - InjectScriptAsOpening: InjectAtToken → [NPC line 优先级200] → [options]
        /// </summary>
        public static string InjectScriptAsOpening(DialogueInjectScript script, string debugLabel = null)
        {
            if (script == null || script.Turns == null || script.Turns.Count == 0)
                return "Empty script";

            var fileTag = debugLabel ?? $"dyn_{_injectedOwners.Count}";
            fileTag = $"{fileTag}_v{_injectionCounter++}";

            var cm = Campaign.Current.ConversationManager;
            var owner = new InjectOwner { FileName = fileTag, BaseLabel = fileTag };
            _injectedOwners.Add(owner);
            _tokenCounter = 0;

            string startToken = !string.IsNullOrEmpty(script.InjectAtToken)
                ? script.InjectAtToken : "start";

            // 找到入口 turn
            var entryTurn = script.Turns.FirstOrDefault(t => t.Id == script.EntryTurn);
            if (entryTurn == null)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptAsOpening: entry turn '{script.EntryTurn}' not found");
                return $"Error: entry turn '{script.EntryTurn}' not found";
            }

            try
            {
                // ── 第一步：入口 turn 的 NPC 台词直接挂在 startToken ──
                string afterNpcLine = NextToken(fileTag);
                cm.AddDialogLineMultiAgent(
                    $"inj_open_{entryTurn.Id}", startToken, afterNpcLine,
                    new TaleWorlds.Localization.TextObject(entryTurn.NpcLine ?? ""),
                    () => true, null, entryTurn.SpeakerIndex, -1, 200); // priority 200 > 原版 ~100
                DebugLogger.Log($"[DialogueInjector] Opening: NPC line at '{startToken}' → '{afterNpcLine}' | priority=200 | owner={owner.FileName}");

                // ── 第二步：注册入口 turn 的玩家选项（挂在 afterNpcLine）──
                RegisterTurnOptions(cm, entryTurn, afterNpcLine, fileTag, owner);

                // ── 第三步：逐 turn 注册剩余回合 ──
                foreach (var turn in script.Turns)
                {
                    if (turn.Id == script.EntryTurn) continue; // 入口 turn 已处理
                    if (turn.Options == null || turn.Options.Count == 0) continue;

                    string turnEntryToken = TurnToken(fileTag, turn.Id);
                    string turnAfterNpc = NextToken(fileTag);
                    cm.AddDialogLineMultiAgent(
                        $"inj_npc_{turn.Id}", turnEntryToken, turnAfterNpc,
                        new TaleWorlds.Localization.TextObject(turn.NpcLine ?? ""),
                        () => true, null, turn.SpeakerIndex, -1, 125);

                    RegisterTurnOptions(cm, turn, turnAfterNpc, fileTag, owner);
                }

                int turnCount = 1 + script.Turns.Count(t => t.Id != script.EntryTurn && t.Options != null && t.Options.Count > 0);
                return $"SUCCESS (opening mode): '{fileTag}' → {turnCount} turns at '{startToken}'";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptAsOpening error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// 为一个 turn 注册全部玩家选项（挂在 afterNpcLine token 上）。
        /// 抽取公共逻辑，供 InjectScriptInternal 和 InjectScriptAsOpening 共用。
        /// </summary>
        private static void RegisterTurnOptions(
            ConversationManager cm, DialogueInjectTurn turn,
            string afterNpcLine, string fileTag, InjectOwner owner)
        {
            if (turn.Options == null || turn.Options.Count == 0) return;

            foreach (var opt in turn.Options)
            {
                string afterNpcResponse = !string.IsNullOrEmpty(opt.NextTurn)
                    ? TurnToken(fileTag, opt.NextTurn) : "close_window";

                bool isCloseWindow = string.IsNullOrEmpty(opt.NextTurn) || opt.NextTurn == "close_window";
                bool hasNpcResponse = !string.IsNullOrEmpty(opt.NpcResponse)
                                   || !string.IsNullOrEmpty(opt.NpcResponseOnSuccess)
                                   || !string.IsNullOrEmpty(opt.NpcResponseOnFail);
                bool needsBridge = hasNpcResponse || isCloseWindow;

                string afterPlayer;
                if (needsBridge)
                {
                    afterPlayer = NextToken(fileTag);
                    opt.ResultKey = afterPlayer;
                }
                else
                {
                    afterPlayer = afterNpcResponse;
                    opt.ResultKey = null;
                }

                var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
                pdf.AddPlayerLine($"inj_opt_{turn.Id}", afterNpcLine, afterPlayer,
                    ResolveOptionText(opt), BuildOptionCondition(opt), () => ExecuteAction(opt), owner, 125);
                cm.AddDialogFlow(pdf, owner);

                if (needsBridge)
                {
                    RegisterNpcResponseLines(cm, turn, opt, afterPlayer, afterNpcResponse, fileTag);
                }
            }
        }

        // ⚠ 内部方法，与 InjectFromJson 的后半段逻辑完全相同
        private static void InjectScriptInternal(DialogueInjectScript script, string fileTag)
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
                string entryTurnToken = TurnToken(fileTag, script.EntryTurn);
                string entryText = !string.IsNullOrEmpty(script.EntryOption)
                    ? script.EntryOption : $"「{fileTag}」";
                var gateDf = DialogFlow.CreateDialogFlow(startToken, 125);
                gateDf.AddPlayerLine("inj_gateway", startToken, entryTurnToken,
                    entryText, () => true, null, owner, 125);
                cm.AddDialogFlow(gateDf, owner);
                DebugLogger.Log($"[DialogueInjector] Gateway: added PlayerLine at '{startToken}' → '{entryTurnToken}' | priority=125 | owner={owner.FileName}");

                foreach (var turn in script.Turns)
                {
                    if (turn.Options == null || turn.Options.Count == 0) continue;

                    string turnEntryToken = TurnToken(fileTag, turn.Id);
                    string afterNpcLine = NextToken(fileTag);
                    cm.AddDialogLineMultiAgent(
                        $"inj_npc_{turn.Id}", turnEntryToken, afterNpcLine,
                        new TaleWorlds.Localization.TextObject(turn.NpcLine ?? ""),
                        () => true, null, turn.SpeakerIndex, -1, 125);

                    RegisterTurnOptions(cm, turn, afterNpcLine, fileTag, owner);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptInternal error: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析选项显示文本：JSON PlayerLine 优先 → INTENT:xxx 的 DisplayName 兜底 → "…"
        /// </summary>
        private static string ResolveOptionText(DialogueInjectOption opt)
        {
            if (!string.IsNullOrEmpty(opt.PlayerLine))
                return opt.PlayerLine;

            if (!string.IsNullOrEmpty(opt.Action) && opt.Action.StartsWith("INTENT:"))
            {
                string intentName = opt.Action.Substring("INTENT:".Length);
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
        private static ConversationSentence.OnConditionDelegate BuildOptionCondition(DialogueInjectOption opt)
        {
            if (string.IsNullOrEmpty(opt.Action) || !opt.Action.StartsWith("INTENT:"))
                return () => true;

            string intentName = opt.Action.Substring("INTENT:".Length);
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
                    var ctx = new LivingWorldNpcs.IntentContext
                    {
                        Hero = npc,
                        Player = Hero.MainHero,
                        ActiveEvent = evt,
                        IsInMission = TaleWorlds.MountAndBlade.Mission.Current != null,
                        ActionParam = opt.ActionParam
                    };
                    var eligibility = intent.Evaluate(ctx);
                    // 对话中只显示完全可用的选项，Disabled 也隐藏。
                    // Disabled 选项被点击后 ExecuteIntentAction 无法写入 _intentResults，
                    // 会导致条件 NPC 回应行全部不匹配 → 对话死锁。见 RegisterNpcResponseLines。
                    return eligibility.State == EligState.Enabled;
                }
                catch { return true; } // 出错时兜底显示
            };
        }

        /// <summary>
        /// 为单个选项注册 NPC 回应行。优先级：成败双线 > 静态 NpcResponse > 兜底直连。
        /// 返回注册的行数（用于 nodeCount）。
        /// </summary>
        private static int RegisterNpcResponseLines(
            ConversationManager cm, DialogueInjectTurn turn, DialogueInjectOption opt,
            string afterPlayer, string afterNpcResponse, string fileTag)
        {
            int count = 0;
            bool hasConditional = !string.IsNullOrEmpty(opt.NpcResponseOnSuccess)
                               || !string.IsNullOrEmpty(opt.NpcResponseOnFail);
            if (hasConditional)
            {
                string capturedKey = afterPlayer;
                // 成功线
                if (!string.IsNullOrEmpty(opt.NpcResponseOnSuccess))
                {
                    cm.AddDialogLineMultiAgent(
                        $"inj_resp_succ_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                        new TextObject(opt.NpcResponseOnSuccess),
                        () => _intentResults.TryGetValue(capturedKey, out var r) && r,
                        null, turn.SpeakerIndex, -1, 125);
                    count++;
                }
                // 失败线
                if (!string.IsNullOrEmpty(opt.NpcResponseOnFail))
                {
                    cm.AddDialogLineMultiAgent(
                        $"inj_resp_fail_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                        new TextObject(opt.NpcResponseOnFail),
                        () => _intentResults.TryGetValue(capturedKey, out var r) && !r,
                        null, turn.SpeakerIndex, -1, 125);
                    count++;
                }
                // 兜底：只设了一边（如只设成功线），另一边需直连 → 防死胡同
                if (string.IsNullOrEmpty(opt.NpcResponseOnSuccess) || string.IsNullOrEmpty(opt.NpcResponseOnFail))
                {
                    cm.AddDialogLineMultiAgent(
                        $"inj_silent_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                        new TextObject("…"),
                        () =>
                        {
                            if (!_intentResults.TryGetValue(capturedKey, out var r)) return true;
                            bool hasSucc = !string.IsNullOrEmpty(opt.NpcResponseOnSuccess);
                            bool hasFail = !string.IsNullOrEmpty(opt.NpcResponseOnFail);
                            return (r && !hasSucc) || (!r && !hasFail);
                        },
                        null, turn.SpeakerIndex, -1, 125);
                    count++;
                }
                // 安全网：双线都设了但 intent 被禁用 / 未执行（_intentResults 无 key）→ 防死锁
                if (!string.IsNullOrEmpty(opt.NpcResponseOnSuccess) && !string.IsNullOrEmpty(opt.NpcResponseOnFail))
                {
                    cm.AddDialogLineMultiAgent(
                        $"inj_silent_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                        new TextObject("…"),
                        () => !_intentResults.ContainsKey(capturedKey),
                        null, turn.SpeakerIndex, -1, 125);
                    count++;
                }
            }
            else if (opt.LazyNpcResponse != null)
            {
                // 延迟求值：condition 回调在引擎展示 NPC 行前触发 → 更新 Value → GetCachedTokens() 拿到最新文本
                var textObj = new TextObject("…");
                cm.AddDialogLineMultiAgent(
                    $"inj_lazy_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                    textObj,
                    () =>
                    {
                        textObj.Value = opt.LazyNpcResponse();
                        // 清除内部缓存，确保 GetCachedTokens() 从新 Value 重新 tokenize
                        var tokensField = typeof(TextObject).GetField("cachedTokens",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var langField = typeof(TextObject).GetField("cachedTextLanguageId",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        tokensField?.SetValue(textObj, null);
                        langField?.SetValue(textObj, -1);
                        return true;
                    },
                    null, turn.SpeakerIndex, -1, 125);
                count++;
            }
            else if (!string.IsNullOrEmpty(opt.NpcResponse))
            {
                cm.AddDialogLineMultiAgent(
                    $"inj_resp_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                    new TextObject(opt.NpcResponse),
                    () => true, null, turn.SpeakerIndex, -1, 125);
                count++;
            }
            else
            {
                // 兜底直连：防死胡同
                cm.AddDialogLineMultiAgent(
                    $"inj_silent_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                    new TextObject("…"),
                    () => true, null, turn.SpeakerIndex, -1, 125);
                count++;
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // JSON 模型类型（public — LLM 集成时外部需要引用）
        // ═══════════════════════════════════════════════════════════════

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
            /// <summary>对话从哪个 turn 开始（对应 DialogueInjectTurn.Id）。默认 "start"。 </summary>
            public string EntryTurn = "start";
            public List<DialogueInjectTurn> Turns;
        }

        public class DialogueInjectTurn
        {
            /// <summary>唯一标识。其他 turn 的选项通过 NextTurn 引用此 ID 来跳转。</summary>
            public string Id = "start";
            public int SpeakerIndex = 0;
            public string NpcLine;
            public List<DialogueInjectOption> Options;
        }

        public class DialogueInjectOption
        {
            public string PlayerLine;
            public string NpcResponse;
            /// <summary>检定成功时 NPC 的回应（与 NpcResponseOnFail 配对使用，覆盖 NpcResponse）。</summary>
            public string NpcResponseOnSuccess = null;
            /// <summary>检定失败时 NPC 的回应（与 NpcResponseOnSuccess 配对使用，覆盖 NpcResponse）。</summary>
            public string NpcResponseOnFail = null;
            /// <summary>运行时延迟求值：引擎展示此行前才调 delegate 拿最新文本。设置后覆盖 NpcResponse。</summary>
            [Newtonsoft.Json.JsonIgnore]
            public Func<string> LazyNpcResponse = null;
            /// <summary>选了此选项后跳转到哪个 turn。null = 关闭对话。</summary>
            public string NextTurn = null;
            public string Action = "NONE";
            public int ActionValue = 0;
            /// <summary>字符串参数（栽赃目标 ID 等）。INTENT:xxx 执行时注入 IntentContext。</summary>
            public string ActionParam = null;
            /// <summary>[内部] 注入时分配的 afterPlayer token，用作检定结果回写 key。外部不需要设置。</summary>
            internal string ResultKey = null;
        }
    }
}
