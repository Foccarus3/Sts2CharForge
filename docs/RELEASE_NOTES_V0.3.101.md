# V0.3.101 — 破解 / 骇入卡面叠层显示 0 的修复

## 问题

「破解」（Hikii）的自定义描述：

```text
获得{Block:diff()}点格挡2次，给予所有敌人等量于获得格挡量({Hakking:diff()})的骇入。
```

实际打出时：

```text
获得 3 点格挡 × 2 次 = 6
给予所有敌人 6 层骇入
```

但卡面上 `{Hakking:diff()}` 显示 **0**。

## 根因

这张牌的第二个效果是：

```text
ApplyPower（Hakking），AmountFormula = ThisCardBlock
```

卡面预览 lambda 只拿到 `CardModel`，旧逻辑把所有 `ThisCard*` 读数一律按 0 代入：

```csharp
new ForgeFormulaPowerVar<Hakking>(static (card, target) => 0m)
```

所以卡面显示 0；实际打出用的是 `this.ForgeThisCardBlock`，是正确的 6。

## 修复

`ThisCardBlock` / `ThisCardDamage` 现在会按**这张牌自己的 Block / Damage 效果**估算：

```csharp
new ForgeFormulaPowerVar<Hakking>(
    static (card, target) => (card.IsUpgraded ? 8m : 6m))
```

- 数值公式按常量算；
- 乘上「生效次数 / 重复次数」；
- 升级增量算进升级后的数字；
- 只影响卡面显示，实际打出仍用真实的 `ThisCardBlock`。

`ThisCardExhaust / Discard / Transform / Generate` 仍然按 0（要打出时才知道），但不再是「整条公式退回 DamageVar(0)」。

## 验证

| 项 | 结果 |
|---|---|
| Hikii 生成源码 | `{Hakking}` 预览 6 / 升级 8 |
| 卡面 Block 仍显示 3 点 × 2 次 | 不变 |
| 开发版 `--uicheck` | **4949 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4948 PASS / 0 FAIL** |
