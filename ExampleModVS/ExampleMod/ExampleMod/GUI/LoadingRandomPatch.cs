using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony 补丁：loading 背景图内容包注入（织丰同构版，2026-09-06 用户裁定复刻 Shokuho 机制）。
    ///
    /// 织丰机制实证（Shokuho.dll 反编译 101259-101263）：
    ///   [HarmonyPatch(typeof(LoadingWindowViewModel))]
    ///   [HarmonyPatch("SetTotalGenericImageCount")] [HarmonyPostfix]
    ///   static void PostFix(ref int ____totalGenericImageCount)
    ///       => ____totalGenericImageCount = CountOfLoadingScreens;   // =3
    ///   + SpriteData 同名覆盖 loading_01..N（指向自己的类目）+ tpac 键 {Category}_{N}
    /// 即：显示名走原版轮换链（loading_01..N），原版 partial/渲染机制原样复用——不绕开、不自命名。
    ///
    /// 本补丁做两件事：
    /// 1) 同款 postfix：轮换总数 = 内容包类目张数（原版 12 → 79）
    /// 2) 类目按需加载（织丰没做这步，但我们的 partial 链已验证有效 texLoaded=True；
    ///    先 InitializePartialLoad 占位，渲染时由引擎路径取纹理——与织丰的"未知兜底"双保险）
    ///
    /// 内容包契约（Taikou 侧）：
    /// - GUI/TaikouSpriteData.xml：类目 "taikou_loading"（N sheet）+ SpritePart/GenericSprite
    ///   双套命名：{category}_{001..NNN}（池）+ loading_01..NN（原版槽位覆盖，每张一槽）
    /// - AssetPackages/taikou_loading.tpac：键名 {category}_{NNN}（短名）
    ///
    /// 配置（config.json 单字段，默认空 = 不启用、行为与原生一致）：
    ///   "LoadingImageCategory": "taikou_loading"
    /// </summary>
    [HarmonyPatch(typeof(LoadingWindowViewModel), "SetTotalGenericImageCount")]
    public static class LoadingRandomPatch
    {
        public static void Postfix(ref int ____totalGenericImageCount)
        {
            string category = Settings.Instance.LoadingImageCategory;
            if (string.IsNullOrEmpty(category))
            {
                return; // 未配置：原生（12 张原版轮换）
            }
            try
            {
                var cat = V.GetSpriteCategory(category);
                if (cat == null || cat.SpriteSheetCount <= 0)
                {
                    DebugLogger.Log($"[LoadingRandom] 类目 {category} 不存在或空（内容包缺 SpriteData/tpac？）→ 原生");
                    return;
                }
                // 占位初始化（幂等；渲染端由引擎路径取纹理）
                if (!cat.IsLoaded)
                {
                    cat.InitializePartialLoad();
                }
                ____totalGenericImageCount = cat.SpriteSheetCount; // 织丰同构：原版 12 → 池张数（79）
                DebugLogger.Log($"[LoadingRandom] SetTotalGenericImageCount -> {cat.SpriteSheetCount}（{category}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[LoadingRandom] 异常回退原生: {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}
