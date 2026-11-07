# V0.3.35 — 锻造卡自定义描述「填入参数…」改成真正的动态变量

## 现象

锻造卡图上【效果】= 造成 20 点伤害，卡牌页的「自定义描述」点「填入参数…」，
菜单里只有「能量图标」，没有 20 / 伤害数值。

## 真因

「填入参数…」的候选来自 `CSharpCodeGen.ParamHints(card.Effects, isCard)`。
锻造卡的效果在**节点图**里，`CardSpec.Effects` 是空的；而且锻造卡以前**不声明 DynamicVars**，
所以没有 `{Damage:diff()}` 这种动态占位符，菜单只剩界面额外加的「能量图标」。

## 修法

1. `ForgeGraphGen.CardValueEffects(card)` 从升级前 / 升级后的节点图里合成一份
   **只给描述变量用**的 `EffectSpec` 列表：
   - 固定值 → `AmountFormula = "20"`（不是普通 `DamageVar`）；
   - 升级前后差值 → `UpgradeAmount = 10`。
2. `CSharpCodeGen` 把这份列表并进 `CanonicalVars` / `OnUpgrade`，所以生成的是：

```csharp
new ForgeFormulaDamageVar(static (card, target) => (20m), 10m)
```

`ForgeFormulaDamageVar.UpdateCardPreview` 先算 `20` / 升级后的 `30`，再调本体的
`base.UpdateCardPreview` —— 力量 / 敏捷 / **易伤 / 虚弱** 这些加成照旧叠上去，
所以卡面数字会随易伤 / 虚弱变化。

3. 卡牌页「填入参数…」对锻造卡改用这份合成列表，菜单里就是真正的：

```text
{Damage:diff()}
```

不是固定 20。

## 验证

| 项 | 结果 |
|---|---|
| 锻造图 20 点伤害 → 合成 `Damage:20+10`（升级后 30） | PASS |
| 生成卡声明 `ForgeFormulaDamageVar(..., 10m)` | PASS |
| 「填入参数…」给出 `{Damage:diff()}` | PASS |
| 开发版 `--uicheck` | **4885 PASS / 0 FAIL**（+3 条 V0.3.35 断言） |
| 部署版 `--uicheck` | **4884 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里的自定义描述还是老的固定文本。
