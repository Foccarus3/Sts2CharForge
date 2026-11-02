using System;
using System.Collections.Generic;
using System.Linq;
using Sts2CharForge.Core.Plugins;

namespace Sts2CharForge.App;

/// <summary>
/// V0.2.401：**示例编辑器扩展** —— 给想写自己扩展的插件作者当模板。
///
/// <para>它演示了整条链路：</para>
/// <list type="bullet">
///   <item>用 <see cref="IPluginStore{T}"/> 存自己的对象模型（<c>data/project.json</c>，带 schemaVersion）；</item>
///   <item>用 <see cref="IEditorList{T}"/> 做「左侧列表」、<see cref="IEditorPanel"/> 做「右侧详情」；</item>
///   <item>按钮回调里改数据 → Save → Refresh（受控事件）；</item>
///   <item>用 <see cref="IEditorHost.Generate"/> 输出多个文件（用户会先看到文件清单确认窗）。</item>
/// </list>
///
/// <para>入口写法（plugin.json）：<c>"entry": "Sts2CharForge.App.DemoEditorExtension, Sts2CharForge.App"</c>。
/// 你自己的扩展放在插件目录里，写成 <c>"你的命名空间.类名, 你的程序集名"</c> 即可。</para>
/// </summary>
public sealed class DemoEditorExtension : ICharForgeEditorExtension
{
    public string ApiVersion => "1";

    public IReadOnlyList<IEditorPage> CreatePages(IEditorHost host)
    {
        var store = host.GetStore<DemoProject>("project");
        if (store is null) return Array.Empty<IEditorPage>();
        return new List<IEditorPage> { new DemoEnemyPage(host, store) };
    }
}

/// <summary>示例数据模型（真实插件里就是你的 EnemyProject）。</summary>
public sealed class DemoProject
{
    public int SchemaVersion { get; set; } = 1;
    public List<DemoEnemy> Enemies { get; set; } = new();
}

public sealed class DemoEnemy
{
    public string Class { get; set; } = "DemoEnemy";
    public string Name { get; set; } = "示例敌人";
    public int Hp { get; set; } = 30;
    public string Moves { get; set; } = "";
}

/// <summary>示例页面：左列表 + 右详情 + 生成按钮。</summary>
public sealed class DemoEnemyPage : IEditorPage
{
    private readonly IEditorHost _host;
    private readonly IPluginStore<DemoProject> _store;

    public DemoEnemyPage(IEditorHost host, IPluginStore<DemoProject> store)
    {
        _host = host;
        _store = store;
    }

    public string Id => "demo-enemies";
    public string Title => "示例：敌人";

    public object Build()
    {
        var panel = _host.CreatePanel();
        if (panel is null) return new System.Windows.Controls.StackPanel();

        DemoProject project = _store.Value;
        if (project.Enemies.Count == 0)
        {
            project.Enemies.Add(new DemoEnemy { Class = "DemoGoblin", Name = "哥布林", Hp = 22, Moves = "attack:6;block:5" });
            _store.Save();
        }

        panel.AddText("这是示例扩展：左边选敌人，右边改属性；「生成」会写出 C#（你会先看到文件清单）。");

        var list = panel.AddList("敌人", project.Enemies, e => e.Class + "（" + e.Name + "）", e => new DemoEnemy
        {
            Class = e.Class + "Copy",
            Name = e.Name,
            Hp = e.Hp,
            Moves = e.Moves,
        });

        var detail = panel.AddPanel("详情");
        var cls = detail.AddTextBox("类名", "", v => { });
        var name = detail.AddTextBox("显示名", "", v => { });
        var hp = detail.AddNumber("生命", 30m, v => { });
        var moves = detail.AddTextBox("招式", "", v => { });

        void Fill(DemoEnemy? e)
        {
            cls.Text = e?.Class ?? "";
            name.Text = e?.Name ?? "";
            hp.Value = e?.Hp ?? 0;
            moves.Text = e?.Moves ?? "";
        }
        Fill(list.Selected);
        list.SelectionChanged += e => Fill(e);

        detail.AddButton("保存到 data/project.json", () =>
        {
            if (list.Selected is not DemoEnemy sel) return;
            sel.Class = cls.Text;
            sel.Name = name.Text;
            sel.Hp = (int)hp.Value;
            sel.Moves = moves.Text;
            _store.Save();
            _host.Log("示例扩展：已保存 " + sel.Class);
        });

        detail.AddButton("生成 C#", () =>
        {
            var files = project.Enemies.Select(e => new EditorOutputFile
            {
                Path = "Enemies/" + (string.IsNullOrWhiteSpace(e.Class) ? "Unnamed" : e.Class) + ".cs",
                Content = "// 由示例编辑器扩展生成\npublic sealed class " + (string.IsNullOrWhiteSpace(e.Class) ? "Unnamed" : e.Class) + "\n{\n"
                          + "    public string Name = \"" + (e.Name ?? "") + "\";\n"
                          + "    public int Hp = " + e.Hp + ";\n"
                          + "    public string Moves = \"" + (e.Moves ?? "") + "\";\n}\n",
            }).ToList();
            if (files.Count == 0) { _host.ReportProblem("还没有敌人，没东西可生成"); return; }
            var result = _host.Generate(files);
            _host.Log("示例扩展：生成 —— " + result.Summary);
            if (result.Problems.Count > 0) _host.ReportProblem(result.Summary);
        });

        return panel;
    }

    public void Dispose() { }
}
