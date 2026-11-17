# V0.3.82 — 其他模组角色筛选器可点击

## 问题

V0.3.81 主菜单百科里已经能看到其他模组角色的头像按钮，但：

```text
点头像没反应，切不进那个卡池
```

## 原因

自己角色的筛选器创建时接了本体私有方法：

```csharp
UpdateCardPoolFilter
```

但给**其他模组角色**补按钮时只做了：

```csharp
container.AddChild(extra);
pools[extra] = ...;
map[ch] = extra;
```

忘了接 `Toggled` 信号。按钮能显示、能悬停，但点击不会切池。

## 修复

其他模组角色的按钮现在也接：

```csharp
var upd = HarmonyLib.AccessTools.Method(
    typeof(NCardLibrary), "UpdateCardPoolFilter");

if (upd?.CreateDelegate(typeof(Action<NCardPoolFilter>), lib)
    is Action<NCardPoolFilter> act)
    extra.Connect(NCardPoolFilter.SignalName.Toggled, Callable.From(act));

extra.Visible = true;
```

接不上时只记警告，不影响其他角色。

## 验证

| 项 | 结果 |
|---|---|
| 其他模组角色按钮接点击 | 已接 |
| 真实工程编译（SparkleMod） | **通过** |
| 开发版 `--uicheck` | **4924 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4923 PASS / 0 FAIL** |
