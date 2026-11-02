using System;
using System.Collections.Generic;

namespace Sts2CharForge.Core.Plugins;

/// <summary>
/// V0.2.387（需求书第 4.1 节）：**编辑器扩展插件**的入口契约。
///
/// <para>和现有「声明式插件」的区别：声明式插件只能声明字段 + 模板；编辑器扩展插件可以**用自己的代码**
/// 造页面、接按钮事件、维护自己的对象模型 —— 这是复刻老 EnemyForge 那条路。</para>
///
/// <para>安全前提（需求书第 6 节）：只有 plugin.json 里**显式**写了 <c>editorExtension</c> 的插件才会加载；
/// 程序集必须来自该插件自己的目录；所有调用点都由主程序 try/catch 隔离。</para>
/// </summary>
public interface ICharForgeEditorExtension
{
    /// <summary>插件声明支持的扩展 API 版本（目前只有 "1"）。</summary>
    string ApiVersion { get; }

    /// <summary>造出这个插件要挂的页面（可以 0 个、也可以多个）。抛异常 = 这个扩展加载失败，不影响其它插件。</summary>
    IReadOnlyList<IEditorPage> CreatePages(IEditorHost host);
}

/// <summary>
/// V0.2.387：插件能看到的**受控**宿主能力（需求书第 6.2 节「权限范围」）。
/// 刻意不暴露主窗口、反射、任意文件系统、网络与进程 —— 插件只能通过这里做官方允许的事。
/// </summary>
public interface IEditorHost
{
    /// <summary>插件 id（等于 plugin.json 里的 id）。</summary>
    string PluginId { get; }

    /// <summary>插件自己的目录（资源、扩展程序集都在这里）。</summary>
    string PluginDir { get; }

    /// <summary>当前工程允许写出的目录（生成用）。</summary>
    string OutputDir { get; }

    /// <summary>写一行日志到插件详情页（便于审核与排查）。</summary>
    void Log(string message);

    /// <summary>V0.2.388：造一个受控面板（插件页面里的界面都从它开始加控件）。没有界面环境时返回 null。</summary>
    IEditorPanel? CreatePanel();
}

/// <summary>V0.2.387：一个自定义页面。界面由插件通过 <see cref="Build"/> 里的受控控件 API 组装。</summary>
public interface IEditorPage
{
    string Id { get; }
    string Title { get; }

    /// <summary>建界面（返回受控控件树）。由主程序在 try/catch 里调用，抛异常只会让这一页显示失败提示。</summary>
    object Build();

    /// <summary>页面关闭 / 插件重载时释放资源。</summary>
    void Dispose();
}

/// <summary>V0.2.387：一个插件声明的编辑器扩展（来自 plugin.json 的 <c>editorExtension</c> 段）。</summary>
public sealed class EditorExtensionSpec
{
    /// <summary>入口类型名：<c>Namespaced.Type, AssemblyName</c>（和 Type.GetType 的写法一致）。</summary>
    public string Entry { get; set; } = "";

    /// <summary>插件要求的扩展 API 版本（空 = "1"）。</summary>
    public string ApiVersion { get; set; } = "1";

    public bool Ok => !string.IsNullOrWhiteSpace(Entry);
}

/// <summary>V0.2.387：一个扩展的加载状态（需求书第 6.4 节「审核方式」要显示的那些字段）。</summary>
public sealed class EditorExtensionStatus
{
    public string PluginId { get; set; } = "";
    public string Entry { get; set; } = "";
    public string ApiVersion { get; set; } = "";
    public bool Declared { get; set; }
    public bool Loaded { get; set; }
    public bool Disabled { get; set; }
    public string Error { get; set; } = "";
    public int PageCount { get; set; }
    public string LastLog { get; set; } = "";

    public string Summary => !Declared
        ? "无编辑器扩展（声明式插件）"
        : Disabled ? "已禁用"
        : Loaded ? $"已加载（{PageCount} 个页面）"
        : "加载失败：" + Error;
}
