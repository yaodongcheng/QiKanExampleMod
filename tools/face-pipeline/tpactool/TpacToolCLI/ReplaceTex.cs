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
    /// 输出格式：DXT1（对齐原版池贴图）、单级 mip、SystemFlags 清空、Source 清空。
    /// 用法: tpaccli texreplace --packdir <dir> --filter <pack文件子串> --mapping <manifest.json> --out <dir>
    /// manifest 复用 makepack 格式: {"packs":[{"packName":"x","textures":[{"name","png","width","height"}]}]}
    /// </summary>
    public static class ReplaceTex
    {
        public static int Run(string dir, string filter, string mappingPath, string outDir)
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
                byte[] bc1 = Bc3Encoder.EncodeBc1(rgba, png.Width, png.Height);

                var loader = new ExternalLoader<TexturePixelData>(new TexturePixelData
                {
                    PrimaryRawImage = bc1,
                    RawImage = new[] { new[] { bc1 } },
                })
                {
                    OwnerGuid = old.Guid,
                };
                loader.UserData[TexturePixelData.KEY_WIDTH] = (int)png.Width;
                loader.UserData[TexturePixelData.KEY_HEIGHT] = (int)png.Height;
                loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
                loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
                loader.UserData[TexturePixelData.KEY_FORMAT] = TextureFormat.DXT1;

                old.TypelessDataSegments.Clear();
                old.TypelessDataSegments.Add(loader);
                old.TexturePixels = loader;
                old.MipmapCount = 1;
                old.Format = TextureFormat.DXT1;
                old.SystemFlags = new List<string>();
                old.Source = "";
                old.RawMeta = null;   // 🔴救花屏: Save 走 RawMeta ?? WriteMetadata()——老元数据(12mip)残留=引擎按错 mip 读=越界碎块
                Console.WriteLine($"replaced {old.Name}: {png.Width}x{png.Height} DXT1 1mip ({bc1.Length / 1024.0 / 1024.0:F1}MB), guid kept {old.Guid}");
            }

            string outPath = Path.Combine(outDir ?? ".", pkg.File.Name);
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
            return 0;
        }
    }
}
