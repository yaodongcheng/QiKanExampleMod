using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LivingWorldNpcs.Story;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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

            // 3. 创建 owner 哨兵（用于 cleanup），同时用作 token 命名空间隔离。
            string fileTag = Path.GetFileNameWithoutExtension(jsonPath);
            var owner = new InjectOwner { FileName = Path.GetFileName(jsonPath) };
            _injectedOwners.Add(owner);

            // 4. 注入：turn 的 Id 直接用作 ConversationManager token，加文件前缀防跨文件碰撞。
            //    例: test_talk.json 中 Id="more_detail" → token="lwnpc_test_talk_more_detail"
            _tokenCounter = 0;
            int nodeCount = 0;

            try
            {
                // —— 网关：在注入点加一个玩家可点的选项，作为对话入口 ——
                //    hero_main_options 是玩家选项菜单的 token，不能直接塞 NPC 台词。
                //    必须有一个玩家选项挂在这里，点了之后才进入我们的对话图。
                //    选项文本从 JSON 的 EntryOption 字段取，缺省用文件名。
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

                    // 入口 turn 从自己的 entryToken 开始（被网关选项激活）；
                    // 被引用的 turn 从自己的 entryToken 开始（被上一轮的 NPC 回应激活）
                    string turnEntryToken = TurnToken(fileTag, turn.Id);
                    string afterNpcLine = NextToken();

                    cm.AddDialogLineMultiAgent(
                        $"inj_npc_{turn.Id}", turnEntryToken, afterNpcLine,
                        new TextObject(turn.NpcLine ?? ""),
                        () => true, null,
                        turn.SpeakerIndex, -1, 125);
                    nodeCount++;

                    foreach (var opt in turn.Options)
                    {
                        string afterPlayer = NextToken();

                        // NPC 回应的出口 = NextTurn 对应的入口 token，null → 关闭
                        string afterNpcResponse = !string.IsNullOrEmpty(opt.NextTurn)
                            ? TurnToken(fileTag, opt.NextTurn)
                            : "close_window";

                        // 玩家选项
                        var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
                        pdf.AddPlayerLine(
                            $"inj_opt_{Guid.NewGuid():N}", afterNpcLine, afterPlayer,
                            opt.PlayerLine ?? "…",
                            () => true,
                            () => ExecuteAction(opt),
                            owner, 125);
                        cm.AddDialogFlow(pdf, owner);
                        nodeCount++;

                        // NPC 回应
                        if (!string.IsNullOrEmpty(opt.NpcResponse))
                        {
                            cm.AddDialogLineMultiAgent(
                                $"inj_resp_{Guid.NewGuid():N}", afterPlayer, afterNpcResponse,
                                new TextObject(opt.NpcResponse),
                                () => true, null,
                                turn.SpeakerIndex, -1, 125);
                            nodeCount++;
                        }
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
            var toRemove = _injectedOwners.Where(o => o.FileName == label).ToList();
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
        private static readonly List<InjectOwner> _injectedOwners = new List<InjectOwner>();

        private class InjectOwner { public string FileName; }

        private static string NextToken() => $"lwnpc_atk_{_tokenCounter++}";

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
                            ExecuteIntentAction(intentSpec, oneToOne, opt.ActionParam);
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
        private static void ExecuteIntentAction(string intentName, Hero npc, string actionParam = null)
        {
            try
            {
                var intent = LivingWorldNpcs.Story.IntentRegistry.FindByName(intentName);
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

                // 注入犯罪事件上下文
                var settlement = npc?.CurrentSettlement;
                if (settlement != null)
                {
                    ctx.ActiveEvent = WorldEventStore.FindActive(settlement.StringId);
                }

                // 注入 ActionParam（栽赃目标 ID 等）
                if (!string.IsNullOrEmpty(actionParam))
                    ctx.FrameTargetId = actionParam;

                var eligibility = intent.Evaluate(ctx);
                if (eligibility.State == EligState.Hidden)
                {
                    DebugLogger.Log($"[DialogueInjector] Intent {intentName} hidden by Evaluate");
                    return;
                }

                if (intent.Goal == null)
                {
                    intent.OnInstant(ctx);
                }
                else
                {
                    var roll = SingleRollResolver.Compute(ctx, intent.Goal.Value, intent.Tactic, intent.GetOfferValue(ctx));
                    bool passed = SingleRollResolver.Roll(roll.Chance);
                    DebugLogger.Log($"[SkillCheck] {intentName} | {roll.Log} | 掷骰={(passed ? "通过" : "失败")} (chance={roll.Chance:P0})");
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

        // ⚠ 内部方法，与 InjectFromJson 的后半段逻辑完全相同
        private static void InjectScriptInternal(DialogueInjectScript script, string fileTag)
        {
            var cm = Campaign.Current.ConversationManager;
            var owner = new InjectOwner { FileName = fileTag };
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

                foreach (var turn in script.Turns)
                {
                    if (turn.Options == null || turn.Options.Count == 0) continue;

                    string turnEntryToken = TurnToken(fileTag, turn.Id);
                    string afterNpcLine = NextToken();
                    cm.AddDialogLineMultiAgent(
                        $"inj_npc_{turn.Id}", turnEntryToken, afterNpcLine,
                        new TaleWorlds.Localization.TextObject(turn.NpcLine ?? ""),
                        () => true, null, turn.SpeakerIndex, -1, 125);

                    foreach (var opt in turn.Options)
                    {
                        string afterPlayer = NextToken();
                        string afterNpcResponse = !string.IsNullOrEmpty(opt.NextTurn)
                            ? TurnToken(fileTag, opt.NextTurn) : "close_window";

                        var pdf = DialogFlow.CreateDialogFlow(afterNpcLine, 125);
                        pdf.AddPlayerLine($"inj_opt_{turn.Id}", afterNpcLine, afterPlayer,
                            opt.PlayerLine ?? "...", () => true, () => ExecuteAction(opt), owner, 125);
                        if (!string.IsNullOrEmpty(opt.NpcResponse))
                        {
                            cm.AddDialogLineMultiAgent(
                                $"inj_resp_{turn.Id}", afterPlayer, afterNpcResponse,
                                new TaleWorlds.Localization.TextObject(opt.NpcResponse),
                                () => true, null, turn.SpeakerIndex, -1, 125);
                        }
                        cm.AddDialogFlow(pdf, owner);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DialogueInjector] InjectScriptInternal error: {ex.Message}");
            }
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
            /// <summary>选了此选项后跳转到哪个 turn。null = 关闭对话。</summary>
            public string NextTurn = null;
            public string Action = "NONE";
            public int ActionValue = 0;
            /// <summary>字符串参数（栽赃目标 ID 等）。INTENT:xxx 执行时注入 IntentContext。</summary>
            public string ActionParam = null;
        }
    }
}
