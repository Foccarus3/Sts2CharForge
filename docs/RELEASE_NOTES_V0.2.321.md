# V0.2.321 — 自己身上的减益比敌人多挂一轮（连吃两个回合易伤）

用户原话：

> 给自己和敌人一个易伤，一整个回合结束之后对面的易伤没了，自己的还有，连吃两个回合易伤

## 真因：本体的一条「玩家侧减益」规矩

`PowerCmd.Apply` 里写着：

```csharp
if (target.Side == CombatSide.Player && power.Type == PowerType.Debuff)
    power.SkipNextDurationTick = true;   // 玩家身上的减益，下一次结算先跳过
```

配合 `VulnerablePower.AfterSideTurnEnd`（**只在敌方回合结束时** tick），效果就是：

| 谁 | 何时结算 | 结果 |
|---|---|---|
| 敌人身上的易伤 | 本次敌方回合结束 | 1 层 → 0（只影响一个我方回合） |
| 玩家身上的易伤 | 跳过本次，下一次敌方回合结束才掉 | 影响**两个**敌方回合 |

这条规矩是为「**敌人打你**」设计的（不该在你身上刚挂上就掉），但你自己给自己上减益时，
看起来就是「自己吃的轮数比敌人多一倍」—— 你报的就是这个。

## 改法

**我们主动往玩家侧施加状态**（作用对象 = 自己 / 玩家 / 全体单位 / 队友）之后，紧跟一句把标记清掉：

```csharp
await PowerCmd.Apply<VulnerablePower>(choiceContext, base.Owner.Creature, (1m), base.Owner.Creature, this);
if (base.Owner.Creature.GetPower<VulnerablePower>() is { Type: PowerType.Debuff } __selfDebuff)
    __selfDebuff.SkipNextDurationTick = false;
```

* 只在**减益**上生效（`is { Type: PowerType.Debuff }` 运行期判断，增益不受影响）；
* 只清**我们发的那一句**；敌人施加给你的减益照旧走本体逻辑；
* 「施加状态填负数」（扣层数）不加这一句；
* 卡牌 / 遗物 / 药水三条链的「自己」与「全体单位」都覆盖了。

## 验收

* 你存档里的 **针锋相对 `ZouCard51`**（对敌易伤 + 对自己易伤）现在生成：
  `Apply<VulnerablePower>(…, base.Owner.Creature, 1m, …)` + 清标记那一句。
* 探针 `sacprobe selfdebuff`（自己易伤 + 全体单位易伤两张卡）：两句都清标记，整份工程 **`dotnet build` 0 错误**。
* 自检：**4540 PASS / 0 FAIL**（+3 条 V0.2.321 断言：清标记 / 只有一处 / 全体单位也清）。
* 逐字节：Sample 与 WhiteCloak 的老存档**只有「给玩家侧施加状态」的卡变了**（Sample 2 张、WhiteCloak 31 张），
  逐个比对确认**只多了上面那 4 行**，没有别处改动 —— 这是本条修复的预期影响面。
* 只出更新包（`-NoBundle`）。

## 一句话

现在「给自己 1 层易伤」= 只吃**一个**回合的易伤，和敌人身上的同一时刻掉光。
