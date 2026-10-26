# V0.2.287 — 新条件：这张牌是因「预见」被丢弃的

## 用法

条件下拉（效果 / 状态 / 锻造【条件】模块共用那份列表）里多了一条：

```
这张牌是因「预见」被丢弃的
```

挂在「一张牌被丢弃后」这类钩子上，就只会被**预见丢掉的那些牌**触发。

## 生成出来的东西（探针实测）

```csharp
// 条件表达式
protected override bool IsPlayable => (XxxForgeCombatCounters.__inScryDiscard);

// 「预见」丢牌那一小段（只在用到这条条件时才生成）
// 打标记：下面这一小段里的丢弃都算「因预见被丢弃」（条件靠它分辨）
XxxForgeCombatCounters.__inScryDiscard = true;
try     { await CardCmd.Discard(choiceContext, __scryDiscard); }
finally { XxxForgeCombatCounters.__inScryDiscard = false; }
```

要点：
* **只认工具生成的「预见」**：本体没有预见机制（`EmitScry` 是工具自己拼的：看抽牌堆顶 N 张 → 你挑几张 → 丢进弃牌堆），
  所以别人的模组 / 本体的丢弃不会让这条条件成立 —— 和之前「本回合预见次数」那条口径一致；
* 标记用 `try/finally` 包住，丢弃过程中就算抛出也不会把这个静态标记留在 true（否则后面所有丢弃都会被误判）；
* 不用这条条件的老存档：标记那句、字段那句都**不生成**，输出逐字不变。

## 能挂在哪

* 状态：触发时机「一张牌被丢弃后」（`AfterCardDiscarded`）+ 这条条件
* 卡牌：效果里的条件
* 遗物：「一张牌被丢弃后」

## 验收

* 自检：**4453 PASS / 0 FAIL**
* 探针：条件表达式 / 打标记 / 计数器字段三样都生成到位（`__inScryDiscard = true/false` 包在 try/finally 里）
* 只出更新包（`-NoBundle`）
