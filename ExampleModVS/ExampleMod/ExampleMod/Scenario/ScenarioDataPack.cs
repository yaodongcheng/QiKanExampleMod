using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本数据包（16b §3.3 数据包表）：定义（区域表/身份链 RankLadder/官职链/换算系数/基准年），加载期只读、不进存档。
    /// 物理文件 = ModuleData/ScenarioData/pack.json（补充包 07 产出；v1 缺文件 = 全部默认值 + 日志，机制先跑空表）。
    /// </summary>
    public static class ScenarioDataPack
    {
        // —— 基准年（Time::year / 14 时间轴；太阁 1560 时代 = 1560）——
        public static int BaseYear { get; private set; } = 1560;

        // —— 量纲换算系数（16b §4.4；系数 = 数据包可调，禁止硬编码语义值）——
        public static double KokudakaRatio { get; private set; } = 10.0;   // 石高 = 繁荣度 × 系数
        public static double LoyaltyConvert { get; private set; } = 2.0;   // 太阁忠诚度 0~100 ↔ 关系 −100~+100：关系 = (忠诚度−50) × 系数
        public static float IsNeighborDistance { get; private set; } = 40f; // isNeighbor 地图直线距离阈值（16b T2 待核实，数据包可调）

        // —— 区域表（Region::tk5_totomi → 区域定义；allControlled/region_attr_1 用）——
        private static Dictionary<string, RegionDef> _regions = new Dictionary<string, RegionDef>(StringComparer.Ordinal);

        // —— 身份链（17 RankLadder；带序枚举 >= <= 专用；缺表 = 等级比较返回不可判定（null））——
        private static Dictionary<string, int> _identityRanks = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>人物池种子（W3 初始化：三态 + 默认属性；数据包 heroes[]——全量 = 07 产出，`骑砍2太阁Mod表` 125 列）</summary>
        public class HeroSeedDef
        {
            public string StringId;                                   // 骑砍 StringId（铁律 20）
            public int BirthYear = -1;                                // 出生年（剧本年代；-1 = 未知）
            public int DeathYear = -1;                                // 终年（-1 = 未知/未定）
            public Dictionary<string, string> Attrs = new Dictionary<string, string>(System.StringComparer.Ordinal); // 默认属性（五维/技能/功勋/卡优先置位）
        }

        public static readonly List<HeroSeedDef> Heroes = new List<HeroSeedDef>();

        public class RegionDef
        {
            public string Id;                    // Region::tk5_totomi
            public string DisplayName;           // 远江（本地化走铁律 13；此字段 = 人读/构建期用）
            public List<string> Settlements = new List<string>();   // 区域含据点 StringId 集
        }

        public static void LoadAll()
        {
            var root = LocatePackDir();
            string path = root != null ? Path.Combine(root, "pack.json") : null;
            if (path == null || !File.Exists(path))
            {
                DebugLogger.Log($"[ScenarioDataPack] pack.json 缺失（v1 默认值运行）: {path}");
                return;
            }
            try
            {
                string clean = JsoncHelper.StripComments(File.ReadAllText(path, Encoding.UTF8));
                var rootObj = JObject.Parse(clean);
                if (rootObj["baseYear"] != null) BaseYear = (int)rootObj["baseYear"];
                if (rootObj["kokudakaRatio"] != null) KokudakaRatio = (double)rootObj["kokudakaRatio"];
                if (rootObj["loyaltyConvert"] != null) LoyaltyConvert = (double)rootObj["loyaltyConvert"];
                if (rootObj["isNeighborDistance"] != null) IsNeighborDistance = (float)rootObj["isNeighborDistance"];
                if (rootObj["regions"] is JArray ra)
                    foreach (var r in ra)
                    {
                        var d = new RegionDef { Id = (string)r["id"], DisplayName = (string)r["name"] ?? "" };
                        if (r["settlements"] is JArray ss)
                            foreach (var s in ss)
                            {
                                d.Settlements.Add((string)s);
                                _regionOfSettlement[(string)s] = d.Id;
                            }
                        _regions[d.Id] = d;
                    }
                if (rootObj["identityRanks"] is JObject ranks)
                    foreach (var kv in ranks)
                        _identityRanks[kv.Key] = (int)kv.Value;
                if (rootObj["heroes"] is JArray ha)
                    foreach (var h in ha)
                    {
                        var d = new HeroSeedDef { StringId = (string)h["stringId"], BirthYear = (int?)h["birthYear"] ?? -1, DeathYear = (int?)h["deathYear"] ?? -1 };
                        if (h["attrs"] is JObject a)
                            foreach (var kv in a) d.Attrs[kv.Key] = kv.Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(d.StringId)) Heroes.Add(d);
                    }
                DebugLogger.Log($"[ScenarioDataPack] pack.json 加载完成：regions={_regions.Count} ranks={_identityRanks.Count}");
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[ScenarioDataPack] pack.json 解析失败（默认值运行）: {e.Message}");
            }
        }

        /// <summary>区域表反向索引（settlementId → 区域 Id；所屬國 province 用）。数据包加载时构建。</summary>
        private static Dictionary<string, string> _regionOfSettlement = new Dictionary<string, string>(System.StringComparer.Ordinal);

        public static RegionDef GetRegion(string regionId) => _regions.TryGetValue(regionId, out var r) ? r : null;

        public static string FindRegionOfSettlement(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return null;
            return _regionOfSettlement.TryGetValue(settlementId, out var r) ? r : null;
        }

        /// <summary>身份链等级（17 RankLadder）；表外身份 = 无等级（null——带序纪律：只准 ==/!=）</summary>
        public static int? GetIdentityRank(string token) => _identityRanks.TryGetValue(token, out var v) ? v : (int?)null;

        private static string LocatePackDir()
        {
            string gameRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.FullName;
            if (gameRoot == null) return null;
            return Path.Combine(gameRoot, "Modules", "LivingWorldNpcs", "ModuleData", "ScenarioData");
        }
    }
}
