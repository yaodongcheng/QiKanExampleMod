using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace ExampleMod
{

    public class ScriptOption
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }

    // 根节点：整个事件对象

    public class StoryEvent
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } // 例如 "事件"

        [JsonProperty("attributes")]
        public EventAttributes Attributes { get; set; }

        [JsonProperty("trigger")]
        public EventTrigger Trigger { get; set; }

        [JsonProperty("conditions")]
        public List<ScriptNode> Conditions { get; set; } // 事件触发的前置条件

        [JsonProperty("script")]
        public List<ScriptNode> Script { get; set; } // 事件具体的执行流程
    }

    public class EventAttributes
    {
        [JsonProperty("value")]
        public string Value { get; set; } // 例如 "一次"
    }

    public class EventTrigger
    {
        [JsonProperty("type")]
        public string Type { get; set; } // 例如 "遊戲開始時", "室內畫面表示後"

        [JsonProperty("params")]
        public List<string> Params { get; set; }
    }

    // 核心节点类：用于同时表示 Condition(条件) 和 Command(命令)
    // 因为你的JSON中，script列表里混杂了 {type:"調查"} 和 {cmd:"更新"}
    public class ScriptNode
    {
        // --- 识别字段 ---
        [JsonProperty("cmd")]
        public string Cmd { get; set; } // 指令名，如 "更新", "旁白", "主人公別"

        [JsonProperty("type")]
        public string Type { get; set; } // 类型，如 "調查" (当它作为条件出现时)

        // --- 参数字段 ---
        [JsonProperty("params")]
        public List<string> Params { get; set; } // 通用参数数组

        [JsonProperty("options")]
        public List<ScriptOption> Options { get; set; } // 通用参数数组

        [JsonProperty("text")]
        public string Text { get; set; } // 对话或旁白文本

        [JsonProperty("emotion")]
        public string Emotion { get; set; } // 情绪

        [JsonProperty("value")]
        public string Value { get; set; } // 分歧的值，例如 "織田信長" 或 "1"

        // --- 条件逻辑字段 (当 Type="調查" 时使用) ---
        [JsonProperty("operator")]
        public string Operator { get; set; } // "==", "!=", ">"

        [JsonProperty("left")]
        public string Left { get; set; } // 左值

        [JsonProperty("right")]
        public string Right { get; set; } // 右值

        [JsonProperty("expression")]
        public string Expression { get; set; } // 表达式，如 "大名家::武田信玄.存在"

        // --- 递归字段 ---
        [JsonProperty("children")]
        public List<ScriptNode> Children { get; set; } // 子节点（用于 "主人公別", "分歧" 等）

        // 辅助属性：判断这是不是一个逻辑检查节点
        [JsonIgnore]
        public bool IsConditionNode => !string.IsNullOrEmpty(Type) && Type == "調查";

        // 辅助属性：判断这是不是一个命令节点
        [JsonIgnore]
        public bool IsCommandNode => !string.IsNullOrEmpty(Cmd);
    }
    public static class StoryDataLoader
    {
        // 静态缓存，存储所有加载的事件
       // public static List<StoryEvent> AllEvents { get; private set; } = new List<StoryEvent>();
        public static Dictionary<string, List<StoryEvent>> eventsMap { get; private set; } = new Dictionary<string, List<StoryEvent>>();
        // 加载方法
        public static void LoadEvents()
        {
            eventsMap.Clear();
            string moduleName = "ExampleMod";
            string directoryPath = Path.Combine(ModuleHelper.GetModuleFullPath(moduleName), "ModuleData\\StoryJson");
            float eventNum = 0;
            if (!Directory.Exists(directoryPath))
            {
                InformationManager.DisplayMessage(new InformationMessage($"错误: 找不到目录 {directoryPath}"));
                return;
            }
            // 【修改点2】获取目录下所有 .json 文件 (如果只想读EC开头的，可以改成 "EC*.json")
            string[] eventFiles = Directory.GetFiles(directoryPath, "*.json");

            foreach (var eventfilePath in eventFiles)
            {
                string fileNameKey = Path.GetFileNameWithoutExtension(eventfilePath); // 获取文件名作为 Key，例如 "EC500000"

                try
                {
                    string jsonContent = File.ReadAllText(eventfilePath);
                    var eventsInFile = JsonConvert.DeserializeObject<List<StoryEvent>>(jsonContent);
                    if (eventsInFile != null && eventsInFile.Count > 0)
                    {
                        eventsMap[fileNameKey] = eventsInFile;
                        Debug.Print($"文件 {fileNameKey} 加载了 {eventsInFile.Count} 个事件。");
                        eventNum = eventNum + eventsInFile.Count;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"[MyOdaMod] 解析文件 {fileNameKey} 失败: {ex.Message}");
                }
            }
            InformationManager.DisplayMessage(new InformationMessage($"共加载了 {eventsMap.Count} 个文件数据，一共{eventNum}个事件。"));




           
        }
        public static StoryEvent FindEvent(string fileNameKey, string eventId)
        {
            if(eventsMap == null)
            {
                LoadEvents();
            }
            if (eventsMap.ContainsKey(fileNameKey))
            {
                return eventsMap[fileNameKey].Find(e => e.Id == eventId);
            }
            return null;
        }
    }
    public static class StoryDebugHelper
    {
        // 主入口：打印单个事件的完整信息
        public static string PrintEventInfo(StoryEvent storyEvent)
        {
            if (storyEvent == null)
            {
                //Debug.Print("[StorySystem] Error: Event is null.");
                InformationManager.DisplayMessage(new InformationMessage("[StorySystem] Error: Event is null."));
                return "[StorySystem] Error: Event is null.";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"[EVENT LOADED] ID: {storyEvent.Id}");
            sb.AppendLine($"Type: {storyEvent.Type}");

            // 打印属性
            if (storyEvent.Attributes != null)
                sb.AppendLine($"Attribute: {storyEvent.Attributes.Value}");

            // 打印触发器
            if (storyEvent.Trigger != null)
            {
                string paramsStr = storyEvent.Trigger.Params != null ? string.Join(", ", storyEvent.Trigger.Params) : "";
                sb.AppendLine($"Trigger: {storyEvent.Trigger.Type} [{paramsStr}]");
            }

            // 打印前置条件 (Root Conditions)
            sb.AppendLine("--- Pre-Conditions ---");
            if (storyEvent.Conditions != null)
            {
                foreach (var cond in storyEvent.Conditions)
                {
                    PrintNodeRecursive(cond, 1, sb);
                }
            }

            // 打印脚本流程 (Script Flow)
            sb.AppendLine("--- Script Flow ---");
            if (storyEvent.Script != null)
            {
                foreach (var node in storyEvent.Script)
                {
                    PrintNodeRecursive(node, 1, sb);
                }
            }
            sb.AppendLine("========================================");

            // 输出到游戏控制台或调试窗口
            // 注意：如果内容太长，Debug.Print 可能会截断，建议结合断点查看 sb.ToString()
            //Debug.Print(sb.ToString());
            InformationManager.DisplayMessage(new InformationMessage(sb.ToString()));
            return sb.ToString();
        }

        // 递归核心：处理嵌套的 children
        private static void PrintNodeRecursive(ScriptNode node, int level, StringBuilder sb)
        {
            if (node == null) return;

            // 生成缩进字符串 (例如 "  " 或 "    ")
            string indent = new string(' ', level * 4);
            string prefix = indent + "├─ ";

            // 1. 识别节点类型并打印核心信息
            if (!string.IsNullOrEmpty(node.Cmd))
            {
                // 情况A：命令节点 (如 "更新", "旁白", "主人公別")
                sb.Append($"{prefix}[CMD] {node.Cmd}");

                // 如果有 Value (如 "主人公分歧" 下的 "織田信長")
                if (!string.IsNullOrEmpty(node.Value))
                    sb.Append($" (Value: {node.Value})");

                // 如果有 Params
                if (node.Params != null && node.Params.Count > 0)
                    sb.Append($" Params: [{string.Join(", ", node.Params)}]");

                // 如果有 Text (旁白或台词)
                if (!string.IsNullOrEmpty(node.Text))
                    sb.Append($" Text: \"{node.Text}\"");
            }
            else if (!string.IsNullOrEmpty(node.Type))
            {
                // 情况B：条件/逻辑节点 (如 "調查")
                sb.Append($"{prefix}[COND] {node.Type}");

                // 打印逻辑表达式 (Left Operator Right)
                if (!string.IsNullOrEmpty(node.Left))
                    sb.Append($" Logic: {node.Left} {node.Operator} {node.Right}");
            }
            else
            {
                sb.Append($"{prefix}[UNKNOWN NODE]");
            }

            sb.AppendLine(); // 换行

            // 2. 递归打印子节点 (Children)
            if (node.Children != null && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    // 递归调用，层级 +1
                    PrintNodeRecursive(child, level + 1, sb);
                }
            }
        }
    }

    public static class StoryBatchExporter
    {
        // 1. 逻辑匹配：匹配 "人物::" 开头的内容
        // 我们这里稍微放宽正则，把提取后的清洗工作交给 SanitizeName 函数
        private static readonly Regex RegexLogic = new Regex(@"(?<=人物::)([^\s""]+)", RegexOptions.Compiled);

        // 2. 文本匹配：匹配 "(名字)" 或 "(名字.属性)"
        private static readonly Regex RegexText = new Regex(@"\(([^).]+)(?:\.[^)]+)?\)", RegexOptions.Compiled);

        /// <summary>
        /// 【核心入口】批量处理所有事件，提取人名并一次性覆盖写入文件
        /// </summary>
        /// <param name="allEvents">加载好的所有事件列表</param>
        /// <param name="modId">你的Mod ID (即 SubModule.xml 中的 Id)</param>
        public static void ExportAllNamesBatch(List<StoryEvent> allEvents, string modId)
        {
            if (allEvents == null || allEvents.Count == 0) return;

            // 1. 准备容器 (HashSet 自动去重)
            HashSet<string> uniqueNames = new HashSet<string>();

            Debug.Print($"[StorySystem] 开始从 {allEvents.Count} 个事件中提取人名...");

            // 2. 内存遍历 (只在内存操作，不涉及IO)
            foreach (var evt in allEvents)
            {
                ExtractFromEvent(evt, uniqueNames);
            }

            // 3. 构建路径：Modules/ModName/ModuleData/ExtractedNames.txt
            string modulePath = ModuleHelper.GetModuleFullPath(modId);
            string targetDir = Path.Combine(modulePath, "ModuleData");
            string finalPath = Path.Combine(targetDir, "ExtractedNames.txt");

            // 确保目录存在
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            try
            {
                // 4. 排序并写入 (覆盖模式)
                var sortedNames = uniqueNames.OrderBy(n => n).ToList();
                File.WriteAllLines(finalPath, sortedNames);

                Debug.Print($"[StorySystem] 成功导出 {sortedNames.Count} 个唯一人名到: {finalPath}");
            }
            catch (Exception ex)
            {
                Debug.Print($"[StorySystem] 导出人名失败: {ex.Message}");
            }
        }

        // --- 内部辅助方法 ---

        private static void ExtractFromEvent(StoryEvent storyEvent, HashSet<string> names)
        {
            if (storyEvent == null) return;

            // 扫描 Trigger 参数
            if (storyEvent.Trigger?.Params != null)
            {
                foreach (var param in storyEvent.Trigger.Params)
                {
                    ProcessString(param, names, isLogic: true);
                }
            }

            // 递归扫描 Conditions
            ExtractFromNodesRecursive(storyEvent.Conditions, names);

            // 递归扫描 Script
            ExtractFromNodesRecursive(storyEvent.Script, names);
        }

        private static void ExtractFromNodesRecursive(List<ScriptNode> nodes, HashSet<string> names)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                // 1. 检查逻辑参数 (Params, Left, Right)
                if (node.Params != null)
                {
                    foreach (var p in node.Params) ProcessString(p, names, isLogic: true);
                }
                ProcessString(node.Left, names, isLogic: true);
                ProcessString(node.Right, names, isLogic: true);
                ProcessString(node.Value, names, isLogic: true); // 某些 value 可能是直接的人名字符串

                // 2. 检查文本 (Text)
                ProcessString(node.Text, names, isLogic: false);

                // 3. 递归子节点 (Children)
                ExtractFromNodesRecursive(node.Children, names);
            }
        }

        private static void ProcessString(string input, HashSet<string> names, bool isLogic)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            Regex targetRegex = isLogic ? RegexLogic : RegexText;
            MatchCollection matches = targetRegex.Matches(input);

            foreach (Match match in matches)
            {
                string rawName = match.Groups[1].Value; // 获取捕获组的内容
                string cleanName = SanitizeName(rawName);

                if (!string.IsNullOrEmpty(cleanName))
                {
                    names.Add(cleanName);
                }
            }
        }

        /// <summary>
        /// 【关键修复】清洗名字，切除属性后缀 (.xxx) 并过滤系统关键字
        /// </summary>
        private static string SanitizeName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            // 1. 切除后缀
            // 如果输入是 "鳥居強右衛門.主命狀態"，这里会截取为 "鳥居強右衛門"
            int dotIndex = rawName.IndexOf('.');
            if (dotIndex > 0)
            {
                rawName = rawName.Substring(0, dotIndex);
            }
            else if (dotIndex == 0)
            {
                // 如果以 . 开头 (如 .Value)，直接视为无效
                return null;
            }

            // 2. 去空格
            rawName = rawName.Trim();

            // 3. 黑名单过滤 (过滤掉看着像名字但其实是代码关键字的内容)
            if (IsSystemKeyword(rawName)) return null;

            return rawName;
        }

        private static bool IsSystemKeyword(string name)
        {
            // 在这里添加你发现的垃圾数据关键字
            var blockList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "value", "null", "target", "hero", "this", "event", "param", "val", "true", "false", "game"
            };

            return blockList.Contains(name);
        }
    }
}
