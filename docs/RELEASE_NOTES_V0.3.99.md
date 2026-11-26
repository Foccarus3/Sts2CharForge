# V0.3.99 —— 斩杀 → 卡牌奖励：状态栏显示「战斗结束后获得卡牌奖励」的悬赏状态

> 用户原话：「能不能加一个像"狩猎"在状态显示一个战斗结束后获得卡牌奖励的状态」
> （接着 V0.3.98 的「斩杀奖励改成挂进战斗结算奖励」）

## 做了什么

新增一个生成端内置状态 **`<角色>PendingCardRewardPower`（卡牌悬赏）**：

```csharp
public sealed class SevenPendingCardRewardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;   // 层数 = 还欠几次
    public override bool ShowInCombatDisplay => true;                    // 状态栏里看得见
    public override async Task AfterCombatEnd(CombatRoom room) { …逐次 AddExtraReward…; await PowerCmd.Remove(this); }
}
```

- **斩杀触发时**：卡牌里改成先挂这个状态
  `PowerCmd.Apply<SevenPendingCardRewardPower>(choiceContext, base.Owner.Creature, 1m, base.Owner.Creature, this)`
  —— 层数 = 还欠几次「选一张卡」的奖励，战斗中随时能在状态栏看到；
- **战斗结束时**（`AfterCombatEnd`，房间结算之前）：按层数逐次
  `room.AddExtraReward(player, new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), 1, player))`，
  然后自己移除 —— 和本体「猎杀」（TheHunt）同一句写法，奖励出现在结算界面的奖励列表里；
- **图标**：直接用本体「猎杀」的 `images/powers/the_hunt_power.png`（解包目录里没有时按老规矩退回占位）；
- **文案**：`卡牌悬赏` / 「战斗结束后，获得 {Amount} 次「选一张卡」的奖励（和本体「猎杀」同一套结算奖励）。」

上一版（V0.3.98）「斩杀时现取 CombatRoom 直接挂奖励」的逻辑**保留为兜底**（只在拿不到状态类名时才会走到）。

## 验收

- 探针 `_probe/pendreward` **6/6 PASS**：
  - ① 有用到「斩杀 + 卡牌奖励」→ 会生成悬赏状态类
  - ① 卡牌里挂的是 `PowerCmd.Apply<…PendingCardRewardPower>(…)`，且**不再**当场弹选牌 / 直接加结算奖励
  - ② 状态类里有 `AfterCombatEnd(CombatRoom room)` + `room.AddExtraReward(...)` + `await PowerCmd.Remove(this)`
  - ③ 状态栏文案 title=「卡牌悬赏」、description 带 `{Amount}`
  - ④（红线）没勾斩杀条件 → 不生成这个状态，仍是当场选牌
- `dotnet build` 全解决方案 **0 error**
- `--uicheck` **4944 PASS / 0 FAIL**（新增 3 条；V0.3.98 那两条按新行为改写）

## 版本

- `V0.3.99`
