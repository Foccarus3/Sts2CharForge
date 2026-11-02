# V0.2.399 — 配置迁移界面：看到版本、一键迁移（第三期第二步）

V0.2.391 给了迁移**钩子**（插件能注册 `SetMigration`），但界面里看不见、也点不到。这一版接上。

## 插件详情「编辑器扩展」页签新增

```
插件数据文件：
project.json    schemaVersion 2          [迁移 / 重新读取]
```

* 每个由插件通过 `GetStore<T>` 拿到的数据文件都会列出来（名字 / 版本 / 有没有落盘）；
* 版本 > 1 时橙字提醒；
* 点「迁移 / 重新读取」→ 主程序调 `Reload()`（触发插件注册的迁移钩子）→ `Save()` 写回，
  然后弹一条「迁移完成：schemaVersion 1 → 2」（失败则显示原因），并刷新这个页签。

## 实现要点

* `Host` 记下自己发出去的每个 store（`Stores`），所以宿主不需要插件额外上报；
* `EditorExtensionRegistry.StoresOf(pluginId)` / `MigrateStore(pluginId, name)` 用反射读 `SchemaVersion` / `Exists`、调 `Reload` / `Save`——
  不依赖具体 `T`，插件换任何类型都成立；
* 迁移失败（钩子抛异常 / 找不到存储）不会写坏文件：`FilePluginStore` 的「坏文件不覆盖」那条仍然在。

## 自检

**4782 PASS / 0 FAIL**（+3 条）：
列表能看到 `project:v1` 且文件存在 / 一键迁移写回后文件仍是合法信封 /
迁移之后列表版本变成 `v2`。

## 第三期剩余

* 逐帧自动播放（FPS 轮播）—— 最后一个小项；
* （可选）跨插件的引用互查（现在 `RegisterRefs` 是每个插件自己的命名空间，不互通）。
