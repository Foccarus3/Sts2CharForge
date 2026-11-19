# V0.3.93 — 修复插件页保存到 `default` / 列表行变空

## 已确认的根因

### 1. UI 写 `default`，生成端读 `IfMod`

插件页在**载入存档之前**就建好了，`PluginRegistry.CurrentProfileKey` 还是 `default`；
之后载入 `IfMod.json` 只改了 `_currentProfilePath`，没有同步插件 key，也没有重建页面。

于是：

```text
UI：plugins\<插件>\profiles\default\rows.json
生成端：plugins\<插件>\profiles\IfMod\rows.json
```

两边不是同一份文件。

### 2. 「保存这些值」只写 values，不写 rows

列表行里刚选的 `ref`（例如 `Card = IfCard6`）只改了内存里的 row 字典；
保存按钮只调 `SavePageValues`，没有调 `SaveRows`，所以 `rows.json` 仍是空行。

### 3. 重新加载插件没有清缓存

`PluginRegistry.Load` 重新扫描了清单，但页值 / 行数据缓存还在，
所以「重新加载插件」和重开编辑器都看不到磁盘上的新值。

## 修复

1. 载入 / 新建 / 另存 / 改名存档时，统一同步：

```csharp
PluginRegistry.SetCurrentProfile(_currentProfilePath);
```

并重建当前插件页。

2. 保存按钮先同步 key，再同时保存：

```csharp
SavePageValues(p);   // 内部已经连带 SaveRows(p)
```

3. `SavePageValues` 现在同时写 `values.json` 和 `rows.json`。

4. 读盘回退链：

```text
当前存档 profiles\<key>\
  → 全局 plugins\profiles\<key>\
  → profiles\default\
  → 插件根目录旧 values.json / rows.json
```

5. `PluginRegistry.Load` 清掉页值 / 行数据缓存，重新加载插件即重新读盘。

6. 同一插件里页 `id` 重复会报一条问题（避免两个页面互相覆盖）。

## 保存路径

现在保存后状态栏会写清楚：

```text
plugins\<插件id>\profiles\<当前存档 key>\
```

`IfMod` 存档就是：

```text
plugins\末牌减费\profiles\IfMod\rows.json
```

## 验证

| 项 | 结果 |
|---|---|
| 保存页值时 rows.json 同时落盘 | 通过（新增自检） |
| 清缓存后从磁盘读回 `Card=IfCard6` | 通过（新增自检） |
| 存档切换 key 同步 | 通过 |
| 开发版 `--uicheck` | **4937 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4936 PASS / 0 FAIL** |
