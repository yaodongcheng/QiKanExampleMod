using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.TwoDimension;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 立绘镜像工厂（2026-08-31 用户裁定：立绘资源统一朝右——非主角居左（原朝向）、主角居右（需自动镜像翻转）。
    /// 原理：Gauntlet Widget 无翻转 API（FlipX=0 实测）→ Sprite 层做——复制 SpritePart（同图同 UV 数据）后
    /// 反射交换 MinU/MaxU（UV 水平反转 = 图像镜像），包成新 SpriteGeneric；按原 sprite 名缓存（同资源同一镜像）。
    /// 🔴 不崩纪律：非 SpriteGeneric（如 SpriteSimple？）→ 原样返回（不镜像，保命）；镜像失败 = 原样 + 日志（铁律 1）。
    /// </summary>
    public static class SpriteMirror
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static readonly PropertyInfo MinUProp = typeof(SpritePart).GetProperty("MinU", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo MaxUProp = typeof(SpritePart).GetProperty("MaxU", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static Sprite GetOrMirror(Sprite src)
        {
            if (src == null) return null;
            try
            {
                var gen = src as SpriteGeneric;
                if (gen?.SpritePart == null) return src;   // 非 SpriteGeneric = 无法镜像 → 原样

                var sp = gen.SpritePart;
                string key = "flip_" + sp.Name;
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var newPart = new SpritePart("flip_" + sp.Name, sp.Category, sp.Width, sp.Height)
                {
                    SheetID = sp.SheetID,
                    SheetX = sp.SheetX,
                    SheetY = sp.SheetY,
                };
                newPart.UpdateInitValues();
                // UV 水平反转：交换 MinU/MaxU（private set → 反射）
                float minU = (float)MinUProp.GetValue(sp);
                float maxU = (float)MaxUProp.GetValue(sp);
                MinUProp.SetValue(newPart, maxU);
                MaxUProp.SetValue(newPart, minU);

                // 构造镜像 SpriteGeneric（构造签名版本差异 → V 门面：1.2.12 = (name,part) 2 参；1.4/1.5 = (name,part,in nine) 3 参）
                var mirrored = V.NewSpriteGeneric(key, newPart);
                _cache[key] = mirrored;
                DebugLogger.Log($"[SpriteMirror] 生成立绘镜像: {sp.Name}");
                return mirrored;
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[SpriteMirror] 镜像失败（原样返回，不崩）: {e.Message}");
                return src;
            }
        }
    }
}
