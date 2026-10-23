# V0.2.206 —— 「从指定组里抽 N 张选 1 张」：不再列整组、也不再崩（CanonicalModelException）

> 用户原话：「打出后，从指定组中抽10张选1张。这一效果依旧展示了所有牌且无法正常生成牌。」
> 日志（`godot (21).log`）里那条就是这个：

```
[ERROR] GameAction PlayCardAction card: CARD.WHITE_COLAK_HEAVEN … completed with exception:
  System.AggregateException … ---> MegaCrit.Sts2.Core.Models.Exceptions.CanonicalModelException:
  Canonical model of type WhiteCloakMod.WhiteCloakExtort used in incorrect place.
```

## 1. 两个真问题

### ① 「多选1 = 10」被「从范围里自己挑一张」静默盖掉

生成端以前是这个顺序：

```csharp
if (e.SpawnChosen) { 候选 = 这一组里的全部卡（Take(60)）… }      // ← 先判它
if (e.SpawnChoice > 1m) { 候选 = 随机抽 M 张 … }                 // ← 只有上面没中才轮到这里
```

于是**同时**勾着「从范围里自己挑一张」、又把「多选1」填成 10 时，走的是第一条：界面把整组牌
（最多 60 张）全列出来，「10」这个数字**完全没生效** —— 这就是「依旧展示了所有牌」。

现在：`勾自己挑 + 多选1 = M（≥2）` → 走「先随机抽 M 张当候选、再自己挑一张」那条；
只有「自己挑 + 多选1 = 1」才是「候选 = 整组（最多 60）」。

### ② 挑中的是**规范模型**，直接塞进「生成卡入堆」→ 运行时抛 CanonicalModelException

「自己挑」那条路候选是 `ModelDb.Card<X>()`（规范模型），挑完却直接：

```csharp
await CardPileCmd.AddGeneratedCardToCombat(__picked, PileType.Hand, base.Owner);   // ← 要求「刚生成的实例」
```

本体 `AddGeneratedCardsToCombat` 内部对卡调 `AssertMutable()`，规范模型直接抛
`CanonicalModelException: Canonical model of type … used in incorrect place`（就是日志里那条；
`AbstractModel.AssertMutable` 抛的就是它）。**「附加处理」（升级 / 免费 / 关键词）以前也是套在规范模型上的**，同样会炸。

现在先建实例、再套附加处理、再入堆：

```csharp
CardModel __pickedMade = base.Owner.Creature.CombatState.CreateCard(__picked, base.Owner);
<升级 / 免费 / 这一回合免费 / 加关键词 … 全作用在 __pickedMade 上>
await CardPileCmd.AddGeneratedCardToCombat(__pickedMade, PileType.Hand, base.Owner);
```

「多选1」那条路本来就是 `CardFactory.GetDistinctForCombat` 建好的实例，不受影响。

## 2. 你要的效果怎么配

「生成卡牌」+ 取卡方式「按范围随机」+ 范围限定「组：XXX」+ **勾「多选1」= 10**：

- 不勾「从范围里自己挑一张」→ 抽 10 张候选，选 1 张生成（老行为，没问题）；
- 勾上它也**照样**按 10 张抽（这一版起），不再列整组。

界面提示语已同步：「勾上**并且**『多选1』填了 M（≥2）= 先随机抽 M 张当候选、再自己挑一张」。

## 3. 验收

- 探针（`sacprobe gen`）：两种配法各一张卡 → 生成工程 `dotnet build` **0 error**；
  生成出来的两条分别是
  `CreateCard(__picked, …) → AddGeneratedCardToCombat(__pickedMade, …)` 和
  `GetDistinctForCombat(base.Owner, __genPool, 10, …)`。
- 老存档逐文件对拍：示例 **113** 个文件 **0 差异**；WhiteCloak 412 个文件里**只有 `WhiteColakHeaven.cs`**
  变了 —— 正是出事的这张卡（现在会先建实例再入堆）。
- `--uicheck`：**3979 PASS / 0 FAIL**（新增 3 条：先建实例再入堆、`自己挑 + 多选1=10` 真的抽 10 张、
  对照组「只勾自己挑」仍是整组）。

## 4. 版本

- `V0.2.206`（用这一版重新生成一次工程即可，存档不用改）
