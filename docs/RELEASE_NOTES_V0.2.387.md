# V0.2.387 — 编辑器扩展插件：受控加载器（需求书第 4.1 / 6 节，第一期第一步）

需求书要的不是「再加几个字段」，而是一套**受控的编辑器扩展**能力。这一版把**加载与隔离**这一层落地；
页面渲染（受控控件 API）是下一步。

## 新增契约（Core：`Plugins/EditorExtension.cs`）

```json
"editorExtension": { "entry": "EnemyForge.Editor.EnemyForgeEditorExtension, EnemyForgeEditor",
                     "apiVersion": "1" }
```

* `ICharForgeEditorExtension { string ApiVersion; IReadOnlyList<IEditorPage> CreatePages(IEditorHost host); }`
* `IEditorHost` —— 插件能看到的**全部**外部能力：`PluginId` / `PluginDir` / `OutputDir` / `Log(msg)`。
  刻意不给主窗口、反射、任意文件系统、网络、进程（需求书 6.2 的禁止项）。
* `IEditorPage { Id, Title, object Build(), void Dispose(); }`
* `EditorExtensionSpec`（plugin.json 那段）+ `EditorExtensionStatus`（审核页要显示的：是否声明 / 入口 / API 版本 / 加载状态 / 错误 / 页面数 / 最近日志）。

## 加载器（`EditorExtensionRegistry`）

按需求书 6.1 / 6.3 / 6.4 实现：

| 要求 | 实现 |
|---|---|
| 只有显式声明才加载 | 没写 `editorExtension` → 状态为「无编辑器扩展（声明式插件）」，**不碰任何代码** |
| 旧插件行为不变 | 同上（整条路径都不进入） |
| API 版本不兼容 → 拒绝并提示 | 先比 `apiVersion`，再比插件自报的 `ApiVersion`，提示里带具体版本号 |
| 程序集必须来自插件目录 | 只在该插件目录（含子目录）里找 `<AssemblyName>.dll`，且用独立可卸载的 `AssemblyLoadContext` 加载 |
| 入口必须实现接口 | 反射检查，否则报「入口类型没有实现 ICharForgeEditorExtension」 |
| 出错隔离 | 加载 / 实例化 / `CreatePages` 全部 try/catch：只记状态，主程序不崩、别的插件不受影响 |
| 禁用 / 重载 | `SetDisabled(id, bool)` / `Reset()`（卸载 ALC、调页面的 `Dispose`，Dispose 抛异常也吞掉） |

## 自检

**4740 PASS / 0 FAIL**（+6 条，全部针对加载器的真实行为）：
没声明不加载 / API 版本不兼容被拒 / 找不到程序集如实报错 / 入口没实现接口被拒 /
正常扩展加载且报出 2 个页面 / 禁用后不加载 / **初始化抛异常被隔离**（`InvalidOperationException：故意炸的`）。

## 下一步（第二期第一步）

* 受控控件 API（`IEditorPanel` / `IEditorForm` / 文本框 / 数字框 / 复选 / 下拉 / 按钮 / 列表）；
* 把 `IEditorPage` 渲染进 WPF（`Build()` 返回的控件树 → 真实界面）；
* 选中事件 / 点击事件 / 刷新 / 标记脏数据；
* 插件自有对象模型 + `IPluginStore<T>`（保存 / 迁移）；
* 多文件生成（文件清单 → 确认 → 写入）。

## 说明

这一版**还不能**渲染插件页面 —— 加载成功时状态里会显示「已加载（N 个页面）」，但界面入口还没接。
我没有把它说成「已经能跑 EnemyForge」，那是下一步的事。
