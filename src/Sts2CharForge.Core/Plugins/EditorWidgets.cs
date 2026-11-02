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

    /// <summary>
    /// V0.2.390（需求书 4.3 节）：受控**列表** —— 带选中项、增删改排、选中变化事件。
    /// 「敌人列表 + 右侧详情」这种界面就是它 + <see cref="IEditorPanel"/> 拼出来的。
    /// </summary>
    IEditorList<T> AddList<T>(string label, IReadOnlyList<T> items, Func<T, string> labelOf, Func<T, T>? cloner = null) where T : class;

    /// <summary>V0.2.395（需求书 4.9 / 第二期）：加一个**文件选择**栏（选图 / 选音效 / 选任意文件）。</summary>
    IEditorFilePicker AddFilePicker(string label, string filter, string value, Action<string>? onChange = null);

    /// <summary>V0.2.395：加一个**图片预览**（给插件显示选中的 PNG）。</summary>
    IEditorImagePreview AddImagePreview(string label, int height = 160);

    /// <summary>V0.2.396（需求书 4.9）：加一个**音频预览**（音效 / BGM 试听，播放/停止）。</summary>
    IEditorAudioPreview AddAudioPreview(string label);

    /// <summary>V0.2.396：加一个**逐帧动画预览**（给一个装着 PNG 序列的文件夹，能逐帧翻）。</summary>
    IEditorFramePreview AddFramePreview(string label, int height = 160);

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

/// <summary>V0.2.396：音频预览（设置路径即可；坏路径只显示提示，不抛异常）。</summary>
public interface IEditorAudioPreview
{
    string Path { get; set; }
}

/// <summary>
/// V0.2.396：逐帧动画预览。给一个**文件夹**（里面按文件名排好的 PNG 序列），
/// 插件可以设当前帧；界面提供「◀ / ▶」翻帧。
/// </summary>
public interface IEditorFramePreview
{
    /// <summary>帧所在文件夹（绝对路径；空 = 没设）。</summary>
    string Folder { get; set; }

    /// <summary>这个文件夹里被当成帧的图片数（只读，方便插件校验「至少要 1 帧」）。</summary>
    int FrameCount { get; }

    /// <summary>当前显示第几帧（0 起）。</summary>
    int FrameIndex { get; set; }
}
/// <summary>V0.2.395（需求书 4.9）：文件选择栏 —— 插件只能拿到**用户选的路径**，不能自己乱翻文件系统。</summary>
public interface IEditorFilePicker
{
    /// <summary>当前选中的文件（绝对路径；空 = 没选）。</summary>
    string Value { get; set; }
}

/// <summary>V0.2.395：图片预览（SetPath 传绝对路径；文件不存在 / 不是图片时显示提示，不抛异常）。</summary>
public interface IEditorImagePreview
{
    string Path { get; set; }
}
/// <summary>V0.2.390：受控列表（需求书 4.3）。选中项 / 增删改排 / 选中变化事件都在这里。</summary>
public interface IEditorList<T> where T : class
{
    IReadOnlyList<T> Items { get; }

    /// <summary>当前选中项（设 null = 取消选中）。</summary>
    T? Selected { get; set; }

    /// <summary>选中项变了（刷列表导致的重建不会误触发）。</summary>
    event Action<T?>? SelectionChanged;

    void Add(T item);
    void Remove(T item);
    void MoveUp(T item);
    void MoveDown(T item);

    /// <summary>复制一项（用创建列表时给的 cloner；没给就用浅拷贝）。</summary>
    void Clone(T item);

    /// <summary>数据外部变了之后重画列表（选中项尽量保持）。</summary>
    void Refresh();
}
/// <summary>按钮（插件在这里挂点击回调）。</summary>
public interface IEditorButton
{
    string Text { get; set; }
    bool Enabled { get; set; }
}
