using System;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

// 🔴 不 `using TaleWorlds.TwoDimension`：它与 `TaleWorlds.Engine` **都含 `Texture`**，
//    同时导入会让每个 `Texture` 都变成二义引用（本文件全用 Engine 那个 → 需要的类型逐个全限定）。
namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 纹理像素探针（2026-09-12）——回答「sprite 到底取到真图没有」这个唯一问题。
    ///
    /// 为什么需要它：`Sprite` 对象、`part.Category.IsLoaded`、`SpriteSheets[i] != null` 这些**全部可以是"对"的**，
    /// 而实际纹理里一个像素都没有（空纹理 / 名字根本没命中资源）——界面表现就是「面板在、图是空的」，
    /// 日志一路绿灯。所以必须**把像素读回来**才能定论。
    ///
    /// 类型链（反编译实证）：`SpritePart.Texture`(TwoDimension.Texture) → `.PlatformTexture`(ITexture)
    ///   → 实际实现 `EngineTexture` → `.Texture`(TaleWorlds.Engine.Texture) → `GetPixelData(byte[])`。
    /// </summary>
    public static class TextureProbe
    {
        /// <summary>
        /// 按 sprite 名探一次。返回一行人类可读结论（同时写日志）。
        /// </summary>
        public static string ProbeSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return "Error: empty sprite name.";
            }

            var sprite = SpriteAssetsManager.GetOrLoad(spriteName);
            if (sprite == null)
            {
                string msg = $"'{spriteName}': sprite 对象取不到（SpriteData 里没这个名字）";
                DebugLogger.Log($"[TextureProbe] {msg}");
                return msg;
            }

            var part = (sprite as TaleWorlds.TwoDimension.SpriteGeneric)?.SpritePart;
            string objInfo = $"sprite={sprite.Width}x{sprite.Height}";
            if (part != null)
            {
                objInfo += $"，part={part.Width}x{part.Height} sheet#{part.SheetID} 分类={part.Category?.Name}";
            }

            // ① SpritePart 拿到的纹理 → 引擎纹理
            TaleWorlds.Engine.Texture engineTexFromPart = null;
            try
            {
                var twoDimTex = part?.Texture;
                string platDesc = twoDimTex == null ? "平台纹理=null"
                    : $"平台纹理={twoDimTex.Width}x{twoDimTex.Height},IsLoaded={twoDimTex.IsLoaded()}";
                var plat = twoDimTex?.PlatformTexture as EngineTexture;
                engineTexFromPart = plat?.Texture;
                objInfo += $"，{platDesc}";
                if (engineTexFromPart != null)
                {
                    objInfo += $"，引擎纹理名='{engineTexFromPart.Name}'";
                }
                else if (plat != null)
                {
                    objInfo += "，EngineTexture.Texture=null（纹理对象已释放？）";
                }
                else if (twoDimTex?.PlatformTexture != null)
                {
                    objInfo += $"，平台纹理实现={twoDimTex.PlatformTexture.GetType().Name}（不是 EngineTexture）";
                }
            }
            catch (Exception ex)
            {
                objInfo += $"，取纹理异常({ex.GetType().Name} {ex.Message})";
            }

            // ② 按引擎命名规则直查资源（独立于 sprite 对象，验证资源本身在不在）
            string guessed = GuessTextureName(spriteName);
            string directDesc;
            TaleWorlds.Engine.Texture engineTexDirect = null;
            try
            {
                engineTexDirect = TaleWorlds.Engine.Texture.GetFromResource(guessed);
                directDesc = engineTexDirect == null
                    ? $"GetFromResource('{guessed}')=null ❌资源没命中"
                    : $"GetFromResource('{guessed}')={engineTexDirect.Width}x{engineTexDirect.Height},IsLoaded={engineTexDirect.IsLoaded()}";
            }
            catch (Exception ex)
            {
                directDesc = $"GetFromResource('{guessed}') 异常：{ex.Message}";
            }

            // ③ 导出纹理文件（🔴 1.2.12 的 Texture **没有** GetPixelData（那是 1.5.2+）——反编译看对版本！
            //    两版通用的是 SaveToFile：把纹理原样写成 .dds，离线可读像素/可肉眼打开）
            string saved = SaveTexture(engineTexFromPart ?? engineTexDirect, spriteName);

            string line = $"{spriteName}：{objInfo} | {directDesc} | 导出：{saved}";
            DebugLogger.Log($"[TextureProbe] {line}");
            return line;
        }

        /// <summary>sprite 名 → 引擎纹理名。约定 = 分类前缀 + "_" + 序号（`lwnprof_bustup` + `_517`）。</summary>
        private static string GuessTextureName(string spriteName)
        {
            string core = spriteName;
            int lastUnderscore = core.LastIndexOf('_');
            if (lastUnderscore <= 0)
            {
                return spriteName;
            }
            // 情绪图带尾巴（_happy 等）→ 去掉再取号
            string tail = core.Substring(lastUnderscore + 1);
            if (tail == "happy" || tail == "angry" || tail == "sad" || tail == "surprised")
            {
                core = core.Substring(0, lastUnderscore);
            }
            return core;                     // lwnprof_bustup_517 本身就是纹理名
        }

        /// <summary>
        /// 把纹理导出成文件（两版通用 API：`Texture.SaveToFile(path)`）。
        /// 产出落在 `Modules/LivingWorldNpcs/Debug/TextureProbe/{spriteName}.dds`——
        /// 之后可离线解析 DDS 头与像素，或直接拿看图工具打开（DXT5 需支持该格式的工具）。
        /// </summary>
        private static string SaveTexture(TaleWorlds.Engine.Texture tex, string spriteName)
        {
            if (tex == null)
            {
                return "无纹理可存";
            }
            try
            {
                string dir = System.IO.Path.Combine(
                    TaleWorlds.Library.BasePath.Name, "Modules", "LivingWorldNpcs", "Debug", "TextureProbe");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, spriteName + ".dds");
                tex.SaveToFile(file);
                long size = 0;
                try
                {
                    var fi = new System.IO.FileInfo(file);
                    size = fi.Exists ? fi.Length : 0;
                }
                catch (Exception) { /* 大小读不到不影响结论 */ }
                return $"{file}（{tex.Width}x{tex.Height}，{size / 1024}KB）";
            }
            catch (Exception ex)
            {
                return $"SaveToFile 异常：{ex.GetType().Name} {ex.Message}";
            }
        }
    }
}
