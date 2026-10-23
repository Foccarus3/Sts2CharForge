# V0.2.218 —— 锻造图也能做「满足某条件时这张牌打不出去」了

> 来源：用户要求「锻造无法『处于某种条件下时，该牌无法被打出』」。

## 1. 以前为什么做不到

卡牌页有「条件」那一套（`ConditionSpec.UnplayableWhenUnmet` → 生成本体的 `IsPlayable`），
但**锻造图**那边只能用【条件】/【if】表达「条件不满足就跳过这一支」——
那是**执行时**的判断，做不出「这张牌根本不能打出」（本体 `IsPlayable` + 描金边）。

## 2. 现在：给【条件】模块加一栏「不满足时」

| 档位 | 生成的东西 |
| --- | --- |
| **跳过这一支（默认）** | 和以前一样：条件不满足 → 这一支什么都不做（老图生成文本**一个字都不变**） |
| **整张牌打不出去** | `IsPlayable` + 描金边（本体「冲突 / 死亡之门」那套） |

选第二档、把【条件】摆在布上（**不用**接进执行链，它本来就是「卡牌级的门」），生成出来就是：

```csharp
// 锻造图（升级前）里勾了「不满足时整张牌打不出去」的条件：base.Owner is not null && CardPile.GetCards(base.Owner, PileType.Hand).Count() >= 3
protected override bool IsPlayable => (base.Owner is not null && CardPile.GetCards(base.Owner, PileType.Hand).Count() >= 3);
protected override bool ShouldGlowGoldInternal => (base.Owner is not null && CardPile.GetCards(base.Owner, PileType.Hand).Count() >= 3);
```

细节：

- 「升级后」那块布上的门**只在升级后生效**（`!base.IsUpgraded || 条件`）；
- 和卡牌页的条件、以及「缺伙伴不能打」那些门**取交集**（`&&`），一个 `IsPlayable` 里全都写进去；
- 遗物 / 药水 / 状态的布上没有「打出」这回事 → 这一栏在那些布上**不显示**，万一老配置里有就**报错**说清；
- `IsPlayable` / 描金边是**表达式体属性**，里面不能声明局部变量 → 条件的「数值 N / M」必须是生成期常量
  （纯数字 / `A+2` 这种能折叠的）。写了运行时公式（`X` / 血量 / 状态层数）的那条会**跳过并给一条警告**，
  不会静默生效一半；
- 顺带修个误报：只当「打不出去」用的【条件】没接进执行链，以前会被「漏接线」警告点名，现在不算了
  （见 V0.2.216 那条）。

## 3. 验收

- 探针（【条件】手牌 ≥ 3 + 「整张牌打不出去」）→ 生成上面那三行，生成工程 `dotnet build` **0 error**；
- 默认档（跳过这一支）→ **不生成** `IsPlayable`（老图逐字不变）；
- 非卡牌布勾这一档 → 校验器报错；数值写运行时公式 → 警告；
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（只差 `.sln` 的 GUID）；
  `lihuazou.json` 0 差异；`AinodiMod.json` 仍只有 V0.2.217 那条 `AinodiCard63` 的实际修正；
- `--uicheck`：**4059 PASS / 0 FAIL**（新增 6 条）。

## 4. 版本

- `V0.2.218`（用这一版重新生成一次工程即可；要用的那张卡到锻造图的【条件】上把「不满足时」改成「整张牌打不出去」）
