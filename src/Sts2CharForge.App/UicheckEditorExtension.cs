using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Sts2CharForge.App;

/// <summary>V0.2.387 自检用：一个正常的编辑器扩展（两个页面）。</summary>
public sealed class UicheckEditorExtension : Sts2CharForge.Core.Plugins.ICharForgeEditorExtension
{
	public string ApiVersion => "1";
	public IReadOnlyList<Sts2CharForge.Core.Plugins.IEditorPage> CreatePages(Sts2CharForge.Core.Plugins.IEditorHost host)
	{
		host?.Log("uicheck: 建页面");
		return new List<Sts2CharForge.Core.Plugins.IEditorPage> { new UicheckPage("p1", "页一"), new UicheckPage("p2", "页二") };
	}
}

/// <summary>V0.2.387 自检用：初始化就抛异常的扩展。</summary>
public sealed class UicheckThrowingExtension : Sts2CharForge.Core.Plugins.ICharForgeEditorExtension
{
	public string ApiVersion => "1";
	public IReadOnlyList<Sts2CharForge.Core.Plugins.IEditorPage> CreatePages(Sts2CharForge.Core.Plugins.IEditorHost host)
		=> throw new InvalidOperationException("故意炸的");
}

/// <summary>V0.2.387：最小的编辑器页面（界面渲染在下一版接）。</summary>
public sealed class UicheckPage : Sts2CharForge.Core.Plugins.IEditorPage
{
	public UicheckPage(string id, string title) { Id = id; Title = title; }
	public string Id { get; }
	public string Title { get; }
	public object Build() => new StackPanel();
	public void Dispose() { }
}