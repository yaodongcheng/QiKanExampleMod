using System;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony 补丁：loading 背景图内容包注入（随机抽取 + 单张按需加载）。
    ///
    /// 机制（1.2.12 反编译实证）：
    /// - 引擎轮换链：LoadingWindowManager → 原版类目 "ui_loading"(12 张) → SetNextGenericImage
    ///   → LoadingImageName = "loading_XX" → 渲染端 SpriteData.GetSprite 显示全屏图。
    /// - SpriteData 全模块合并（LoadFromDepot 字典覆盖）：内容包 SpriteData 定义同名
    ///   sprite 条目即覆盖原版；但纹理必须显式加载（AlwaysLoad 或 PartialLoad），
    ///   否则 SpritePart.Texture 为 null → 黑块。
    /// - 本补丁在 SetNextGenericImage（每次 loading 开启触发一次）处接管：
    ///   从内容包类目随机抽一张 → PartialLoad(该 sheet) → LoadingImageName = {category}_{NNN}。
    ///
    /// 内容包契约（Taikou 侧）：
    /// - GUI/&lt;模块名&gt;SpriteData.xml：类目 "taikou_loading"（N 个 sheet，每张 = 纹理尺寸）
    ///   + 每张一条 SpritePart/GenericSprite，命名 = {category}_{001..NNN}（3 位零填充）。
    /// - AssetPackages/taikou_loading.tpac：键名 = {category}_{NNN}（短名，引擎取 \ 末段）。
    ///
    /// 配置（config.json 单字段，默认空 = 不启用、行为与原生一致）：
    ///   "LoadingImageCategory": "taikou_loading"
    /// 内容包缺失/类目为空 → 静默回退原生（返回 true，不崩）。
    /// </summary>
    [HarmonyPatch(typeof(LoadingWindowViewModel), "SetNextGenericImage")]
    public static class LoadingRandomPatch
    {
        private static readonly System.Random _rng = new System.Random();
        private static int _lastSheetIndex;

        /// <summary>返回 false = 接管（跳过原生轮换）；true = 交给原生</summary>
        public static bool Prefix(LoadingWindowViewModel __instance)
        {
            string category = Settings.Instance.LoadingImageCategory;
            if (string.IsNullOrEmpty(category))
            {
                return true; // 未配置：原生
            }
            try
            {
                var cat = V.GetSpriteCategory(category);
                if (cat == null || cat.SpriteSheetCount <= 0)
                {
                    DebugLogger.Log($"[LoadingRandom] 类目 {category} 不存在或空（内容包缺 SpriteData/tpac？）→ 回退原生");
                    return true;
                }
                // 引擎卸载（热重载/跨屏）后重建 partial 模式 —— 与 SpriteAssetsManager 同法
                if (!cat.IsLoaded || cat.SpriteSheets == null || cat.SpriteSheets.Count < cat.SpriteSheetCount)
                {
                    cat.InitializePartialLoad();
                }

                int next = _rng.Next(1, cat.SpriteSheetCount + 1);

                // 单缓冲：释放上一张，加载当前张（loading 是瞬态画面，1 张常驻足够）
                if (_lastSheetIndex >= 1 && _lastSheetIndex <= cat.SpriteSheetCount && _lastSheetIndex != next)
                {
                    if (cat.SpriteSheets[_lastSheetIndex - 1] != null)
                    {
                        cat.PartialUnloadAtIndex(_lastSheetIndex);
                    }
                }
                if (cat.SpriteSheets[next - 1] == null)
                {
                    cat.PartialLoadAtIndex(UIResourceManager.ResourceContext, V.UIResourceDepot(), next);
                }
                _lastSheetIndex = next;

                __instance.LoadingImageName = category + "_" + next.ToString("000");
                // 探针：区分「SpriteData 未合并(SPRITE_NULL)」vs「纹理未加载(TEXTURE_NULL)」
                var probeSprite = SpriteAssetsManager.GetSprite(__instance.LoadingImageName);
                var tex = cat.SpriteSheets == null || cat.SpriteSheets[next - 1] == null ? null : cat.SpriteSheets[next - 1];
                bool texLoaded = false;
                try { texLoaded = tex != null && tex.IsLoaded(); } catch { }
                DebugLogger.Log($"[LoadingRandom] {category}#{next} IsLoaded={cat.IsLoaded} texNull={tex == null} " +
                                $"texLoaded={texLoaded} sprite={(probeSprite == null ? "NULL" : "OK")}");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[LoadingRandom] 异常回退原生: {ex.GetType().Name} {ex.Message}");
                return true;
            }
        }
    }
}
