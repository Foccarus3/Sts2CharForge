using System;
using System.IO;
using System.IO.Compression;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// V0.2.289（用户要求「用原版易伤/虚弱/力量/敏捷的图标，但渲成紫色」）：
/// 极简 PNG 染色器 —— **不加任何第三方依赖**（工程里没有图像库，也不想去联网还原 NuGet）。
///
/// <para>只支持本体图标那种常见格式：8 位、非隔行、色彩类型 6（RGBA）/ 2（RGB）/ 3（调色板）。
/// 其它格式直接返回 <c>null</c>，调用方退回「直接复原本体图标」——**绝不产出坏图**。</para>
///
/// <para>染色算法：按亮度 L 往紫色方向映射（保留 alpha）——
/// R = 0.55L + 70、G = 0.30L + 20、B = 0.95L + 120（都夹到 0..255），
/// 深色仍深、亮部偏紫白，本体图标的明暗层次还在。</para>
/// </summary>
public static class PngTint
{
    /// <summary>把 <paramref name="srcPng"/> 染成紫色，返回新的 PNG 字节；不支持就返回 null。</summary>
    public static byte[]? Purple(byte[] srcPng)
    {
        try
        {
            var img = Decode(srcPng);
            if (img is null) return null;
            var (w, h, px) = img.Value;   // px = RGBA，每像素 4 字节
            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a == 0) continue;
                double l = 0.299 * px[i] + 0.587 * px[i + 1] + 0.114 * px[i + 2];
                px[i] = Clamp(0.55 * l + 70);
                px[i + 1] = Clamp(0.30 * l + 20);
                px[i + 2] = Clamp(0.95 * l + 120);
                px[i + 3] = a;
            }
            return Encode(w, h, px);
        }
        catch
        {
            return null;   // 任何解析失败都不抛，交给调用方回退
        }
    }

    private static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));

    // ================== 解码 ==================

    private static (int W, int H, byte[] Rgba)? Decode(byte[] png)
    {
        if (png.Length < 33) return null;
        if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return null;
        int w = 0, h = 0, bitDepth = 0, colorType = 0, interlace = 0;
        var idat = new MemoryStream();
        byte[]? palette = null;
        byte[]? trns = null;
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            int dataAt = pos + 8;
            if (len < 0 || dataAt + len > png.Length) return null;
            if (type == "IHDR")
            {
                w = (png[dataAt] << 24) | (png[dataAt + 1] << 16) | (png[dataAt + 2] << 8) | png[dataAt + 3];
                h = (png[dataAt + 4] << 24) | (png[dataAt + 5] << 16) | (png[dataAt + 6] << 8) | png[dataAt + 7];
                bitDepth = png[dataAt + 8];
                colorType = png[dataAt + 9];
                interlace = png[dataAt + 12];
            }
            else if (type == "PLTE") { palette = new byte[len]; Buffer.BlockCopy(png, dataAt, palette, 0, len); }
            else if (type == "tRNS") { trns = new byte[len]; Buffer.BlockCopy(png, dataAt, trns, 0, len); }
            else if (type == "IDAT") idat.Write(png, dataAt, len);
            else if (type == "IEND") break;
            pos = dataAt + len + 4;   // 跳过 CRC
        }
        if (w <= 0 || h <= 0 || bitDepth != 8 || interlace != 0) return null;
        int channels = colorType switch { 6 => 4, 2 => 3, 3 => 1, 0 => 1, 4 => 2, _ => 0 };
        if (channels == 0) return null;
        if (colorType == 3 && palette is null) return null;

        byte[] raw = Inflate(idat.ToArray());
        int stride = w * channels;
        if (raw.Length < (stride + 1) * h) return null;
        var lines = new byte[h][];
        var prev = new byte[stride];
        int p = 0;
        for (int y = 0; y < h; y++)
        {
            int filter = raw[p++];
            var cur = new byte[stride];
            Buffer.BlockCopy(raw, p, cur, 0, stride);
            p += stride;
            Unfilter(filter, cur, prev, channels);
            lines[y] = cur;
            prev = cur;
        }

        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            var line = lines[y];
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (colorType == 6) { rgba[o] = line[x * 4]; rgba[o + 1] = line[x * 4 + 1]; rgba[o + 2] = line[x * 4 + 2]; rgba[o + 3] = line[x * 4 + 3]; }
                else if (colorType == 2) { rgba[o] = line[x * 3]; rgba[o + 1] = line[x * 3 + 1]; rgba[o + 2] = line[x * 3 + 2]; rgba[o + 3] = 255; }
                else if (colorType == 3)
                {
                    int idx = line[x];
                    int at = idx * 3;
                    if (at + 2 >= palette!.Length) return null;
                    rgba[o] = palette[at]; rgba[o + 1] = palette[at + 1]; rgba[o + 2] = palette[at + 2];
                    rgba[o + 3] = trns is not null && idx < trns.Length ? trns[idx] : (byte)255;
                }
                else if (colorType == 0) { rgba[o] = rgba[o + 1] = rgba[o + 2] = line[x]; rgba[o + 3] = 255; }
                else { byte g = line[x * 2]; rgba[o] = rgba[o + 1] = rgba[o + 2] = g; rgba[o + 3] = line[x * 2 + 1]; }
            }
        }
        return (w, h, rgba);
    }

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        switch (filter)
        {
            case 0: break;
            case 1: for (int i = bpp; i < cur.Length; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]); break;
            case 2: for (int i = 0; i < cur.Length; i++) cur[i] = (byte)(cur[i] + prev[i]); break;
            case 3:
                for (int i = 0; i < cur.Length; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    cur[i] = (byte)(cur[i] + ((a + prev[i]) >> 1));
                }
                break;
            case 4:
                for (int i = 0; i < cur.Length; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    int c = i >= bpp ? prev[i - bpp] : 0;
                    int pp = a + b - c;
                    int pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
                    int pr = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                    cur[i] = (byte)(cur[i] + pr);
                }
                break;
        }
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new MemoryStream(zlib);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        z.CopyTo(outMs);
        return outMs.ToArray();
    }

    // ================== 编码（RGBA / filter 0）==================

    private static byte[] Encode(int w, int h, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteInt(ihdr, 0, w);
        WriteInt(ihdr, 4, h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        var raw = new byte[(w * 4 + 1) * h];
        int q = 0;
        for (int y = 0; y < h; y++)
        {
            raw[q++] = 0;   // filter: none
            Buffer.BlockCopy(rgba, y * w * 4, raw, q, w * 4);
            q += w * 4;
        }
        byte[] compressed;
        using (var outMs = new MemoryStream())
        {
            using (var z = new ZLibStream(outMs, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            compressed = outMs.ToArray();
        }
        WriteChunk(ms, "IDAT", compressed);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteInt(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8];
        WriteInt(head, 0, data.Length);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        Buffer.BlockCopy(t, 0, head, 4, 4);
        s.Write(head);
        s.Write(data);
        uint crc = Crc32(t, data);
        var tail = new byte[4];
        tail[0] = (byte)(crc >> 24); tail[1] = (byte)(crc >> 16); tail[2] = (byte)(crc >> 8); tail[3] = (byte)crc;
        s.Write(tail);
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] a, byte[] b)
    {
        if (_crcTable is null)
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            _crcTable = table;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte x in a) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (byte x in b) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
