# V0.2.224 —— 额外资源量遗物上的效果真的只在「战斗开始时」跑一次了

> 来源：用户实测报的「额外资源量遗物触发时机不是固定战斗开始时吗，往上面添加效果实际上是玩家回合开始时」。

## 1. 真因：生成在 `BeforeSideTurnStart` 里，却漏了「只认第一回合」的守卫

「额外资源量」页上加的效果，生成时挂在载体遗物（`<角色>ExtraResourceRelic`）的
`BeforeSideTurnStart` 上 —— 这个钩子**每一回合、每一边**开始时都会调一次，
而遗物链「战斗开始时」那一档（`RelicTriggerGuardLines` 的 `CombatStart` 分支）本来靠两句守卫把它
收成「每场战斗一次」：

```csharp
if (!participants.Contains(base.Owner.Creature)) return;                                  // 只认玩家侧
if (base.Owner.PlayerCombatState.TurnNumber > 1) return;                                  // 只认第 1 回合
```

额外资源量这一块**只有第一句**，第二句漏了：

```csharp
// 以前（V0.2.223 及更早）：
public override async Task BeforeSideTurnStart(…) {
    if (!participants.Contains(base.Owner.Creature)) return;
    …用户加的效果（每回合都跑一次）…
}
```

于是「战斗开始时 → 抽 1 张 / 获得 4 点格挡」变成了**每回合开始时**都触发 ——
和页面自己写的「触发时机固定『战斗开始时』」不符（用户就是照那句话配的）。

## 2. 修法

补上第二句（和遗物链**同一口径**，只是多了个 `?.` 防空）：

```csharp
public override async Task BeforeSideTurnStart(…) {
    if (!participants.Contains(base.Owner.Creature)) return;
    if (base.Owner.PlayerCombatState?.TurnNumber > 1) return;   // 战斗开始时（每场战斗只跑一次）
    …用户加的效果…
}
```

- 「开场发放初始数量 / 跨战斗继承」那段（`AfterSideTurnStart`）本来就有这两句守卫，没动；
- 「每场战斗只触发一次」那条效果条件（`OncePerCombat`）的标记照旧在 `AfterCombatEnd` 里清；
- 从工程恢复（`ParseExtraResource`）读的是整个方法体，多这一行守卫不影响回读（它不是效果）。

## 3. 验收

- 自检新增 2 条：
  - 加了效果时，`BeforeSideTurnStart` 里「第 1 回合守卫」在效果之前（守卫 → 效果）；
  - 没加效果时**根本不生成**那个方法（老输出逐字不变，红线）；
- 探针 `sacprobe extrares`（额外资源量启用 + 两条效果）生成出来的工程
  `dotnet build --no-incremental` **0 error**，生成的 `cs/ExtraResource.cs` 里能看到那两句守卫；
- 老存档逐文件对拍（新 Core ↔ 已发布 V0.2.223 的 Core，同一份输入各生成一遍）：
  示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的 GUID**（这两份存档没用到额外资源量效果）；
- `--uicheck`：**4135 PASS / 0 FAIL**。

## 4. 版本

- `V0.2.224`（用这一版重新生成一次工程即可；「额外资源量」页上给那只载体遗物加的效果
  现在真的只在每场战斗开始时跑一次）
