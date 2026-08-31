using System.Text;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本控制台指令（12 分拆测试；注册样式 = Debug/MyCommands.cs 同款 CommandLineFunctionality 特性）。
    /// 🔴 新指令 = 本类加行 + 11-存档与配置.md 控制台全集加行。
    /// </summary>
    public static class ScenarioCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_list", "custom")]
        public static string ScnList(string[] args)
        {
            if (ScenarioLoader.Events.Count == 0)
                ScenarioLoader.LoadAll();

            var sb = new StringBuilder();
            sb.AppendLine($"== 剧本事件 {ScenarioLoader.Events.Count} 个（{ScenarioLoader.LoadedFileCount} 文件）==");
            foreach (var evt in ScenarioLoader.Events)
                sb.AppendLine($"  {evt}");
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("scn_status", "custom")]
        public static string ScnStatus(string[] args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== 剧本状态 ==");
            var sink = GlobalVariableBehavior.Instance;
            sb.AppendLine($"  仓存在: {(sink != null ? "是" : "否（未开始战役）")}");
            sb.AppendLine($"  加载报告 {ScenarioLoader.LoadReport.Count} 行:");
            var tail = ScenarioLoader.LoadReport.Count > 30 ? ScenarioLoader.LoadReport.GetRange(ScenarioLoader.LoadReport.Count - 30, 30) : ScenarioLoader.LoadReport;
            foreach (var line in tail)
                sb.AppendLine($"    {line}");
            sb.AppendLine("  [ERROR 行] " + ScenarioLoader.LoadReport.FindAll(l => l.StartsWith("[ERR]")).Count);
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("scn_reload", "custom")]
        public static string ScnReload(string[] args)
        {
            ScenarioLoader.Reset();
            ScenarioLoader.LoadAll();
            return "剧本数据已重载";
        }

        /// <summary>custom.scn_init：手动跑一次剧本初始化（新档自动跑；本指令 = 测试/补种）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_init", "custom")]
        public static string ScnInit(string[] args)
        {
            ScenarioInit.Apply();
            return $"[Scenario][Init] 完成：英雄 {ScenarioInit.SeededHeroes} / 属性 {ScenarioInit.SeededAttrs} / 拨年龄 {ScenarioInit.AdjustedAges} / 未出生 {ScenarioInit.UnbornSkipped} / 已死 {ScenarioInit.DeceasedSkipped}";
        }

        /// <summary>custom.scn_force_event &lt;eventId&gt;：强制触发某事件（12 A2；互斥选路/once/done 全走真路径）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_force_event", "custom")]
        public static string ScnForceEvent(string[] args)
        {
            string id = args != null && args.Length > 0 ? args[0] : null;
            if (string.IsNullOrEmpty(id)) return "用法: custom.scn_force_event EVENT_ID";
            ScenarioScheduler.EnsureLoadedAll();
            var evt = ScenarioLoader.Events.Find(e => e.Id == id);
            if (evt == null) return $"未找到事件 {id}（先 scn_list 看清单）";
            ScenarioScheduler.ExecuteEvent(evt);
            return $"[Scenario] 事件 {id} 执行完毕（once={(evt.Once ? "是→done" : "否")}）";
        }

        /// <summary>custom.scn_run_action &lt;action&gt; [key=value...]：单动作落世界（12 A3；例：scn_run_action update target="(Hero::x.alive)" value="true"）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_run_action", "custom")]
        public static string ScnRunAction(string[] args)
        {
            if (args == null || args.Length == 0) return "用法: custom.scn_run_action ACTION [k=v ...]";
            var step = new ScenarioScriptStep { Step = "effect", Action = args[0] };
            for (int i = 1; i < args.Length; i++)
            {
                int eq = args[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = args[i].Substring(0, eq), v = args[i].Substring(eq + 1);
                if (step.Extra == null) step.Extra = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>();
                step.Extra[k] = new Newtonsoft.Json.Linq.JValue(v.Trim('"'));
            }
            bool ok = ScenarioActions.Execute(step, ScenarioContext.Instance);
            return $"[Scenario][Action] {args[0]} 执行{(ok ? "完成" : "（返回 false）")}";
        }

        /// <summary>custom.dsl_eval &lt;表达式&gt;：单条 DSL 求值（12 A1 分拆测试）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("dsl_eval", "custom")]
        public static string DslEval(string[] args)
        {
            string expr = string.Join(" ", args ?? new string[0]);
            if (string.IsNullOrWhiteSpace(expr)) return "用法: custom.dsl_eval \"表达式\"（空格会拼接）";
            var r = DslEvaluator.EvaluateDetailed(expr);
            return $"[Scenario][Dsl] {r.Result} | 不可判定={r.Undecidable.Count} | 错误={r.Error ?? "(无)"}" +
                   (r.Undecidable.Count > 0 ? "\n  " + string.Join("\n  ", r.Undecidable) : "");
        }

        /// <summary>custom.dsl_validate：全事件条件求值状态（可判定/不可判定/错误 三档汇总）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("dsl_validate", "custom")]
        public static string DslValidate(string[] args)
        {
            if (ScenarioLoader.Events.Count == 0) ScenarioLoader.LoadAll();
            var sb = new StringBuilder();
            int ok = 0, undec = 0, err = 0;
            foreach (var evt in ScenarioLoader.Events)
            {
                var r = DslEvaluator.EvaluateDetailed(evt.Condition);
                if (r.Error != null) err++;
                else if (r.Undecidable.Count > 0) undec++;
                else ok++;
                sb.AppendLine($"  {evt.Id}: {(r.Error != null ? "语法错误" : r.Undecidable.Count > 0 ? $"不可判定 {r.Undecidable.Count} 条" : "可判定")}");
                foreach (var u in r.Undecidable) sb.AppendLine($"      ↳ {u}");
            }
            sb.Insert(0, $"[Scenario][Dsl-validate] {ScenarioLoader.Events.Count} 事件：可判定 {ok} / 不可判定 {undec} / 错误 {err}\n");
            return sb.ToString();
        }
    }
}
