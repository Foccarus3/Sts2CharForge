# V0.3.74 — 遗物「随机敌人 + 造成伤害」修复

## 问题

遗物设定：

```text
打出攻击牌时，对随机敌人造成 3 伤害
```

实际：

- 对所有敌人各造成 3 伤害；
- 伤害被力量 / 虚弱 / 易伤等修正。

## 原因

遗物旧版 `Effects` 链的 `Damage` 分支只处理：

```text
Self
AllUnits
AllAllies
AnyAlly / Ally
其余 → HittableEnemies（全体）
```

`RandomEnemies` 掉进了最后的「其余」，所以变成全体；而且用的是：

```csharp
ValueProp.Move
```

`Move` 是卡牌攻击属性，会被力量 / 虚弱 / 易伤放大；遗物伤害是固定数值，应该用：

```csharp
ValueProp.Unpowered
```

## 修复

遗物 `Damage` 新增 `RandomEnemies` 分支：

```csharp
List<Creature> foes = base.Owner.Creature.CombatState.HittableEnemies.ToList();
for (int __relDmgIdx = 0; __relDmgIdx < 1 && foes.Count > 0; __relDmgIdx++)
{
    Creature foe = base.Owner.RunState.Rng.CombatTargets.NextItem(foes);
    await CreatureCmd.Damage(choiceContext, foe, 3m, ValueProp.Unpowered, base.Owner.Creature);
}
```

同时把遗物 `Damage` 的其它分支统一成 `ValueProp.Unpowered`：

- 随机敌人；
- 全体敌人；
- 全体单位；
- 全体友方；
- 指定友方。

行为：

- 只打随机一个敌人；
- 格挡仍然能挡；
- 不被力量 / 虚弱 / 易伤改数值；
- 遗物伤害保持固定值。

## 验证

| 项 | 结果 |
|---|---|
| 遗物随机敌人伤害 | 只打一只，不再全体 |
| 伤害属性 | `ValueProp.Unpowered` |
| 开发版 `--uicheck` | **4916 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4915 PASS / 0 FAIL** |
