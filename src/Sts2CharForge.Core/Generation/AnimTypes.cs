using System;
using System.Collections.Generic;

namespace Sts2CharForge.Core.Generation;

// ==================== 逐帧动画（动图拆帧）的公共数据 ====================
// 见 docs\逐帧动画_吸收设计.md。
//
// 为什么单独一个文件、而且要写成 partial：
//   ① 这两个 record 是「解码结果」和「生成计划」，**解码 / 规划 / 写文件 / 出运行时**四块都要用，
//      放在这里是为了让那几块各占一个文件、互不重叠（改一处不会把别的文件搞脏）；
//   ② 类型名 / 字段名和参考实现（那份只有二进制的改造版）**逐字一致**，
//      这样「同一份输入 → 逐字节对比输出」的验收脚本可以直接对我们的实现跑。
// 字段含义：
//   GifFrames.Frame   —— 一帧解出来的 RGBA（长 4×W×H）+ 这一帧的显示时长（毫秒）
//   GifFrames.Result  —— 整张动图（画布尺寸 + 帧序列）
//   AnimFramesGen.AnimPlan —— 一个动作的计划（哪个动作、源文件、循环/定格、裁剪后的尺寸、帧数、每帧秒数、给用户看的说明）
//   AnimFramesGen.Plan     —— 整份计划（各动作 + 全局裁剪框 + 缩放/位置 + 提示行 + 能不能用）

/// <summary>自己写的 GIF 解码器（Core 不能依赖 WPF / System.Drawing，所以手写 LZW）。</summary>
public static partial class GifFrames
{
    /// <summary>一帧：<paramref name="Rgba"/> 长度 = 4 × W × H（顺序 R,G,B,A），<paramref name="DelayMs"/> = 这一帧显示多少毫秒。</summary>
    public sealed record Frame(byte[] Rgba, int DelayMs);

    /// <summary>一张解开的动图：画布尺寸 + 帧序列（已经按处置方式合成到同一块画布上）。</summary>
    public sealed record Result(int Width, int Height, List<Frame> Frames);
}

/// <summary>「按计划拆帧 → 裁剪 / 抠底 / 剥描边 → 写 PNG + SpriteFrames + 清单」的生成器。</summary>
public static partial class AnimFramesGen
{
    /// <summary>一个动作的计划。</summary>
    /// <param name="Anim">本体动画名（idle_loop / attack / cast / hurt / die / overgrowth_loop / relaxed_loop）</param>
    /// <param name="SourcePath">素材来源（GIF 路径）</param>
    /// <param name="Loop">是不是循环动画（待机 / 篝火 / 商店是）</param>
    /// <param name="HoldAtEnd">播完定格在最后一帧（死亡用）</param>
    /// <param name="Width">裁剪后的宽、<paramref name="Height"/> 裁剪后的高</param>
    /// <param name="FrameCount">帧数</param>
    /// <param name="FrameSeconds">每帧显示多少秒（已经把「覆盖值 / 平均延时 / 速度系数」算完）</param>
    /// <param name="Note">给用户看的一句（例：<c>3 帧</c>、<c>只有 1 帧 = 静止图</c>）</param>
    public sealed record AnimPlan(string Anim, string SourcePath, bool Loop, bool HoldAtEnd,
        int Width, int Height, int FrameCount, double FrameSeconds, string Note);

    /// <summary>整份计划：所有动作共用同一个裁剪框和缩放 / 位置（换动作时角色不跳位置）。</summary>
    /// <param name="Anims">各动作（至少有待机）</param>
    /// <param name="CropX0">裁剪框（x1 / y1 是开区间）</param>
    /// <param name="Width">裁剪框的宽、<paramref name="Height"/> 裁剪框的高</param>
    /// <param name="Scale">缩放（0 值已被换成自动算出来的值）</param>
    /// <param name="YOffset">Y 偏移（0 值已被换成自动算出来的值）</param>
    /// <param name="Notes">提示 / 警告 / 错误行（原样显示给用户）</param>
    /// <param name="Ok">能不能用（待机解不开 / 没有待机 → false，这时不要生成任何动画文件）</param>
    public sealed record Plan(List<AnimPlan> Anims, int CropX0, int CropY0, int CropX1, int CropY1,
        int Width, int Height, double Scale, double YOffset, List<string> Notes, bool Ok);
}
