# V0.2.97 更新说明

> 队列第⑦项**第二批**做完：**条件里的数值也能写公式** —— 「手牌数不少于 N 张」「生命值在 N%~M% 之间」
> 这类条件里的数字，现在可以按打出的那一刻现算。

## 一、条件选项多了一个公式框

只有**卡牌**的效果编辑器里有（和「数值公式」「次数公式」同一条规矩）：

- 留空 = 用左边那个固定数字；写了 = **打出这张牌时现算**；
- 写法与「数值公式」完全一样：参数 `M` / `N`…、`X` 当前能量、`Y` 当前额外资源量、内置只读量
  （`Turn` / `HandCount` / `DiscardedThisTurn` / `PowersOnMe`…）、`min/max/floor/ceil/round/abs`；
- 例：
  - 「手牌数不少于 N 张」写 `Turn` → 第几回合就要求几张；
  - 「生命值在 N%~M% 之间」下限写 `25`、上限写 `HandCount * 10`；
- 「生命区间」那一条的**两个数值**各有一个框（数值 N / 数值 M）。

## 二、生成出来是**内联**表达式（这一版的关键实现点）

条件会被写到 `IsPlayable` / `ShouldGlowGoldInternal` 那两个**表达式体属性**里 —— 那里面没法像
OnPlay 那样先声明一堆局部变量（`__fgPlayer` / `__pM`…）。所以条件公式走**内联**解析：

```csharp
// 「手牌数不少于 Turn 张」
CardPile.GetCards(base.Owner, PileType.Hand).Count() >= (int)System.Math.Max(0m, (base.Owner?.PlayerCombatState?.TurnNumber ?? 0))
```

- 内置只读量展开成 `base.Owner` / `base.Owner.Creature` 的写法；
- `X` / `Y` 展开成 `ForgeFormulaRuntime.Energy/Stars(base.Owner)`；
- 自定义参数（`M`/`N`…）把参数表达式里的三个局部变量名文本内联掉
  （`__fgPlayer` → `base.Owner`、`__fgSelf` → `base.Owner.Creature`、`__fgTarget` → `cardPlay?.Target`）。

## 三、用到了「打出的目标」时只包住那条效果

公式里引用了「指定敌人」来源的参数（`__fgTarget` / `cardPlay.Target`）时，这条条件**不写进**
`IsPlayable` / 描金边（那两处还没有 `cardPlay`，写了会编译不过），只包住那条效果 ——
和本体「指定敌人」那类条件的处理一致（表现：能打出，但效果不生效）。

## 四、写了公式时「填负数 = 反向」不生效

公式算出来的正负**生成时不知道**，所以这一版一律按「正面」判断，固定数值那个负号不参与。
校验器会给一条**提示**说清这件事 —— 要反向请把负号写进公式本身（例 `0 - Turn`），或者清空公式用固定负值。

## 五、回读：整条条件都写进标记

守卫那一行已经变成一整句算完的表达式，从它既反推不回公式、也认不出条件种类 ——
所以生成端写一行 `// CET:CondFull=Kind=HandAtLeast|Amount=3|…|AmountFormula=Turn`，
`--recover` 按它把整条条件（种类 / 固定值 / 状态 / 指向对象 / 子时机 / 两个公式）重建出来。
**没写公式的条件一个字符都不变**（不写这行标记，还是老的「按表达式反推」那条路）。

## 六、配套（这一版一起做的）

- **存档字段**：`ConditionSpec.AmountFormula` / `Amount2Formula` + `UsesAmountFormula` / `UsesAmount2Formula` /
  `ShowAmountFormula` / `ShowAmount2Formula` / `UsesAnyFormula` + 那句说明文字；
- **生成**：`ForgeFormula.ConditionResolver`（内联解析器）+ `CSharpCodeGen.ConditionFormulaExpr`（带
  `withPlayTarget`）+ `ConditionFormulaNeedsPlayTarget` + `ConditionExpr` 里的两个数值槽改成公式感知
  （计数类夹成 ≥ 0 的 int、百分比类保持 decimal）；
- **回读**：`CET:CondFull=` 标记 + `ConditionFromMarker`（卡牌级条件与逐条效果条件都会用它）；
- **界面**：卡牌条件模板里两个公式框 + 说明行（遗物 / 药水 / 状态模板里**不出现**，用户不会填了才被拦）；
- **卡面**：条件文案里印公式本身（「手牌数不少于 Turn 张」）；
- **校验器**：只做在卡牌上、公式能解析、名字对得上、负号+公式给提示；
- **自检**：13 条新断言（内联表达式、标记、默认档逐字不变、目标参数时跳过 gate/glow、三条校验器、生成→回读往返）；
- **探针**：`_tmp\selfreturnprobe`（`RunConditionFormulas`）→ 生成 → 真 `dotnet build` **0 错误** →
  回读条件种类 / 固定值 / 两个公式全对、0 条「没认出来」。
