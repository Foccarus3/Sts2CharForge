# V0.3.118 — 敌人身上的「玩家回合开始时」状态不触发

## 现象

自定义状态「威力无效」挂在敌人身上，触发时机是：

```text
玩家回合开始时
```

游戏里回合开始后什么都不发生。

## 原因

生成的守卫是：

```csharp
public override async Task AfterPlayerTurnStart(...)
{
    if (player != base.Owner.Player) return;
    ...
}
```

`base.Owner.Player` 只有**玩家自己的状态**才有值。  
敌人 / 召唤物身上的状态没有 `Player`，这个值是 `null`：

```csharp
player != null  // 永远成立 → 直接 return
```

所以敌人身上的状态永远不会触发玩家回合开始。

## 修复

V0.3.118 的守卫改成：

```csharp
if (base.Owner.Player is not null)
{
    if (player != base.Owner.Player) return;   // 玩家自己的状态：只认自己的回合
}
else if (!MegaCrit.Sts2.Core.Context.LocalContext.IsMe(player))
{
    return;   // 敌 / 召唤物身上的状态：按本地玩家回合触发
}
```

覆盖：

- 玩家回合开始时；
- 回合开始后（抽牌前）；
- 回合开始后（最晚）。

## 语义

| 状态挂在 | 触发条件 |
|---|---|
| 玩家身上 | 那个玩家自己的回合 |
| 敌人 / 召唤物身上 | 本地玩家的回合 |

单人游戏就是玩家回合；联机时不会因为别的玩家回合而触发敌人身上的状态。

## 生效方式

更新到 V0.3.118，重新生成 Carmenmod 并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 敌人状态生成 `LocalContext.IsMe` 分支 | 通过 |
| 玩家状态仍只认自己的回合 | 通过 |
| 开发版 `--uicheck` | **4964 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4963 PASS / 0 FAIL** |
