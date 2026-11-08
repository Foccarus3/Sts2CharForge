# V0.3.44 — 联机：敌人触发不再被队友 / 宠物触发

## 现象

自定义状态「反击准备」「破防架势」的触发时机是「敌人造成伤害后」，
联机里**队友造成伤害也会触发**。

## 真因

生成端判断「敌人」用的是：

```csharp
if (dealer is null || dealer == base.Owner) return;   // 只要不是自己就算敌人
```

单机时非自己 = 敌人；联机时**队友也不是自己**，所以队友造成伤害被当成敌人。
「敌人受到伤害后」「敌人获得格挡后」同样：

```csharp
target == base.Owner.Creature   // 队友挨打也算敌人受伤
creature == base.Owner.Creature // 队友获得格挡也算敌人获得格挡
```

## 修法

改用本体 `Creature.IsEnemy`（`Side == CombatSide.Enemy`）：

```csharp
if (!target.IsEnemy || !target.IsAlive) return;   // 受伤的是敌人（队友 / 宠物不算）
if (dealer is null || !dealer.IsEnemy) return;     // 打人的是敌人（队友 / 宠物不算）
if (!creature.IsEnemy) return;                     // 拿格挡的是敌人（队友 / 宠物不算）
```

同步修改：

- 自定义状态（单条 / 多条触发合并两种生成路径）；
- 锻造遗物图；
- 遗物页的对应触发时机。

## 验证

用 `maxiuMOD.json` 真跑 `CustomPowerGen.Source`：

```text
=== MaxiuPower4
if (dealer is null || !dealer.IsEnemy) return;   // 打人的是敌人（队友 / 宠物不算）
=== MaxiuPower9
if (dealer is null || !dealer.IsEnemy) return;   // 打人的是敌人（队友 / 宠物不算）
```

| 项 | 结果 |
|---|---|
| 敌人造成伤害只认 `IsEnemy` | PASS |
| 敌人受到伤害只认 `IsEnemy` | PASS |
| 敌人获得格挡只认 `IsEnemy` | PASS |
| 开发版 `--uicheck` | **4898 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4897 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里还是 `dealer == base.Owner` 的旧守卫。
