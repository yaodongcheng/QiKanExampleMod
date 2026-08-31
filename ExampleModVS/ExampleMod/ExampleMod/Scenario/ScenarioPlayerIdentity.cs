using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 当前扮演身份锚（2026-08-31：用户 "我是信长"——主角显示层（名字/立绘/立绘镜像位）跟随设定；06 时代重置前身，06 落地时写入同一字段）。
    /// null = 引擎默认（Hero.MainHero 实际角色，未启动剧本身份）。
    /// 🔴 范围纪律：本锚只影响「显示层」（立绘/名字/镜像判定），不改世界状态（世界身份注入 = 06：改名/换族/传送——后置）。
    /// </summary>
    public static class ScenarioPlayerIdentity
    {
        public static string CurrentHeroId { get; private set; }

        /// <summary>设定当前扮演（剧本开档/调试切换）；null/空 = 清除回引擎默认</summary>
        public static void SetPlayerHero(string heroId)
        {
            CurrentHeroId = string.IsNullOrEmpty(heroId) ? null : heroId;
            DebugLogger.Log($"[Scenario][Identity] 当前扮演 = {(CurrentHeroId ?? "引擎默认（MainHero 实际角色）")}");
        }

        /// <summary>主角 StringId（身份锚 ?? MainHero 实际）</summary>
        public static string ResolveMainHeroId()
        {
            return CurrentHeroId ?? (Hero.MainHero?.StringId);
        }

        /// <summary>主角 Hero 对象（取不到 = null，调用方兜底）</summary>
        public static Hero ResolveMainHero()
        {
            string id = ResolveMainHeroId();
            return string.IsNullOrEmpty(id) ? null : AttributeResolver.FindHero(id);
        }

        /// <summary>是否主角引用（speaker 引用文本；MainHero 常量或身份锚命中）</summary>
        public static bool IsMainHeroRef(string speakerRef)
        {
            if (speakerRef == "Hero::MainHero") return true;
            return speakerRef != null && speakerRef.StartsWith("Hero::")
                && string.Equals(speakerRef.Substring(6), ResolveMainHeroId(), System.StringComparison.Ordinal);
        }
    }
}
