using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 属性读链（16b 三层：T1 引擎直读 / T2 换算读 / T3 外置仓读；链尾空值兜底）
    /// + 函数 15（16 §三 + 16b §六）。
    /// 🔴 纪律：任何一步失败 → Null（不抛异常，铁律 1/2）；Null 参与比较 = 假（01 兜底）；
    ///    未实现的 T2 换算 = ReportUndecidable（custom.dsl_validate 汇总，实测后回填落地）。
    /// </summary>
    public static class AttributeResolver
    {
        public static IScenarioQuery Query = new ScenarioQuery();

        /// <summary>本次求值累计的不可判定项（validator/调试用，每次 EvaluateDetailed 结算后清空）</summary>
        public static List<string> UndecidableLog = new List<string>();

        public static void ReportUndecidable(string why)
        {
            if (UndecidableLog.Count < 64) UndecidableLog.Add(why);
        }

        // ============================================================
        // 函数 15（参数 = DslNode 列表——函数自行求值/取引用）
        // ============================================================
        public static DslValue CallFunction(string name, List<DslNode> args, ScenarioContext ctx)
        {
            var a = args ?? new List<DslNode>();

            switch (name)
            {
                case "exists":
                    return DslValue.FromBool(a.Count >= 1 && a[0].Eval(ctx) != DslValue.Null && !a[0].Eval(ctx).IsNull);

                case "atWar":
                {
                    var k1 = FindKingdom(RefOrNull(a, 0, ctx)); var k2 = FindKingdom(RefOrNull(a, 1, ctx));
                    return DslValue.FromBool(k1 != null && k2 != null && k1.IsAtWarWith(k2));
                }
                case "isAllied":
                {
                    // 16b T2 待核实（1.2.12/1.5.1 DLL 无 IsAlliedWith 命中 2026-08-30）→ v1 = 非交战近似 + 日志
                    var k1 = FindKingdom(RefOrNull(a, 0, ctx)); var k2 = FindKingdom(RefOrNull(a, 1, ctx));
                    bool allied = k1 != null && k2 != null && !k1.IsAtWarWith(k2);
                    ReportUndecidable("isAllied: v1 用「非交战」近似（引擎同盟 API 待核实）");
                    return DslValue.FromNumber(allied ? 1 : 0);
                }
                case "hasMet":
                {
                    var h1 = FindHero(RefOrNull(a, 0, ctx)); var h2 = FindHero(RefOrNull(a, 1, ctx));
                    // 引擎 API：Hero.HasMet bool 属性（出现过）（16 §三 hasMet 登记口径；双向已见 v1）
                    return DslValue.FromBool(h1 != null && h2 != null && h1.HasMet && h2.HasMet);
                }
                case "hasRelation":
                {
                    if (a.Count < 2) return DslValue.FromBool(false);
                    var h1 = FindHero(RefOrNull(a, 0, ctx)); var h2 = FindHero(RefOrNull(a, 1, ctx));
                    long want = a.Count >= 4 && a[3].Eval(ctx).Kind == DslValueKind.Number ? a[3].Eval(ctx).Num : a[a.Count - 1].Eval(ctx).Num;
                    // 参数形态（TK5 亲密度比较）：(hero, hero, op, 数值)——v1 按「>= 数值」
                    return DslValue.FromBool(h1 != null && h2 != null && h1.GetRelation(h2) >= want);
                }
                case "relation":
                {
                    var k1 = FindKingdom(RefOrNull(a, 0, ctx)); var k2 = FindKingdom(RefOrNull(a, 1, ctx));
                    if (k1 == null || k2 == null) { ReportUndecidable("relation: 王国引用缺失"); return DslValue.Null; }
                    // 16b T2 归一 v1（太阁量纲 0~100）：交战 -100 / 非交战 0 / 同盟 100（详情数值 API 待核实）
                    if (k1.IsAtWarWith(k2)) return DslValue.FromNumber(-100);
                    return DslValue.FromNumber(0);
                }
                case "sameSettlement":
                {
                    var h1 = FindHero(RefOrNull(a, 0, ctx)); var h2 = FindHero(RefOrNull(a, 1, ctx));
                    return DslValue.FromBool(h1 != null && h2 != null && h1.CurrentSettlement != null && h1.CurrentSettlement == h2.CurrentSettlement);
                }
                case "isNeighbor":
                {
                    var s1 = FindSettlement(RefOrNull(a, 0, ctx)); var s2 = FindSettlement(RefOrNull(a, 1, ctx));
                    if (s1 == null || s2 == null) return DslValue.FromBool(false);
                    // 16b T2 待核实：地图坐标直线距离 < 阈值（数据包 IsNeighborDistance，默认 40）
                    float d = (s1.Position2D - s2.Position2D).Length;
                    return DslValue.FromBool(d <= ScenarioDataPack.IsNeighborDistance);
                }
                case "allControlled":
                {
                    var region = ScenarioDataPack.GetRegion(RefOrNull(a, 0, ctx));
                    var clan = FindClan(RefOrNull(a, 1, ctx));
                    if (region == null || clan == null || region.Settlements.Count == 0)
                    { ReportUndecidable("allControlled: 区域表缺失（数据包）"); return DslValue.FromBool(false); }
                    return DslValue.FromBool(region.Settlements.All(sid => FindSettlement(sid)?.OwnerClan == clan));
                }
                case "region_attr_1":
                {
                    // 16b §六：区域支配大名家 = 据点归属多数派（数据包区域表）
                    var region = ScenarioDataPack.GetRegion(RefOrNull(a, 0, ctx));
                    if (region == null || region.Settlements.Count == 0) { ReportUndecidable("region_attr_1: 区域表缺失"); return DslValue.FromBool(false); }
                    var counts = new Dictionary<Clan, int>();
                    foreach (var sid in region.Settlements)
                    {
                        var s = FindSettlement(sid);
                        if (s?.OwnerClan != null) { counts[s.OwnerClan] = counts.TryGetValue(s.OwnerClan, out var v) ? v + 1 : 1; }
                    }
                    var top = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
                    return top.Key == null ? DslValue.FromBool(false) : DslValue.FromString("Clan::" + top.Key.StringId);
                }
                case "canMove":
                case "canAttack":
                    // 16b T2 待核实（02 锁军后补）：v1 = 恒真（未锁先 = 恒可，00 v3 §2.2）
                    ReportUndecidable(name + ": v1 恒真（02 锁军未接入）");
                    return DslValue.FromBool(true);
                case "hasCard":
                {
                    var h = FindHero(RefOrNull(a, 0, ctx)); var card = RefOrNull(a, 1, ctx);
                    if (h == null || card == null) return DslValue.FromBool(false);
                    return DslValue.FromBool(ScenarioAttrStore.GetAttr("Hero::" + h.StringId, "card_" + card) == "1");
                }
                case "canPromote":
                    ReportUndecidable("canPromote: 桩（17 晋升链未接入）");
                    return DslValue.FromBool(false);
                case "unknown_2":
                case "unknown_8":
                    // 16b §六：语料样本不足 → 恒假 + 告警
                    ReportUndecidable(name + ": 恒假（16b 裁定）");
                    return DslValue.FromBool(false);
            }
            ReportUndecidable("未知函数: " + name);
            return DslValue.FromBool(false);
        }

        private static string RefOrNull(List<DslNode> a, int i, ScenarioContext ctx)
        {
            if (i >= a.Count) return null;
            var v = a[i].Eval(ctx);
            return v.IsNull ? null : v.Str;
        }

        // ============================================================
        // 域读链（T1 直读 / T2 换算 / T3 仓读——链尾空值）
        // ============================================================
        private class ScenarioQuery : IScenarioQuery
        {
            public DslValue Resolve(string domain, string id, string attr)
            {
                if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(id)) return DslValue.Null;
                try
                {
                    switch (domain)
                    {
                        case "Hero::": return ResolveHero(id, attr);
                        case "Clan::": return ResolveClan(id, attr);
                        case "Settlement::": return ResolveSettlement(id, attr);
                        case "Faction::": case "Kingdom::": return ResolveFaction(id, attr);
                        case "Region::": return ScenarioDataPack.GetRegion(id) != null ? DslValue.FromString(domain + id) : DslValue.Null;
                        case "Time::": return ResolveTime(id, attr);
                        case "Flag::": return DslValue.FromBool(ScenarioStateStore.GetFlag(id));
                        case "Variable::": { var v = ScenarioStateStore.GetVariable(id); return v == null ? DslValue.Null : DslValue.FromString(v); }
                        case "GlobalSlot::": { var v = ScenarioStateStore.GlobalGet(id); return v == null ? DslValue.Null : DslValue.FromString(v); }
                        case "Event::": return DslValue.FromBool(ScenarioStateStore.GetRaw("Event::" + id) == "done"); // 调度器自动记 Event::X.done
                        case "Army::": return ResolveArmy(id, attr);
                        case "Org::": case "Card::": case "Item::":
                            ReportUndecidable("域 " + domain + " 对象引用（数据包/07 内容）");
                            return DslValue.Null;
                    }
                    ReportUndecidable("未知域: " + domain);
                    return DslValue.Null;
                }
                catch (Exception e)
                {
                    ReportUndecidable($"{domain}{id}.{attr} 求值异常: {e.Message}");
                    return DslValue.Null;
                }
            }

            private DslValue ResolveHero(string id, string attr)
            {
                var hero = FindHero(id);
                if (attr == null) return hero != null ? DslValue.FromString("Hero::" + hero.StringId) : DslValue.Null;
                if (hero == null)
                {
                    // 对象不存在（时代未登场/织丰缺失/其他 mod 屏蔽）——孤儿键：仓读保留不删（16b §3.1）
                    var orphan = ScenarioAttrStore.GetAttr("Hero::" + id, attr);
                    return orphan == null ? DslValue.Null : DslValue.FromString(orphan);
                }
                switch (attr)
                {
                    case "alive": return DslValue.FromBool(hero.IsAlive);
                    case "gender": return DslValue.FromString(hero.IsFemale ? "female" : "male");
                    case "age": return DslValue.FromNumber((long)hero.Age);
                    case "clan": return hero.Clan != null ? DslValue.FromString("Clan::" + hero.Clan.StringId) : DslValue.Null;
                    case "kingdom": return hero.MapFaction is Kingdom k ? DslValue.FromString(k.StringId) : DslValue.Null;
                    case "home": return hero.HomeSettlement != null ? DslValue.FromString("Settlement::" + hero.HomeSettlement.StringId) : DslValue.Null;
                    case "settlement": return hero.CurrentSettlement != null ? DslValue.FromString("Settlement::" + hero.CurrentSettlement.StringId) : DslValue.Null;
                    case "party": return hero.PartyBelongedTo != null ? DslValue.FromString("Army::" + hero.PartyBelongedTo.StringId) : DslValue.Null;
                    case "faction": return hero.MapFaction != null ? DslValue.FromString(hero.MapFaction.StringId) : DslValue.Null;
                    case "identity": return AttrOrNull("Hero::" + hero.StringId, "identity");   // T3 17 身份链（枚举 token）
                    case "quest_state": return AttrOrNull("Hero::" + hero.StringId, "quest_state") ?? DslValue.FromNumber(0);  // 13 主命状态
                    case "superior": return hero.Clan?.Leader != null ? DslValue.FromString("Hero::" + hero.Clan.Leader.StringId) : DslValue.Null;   // T2 v1：所屬上司 = 家主（16b：从属家族取宗主——v1 合并，注释）
                    case "available": return DslValue.FromBool(hero.IsAlive && !hero.IsPrisoner); // T2 组合 v1：活着 + 非俘虏（失踪/外出待实装）
                    case "relation_to": return DslValue.Null; // 关系走函数（hasRelation）
                    default:
                        return AttrOrNull("Hero::" + hero.StringId, attr);   // T3 外置仓（技能/功勋/标志位……一位一字段）
                }
            }

            private DslValue ResolveClan(string id, string attr)
            {
                var clan = FindClan(id);
                if (attr == null) return clan != null ? DslValue.FromString("Clan::" + clan.StringId) : DslValue.Null;
                if (clan == null) { ReportUndecidable($"Clan::{id} 不存在"); return DslValue.Null; }
                switch (attr)
                {
                    case "kingdom": return clan.Kingdom != null ? DslValue.FromString(clan.Kingdom.StringId) : DslValue.Null;
                    case "leader": return clan.Leader != null ? DslValue.FromString("Hero::" + clan.Leader.StringId) : DslValue.Null;
                    case "home": return clan.HomeSettlement != null ? DslValue.FromString("Settlement::" + clan.HomeSettlement.StringId) : DslValue.Null;
                    case "power": return DslValue.FromNumber((long)clan.TotalStrength);                       // T2 归一 v1（原始强度值；量纲 16b §4.4 待核实改造）
                    case "settlements": return DslValue.FromNumber(clan.Settlements.Count);
                    default: return AttrOrNull("Clan::" + clan.StringId, attr);
                }
            }

            private DslValue ResolveSettlement(string id, string attr)
            {
                var s = FindSettlement(id);
                if (attr == null) return s != null ? DslValue.FromString("Settlement::" + s.StringId) : DslValue.Null;
                if (s == null) { ReportUndecidable($"Settlement::{id} 不存在"); return DslValue.Null; }
                switch (attr)
                {
                    case "owner": return s.OwnerClan?.Leader != null ? DslValue.FromString("Hero::" + s.OwnerClan.Leader.StringId) : DslValue.Null;
                    case "clan": return s.OwnerClan != null ? DslValue.FromString("Clan::" + s.OwnerClan.StringId) : DslValue.Null;
                    case "siege": return DslValue.FromBool(s.IsUnderSiege);
                    case "type": return DslValue.FromString(s.IsTown ? "town" : s.IsCastle ? "castle" : "village");
                    case "food": return s.Town != null ? DslValue.FromNumber((long)s.Town.FoodStocks) : DslValue.Null;   // 村庄按所属城（v1 空 + 日志）
                    case "loyalty": case "morale": return s.Town != null ? DslValue.FromNumber((long)s.Town.Loyalty) : DslValue.Null;  // T2：据点士气 ↔ Loyalty（16b §4.4）
                    case "kokudaka": return s.Town != null ? DslValue.FromNumber((long)(s.Town.Prosperity * ScenarioDataPack.KokudakaRatio)) : DslValue.Null;  // T2 换算：石高 = 繁荣度×系数
                    case "defense":
                        ReportUndecidable("Settlement.defense: 墙级+墙血 16b §4.4 待核实（wall API）");
                        return DslValue.Null;
                    case "garrison": return DslValue.FromNumber(GetGarrisonCount(s));
                    case "province": return ScenarioDataPack.FindRegionOfSettlement(s.StringId) != null
                                            ? DslValue.FromString("Region::" + ScenarioDataPack.FindRegionOfSettlement(s.StringId)) : DslValue.Null;  // T2：所屬國 = 区域表反查（16b §3.3）
                    case "security": case "rebllion":
                        return s.Town != null ? DslValue.FromNumber((long)s.Town.Security) : DslValue.Null;  // T2 v1：暴動標誌 = 忠诚度 < 阈值（v1 借 Security）
                    default: return AttrOrNull("Settlement::" + s.StringId, attr);
                }
            }

            private DslValue ResolveFaction(string id, string attr)
            {
                // Faction 无属性白名单（01）：:: 后整体为 StringId（Kingdom.oda 含点）
                var k = FindKingdom(id);
                return k != null ? DslValue.FromString(k.StringId) : DslValue.Null;
            }

            private DslValue ResolveArmy(string id, string attr)
            {
                // Army:: = 军団/部队（MobileParty StringId；16b：T1 引擎（leader/faction）+ T3 外置仓（intent/state/result/attr_N））
                if (attr == null)
                {
                    var p = FindParty(id);
                    return p != null ? DslValue.FromString("Army::" + p.StringId) : DslValue.Null;
                }
                if (attr == "leader") return FindParty(id)?.LeaderHero != null ? DslValue.FromString("Hero::" + FindParty(id).LeaderHero.StringId) : DslValue.Null;
                if (attr == "faction") return FindParty(id)?.MapFaction != null ? DslValue.FromString(FindParty(id).MapFaction.StringId) : DslValue.Null;
                return AttrOrNull("Army::" + id, attr);   // T3：intent/state/result/attr_N 全走外置仓
            }

            private MobileParty FindParty(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                try
                {
                    var p = Campaign.Current != null ? Campaign.Current.CampaignObjectManager.Find<MobileParty>(id) : null;
                    if (p == null) ReportUndecidable($"Army::{id} 部队查无");
                    return p;
                }
                catch (Exception e) { ReportUndecidable($"FindParty({id}): {e.Message}"); return null; }
            }

            private DslValue ResolveTime(string id, string attr)
            {
                // Time::X = 域::属性（全局属性无对象）：attr 拆点为 null 时属性名 = id 本身
                string prop = attr ?? id;
                switch (prop)
                {
                    case "year": return DslValue.FromNumber(ScenarioClock.CurrentAbsoluteYear());
                    case "month": return DslValue.FromNumber(ScenarioClock.CurrentMonthOfYear());
                    case "day": return DslValue.FromNumber(ScenarioClock.CurrentDayOfYear());
                    case "assessment_flag":
                        return DslValue.FromBool(ScenarioStateStore.GetRaw("Time::assessment_flag") == "1");
                    default:
                    {
                        // 计数器/日数类语义域（Time::counter_N 走 旗标计数仓键）
                        string raw = ScenarioStateStore.GetRaw("Time::" + prop);
                        return raw == null ? DslValue.Null : DslValue.FromString(raw);
                    }
                }
            }

            private long GetGarrisonCount(Settlement s)
            {
                long n = 0;
                if (s.Party != null)
                    foreach (var t in s.Party.MemberRoster.GetTroopRoster())
                        if (t.Character?.IsHero != true) n += t.Number;
                return n;
            }
        }

        /// <summary>attr 直接读外置仓（无值 = Null；未注册字段 = 日志——16a 表外由 validator 拦截）</summary>
        private static DslValue AttrOrNull(string key, string attr)
        {
            string v = ScenarioAttrStore.GetAttr(key, attr);
            if (v == null) return DslValue.Null;
            if (long.TryParse(v, out var n)) return DslValue.FromNumber(n);
            if (v == "true") return DslValue.FromBool(true);
            if (v == "false") return DslValue.FromBool(false);
            return DslValue.FromString(v);
        }

        // ============================================================
        // 引擎查找（两轮：预设 ID → predicate 兜底，铁律 5——Settlement/Clan/Kingdom 为 MBObjectBase）
        // ============================================================
        internal static Hero FindHero(string refString)
        {
            if (string.IsNullOrEmpty(refString)) return null;
            if (Campaign.Current == null || MBObjectManager.Instance == null) return null;   // 主菜单/无战役 = 直接 null（铁律 1，零异常路径）
            string id = refString.StartsWith("Hero::") ? refString.Substring(6) : refString;
            try
            {
                // 第一轮：CampaignObjectManager 精确查找（项目先例 StoryContext.FindHeroById）
                var h = Campaign.Current.CampaignObjectManager.Find<Hero>(id);
                if (h != null) return h;
                // 第二轮：CharacterObject 桥（MBObjectBase 注册表）
                var co = MBObjectManager.Instance.GetObject<CharacterObject>(x => x.StringId == id);
                return co?.HeroObject;
            }
            catch (Exception e) { ReportUndecidable($"FindHero({id}): {e.Message}"); return null; }
        }

        internal static Settlement FindSettlement(string refString)
        {
            if (string.IsNullOrEmpty(refString)) return null;
            string id = refString.StartsWith("Settlement::") ? refString.Substring(13) : refString;
            try
            {
                var s = MBObjectManager.Instance.GetObject<Settlement>(id);
                if (s == null) s = MBObjectManager.Instance.GetObject<Settlement>(x => x.StringId == id);
                if (s == null) ReportUndecidable($"Settlement::{id} 查无（07 数据包/占位城）");
                return s;
            }
            catch (Exception e) { ReportUndecidable($"FindSettlement({id}): {e.Message}"); return null; }
        }

        internal static Clan FindClan(string refString)
        {
            if (string.IsNullOrEmpty(refString)) return null;
            string id = refString.StartsWith("Clan::") ? refString.Substring(6) : refString;
            try
            {
                var c = MBObjectManager.Instance.GetObject<Clan>(id);
                if (c == null) c = MBObjectManager.Instance.GetObject<Clan>(x => x.StringId == id);
                if (c == null) ReportUndecidable($"Clan::{id} 查无（07 数据包/预备势力）");
                return c;
            }
            catch (Exception e) { ReportUndecidable($"FindClan({id}): {e.Message}"); return null; }
        }

        internal static Kingdom FindKingdom(string refString)
        {
            if (string.IsNullOrEmpty(refString)) return null;
            string id = refString;
            if (id.StartsWith("Faction::")) id = id.Substring(9);
            if (id.StartsWith("Kingdom::")) id = id.Substring(9);
            try
            {
                var k = MBObjectManager.Instance.GetObject<Kingdom>(id);
                if (k == null) k = MBObjectManager.Instance.GetObject<Kingdom>(x => x.StringId == id);
                if (k == null) ReportUndecidable($"Kingdom::{id} 查无");
                return k;
            }
            catch (Exception e) { ReportUndecidable($"FindKingdom({id}): {e.Message}"); return null; }
        }
    }

    /// <summary>剧本时钟（Time::year/month/day；绝对年 = 数据包基准年 + 引擎流逝年差；引擎无月份 → 30 天/月推算）</summary>
    public static class ScenarioClock
    {
        private static long? _startEngineYear;

        internal static long CurrentAbsoluteYear()
        {
            try
            {
                var now = CampaignTime.Now;
                long nowYear = now.GetYear;   // CampaignTime.GetYear 为属性（1.2.12/1.5.1 实测签名一致）
                if (_startEngineYear == null) _startEngineYear = V.GetStartTime().GetYear;
                return ScenarioDataPack.BaseYear + (nowYear - _startEngineYear.Value);
            }
            catch (Exception e) { AttributeResolver.ReportUndecidable("Time::year: " + e.Message); return ScenarioDataPack.BaseYear; }
        }

        internal static long CurrentMonthOfYear()
        {
            try
            {
                int day = CampaignTime.Now.GetDayOfYear;
                return (day - 1) / 30 + 1;   // 引擎无月份 API（1.2.12/1.5.1 实测）→ 30 天/月推算（数据包可调）
            }
            catch (Exception e) { AttributeResolver.ReportUndecidable("Time::month: " + e.Message); return 1; }
        }

        internal static long CurrentDayOfYear()
        {
            try
            {
                return CampaignTime.Now.GetDayOfYear;
            }
            catch (Exception e) { AttributeResolver.ReportUndecidable("Time::day: " + e.Message); return 0; }
        }
    }
}
