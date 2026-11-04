# V0.3.12 — 诅咒 / 遗物 / 状态链的「全体友方」伤害会打到敌人（修）+ 变量页第一步（模型）

## 一、修了你发我的那份 `CarmenBulletOfDespair.cs`

你文件里这一行：

```csharp
await CreatureCmd.Damage(choiceContext, base.Owner.Creature.CombatState.HittableEnemies, base.DynamicVars.Damage.BaseValue, ValueProp.Move, base.Owner.Creature);
```

`HittableEnemies` = **全体敌人** —— 但效果里配的是「全体友方」。

**根因**：诅咒 / 遗物 / 自定义状态共用的那条「钩子效果」链里，【造成伤害】只有三档：

```
Self（自己） / AllUnits（全体单位） / 其余一律 → HittableEnemies（全体敌人）
```

「全体友方 / 指定友方」掉进了「其余」。

**改法**：补两档

```csharp
else if (e.TargetSide == "AllAllies")
{
    foreach (Creature __allyTarget in base.Owner.Creature.CombatState.Allies.Where(c => c.IsAlive).ToList())
        await CreatureCmd.Damage(choiceContext, __allyTarget, amt, ValueProp.Move, base.Owner.Creature);
}
else if (e.TargetSide is "AnyAlly" or "Ally")
    await CreatureCmd.Damage(choiceContext, base.Owner.Creature, amt, ValueProp.Move, null, null);   // 这条链没有选中目标 → 落到自己
```

顺带说明：你那份文件里 `base(-1, CardType.Curse, CardRarity.Curse, TargetType.None)` —— `TargetType.None` 说明它是**较旧版本**生成的；新版对同一张牌会生成 `TargetType.AllAllies` 并逐个 `Targeting(ally)`。**两条路这次都对了**。

## 二、变量页：第一步（数据模型）

按你定稿的规格先落地「模型 + 命名规范」，为下一步生成/界面打底：

* 新文件 `Profile\ForgeVariables.cs`：
  * `VarSpec`：`Name` / `Note` / `Formula`（值=公式）/ `Global`（本局跨战斗）/ `AllowNegative` / `Max`；
  * `VarNames`：规范化（空白/符号收成 `_`、中文直接拒成空串、最长 **12** 位）、`Check()`（保留名 `X` `Y` + C# 关键字）、`NextFree()`（a…z、a2…）。
* `CharacterProfile.Variables`（和只读的 `CustomParams` 分开存）。

## 三、自检

**4808 PASS / 0 FAIL**（+4 条，变量模型）：

```
中文=「」 ATK=「ATK_2」 超长=12
X=「X」是内置保留名 / ATK_2=ok / 1abc=只能字母数字下划线 / 13位=太长
cnt = 0 [本局] | atk = cnt + 1
下一个=a
```

## 四、下一步（V0.3.13）

1. **生成端**：变量容器类（本场战斗 / 本局两档）+ 公式里可用变量 + `+ - = 清零` 动作（四链 + 锻造图节点）；
2. **变量页 UI**：命名规范常驻横幅、行内校验、试算、允许负数勾选、自动起名；
3. **校验**：撞名 / 循环引用 / 非法名；
4. 多人同步：按之前说的，先做单机正确并在说明里写清联机状态。
