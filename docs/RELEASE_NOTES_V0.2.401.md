# V0.2.401 — 示例编辑器扩展（随程序发布，给插件作者当模板）

需求书第 8 节要「用真实迁移插件验收」。在那之前，得先有一份**能跑的最小样板**——
这一版随程序发了 `plugins\示例编辑器扩展`，把整条链路演一遍。

## 它演示了什么

```
左侧敌人列表（IEditorList<T>）
  → 选中 → 右侧详情（IEditorPanel：类名 / 显示名 / 生命 / 招式）
  → 「保存」→ IPluginStore<DemoProject> 写 data/project.json
  → 「生成 C#」→ IEditorHost.Generate（先弹文件清单确认窗）
```

对应插件作者要写的三步：

1. **plugin.json** 声明入口：

```json
"editorExtension": { "entry": "你的命名空间.你的类, 你的程序集", "apiVersion": "1" }
```

2. **入口类**实现 `ICharForgeEditorExtension`：`CreatePages(host)` 里 `host.GetStore<T>("project")` 拿数据，返回页面；
3. **页面类**实现 `IEditorPage`：`Build()` 里用 `host.CreatePanel()` 加控件，按钮回调里改数据 + `Save()` + `Generate()`。

参考实现：[DemoEditorExtension.cs](src/Sts2CharForge.App/DemoEditorExtension.cs)（约 190 行，含注释）。

## 自检

**4789 PASS / 0 FAIL**（+3 条）：
随程序的示例插件能加载（1 个页面）/ 页面里确实有敌人列表 + 7 个按钮（保存、生成、添加、删除、上移、下移、复制）/
示例扩展拿到了自己的 `data/project.json`。

## 给插件作者的话（也写在这里，免得只看代码）

* 你**不需要**改 CharForge 主程序，也不需要反射它的内部类型；
* 你的数据只在 `<你的插件目录>/data/`，资源只在 `<你的插件目录>/assets/`；
* 生成必须走 `IEditorHost.Generate` —— 用户会先看到文件清单，点「写入」才落盘；
* 你抛异常不会让主程序崩：页面显示「页面创建失败（已隔离）」，审核页记「最近一次异常」；
* 需要更多控件 / 接口（表格、树、颜色选择、导入导出、批量生成…）直接提，按现有契约往上加比改设计便宜。
