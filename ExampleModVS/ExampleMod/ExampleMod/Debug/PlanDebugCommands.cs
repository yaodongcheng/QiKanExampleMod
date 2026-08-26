using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanDebugCommands.cs — 密谋命令系统调试指令（§12 验证方案）
    //
    // custom.plan_debug snapshot                → 场景快照文本
    // custom.plan_debug list                    → 示例计划清单
    // custom.plan_debug run <示例名> [agentId]   → 注入并执行示例计划
    // custom.plan_debug status [agentId]        → 执行器状态
    // custom.plan_debug stop [agentId]          → 停止执行中的计划
    // custom.plan_debug role <角色名> [agentId]  → 按角色名跑计划（角色表注入）
    //
    // 示例计划路径：模块根/Debug/PlanExamples/*.json（开发期测试数据，不随 Mod 交付）。
    // ═══════════════════════════════════════════════════════════════

    public static class PlanDebugCommands
    {
        private static string ExamplesDir
        {
            get
            {
                try
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Modules", "LivingWorldNpcs", "Debug", "PlanExamples");
                }
                catch { return "Debug/PlanExamples"; }
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("plan_debug", "custom")]
        public static string ExecutePlanDebug(List<string> args)
        {
            if (Mission.Current == null) return "error: must be in a mission";
            if (args == null || args.Count == 0) return Usage();
            string cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "snapshot":
                    return DumpSnapshot();
                case "list":
                    return ListExamples();
                case "run":
                    return RunExample(args);
                case "status":
                    return Status(args);
                case "stop":
                    return Stop(args);
                case "role":
                    return RunWithRole(args);
                case "step":
                    return StepDetail(args);
                case "replan":
                    return ForceReplan(args);
                case "steal_rate":
                    return SetStealRate(args);
                default:
                    return Usage();
            }
        }

        private static string Usage()
        {
            return "usage: custom.plan_debug <snapshot|list|run <name> [agentId]|status [agentId]|stop [agentId]|role <role> [agentId]|step [agentId]|replan [agentId]|steal_rate <0.05~0.95|off>>";
        }

        /// <summary>调试（2026-08-21）：偷窃/击晕成功率强制覆盖（本地调试：NPC 偷窃老失败时锁概率）。
        /// 随从偷窃（InlineSteps）+ KnockoutFlow 共享管线（玩家/NPC 击晕）都吃；off 恢复原公式。</summary>
        private static string SetStealRate(List<string> args)
        {
            if (args.Count < 2) return "usage: custom.plan_debug steal_rate <0.05~0.95|off>";
            string v = args[1].ToLowerInvariant();
            if (v == "off" || v == "none" || v == "0")
            {
                Settings.Instance.StealSuccessRateOverride = -1f;
                return $"偷窃/击晕成功率覆盖已关闭（走原公式）";
            }
            if (!float.TryParse(v, out float rate) || rate < 0.05f || rate > 0.95f)
                return "error: 值须在 0.05~0.95 之间（或 off）";
            Settings.Instance.StealSuccessRateOverride = rate;
            return $"偷窃/击晕成功率强制为 {rate:P0}（随从偷窃 + 击晕共享管线生效）";
        }

        /// <summary>当前步骤详情（§12 调试：执行器游标位置/步骤动作/摘要）。</summary>
        private static string StepDetail(List<string> args)
        {
            var agent = ResolveAgent(args.Count > 1 ? args[1] : null);
            if (agent == null) return "error: agent not found";
            var ex = PlanExecutor.GetExecutorFor(agent);
            if (ex == null) return $"{agent.Name} 当前无执行中的计划";
            var sb = new StringBuilder();
            sb.AppendLine($"{agent.Name} 当前执行详情:");
            sb.AppendLine($"  State: {ex.State}   Elapsed: {ex.Elapsed:F1}s");
            sb.AppendLine($"  Goal: {ex.Plan?.Goal?.Type ?? "(无 goal)"}");
            sb.AppendLine($"  主链步骤: {ex.Plan?.Steps?.Count ?? 0}  预案: {ex.Plan?.Fallbacks?.Count ?? 0}  contingencies: {ex.Plan?.Contingencies?.Count ?? 0}");
            var world = ex.World;
            var steps = ex.Plan?.Steps;
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    var s = steps[i];
                    if (s == null) continue;
                    string marker = i == ex.SelfCursorIndex ? "→" : " ";
                    sb.AppendLine($"  {marker} {s.Id}: {s.Action} target={PlanRefUtil.Normalize(s.Target, out _)} timeout={s.TimeoutS}s");
                }
            }
            return sb.ToString();
        }

        /// <summary>强制触发 replan（§12 调试：R5 意外重入的链路验证）。</summary>
        private static string ForceReplan(List<string> args)
        {
            var agent = ResolveAgent(args.Count > 1 ? args[1] : null);
            if (agent == null) return "error: agent not found";
            var ex = PlanExecutor.GetExecutorFor(agent);
            if (ex == null) return $"{agent.Name} 当前无执行中的计划";
            if (!Settings.Instance.IsLLMConfigured) return "error: LLM 未配置（IsLLMConfigured=false）";
            if (string.IsNullOrEmpty(ex.OriginalCommand)) return "error: 无原命令（replan 上下文缺失）";
            ex.EventLog.Add($"{ex.Elapsed:F0}s: 调试强制 replan（步骤 {ex.SelfCursorIndex} 处）");
            ex.AbortForReplanDebug(PlanTexts.FightBrokeOut);
            return $"{agent.Name} 已触发 replan（第 {ex.ReplanCount + 1} 次，LLM 异步执行，结果看日志）";
        }

        private static string DumpSnapshot()
        {
            var snap = SceneSnapshot.Build(Mission.Current, agentLimit: 30);
            return snap.ToPromptText();
        }

        private static string ListExamples()
        {
            if (!Directory.Exists(ExamplesDir)) return $"error: examples dir not found: {ExamplesDir}";
            var files = Directory.GetFiles(ExamplesDir, "*.json");
            var sb = new StringBuilder();
            sb.AppendLine($"示例计划（{files.Length} 份，{ExamplesDir}）:");
            foreach (var f in files)
                sb.AppendLine($"  {Path.GetFileNameWithoutExtension(f)}");
            return sb.ToString();
        }

        private static string RunExample(List<string> args)
        {
            if (args.Count < 2) return "usage: custom.plan_debug run <name> [agentId]";
            string name = args[1];
            string path = Path.Combine(ExamplesDir, name.EndsWith(".json") ? name : name + ".json");
            if (!File.Exists(path)) return $"error: example not found: {path}";
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { return $"error: read failed: {ex.Message}"; }
            return InjectPlan(json, args.Count > 2 ? args[2] : null, null);
        }

        private static string RunWithRole(List<string> args)
        {
            if (args.Count < 2) return "usage: custom.plan_debug role <role> [agentId]";
            string role = args[1];
            string path = Path.Combine(ExamplesDir, "A_DISTRACT.json");
            if (!File.Exists(path)) return "error: A_DISTRACT.json not found";
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { return $"error: read failed: {ex.Message}"; }
            return InjectPlan(json, args.Count > 2 ? args[2] : null, role);
        }

        private static string InjectPlan(string json, string agentId, string roleOverride)
        {
            Plan plan = null;
            try
            {
                plan = JsonConvert.DeserializeObject<Plan>(LLMService.CleanJson(json));
            }
            catch (Exception ex)
            {
                return $"error: plan JSON 解析失败: {ex.Message}";
            }
            if (plan == null) return "error: plan is null";

            Agent agent = ResolveAgent(agentId);
            if (agent == null) return "error: agent not found（需在场景中，或指定 agentId）";

            // 角色表注入：示例里的 guard/chief 等角色 → 场景匹配
            var roleAgents = new Dictionary<string, Agent>();
            if (!string.IsNullOrEmpty(roleOverride))
                roleAgents[roleOverride] = agent;
            else
            {
                var snap = SceneSnapshot.Build(Mission.Current);
                var intent = plan.Intent;
                if (intent != null)
                {
                    var refs = new[] { intent.Target, intent.WatchPoint, intent.Opponent };
                    foreach (var r in refs)
                    {
                        string t = PlanRefUtil.Normalize(r, out string _);
                        if (string.IsNullOrEmpty(t) || t == "player" || t == "self") continue;
                        var info = snap.FindAgent(t);
                        if (info?.Agent != null && info.Agent != agent)
                            roleAgents[t] = info.Agent;
                    }
                }
            }

            var executor = PlanExecutor.Create(agent, plan, plan.Intent?.IntentType, roleAgents);
            if (executor == null) return "error: 计划校验未通过（见日志）";

            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain == null) return "error: agent has no brain";
            brain.SetNpcIntent(NpcIntentType.ExecutingCommand, null,
                commandDetail: PlanExecutor.ParseIntentType(plan.Intent?.IntentType));
            brain.ClearAllActions();
            // D1（单脑化重构）：占位动作 ExecutePlanAction 已删——执行器直接启动（不入队），
            // 行为步骤由执行器逐步入队（脑队列持有真实动作）。
            executor.Start(agent);
            var exRef = executor;
            executor.OnFinished += e => brain.OnPlanExecutorFinished(exRef);
            // Replan 接线（调试注入同样支持意外重入）
            PlanReplan.Wire(executor, plan.Summary ?? "（调试命令）", plan.Intent?.IntentType);

            return $"计划已注入 {agent.Name}：{plan.Summary ?? plan.Intent?.IntentType}（steps={plan.Steps?.Count ?? 0}）";
        }

        private static string Status(List<string> args)
        {
            var agent = ResolveAgent(args.Count > 1 ? args[1] : null);
            if (agent == null) return "error: agent not found";
            var ex = PlanExecutor.GetExecutorFor(agent);
            if (ex == null) return $"{agent.Name} 当前无执行中的计划";
            var sb = new StringBuilder();
            sb.AppendLine($"{agent.Name} 执行器状态:");
            sb.AppendLine($"  State: {ex.State}");
            sb.AppendLine($"  Intent: {ex.IntentType}");
            sb.AppendLine($"  Elapsed: {ex.Elapsed:F1}s");
            sb.AppendLine($"  Summary: {ex.CurrentSummary}");
            sb.AppendLine($"  PauseReason: {ex.PauseReason ?? "-"}");
            return sb.ToString();
        }

        private static string Stop(List<string> args)
        {
            var agent = ResolveAgent(args.Count > 1 ? args[1] : null);
            if (agent == null) return "error: agent not found";
            var ex = PlanExecutor.GetExecutorFor(agent);
            if (ex == null) return $"{agent.Name} 当前无执行中的计划";
            ex.CancelByPlayer("调试停止");
            return $"{agent.Name} 计划已停止";
        }

        /// <summary>解析目标 agent：agentId（StringId）→ 指定；null → 随从（Leader==Main）或最近的非玩家。</summary>
        private static Agent ResolveAgent(string agentId)
        {
            if (Mission.Current == null || Mission.Current.Agents == null) return null;
            if (!string.IsNullOrEmpty(agentId))
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !AgentControlHelper.SafeIsActive(a)) continue;
                    if (string.Equals(a.Character?.StringId, agentId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a.Name?.ToString(), agentId, StringComparison.OrdinalIgnoreCase))
                        return a;
                }
                return null;
            }
            // 默认：随从（有 Leader 且 Leader==Main）优先
            Agent best = null;
            float bestDist = float.MaxValue;
            foreach (var a in Mission.Current.Agents)
            {
                if (a == null || !AgentControlHelper.SafeIsActive(a) || !AgentControlHelper.IsHumanOrChild(a) || a == Agent.Main) continue;
                var brain = AgentAIController.GetBrainForAgent(a);
                if (brain?.Leader == Agent.Main) return a;   // 随从优先
                float d = a.Position.DistanceSquared(Agent.Main?.Position ?? a.Position);
                if (d < bestDist) { bestDist = d; best = a; }
            }
            return best;
        }
    }
}
