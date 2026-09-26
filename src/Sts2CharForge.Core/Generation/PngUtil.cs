using System.IO.Compression;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 极简 PNG 读写 + 改色工具（只支持 8bit 非隔行的 RGB/RGBA，够用且不引第三方库）。
/// 用途：把本体贴图重新着色（头像描边、左侧能量球等），避免整块沿用本体配色。
/// </summary>
public static class PngUtil
{
    /// <summary>读成 RGBA 字节数组；不支持的格式返回 null。</summary>
    public static (int W, int H, byte[] Rgba)? Decode(string path)
    {
        try
        {
            byte[] all = File.ReadAllBytes(path);
            if (all.Length < 8 || all[0] != 0x89 || all[1] != 0x50) return null;

            int pos = 8, w = 0, h = 0, bd = 0, ct = 0, interlace = 0;
            using var idat = new MemoryStream();
            while (pos + 8 <= all.Length)
            {
                int len = Be(all, pos);
                string type = System.Text.Encoding.ASCII.GetString(all, pos + 4, 4);
                int dataAt = pos + 8;
                if (type == "IHDR")
                {
                    w = Be(all, dataAt);
                    h = Be(all, dataAt + 4);
                    bd = all[dataAt + 8];
                    ct = all[dataAt + 9];
                    interlace = all[dataAt + 12];
                }
                else if (type == "IDAT") idat.Write(all, dataAt, len);
                else if (type == "IEND") break;
                pos = dataAt + len + 4;
            }

            if (w <= 0 || h <= 0 || bd != 8 || interlace != 0) return null;
            int bpp = ct switch { 2 => 3, 6 => 4, _ => 0 };
            if (bpp == 0) return null;

            idat.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                raw = outMs.ToArray();
            }

            int stride = w * bpp;
            if (raw.Length < (long)(stride + 1) * h) return null;
            var px = new byte[stride * h];
            Buffer.BlockCopy(raw, 0, px, 0, px.Length);

            // 逐行反滤波
            var rgba = new byte[w * h * 4];
            var cur = new byte[stride];
            var prev = new byte[stride];
            int src = 0;
            for (int y = 0; y < h; y++)
            {
                int filter = raw[src++];
                Buffer.BlockCopy(raw, src, cur, 0, stride);
                src += stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0;
                    int b = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    int val = cur[x];
                    cur[x] = (byte)(filter switch
                    {
                        1 => val + a,
                        2 => val + b,
                        3 => val + (a + b) / 2,
                        4 => val + Paeth(a, b, c),
                        _ => val,
                    });
                }
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    int s = x * bpp;
                    rgba[o] = cur[s];
                    rgba[o + 1] = cur[s + 1];
                    rgba[o + 2] = cur[s + 2];
                    rgba[o + 3] = bpp == 4 ? cur[s + 3] : (byte)255;
                }
                Buffer.BlockCopy(cur, 0, prev, 0, stride);
            }
            return (w, h, rgba);
        }
        catch { return null; }
    }

    /// <summary>
    /// 估算一张图的主色相（按饱和度×不透明度加权平均），用于「能量球跟着能量图标的颜色走」。
    /// 图太灰/太透明时返回 false。
    /// </summary>
    public static bool TryGetDominantHue(string path, out double hue, double minSaturation = 0.15)
    {
        hue = 0;
        var img = Decode(path);
        if (img is null) return false;
        var (w, h, px) = img.Value;

        double sumSin = 0, sumCos = 0, weightSum = 0;
        for (int i = 0; i < w * h; i++)
        {
            byte a = px[i * 4 + 3];
            if (a < 32) continue;
            double r = px[i * 4] / 255.0, g = px[i * 4 + 1] / 255.0, b = px[i * 4 + 2] / 255.0;
            RgbToHsv(r, g, b, out double hh, out double s, out double v);
            if (s < minSaturation || v < 0.08) continue;      // 灰/黑不参与
            double weight = s * (a / 255.0) * v;
            sumSin += Math.Sin(hh * 2 * Math.PI) * weight;
            sumCos += Math.Cos(hh * 2 * Math.PI) * weight;
            weightSum += weight;
        }
        if (weightSum <= 0) return false;
        // 色相是环形的，用向量平均避免 0°/360° 处出错
        double angle = Math.Atan2(sumSin / weightSum, sumCos / weightSum);
        if (angle < 0) angle += 2 * Math.PI;
        hue = angle / (2 * Math.PI);
        return true;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
    }

    private static int Be(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

    /// <summary>写 RGBA PNG。</summary>
    public static void WriteRgba(string path, int width, int height, byte[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var raw = new byte[(width * 4 + 1) * height];
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            raw[p++] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, p, width * 4);
            p += width * 4;
        }

        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            idat = ms.ToArray();
        }

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width);
        WriteBe(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = 6;

        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(fs, "IHDR", ihdr);
        WriteChunk(fs, "IDAT", idat);
        WriteChunk(fs, "IEND", []);
    }

    /// <summary>把贴图变成「指定颜色的实心剪影」（用于头像描边）。</summary>
    public static bool Silhouette(string srcPath, string dstPath, byte r, byte g, byte b, byte alphaThreshold = 8)
    {
        var img = Decode(srcPath);
        if (img is null) return false;
        var (w, h, src) = img.Value;
        var outPx = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte a = src[i * 4 + 3];
            outPx[i * 4] = r;
            outPx[i * 4 + 1] = g;
            outPx[i * 4 + 2] = b;
            outPx[i * 4 + 3] = a > alphaThreshold ? (byte)255 : (byte)0;
        }
        WriteRgba(dstPath, w, h, outPx);
        return true;
    }

    /// <summary>
    /// 缩放到指定尺寸（按 alpha 加权平均，缩小后边缘不会发黑）。
    ///
    /// 为什么需要它：本体规定了好几种图标的**固定尺寸**，而用户上传的图可能是 512×512 甚至更大：
    ///   · <c>images/packed/sprite_fonts/*_energy_icon.png</c> 是卡牌描述里用 [img] 内联的图标，本体就是 24×24；
    ///     内联图按原图尺寸绘制，塞一张 512×512 进去会把卡牌描述撑爆 → 游戏的字体自适应把字号缩得极小
    ///     （用户报过「大奖这张牌的描述字体变得非常小」就是这个原因）。
    ///   · <c>ui_atlas</c> 里的能量图标精灵是 74×74。
    /// </summary>
    public static bool Resize(string srcPath, string dstPath, int targetW, int targetH)
    {
        var img = Decode(srcPath);
        if (img is null) return false;
        var (w, h, src) = img.Value;
        targetW = Math.Max(1, targetW);
        targetH = Math.Max(1, targetH);
        if (w == targetW && h == targetH)
        {
            WriteRgba(dstPath, w, h, src);
            return true;
        }

        var outPx = new byte[targetW * targetH * 4];
        for (int y = 0; y < targetH; y++)
        {
            int y0 = (int)((long)y * h / targetH);
            int y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * h / targetH));
            for (int x = 0; x < targetW; x++)
            {
                int x0 = (int)((long)x * w / targetW);
                int x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * w / targetW));

                long sa = 0, sr = 0, sg = 0, sb = 0;
                int n = 0;
                for (int yy = y0; yy < y1; yy++)
                {
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int i = (yy * w + xx) * 4;
                        int a = src[i + 3];
                        sa += a;
                        sr += (long)src[i] * a;
                        sg += (long)src[i + 1] * a;
                        sb += (long)src[i + 2] * a;
                        n++;
                    }
                }

                int o = (y * targetW + x) * 4;
                if (n == 0 || sa == 0)
                {
                    outPx[o] = 0; outPx[o + 1] = 0; outPx[o + 2] = 0; outPx[o + 3] = 0;
                    continue;
                }
                outPx[o] = (byte)Math.Clamp(sr / sa, 0, 255);
                outPx[o + 1] = (byte)Math.Clamp(sg / sa, 0, 255);
                outPx[o + 2] = (byte)Math.Clamp(sb / sa, 0, 255);
                outPx[o + 3] = (byte)Math.Clamp(sa / n, 0, 255);
            }
        }

        WriteRgba(dstPath, targetW, targetH, outPx);
        return true;
    }

    /// <summary>
    /// 把贴图整体转到指定的色相（保留明暗/饱和度与透明通道；近灰白的像素不动，
    /// 这样高光和阴影不会被染色）。用于把铁甲的能量球改成自定义颜色。
    /// </summary>
    public static bool HueShiftTo(string srcPath, string dstPath, double targetHue01, double minSaturation = 0.08)
    {
        var img = Decode(srcPath);
        if (img is null) return false;
        var (w, h, src) = img.Value;
        var outPx = new byte[src.Length];
        for (int i = 0; i < w * h; i++)
        {
            double r = src[i * 4] / 255.0, g = src[i * 4 + 1] / 255.0, b = src[i * 4 + 2] / 255.0;
            RgbToHsv(r, g, b, out double _, out double s, out double v);
            if (s <= minSaturation)
            {
                outPx[i * 4] = src[i * 4];
                outPx[i * 4 + 1] = src[i * 4 + 1];
                outPx[i * 4 + 2] = src[i * 4 + 2];
            }
            else
            {
                HsvToRgb(targetHue01, s, v, out double nr, out double ng, out double nb);
                outPx[i * 4] = (byte)Math.Clamp(Math.Round(nr * 255), 0, 255);
                outPx[i * 4 + 1] = (byte)Math.Clamp(Math.Round(ng * 255), 0, 255);
                outPx[i * 4 + 2] = (byte)Math.Clamp(Math.Round(nb * 255), 0, 255);
            }
            outPx[i * 4 + 3] = src[i * 4 + 3];
        }
        WriteRgba(dstPath, w, h, outPx);
        return true;
    }

    public static void RgbToHsv(double r, double g, double b, out double hue, out double s, out double v)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        v = max;
        double d = max - min;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { hue = 0; return; }
        if (max == r) hue = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (max == g) hue = ((b - r) / d + 2) / 6;
        else hue = ((r - g) / d + 4) / 6;
    }

    public static void HsvToRgb(double hue, double s, double v, out double r, out double g, out double b)
    {
        hue -= Math.Floor(hue);
        int i = (int)(hue * 6);
        double f = hue * 6 - i;
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    private static void WriteBe(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte bb in typeBytes) crc = Crc(crc, bb);
        foreach (byte bb in data) crc = Crc(crc, bb);
        crc ^= 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBe(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes);
    }

    private static readonly uint[] _table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc(uint crc, byte b) => _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
}
