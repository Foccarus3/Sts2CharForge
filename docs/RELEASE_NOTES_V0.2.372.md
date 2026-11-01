# V0.2.372 — 修「斩杀时变化卡牌（全局）」：选牌被击杀后的奖励流程吃掉

## 原因

「斩杀」这个条件判的是**这一击有没有把目标打死** —— 结果要等伤害结算完才知道，
所以这条效果只能排在「造成伤害」**之后**跑。

而你这一击要是把最后一只怪打死，战斗立刻进入**结束 / 奖励流程**：
那一刻再弹「从牌组选牌变化」的界面，会被战斗结束流程顶掉 → 选牌返回空 → 效果静默什么都不做。
（同一类问题：V0.2.363 的「被保留」选牌卡死、V0.2.370 的钩子里选牌卡住，病根都是"在不合适的时机弹选牌"。）

## 改法：斩杀预判（你选的 A）

遇到「造成伤害 + 斩杀时需要选牌」这个组合时，生成端会：

1. **在造成伤害之前**先算这一击的真实伤害（用本体的 `Hook.ModifyDamage`，和卡面预览同一套修正：
   力量、易伤、格挡上限这些都算进去）；
2. 拿它和目标的当前生命比大小 → `__fatalWill`（这一击会不会打死）；
3. 把「斩杀时需要选牌」的那几条效果**挪到伤害之前**，包在 `if (__fatalWill)` 里执行。

生成出来大致是：

```csharp
bool __fatalWill = false;
if (cardPlay is not null && cardPlay.Target is not null && !cardPlay.Target.IsDead) {
    decimal __fatalDmg = Hook.ModifyDamage(..., base.DynamicVars.Damage.BaseValue, ValueProp.Move, this, cardPlay,
                                           ModifyDamageHookType.All, CardPreviewMode.None, out _);
    __fatalWill = __fatalDmg >= (decimal)cardPlay.Target.CurrentHp;
}
if (__fatalWill) {
    …从牌组选牌 + 变化（战斗还活着，界面正常）…
}
__lastAttack = await DamageCmd.Attack(base.DynamicVars.Damage.BaseValue)…   // 伤害照旧
```

* 只有**真的存在**这个组合时才这么生成；其它牌（包括普通攻击牌、以及斩杀但不需要选牌的效果）生成文本**一个字都不变**；
* 斩杀条件本身仍然保留：不需要选牌的斩杀效果，还是按「打完再判」的老路走。

## 自检

**4706 PASS / 0 FAIL**（+2 条：斩杀+选牌必须「预判 → 选牌 → 伤害」这个顺序 / 普通攻击牌不许平白多出 `__fatalWill`）。

## 需要你验证的地方

1. 更新到 **V0.2.372** → 生成 → 构建安装；
2. 用那张「斩杀时变化卡牌（全局）」去杀最后一只怪：
   - 应该**先弹选牌界面**（战斗还活着），选完牌正常变化，然后才结算伤害 / 进入奖励。

⚠ 要注意的边界：预判用的是「这一击的伤害 ≥ 目标当前生命」。
如果这一击会被**格挡**挡掉一部分，`Hook.ModifyDamage` 只算「伤害数值」，不含格挡
（格挡是伤害结算时再扣的）—— 也就是说「目标有格挡、伤害刚好够打穿血量」这种极窄的情况，
预判可能会认为是斩杀而实际没打死（此时选牌照弹、变化照做）。遇到这种边界告诉我，我再把格挡算进预判。
