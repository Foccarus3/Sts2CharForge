using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

// ==================== 逐帧动画：出计划 + 写文件 ====================
//
// 这一半只负责「把 GIF 变成游戏能吃的东西」：
//   BuildPlan  —— 解 7 个槽位的动图、算公用裁剪框 / 缩放 / Y 偏移、收集提示行（**不写任何文件**）
//   Generate   —— 按计划把每帧裁出来（抠底 / 剥描边）写 PNG + .png.import + SpriteFrames + 清单
// 运行时代码（FrameAnimRuntime.cs / FrameAnimPatches.cs）在 FrameAnimGen 里，不在这。
//
// 几个「为什么这么做」的坑（都踩过，改之前先看）：
//   · **计划按原图算，抠底只在写帧时做**：抠底 / 剥描边都会吃掉内容边界，
//     如果先抠再算裁剪框，同一份素材在不同设置下会裁出不同大小 —— 换设置角色就跳一下。
//   · **裁剪框 / 缩放 / Y 偏移是 7 个动作共用的**：每个动作各裁各的，切换动作时角色会跳位置。
//   · **每帧秒数写在 SpriteFrames 的 frame.duration 上，speed 恒为 1.0**：
//     Godot 的 SpriteFrames.speed 是**倍率不是 fps**，填 30 会额外加速 30 倍（坑过一次）。
//   · **.png.import 里的 ctex 哈希是「源文件 res:// 路径」的 MD5**，不是文件内容的哈希。
//   · 帧图一律重写、`.tres.import` 一律删掉：Godot 会自己重新导入，留着旧 import 会引用错资源。
public static partial class AnimFramesGen
{
    /// <summary>动作名 → 本体「动画触发名」（写进清单的 <c>trigger</c>，运行时要按它翻译请求）。</summary>
    private static readonly Dictionary<string, string> TriggerOf = new(StringComparer.Ordinal)
    {
        ["idle_loop"] = "Idle",
        ["low_health_loop"] = "Idle",
        ["attack"] = "Attack",
        ["cast"] = "Cast",
        ["hurt"] = "Hit",
        ["die"] = "Dead",
        ["overgrowth_loop"] = "Relaxed",
        ["hive_loop"] = "Relaxed",
        ["glory_loop"] = "Relaxed",
        ["relaxed_loop"] = "Relaxed",
    };

    /// <summary>
    /// 出计划：解图 → 算裁剪框 / 缩放 / Y 偏移 → 收提示行。**不写文件、不改 profile**，
    /// 所以界面上「预览动画计划」可以放心反复调；真正落盘在 <see cref="Generate"/>。
    /// </summary>
    public static Plan BuildPlan(CharacterProfile p, Action<string> log)
    {
        var a = p.Animation;
        var notes = new List<string>();
        var anims = new List<AnimPlan>();

        // 没启用 / 没素材：返回一份「空计划」，缩放给 1 而不是 0 —— 调用方拿它去算
        // 注册用的 const float 时，0 会让角色缩成一个点。
        if (!a.Enabled || !a.HasAny)
            return new Plan(anims, 0, 0, 0, 0, 0, 0, 1, 0, notes, false);

        var idle = GifFrames.Decode(a.Idle);
        if (idle == null)
        {
            notes.Add("[错误] 待机动图解不开：" + a.Idle);
            return new Plan(anims, 0, 0, 0, 0, 0, 0, 1, 0, notes, false);
        }

        int idleW = idle.Width, idleH = idle.Height;

        // (动作名, GIF 路径, 解出来的动图, 是否循环, 是否定格)
        var decoded = new List<(string Anim, string Path, GifFrames.Result Gif, bool Loop, bool HoldAtEnd)>();

        foreach (var (anim, path) in a.Slots)
        {
            // 循环 = 待机那一类；死亡是「播完定格」；其余（攻击 / 施法 / 受击）播完回待机
            bool loop = anim is "idle_loop" or "low_health_loop" or "relaxed_loop"
                or "overgrowth_loop" or "hive_loop" or "glory_loop";
            bool die = anim == "die";

            // 单个槽位坏掉只写一行提示就跳过，不让整份生成失败（其它动作照样能用）
            if (!File.Exists(path))
            {
                notes.Add("[错误] " + anim + "：找不到文件 " + path);
                continue;
            }

            var gif = GifFrames.Decode(path);
            if (gif == null)
            {
                notes.Add("[错误] " + anim + "：解不开（只支持 GIF；PNG 序列帧请合成 GIF 或改用手动裁剪）" + path);
                continue;
            }

            if (gif.Width != idleW || gif.Height != idleH)
                notes.Add($"[警告] {anim}：画布 {gif.Width}×{gif.Height} 与待机的 {idleW}×{idleH} 不同，仍按同一裁剪框裁（可能错位）");

            decoded.Add((anim, path, gif, loop, die));
        }

        int x0, y0, x1, y1;
        if (!string.IsNullOrWhiteSpace(a.ManualCrop))
        {
            string[] parts = a.ManualCrop.Split(new[] { ',' }, 4, StringSplitOptions.None);
            if (parts.Length == 4 && parts.All(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                x0 = int.Parse(parts[0], CultureInfo.InvariantCulture);
                y0 = int.Parse(parts[1], CultureInfo.InvariantCulture);
                x1 = int.Parse(parts[2], CultureInfo.InvariantCulture);
                y1 = int.Parse(parts[3], CultureInfo.InvariantCulture);
                notes.Add($"裁剪框用界面指定的：{x0},{y0}..{x1},{y1}");
            }
            else
            {
                // 参考实现在这里对「不是数字」的输入会直接抛 FormatException 把整次生成带崩，
                // 我们改成「当作格式不对 → 改用自动」：用户手打的框打错了不该让整个工程生成失败。
                notes.Add("[警告] 手动裁剪框格式不对（要 x0,y0,x1,y1）：" + a.ManualCrop + "，改用自动");
                (x0, y0, x1, y1) = AutoCrop(idle, idleW, idleH, decoded);
            }
        }
        else
        {
            (x0, y0, x1, y1) = AutoCrop(idle, idleW, idleH, decoded);
        }

        x0 = Math.Clamp(x0, 0, idleW - 1);
        y0 = Math.Clamp(y0, 0, idleH - 1);
        x1 = Math.Clamp(x1, x0 + 1, idleW);   // x1/y1 是**开区间**，至少要留 1 像素
        y1 = Math.Clamp(y1, y0 + 1, idleH);
        int w = x1 - x0;
        int h = y1 - y0;

        foreach (var (anim, path, gif, loop, die) in decoded)
        {
            double seconds = a.FrameSeconds > 0 ? a.FrameSeconds : AverageSeconds(gif);
            string note = gif.Frames.Count + " 帧";
            if (gif.Frames.Count == 1) note += "（只有 1 帧 = 静止图）";
            anims.Add(new AnimPlan(anim, path, loop, die, w, h, gif.Frames.Count, seconds, note));
        }

        double scale = a.Scale;
        if (!(scale > 0))
        {
            int contentH = ContentHeight(idle, 0, x0, y0, x1, y1);
            int staticH = StaticArtHeight(p);
            scale = contentH <= 0 || staticH <= 0 ? 1 : (double)staticH / contentH;
            notes.Add($"缩放自动：静态立绘高 {staticH} ÷ 待机内容高 {contentH} = {scale.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        double yOffset = a.YOffset;
        if (Math.Abs(yOffset) < 1e-9)
        {
            yOffset = AutoYOffset(idle, 0, y0, y1, scale);
            notes.Add($"yOffset 自动：{yOffset.ToString("0.#", CultureInfo.InvariantCulture)}（让帧内内容底边落在静态立绘底边所在的位置）");
        }

        if (!anims.Any(x => x.Anim == "idle_loop"))
            notes.Add("[错误] 待机是回退目标，必须有（其它动作没素材时都会退回它）");
        if (!anims.Any(x => x.Anim == "attack" || x.Anim == "cast"))
            notes.Add("[提示] 没攻击也没技能：打牌时角色只会站着");
        if (!anims.Any(x => x.Anim == "die"))
            notes.Add("[提示] 没死亡素材：死亡时会退回待机");
        if (!anims.Any(x => x.Anim == "overgrowth_loop"))
            notes.Add("[提示] 没篝火素材：篝火场景退回待机");
        if (!anims.Any(x => x.Anim == "relaxed_loop"))
            notes.Add("[提示] 没商店素材：商店场景退回待机");

        return new Plan(anims, x0, y0, x1, y1, w, h, scale, yOffset, notes, true);
    }

    /// <summary>
    /// 落盘：按计划把每帧裁出来（抠底 / 剥描边）写 PNG + <c>.png.import</c>，
    /// 再写该动作的 SpriteFrames（<c>*_frames.tres</c>）和清单（<c>*_manifest.json</c>）。
    /// <paramref name="plan"/> 为 null 时内部先 <see cref="BuildPlan"/>（命令行一把梭的情况）。
    /// </summary>
    public static Plan Generate(CharacterProfile p, string outRoot, Action<string> log, Plan plan)
    {
        plan ??= BuildPlan(p, log);

        if (!plan.Ok)
        {
            // 计划不成立就一行文件都不写：宁可什么都没有，也不要写出半套动画
            // （半套的后果是游戏里动画能播但帧数不对，比完全没有更难查）
            // 顺手给「解不开」那一行补上 GifFrames 记下的**具体原因**（哪个块 / 属于哪种坏法），
            // 不然用户只看到一句「解不开」，不知道该改什么。只在最后一行加，不动上面的判断逻辑。
            if (plan.Notes.Count > 0
                && plan.Notes[plan.Notes.Count - 1].Contains("解不开", StringComparison.Ordinal))
                plan.Notes[plan.Notes.Count - 1] += GifFrames.LastErrorSuffix();
            foreach (var n in plan.Notes) log?.Invoke("  " + n);
            return plan;
        }

        var naming = Naming.From(p);
        string dir = Path.Combine(outRoot, "images", "characters", "anim", naming.CharSlug);
        Directory.CreateDirectory(dir);

        int totalFrames = 0;

        // 同一个 GIF 可能被多个槽位引用，解一次就够（解码是这里最贵的一步）
        var cache = new Dictionary<string, GifFrames.Result?>(StringComparer.Ordinal);

        foreach (var anim in plan.Anims)
        {
            if (!cache.TryGetValue(anim.SourcePath, out var gif))
            {
                gif = GifFrames.Decode(anim.SourcePath);
                cache[anim.SourcePath] = gif;
            }
            if (gif == null) continue;

            var files = new List<string>();
            for (int i = 0; i < gif.Frames.Count; i++)
            {
                byte[] rgba = CropFrame(gif.Frames[i].Rgba, gif.Width, gif.Height,
                    plan.CropX0, plan.CropY0, plan.CropX1, plan.CropY1, p.Animation);
                // 帧名 = <slug>_<anim>_<序号 3 位零填充>.png（和参考实现一致；一张动图不会超过 1000 帧）
                string name = $"{naming.CharSlug}_{anim.Anim}_{i:000}.png";
                string framePath = Path.Combine(dir, name);
                PngUtil.WriteRgba(framePath, plan.Width, plan.Height, rgba);
                files.Add(name);
                WritePngImport(framePath);
            }
            totalFrames += files.Count;

            string tresPath = Path.Combine(dir, naming.CharSlug + "_" + anim.Anim + "_frames.tres");
            ProjectFilesGen.WriteText(tresPath, SpriteFramesTres(naming.CharSlug, anim, files, plan.Width, plan.Height));

            // SpriteFrames 不是「按源文件路径缓存」的资源；同名 .import 是 Godot 给 PNG 用的，
            // 留在 .tres 旁边会变成「同名资源的导入设置」，所以主动删掉。
            try { FsSafe.DeleteFile(tresPath + ".import"); } catch { /* 没权限/不存在都无所谓 */ }

            WriteManifest(dir, naming.CharSlug, anim, plan);

            log?.Invoke($"  {anim.Anim}：{files.Count} 帧 {plan.Width}×{plan.Height}，每帧 {(anim.FrameSeconds * 1000).ToString("0.#", CultureInfo.InvariantCulture)}ms"
                + (anim.HoldAtEnd ? "（播完定格）" : anim.Loop ? "（循环）" : ""));
        }

        foreach (var n in plan.Notes) log?.Invoke("  " + n);
        log?.Invoke($"  动画资源：{totalFrames} 帧 -> images/characters/anim/{naming.CharSlug}/");
        return plan;
    }

    /// <summary>
    /// 裁一帧，然后按设置抠底 / 剥描边。
    /// <paramref name="w"/>/<paramref name="h"/> 是**源图**尺寸，裁剪框越界时按画布外算（补透明），
    /// 这样手动裁剪框填大了也不会崩，只是多一圈透明。
    /// </summary>
    public static byte[] CropFrame(byte[] rgba, int w, int h, int x0, int y0, int x1, int y1, AnimationSpec spec)
    {
        int cw = x1 - x0;
        int ch = y1 - y0;
        byte[] dst = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
        {
            int sy = y0 + y;
            if (sy < 0 || sy >= h) continue;
            for (int x = 0; x < cw; x++)
            {
                int sx = x0 + x;
                if (sx < 0 || sx >= w) continue;
                int si = (sy * w + sx) * 4;
                int di = (y * cw + x) * 4;
                dst[di] = rgba[si];
                dst[di + 1] = rgba[si + 1];
                dst[di + 2] = rgba[si + 2];
                dst[di + 3] = rgba[si + 3];
            }
        }

        if (spec.KeyBackground && spec.KeyTolerance > 0) KeyBackground(dst, spec.KeyTolerance);
        if (spec.StripOutline) StripOutline(dst, cw, ch, spec.OutlineLuminance);
        return dst;
    }

    /// <summary>
    /// 抠纯色底：取头尾两个像素的平均色当底色，RGB 三个通道差值的**最大**值不超过容差的整像素 alpha 清 0。
    /// 容差按通道取最大差（不是欧氏距离）—— 这是参考实现的定义，逐像素对过。
    /// </summary>
    public static void KeyBackground(byte[] rgba, double tolerance)
    {
        int n = rgba.Length / 4;
        if (n == 0) return;

        // 只取「第一个像素」和「最后一个像素」的均值（两点平均，不是「取某一角」：
        // 取平均能压掉一点压缩噪点）。别自作聪明加四角 / 边缘中点：参考实现的探测下标
        // 就是 {0, n-1} —— 它是 new int[2] 之后只写了 [1] = n-1，[0] 留在默认值 0，
        // 反编译出来像四个采样点，其实是两个。多采几个会把底色均值拉偏、抠出窟窿。
        // 代价是「角色压住角上」的素材抠不干净，但这是参考实现的行为，照做。
        int i0 = 0, i1 = n - 1;
        double ar = (rgba[i0 * 4] + rgba[i1 * 4]) / 2.0;
        double ag = (rgba[i0 * 4 + 1] + rgba[i1 * 4 + 1]) / 2.0;
        double ab = (rgba[i0 * 4 + 2] + rgba[i1 * 4 + 2]) / 2.0;

        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            double d = Math.Max(Math.Abs(rgba[o] - ar), Math.Max(Math.Abs(rgba[o + 1] - ag), Math.Abs(rgba[o + 2] - ab)));
            if (d <= tolerance) rgba[o + 3] = 0;
        }
    }

    /// <summary>
    /// 剥最外 1~2 层浅色描边。做法是「从透明像素出发，往浅色（min(R,G,B) &gt; 阈值）且不透明的
    /// 像素做 BFS」，按层记距离，**只剥第 1、2 层**。
    ///
    /// 为什么要限层、而不是「凡是连通的浅色都剥掉」：素材轮廓里常有浅色高光，
    /// 一路连过去会把角色的浅色部分整块挖空（实测把白色盔甲挖成了透明）。
    /// 另外这里**会吃掉不透明像素**（这正是「剥描边」的意图，名字里的警告就是这个意思）。
    /// 还有个兜底：如果剥掉的像素超过全图 2.5%，说明阈值定得太低/素材本来就很浅，
    /// 这时**整块还原**（不改 alpha）—— 宁可留一圈边，也不要把角色剥没。
    /// </summary>
    public static void StripOutline(byte[] rgba, int w, int h, double luminance)
    {
        int n = w * h;
        if (n == 0) return;

        int[] depth = new int[n];
        bool[] opaque = new bool[n];
        bool[] light = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            opaque[i] = rgba[o + 3] > 8;
            light[i] = Math.Min(rgba[o], Math.Min(rgba[o + 1], rgba[o + 2])) > luminance;
            depth[i] = opaque[i] ? 0 : -1;   // -1 = 透明像素（BFS 的起点）
        }

        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (depth[i] != -1) continue;
            foreach (int j in Neighbors(i, w, h))
            {
                if (opaque[j] && light[j] && depth[j] == 0)
                {
                    depth[j] = 1;
                    queue.Enqueue(j);
                }
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int d = depth[i];
            if (d >= 2) continue;                 // 只剥最外 2 层
            foreach (int j in Neighbors(i, w, h))
            {
                if (opaque[j] && light[j] && depth[j] == 0)
                {
                    depth[j] = d + 1;
                    queue.Enqueue(j);
                }
            }
        }

        int removed = 0;
        for (int i = 0; i < n; i++)
        {
            if (depth[i] > 0)
            {
                rgba[i * 4 + 3] = 0;
                removed++;
            }
        }

        if (removed > (int)(0.025 * n))
        {
            for (int i = 0; i < n; i++)
                if (depth[i] > 0) rgba[i * 4 + 3] = 255;
        }
    }

    /// <summary>四邻域（上下左右）。用四邻域不用八邻域：八邻域会从对角「漏」进角色内部的浅色区。</summary>
    private static IEnumerable<int> Neighbors(int i, int w, int h)
    {
        int x = i % w, y = i / w;
        if (y > 0) yield return i - w;
        if (x > 0) yield return i - 1;
        if (x < w - 1) yield return i + 1;
        if (y < h - 1) yield return i + w;
    }

    /// <summary>
    /// 自动裁剪框 = 所有动作所有帧里有内容（alpha &gt; 8）的像素并集，四周各留 8 像素余量。
    ///
    /// 并按「只按待机」补一次：<paramref name="idle"/> 的帧先吃一遍，如果它一个内容像素都没有
    /// （整张全透明），就再吃一遍（等于没补）—— 参考实现就是这么写的，保留原样。
    /// 全透明素材的并集为空时退化成「整幅画布」，至少不会返回一个 1×1 的框。
    /// </summary>
    public static (int X0, int Y0, int X1, int Y1) AutoCrop(GifFrames.Result idle, int width, int height,
        List<(string Anim, string Path, GifFrames.Result Gif, bool Loop, bool HoldAtEnd)> slots)
    {
        var box = new CropBox { X0 = width, Y0 = height, X1 = 0, Y1 = 0 };

        foreach (var s in slots)
            foreach (var f in s.Gif.Frames)
                box.Eat(f.Rgba, s.Gif.Width, s.Gif.Height);

        if (box.X1 <= box.X0)
            foreach (var f in idle.Frames)
                box.Eat(f.Rgba, idle.Width, idle.Height, clamp: true);

        if (box.X1 <= box.X0) return (0, 0, width, height);   // 全透明：退化成整幅

        return (Math.Max(0, box.X0 - 8), Math.Max(0, box.Y0 - 8),
                Math.Min(width, box.X1 + 8), Math.Min(height, box.Y1 + 8));
    }

    /// <summary>
    /// 累积「有内容像素」的并集（<paramref name="clamp"/> 时把结果限制在画布内；
    /// 参考实现里待机补吃那一遍传了 true，其余传 false —— 内容框本来就落在画布内，两者等价）。
    /// </summary>
    private sealed class CropBox
    {
        public int X0, Y0, X1, Y1;

        public void Eat(byte[] rgba, int w, int h, bool clamp = false)
        {
            var (left, top, right, bottom) = ContentBox(rgba, w, h);
            if (right <= left) return;   // 这一帧全透明，不贡献
            if (clamp)
            {
                left = Math.Max(0, left); top = Math.Max(0, top);
                right = Math.Min(w, right); bottom = Math.Min(h, bottom);
            }
            X0 = Math.Min(X0, left);
            Y0 = Math.Min(Y0, top);
            X1 = Math.Max(X1, right);
            Y1 = Math.Max(Y1, bottom);
        }
    }

    /// <summary>
    /// 一帧里「有内容」的范围：<c>(left, top, right, bottom)</c>，right/bottom 是**开区间**。
    /// 判定用 alpha &gt; 8（而不是 &gt; 0）：抠底/缩放后会留下 alpha=1~8 的毛边，
    /// 算进去会让自动裁剪框虚胖一圈。
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) ContentBox(byte[] rgba, int w, int h)
    {
        int left = w, top = h, right = 0, bottom = 0;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (rgba[(row + x) * 4 + 3] <= 8) continue;
                if (x < left) left = x;
                if (x + 1 > right) right = x + 1;
                if (y < top) top = y;
                if (y + 1 > bottom) bottom = y + 1;
            }
        }
        return (left, top, right, bottom);
    }

    /// <summary>
    /// 「待机第 <paramref name="frameIndex"/> 帧的内容高」—— 只数落在裁剪框纵向范围内的内容像素。
    /// 自动缩放用的就是它（静态立绘高 ÷ 它）。
    /// </summary>
    public static int ContentHeight(GifFrames.Result gif, int frameIndex, int x0, int y0, int x1, int y1)
    {
        if (frameIndex >= gif.Frames.Count) return 0;
        var (_, top, _, bottom) = ContentBox(gif.Frames[frameIndex].Rgba, gif.Width, gif.Height);
        top = Math.Max(top, y0);
        bottom = Math.Min(bottom, y1);
        return Math.Max(0, bottom - top);
    }

    /// <summary>
    /// 自动 Y 偏移：让「帧内内容底边」落在静态立绘底边所在的位置（脚不动）。
    ///
    /// 式子 = <c>(H/2 − (clamp(b, y0, y1) − y0)) × scale</c>（H = y1 - y0 = 裁剪框高、
    /// b = 内容的**全画布**底边）。也就是「裁剪框纵向中点 − 内容底边在裁剪框内的位置」：
    /// 负值 = 往上挪。注意 clamp 之后要减 y0 换成裁剪框内坐标 —— 自动裁剪框 y0=0 时看不出来，
    /// 手动裁剪框（y0=12 那种）少减一次 y0 就会差出 260 像素（实测 -97.5 → -357.5）。
    /// 这是拿参考实现跑 7 组实验（含透明底素材、三种裁剪框）逐组核对出来的，别照「直觉」重写。
    /// </summary>
    public static double AutoYOffset(GifFrames.Result gif, int frameIndex, int y0, int y1, double scale)
    {
        if (frameIndex >= gif.Frames.Count) return -97;   // 参考实现的兜底值（不是一个「算出来」的数，就照抄）
        var (_, _, _, bottom) = ContentBox(gif.Frames[frameIndex].Rgba, gif.Width, gif.Height);
        int h = y1 - y0;
        // = 半个裁剪框高 − 内容底边在**裁剪框内**的坐标（负值 = 往上挪，让脚落在静态立绘底边）。
        //
        // 关键在 clamp(bottom, y0, y1) 之后还要再减一次 y0：bottom 是**全画布**坐标，
        // 而这里要的是「裁剪框内」坐标。少减这个 y0，手动裁剪框（y0≠0）会算错，
        // 实测 7,12,13,21 那档从 -97.5 变成 -357.5；自动裁剪框（y0=0）恰好看不出来。
        // 这个式子是拿参考实现跑了 7 组（含透明底素材、几种裁剪框）拟合+逐组核对出来的，别再化简。
        return (h / 2.0 - (Math.Clamp(bottom, y0, y1) - y0)) * scale;
    }

    /// <summary>
    /// 「静态立绘的高」—— 自动缩放的分子。从「美术资源」页那张战斗立绘 PNG 上量。
    /// 读不到就按 195（本工具一直用的兜底值，保证没配美术时缩放也是个合理数）。
    /// </summary>
    public static int StaticArtHeight(CharacterProfile p)
    {
        string? path = p.Art.CharacterStatic;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var img = PngUtil.Decode(path);
            if (img.HasValue && img.Value.H > 0) return img.Value.H;
        }
        return 195;
    }

    /// <summary>一帧的平均显示时长（秒）。没有帧时给 1/30 秒，免得除出 0 让游戏里「每帧 0 秒」空转。</summary>
    public static double AverageSeconds(GifFrames.Result gif)
    {
        if (gif.Frames.Count == 0) return 1.0 / 30.0;
        return gif.Frames.Average(f => (double)f.DelayMs) / 1000.0;
    }

    /// <summary>
    /// 生成 Godot 的 SpriteFrames 资源文本。
    /// <c>load_steps = 帧数 + 1</c>（1 是 [resource] 自己）；每帧一个 ext_resource（id=t0/t1/…）；
    /// <c>speed</c> 恒为 1.0，时长写在每个 frame 的 <c>duration</c> 上（见文件头的坑）。
    /// </summary>
    public static string SpriteFramesTres(string slug, AnimPlan anim, List<string> frames, int width, int height)
    {
        var w = new CodeWriter();
        w.Line($"[gd_resource type=\"SpriteFrames\" load_steps={frames.Count + 1} format=3]");
        w.Line();
        for (int i = 0; i < frames.Count; i++)
            w.Line($"[ext_resource type=\"Texture2D\" path=\"res://images/characters/anim/{slug}/{frames[i]}\" id=\"t{i}\"]");
        w.Line();
        w.Line("[resource]");
        w.Line("animations = [{");
        w.Line("\"frames\": [");
        for (int i = 0; i < frames.Count; i++)
        {
            w.Line("{");
            // 0.#### = 最多 4 位小数：100ms→0.1，20ms→0.02（再多的位数 Godot 也不看）
            w.Line("\"duration\": " + anim.FrameSeconds.ToString("0.####", CultureInfo.InvariantCulture) + ",");
            w.Line($"\"texture\": ExtResource(\"t{i}\")");
            w.Line(i < frames.Count - 1 ? "}," : "}");
        }
        w.Line("],");
        w.Line("\"loop\": " + (anim.Loop ? "true" : "false") + ",");
        w.Line("\"name\": &\"" + anim.Anim + "\",");
        w.Line("\"speed\": 1.0");
        w.Line("}]");
        w.Line();
        return w.ToString();
    }

    /// <summary>
    /// 写 <c>&lt;png&gt;.import</c>。Godot 的导入缓存文件名是
    /// <c>&lt;文件名&gt;-&lt;md5(源文件 res:// 路径)&gt;.ctex</c> —— 哈希算的是**路径**不是内容。
    /// 参数里写死「不做压缩 / 不生成 mipmap」，和参考实现一致。
    /// </summary>
    public static void WritePngImport(string pngPath)
    {
        string norm = pngPath.Replace('\\', '/');
        // 取 "/images/" 之后的部分（把 outRoot 之前的前缀裁掉），保证 res:// 路径稳定：
        // 输出目录换到别处时，res:// 仍然从 images/ 开始。
        string res = "res://" + norm.Substring(norm.IndexOf("/images/", StringComparison.Ordinal) + 1);

        var w = new CodeWriter();
        w.Line("[remap]");
        w.Line();
        w.Line("importer=\"texture\"");
        w.Line("type=\"CompressedTexture2D\"");
        w.Line("uid=\"\"");
        w.Line($"path=\"res://.godot/imported/{Path.GetFileName(pngPath)}-{Hash(res)}.ctex\"");
        w.Line();
        w.Line("[deps]");
        w.Line();
        w.Line("source_file=\"" + res + "\"");
        w.Line($"dest_files=[\"res://.godot/imported/{Path.GetFileName(pngPath)}-{Hash(res)}.ctex\"]");
        w.Line();
        w.Line("[params]");
        w.Line();
        w.Line("compress/mode=0");
        w.Line("compress/high_quality=false");
        w.Line("compress/lossy_quality=0.7");
        w.Line("compress/hdr_compression=1");
        w.Line("compress/normal_map=0");
        w.Line("compress/channel_pack=0");
        w.Line("mipmaps/generate=false");
        w.Line("mipmaps/limit=-1");
        w.Line("roughness/mode=0");
        w.Line("roughness/src_normal=\"\"");
        w.Line("process/fix_alpha_border=false");
        w.Line("process/premult_alpha=false");
        w.Line("process/normal_map_invert_y=false");
        w.Line("process/hdr_as_srgb=false");
        w.Line("process/hdr_clamp_exposure=false");
        w.Line("process/size_limit=0");
        w.Line("detect_3d/compress_to=1");
        ProjectFilesGen.WriteText(pngPath + ".import", w.ToString());
    }

    /// <summary>字符串的 MD5（小写十六进制）。只用于 Godot 的导入缓存文件名，不涉及安全。</summary>
    public static string Hash(string text)
    {
        using var md5 = MD5.Create();
        string hex = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(text)));
        return hex.ToLowerInvariant();
    }

    /// <summary>
    /// 写该动作的清单（<c>&lt;slug&gt;_&lt;anim&gt;_manifest.json</c>）。
    /// 运行时不读它，它是给「用户想自己核对 / 想手改」用的：裁剪框、缩放、Y 偏移、触发名全在里面。
    /// 数字一律 InvariantCulture —— 中文系统的小数点是「.」没错，但换个语言环境就可能变成「,」，
    /// 那样 JSON 直接读不出来（这个坑在别处踩过）。
    /// </summary>
    public static void WriteManifest(string dir, string slug, AnimPlan anim, Plan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"anim\": \"{anim.Anim}\",");
        sb.AppendLine($"  \"loop\": {(anim.Loop ? "true" : "false")},");
        sb.AppendLine($"  \"holdAtEnd\": {(anim.HoldAtEnd ? "true" : "false")},");
        sb.AppendLine($"  \"frames\": {anim.FrameCount},");
        sb.AppendLine($"  \"frameSeconds\": {anim.FrameSeconds.ToString("0.#####", CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"size\": [{plan.Width}, {plan.Height}],");
        sb.AppendLine($"  \"crop\": [{plan.CropX0}, {plan.CropY0}, {plan.CropX1}, {plan.CropY1}],");
        sb.AppendLine($"  \"scale\": {plan.Scale.ToString("0.####", CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"yOffset\": {plan.YOffset.ToString("0.###", CultureInfo.InvariantCulture)},");
        sb.AppendLine($"  \"trigger\": \"{(TriggerOf.TryGetValue(anim.Anim, out var t) ? t : "Idle")}\"");
        sb.AppendLine("}");
        ProjectFilesGen.WriteText(Path.Combine(dir, slug + "_" + anim.Anim + "_manifest.json"), sb.ToString());
    }
}
