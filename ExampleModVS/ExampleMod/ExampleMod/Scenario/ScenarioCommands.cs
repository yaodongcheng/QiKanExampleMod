using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本控制台指令（12 分拆测试；注册样式 = Debug/MyCommands.cs 同款 CommandLineFunctionality 特性）。
    /// 🔴 签名必须是 static string Xxx(List&lt;string&gt; args)——引擎委托是 Func&lt;List&lt;string&gt;,string&gt;，写 string[] 编译能过但启动必崩
    ///    （Delegate.CreateDelegate 绑定失败 ArgumentException，CollectCommandLineFunctions 扫描到即炸整场启动）。
    /// 🔴 新指令 = 本类加行 + 11-存档与配置.md 控制台全集加行。
    /// </summary>
    public static class ScenarioCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_list", "custom")]
        public static string ScnList(List<string> args)
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
        public static string ScnStatus(List<string> args)
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
        public static string ScnReload(List<string> args)
        {
            ScenarioLoader.Reset();
            ScenarioLoader.LoadAll();
            return "剧本数据已重载";
        }

        /// <summary>custom.scn_init：手动跑一次剧本初始化（新档自动跑；本指令 = 测试/补种）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_init", "custom")]
        public static string ScnInit(List<string> args)
        {
            ScenarioInit.Apply();
            return $"[Scenario][Init] 完成：英雄 {ScenarioInit.SeededHeroes} / 属性 {ScenarioInit.SeededAttrs} / 拨年龄 {ScenarioInit.AdjustedAges} / 未出生 {ScenarioInit.UnbornSkipped} / 已死 {ScenarioInit.DeceasedSkipped}";
        }

        /// <summary>custom.scn_force_event &lt;eventId&gt;：强制触发某事件（12 A2；互斥选路/once/done 全走真路径）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_force_event", "custom")]
        public static string ScnForceEvent(List<string> args)
        {
            string id = args != null && args.Count > 0 ? args[0] : null;
            if (string.IsNullOrEmpty(id)) return "用法: custom.scn_force_event EVENT_ID";
            ScenarioScheduler.EnsureLoadedAll();
            var evt = ScenarioLoader.Events.Find(e => e.Id == id);
            if (evt == null) return $"未找到事件 {id}（先 scn_list 看清单）";
            ScenarioScheduler.ExecuteEvent(evt);
            return $"[Scenario] 事件 {id} 执行完毕（once={(evt.Once ? "是→done" : "否")}）";
        }

        /// <summary>custom.scn_run_action &lt;action&gt; [key=value...]：单动作落世界（12 A3；例：scn_run_action update target="(Hero::x.alive)" value="true"）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_run_action", "custom")]
        public static string ScnRunAction(List<string> args)
        {
            if (args == null || args.Count == 0) return "用法: custom.scn_run_action ACTION [k=v ...]";
            var step = new ScenarioScriptStep { Step = "effect", Action = args[0] };
            for (int i = 1; i < args.Count; i++)
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

        /// <summary>custom.playback_dump &lt;playbackId&gt;：dump 一个分件的指令流（05 验收；无参 = 列清单）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("playback_dump", "custom")]
        public static string PlaybackDump(List<string> args)
        {
            if (ScenarioLoader.Playbacks.Count == 0) ScenarioLoader.LoadPlaybacks();
            var sb = new StringBuilder();
            if (args == null || args.Count == 0 || string.IsNullOrEmpty(args[0]))
            {
                sb.AppendLine($"== 演绎分件 {ScenarioLoader.Playbacks.Count} 个 ==");
                foreach (var p in ScenarioLoader.Playbacks)
                    sb.AppendLine($"  {p.Id}  [{(p.Form ?? "?")}] {p.Lines?.Count ?? 0} 行");
                return sb.ToString();
            }
            var def = ScenarioLoader.FindPlayback(args[0]);
            if (def == null) return $"未找到分件 {args[0]}";
            sb.AppendLine($"{def.Id} [{(def.Form ?? "?")}]");
            foreach (var ln in def.Lines ?? new List<PlaybackLine>())
                sb.AppendLine($"  {ln}");
            return sb.ToString();
        }

        /// <summary>custom.playback_play &lt;playbackId&gt;：实演一个分件（面板真机验收；menu 形态全要素）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("playback_play", "custom")]
        public static string PlaybackPlay(List<string> args)
        {
            if (args == null || args.Count == 0 || string.IsNullOrEmpty(args[0])) return "用法: custom.playback_play PLAYBACK_ID";
            if (ScenarioLoader.Playbacks.Count == 0) ScenarioLoader.LoadPlaybacks();
            PlaybackPlayer.Play(args[0]);
            return PlaybackPlayer.IsPlaying ? $"[Playback] 开始演 {args[0]}" : $"[Playback] {args[0]} 未开始（见日志）";
        }

        /// <summary>custom.scn_set_identity &lt;heroId|none&gt;：设定当前扮演身份（显示层：立绘/名字/镜像位；06 身份注入前身）。例：scn_set_identity lord_1_oda</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("scn_set_identity", "custom")]
        public static string ScnSetIdentity(List<string> args)
        {
            if (args == null || args.Count == 0 || args[0] == "none") return "用法: custom.scn_set_identity HERO_ID（none 清除）";
            ScenarioPlayerIdentity.SetPlayerHero(args[0].Trim());
            return $"[Scenario][Identity] 当前扮演 = {args[0]}（显示层跟随；世界注入 = 06 后置）";
        }

        /// <summary>
        /// custom.playback_demo：对话流演示（来回对白 + 选项 + 立绘——附录-立绘显示接入与分发方案.cs 轮子）。
        /// 主角 = 信长（身份锚已设：lord_1_oda——右侧镜像）；非主角（秀吉）= 左侧原朝向。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("playback_demo", "custom")]
        public static string PlaybackDemo(List<string> args)
        {
            ScenarioPlayerIdentity.SetPlayerHero("lord_1_oda");   // 用户 2026-08-31："我是信长"——demo 固定主角=信长（换人 = scn_set_identity 手动）
            string T(string key, string fallback) => new TaleWorlds.Localization.TextObject("{=" + key + "}" + fallback).ToString();

            var def = new ScenarioPlaybackDef
            {
                Id = "demo_flow",
                Form = "scene",
                Lines = new List<PlaybackLine>
                {
                    new PlaybackLine { Cmd = "dialogue", Speaker = "Hero::lord_1_kinoshita", Text = T("LWN_SCN_demo_1", "（秀吉）织田信长大人——今宵，就是决断之时！") },
                    new PlaybackLine { Cmd = "dialogue", Speaker = "Hero::lord_1_oda", Text = T("LWN_SCN_demo_2", "（信长）哦？终于轮到你开口了吗，羽柴！") },
                    new PlaybackLine { Cmd = "dialogue", Speaker = "Hero::MainHero", Text = T("LWN_SCN_demo_5", "（你）主公！末将愿为先锋——请下令！") },
                    new PlaybackLine { Cmd = "dialogue", Speaker = "Hero::lord_1_kinoshita", Text = T("LWN_SCN_demo_3", "（秀吉）天下布武，就在今朝！前线大军**兵发清州**！") },
                    new PlaybackLine { Cmd = "choice", Options = new List<PlaybackOption>
                    {
                        new PlaybackOption { Text = T("LWN_SCN_demo_opt0", "立刻发兵！") },
                        new PlaybackOption { Text = T("LWN_SCN_demo_opt1", "休要急躁，再探军情") },
                    }},
                    new PlaybackLine { Cmd = "dialogue", Speaker = "Hero::lord_1_kinoshita", Text = T("LWN_SCN_demo_4", "（秀吉）好！末将这就去整军出发——決断已定！") },
                },
            };
            PlaybackPlayer.Play(def);
            return PlaybackPlayer.IsPlaying ? "[Playback] 演示开始（秀吉→信长→选项→秀吉）" : "[Playback] 演示未开始（见日志）";
        }

        /// <summary>custom.playback_show &lt;text&gt;：显示一条 PlaybackDialog 对白（T7 面板真机验证）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("playback_show", "custom")]
        public static string PlaybackShow(List<string> args)
        {
            if (args == null || args.Count == 0) return "用法: custom.playback_show \"台词\" 或 playback_show 说话人 台词";
            string speaker = args.Count >= 2 ? args[0] : "测试";
            string text = args.Count >= 2 ? string.Join(" ", args.Skip(1)) : string.Join(" ", args);
            PlaybackDialogUI.VM.Show(speaker, text, null);
            PlaybackDialogUI.Open();
            return $"[PlaybackDialog] 显示: {speaker} —— {text}";
        }

        /// <summary>custom.playback_close：关闭面板</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("playback_close", "custom")]
        public static string PlaybackClose(List<string> args)
        {
            PlaybackDialogUI.VM.Close();
            PlaybackDialogUI.Close();
            return "[PlaybackDialog] 已关闭";
        }

        /// <summary>custom.dsl_eval &lt;表达式&gt;：单条 DSL 求值（12 A1 分拆测试）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("dsl_eval", "custom")]
        public static string DslEval(List<string> args)
        {
            string expr = string.Join(" ", args ?? new List<string>());
            if (string.IsNullOrWhiteSpace(expr)) return "用法: custom.dsl_eval \"表达式\"（空格会拼接）";
            var r = DslEvaluator.EvaluateDetailed(expr);
            return $"[Scenario][Dsl] {r.Result} | 不可判定={r.Undecidable.Count} | 错误={r.Error ?? "(无)"}" +
                   (r.Undecidable.Count > 0 ? "\n  " + string.Join("\n  ", r.Undecidable) : "");
        }

        /// <summary>custom.dsl_validate：全事件条件求值状态（可判定/不可判定/错误 三档汇总）</summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("dsl_validate", "custom")]
        public static string DslValidate(List<string> args)
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
