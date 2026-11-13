using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>
    /// **LLM 诊断台**（2026-11-13）：`custom.llm info` / `custom.llm test`。
    ///
    /// 为什么需要命令行：MCM 的「测试 LLM 连接」按钮只弹一句红字/绿字就没了 ——
    /// 排查"填了却连不上"时必须能一次看到【实际请求到哪条 URL、走哪种接口方言、
    /// 失败归到哪一类】，否则只能靠猜。本命令把这些原样打出来（key 掩码）。
    ///
    /// 纪律：返回文本纯英文；首参可弃（数字/未知 → 回落到 info）。
    /// </summary>
    public static class LlmCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("llm", "custom")]
        public static string Execute(List<string> args)
        {
            string note = string.Empty;
            string sub = args != null && args.Count > 0 ? (args[0] ?? "").Trim().ToLowerInvariant() : "info";
            if (sub.Length > 0 && char.IsDigit(sub[0]))
            {
                note = $" [note: '{sub}' is not a subcommand -> using info]";
                sub = "info";
            }

            try
            {
                switch (sub)
                {
                    case "info": return CmdInfo() + note;
                    case "test": return CmdTest() + note;
                    default:
                        return $"llm: unknown subcommand '{sub}' (use: info / test)";
                }
            }
            catch (Exception ex)
            {
                return "llm: error " + ex.GetType().Name + ": " + OneLine(ex.Message);
            }
        }

        /// <summary>打印当前生效的配置与实际请求 URL（不含 key 明文）。</summary>
        private static string CmdInfo()
        {
            var cfg = Settings.Instance;
            string keyMask = !string.IsNullOrWhiteSpace(cfg?.LLMApiKey)
                ? cfg.LLMApiKey.Substring(0, Math.Min(4, cfg.LLMApiKey.Length)) + "...(" + cfg.LLMApiKey.Length + ")"
                : "(empty)";
            string baseUrl = string.IsNullOrWhiteSpace(cfg?.LLMBaseUrl) ? "(empty)" : OneLine(cfg.LLMBaseUrl);
            return $"llm: ready={cfg?.IsLLMConfigured} provider={LLMService.DiagnosticProvider} " +
                   $"base={baseUrl} url={OneLine(LLMService.DiagnosticUrl)} " +
                   $"model={OneLine(LLMService.DiagnosticModel)} key={keyMask}";
        }

        /// <summary>跑一次真实连接测试（同步，最长冻结 10 秒），打印分类结果与错误体。</summary>
        private static string CmdTest()
        {
            var r = LLMService.TestConnection();
            string reason = r == null || r.Success ? "None" : r.Reason.ToString();
            string detail = r == null || string.IsNullOrEmpty(r.Detail) ? "" : " detail=" + OneLine(r.Detail);
            return $"llm: test success={(r != null && r.Success)} reason={reason}{detail}";
        }

        /// <summary>控制台一行显示：把消息里的换行压平（错误体常是多行 JSON）并截断。</summary>
        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 300 ? t.Substring(0, 300) + "..." : t;
        }
    }
}
