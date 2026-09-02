using System;

namespace TpacCli
{
    /// <summary>
    /// DXT5/BC3 块编码器（立绘打包用）。
    /// 布局 = 每 4x4 像素块 16 字节：BC4 alpha 块(8B) 在前 + BC1 色块(8B) 在后（DirectX 标准 BC3 布局）。
    /// 色块固定用 4 色模式（禁用 3 色+黑透掩——防止半透明硬边缘出黑经）。
    /// 直 alpha 像素，不做 premultiplied。
    /// </summary>
    public static class Bc3Encoder
    {
        // ── 编码：RGBA8 (w*h*4) → BC1 裸块串（无 alpha 版，每块 8B）────────
        public static byte[] EncodeBc1(byte[] rgba, int w, int h)
        {
            byte[] bc3 = Encode(rgba, w, h);
            int blocks = bc3.Length / 16;
            byte[] bc1 = new byte[blocks * 8];
            for (int i = 0; i < blocks; i++)
                Array.Copy(bc3, i * 16 + 8, bc1, i * 8, 8);   // BC3 = [alpha8B][color8B]，剥 alpha 段
            return bc1;
        }

        // ── 编码：RGBA8 (w*h*4) → BC3 裸块串 ──────────────────────────────
        public static byte[] Encode(byte[] rgba, int w, int h)
        {
            if (rgba.Length != w * h * 4)
                throw new ArgumentException("rgba size mismatch");
            int blockW = (w + 3) / 4;
            int blockH = (h + 3) / 4;
            byte[] outData = new byte[blockW * blockH * 16];
            int outPos = 0;
            byte[] block = new byte[16 * 4];
            for (int by = 0; by < blockH; by++)
            {
                for (int bx = 0; bx < blockW; bx++)
                {
                    // 收集 4x4（越界边缘像素取样角点/透明填充——UI sheet 尺寸均为 4 的倍数，仅兜底）
                    int n = 0;
                    for (int py = 0; py < 4; py++)
                    {
                        int yy = by * 4 + py;
                        if (yy >= h) yy = h - 1;
                        for (int px = 0; px < 4; px++)
                        {
                            int xx = bx * 4 + px;
                            if (xx >= w) xx = w - 1;
                            int s = (yy * w + xx) * 4;
                            block[n * 4 + 0] = rgba[s + 0];
                            block[n * 4 + 1] = rgba[s + 1];
                            block[n * 4 + 2] = rgba[s + 2];
                            block[n * 4 + 3] = rgba[s + 3];
                            n++;
                        }
                    }
                    EncodeBlock(block, outData, outPos);
                    outPos += 16;
                }
            }
            return outData;
        }

        // ── 单块 ────────────────────────────────────────────────────────
        private static void EncodeBlock(byte[] block, byte[] dst, int dpos)
        {
            // -------- BC4 alpha（8B 在前）--------
            {
                byte aMin = 255, aMax = 0;
                for (int i = 0; i < 16; i++)
                {
                    byte a = block[i * 4 + 3];
                    if (a < aMin) aMin = a;
                    if (a > aMax) aMax = a;
                }
                // 8 级调色板需要 a0 > a1 → b0 = max（alpha 是 256 级直储存，max/min 互换后索引反查即可）
                dst[dpos + 0] = aMax;
                dst[dpos + 1] = aMin;
                if (aMax == aMin)
                {
                    // 纯色 alpha：全部索引 0
                    for (int i = 0; i < 6; i++) dst[dpos + 2 + i] = 0;
                }
                else
                {
                    int[] pal = BuildBc4Palette(aMax, aMin);
                    ulong bits = 0;
                    int bitPos = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        int a = block[i * 4 + 3];
                        int best = 0, bestErr = 0x7FFFFFFF;
                        for (int p = 0; p < 8; p++)
                        {
                            int d = a - pal[p];
                            if (d < 0) d = -d;
                            if (d < bestErr) { bestErr = d; best = p; }
                        }
                        bits |= (ulong)best << bitPos;
                        bitPos += 3;
                    }
                    for (int i = 0; i < 6; i++)
                        dst[dpos + 2 + i] = (byte)((bits >> (i * 8)) & 0xFF);
                }
            }

            // -------- BC1 color 4 色模式（8B 在后）--------
            {
                // 端点选择：PCA 主轴投影（块内 RGB 协方差主方向，投影 min/max 点作为端点）。
                // 单像素极值法在皮肤渐变处会出黑点斑（stipple noise），PCA 盒拟合在艺术素材上稳定得多。
                float[] mean = new float[3];
                for (int i = 0; i < 16; i++)
                {
                    mean[0] += block[i * 4 + 0];
                    mean[1] += block[i * 4 + 1];
                    mean[2] += block[i * 4 + 2];
                }
                mean[0] /= 16; mean[1] /= 16; mean[2] /= 16;
                float[,] cov = new float[3, 3];
                for (int i = 0; i < 16; i++)
                {
                    float dr = block[i * 4 + 0] - mean[0];
                    float dg = block[i * 4 + 1] - mean[1];
                    float db = block[i * 4 + 2] - mean[2];
                    cov[0, 0] += dr * dr; cov[0, 1] += dr * dg; cov[0, 2] += dr * db;
                    cov[1, 0] += dg * dr; cov[1, 1] += dg * dg; cov[1, 2] += dg * db;
                    cov[2, 0] += db * dr; cov[2, 1] += db * dg; cov[2, 2] += db * db;
                }
                float[] v = { 1f, 1f, 1f };
                for (int it = 0; it < 6; it++)
                {
                    float nv0 = cov[0, 0] * v[0] + cov[0, 1] * v[1] + cov[0, 2] * v[2];
                    float nv1 = cov[1, 0] * v[0] + cov[1, 1] * v[1] + cov[1, 2] * v[2];
                    float nv2 = cov[2, 0] * v[0] + cov[2, 1] * v[1] + cov[2, 2] * v[2];
                    float len = (float)Math.Sqrt(nv0 * nv0 + nv1 * nv1 + nv2 * nv2);
                    if (len < 1e-6f)
                    {
                        v[0] = 1; v[1] = 0; v[2] = 0;
                        break;
                    }
                    v[0] = nv0 / len; v[1] = nv1 / len; v[2] = nv2 / len;
                }
                float minP = float.MaxValue, maxP = float.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    float p = (block[i * 4 + 0] - mean[0]) * v[0] + (block[i * 4 + 1] - mean[1]) * v[1] + (block[i * 4 + 2] - mean[2]) * v[2];
                    if (p < minP) minP = p;
                    if (p > maxP) maxP = p;
                }
                byte Clamp255(float x) => (byte)Math.Max(0, Math.Min(255, (int)(x + 0.5f)));
                ushort e0 = Rgb565(
                    Clamp255(mean[0] + v[0] * minP), Clamp255(mean[1] + v[1] * minP), Clamp255(mean[2] + v[2] * minP));
                ushort e1 = Rgb565(
                    Clamp255(mean[0] + v[0] * maxP), Clamp255(mean[1] + v[1] * maxP), Clamp255(mean[2] + v[2] * maxP));
                // 候选2：通道极值中点（主轴退化/近灰块时兜底：纯色块 → 单色调色板）
                byte[] cByChan = new byte[3];
                for (int c = 0; c < 3; c++)
                {
                    byte mn = 255, mx = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        byte val = block[i * 4 + c];
                        if (val < mn) mn = val;
                        if (val > mx) mx = val;
                    }
                    cByChan[c] = (byte)((mn + mx) / 2);
                }
                ushort mid = Rgb565(cByChan[0], cByChan[1], cByChan[2]);
                // 候选3：明暗两个纯色 565（保底避免退化）
                long bestErr = long.MaxValue;
                ushort[] cands = { e0, e1, mid, Rgb565(0, 0, 0), Rgb565(255, 255, 255) };
                ushort bestE0 = cands[0], bestE1 = cands[1];
                for (int a = 0; a < cands.Length; a++)
                {
                    for (int b = a + 1; b < cands.Length; b++)
                    {
                        long err = ColorBlockError(block, cands[a], cands[b]);
                        if (err < bestErr)
                        {
                            bestErr = err;
                            bestE0 = cands[a];
                            bestE1 = cands[b];
                        }
                    }
                }
                // 端点重定位精修（对渐变块收效显著，如脸部特写 minihead）：
                // 用当前端点把像素分类到 4 色板，重算两个"端点组"（近 c0 组与近 c3 组）的均值作为新端点
                if (bestE0 <= bestE1) { ushort tt = bestE0; bestE0 = bestE1; bestE1 = tt; }
                byte[] rc0 = Decode565(bestE0), rc1 = Decode565(bestE1);
                byte[] rp0 = { (byte)Math.Min(255, (2 * rc0[0] + rc1[0]) / 3), (byte)Math.Min(255, (2 * rc0[1] + rc1[1]) / 3), (byte)Math.Min(255, (2 * rc0[2] + rc1[2]) / 3) };
                byte[] rp3 = { (byte)Math.Min(255, (rc0[0] + 2 * rc1[0]) / 3), (byte)Math.Min(255, (rc0[1] + 2 * rc1[1]) / 3), (byte)Math.Min(255, (rc0[2] + 2 * rc1[2]) / 3) };
                var grpA = new System.Collections.Generic.List<byte[]>();
                var grpB = new System.Collections.Generic.List<byte[]>();
                for (int i = 0; i < 16; i++)
                {
                    byte r = block[i * 4 + 0], g = block[i * 4 + 1], b = block[i * 4 + 2];
                    long d0 = Dist2(r, g, b, rc0[0], rc0[1], rc0[2]);
                    long d3 = Dist2(r, g, b, rp3[0], rp3[1], rp3[2]);
                    if (d0 <= d3) grpA.Add(new byte[] { r, g, b });
                    else grpB.Add(new byte[] { r, g, b });
                }
                if (grpA.Count > 0 && grpB.Count > 0)
                {
                    byte[] mA = Mean(grpA), mB = Mean(grpB);
                    ushort rE0 = Rgb565(mA[0], mA[1], mA[2]);
                    ushort rE1 = Rgb565(mB[0], mB[1], mB[2]);
                    long refineErr = ColorBlockError(block, rE0, rE1);
                    if (refineErr < bestErr)
                    {
                        bestErr = refineErr;
                        bestE0 = rE0;
                        bestE1 = rE1;
                    }
                }
                EncodeBc1Color(block, bestE0, bestE1, dst, dpos + 8);
            }
        }

        private static int[] BuildBc4Palette(byte a0, byte a1)
        {
            // 8 级：pal[0]=a0, pal[1]=a1, k=2..7: ((7-k)*a0 + k*a1)/7
            int[] pal = new int[8];
            pal[0] = a0;
            pal[1] = a1;
            for (int k = 2; k < 8; k++)
                pal[k] = ((7 - k) * a0 + k * a1) / 7;
            return pal;
        }

        private static ushort Rgb565(int r, int g, int b)
        {
            ushort v = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
            // 565 0 通道在低位（小端格局）；BC1 标准低 5 bit = B。此为绿色分量中间。
            return v;
        }

        private static long Dist2(int r, int g, int b, int pr, int pg, int pb)
        {
            long dr = r - pr, dg = g - pg, db = b - pb;
            return dr * dr * 30 + dg * dg * 59 + db * db * 11;
        }

        private static byte[] Mean(System.Collections.Generic.List<byte[]> list)
        {
            int r = 0, g = 0, b = 0;
            foreach (var c in list) { r += c[0]; g += c[1]; b += c[2]; }
            return new[] { (byte)(r / list.Count), (byte)(g / list.Count), (byte)(b / list.Count) };
        }

        private static byte[] Decode565(ushort c)
        {
            return new[] {
                (byte)(((c >> 11) & 0x1F) * 255 / 31),
                (byte)(((c >> 5) & 0x3F) * 255 / 63),
                (byte)((c & 0x1F) * 255 / 31),
            };
        }

        private static long ColorBlockError(byte[] block, ushort e0, ushort e1)
        {
            // BC1 4 色模式要求 c0 > c1（数值），否则解码器走 3 色模式（索引 3 = 透明黑）→
            // encode 端必须先归一并保证 c0 > c1，调色板插值公式 (2c0+c1)/3、(c0+2c1)/3 在 c0>c1 时成立。
            if (e0 <= e1) { ushort t = e0; e0 = e1; e1 = t; }
            byte[] c0 = Decode565(e0), c1 = Decode565(e1);
            byte[] pal = {
                (byte)Math.Min(255, (2 * c0[0] + c1[0]) / 3), (byte)Math.Min(255, (2 * c0[1] + c1[1]) / 3), (byte)Math.Min(255, (2 * c0[2] + c1[2]) / 3),
                (byte)Math.Min(255, (c0[0] + 2 * c1[0]) / 3), (byte)Math.Min(255, (c0[1] + 2 * c1[1]) / 3), (byte)Math.Min(255, (c0[2] + 2 * c1[2]) / 3),
            };
            // palette[0..3] = c0, c1, p2, p3
            long err = 0;
            for (int i = 0; i < 16; i++)
            {
                byte r = block[i * 4], g = block[i * 4 + 1], b = block[i * 4 + 2];
                long best = long.MaxValue;
                for (int p = 0; p < 4; p++)
                {
                    byte pr, pg, pb;
                    if (p == 0) { pr = c0[0]; pg = c0[1]; pb = c0[2]; }
                    else if (p == 1) { pr = c1[0]; pg = c1[1]; pb = c1[2]; }
                    else { pr = pal[(p - 2) * 3]; pg = pal[(p - 2) * 3 + 1]; pb = pal[(p - 2) * 3 + 2]; }
                    long dr = r - pr, dg = g - pg, db = b - pb;
                    long d = dr * dr * 30 + dg * dg * 59 + db * db * 11;
                    if (d < best) best = d;
                }
                err += best;
            }
            return err;
        }

        private static void EncodeBc1Color(byte[] block, ushort e0, ushort e1, byte[] dst, int dpos)
        {
            // 🔴 enforce c0 > c1（4 色模式必要条件；否则解码器切 3 色模式出透明黑像素）
            if (e0 <= e1) { ushort t = e0; e0 = e1; e1 = t; }
            byte[] c0 = Decode565(e0), c1 = Decode565(e1);
            byte[] pal0 = { (byte)Math.Min(255, (2 * c0[0] + c1[0]) / 3), (byte)Math.Min(255, (2 * c0[1] + c1[1]) / 3), (byte)Math.Min(255, (2 * c0[2] + c1[2]) / 3) };
            byte[] pal1 = { (byte)Math.Min(255, (c0[0] + 2 * c1[0]) / 3), (byte)Math.Min(255, (c0[1] + 2 * c1[1]) / 3), (byte)Math.Min(255, (c0[2] + 2 * c1[2]) / 3) };
            uint bits = 0;
            int bitPos = 0;
            for (int i = 0; i < 16; i++)
            {
                byte r = block[i * 4], g = block[i * 4 + 1], b = block[i * 4 + 2];
                int best = 0;
                long bestD = long.MaxValue;
                for (int p = 0; p < 4; p++)
                {
                    byte pr = p == 0 ? c0[0] : p == 1 ? c1[0] : p == 2 ? pal0[0] : pal1[0];
                    byte pg = p == 0 ? c0[1] : p == 1 ? c1[1] : p == 2 ? pal0[1] : pal1[1];
                    byte pb = p == 0 ? c0[2] : p == 1 ? c1[2] : p == 2 ? pal0[2] : pal1[2];
                    long dr = r - pr, dg = g - pg, db = b - pb;
                    long d = dr * dr * 30 + dg * dg * 59 + db * db * 11;
                    if (d < bestD) { bestD = d; best = p; }
                }
                bits |= (uint)best << bitPos;
                bitPos += 2;
            }
            // 565 边序：低 5 位 = B —— 与 Decode565 对应（DirectX 标准）
            dst[dpos + 0] = (byte)(e0 & 0xFF);
            dst[dpos + 1] = (byte)(e0 >> 8);
            dst[dpos + 2] = (byte)(e1 & 0xFF);
            dst[dpos + 3] = (byte)(e1 >> 8);
            for (int i = 0; i < 4; i++)
                dst[dpos + 4 + i] = (byte)((bits >> (i * 8)) & 0xFF);
        }
    }
}
