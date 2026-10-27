# V0.2.302 — 新触发时机「突破格挡时」

## 参考本体「手钻」HandDrill

```csharp
public override async Task AfterBlockBroken(PlayerChoiceContext choiceContext, Creature target, Creature? breaker)
{
    if ((breaker == base.Owner.Creature || breaker?.PetOwner == base.Owner) && !target.IsPlayer) { … }
}
```

本体有**专门的钩子** `AfterBlockBroken(ctx, target, breaker)`，不用自己拿伤害结果推「这次是不是打穿了格挡」。

## 三处都能选了

| 位置 | 状态 |
|---|---|
| 状态（自定义状态）触发时机 | ✅ 新增「突破格挡时」 |
| 遗物触发时机 | ✅ 同上（显示名逐字一致 —— 自检要求两侧菜单必须一样） |
| 锻造页触发时机 | ✅ 同上 |

生成出来的代码（探针实测原文，状态链）：

```csharp
public override async Task AfterBlockBroken(PlayerChoiceContext choiceContext, Creature target, Creature? breaker) {
    if (target.IsPlayer) return;                                   // 只认敌人的格挡被打穿
    if (!(breaker == base.Owner || breaker?.PetOwner?.Creature == base.Owner)) return;   // 只认自己或自己的召唤物打的（照抄手钻）
    …效果…
}
```

遗物链是同一套守卫（`breaker == base.Owner.Creature || breaker?.PetOwner?.Creature == base.Owner.Creature`）。

**召唤物替主人打穿格挡也算** —— 和手钻完全一致。

## 关于「条件」那一份（我做了个决定，说明一下）

「条件」是没有钩子上下文的：它可以挂在任何时机上，而 `target` / `breaker` 这两个变量**只在 `AfterBlockBroken` 里存在**。
所以把「突破格挡时」做成一个**条件**只有两种结局：

* 要么它在别的时机上永远不成立（等于骗人）；
* 要么生成出 `target` / `breaker` 未定义的代码（编译不过）。

而作为一种「触发时机」它已经完全表达了你要的语义（**那一下就是突破格挡的时候**），挂在它下面的效果天然只会在突破格挡时跑。
所以我**没有**加那条条件，而是把「突破格挡时」做成了正式触发时机 —— 这是它唯一能正确成立的形式。如果你确实需要一个「条件」形态（比如挂在别的钩子上判断「这一下有没有打穿格挡」），那需要另一套东西（把 DamageResult 传下来），我可以按那个思路再做一版。

## 验收 / 发布

* 自检：**4456 PASS / 0 FAIL**（4684 行；比上一版 +2 条，就是新时机的守卫/上下文断言）
* 自检里三条硬断言都过了：归属守卫、`choiceContext` 判定（本体钩子自带 → 直接用参数）、遗物/状态两侧菜单一致
* 只出更新包（`-NoBundle`）
