using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    // 这是一个战役行为，它会自动随游戏保存和加载
    // 🔴 2026-08-30 升级（W1 数据底座）：对齐 11 存档键登记表——SyncData 键名 = lwn_scn_attr / lwn_scn_state；
    //   保留 16b §3.1 红线（禁 JSON 字符串键、单值长上限）；键总量上限放宽到 10 万级（16b：2000 人物×8 格 ≈ 2 万格，
    //   超 5 万格才告警），自动淘汰改为告警（🎲 剧本剧情状态被淘汰 = 永久丢失，禁止——16b「清理只走控制台人工确认」）。
    public class GlobalVariableBehavior : CampaignBehaviorBase
    {
        // 存储结构： 主体ID -> { 属性名 -> 值 }
        // 例如: "hero_oda_nobunaga" -> { "親密度": "80", "出現標誌": "未出現" }
        // 🔴 剧本侧写入 key 规范 = 「域:StringId」（铁律 20，见 ScenarioAttrStore）——旧 Story 引擎无前缀用法继续可用（key 空间不相交）
        private Dictionary<string, Dictionary<string, string>> _extendedProperties = new Dictionary<string, Dictionary<string, string>>();

        // 全局变量，比如 "狀況::劇本"
        private Dictionary<string, string> _globalStates = new Dictionary<string, string>();

        private Dictionary<string, NPCProfile> _npcProfiles = new Dictionary<string, NPCProfile>();
        //可能还有一些非人主体的，或许放在_extendedProperties

        public override void RegisterEvents()
        {
            // 不需要注册事件，只做存储
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 存档键名 = 11-存档与配置.md 登记表（唯一权威；改键名 = 改登记表 + 本处同步，禁止单边）
            dataStore.SyncData("lwn_scn_attr", ref _extendedProperties);
            dataStore.SyncData("lwn_scn_state", ref _globalStates);

          //  dataStore.SyncData("_npcProfiles", ref _npcProfiles);

        }

        // --- 公共访问接口 ---

        public string GetExtendedProperty(string subjectId, string propertyKey)
        {
            if (_extendedProperties.ContainsKey(subjectId) && _extendedProperties[subjectId].ContainsKey(propertyKey))
            {
                return _extendedProperties[subjectId][propertyKey];
            }
            return null; // 返回 null 代表没存过
        }
        public NPCProfile GetNPCProfile(string heroId)
        {
            if (_npcProfiles.ContainsKey(heroId))
            {
                return _npcProfiles[heroId];
            }
            return null;
        }
        public NPCProfile GetOrCreateNPCProfile(string heroId)
        {
            if (!_npcProfiles.ContainsKey(heroId))
            {
                var newProfile = new NPCProfile { StringId = heroId };
                _npcProfiles[heroId] = newProfile;
            }
            return _npcProfiles[heroId];
        }
        public void SetExtendedProperty(string subjectId, string propertyKey, string value)
        {
            if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(propertyKey)) return;

            // 单值上限（防存档 Strings 表 short 溢出）：LLM/外部写入可能无限长。
            // 1000 字符中文 ≈ 3000 字节 < 32767 安全；截断只影响单个属性值，调用方全走 ?. 传播。
            if (value != null && value.Length > MaxExtendedValueChars)
            {
                DebugLogger.Log($"[ExtPropsGuard] {subjectId}.{propertyKey} 超长 {value.Length} 字符 → 截断 {MaxExtendedValueChars}");
                value = value.Substring(0, MaxExtendedValueChars);
            }

            if (!_extendedProperties.ContainsKey(subjectId))
            {
                // 🔴 2026-08-30（W1）：总量上限评估从「淘汰最老」改为「告警」——自动淘汰 = 剧情状态永久丢失（16b §3.1 禁止）
                if (TotalPropertyKeys() >= MaxExtendedTotalKeys)
                {
                    DebugLogger.Log($"[ExtPropsGuard] 🔴 剧本属性总量超上限 {MaxExtendedTotalKeys}（现 {TotalPropertyKeys()}）——不再自动淘汰，调用方排查（custom.scn_status）");
                }
                _extendedProperties[subjectId] = new Dictionary<string, string>();
            }
            _extendedProperties[subjectId][propertyKey] = value;
        }

        private int TotalPropertyKeys()
        {
            int n = 0;
            foreach (var s in _extendedProperties) n += s.Value?.Count ?? 0;
            return n;
        }

        /// <summary>清除全部剧本状态（11 存档纪律：新档挂 ResetAllCampaignState；本行为实例新档自动重建，这是双保险）</summary>
        public void ClearAll()
        {
            _extendedProperties.Clear();
            _globalStates.Clear();
        }

        // --- 全局状态读写（旗标/计数器/变量/全局槽；16 §一 三档 + 16b §3.2 旗标计数仓） ---

        public string GetGlobalState(string key)
        {
            return _globalStates.TryGetValue(key, out var v) ? v : null;
        }

        public void SetGlobalState(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _globalStates[key] = value;
        }

        /// <summary>枚举全部全局状态键（ScenarioCampaignBehavior 计数器 +1 用；复制快照）</summary>
        public List<string> EnumerateGlobalStateKeys()
        {
            return new List<string>(_globalStates.Keys);
        }

        /// <summary>扩展属性总键数上限（所有 subject 的 property 之和；10 万级 = 16b 估算 2 万格 × 安全余量；超限 = 告警不淘汰）</summary>
        private const int MaxExtendedTotalKeys = 100000;
        /// <summary>单属性值长度上限（字符数；1000 中文字 ≈ 3000 字节，远低于 Strings 表 32767 上限）</summary>
        private const int MaxExtendedValueChars = 1000;

        // 获取单例的快捷方式
        public static GlobalVariableBehavior Instance
        {
            get { return Campaign.Current?.GetCampaignBehavior<GlobalVariableBehavior>(); }
        }

        public string DumpAllVariables()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"========== 剧本变量导出 {DateTime.Now} ==========");

            // 1. 临时上下文 (StoryContext中的)
            // 既然要Dump，最好把 StoryContext 里的 LocalVariables 也打印出来
            sb.AppendLine("\n[临时上下文 (Local Variables)]");
            var locals = StoryContext.Instance.LocalVariables; // 假设你在 StoryContext 把这个属性公开了 get
            if (locals == null || locals.Count == 0)
                sb.AppendLine("  (无)");
            else
                foreach (var kv in locals) sb.AppendLine($"  {kv.Key} = {kv.Value}");

            // 2. 全局状态
            sb.AppendLine("\n[全局状态 (Global States)]");
            if (_globalStates.Count == 0)
                sb.AppendLine("  (无)");
            else
                foreach (var kv in _globalStates) sb.AppendLine($"  {kv.Key} = {kv.Value}");

            // 3. 人物/对象 扩展属性
            sb.AppendLine("\n[扩展属性 (Extended Properties)]");
            if (_extendedProperties.Count == 0)
                sb.AppendLine("  (无)");
            foreach (var subject in _extendedProperties)
            {
                // 可以在这里把 ID 转换回中文名方便阅读，如果不嫌麻烦的话
                sb.AppendLine($"> 主体: {subject.Key}");
                foreach (var prop in subject.Value)
                {
                    sb.AppendLine($"    - {prop.Key} : {prop.Value}");
                }
            }
            /*
            sb.AppendLine("\n[AI 深度档案 (NPC Profiles)]");
            foreach (var kv in _npcProfiles)
            {
                sb.AppendLine($"> 英雄ID: {kv.Key}, 目标: {kv.Value.CurrentGoal}, 事件数: {kv.Value.KnownEvents.Count}");
            }
            */
            return sb.ToString();
        }
    }

    // 存档定义，必须加这个否则 SaveSystem 不知道怎么存字典
    public class SaveDefiner : SaveableTypeDefiner
    {
        public SaveDefiner() : base(123456789) // 随便找个唯一的ID
        {
        }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(Dictionary<string, string>));
            ConstructContainerDefinition(typeof(Dictionary<string, Dictionary<string, string>>));
            // 【新增】注册 NPCProfile 的字典
         //   ConstructContainerDefinition(typeof(Dictionary<string, NPCProfile>));
            // 【新增】注册 NPCProfile 内部用到的字典
        //    ConstructContainerDefinition(typeof(Dictionary<string, int>));
        }

        protected override void DefineClassTypes()
        {
            // 给每个类分配一个在这个 Definer 内部唯一的 ID
            // ── 存档修复阶段二：全部自定义存档类型注册（2026-08-03，根因=实例进存档图无定义即崩）──

            // 1. 注册 GeneratedStoryResult (生成结果)
            AddClassDefinition(typeof(LivingWorldNpcs.GeneratedStoryResult), 10);
            // 2. 注册 CommissionHubIssue (犯罪委托信号 Issue —— 已复现并验证修复)
            AddClassDefinition(typeof(LivingWorldNpcs.CommissionHubIssue), 15);
            // 3. Quest 体系
            // QuestData/GenericQuest 已标记 Obsolete（统一到 CommissionQuest）——
            // 但存档注册必须保留：GenericQuest 创建路径仍存活（QuestManager 控制台命令），
            // 且旧档可能含这些类型的实例，不注册会导致读档时对象解析为 null。
#pragma warning disable CS0618
            AddClassDefinition(typeof(LivingWorldNpcs.QuestData), 11);
            AddClassDefinition(typeof(LivingWorldNpcs.GenericQuest), 12);
#pragma warning restore CS0618
            AddClassDefinition(typeof(LivingWorldNpcs.CommissionQuest), 13);
            AddClassDefinition(typeof(LivingWorldNpcs.CommissionData), 14);
            // 4. 自定义 PartyComponent（quest 护送/追击、报复部队、控制台事件）
            AddClassDefinition(typeof(LivingWorldNpcs.SafeLordPartyComponent), 16);
            AddClassDefinition(typeof(LivingWorldNpcs.CustomPartyComponent), 17);
        }

        protected override void DefineStructTypes()
        {
            // CommissionIssueContext：CommissionHubIssue._context 的载体（读档后 Title 还原用）
            AddStructDefinition(typeof(LivingWorldNpcs.CommissionIssueContext), 18);
        }

        protected override void DefineEnumTypes()
        {
            // QuestType 已标记 Obsolete，但 QuestData.Type 字段仍在用（存档注册必须保留）
#pragma warning disable CS0618
            AddEnumDefinition(typeof(LivingWorldNpcs.QuestType), 20);
#pragma warning restore CS0618
            AddEnumDefinition(typeof(LivingWorldNpcs.CommissionCategory), 21);
            AddEnumDefinition(typeof(LivingWorldNpcs.ResolutionPath), 22);
            AddEnumDefinition(typeof(LivingWorldNpcs.CommissionTier), 23);
            AddEnumDefinition(typeof(LivingWorldNpcs.CommissionGrade), 24);
            // EventStage：CommissionIssueContext.CrimeEventStage 依赖（2026-08-03 补）
            AddEnumDefinition(typeof(LivingWorldNpcs.EventStage), 25);
        }
    }

    public static class VariableManager
    {
        // 剧本中文属性 -> 内部逻辑的映射（如果需要特殊处理）
        // 大部分情况下，你可以直接把中文当Key存进去，只要保持一致即可。
        // 但对于原生属性，需要 switch-case 处理。

        public static string GetValue(string rawKey)
        {

            // 0. 【新增】先检查是否是脚本上下文变量 (如 "人物B", "人物A")
            // 如果 rawKey 本身就是 "人物B"，直接查表返回其代表的真实ID
            string localVal = StoryContext.Instance.GetLocalVariable(rawKey);
            if (localVal != null)
            {
                // 注意：如果 "人物B" 存的是 ID，这里可能需要递归处理属性
                // 但通常 "人物B" 是作为前缀使用的，比如 "人物B.亲密度"
                // 这种情况会在下方的 Contains("::") 逻辑或者 Split 逻辑中处理吗？
                // 脚本里通常写的是 "人物B" 直接作为参数，或者隐含。
                // 骑砍里我们需要把它转换。
                return localVal;
            }

            // 1. 解析 Key，例如 "人物::織田信長.親密度"
            // 你的剧本格式很工整，用 Split 拆分
            if (rawKey.Contains("::"))
            {
                var mainParts = rawKey.Split(new[] { "::" }, System.StringSplitOptions.RemoveEmptyEntries);
                string type = "";
                string rest = "";
                if (mainParts.Length == 2)
                {
                     type = mainParts[0]; // "人物", "町", "狀況"
                     rest = mainParts[1]; // "織田信長.親密度" 或 "劇本"
                }
                else if(mainParts.Length == 1)
                {
                    rest = mainParts[0];
                }

                //这里之后需要补充存储一种情况，就是没有type的
               


                if (type == "狀況")
                {
                    // 先查 GlobalBehavior
                    string val = GlobalVariableBehavior.Instance?.GetExtendedProperty("GLOBAL", rest);
                    if (val != null) return val;

                    // 再查原生环境
                    if (rest == "年") return V.GetStartTime().GetYear.ToString();                    
                    //if (rest == "月") return Campaign.Current.CampaignStartTime.GetMonthOfYear.ToString();
                    return "0";
                }
                else if (type == "人物")
                {
                    var subParts = rest.Split('.');
                    string scriptName = subParts[0];
                    string property = subParts.Length > 1 ? subParts[1] : "";

                    // 1. 找到对应的 Hero 对象
                    // 建议这里做一个缓存 Name -> StringID 的表，或者直接用 StringID
                    string stringId = GameDatabase.Heroes.GetIdByName(scriptName);
                    Hero hero = StoryContext.Instance.FindHeroById(stringId);

                    if (hero == null) return "0";

                    // 2. 路由：原生属性 vs 扩展属性
                    return ResolveHeroProperty(hero, property);
                }
                // 这里可以继续加 "城::", "大名家::" 等
            }

            // 纯临时变量名处理
            return GlobalVariableBehavior.Instance?.GetExtendedProperty("Variables", rawKey) ?? "0";
        }

        public static bool SetValue(string rawKey, string value)
        {

            //检查value是不是变量
            string realValue = value;
            if (value == "ａ")
                realValue = VariableManager.GetValue(value);
            //后面继续扩充



            int keySeparatorIndex = rawKey.IndexOf("::", System.StringComparison.Ordinal);

            // 情况 A: 纯变量 (没有 ::)
            if (keySeparatorIndex == -1)
            {
                GlobalVariableBehavior.Instance?.SetExtendedProperty("Variables", rawKey, realValue);
                return true;
            }
            string type = rawKey.Substring(0, keySeparatorIndex);
            // rest = "柳生石舟齋.卡持有(卡::無刀取)" —— 完整保留了后续内容
            string rest = rawKey.Substring(keySeparatorIndex + 2);

          

            if (type == "狀況")
            {
                GlobalVariableBehavior.Instance?.SetExtendedProperty("GLOBAL", rest, realValue);
                return true;
            }
            else if (type == "人物")
            {
                int dotIndex = rest.IndexOf('.');

                string scriptName;
                string property;
                if (dotIndex > 0)
                {
                    scriptName = rest.Substring(0, dotIndex); // "柳生石舟齋"
                    property = rest.Substring(dotIndex + 1);   // "卡持有(卡::無刀取)"
                }
                else
                {
                    scriptName = rest;
                    property = string.Empty;
                }


                string stringId = GameDatabase.Heroes.GetIdByName(scriptName);
                Hero hero = StoryContext.Instance.FindHeroById(stringId);
                if (hero != null)
                {
                    ApplyHeroProperty(hero, property, realValue);
                    return true;
                }
                else
                {
                    //剧本人物还没有完全导入，得导入了
                    return false;
                }
            }
            
            // 纯变量
            GlobalVariableBehavior.Instance?.SetExtendedProperty("Variables", rawKey, realValue);
            return true;
        }

        // === 核心逻辑：原生 vs 扩展 路由 ===
        public static string GetHeroProperty(string heroId, string property)
        {
            Hero hero = StoryContext.Instance.FindHeroById(heroId);
            if (hero == null) return "0";
            return ResolveHeroProperty(hero, property);
        }
        private static string ResolveHeroProperty(Hero hero, string property)
        {
            // A. 优先匹配骑砍2 原生属性
            switch (property)
            {
                case "持有金": return hero.Gold.ToString();
                case "親密度": return hero.GetRelation(Hero.MainHero).ToString();
                case "認識標誌": return hero.HasMet ? "已認識" : "未認識";
                case "Reconized": return hero.HasMet ? "True" : "False";
                case "出現標誌": return hero.IsActive ? "已出現" : "未出現";
                case "Appeared": return hero.IsActive ? "True" : "False";
                case "妻": return hero.Spouse?.StringId ?? "無效";
                case "Wife": return hero.Spouse?.StringId ?? "None";
                case "年齡": return ((int)hero.Age).ToString();
                case "所在": return hero.CurrentSettlement?.Name.ToString() ?? "野外";
                case "身分": return hero.IsLord ? "大名" : "浪人"; // 这里需要根据MOD逻辑细化
                case "死亡": return hero.IsDead ? "1" : "0";
                    // ... 添加更多原生映射
            }

            // B. 如果不是原生属性，去查扩展数据库 (GlobalVariableBehavior)
            // 这里直接用中文 "親密度", "出現標誌" 当 Key 存进去，完全没问题
            string val = GlobalVariableBehavior.Instance?.GetExtendedProperty(hero.StringId, property);

            // C. 如果数据库里也没有，读 CSV 里的静态默认值
            if (val == null)
            {
                // 假设你有一个 GameDatabase.Heroes 表
                var record = GameDatabase.Heroes.GetByID(hero.StringId);
                if (record != null)
                {
                    return record.GetString(property); // 从CSV读取默认初始值
                }
                return "0"; // 默认缺省值
            }

            return val;
        }

        public static void SetV(string stringId,string property,string value)
        {
            GlobalVariableBehavior.Instance?.SetExtendedProperty(stringId, property, value);
        }
        public static string GetV(string stringId, string property)
        {
            return GlobalVariableBehavior.Instance?.GetExtendedProperty(stringId, property);
        }

        public static void  ApplyHeroProperty(string  heroId, string property, string value)
        {
             //获取hero，然后调用ApplyHeroProperty
             Hero hero = StoryContext.Instance.FindHeroById(heroId);
            if (hero != null)
            {
                ApplyHeroProperty(hero, property, value);
            }
        }
        private static void ApplyHeroProperty(Hero hero, string property, string value)
        {
            // A. 尝试写入原生属性
            switch (property)
            {
                case "持有金":
                case "Gold":
                    // 剧本绝对赋值（上帝指令），统一走 AgentControlHelper 归口
                    int targetGold = int.Parse(value);
                    AgentControlHelper.SetGold(hero, targetGold);
                    return;
                // 注意：大部分原生属性可能是只读的，或者需要特殊函数修改
                case "親密度":
                case "Relation":
                    //修改hero对主角的亲密度
                    int targetRelation = int.Parse(value);
                    ChangeRelationAction.ApplyPlayerRelation(hero, targetRelation, true, true);
                    return;
                case "認識標誌":
                case "Recognized":
                    bool targetRecognized = value == "已認識" ? true : false;
                    hero.SetHasMet();
                    //一般来说这个是不可逆的，所以这里不做处理                    
                    return;
                case "出現標誌":
                case "Appeared":
                    bool targetAppeared = value == "已出現" ? true : false;
                    if(targetAppeared)
                        hero.ChangeState(Hero.CharacterStates.Active);
                    else
                        hero.ChangeState(Hero.CharacterStates.Disabled);
                    return;
                case "妻":
                case "Wife":
                     Hero targetSpouse = StoryContext.Instance.FindHeroById(value);
                     hero.Spouse = targetSpouse;
                     return;
                default:
                    break;
            }

            // B. 写入扩展数据库
            GlobalVariableBehavior.Instance?.SetExtendedProperty(hero.StringId, property, value);
        }
    }
}
