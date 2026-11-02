# V0.2.388 — 受控控件 API + 插件页面渲染（需求书第 4.2 节）

V0.2.387 只做到「能安全加载插件代码」；这一版让插件的 `Build()` 真的**变成界面**。

## 受控控件（Core：`Plugins/EditorWidgets.cs`）

插件只能通过 `IEditorPanel` 加这几种控件（没有任意 WPF）：

```csharp
IEditorText    AddText(string text)
IEditorTextBox AddTextBox(string label, string value, Action<string>? onChange)
IEditorNumberBox AddNumber(string label, decimal value, Action<decimal>? onChange)
IEditorCheckBox  AddCheck(string label, bool value, Action<bool>? onChange)
IEditorCombo     AddCombo(string label, IReadOnlyList<string> options, string value, Action<string>? onChange)
IEditorButton    AddButton(string text, Action onClick)
IEditorPanel     AddPanel(string? title)      // 可以嵌套（分组 / 左右两栏）
void             Clear()
```

`IEditorHost.CreatePanel()` 拿根面板；`EditorExtensionRegistry.PanelFactory` 由界面层注入（Core 不引用 WPF）。

## 渲染（App：`EditorPanelAdapter` / `PluginExtensionPages`）

* `EditorPanelAdapter` 把受控面板变成真实 WPF 控件（TextBlock / TextBox / CheckBox / ComboBox / Button / 嵌套 Border）；
* `PluginExtensionPages.BuildSection(plugin, dir, outDir)` —— 每个页面一个 Tab，页面单独 try/catch：
  **某一页崩了只显示「页面创建失败（已隔离）」，其它页照常、主程序不崩**；
* 插件回调（按钮点击 / 输入变化）也包在 `Guard` 里：抛异常只弹一条提示，不把异常抛回主程序；
* 插件如果绕开受控面板直接返回任意 WPF 元素：**不拒绝，但界面顶部标一条橙色警告**「这类插件需要人工审核」
  —— 与需求书 6.4 的审核口径一致（不偷偷放行）。

## 自检

**4743 PASS / 0 FAIL**（+3 条）：
两个 Tab + 文字/输入框/按钮/数字/勾选/下拉/子面板都渲染出来（文字 15 个、按钮 2 个）/
按钮点击回调能跑 / 页面 `Build()` 抛异常被隔离（显示「页面创建失败（已隔离）：InvalidOperationException：页面炸了」）。

## 下一步

* `IPluginStore<T>`：插件自有对象模型（`EnemyProject` 那种）的保存 / 读取 / 迁移；
* 列表控件 `IEditorList<T>`（选中项、Add/Remove/Clone/MoveUp/MoveDown、SelectionChanged）；
* 多文件生成：文件清单 → 预览 → 确认 → 写入（需求书 6.4 的「预览生成文件清单」）；
* 主界面入口：把 `BuildSection` 挂到「插件」页（现在只有自检在调它）。

## 诚实说明

渲染是真的（自检里驱动了真实 WPF），但**主界面还没有入口按钮**把扩展页面挂出来 —— 那是下一步；
现在插件作者可以先用自检验证自己的页面能不能建起来。
