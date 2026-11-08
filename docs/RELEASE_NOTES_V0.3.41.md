# V0.3.41 — 锻造图「消耗卡牌」数值 0 卡死

## 现象

卡门 mod 的「虚伪王座」：锻造图里的【效果】= **消耗卡牌**，数值填 **0**，
打出后整场战斗卡住，没有报错。

## 真因

`ForgeGraphGen.EmitCardPick` 对 `ExhaustCard` / `DiscardCard` / `UpgradeCard` 先生成选牌界面：

```csharp
new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, (int)(0))
```

本体的选择框会一直等一个「选 0 张」的确认，永远等不到 → 卡死。

卡牌页那条链早就有「0 = 这张牌自己」的语义（`AmountZeroIsSelf`），但锻造图的
`EmitCardPick` 没走这个判断，直接进了选牌分支。

## 修法

`EmitCardPick` 最前面加 0 张守卫：

- **消耗 / 丢弃** 数值 0 = **这张牌自己**，直接：

```csharp
await CardCmd.Exhaust(choiceContext, this);
```

不弹选牌框；
- **升级** 没有「0 = 自己」的语义 → 整条跳过，并写注释说明（同样不弹 0 张框）；
- `CountMode = 全部` 不按 0 处理（全部仍然是全部）。

生成结果里现在会看到：

```csharp
// fg:...【ExhaustCard】数值 0 = 这张牌自己：直接消耗自己（不弹 0 张选择框）
await CardCmd.Exhaust(choiceContext, this);
```

## 说明：你的节点是「消耗卡牌」

`CarmenFalseThrone` 里那条是 **`ExhaustCard`（消耗卡牌）**，不是 `TransformCard`。
所以「数值 0」现在表示**消耗这张牌自己**；如果你本来想要「变化这张牌自己」，
要把效果种类改成「变化卡牌」，它的 0 也是「变化自己」（V0.2.320 起支持）。

## 验证

| 项 | 结果 |
|---|---|
| 锻造图消耗卡牌 0 → 直接 `CardCmd.Exhaust(choiceContext, this)` | PASS |
| 不再生成 `ExhaustSelectionPrompt` 的 0 张选择框 | PASS |
| 开发版 `--uicheck` | **4892 PASS / 0 FAIL**（+1 条 V0.3.41 断言） |
| 部署版 `--uicheck` | **4891 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里还是 `CardSelectorPrefs(..., 0)`。
