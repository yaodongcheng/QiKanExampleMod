using HarmonyLib;
using SandBox.Missions.MissionLogics;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **原版人口生成入口的拦截补丁**（2026-11-15 立，对应总纲 §3.3）。
    ///
    /// 🔴 只拦「当前 Mission 持有剧情人口标记」的那一场（<see cref="StoryPopulationGuard.IsActive"/>）；
    ///    普通逛城、酒馆、地牢、其他城镇一律原样放行。
    ///
    /// 🔴 只拦**返回 void** 的批量 / 排队入口（总纲 §3.3 实施纪律）：
    ///    · `SpawnWanderingAgent`（返回 Agent）、`SpawnHorses`（返回 List&lt;Agent&gt;）**不拦** ——
    ///      Prefix 返回 false 会让它们返回 null，上层解引用即崩。
    ///    · 1.3+ 的打手 / 同伴 / 马匹挪去了别处（`SandBoxHelpers.MissionHelper` 一带），本批未接线，
    ///      属**已知缺口**，在 §16 状态账里记着，不假装已经覆盖。
    ///
    /// 补丁目标已用 ilspycmd 逐版本核对（1.2.12 / 1.5.x 的 `SandBox.dll`），不是照名字猜的。
    /// </summary>
    internal static class StoryPopulationPatches
    {
        // 公共：初始居民整批生成、从别处走入的人
        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnLocationCharacters")]
        internal static class SpawnLocationCharactersPatch
        {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix()
            {
                return !StoryPopulationGuard.ShouldBlock("SpawnLocationCharacters");
            }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnEnteringLocationCharacter")]
        internal static class SpawnEnteringLocationCharacterPatch
        {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix()
            {
                return !StoryPopulationGuard.ShouldBlock("SpawnEnteringLocationCharacter");
            }
        }
    }

#if !MB2_GE_130
    /// <summary>1.2.12 专有：打手、自动同伴、家禽家畜（这些方法在 1.3+ 已搬走）。</summary>
    internal static class StoryPopulationPatches1212
    {
        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnThugs")]
        internal static class SpawnThugsPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnThugs"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnCharactersAccompanyingPlayer")]
        internal static class SpawnCharactersAccompanyingPlayerPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnCharactersAccompanyingPlayer"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnSheeps")]
        internal static class SpawnSheepsPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnSheeps"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnCows")]
        internal static class SpawnCowsPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnCows"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnGeese")]
        internal static class SpawnGeesePatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnGeese"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnChicken")]
        internal static class SpawnChickenPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnChicken"); }
        }

        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnHogs")]
        internal static class SpawnHogsPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnHogs"); }
        }
    }
#endif

#if MB2_GE_130
    /// <summary>1.3+ 专有：延迟生成入口（1.2.12 没有这个方法）。</summary>
    internal static class StoryPopulationPatchesNew
    {
        [HarmonyPatch(typeof(MissionAgentHandler), "SpawnWanderingAgentWithDelay")]
        internal static class SpawnWanderingAgentWithDelayPatch
        {
            private static bool Prefix() { return !StoryPopulationGuard.ShouldBlock("SpawnWanderingAgentWithDelay"); }
        }
    }
#endif
}
