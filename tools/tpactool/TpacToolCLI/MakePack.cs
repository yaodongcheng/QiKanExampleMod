using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    public static class MakePack
    {
        // manifest: { "packs": [ { "packName": "...", "textures": [ { "name": "...", "png": "...", "width": n, "height": n } ] } ] }
        public static int Run(string manifestPath, string outDirOverride)
        {
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine("manifest not found: " + manifestPath);
                return 1;
            }
            var doc = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var pack in doc.Packs)
            {
                if (string.IsNullOrWhiteSpace(pack.PackName))
                {
                    Console.Error.WriteLine("pack missing packName");
                    return 1;
                }
                string outPath = Path.Combine(outDirOverride ?? doc.OutDir ?? ".", pack.PackName + ".tpac");
                var pkg = new AssetPackage();
                Console.WriteLine($"pack {pack.PackName}: {pack.Textures.Count} textures -> {outPath}");
                int done = 0;
                foreach (var t in pack.Textures)
                {
                    var asset = BuildTextureAsset(t);
                    pkg.Items.Add(asset);
                    done++;
                    if (done % 64 == 0 || done == pack.Textures.Count)
                        Console.WriteLine($"  {done}/{pack.Textures.Count}");
                }
                pkg.Save(outPath);
                Console.WriteLine($"  saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
            }
            return 0;
        }

        static Texture BuildTextureAsset(TextureDef t)
        {
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
            // format 支持 DXT1（默认 DXT5）：DXT1 = BC1 4B/px 无 alpha（引擎早期链路/原生 core_loading 同规格）
            bool dxt1 = string.Equals(t.Format, "DXT1", StringComparison.OrdinalIgnoreCase);
            byte[] encoded = dxt1 ? Bc3Encoder.EncodeBc1(rgba, png.Width, png.Height)
                                  : Bc3Encoder.Encode(rgba, png.Width, png.Height);

            var asset = new Texture
            {
                Name = t.Name,
                Source = "",
                Flags = new List<string> { "dont_degrade", "dont_delay_loading" },
                Width = (uint)png.Width,
                Height = (uint)png.Height,
                MipmapCount = 1,
                ArrayCount = 1,
                Format = dxt1 ? TextureFormat.DXT1 : TextureFormat.DXT5,
                SystemFlags = dxt1 ? new List<string>() : new List<string> { "has_alpha" },
                GeneratedAssets = new List<Tuple<Guid, Guid>>(),
                UnknownUlong = 0,
                UnknownUlong2 = 0,
            };
            // 确定性 GUID（同 name 每次打包产物一致；段 OwnerGuid 与 asset.Guid 对齐——引擎约定）
            asset.Guid = DeterministicGuid("tpac:" + t.Name);

            var loader = new ExternalLoader<TexturePixelData>(new TexturePixelData
            {
                PrimaryRawImage = encoded,
                RawImage = new[] { new[] { encoded } },
            })
            {
                OwnerGuid = asset.Guid,
            };
            loader.UserData[TexturePixelData.KEY_WIDTH] = (int)png.Width;
            loader.UserData[TexturePixelData.KEY_HEIGHT] = (int)png.Height;
            loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
            loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
            loader.UserData[TexturePixelData.KEY_FORMAT] = dxt1 ? TextureFormat.DXT1 : TextureFormat.DXT5;
            asset.TypelessDataSegments.Add(loader);
            return asset;
        }

        static Guid DeterministicGuid(string s)
        {
            using var sha = SHA256.Create();
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return new Guid(h.Take(16).ToArray());
        }

        public class Manifest
        {
            [JsonPropertyName("outDir")] public string OutDir { get; set; }
            [JsonPropertyName("packs")] public List<PackDef> Packs { get; set; } = new List<PackDef>();
        }

        public class PackDef
        {
            [JsonPropertyName("packName")] public string PackName { get; set; }
            [JsonPropertyName("textures")] public List<TextureDef> Textures { get; set; } = new List<TextureDef>();
        }

        public class TextureDef
        {
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("png")] public string Png { get; set; }
            [JsonPropertyName("width")] public int Width { get; set; }
            [JsonPropertyName("height")] public int Height { get; set; }
            [JsonPropertyName("format")] public string Format { get; set; }   // 可选 "DXT1"（默认 DXT5）
        }
    }
}
