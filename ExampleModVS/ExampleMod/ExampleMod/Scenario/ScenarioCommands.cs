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
