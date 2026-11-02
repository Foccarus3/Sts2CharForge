using System;
using System.Collections.Generic;
using System.IO;

namespace Sts2CharForge.Core.Plugins;

/// <summary>V0.2.392（需求书 6.4 / 第一期「多文件生成」）：插件要写出的一份文件。</summary>
public sealed class EditorOutputFile
{
    /// <summary>相对输出目录的路径（可以带子目录）。</summary>
    public string Path { get; set; } = "";

    /// <summary>文件内容（UTF-8 写出）。<see cref="AssetPath"/> 非空时忽略它。</summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// V0.2.397（第二期收尾）：要**复制**的资源（相对插件目录，例如 <c>assets/hero.png</c>）。
    /// 非空 = 这次输出的是二进制资源，内容从插件目录原样拷过去。
    /// </summary>
    public string AssetPath { get; set; } = "";

    public bool IsAsset => !string.IsNullOrWhiteSpace(AssetPath);
}

/// <summary>V0.2.392：生成**计划**（还没写盘）—— 界面拿它给用户看文件清单。</summary>
public sealed class EditorGeneratePlan
{
    public string PluginId { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public List<EditorOutputFile> Files { get; set; } = new();

    /// <summary>计划里发现的问题（越界路径 / 空文件名 …）。非空 = 不允许写。</summary>
    public List<string> Problems { get; set; } = new();

    public bool Ok => Problems.Count == 0 && Files.Count > 0;

    /// <summary>给界面看的一行摘要。</summary>
    public string Summary => Problems.Count > 0
        ? $"不能生成：{Problems.Count} 个问题（{string.Join("；", Problems)}）"
        : $"{Files.Count} 个文件 → {OutputDir}";
}

/// <summary>V0.2.392：生成结果。</summary>
public sealed class EditorGenerateResult
{
    public bool Confirmed { get; set; }
    public List<string> Written { get; set; } = new();
    public List<string> Problems { get; set; } = new();

    public string Summary => !Confirmed
        ? "用户取消了生成" + (Problems.Count > 0 ? "（" + string.Join("；", Problems) + "）" : "")
        : Problems.Count > 0
            ? $"写了 {Written.Count} 个文件，{Problems.Count} 个问题"
            : $"已写出 {Written.Count} 个文件";
}
