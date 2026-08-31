using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本数据加载器（W1）：读 events.jsonc（多对象顺序拼接/数组均可）→ List&lt;ScenarioEventDef&gt;。
    /// 部署路径 = 模块根 ModuleData/ScenarioData/*.jsonc（发布流程 07 拷贝；补充包路径待 07 后加扫）。
    /// 校验子集（01 检查项 1-3 的加载期部分）：id 唯一/id 非空/trigger ∈ 注册表/facility 规则/step ∈ 步骤表/condition 非空 + 括号配平。
    /// 纪律：坏文件 = 日志 + 跳过不崩（铁律 1）；表外 token = 记录 [Scenario]，验证器/W4 执行器拦截。
    /// </summary>
    public static class ScenarioLoader
    {
        public static List<ScenarioEventDef> Events { get; } = new List<ScenarioEventDef>();

        /// <summary>演绎分件（story/*.jsonc；W5 播放器消费）</summary>
        public static List<ScenarioPlaybackDef> Playbacks { get; } = new List<ScenarioPlaybackDef>();

        public static ScenarioPlaybackDef FindPlayback(string id) => Playbacks.FirstOrDefault(p => p.Id == id);

        public static List<string> LoadReport { get; } = new List<string>();

        public static int LoadedFileCount { get; private set; }

        /// <summary>加载全部剧本事件（游戏启动懒加载 / 控制台手动触发）</summary>
        public static void LoadAll(IEnumerable<string> files = null)
        {
            if (files == null)
                files = LocateScenarioFiles();

            foreach (var file in files)
            {
                string clean = JsoncHelper.StripComments(File.ReadAllText(file, Encoding.UTF8));
                foreach (var chunk in SplitJsonObjects(clean))
                {
                    ScenarioEventDef evt;
                    try
                    {
                        evt = JsonConvert.DeserializeObject<ScenarioEventDef>(chunk);
                    }
                    catch (Exception e)
                    {
                        LoadReport.Add($"[ERR] {Path.GetFileName(file)}: JSON 解析失败（跳过）: {e.Message}");
                        continue;
                    }
                    if (evt == null || string.IsNullOrEmpty(evt.Id))
                    {
                        LoadReport.Add($"[ERR] {Path.GetFileName(file)}: 缺 id（跳过）");
                        continue;
                    }
                    ValidateEvent(evt, LoadReport);
                    Events.Add(evt);
                }
                LoadedFileCount++;
                LoadReport.Add($"[OK] {Path.GetFileName(file)}: 事件累计 {Events.Count}");
            }
            foreach (var line in LoadReport)
                DebugLogger.Log($"[Scenario] {line}");
        }

        /// <summary>读取全部演绎分件（ModuleData/ScenarioData/story/*.jsonc——每文件单对象）</summary>
        public static void LoadPlaybacks()
        {
            foreach (var file in LocateStoryFiles())
            {
                try
                {
                    string clean = JsoncHelper.StripComments(File.ReadAllText(file, Encoding.UTF8));
                    var def = JsonConvert.DeserializeObject<ScenarioPlaybackDef>(clean);
                    if (def == null || string.IsNullOrEmpty(def.Id)) { LoadReport.Add($"[ERR] {Path.GetFileName(file)}: 分件缺 id"); continue; }
                    if (Playbacks.Any(p => p.Id == def.Id)) { LoadReport.Add($"[WARN] {Path.GetFileName(file)}: 分件 id 重复（忽略）: {def.Id}"); continue; }
                    Playbacks.Add(def);
                }
                catch (Exception e)
                {
                    LoadReport.Add($"[ERR] {Path.GetFileName(file)}: 分件解析失败: {e.Message}");
                }
            }
            foreach (var line in LoadReport.Where(l => !l.StartsWith("[OK]")))
                DebugLogger.Log($"[Scenario] {line}");
        }

        private static IEnumerable<string> LocateStoryFiles()
        {
            string gameRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.FullName;
            if (gameRoot == null) return new List<string>();
            string dir = Path.Combine(gameRoot, "Modules", "LivingWorldNpcs", "ModuleData", "ScenarioData", "story");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.jsonc").OrderBy(f => f).ToList();
        }

        private static void ValidateEvent(ScenarioEventDef evt, List<string> report)
        {
            if (!ScenarioRegistry.Triggers.Contains(evt.Trigger))
                report.Add($"[ERR] {evt.Id}: trigger 表外 \"{evt.Trigger}\"（16 §二 注册表）");
            if (!string.IsNullOrEmpty(evt.Facility))
            {
                string f = evt.Facility.Replace("Facility::", "");
                if (evt.Trigger != "house_enter")
                    report.Add($"[ERR] {evt.Id}: 非 house_enter 带 facility \"{evt.Facility}\"（validator #16）");
                else if (!ScenarioRegistry.Facilities.Contains(f))
                    report.Add($"[ERR] {evt.Id}: facility 表外 \"{f}\"（16 §二 注册表）");
            }
            else if (evt.Trigger == "house_enter")
                report.Add($"[WARN] {evt.Id}: house_enter 缺 facility（validator #16）");

            if (evt.Priority != "normal" && evt.Priority != "weak")
                report.Add($"[ERR] {evt.Id}: priority 表外 \"{evt.Priority}\"");

            if (string.IsNullOrWhiteSpace(evt.Condition))
                report.Add($"[ERR] {evt.Id}: condition 为空");
            else if (!IsBalanced(evt.Condition))
                report.Add($"[ERR] {evt.Id}: condition 括号不配平（语法 W2 求值器接）");

            if (evt.Script != null)
            {
                var unknown = evt.Script.Where(s => !ScenarioRegistry.StepTypes.Contains(s.Step))
                                        .Select(s => s.Step).Distinct().Take(5).ToList();
                if (unknown.Count > 0)
                    report.Add($"[ERR] {evt.Id}: script 含表外 step（{string.Join(",", unknown)}）");
            }
            else
                report.Add($"[WARN] {evt.Id}: script 为空");
        }

        private static bool IsBalanced(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int depth = 0;
            foreach (var c in s)
            {
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth < 0) return false; }
            }
            return depth == 0;
        }

        /// <summary>
        /// 顶层对象拆分：兼容 ①数组包裹 ②单对象 ③多对象顺序拼接（events.jsonc 实际形态 = 顺序多个对象）。
        /// 扫描括号配平（字符串内跳过），不依赖数组边界——两形态通用。
        /// </summary>
        public static IEnumerable<string> SplitJsonObjects(string text)
        {
            string trimmed = (text ?? "").Trim();
            if (trimmed.StartsWith("﻿")) trimmed = trimmed.Substring(1).Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                // 数组包裹 → 剥外层方括号（括号配平找匹配的 ]）
                int depth = 0; bool inStr = false; int end = -1;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if (c == '"' && (i == 0 || trimmed[i - 1] != '\\')) inStr = !inStr;
                    if (inStr) continue;
                    if (c == '[') depth++;
                    else if (c == ']') { depth--; if (depth == 0) { end = i; break; } }
                }
                if (end > 0) return SplitJsonObjects(trimmed.Substring(1, end - 1));
            }

            var chunks = new List<string>();
            int d = 0; bool s = false; int start = -1;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '"' && (i == 0 || trimmed[i - 1] != '\\')) s = !s;
                if (s) continue;
                if (c == '{') { if (d == 0) start = i; d++; }
                else if (c == '}') { d--; if (d == 0 && start >= 0) { chunks.Add(trimmed.Substring(start, i - start + 1)); start = -1; } }
            }
            return chunks;
        }

        /// <summary>模块数据目录定位：本模块 ModuleData/ScenarioData/（v1；跨模块/补充包目录 07 后加扫）</summary>
        private static IEnumerable<string> LocateScenarioFiles()
        {
            string gameRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.FullName;
            if (gameRoot == null) return new List<string>();
            string dir = Path.Combine(gameRoot, "Modules", "LivingWorldNpcs", "ModuleData", "ScenarioData");
            if (!Directory.Exists(dir))
            {
                DebugLogger.Log($"[Scenario] 剧本数据目录不存在（首次启动正常）: {dir}");
                return new List<string>();
            }
            return Directory.GetFiles(dir, "*.jsonc").OrderBy(f => f).ToList();
        }

        public static void Reset()
        {
            Events.Clear();
            Playbacks.Clear();
            LoadReport.Clear();
            LoadedFileCount = 0;
        }
    }

    /// <summary>JSONC 注释剥离（行注释 + 块注释；字符串内不剥）——数据文件为 jsonc（T#/源行注释），引擎读前先剥。</summary>
    public static class JsoncHelper
    {
        public static string StripComments(string text)
        {
            if (text == null) return null;
            var sb = new StringBuilder(text.Length);
            bool inStr = false, lineComment = false, blockComment = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i], n = i + 1 < text.Length ? text[i + 1] : '\0';
                if (lineComment)
                {
                    if (c == '\n') { lineComment = false; sb.Append(c); }
                    continue;
                }
                if (blockComment)
                {
                    if (c == '*' && n == '/') { blockComment = false; i++; }
                    continue;
                }
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '"' && (i == 0 || text[i - 1] != '\\')) inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '/' && n == '/') { lineComment = true; i++; continue; }
                if (c == '/' && n == '*') { blockComment = true; i++; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
