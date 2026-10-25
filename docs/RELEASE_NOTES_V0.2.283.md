# V0.2.283 — 零费打出 X 牌，还是至少打一下

## 你报的

> 在零费时打出 x 牌，怎么却还是至少会生效一次

## 定位（拿你的 `SerenMod.json` 复现）

「思念倾泻」`SerenCard6` 是**额外资源量 X 费**的牌（`StarCostIsX = True`），生成出来是：

```csharp
int x = ResolveStarXValue();          // 0 点打出 → 0
await DamageCmd.Attack((base.IsUpgraded ? 9m : 6m))
    .WithHitCount(x)                  // ← x = 0
```

**本体对 `WithHitCount(0)` 仍然会结算一次**（它按「至少一次」处理），所以 0 点打出还是掉了一下血。

上一版（V0.2.274）我只把「**公式**算出来的次数」下限从 1 改成 0 ——「**次数 = X**」这条独立的路没有守卫，就是漏的那条。

## 修法

`EmitAttack` 外面包一层（`useX && RepeatIsX` 时）：

```csharp
if (x > 0) {                          // X = 0（零费打出）：这条攻击一次都不执行
    await DamageCmd.Attack(...).WithHitCount(x)...;
}
```

用你的存档重新生成，「思念倾泻」现在长这样（探针原文）：

```
int x = ResolveStarXValue();
if (x > 0)   // X = 0（零费打出）：这条攻击一次都不执行 {
await DamageCmd.Attack((base.IsUpgraded ? 9m : 6m))
.WithHitCount(x)
```

不涉及 X 的存档：`useX && RepeatIsX` 为假 → 直接走原来那条路，生成文本**逐字不变**。

## 验收

* 自检：**4453 PASS / 0 FAIL**。
* 用你的 `SerenMod.json` 生成「思念倾泻」：上面那段 `if (x > 0)` 已就位。
