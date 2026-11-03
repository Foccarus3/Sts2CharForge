# V0.3.6 — 药水的「作用目标」新增「友方」和「全体友方」

## 改了什么

药水属性里的**作用目标**下拉，从 3 档变成 5 档：

| 选项 | 本体的 `TargetType` | 实际打谁 |
|---|---|---|
| `Self`（自己） | `Self` | 自己 |
| `AnyEnemy`（指定敌人） | `AnyEnemy` | 打出时选中的那个敌人 |
| `AllEnemies`（全体敌人） | `AllEnemies` | 所有敌人 |
| **`AnyAlly`（友方）** | `AnyAlly` | 多人里选中谁就作用到谁；**单人没有可选友方时落到自己** |
| **`AllAllies`（全体友方）** | `AllAllies` | 玩家这一侧的**每个**存活单位（自己 + 召唤物） |

生成端每条药水效果都会按这 5 档展开：

* `AllAllies` → `foreach (Creature ally in base.Owner.Creature.CombatState.Allies.Where(c => c.IsAlive).ToList())`
* `AnyAlly` → `if (target is not null && target != base.Owner.Creature) { 打 target } else { 打自己 }`（单人兜底，不会「什么都没发生」）

伤害类效果也一样（`CreatureCmd.Damage` 逐个友方来）。

## 自检

**4801 PASS / 0 FAIL**（+2 条）：

* 下拉里确实有 5 档，生成的药水类分别声明 `TargetType.AllAllies` / `TargetType.AnyAlly`；
* `AllAllies` 生成逐个友方的循环、`AnyAlly` 生成「选中谁就作用到谁、没有就落到自己」的守卫。

## ⚠ 用法提醒（如实说）

* **「友方」在多人才有意义**（本体 `AnyAlly` 就是给多人准备的：单人时没有别的玩家/召唤物可选）。
  单人想给自己上 buff 请用「自己」；想连召唤物一起就用「全体友方」。
* 「全体友方」**包含自己**（本体 `CombatState.Allies` 就是玩家这一侧）。
* 药水的「效果里的『作用对象』仍然不生效」，一直是**药水属性里的作用目标**说了算（这点没变）。
