# V0.3.54 — 锻造卡括号里的 `{Damage:diff()}` 只显示 6

## 现象

「遗恨」的卡面：

```text
造成 5+ 本场战斗中生成过的余烬数量×7点伤害。
（造成{Damage:diff()}点伤害）
```

游戏里括号内只显示 `6`，但效果实际能生效。

## 真因

这张卡的节点图里：

```text
【数值】 5 + GeneratedEmber*7
        ↓（接在效果的「数值」口）
【效果】造成伤害（Amount 默认 6）
```

真正生成 `DamageCmd.Attack(...)` 时用的是【数值】节点的公式；
但 `ForgeGraphGen.CardValueEffects`（给卡面 `CanonicalVars` 用）以前只读：

```csharp
EffectSpec.AmountFormula
EffectSpec.Amount   // = 6
```

**完全没有读【数值】节点的公式**，所以 `{Damage:diff()}` 的现算变量被固定成 6。

## 修法

`CardValueEffects` 现在会从效果节点的 **port 1**（「数值」入口）取【数值】模块：

- `5 + GeneratedEmber*7` 直接作为 `ForgeFormulaDamageVar` 的公式；
- 升级前 / 升级后公式不同时（`*7` / `*9`），生成 `IsUpgraded ? 升级后 : 升级前`；
- 只有两边都没有现算公式时，才继续用固定数值 + 升级增量。

`IsUpgraded` 也加成了公式可用的只读量（卡牌专用）。

## 验证

| 项 | 结果 |
|---|---|
| 【数值】节点公式进入 `ForgeFormulaDamageVar` | PASS |
| 不再生成固定的 `new DamageVar(6m)` / `Attack((6m))` | PASS |
| 升级前 / 升级后不同公式用 `IsUpgraded` 切换 | PASS |
| 开发版 `--uicheck` | **4908 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4907 PASS / 0 FAIL** |

⚠ 必须重新生成「遗恨」所在工程；旧工程的卡面变量仍是固定 6。

⚠ 如果 `GeneratedEmber` 是插件 / 自定义变量，生成器那边要能解析到它；否则它会退回固定值并应在校验里报出来。
