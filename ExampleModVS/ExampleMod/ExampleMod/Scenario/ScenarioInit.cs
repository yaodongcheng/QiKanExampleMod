using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本初始化（W3，06 时代重置的最小种子版）：新档执行一次——
    /// ①人物池三态（活着 / 未出生 / 已死——v1：拨年龄 + 未出生生日设未来；「已死」只日志（官方击杀流程有断言/副作用风险，06 时代重置真身落地）
    /// ②数据包 heroes[].attrs 默认值灌入外置仓（五维/技能/功勋开局即真值，非 0——16b §3.1）③孤儿键（对象不在 = attrs 照常 seed，16b §3.1）
    /// 🔴 顺序纪律：本类由 ScenarioCampaignBehavior.OnNewGameCreated 触发（AddBehavior 顺序 = MyBehavior 清仓之后）——清仓 → 种子。
    /// 🔴 绝对年校准（先年龄后技能，官方初始化顺序）＝ 06 时代重置真身；W3 用「相对日」近似（DaysFromNow），误差年内可接受，注释留痕。
    /// </summary>
    public static class ScenarioInit
    {
        public static int SeededHeroes { get; private set; }
        public static int SeededAttrs { get; private set; }
        public static int AdjustedAges { get; private set; }
        public static int UnbornSkipped { get; private set; }
        public static int DeceasedSkipped { get; private set; }

        public static void Apply()
        {
            ResetCounters();
            var pack = ScenarioDataPack.Heroes;
            if (pack.Count == 0)
            {
                DebugLogger.Log("[Scenario][Init] 数据包 heroes 空（07 人物池未接入——机制先跑）");
                return;
            }
            foreach (var seed in pack)
            {
                var hero = AttributeResolver.FindHero(seed.StringId);
                if (hero == null)
                {
                    // 孤儿键：对象不在（时代未登场/织丰缺失/屏蔽）——默认值照常 seed，对象回来自动接上（16b §3.1 禁止删行）
                    DebugLogger.Log($"[Scenario][Init] Hero::{seed.StringId} 对象不在（孤儿键 seed）");
                }
                else
                {
                    ApplyAging(seed, hero);
                }
                foreach (var kv in seed.Attrs)
                {
                    ScenarioAttrStore.SetAttr("Hero::" + seed.StringId, kv.Key, kv.Value);
                    SeededAttrs++;
                }
                SeededHeroes++;
            }
            DebugLogger.Log($"[Scenario][Init] 完成：英雄 {SeededHeroes} / 默认属性 {SeededAttrs} / 拨年龄 {AdjustedAges} / 未出生跳过 {UnbornSkipped} / 已死跳过 {DeceasedSkipped}");
        }

        private static void ApplyAging(ScenarioDataPack.HeroSeedDef seed, Hero hero)
        {
            int era = ScenarioDataPack.BaseYear;   // 剧本年代锚（1560 时代 = 1560）
            try
            {
                // 未出生：生日设未来（防成年事件拉活——06 坑：未出生必须同时把生日设未来）
                if (seed.BirthYear > era)
                {
                    hero.SetBirthDay(CampaignTime.DaysFromNow(50f * 365f));
                    UnbornSkipped++;
                    DebugLogger.Log($"[Scenario][Init] {seed.StringId} 未出生（{seed.BirthYear}>era {era}）→ 生日设未来");
                    return;
                }
                // 已死：仅日志（击杀流程（06 官方初始化）落地前不执行——防二次击杀断言崩）。
                // 🔴 年粒度边界：DeathYear < era 才算已死（1560 年死的角色在 1560 时代开局 = 仍在场）
                if (seed.DeathYear > 0 && seed.DeathYear < era)
                {
                    DeceasedSkipped++;
                    DebugLogger.Log($"[Scenario][Init] {seed.StringId} 时代已死（{seed.DeathYear}≤{era}）→ 击杀流程待 06，跳过");
                    return;
                }
                // 活着：按剧本年代校年龄（相对日近似：当前 ≈ 剧本年）
                if (seed.BirthYear > 0)
                {
                    int age = era - seed.BirthYear;
                    hero.SetBirthDay(CampaignTime.DaysFromNow(-age * 365f));
                    AdjustedAges++;
                }
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Scenario][Init] {seed.StringId} 拨年龄失败（跳过，不崩）: {e.Message}");
            }
        }

        private static void ResetCounters()
        {
            SeededHeroes = SeededAttrs = AdjustedAges = UnbornSkipped = DeceasedSkipped = 0;
        }
    }
}
