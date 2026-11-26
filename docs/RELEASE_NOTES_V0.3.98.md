# V0.3.98 —— 「斩杀 → 获得卡牌奖励」改成挂进战斗结算奖励（像本体「猎杀」）

> 用户原话：「斩杀获得卡牌奖励发现是先获得卡牌奖励再造成伤害欸，能不能做的像原版"狩猎"一样，
> 在战斗结束后获得一个可选择的卡牌奖励」

## 原来的问题

卡牌上「获得卡牌奖励」+ 条件「斩杀（Fatal）」时，生成端走的是**当场弹选牌界面**那条路
（`CardSelectCmd.FromChooseACardScreen`）—— 斩杀那一刻就跳奖励，和伤害结算的先后完全乱。

本体「猎杀」（TheHunt）不是这么做的：它把奖励**挂进本场战斗的结算奖励**：

```csharp
room.AddExtraReward(player, new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), N, player));
```

打完在奖励界面里多一条「选一张卡」。这条路我们早就实现了，但**只有**「战斗胜利后」这种带
`CombatRoom room` 参数的钩子能走；卡牌链里没有 room 参数。

## 改了什么

卡牌链里遇到「斩杀 + 获得卡牌奖励」时，运行时现取当前战斗房间再挂奖励：

```csharp
MegaCrit.Sts2.Core.Rooms.CombatRoom? __rewardRoom =
    MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState()?.CurrentRoom as MegaCrit.Sts2.Core.Rooms.CombatRoom;
if (__rewardRoom is not null)
{
    // 挂进本场战斗的结算奖励（和本体「王国资产」「猎杀」同一句）
    __rewardRoom.AddExtraReward(base.Owner, new CardReward(…, N, base.Owner));
}
else
{
    // 兜底：取不到房间时还是当场选牌（老行为）
}
```

核过 `sts2.dll`：`RunManager.DebugOnlyGetState()` / `RunState.CurrentRoom` / `CombatRoom.AddExtraReward` 都存在。

**没勾斩杀条件的卡牌奖励一个字都没变**（红线：想要「当场选」的照样当场选）。

## 验收

- 探针 `_probe/fatalreward` **4/4 PASS**：
  - 斩杀 + 卡牌奖励 → 生成 `AddExtraReward(...)`
  - 取的是当前 `CombatRoom`（`DebugOnlyGetState()?.CurrentRoom as ...CombatRoom`）
  - 当场选牌那句只在「取不到房间」的兜底分支里（顺序断言）
  - 红线：非斩杀的卡牌奖励仍是原来那句当场选牌
- `dotnet build` 全解决方案 **0 error**
- `--uicheck` **4943 PASS / 0 FAIL**（新增 2 条）

## 版本

- `V0.3.98`
