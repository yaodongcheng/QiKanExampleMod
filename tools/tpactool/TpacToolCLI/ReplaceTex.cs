using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// texreplace: 用 PNG 替换 pack 内同名 Texture 的像素数据（字段对齐原版样式）。
    /// 保留原 item 对象（GUID/位置/顺序/依赖零风险）——只换数据段 + 同步元数据字段。
    /// 输出格式：默认 DXT1（对齐原版池贴图）；**加 `--alpha` 走 BC3/DXT5（带 alpha）**、
    /// 单级 mip、SystemFlags 清空、Source 清空。
    /// 🔴 贴花这类**靠 alpha 定形状**的贴图必须加 `--alpha` —— DXT1 会把 alpha 抹平。
    /// 用法: tpaccli texreplace --packdir &lt;dir&gt; --filter &lt;pack文件子串&gt; --mapping &lt;manifest.json&gt; --out &lt;dir&gt; [--alpha]
    /// manifest 复用 makepack 格式: {"packs":[{"packName":"x","textures":[{"name","png","width","height"}]}]}
    /// </summary>
    public static class ReplaceTex
    {
        public static int Run(string dir, string filter, string mappingPath, string outDir, bool withAlpha = false)
        {
            if (filter == null || mappingPath == null)
            {
                Console.Error.WriteLine("texreplace requires --filter (pack name substring) and --mapping <json>");
                return 1;
            }
            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));

            var doc = JsonSerializer.Deserialize<MakePack.Manifest>(File.ReadAllText(mappingPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc == null || doc.Packs == null || doc.Packs.Count == 0)
            {
                Console.Error.WriteLine("manifest empty");
                return 1;
            }
            var pkg = mgr.LoadedPackages.FirstOrDefault(p =>
                p.File != null && p.File.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (pkg == null)
            {
                Console.Error.WriteLine("no package matched " + filter);
                return 1;
            }

            foreach (var t in doc.Packs[0].Textures)
            {
                var old = pkg.Items.OfType<Texture>().FirstOrDefault(i => i.Name == t.Name);
                if (old == null)
                {
                    Console.Error.WriteLine("no texture item named " + t.Name + " in " + pkg.File.Name);
                    return 1;
                }
                var png = Png.Open(t.Png);
                if (png.Width != t.Width || png.Height != t.Height)
                    throw new InvalidDataException($"{t.Name}: png {png.Width}x{png.Height} != manifest {t.Width}x{t.Height}");
                byte[] rgba = new byte[png.Width * png.Height * 4];
                int pos = 0;
                for (int y = 0; y < png.Height; y++)
                {
                    for (int x = 0; x < png.Width; x++)
                    {
                        var px = png.GetPixel(x, y);
                        rgba[pos++] = px.R;
                        rgba[pos++] = px.G;
                        rgba[pos++] = px.B;
                        rgba[pos++] = px.A;
                    }
                }
                // 🔴 **生成完整 mip 链**（盒式降采样 + 逐级编码）。
                //    只写 mip0 会让减采样时永远取最清晰那级 ⇒ 掠射角/远处**闪烁**
                //    （贴花正是"贴地 + 掠射角"的场合，必闪）。
                //    ⚠️ mip 条数必须与 KEY_MIPMAP / MipmapCount 三处一致 —— 不一致 = 引擎按错 mip 读 = 越界碎块。
                var mips = new List<byte[]>();
                int mw = png.Width, mh = png.Height;
                byte[] cur = rgba;
                while (true)
                {
                    mips.Add(withAlpha
                        ? Bc3Encoder.Encode(cur, mw, mh)
                        : Bc3Encoder.EncodeBc1(cur, mw, mh));
                    if (mw == 1 && mh == 1) break;
                    int nw = Math.Max(1, mw / 2), nh = Math.Max(1, mh / 2);
                    cur = DownscaleHalf(cur, mw, mh, nw, nh);
                    mw = nw; mh = nh;
                }
                byte[] blocks = mips[0];
                var outFmt = withAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1;

                var loader = new ExternalLoader<TexturePixelData>(new TexturePixelData
                {
                    PrimaryRawImage = blocks,
                    RawImage = new[] { mips.ToArray() },
                })
                {
                    OwnerGuid = old.Guid,
                };
                loader.UserData[TexturePixelData.KEY_WIDTH] = (int)png.Width;
                loader.UserData[TexturePixelData.KEY_HEIGHT] = (int)png.Height;
                loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
                loader.UserData[TexturePixelData.KEY_MIPMAP] = mips.Count;
                loader.UserData[TexturePixelData.KEY_FORMAT] = outFmt;

                old.TypelessDataSegments.Clear();
                old.TypelessDataSegments.Add(loader);
                old.TexturePixels = loader;
                old.MipmapCount = (byte)mips.Count;
                old.Format = outFmt;
                // 🔴🔴 **SystemFlags 绝不能无条件清空**（2026-10-06 实机踩到）：
                //   贴花贴图带 `has_alpha` —— 清掉之后引擎按**不透明**处理，
                //   shader 里 `diffuse_texture_color.a` **恒为 1** ⇒ `alpha_test` 永远不裁
                //   ⇒ 整块方形盖住地面（alpha=0 的外圈 RGB 被当实体画上去）。
                //   旧代码无条件清空是给「立绘池贴图」用的，对靠 alpha 定形状的贴图是致命的。
                //   规则：保留原有的，只按本次是否写 alpha 增删 `has_alpha` 这一项。
                var sysFlags = new List<string>(old.SystemFlags ?? new List<string>());
                sysFlags.RemoveAll(s => s.Equals("has_alpha", StringComparison.OrdinalIgnoreCase));
                if (withAlpha) sysFlags.Add("has_alpha");
                old.SystemFlags = sysFlags;
                old.Source = "";
                old.RawMeta = null;   // 🔴救花屏: Save 走 RawMeta ?? WriteMetadata()——老元数据(12mip)残留=引擎按错 mip 读=越界碎块
                Console.WriteLine($"replaced {old.Name}: {png.Width}x{png.Height} {outFmt} {mips.Count}mip ({blocks.Length / 1024.0 / 1024.0:F1}MB), guid kept {old.Guid}");
            }

            string outPath = Path.Combine(outDir ?? ".", pkg.File.Name);
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
            return 0;
        }

        /// <summary>盒式降采样（RGBA8）：2×2 平均，奇数边时最后一列/行复用。</summary>
        private static byte[] DownscaleHalf(byte[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int y0 = Math.Min(sh - 1, y * 2), y1 = Math.Min(sh - 1, y * 2 + 1);
                for (int x = 0; x < dw; x++)
                {
                    int x0 = Math.Min(sw - 1, x * 2), x1 = Math.Min(sw - 1, x * 2 + 1);
                    int o = (y * dw + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        int s = src[(y0 * sw + x0) * 4 + c] + src[(y0 * sw + x1) * 4 + c]
                              + src[(y1 * sw + x0) * 4 + c] + src[(y1 * sw + x1) * 4 + c];
                        dst[o + c] = (byte)((s + 2) / 4);
                    }
                }
            }
            return dst;
        }
    }
}
