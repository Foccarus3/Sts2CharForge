using System;
using System.Collections.Generic;

namespace Sts2CharForge.Core.Plugins;

// V0.2.388（需求书第 4.2 节「受控界面 API」）：编辑器扩展能用的**受控控件**。
// 刻意不给任意 WPF：插件只能通过 IEditorPanel 加这几种控件，主程序负责把它们变成真实界面。
// 这样「插件不会反射主窗口 / 不会改私有控件」这条审核要求才成立。

/// <summary>一个受控面板（根容器）。插件用它在页面里加控件。</summary>
public interface IEditorPanel
{
    /// <summary>加一行说明文字（不可编辑）。</summary>
    IEditorText AddText(string text);

    /// <summary>加一个文本输入框。</summary>
    IEditorTextBox AddTextBox(string label, string value, Action<string>? onChange = null);

    /// <summary>加一个数字输入框（非数字会被拒绝并保留原值）。</summary>
    IEditorNumberBox AddNumber(string label, decimal value, Action<decimal>? onChange = null);

    /// <summary>加一个勾选框。</summary>
    IEditorCheckBox AddCheck(string label, bool value, Action<bool>? onChange = null);

    /// <summary>加一个下拉框。</summary>
    IEditorCombo AddCombo(string label, IReadOnlyList<string> options, string value, Action<string>? onChange = null);

    /// <summary>加一个按钮。</summary>
    IEditorButton AddButton(string text, Action onClick);

    /// <summary>加一个可嵌套的子面板（用于分组 / 左右两栏）。</summary>
    IEditorPanel AddPanel(string? title = null);

    /// <summary>清空本面板里所有控件。</summary>
    void Clear();
}

/// <summary>说明文字。</summary>
public interface IEditorText
{
    string Text { get; set; }
}

/// <summary>文本输入框。</summary>
public interface IEditorTextBox
{
    string Text { get; set; }
    bool ReadOnly { get; set; }
}

/// <summary>数字输入框。</summary>
public interface IEditorNumberBox
{
    decimal Value { get; set; }
}

/// <summary>勾选框。</summary>
public interface IEditorCheckBox
{
    bool IsChecked { get; set; }
}

/// <summary>下拉框。</summary>
public interface IEditorCombo
{
    string Value { get; set; }
    void SetOptions(IReadOnlyList<string> options);
}

/// <summary>按钮（插件在这里挂点击回调）。</summary>
public interface IEditorButton
{
    string Text { get; set; }
    bool Enabled { get; set; }
}
