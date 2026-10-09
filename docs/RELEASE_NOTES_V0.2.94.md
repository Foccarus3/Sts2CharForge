# V0.2.94 更新说明

> 队列第⑤项做完：**遗物 / 状态的新触发时机** —— 打人每一下、每花一点能量、每花一点额外资源量、
> 每生成一张牌；另外多了一条条件「某状态层数刚好达到 N 层」。

## 一、遗物页新增三条触发时机

| 触发时机 | 本体钩子 | 触发次数 |
|---|---|---|
| 造成伤害时（每一下） | `AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)` | 每一下命中都触发（连击 = 多次） |
| 每消耗一个能量 | `AfterEnergySpent(CardModel card, int amount)` | 按**点数**各触发一次 |
| 每消耗一个额外资源量 | `AfterStarsSpent(int amount, Player spender)` | 按**点数**各触发一次 |
| 生成卡牌时 | `AfterCardGeneratedForCombat(CardModel card, Player? creator)` | 每生成一张触发一次 |

- **「造成伤害时（每一下）」**：判据 `dealer == 自己`。
- **两条「每消耗一个」**：本体给的 `amount` 是**这一张牌一共花的**点数（X 费牌 = 当前全部能量），
  所以效果按点数各跑一次（本体 `OrbitPower` 也是按点数累加的）；掷骰留在循环**外面**（一次触发只掷一次）。
  守卫两条：`amount <= 0`（本体这条钩子**0 费也会调**）和「是我方角色花的」
  （`card.Owner.Creature` / `spender`）—— 不判的话联机时别人打牌也会触发你的遗物。
- **「生成卡牌时」**：本体注释写明「只算玩家自己生成的牌」（敌人塞给你的状态牌不算），
  判据 `creator != null && creator == 自己`。

## 二、自定义状态页新增「生成卡牌时」

自定义状态的触发时机里也多了「生成卡牌时」（本体 `AfterCardGeneratedForCombat`），
判据 `creator.Creature == base.Owner`；每生成一张触发一次。

## 三、这四个钩子没有「选牌上下文」，工具自己造

本体的 `AfterEnergySpent` / `AfterStarsSpent` / `AfterCardGeneratedForCombat` 签名里**没有**
`choiceContext`。需要它的效果（造成伤害 / 抽牌 / 施加状态 / 生成卡牌…）生成时会照本体 `Hook.cs`
的做法自己造一个：

```csharp
ulong __netId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL;
var choiceContext = new HookPlayerChoiceContext(this, base.Owner, __netId, GameActionType.Combat);
```

遗物用「(source, owner, netId, type)」那个重载（遗物的 `Owner` 就是 `Player`）。
**战斗里不能用 `BlockingPlayerChoiceContext`**（那是局外的阻塞式上下文，「获得时」用的才是它），
所以「获得时」保持原样、这四条走新的写法。不需要上下文的效果不会多造那个变量。

## 四、新条件「某状态层数刚好达到 N 层」

自定义状态的触发时机「**某个状态层数变化后**」上多了一条条件：

- 判据用的是那条钩子给的两个参数：`power.Amount`（变化之后的层数）和 `amount`（这一次的增量）→
  `power is XPower && power.Amount >= N && power.Amount - amount < N`；
- 例：盯「虚弱」+「刚好达到 5 层」→ 只有从 4 层涨到 5 层（或更多）那一次才触发，已经是 5 层再加不会重复；
- 它**只能**挂在「某个状态层数变化后」上（别的触发时机没有这两个参数、生成出来永远不成立）——
  校验器会**报错拦住**并告诉你改用「拥有某状态至少 N 层」（那条是随时可查的，卡牌 / 遗物 / 状态都能用）；
- 状态触发器的条件候选是**单独一份**（比遗物那份多出这一条），所以不会出现
  「在遗物上选得到、选完才被拦」的情况。

## 五、配套（这一版一起做的）

- **生成端**：`EffectCatalog.RelicTriggers` 四条新时机 + `CSharpCodeGen.RelicTriggerSignature` /
  `HasContext` / `BootstrapInCombat` / `PerPointTrigger`；`PowerTriggers` 加「生成卡牌时」+
  `CustomPowerGen` 新分支；`ConditionExpr` 加 `PowerJustReached`；
- **回读**：`ProjectRecovery.TriggerOfHook` 补 `AfterCardGeneratedForCombat`（其余三条早就映射好了），
  `ConditionFromExpr` 认回新条件的表达式；
- **校验器**：新条件挂在别的触发时机上 / 没选状态 → 报错说清；
- **界面**：状态触发器用自己的条件候选（新增 `EffectConditionPower` 模板）；
- **自检**：24 条新断言（四条时机的签名 / 上下文判定 / 守卫 / 按点数循环 / 生成→回读、
  状态那条链的新时机与自造上下文、新条件的表达式与三个上下文、界面候选表、校验器两条）；
- **探针**：`_tmp\selfreturnprobe`（`RunNewTriggers`）→ 生成 → 真 `dotnet build` **0 错误** →
  回读四条时机全部对上、0 条「没认出来」。
