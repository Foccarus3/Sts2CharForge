# V0.2.99 更新说明

> 你要求的那一条：**遗物多一个触发时机「某个状态层数到达 N 层时」**。

## 一、遗物页：触发时机多了「某个状态层数到达 N 层时」

选上它，遗物详情里会多出两栏：

- **盯哪个状态**：只盯某一个状态（可以打字搜索，候选就是本体的增益 / 减益 + 你自己做的状态）；
  **留空 = 自己身上任何状态**的层数跨过 N 层都触发。
- **到达 N 层**：填 N。

判据是「**之前不到 N、变化之后到了 N 或更多**」那一下 —— 已经是 5 层再加 1 层**不会**重复触发；
一次上 3 层（3→6）也只算跨过一次。生成出来的守卫长这样：

```csharp
public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource) {
    // CET:PowerWatch=WeakPower CET:PowerReach=5m
    if (power.Owner is null || power.Owner != base.Owner.Creature) return;   // 只看自己身上的状态
    if (power is not WeakPower) return;                                      // 只盯「虚弱」
    if (!(power.Amount >= 5m && power.Amount - amount < 5m)) return;         // 只有「层数跨过 5」那一下才触发
    …
}
```

要点：

- 本体这条钩子（`AfterPowerAmountChanged`）**任何单位**的状态层数一变都会调（敌人的、队友的、每层一次），
  所以生成端一定会先判「是不是自己身上的状态」—— 不判的话联机时别人的状态也会触发你的遗物；
- 它带 `choiceContext`，所以「生成卡牌 / 抽牌 / 召唤伙伴 / 施加状态」这些需要上下文的效果直接用就行；
- 「盯哪个状态」留空时可以再用**效果自己的条件**（例：条件「拥有某状态至少 N 层」）进一步筛。

## 二、界面上会告诉你现在到底在盯什么

触发时机下面那行说明是**实时**的：会写出「现在盯的是自己身上的『虚弱』」或「自己身上**任何**状态」，
并把「跨过 N 层那一下才触发」写清楚 —— 不用去猜。

## 三、校验器（三条，不静默）

- 「到达 N 层」填 ≤ 0 → **报错**（层数要从 0 往上跨才可能触发）；
- 「盯哪个状态」留空 → **提示**说清「任何状态的层数跨过 N 层都会触发」；
- **没选这个触发时机却填了这两个值** → **提示**「这两个值不生效（留着不碍事，换成那个时机就会用上）」。

## 四、配套（这一版一起做的）

- **生成端**：`EffectCatalog.RelicTriggers` 加 `PowerReached` + `CSharpCodeGen.RelicTriggerSignature` /
  `HasContext` + `RelicSource` 里那条分支（三条守卫）+ 回读标记 `CET:PowerWatch=` / `CET:PowerReach=`；
- **存档字段**：`RelicSpec.TriggerPowerId` / `TriggerAmount` + `ShowTriggerPowerFields`（两栏的显隐）+
  `TriggerHint`（那句实时说明）；
- **回读**：新增 `RelicTriggerOfHook` —— 同一个钩子在**状态**那边叫 `PowerChanged`、在**遗物**这边叫
  `PowerReached`，所以遗物先走这一层再落到通用表；两个参数按标记还原；
- **界面**：遗物页触发时机下面两栏（只在选了这个时机时出现）+ 实时说明；
- **自检**：12 条新断言（时机在不在、签名、三条守卫、标记、留空不写守卫、两栏显隐与说明、
  三条校验器、生成 → 回读往返）；
- **探针**：`_tmp\selfreturnprobe`（`RunRelicPowerReached`，含「盯虚弱 5 层」和「任意状态 3 层」两种）→
  生成 → 真 `dotnet build` **0 错误** → 回读 `trigger=PowerReached / watch=WeakPower / reach=5` 全对、
  0 条「没认出来」。
