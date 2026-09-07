using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

// 🔴 TaleWorlds.Engine.Path 与 System.IO.Path 同名冲突，必须走别名
using SysPath = System.IO.Path;
using SysFile = System.IO.File;
using SysDirectory = System.IO.Directory;
using SysFileStream = System.IO.FileStream;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 实机地形高度图导出命令（2026-09-07）。
    /// 游戏内 `~` 控制台调用（参数一律忽略）：
    ///   custom.export_heightmap            # 导出到 <模块根>/Debug/HeightmapExport/
    ///
    /// 产物 = 16bit 灰度 PNG（引擎 Import Heightmap / 编辑器 Export Heightmap 同规格）
    ///   + info.txt（真实四件套 X/Y/Size/Dim/Scale + 模式说明，人读摘要）。
    /// 数据来源（🔴 2026-09-07 实机定案：客户端一律 sampling）：
    ///   GetTerrainHeightData（编辑器式整格导出）对原版=空壳、对织丰=direct native crash（托管 catch 不住、
    ///   引擎 crash handler 都不弹，tracelog 冻结于调用行）→ 永久禁用；
    ///   只走 GetTerrainHeight(Vec2) 逐像素采样（运行时射线/寻路同源 API，必活），
    ///   按真实网格分辨率采样，输出插值曲面高度图。采样进度写 DebugLogger。
    /// 场景解析顺序：Mission 场景优先，否则战役大地图（SandBox.MapScene.Scene）。
    /// PNG 编码零依赖手写（PNG 签名 + IHDR/IDAT/IEND + zlib(DeflateStream) + CRC32），无 System.Drawing。
    /// </summary>
    public class TerrainExportCommands
    {
        // 🔴 专用崩溃追踪日志：DebugLogger 每行独立 Write 但可能有缓冲，进程崩溃丢尾行（织丰实机 2026-09-07 教训）；
        //    TraceLog 用 AutoFlush StreamWriter 直写，崩溃不丢最后一行 → 冻结行 = native 崩溃点。
        private static readonly object _traceLock = new object();

        private static string TracePath()
        {
            return SysPath.Combine(ModuleRootDir(), "Debug", "HeightmapExport", "tracelog.txt");
        }

        private static void TraceLog(string message)
        {
            try
            {
                lock (_traceLock)
                {
                    string dir = SysPath.GetDirectoryName(TracePath());
                    SysDirectory.CreateDirectory(dir);
                    using (var sw = new StreamWriter(TracePath(), true, Encoding.UTF8) { AutoFlush = true })
                    {
                        sw.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
                    }
                }
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log("[TerrainExport] tracelog write failed: " + ex.Message); } catch { }
            }
        }
        /// <summary>
        /// 导出当前场景的地形高度图（16bit 灰度 PNG）。参数忽略，输出路径固定模块目录。
        /// </summary>
        /// <param name="args">忽略（调试命令参数无意义，防止手滑误传目录/数值）</param>
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("export_heightmap", "custom")]
        public static string ExportHeightmap(List<string> args)
        {
            try
            {
                TraceLog("cmd entered, args=" + (args == null ? "0" : args.Count.ToString()));

                Scene scene = ResolveCurrentScene();
                TraceLog("scene resolved: " + (scene != null ? "ok" : "null"));
                if (scene == null)
                    return "error: no scene available (run in a mission or in campaign map)";

                bool containsTerrain = scene.ContainsTerrain;
                TraceLog("ContainsTerrain=" + containsTerrain);
                if (!containsTerrain)
                    return "error: current scene has no terrain";

                bool hasHeightmap = scene.HasTerrainHeightmap;
                TraceLog("HasTerrainHeightmap=" + hasHeightmap);
                if (!hasHeightmap)
                    return "error: HasTerrainHeightmap == false, height grid not available in client";

                scene.GetTerrainData(out Vec2i nodeDim, out float nodeSize, out int layerCount, out int layerVersion);
                TraceLog(string.Format("GetTerrainData ok: nodeDim={0}x{1} size={2} layers={3}v{4}", nodeDim.X, nodeDim.Y,
                    nodeSize.ToString("0.###", CultureInfo.InvariantCulture), layerCount, layerVersion));

                int nodeCount = nodeDim.X * nodeDim.Y;
                if (nodeDim.X <= 0 || nodeDim.Y <= 0 || nodeCount > 100000)
                    return string.Format("error: invalid nodeDim ({0}x{1})", nodeDim.X, nodeDim.Y);

                // ── 元数据探测（GetTerrainNodeData 是纯元数据读；🔴 实机 2026-09-07：客户端返回无效值 vtx<=0 或 quadLength<=0）──
                scene.GetTerrainNodeData(0, 0, out int vtx, out float quadLength, out float node0Min, out float node0Max);
                TraceLog("GetTerrainNodeData(0,0): vtx=" + vtx + " quadLength=" + quadLength);
                bool nodeMetaValid = vtx > 0 && quadLength > 0;
                if (!nodeMetaValid)
                {
                    DebugLogger.Log(string.Format("[TerrainExport] GetTerrainNodeData(0,0) invalid: vtx={0}, quadLength={1} (fallback 256 quads per node)", vtx, quadLength));
                    vtx = 0;
                    quadLength = 0;
                }

                // ── 整格数据：🔴 永久禁用（2026-09-07 织丰实机：GetTerrainHeightData(0,0) 直接 native 崩溃，
                //    托管 try/catch 包不住、引擎 crash handler 都不弹——该调用对织丰场景=炸弹，原版=空壳。
                //    一律走 sampling（GetTerrainHeight 是客户端唯一安全活路）。──
                bool gridMode = false;
                TraceLog("mode decision: gridMode=" + gridMode + " (GetTerrainHeightData disabled, crash-risk)");

                float[] grid;
                int totalW, totalH;
                string mode;

                // 🔴 GetTerrainMemoryUsage 同样禁用（原版空但织丰/未知场景有崩溃风险，2026-09-07）
                mode = "sampled";
                grid = ExportBySampling(scene, nodeDim, nodeSize, nodeMetaValid ? quadLength : 0f, out totalW, out totalH);
                DebugLogger.Log("[TerrainExport] 整格数据不可用（grid mode fallback）→ sampled 模式采样完成 " + totalW + "x" + totalH);
                TraceLog("sampled export done: " + totalW + "x" + totalH);

                // ── 归一化区间：优先引擎全局 min/max；NaN/退化时用数据实际范围 ──
                float gMin, gMax;
                bool gotMinMax = scene.GetTerrainMinMaxHeight(out gMin, out gMax);
                bool rangeUsable = gotMinMax
                    && !float.IsNaN(gMin) && !float.IsNaN(gMax)
                    && !float.IsInfinity(gMin) && !float.IsInfinity(gMax)
                    && gMax > gMin;
                if (!rangeUsable)
                {
                    gMin = float.MaxValue;
                    gMax = float.MinValue;
                    for (int i = 0; i < grid.Length; i++)
                    {
                        if (grid[i] < gMin) gMin = grid[i];
                        if (grid[i] > gMax) gMax = grid[i];
                    }
                }

                float dataMin = float.MaxValue, dataMax = float.MinValue;
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i] < dataMin) dataMin = grid[i];
                    if (grid[i] > dataMax) dataMax = grid[i];
                }

                ushort[] pixels = new ushort[grid.Length];
                if (gMax > gMin)
                {
                    double scale = 65535.0 / (gMax - gMin);
                    for (int i = 0; i < grid.Length; i++)
                    {
                        double t = (grid[i] - gMin) * scale;
                        if (t < 0) t = 0;
                        else if (t > 65535) t = 65535;
                        pixels[i] = (ushort)(t + 0.5);
                    }
                }
                else
                {
                    // 退化：全图常数，取中间灰
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = 32768;
                    }
                }

                string outDir = SysPath.Combine(ModuleRootDir(), "Debug", "HeightmapExport");

                SysDirectory.CreateDirectory(outDir);

                string pngPath = SysPath.Combine(outDir, "heightmap_16bit.png");
                string infoPath = SysPath.Combine(outDir, "info.txt");

                WritePng16Gray(pngPath, pixels, totalW, totalH);

                // ── info.txt：人读摘要（命令产物文本一律英文）──
                var info = new StringBuilder();
                info.AppendLine("Live terrain heightmap export info");
                info.AppendLine("================================");
                info.AppendLine(string.Format("Mode          : {0} (per-pixel GetTerrainHeight sampling; grid API GetTerrainHeightData disabled — native crash on Shokuho map, 2026-09-07)", mode));
                info.AppendLine(string.Format("IMPORT PARAMS (real engine values)  "));
                info.AppendLine(string.Format("  X     = {0} (nodes along X)", nodeDim.X));
                info.AppendLine(string.Format("  Y     = {0} (nodes along Y)", nodeDim.Y));
                info.AppendLine(string.Format("  Size  = {0} m (node edge; world = X*Size x Y*Size = {1} x {2} m)",
                    nodeSize.ToString("0.###", CultureInfo.InvariantCulture),
                    (nodeDim.X * nodeSize).ToString("0.###", CultureInfo.InvariantCulture),
                    (nodeDim.Y * nodeSize).ToString("0.###", CultureInfo.InvariantCulture)));
                info.AppendLine(vtx > 0
                    ? string.Format("  Dim   = {0} verts per node (vertexCountAlongAxis; quad {1} m)",
                        vtx, quadLength.ToString("0.###", CultureInfo.InvariantCulture))
                    : "  Dim   = n/a (runtime metadata unavailable; sampled at 256 quads per node)");
                info.AppendLine(string.Format("  Scale = {0} m (terrain max height; min = {1})",
                    gMax.ToString("0.###", CultureInfo.InvariantCulture), gMin.ToString("0.###", CultureInfo.InvariantCulture)));
                info.AppendLine(string.Format("Layers        : {0} (v{1})", layerCount, layerVersion));
                info.AppendLine(string.Format("PNG grid      : {0} x {1}", totalW, totalH));
                info.AppendLine("Terrain mem   : n/a (GetTerrainMemoryUsage not queried; crash risk)");
                info.AppendLine(string.Format("Data min/max  : {0} / {1}", dataMin.ToString("0.###", CultureInfo.InvariantCulture), dataMax.ToString("0.###", CultureInfo.InvariantCulture)));
                info.AppendLine(string.Format("Normalize     : [{0}, {1}] -> [0, 65535]", gMin.ToString("0.###", CultureInfo.InvariantCulture), gMax.ToString("0.###", CultureInfo.InvariantCulture)));
                info.AppendLine(string.Format("PNG           : {0}", pngPath));
                SysFile.WriteAllText(infoPath, info.ToString(), Encoding.UTF8);

                return string.Format(
                    "OK: mode={0} X={1} Y={2} Size={3} Dim={4} Scale={5} (min={6}) | grid={7}x{8} | png={9}",
                    mode, nodeDim.X, nodeDim.Y,
                    nodeSize.ToString("0.###", CultureInfo.InvariantCulture),
                    vtx,
                    gMax.ToString("0.###", CultureInfo.InvariantCulture),
                    gMin.ToString("0.###", CultureInfo.InvariantCulture),
                    totalW, totalH, pngPath);
            }
            catch (Exception ex)
            {
                // 🔴 异常信息全量进日志（中文/英文均可）——控制台只给英文固定提示（命令返回文本纪律）
                DebugLogger.Log("[TerrainExport] exception: " + ex);
                return "error: exception (full details in Debug/StoryEngine_RuntimeLog.txt)";
            }
        }


        /// <summary>
        /// 采样模式：无整格数据时按逐像素 GetTerrainHeight（运行时必活 API）采样。
        /// 分辨率 = nodeDim x 每节点格数（quadLength 有效时 = 世界尺寸/quadLength，否则退化 256 格/节点）。
        /// 采样前先做 3x3 探针写日志（验证 GetTerrainHeight 客户端可用性 + 高度量级）。
        /// 进度每若干行写一次 DebugLogger（同步命令需数秒~数十秒，进度可查）。
        /// </summary>
        private static float[] ExportBySampling(Scene scene, Vec2i nodeDim, float nodeSize, float quadLength, out int totalW, out int totalH)
        {
            // 🔴 元数据无效（quadLength<=0，实机实证）→ 每节点 256 格 Fallback（对齐主档规格表原版 Dim）
            int quadsPerNode = quadLength > 0 ? (int)Math.Max(1.0, Math.Round(nodeSize / quadLength)) : 256;
            totalW = nodeDim.X * quadsPerNode;
            totalH = nodeDim.Y * quadsPerNode;

            if (totalW > 40000 || totalH > 40000)
                throw new Exception("sampled grid too large (" + totalW + "x" + totalH + ")");

            float quad = nodeSize / (float)quadsPerNode;
            float worldW = nodeDim.X * nodeSize;
            float worldH = nodeDim.Y * nodeSize;

            // ── 3x3 探针：5% / 50% / 95% 世界坐标 → 高度写日志（一次实机回答“GetTerrainHeight 是否活 + 量级”）──
            var probFrac = new[] { 0.05f, 0.50f, 0.95f };
            var probeLog = new StringBuilder("[TerrainExport] height probes (fx,fy -> meters):");
            foreach (float fy in probFrac)
            {
                foreach (float fx in probFrac)
                {
                    float ph = scene.GetTerrainHeight(new Vec2(fx * worldW, fy * worldH), true);
                    probeLog.Append(string.Format(" ({0:0.00},{1:0.00})={2:0.###}", fx, fy, ph));
                }
            }
            DebugLogger.Log(probeLog.ToString());
            TraceLog(probeLog.ToString());

            float[] grid = new float[totalW * totalH];
            long done = 0;
            long total = (long)totalW * totalH;
            // 🔴 朝向定案（2026-09-07 织丰实机）：引擎世界 Y+ 指向南 → 采样 y=0 是南端、图上下颠倒。
            //    采样行按 y 反转（wy 从大到小），输出恒为北朝上（北海道上、九州下）。
            for (int y = 0; y < totalH; y++)
            {
                float wy = (totalH - 1 - y + 0.5f) * quad;
                for (int x = 0; x < totalW; x++)
                {
                    float wx = (x + 0.5f) * quad;
                    grid[y * totalW + x] = scene.GetTerrainHeight(new Vec2(wx, wy), true);
                    done++;
                }
                if (y % 128 == 0)
                {
                    DebugLogger.Log(string.Format("[TerrainExport] sampled {0}/{1} rows ({2:P0})", y, totalH, (double)done / total));
                }
            }
            return grid;
        }

        /// <summary>
        /// 物理材质索引探针：验证 GetTerrainPhysicsMaterialIndexData（PHYM 段）在客户端是否可用
        /// ——若可用 = 逐顶点「层/物理索引」网格（材质分层判据的替代路线，matmap 运行时无 API）。
        /// 🔴 独立命令（不并入 export_heightmap）：GetTerrainHeightData 同族 API 有 native 崩溃风险，
        ///    单独探测互不牵连；控制台英文摘要，全量写 tracelog。
        /// </summary>
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("export_terrainlayers", "custom")]
        public static string ProbeTerrainLayers(List<string> args)
        {
            try
            {
                Scene scene = ResolveCurrentScene();
                TraceLog("probe: scene=" + (scene == null ? "null" : "ok"));
                if (scene == null)
                    return "error: no scene available";

                bool contains = scene.ContainsTerrain;
                TraceLog("probe: ContainsTerrain=" + contains);
                if (!contains)
                    return "error: scene has no terrain";

                scene.GetTerrainData(out Vec2i nodeDim, out float nodeSize, out int layerCount, out int layerVersion);
                TraceLog("probe: GetTerrainData nodeDim=" + nodeDim.X + "x" + nodeDim.Y +
                    " size=" + nodeSize.ToString("0.###", CultureInfo.InvariantCulture) +
                    " layers=" + layerCount + "v" + layerVersion);

                short[] phy = scene.GetTerrainPhysicsMaterialIndexData(0, 0);
                TraceLog("probe: GetTerrainPhysicsMaterialIndexData(0,0) len=" + (phy == null ? -1 : phy.Length));
                if (phy == null || phy.Length == 0)
                    return "error: physics material index data empty";

                int min = int.MaxValue, max = int.MinValue;
                var hist = new Dictionary<int, int>();
                foreach (short v in phy)
                {
                    int iv = v;
                    if (iv < min) min = iv;
                    if (iv > max) max = iv;
                    hist[iv] = hist.TryGetValue(iv, out int c) ? c + 1 : 1;
                }
                var top = new List<string>();
                foreach (var kv in hist.OrderByDescending(x => x.Value).Take(8))
                {
                    top.Add(kv.Key + ":" + kv.Value);
                }

                string summary = "OK: len=" + phy.Length + " min=" + min + " max=" + max +
                    " uniq=" + hist.Count + " top=" + string.Join(",", top);
                TraceLog("probe result: " + summary);
                return summary;
            }
            catch (Exception ex)
            {
                TraceLog("probe exception: " + ex);
                return "error: exception (see tracelog.txt)";
            }
        }

        /// <summary>
        /// 当前可用场景：Mission 优先，其次战役大地图。
        /// </summary>
        private static Scene ResolveCurrentScene()
        {
            Mission mission = Mission.Current;
            if (mission != null && mission.Scene != null)
            {
                return mission.Scene;
            }

            if (Campaign.Current != null && Campaign.Current.MapSceneWrapper is SandBox.MapScene mapScene)
            {
                return mapScene.Scene;
            }

            return null;
        }

        /// <summary>
        /// 模块根目录：从 DLL 路径（&lt;Module&gt;/bin/Win64_Shipping_Client/）上溯两级。
        /// </summary>
        private static string ModuleRootDir()
        {
            string dllDir = SysPath.GetDirectoryName(typeof(TerrainExportCommands).Assembly.Location);
            return SysPath.GetFullPath(SysPath.Combine(dllDir, "..", ".."));
        }

        // ────────────────────────────────────────────────────────────────
        // 零依赖 16bit 灰度 PNG 编码器（PNG 签名 + IHDR + IDAT + IEND，
        // IDAT = zlib 包装（0x78 0x9C + raw deflate + Adler32），块 CRC32 手写表）
        // ────────────────────────────────────────────────────────────────

        private static readonly byte[] PngSignature = { (byte)'\x89', (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 };

        private static void WritePng16Gray(string path, ushort[] pixels, int width, int height)
        {
            byte[] ihdr = new byte[13];
            WriteU32Be(ihdr, 0, (uint)width);
            WriteU32Be(ihdr, 4, (uint)height);
            ihdr[8] = 16;  // bit depth = 16
            ihdr[9] = 0;   // color type = grayscale
            ihdr[10] = 0;  // compression
            ihdr[11] = 0;  // filter
            ihdr[12] = 0;  // interlace

            // 原始扫描线：每行前导 filter byte(0=none) + 大端 ushort 数据
            int rowBytes = width * 2 + 1;
            byte[] raw = new byte[height * rowBytes];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowBytes;
                int pxBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    ushort v = pixels[pxBase + x];
                    raw[rowStart + 1 + x * 2] = (byte)(v >> 8);
                    raw[rowStart + 2 + x * 2] = (byte)(v & 0xFF);
                }
            }

            // zlib 包装：2 字节头（0x78 0x9C）+ DeflateStream 原始流 + Adler32（大端）
            byte[] deflated = Deflate(raw);
            byte[] idat = new byte[deflated.Length + 6];
            idat[0] = 0x78;
            idat[1] = 0x9C;
            Array.Copy(deflated, 0, idat, 2, deflated.Length);
            uint adler = Adler32(raw);
            idat[deflated.Length + 2] = (byte)(adler >> 24);
            idat[deflated.Length + 3] = (byte)(adler >> 16);
            idat[deflated.Length + 4] = (byte)(adler >> 8);
            idat[deflated.Length + 5] = (byte)adler;

            using (SysFileStream fs = SysFile.Create(path))
            {
                fs.Write(PngSignature, 0, PngSignature.Length);
                WriteChunk(fs, "IHDR", ihdr);
                WriteChunk(fs, "IDAT", idat);
                WriteChunk(fs, "IEND", new byte[0]);
            }
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
                {
                    ds.Write(raw, 0, raw.Length);
                }
                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream fs, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            byte[] len = new byte[4];
            WriteU32Be(len, 0, (uint)data.Length);
            fs.Write(len, 0, 4);
            fs.Write(typeBytes, 0, 4);
            fs.Write(data, 0, data.Length);
            byte[] crc = new byte[4];
            WriteU32Be(crc, 0, Crc32(typeBytes, data));
            fs.Write(crc, 0, 4);
        }

        private static void WriteU32Be(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static uint Crc32(byte[] a, byte[] b)
        {
            uint crc = 0xFFFFFFFF;
            CheckCrcByte(a, ref crc);
            CheckCrcByte(b, ref crc);
            return crc ^ 0xFFFFFFFF;
        }

        private static void CheckCrcByte(byte[] data, ref uint crc)
        {
            foreach (byte x in data)
            {
                crc ^= x;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
        }

        private static uint Adler32(byte[] data)
        {
            const uint mod = 65521;
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % mod;
                b = (b + a) % mod;
            }
            return (b << 16) | a;
        }
    }
}
