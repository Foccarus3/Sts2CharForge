# V0.3.102 — newTab 插件页切存档不重建（召唤物布局改配置工程不变）

## 问题

「召唤物布局」是 `pages[].newTab = true` 的一级功能页，不在「插件」详情里。

切到 `IfMod` 存档后：

- 页面看起来还在；
- 改站位 / 高度 / 隐藏血条；
- 生成工程后 `cs/Plugins/petlayout/PetLayoutTable.cs` 不变。

## 根因

V0.3.93 的 `SyncPluginProfileKey` 只重建了**插件详情页**：

```csharp
if (PluginList?.SelectedItem is PluginListItem item)
    ShowPluginPages(item.Spec);
```

`newTab` 页面走的是 `RebuildPluginTopLevelTabs()`，没有被调用。

于是页面还拿着切存档前 `profiles\default\rows.json` 的那份行列表：

- 改配置改的是旧列表；
- 生成读的是当前存档；
- 两边不是同一份数据 → 工程文件不变。

## 修复

切存档 / 新建 / 另存 / 改名时，除了详情页，也重建：

```csharp
RebuildPluginTopLevelTabs();      // newTab 一级功能页
RebuildPluginExtensionPages();    // 挂到卡牌 / 遗物等页签的扩展页
```

这样「召唤物布局」会按当前存档重新读行数据，再生成 `PetLayoutTable.cs`。

## 旧数据提醒

如果之前旧版本把配置写进了：

```text
plugins\召唤物布局\profiles\default\rows.json
```

而当前存档的 `profiles\IfMod\rows.json` 还是默认值，更新后需要：

1. 打开「召唤物布局」页；
2. 确认当前行是你要的值；
3. 重新点「保存这些值」/「生成工程」。

仍不对时，把 `profiles\default\rows.json` 里那一行手工复制到 `profiles\IfMod\rows.json`，再生成一次。

## 验证

| 项 | 结果 |
|---|---|
| 切存档后 newTab 页重建 | 通过 |
| 列表行保存 / 生成 | 通过 |
| 开发版 `--uicheck` | **4949 PASS / 0 FAIL** |
| 部署版 `--uicheck` | （打包后跑） |
