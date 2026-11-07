# V0.3.33 — 锻造【触发时机】的「只对哪类牌生效」改成下拉，可选某张卡 / 某组卡 / 某种类型

## 改了什么

以前【触发时机】「一张牌结算完毕时 / 打出一张牌时」下面的「只对哪类牌生效」是一个**文本框**，
而且生成端**根本没读这一栏**（只把值存进 `CardFilter`，没有生成守卫）——选什么都等于任意牌。

V0.3.33：

- 这一栏改成**可搜索下拉**；
- 候选来自 `PowerTriggers.CardFiltersFor(profile)`：
  - 任意牌；
  - 类型：攻击 / 技能 / 能力 / 诅咒 / 状态 / 任务；
  - 每个自定义关键词一组；
  - 每个「组」一组；
  - **每张自己的卡**（`Card:<生成类名>`，显示「指定卡：卡名（类名）」）。
- 生成端真的加守卫：

```csharp
if (!(cardPlay.Card.GetType().Name == "SparkleCard50")) return;   // 只对「卡：SparkleCard50」生效
```

类型 / 组 / 关键词仍然走原来的 `Type` / `Role` / `GivenKeywords` 判据。

## 说明：这条时机在「遗物档」

`ForgeTimings.CardResolved`（一张牌结算完毕时）的 Scope 是 **Relic**：
它表示「你打出的某张牌结算完之后」触发这只遗物。
卡牌自己的锻造时机是「打出时 / 被消耗 / 被丢弃 / 被抽到 / 被保留」等。
所以「只对哪类牌生效」的下拉是在**遗物布**上用的；放在卡牌布上会被校验器提示「这个时机是给遗物用的」。

## 界面刷新

`MainWindow.RefreshForgeChoices` 现在会同时刷新 `ForgeGraphCatalog.CardFilterChoices`，
加删卡 / 改组 / 改关键词之后再打开锻造页，指定卡候选会跟着变。

## 验证

| 项 | 结果 |
|---|---|
| `CardFiltersFor` 含「指定某一张卡」 | PASS |
| 锻造下拉能拿到该选项 | PASS |
| 生成端出现 `cardPlay.Card.GetType().Name == "..."` 守卫 | PASS |
| `CardFilter` 字段是 `Pick` + 可搜索 | PASS |
| 开发版 `--uicheck` | **4882 PASS / 0 FAIL**（+3 条 V0.3.33 断言） |
| 部署版 `--uicheck` | **4881 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里这一栏还是文本、生成端也没有筛选守卫。
