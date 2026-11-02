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
		if (host is not null) UicheckStoreHost.Probe(host);
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
	public bool Clicked { get; private set; }
	public object Build()
	{
		var panel = new EditorPanelAdapter("uicheck");
		panel.AddText("说明：这是插件造的页面");
		var box = panel.AddTextBox("名字", "横扫", v => { });
		panel.AddButton("点我", () => { Clicked = true; box.Text = "点过了"; });
		panel.AddNumber("伤害", 8m, v => { });
		panel.AddCheck("是攻击", true, v => { });
		panel.AddCombo("类型", new[] { "attack", "block" }, "attack", v => { });
		var sub = panel.AddPanel("动作");
		sub.AddText("子面板");
		return panel;
	}
	public void Dispose() { }
}
/// <summary>V0.2.388 自检用：Build() 就抛异常的页面。</summary>
public sealed class UicheckBadPageExtension : Sts2CharForge.Core.Plugins.ICharForgeEditorExtension
{
	public string ApiVersion => "1";
	public IReadOnlyList<Sts2CharForge.Core.Plugins.IEditorPage> CreatePages(Sts2CharForge.Core.Plugins.IEditorHost host)
		=> new List<Sts2CharForge.Core.Plugins.IEditorPage> { new UicheckBadPage() };
}

public sealed class UicheckBadPage : Sts2CharForge.Core.Plugins.IEditorPage
{
	public string Id => "bad";
	public string Title => "坏页";
	public object Build() => throw new InvalidOperationException("页面炸了");
	public void Dispose() { }
}
/// <summary>V0.2.390 自检用：列表里的一项。</summary>
public sealed class UicheckMove
{
	public string Name { get; set; } = "";
	public override string ToString() => Name;
}
/// <summary>V0.2.391 自检用：插件自己的对象模型。</summary>
public sealed class UicheckProject
{
	public string Name { get; set; } = "";
	public int Count { get; set; }
}

/// <summary>V0.2.391 自检用：记录插件拿到过的存储（并验证危险名字被拒）。</summary>
public static class UicheckStoreHost
{
	public static Sts2CharForge.Core.Plugins.IPluginStore<UicheckProject>? LastStore;
	public static bool SawUnsafeStore;

	public static void Probe(Sts2CharForge.Core.Plugins.IEditorHost host)
	{
		LastStore = host.GetStore<UicheckProject>("project");
		SawUnsafeStore = host.GetStore<UicheckProject>("../escape") is not null
			|| host.GetStore<UicheckProject>("a/b") is not null;
	}
}