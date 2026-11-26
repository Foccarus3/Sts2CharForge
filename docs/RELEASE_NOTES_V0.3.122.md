# V0.3.122 — 「这张牌被变化时」效果体里的 base.CombatState 为 null

## 现象

卡牌「循环」：

```text
当这张牌被变化时：
  对所有敌人造成伤害
  生成 1 张循环，放入弃牌堆
```

实际被变化时，后面的效果没有正常执行（用户看到的「不回能 / 不继续结算」）。

## 原因

本体 `CardCmd.Transform` 的顺序是：

```csharp
original.RemoveFromCurrentPile();
...
original.AfterThisWasTransformedInto(replacement);
```

钩子被调用时，**这张牌已经不在任何牌堆里**，所以：

```csharp
base.CombatState == null
```

V0.3.116 已经给外层守卫加了兜底：

```csharp
var __cs = card?.CombatState ?? base.Owner?.Creature?.CombatState ?? base.CombatState;
if (__cs is null) return;
```

但**效果体里仍然用 `base.CombatState`**：

```csharp
TargetingAllOpponents(base.CombatState)
```

于是 `TargetingAllOpponents(null)` 抛异常，后面的生成卡牌 / 能量效果一起丢掉。

## 修复

V0.3.122：

1. `ForgeOnTransformedFrom` 增加参数：

```csharp
private async Task ForgeOnTransformedFrom(
    PlayerChoiceContext choiceContext,
    ICombatState __cs)
```

2. 调用时传外层的 `__cs`：

```csharp
ForgeOnTransformedFrom(choiceContext, __cs)
```

3. 被变化时机的**效果体**里，所有：

```csharp
base.CombatState
```

统一替换成：

```csharp
__cs
```

这样伤害 / 生成卡牌 / 回能等效果都有有效的战斗状态。

## 范围

只改「这张牌被变化时」这一条卡牌时机；其它时机（被消耗 / 被丢弃 / 被生成…）行为不变。

## 生效方式

更新到 V0.3.122，重新生成 Devoutnun 工程并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 被变化效果体使用 `__cs` | 通过 |
| 不再出现 `TargetingAllOpponents(base.CombatState)` | 通过 |
| 被变化时机「生成 → 回读」仍正常 | 通过 |
| 开发版 `--uicheck` | **4969 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4968 PASS / 0 FAIL** |
