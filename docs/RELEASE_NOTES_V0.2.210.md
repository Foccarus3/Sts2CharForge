# V0.2.210 —— 自定义状态里能放「给伙伴施加状态」等 6 个宠物效果了（用户截图那条报错）

> 来源：用户截图 —— 状态「新状态29」『打出一张牌时（这张牌还没生效）』第 1 条是「给伙伴施加状态」，
> 生成时被校验拦成错误，对话框里把「状态触发器里能用的是：…」那份清单原样列了出来。
>
> **根因**：`PowerTriggers.SupportedEffectKinds`（自定义状态那条链的白名单）里只有 `SummonPet` / `PetSacrifice`，
> 其余 6 个宠物效果（施加状态 / 治疗 / 失去生命 / 最大生命 / 承伤开关）既没写进白名单、生成端
> （`CustomPowerGen.EmitEffect`）也没有对应实现。

## 1. 状态触发器现在能用这 6 个宠物效果

| 效果 | 生成的那一句（状态钩子里，`base.Owner` **本身**就是 Creature） |
| --- | --- |
| 给伙伴施加状态 | `PowerCmd.Apply<状态>(choiceContext, __伙伴, 层数, base.Owner, null)` |
| 治疗伙伴 | `CreatureCmd.Heal(__伙伴, 点数)` |
| 伙伴失去生命 | `CreatureCmd.Damage(choiceContext, __伙伴, 点数, ValueProp.Unblockable \| Unpowered \| Move, base.Owner, null, null)` |
| 伙伴最大生命 +N | `CreatureCmd.GainMaxHp(__伙伴, N)`（本体内部会 Heal 等量） |
| 伙伴替主人承伤（开 / 关） | `PowerCmd.Apply<…ForgePetGuardianPower>(…)` / `PowerCmd.Remove<…>(__伙伴)` |

写法与卡牌那条链**复用同一批发射器**（`EmitPetApplyPower` / `EmitPetHeal` / `EmitPetLoseHp` /
`EmitPetGainMaxHp` / `EmitPetGuard`），只把「上下文表达式」换成状态链的：

- 「找那只伙伴」= `XxxCmd.Get(base.Owner.Player)` 一行，**拿不到就整条跳过**（伙伴还没上场 /
  上一场战斗结束时被本体清掉了 —— 和卡牌链同一个行为，不会报错也不会做错人）；
- 「施加者 / 主人」= `base.Owner`（卡牌链里写 `base.Owner.Creature`，状态链里没有 `.Creature`）；
- 「这张牌」= `null`（状态里根本没有 `CardModel`；传 `this` 会 CS1503，整个模组编译不过）。

「全部召唤物」也支持：按**已启用的那几只**逐只展开（每只各自判在场，不在场的那只跳过）。

## 2. 顺手修掉的 4 个「生成出来编译不过 / 静默失效」（都是上面这条路踩出来的）

1. **状态里的「牺牲伙伴」引用了不存在的东西**：以前生成 `base.DynamicVars["…"]` / `base.DynamicVars.CalculatedBlock`
   / `cardPlay.Target` —— 这三样在自定义状态类里都没有（CS0103 / CS1061 / CS1503）。
   现在：固定数值档直接用状态链算好的数值；按生命值算的档内联读这只宠物自己的生命值；
   「指定敌人」按触发时机能给的那个敌人 / 可打的第一个敌人解析（和「造成伤害」同一套）。
2. **状态里的「全部召唤物」是空转**：`PetGen.Resolve("*")` 返回 null → 生成一句
   「⚠ 这条件没有可用的召唤物」然后什么都不做，而**校验器明确允许**这个组合。现在逐只展开。
3. **「下回合生效」+ 宠物效果**：延迟状态类体里没有 `base.DynamicVars`，以前会生成
   `base.DynamicVars.Heal.BaseValue` 这种引用 → 编译不过。现在这类数值一律在生成期折成字面量。
   「给伙伴施加状态 + 下回合生效」还修了一个**死循环**：延迟状态类以前会把自己再挂一遍
   （永远施加不到宠物身上），现在它在类体里直接施加真正的状态。
4. **两只伙伴各配一条「下回合生效：给伙伴施加中毒」会撞类名**（CS0101）：延迟状态类名现在把
   「哪一只伙伴」也编进去（`…ForgeDelayedPetApplyPowerPoisonPowerDaYun`）。
   「下回合生效」+「全部召唤物」这一档生成不出来（延迟状态里只认一只），校验器直接报错说清怎么改。

## 3. 文案

`PowerTriggerCatalog.EffectText` 补上这 6 条的描述文字（以前会原样印出 `PetApplyPower` 这种英文种类名）：

```
打出一张牌时（这张牌还没生效）：给伙伴施加 2 层易伤，治疗伙伴 5 点生命，伙伴失去 3 点生命，
伙伴最大生命 4（当前生命同时回复），伙伴替主人承伤（开），取消伙伴替主人承伤（关）。
```

## 4. 验收

- 用一份专门的探针存档（2 只伙伴 + 状态触发器里放满 6 个效果 + 卡牌上的「下回合生效」组合）
  生成工程后 `dotnet build`：**0 error**；
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（只差 `.sln` 的 GUID）；
- `--uicheck`：**4004 PASS / 0 FAIL**（新增 15 条，含「不再引用 base.DynamicVars / cardPlay.Target」、
  「全部召唤物逐只展开」、「下回合生效走延迟状态」这几条红线）；
- 生成出来的示例（节选）：

```csharp
// 查一次这只伙伴（可能还没召唤 / 上一场战斗结束时被本体清掉了）：不在场就整条跳过
Creature? __daYun = DaYunCmd.Get(base.Owner.Player);
if (__daYun is not null) {
    await PowerCmd.Apply<VulnerablePower>(choiceContext, __daYun, 2m, base.Owner, null);
}
```

## 5. 版本

- `V0.2.210`（用这一版重新生成一次工程即可，存档不用改）
