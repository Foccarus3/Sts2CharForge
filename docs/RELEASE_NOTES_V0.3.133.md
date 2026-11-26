# V0.3.133 — 补齐 ComboBox / 标签页白底，参数参考窗真正跟随

## 这次针对截图里的白框

V0.3.132 的主题资源已经覆盖普通 TextBox / 按钮，但 WPF 默认 `ComboBox` 模板里还有自己的白底 ToggleButton，`TabItem` 也有独立的模板，所以截图里：

- 下拉框仍是白底；
- 顶部标签页仍是白块；
- 可搜索下拉框的候选面板仍是白底。

V0.3.133 直接替换了这三类模板：

1. `ComboBox`：主题化 ToggleButton 背景 / 边框 / 箭头 / 下拉 Popup / 候选行；
2. `TabItem`：主题化标签页头，选中 / 悬停也按主题色；
3. `SearchComboBox`：候选用的 `StackPanel` 改成 `UiFieldBg` 动态资源，高亮行用 `UiBorder`。

这些模板都引用 `UiFieldBg / UiBorder / UiText`，深色和浅色切换会真正生效。

## 参数参考窗

- `ParamReferenceWindow` 在 `Loaded` 和 `Activated` 时都会重新套用当前主窗主题；
- 主窗切浅色时会还原参数参考窗里深色期间写入的局部值，再交回动态资源；
- 不再出现“主窗浅色、参数参考一直深色”。

参数参考条目继续保持只读 `TextBox`，可以鼠标拖选、Ctrl+C。

## 说明

这次按你的要求**没有跑整套 `--uicheck`**，只做了编译和打包。  
如果替换后仍有某个具体控件白底，请直接截那个控件所在页，我按控件类型补模板。

## 版本

```text
V0.3.133
```
