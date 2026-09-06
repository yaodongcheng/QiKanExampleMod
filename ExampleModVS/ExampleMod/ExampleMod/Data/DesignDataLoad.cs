using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;
using System.IO;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using System.Web;

namespace LivingWorldNpcs
{
    // 表格里面的每一行的通用写法，比如Hero里面的姓、生死年等
    public class DynamicRecord
    {
        // 存储核心：Key=英文变量名, Value=具体值
        private Dictionary<string, object> _data = new Dictionary<string, object>();

        public void SetField(string key, object value)
        {
            if (!_data.ContainsKey(key))
                _data.Add(key, value);
            else
                _data[key] = value;
        }

        // --- 通用取值方法 (带防崩处理) ---

        public string GetString(string key,string defaultValue = "")
        {
            return _data.ContainsKey(key) ? _data[key]?.ToString() : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (_data.ContainsKey(key) && _data[key] is int val) return val;
            return defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (_data.ContainsKey(key) && _data[key] is float val) return val;
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (_data.ContainsKey(key) && _data[key] is bool val) return val;
            return defaultValue;
        }

        // 获取列表类型 (对应 CSV 中的 "item1|item2")
        public List<string> GetList(string key)
        {
            if (_data.ContainsKey(key) && _data[key] is List<string> list) return list;
            return new List<string>();
        }
    }

    //代表一张csv表，比如hero.csv或者music.csv
    public class DataTable
    {
        // 主索引：ID -> 数据行
        private Dictionary<string, DynamicRecord> _records = new Dictionary<string, DynamicRecord>();

        // 反向索引：中文剧本名 -> ID
        private Dictionary<string, string> _scriptNameToId = new Dictionary<string, string>();

        public string TableName { get; private set; }

        public DataTable(string tableName)
        {
            TableName = tableName;
        }

        // 添加数据（加载器调用）
        public void AddRecord(string id, string scriptName, DynamicRecord record)
        {
            // 1. 存主数据
            if (!_records.ContainsKey(id))
            {
                _records.Add(id, record);
            }

            // 2. 存中文反向索引 (如果CSV里有 ScriptName 这一列)
            if (!string.IsNullOrEmpty(scriptName))
            {
                if (!_scriptNameToId.ContainsKey(scriptName))
                    _scriptNameToId.Add(scriptName, id);
            }
        }

        // --- 查询方法 ---

        // 方法 A: 此时你手里有 ID (引擎逻辑)
        public DynamicRecord GetByID(string id)
        {
            if (_records.TryGetValue(id, out var record))
                return record;
            return null; // 或者返回一个空对象防止报错
        }

        public string TransformPronoun2ID(string scriptName)
        {
            if (scriptName == "主人公")
                return Agent.Main.Character.StringId;

            return "";
        }


        public string GetIdByName(string scriptName)
        {
            //代词处理
            string checkPronounID = TransformPronoun2ID(scriptName);
            if(checkPronounID !="")
            {
                return checkPronounID;
            }

            if (_scriptNameToId.TryGetValue(scriptName, out var stringId))
            {
                return stringId;
            }
            return "";
        }
        // 方法 B: 此时你手里只有中文名 (剧本逻辑)
        public DynamicRecord GetByScriptName(string scriptName)
        {
            if (_scriptNameToId.TryGetValue(scriptName, out var id))
            {
                return GetByID(id);
            }
            return null;
        }

        // 获取所有数据（遍历用）
        public IEnumerable<DynamicRecord> GetAll()
        {
            return _records.Values;
        }
    }

    public static class CsvLoader
    {
        // 这是通用的加载函数，传入文件路径，返回一个表对象
        public static DataTable LoadTable(string filePath, string tableName)
        {
            DataTable table = new DataTable(tableName);

            if (!File.Exists(filePath))
            {
                // 可以在这里输出错误日志
                Debug.Print($"[Error] CSV not found: {filePath}");
                return table; // 返回空表
            }

            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 4) return table; // 格式不对，至少要有4行

            // 1. 解析表头
            // Split('\t') 假设你用Tab分隔，如果是逗号用 ','
            // 建议使用 Tab 或 分号 以避免文本内逗号冲突
            var keys = lines[0].Split(',');   // 第1行：英文Key
            // var comments = lines[2];       // 第3行：中文备注 (跳过)
            var types = lines[1].Split(',');  // 第2行：数据类型

            // 2. 解析数据行 (从第4行开始)
            for (int i = 3; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = line.Split(',');

                // 创建数据行对象
                var record = new DynamicRecord();
                string currentId = "";
                string currentScriptName = "";

                // 遍历每一列
                for (int c = 0; c < keys.Length; c++)
                {
                    if (c >= values.Length) break;

                    string key = keys[c].Trim();
                    string type = types[c].Trim().ToLower();
                    string rawValue = values[c].Trim();

                    object finalValue = rawValue;

                    // 类型转换逻辑
                    if (type == "int")
                        finalValue = int.TryParse(rawValue, out int iVal) ? iVal : 0;
                    else if (type == "float")
                        finalValue = float.TryParse(rawValue, out float fVal) ? fVal : 0f;
                    else if (type == "bool")
                        finalValue = bool.TryParse(rawValue, out bool bVal) ? bVal : false;
                    else if (type == "list")
                        finalValue = rawValue.Split('|').ToList(); // 竖线分隔数组

                    // 存入 Record
                    record.SetField(key, finalValue);

                    // 抓取特殊字段用于索引
                    if (key == "ID") currentId = rawValue;
                    if (key == "ScriptName") currentScriptName = rawValue;
                }

                // 将这一行加入 Table
                if (!string.IsNullOrEmpty(currentId))
                {
                    table.AddRecord(currentId, currentScriptName, record);
                }
            }

            return table;
        }
    }

    public static class GameDatabase
    {
        // === 分类入口 ===
        // 以后你想加新表，就在这里加一行
        public static DataTable Heroes { get; private set; }
        public static DataTable Music { get; private set; }

        public static DataTable TagPoint { get; private set; }
        public static DataTable Camera { get; private set; }

        public static DataTable Emotion { get; private set; }

        // Narrative/NpcSpeech/Dialogue/CommissionNarrative 已迁移到 XML 本地化系统
        // 不再通过 CSV 加载，直接走 LWNTextHelper + Language XML

        static string moduleName = "LivingWorldNpcs";
        static string directoryPath = Path.Combine(ModuleHelper.GetModuleFullPath(moduleName), "ModuleData\\DesignData");

        /// <summary>
        /// 内容包（如 TaikouContent）可在其 OnSubModuleLoad 中调用此方法，
        /// 将其 ModuleData/DesignData 目录下的 CSV 注入到 GameDatabase。
        /// Mod A 本身不引用任何内容包，保持单向依赖。
        /// </summary>
        public static void LoadTablesFromPath(string externalDesignDataPath)
        {
            if (string.IsNullOrEmpty(externalDesignDataPath) || !Directory.Exists(externalDesignDataPath))
                return;

            Heroes = CsvLoader.LoadTable(Path.Combine(externalDesignDataPath, "Hero.csv"), "Heroes");
            Music  = CsvLoader.LoadTable(Path.Combine(externalDesignDataPath, "Music.csv"), "Music");
            TagPoint = CsvLoader.LoadTable(Path.Combine(externalDesignDataPath, "TagPoint.csv"), "TagPoint");
            Emotion = CsvLoader.LoadTable(Path.Combine(externalDesignDataPath, "Emotion.csv"), "Emotion");

            // Narrative.csv / NpcSpeech.csv 已废弃，文本迁移到 XML 本地化系统
        }

        // === 初始化：一次性加载所有表 ===
        public static void Initialize()
        {
            // Camera.csv 是唯一泛用表，始终从 Mod A 加载
            Camera = CsvLoader.LoadTable(Path.Combine(directoryPath, "Camera.csv"), "Camera");

            // Narrative/NpcSpeech 已迁移到 XML，不再加载

            // 织丰内容表初始化为空，等待内容包注入
            Heroes   = new DataTable("Heroes");
            Music    = new DataTable("Music");
            TagPoint = new DataTable("TagPoint");
            Emotion  = CsvLoader.LoadTable(Path.Combine(directoryPath, "Emotion.csv"), "Emotion");

            // 内容包注入（通用）：config.json "DesignDataModuleId" 指定内容包模块 Id（如 Taikou），
            // 非空则把该模块 ModuleData/DesignData 下的 CSV（Hero/Music/TagPoint/Emotion）注入 GameDatabase。
            // 与 Core/SplashVideoReplacePatch.cs 同款：功能逻辑留在 Mod A，内容包零 DLL。
            string designDataModuleId = Settings.Instance.DesignDataModuleId;
            if (!string.IsNullOrEmpty(designDataModuleId))
            {
                try
                {
                    string injectPath = Path.Combine(
                        TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath(designDataModuleId),
                        "ModuleData", "DesignData");
                    if (Directory.Exists(injectPath))
                    {
                        LoadTablesFromPath(injectPath);
                        DebugLogger.Log($"[DesignData] 内容包注入成功：{designDataModuleId} ({injectPath})");
                    }
                    else
                    {
                        DebugLogger.Log($"[DesignData] 内容包未就绪（目录不存在），保持空表：{injectPath}");
                    }
                }
                catch (System.Exception ex)
                {
                    DebugLogger.Log($"[DesignData] 内容包注入失败，保持空表：{designDataModuleId} ({ex.GetType().Name})");
                }
            }
        }

    }
}
