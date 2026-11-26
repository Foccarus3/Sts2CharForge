# V0.3.119 — 自定义状态「造成伤害 + 全体友方」打到全体敌人

## 现象

状态「大怪兽」里有一条：

```text
造成 15 点伤害，作用对象 = 全体友方
```

实际打出来是**全体敌人**各挨 15 点。

## 原因

状态链的伤害分派 `EmitDamage` 只有：

```text
Self
AllEnemies
AllUnits
RandomEnemies
default → EmitFoeBlock（第一个 / 全体敌人）
```

没有 `AllAllies` 这一档。  
所以「全体友方」掉进 `default`，被当成敌方目标处理。

## 修复

V0.3.119 在状态链的伤害分派里补上：

```csharp
case "AllAllies":
    foreach (Creature other in base.Owner.CombatState.Allies.Where(...).ToList())
        await CreatureCmd.Damage(choiceContext, other, ...);
```

过滤条件和状态链其它「全体友方」一致：

- 存活；
- 默认不含自己；
- 含召唤物；
- 尊重「不包括召唤物」勾选。

## 范围

只改**自定义状态触发器**里的「造成伤害」：

```text
效果种类 = 造成伤害
作用对象 = 全体友方
```

卡牌 / 遗物 / 药水那几条链的「全体友方」之前已经分别修过，不受影响。

## 生效方式

更新到 V0.3.119，重新生成 Carmenmod 并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 状态 Damage + AllAllies 生成 `CombatState.Allies` | 通过 |
| 不再出现 `HittableEnemies.FirstOrDefault()` | 通过 |
| 开发版 `--uicheck` | **4965 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4964 PASS / 0 FAIL** |
