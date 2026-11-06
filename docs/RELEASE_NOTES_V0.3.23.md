# V0.3.23 — 「意图为攻击」看错敌人（场上两个敌人时）

用 `Carmenmod (4).json` 的「提线木偶 / CarmenMarionette」复现：

* 场上两个敌人，一个意图攻击、一个意图减益；
* 卡牌指向**减益**的那个敌人，却进了「意图攻击」分支（造成 7 点伤害）。

## 一、真因

`EnemyIntent` 条件生成的是：

```csharp
((base.Owner.Creature.CombatState?.HittableEnemies?.FirstOrDefault())
  ?.Monster?.NextMove?.Intents?.Any(i => i.IntentType == ...))
```

它读的是**可打的第一个敌人**，而不是这张牌选中的 `cardPlay.Target`。
两个敌人时，第一个是攻击意图、第二个（实际指向的）是减益意图 → 条件判成攻击。

## 二、修法

`CSharpCodeGen.ConditionExpr` 的 `EnemyIntent` 分支：

* **卡牌 OnPlay**（有选中的目标）→ 看 `cardPlay.Target`：

  ```csharp
  ((cardPlay.Target)?.Monster?.NextMove?.Intents?.Any(i => i.IntentType == IntentType.Attack) ?? false)
  ```

* **IsPlayable / 遗物 / 药水 / 自定义状态**（没有「选中的目标」）→ 仍退回第一个敌人
  （这条口径不变，原有输出不受影响）。

锻造节点图里写在【条件】→【是】分支上的意图条件同样走这条（图上的【指向对象】就是
`cardPlay.Target`，所以也会看对目标）。

## 三、实测

用 `Carmenmod (4).json` 重新生成 `CarmenMarionette`，Attack / Buff / Debuff / … 十个分支
全部从 `HittableEnemies.FirstOrDefault()` 变成 `cardPlay.Target`：

```csharp
if (((cardPlay.Target)?.Monster?.NextMove?.Intents?.Any(
        i => i.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) ?? false))
```

## 四、验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4859 PASS / 0 FAIL**（+2 条 V0.3.23 断言） |
| 部署版 `--uicheck` | **4858 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 含一张「意图 = 攻击 → 造成伤害」的锻造卡，**0 错误** |

⚠ 需要重新生成 Carmenmod 工程才会生效。
