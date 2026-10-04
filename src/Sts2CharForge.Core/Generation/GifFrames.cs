using System;
using System.Collections.Generic;
using System.IO;

namespace Sts2CharForge.Core.Generation;

// ==================== 自写的 GIF 解码器（逐帧动画的「拆帧」这一半） ====================
//
// 为什么不用 System.Drawing / WPF 的 GIF 解码：
//   ① Core 是 netstandard 级别的库，引 WPF 会把界面依赖拖进生成器（命令行也要能跑）；
//   ② GIF 的「帧合成」语义（处置方式 disposal + 透明索引）各库实现差别很大，
//      我们要的结果是「和参考实现逐像素一致」，自己写反而最短。
//
// 反直觉但**必须照做**的地方（都是拿参考实现逐像素对出来的，改了就偏）：
//   · 逻辑屏幕描述符读完 packed 之后指针一次性 +2（跳过背景色索引**和**像素宽高比）。
//     标准解码器通常只跳过背景色索引；参考实现跳了 2，这里跟着跳 —— 反正后面的
//     全局色表、帧位置都以此为准，两者只要一致就不会错位。
//   · 帧数据是**叠加**到画布上的：当前帧只覆盖「不透明」像素，透明像素保留画布上已有的
//     颜色（不是抠掉，也不是保留上一帧的 alpha）。首帧之前画布是全 0（全透明黑）。
//   · 处置方式为 3（RestoreToPrevious）时恢复的是**本帧解码前**的整块画布；
//     为 2（RestoreBackground）时把本帧矩形整块清成 0（alpha 也清 0）。
//   · 延时是「每帧独立」记录的：图形控制扩展读到的值乘 10 得到毫秒，读到 0 或负值
//     一律按 100ms（Chrome / Firefox 的通行做法，否则有的素材会「闪一下」）。
//   · LZW 解出来的字节数**可能少于**期望像素数（截断的 GIF）：不补齐也不报错，
//     直接按解出来的长度返回 —— BuildPlan 那边只关心「解不开 = null」。
public static partial class GifFrames
{
    /// <summary>
    /// 从文件解码（文件不存在 → null；读文件出错（被占用 / 权限）→ null）。
    /// 生成流程里对每个槽位都调一次，所以这里**不要抛异常**：一个坏素材不该让整份生成炸掉。
    /// </summary>
    public static Result? Decode(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch
        {
            return null;   // 读不动就当解不开，走「[错误] 解不开」那条提示
        }
    }

    public static Result? Decode(byte[] bytes) => DecodeCore(bytes, null);

    /// <summary>带跟踪日志的解码（诊断「为什么这张 GIF 解不开」用；正常生成走 <see cref="Decode(byte[])"/>）。</summary>
    public static Result? DecodeWithTrace(byte[] bytes, Action<string> log) => DecodeCore(bytes, log);

    private static Result? DecodeCore(byte[] b, Action<string>? log)
    {
        if (log != null)
            log($"DecodeCore 进入：{b.Length} 字节，头={(b.Length < 3 ? "??" : "" + (char)b[0] + (char)b[1] + (char)b[2])}");

        if (b.Length < 14) return null;                              // 头 + 逻辑屏幕描述符都不够，别往下读了
        if (b[0] != (byte)'G' || b[1] != (byte)'I' || b[2] != (byte)'F') return null;

        int p = 6;
        int w = ReadU16(b, ref p);
        int h = ReadU16(b, ref p);
        // 8192 是「明显不是给人看的素材」的上限：解码一次要 4×W×H 字节，不限住会把内存吃光
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;

        byte packed = b[p++];
        p += 2;   // 背景色索引 + 像素宽高比（见文件头说明）
        byte[]? gct = null;
        if ((packed & 0x80) != 0)
        {
            int n = (1 << ((packed & 7) + 1)) & 31;   // &31 是防「非法 GIF 写 8」时移位过头
            gct = ReadPalette(b, ref p, n);
        }
        if (gct == null) return null;                 // 没有全局色表就没法解（局部色表救不了首帧之前）

        if (log != null)
            log($"头解析完：{w}x{h} packed=0x{packed:X2} 调色板={gct.Length / 3}色 p={p}");

        byte[] canvas = new byte[w * h * 4];          // 合成画布（初始全 0 = 全透明黑）
        var frames = new List<Frame>();
        int delay = 100;
        int disposal = 0;                             // 处置方式：0 未指定 / 2 恢复背景 / 3 恢复上一帧
        int transIndex = -1;
        bool hasTrans = false;
        byte[] palette = gct;

        while (p < b.Length)
        {
            byte block = b[p++];
            if (log != null)
                log($"循环 p={p - 1} 标识=0x{block:X2} 已解{frames.Count}帧");

            if (block == 0x3B) break;                 // 0x3B = 文件结束

            // 下面每一步都可能踩到「文件在块中间断掉」（下载到一半 / 手改坏的 GIF）。
            // 参考实现靠 BCL 自己的边界行为躲过去了（实测截断的 GIF 它也只是解不开、不抛），
            // 但它躲得并不整齐；这里显式挡住，保证「永远不把异常抛给调用方」这条约定成立。
            if (block == 0x21)                        // 0x21 = 扩展块
            {
                if (p >= b.Length) break;
                byte label = b[p++];
                if (label == 0xF9)                    // 图形控制扩展
                {
                    if (p >= b.Length) break;
                    byte size = b[p++];
                    if (size >= 4 && p + 4 <= b.Length)
                    {
                        byte gcd = b[p];
                        disposal = (gcd >> 2) & 7;
                        hasTrans = (gcd & 1) != 0;
                        delay = b[p + 1] | (b[p + 2] << 8);
                        delay *= 10;
                        transIndex = b[p + 3];
                        p += 4;
                        if (delay <= 0) delay = 100;
                    }
                    else
                    {
                        p += size;                    // 长度不对 / 不够读：整块跳过（不猜结构）
                    }
                    SkipSubBlocks(b, ref p);          // 扩展块固定以 0x00 结束
                }
                else
                {
                    SkipSubBlocks(b, ref p);          // 注释 / 应用扩展 / 纯文本：全都用子块携带
                }
                continue;
            }

            if (block != 0x2C && block != 0x00) break; // 既不是图像描述符也不是填充：结构坏了，停

            if (block == 0x00) continue;              // 填充字节，继续找下一个块

            // ---- 图像描述符 ----
            if (p + 9 > b.Length) break;              // 4 个 u16 + packed = 9 字节
            int left = ReadU16(b, ref p);
            int top = ReadU16(b, ref p);
            int fw = ReadU16(b, ref p);
            int fh = ReadU16(b, ref p);
            byte fpacked = b[p++];
            bool local = (fpacked & 0x80) != 0;
            bool interlaced = (fpacked & 0x40) != 0;
            if (local)
            {
                int n = (1 << ((fpacked & 7) + 1)) & 31;
                palette = ReadPalette(b, ref p, n);
            }
            else
            {
                palette = gct;                        // 本帧没带局部色表 → 回到全局色表
            }

            if (palette == null) break;
            if (fw <= 0 || fh <= 0) break;

            // 处置方式 3 要在「本帧覆盖画布之前」留一份快照，所以只能在这里拷
            byte[]? snapshot = null;
            if (disposal == 3) snapshot = (byte[])canvas.Clone();

            if (p >= b.Length) break;                 // 连「最小码长」字节都没有：这帧废了
            byte minCode = b[p++];
            byte[]? idx = ReadImageData(b, ref p, minCode, fw * fh, out int compressedLen);
            if (log != null)
                log($"  图像 left={left} top={top} {fw}x{fh} 局部色板={local} 隔行={interlaced} LZW最小码长={minCode} 压缩数据={compressedLen}字节 解出={(idx == null ? -1 : idx.Length)}像素（期望{fw * fh}）");
            if (idx == null) break;

            if (interlaced) idx = Deinterlace(idx, fw, fh);

            // 把这一帧盖到画布上：透明像素不动画布（见文件头说明）
            for (int y = 0; y < fh; y++)
            {
                int cy = top + y;
                if (cy < 0 || cy >= h) continue;
                for (int x = 0; x < fw; x++)
                {
                    int cx = left + x;
                    if (cx < 0 || cx >= w) continue;
                    byte ci = idx[y * fw + x];
                    if (hasTrans && ci == transIndex) continue;       // 透明像素只是「不覆盖」
                    int c3 = ci * 3;
                    if (c3 + 2 >= palette.Length) continue;           // 索引越界的像素丢掉（坏素材别让它崩）
                    int o = (cy * w + cx) * 4;
                    canvas[o] = palette[c3];
                    canvas[o + 1] = palette[c3 + 1];
                    canvas[o + 2] = palette[c3 + 2];
                    canvas[o + 3] = 255;
                }
            }

            frames.Add(new Frame((byte[])canvas.Clone(), delay));

            if (disposal == 2)
            {
                // 恢复背景：把本帧矩形整块清 0（含 alpha）
                for (int y = 0; y < fh; y++)
                {
                    int cy = top + y;
                    if (cy < 0 || cy >= h) continue;
                    for (int x = 0; x < fw; x++)
                    {
                        int cx = left + x;
                        if (cx < 0 || cx >= w) continue;
                        int o = (cy * w + cx) * 4;
                        canvas[o] = canvas[o + 1] = canvas[o + 2] = canvas[o + 3] = 0;
                    }
                }
            }
            else if (disposal == 3 && snapshot != null)
            {
                canvas = snapshot;
            }

            // 图像块之后回到「没有局部色表 / 没有透明 / 处置未指定 / 默认延时」的状态
            if (!local) palette = gct;
            hasTrans = false;
            transIndex = -1;
            disposal = 0;
            delay = 100;
        }

        if (frames.Count == 0) return null;
        return new Result(w, h, frames);
    }

    /// <summary>小端 16 位；读不到就返回 0 并把指针顶到末尾（坏文件不让它越界）。</summary>
    private static int ReadU16(byte[] b, ref int p)
    {
        if (p + 1 >= b.Length) { p = b.Length; return 0; }
        int v = b[p] | (b[p + 1] << 8);
        p += 2;
        return v;
    }

    /// <summary>
    /// 读 <paramref name="count"/> 个 RGB 三元组。**故意允许读到文件尾**：
    /// 末尾缺几色就补 0（黑），而不是整体失败 —— 那种 GIF 在别的看图软件里也能显示。
    /// </summary>
    private static byte[] ReadPalette(byte[] b, ref int p, int count)
    {
        byte[] pal = new byte[count * 3];
        int n = pal.Length;
        if (p + n > b.Length) n = Math.Max(0, b.Length - p);
        Array.Copy(b, p, pal, 0, n);
        p += pal.Length;                 // 指针按「本该有多少」前进（保证后续块仍对齐）
        return pal;
    }

    /// <summary>跳过一个「长度字节 + 数据」的子块序列（读到 0 长度为止）。</summary>
    private static void SkipSubBlocks(byte[] b, ref int p, Action<string>? log = null)
    {
        int read = 0;
        while (p < b.Length)
        {
            int len = b[p++];
            if (log != null && read < 4)
                log($"    子块 @{p - 1} 长度={len}");
            read++;
            if (len == 0) break;
            p += len;
        }
        if (log != null)
            log($"    子块结束 p={p}（读了 {read} 块）");
    }

    /// <summary>
    /// 收集图像数据的所有子块，拼成一段连续压缩流，然后 LZW 解码。
    /// <paramref name="minCode"/> 是「最小码长」字节（GIF 里它**必须存在**：少了它整帧就废）。
    /// </summary>
    private static byte[]? ReadImageData(byte[] b, ref int p, byte minCode, int expected, out int compressedLen)
    {
        compressedLen = 0;
        if (minCode < 2 || minCode > 11) return null;   // 合法 GIF 只可能是 2..8，这里放宽到 11 兜底

        using var ms = new MemoryStream();
        while (p < b.Length)
        {
            int len = b[p++];
            if (len == 0) break;
            if (p + len > b.Length) len = Math.Max(0, b.Length - p);   // 截断的最后一块：能读多少读多少
            ms.Write(b, p, len);
            p += len;
        }

        byte[] data = ms.ToArray();
        compressedLen = data.Length;
        return LzwDecode(data, minCode, expected);
    }

    /// <summary>
    /// GIF 变体的 LZW 解码。和「标准 LZW」的差别都在下面注释里 —— 这些就是 GIF 的方言，
    /// 少一条都会和参考实现对不上：
    ///   · 码长在「字典项数达到 1&lt;&lt;码长」时 +1，只涨到 12 为止；
    ///   · 字典满 4096 之后**不再新增项**（只能等下一个 Clear 码），而不是溢出去覆盖旧项；
    ///   · 遇到「码 = 下一个待分配下标」这种经典 KwKwK 情形时，用「上一个码的首字节」补一位；
    ///   · 码长是「读之前」的值（有些实现写成读到之后再判断，会整段错位）。
    /// </summary>
    private static byte[]? LzwDecode(byte[] data, int minCode, int expected)
    {
        int clearCode = 1 << (minCode & 31);
        int endCode = clearCode + 1;
        byte[] outBuf = new byte[expected];   // 就算数据是坏的，输出也不会超过这个长度
        int outLen = 0;

        const int dictSize = 4096;
        int[] prefix = new int[dictSize];
        byte[] suffix = new byte[dictSize];
        byte[] firstByte = new byte[dictSize];
        ResetDict();

        int codeSize = minCode + 1;
        int next = endCode + 1;
        int prev = -1;
        int bitPos = 0;
        int totalBits = data.Length * 8;
        byte[] stack = new byte[dictSize + 2];   // +2 是给 KwKwK 情形多压一位留的余量

        while (bitPos + codeSize <= totalBits)
        {
            int code = ReadBits(data, bitPos, codeSize);
            bitPos += codeSize;

            if (code == clearCode)
            {
                ResetDict();
                codeSize = minCode + 1;
                next = endCode + 1;
                prev = -1;
                continue;
            }
            if (code == endCode) break;

            int sp = 0;
            int cur = code;
            if (code >= next)
            {
                if (prev < 0) break;             // 首码就指向未定义的项：数据坏了
                stack[sp++] = firstByte[prev];   // KwKwK：补上「上一个串的首字节」
                cur = prev;
            }
            while (cur >= 0 && sp < stack.Length)
            {
                stack[sp++] = suffix[cur];
                cur = prefix[cur];
            }

            for (int i = sp - 1; i >= 0; i--)
                if (outLen < outBuf.Length) outBuf[outLen++] = stack[i];

            if (sp > 0)
                firstByte[next < dictSize ? next : dictSize - 1] = stack[sp - 1];

            if (prev >= 0 && next < dictSize)
            {
                prefix[next] = prev;
                suffix[next] = stack[sp - 1];
                next++;
                if (next == (1 << (codeSize & 31)) && codeSize < 12) codeSize++;
            }

            prev = code;
            if (outLen >= outBuf.Length) break;  // 已经攒够像素了，不必再读
        }

        if (outLen < outBuf.Length) Array.Resize(ref outBuf, outLen);
        return outBuf;

        void ResetDict()
        {
            for (int i = 0; i < clearCode; i++)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
                firstByte[i] = (byte)i;
            }
            for (int i = clearCode; i < dictSize; i++)
            {
                prefix[i] = -1;
                suffix[i] = 0;
                firstByte[i] = 0;
            }
        }
    }

    /// <summary>
    /// 从 <paramref name="bit"/> 位起读 <paramref name="n"/> 位（**低位在前**，GIF 的位序）。
    /// 越界的位当 0 读，不抛异常 —— 这样「压缩数据少一两个字节」的 GIF 也能解出大部分像素，
    /// 而不是整帧失败。（这里没像别处那样单独抽一个方法：只有 LzwDecode 用得到。）
    /// </summary>
    private static int ReadBits(byte[] data, int bit, int n)
    {
        int v = 0;
        for (int i = 0; i < n; i++)
        {
            int at = bit + i;
            int bi = at >> 3;
            if (bi >= data.Length) break;
            int bitVal = (data[bi] >> (at & 7)) & 1;
            v |= bitVal << (i & 31);
        }
        return v;
    }

    /// <summary>
    /// 隔行（interlace）还原。GIF 的 4 趟：起点 0/4/2/1，步长 8/8/4/2 ——
    /// 注意是**四趟依次把整幅填满**，不是「每隔 8 行取一趟」那种写法。
    /// </summary>
    private static byte[] Deinterlace(byte[] src, int w, int h)
    {
        byte[] dst = new byte[src.Length];
        int row = 0;
        int[] starts = { 0, 4, 2, 1 };
        int[] steps = { 8, 8, 4, 2 };
        for (int pass = 0; pass < 4; pass++)
        {
            // 注意这里**只判 row < h，不判 y < h**：四趟的起点/步长决定了 y 永远落在画布内。
            // 但若解出来的像素数少于 W×H（截断的素材），参考实现会在 Copy 这里越界；
            // 我们按「src/dst 装不下就停」处理，行为一致且不会抛。
            for (int y = starts[pass]; row < h; y += steps[pass])
            {
                if (row * w + w > src.Length || y * w + w > dst.Length) break;
                Array.Copy(src, row * w, dst, y * w, w);
                row++;
            }
        }
        return dst;
    }
}
