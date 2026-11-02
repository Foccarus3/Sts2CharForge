# V0.2.398 — 跨对象引用：登记 / 解析 / 自报校验问题（需求书 4.6，第三期第一步）

插件自己的对象模型里经常有「这条动作引用某个状态 / 某张卡 / 某只召唤物」这种关系。
这一版给了一个受控的引用层，让插件能校验「引用的东西还在不在」。

## 契约

```csharp
// IEditorHost
void RegisterRefs(string kind, IReadOnlyList<EditorRefItem> items);   // 登记可引用对象
bool TryResolveRef(string kind, string id, out string label);        // 查引用
void ReportProblem(string message);                                  // 把自己发现的问题交上去
```

`EditorRefItem { Id, Label }`。

## 用法（插件侧）

```csharp
host.RegisterRefs("power", project.Powers.Select(p => new EditorRefItem { Id = p.Class, Label = p.Name }));
foreach (var action in project.Enemies.SelectMany(e => e.Moves).SelectMany(m => m.Actions))
    if (!host.TryResolveRef("power", action.PowerId, out _))
        host.ReportProblem($"动作「{action.Name}」引用了不存在的状态 {action.PowerId}");
```

## 审核页新增两行

* `可引用对象：N 个；插件自报校验问题：M 条`（M > 0 时橙字）；
* 自报的问题同时写进「最近一次异常」（红字），所以用户一眼能看到断引用。

## 自检

**4779 PASS / 0 FAIL**（+3 条）：
登记过的引用能查到（`weak → 虚弱`）、没登记查不到 /
`ReportProblem` 进审核信息（`问题数=1`、`LastError=动作 2 引用了不存在的状态 nope`）/ `RefCount=2`。

## 第三期剩余

* 配置迁移的**界面**（迁移钩子在 V0.2.391 已有；还差「发现旧版本 → 提示 → 一键迁移」）；
* 逐帧自动播放（FPS 轮播）。
