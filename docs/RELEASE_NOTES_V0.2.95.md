# V0.2.95 更新说明

> 队列第⑥项做完：**参数监听** —— 「数值公式」里多了一批内置的实时只读量（回合数、丢牌数、消耗数、
> 生成数、花掉的能量、拿到的额外资源量、身上状态数…），不用先在「自定义参数 / 组」页定义。

## 一、公式里能直接写这些实时读数了

「数值公式」那一栏原来只能写数字、`X` / `Y` 和自定义参数（`M` / `N`…）。现在多了一批**内置只读量**，
直接写名字就行（大小写不敏感），每次求值现算：

| 名字 | 是什么 |
|---|---|
| `Turn` / `Round` | 自己这一侧的第几回合 / 当前战斗轮数 |
| `HandCount` / `DrawCount` / `DiscardCount` / `ExhaustCount` | 手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆的张数 |
| `DiscardedThisTurn` / `DiscardedThisCombat` | 本回合 / 本场战斗**丢弃**的牌数 |
| `ExhaustedThisTurn` / `ExhaustedThisCombat` | 本回合 / 本场战斗**消耗**的牌数 |
| `GeneratedThisTurn` / `GeneratedThisCombat` | 本回合 / 本场战斗**生成**的牌数（**复制出来的也算**） |
| `EnergySpentThisTurn` / `EnergySpentThisCombat` | 本回合 / 本场战斗**花掉的能量** |
| `StarsGainedThisTurn` / `StarsGainedThisCombat` | 本回合 / 本场战斗**获得的额外资源量** |
| `PowersOnMe` | 自己身上的状态数量（不含本体内部状态） |

例：`Turn*2`（第 N 回合打 2N 点）、`DiscardedThisTurn*3`、`HandCount + PowersOnMe`。

## 二、全部用本体现成的 API，不需要任何补丁

这一点是特意选的实现路子 —— **不生成记录器 / 不写 Harmony 补丁**：

- 回合数 / 轮数：`PlayerCombatState.TurnNumber` / `ICombatState.RoundNumber`；
- 牌堆张数：`CardPile.GetCards(player, PileType.Hand)` / `PileType.X.GetPile(player).Cards`；
- 丢弃 / 消耗 / 生成：`CombatManager.Instance.History` 里的 `CardDiscardedEntry` / `CardExhaustedEntry` /
  `CardGeneratedEntry`（本体在每个动作发生时都会记一条，见 `CombatHistory`）；
- 花掉的能量 / 拿到的额外资源量：`EnergySpentEntry` / `StarsModifiedEntry`（同一条记录正数是获得、负数是花掉，
  所以「获得的额外资源量」只数正数）；
- 身上的状态数量：`Creature.Powers` 里 `IsVisible` 的那些（本体内部状态不算）。

「本回合」用的是本体那套判据（`HappenedThisTurn`：同一轮 + 同一侧 + 每个玩家的回合数都没变），
所以**敌人的回合里读到的是你上一个回合的数据** —— 文档里写明了。

## 三、「复制张数」为什么和「生成张数」是同一条

本体把**复制出来的牌也记成「生成」**：复制走的是同一个
`CardPileCmd.AddGeneratedCardToCombat` → `History.CardGenerated`，历史里没有单独的「复制」记录。
所以没有 `CopiedThisTurn` 这种名字 —— `Generated*` 里本来就包含复制出来的牌，
界面提示和文档里都写明了（不假装有一个其实不存在的读数）。

## 四、写错了会拦住并列出名单

- 公式里写了既不是参数、也不是内置量的多字母名字（例：`MaxHealth*2`）→ **报错**，
  错误里把 17 个内置名字全列出来；
- 自定义参数 / 组的定义照旧；`X` / `Y` 照旧；
- 锻造节点图【数值】模块里的公式走的是同一套解析（同一份名单、同一条报错）。

## 五、配套（这一版一起做的）

- **解析**：`ForgeFormula` 现在认多字母标识符（只认名单里的内置量），错误文案按新名单写；
- **生成**：新 `FormulaBuiltins`（名字 / 中文说明 / C# 表达式模板）+ `ForgeFormula.CardResolver`
  统一了「X / Y / 内置量 / 自定义参数」的解析（卡牌公式与锻造【数值】两处共用一份）；
  内置量写成 `__fgPlayer` / `__fgSelf` 的表达式，所以生成端「用到才声明那两个变量」那套自动生效；
- **界面**：`AmountFormulaHint` 与输入框 ToolTip 里列出内置量（提示文本直接取自名单，不会和代码漂移）；
- **校验器**：内置量不再被当成「没定义的参数」；没定义的多字母名字报错 + 列名单；
- **自检**：33 条新断言（17 个名字都在、大小写不敏感、生成表达式对得上本体 API、
  自动声明 `__fgPlayer` / `__fgSelf`、提示里列了名字、校验器两条、生成 → 真编译 → 回读往返）；
- **探针**：`_tmp\selfreturnprobe`（`RunFormulaBuiltins`）→ 生成 → 真 `dotnet build` **0 错误** →
  回读公式原样、0 条「没认出来」。
