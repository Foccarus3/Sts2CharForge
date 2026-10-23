# V0.2.254 — 「死焰」不卡了但灾厄没上：伙伴攻击没往「本次打出的伤害」里记账

## 你报的

> 死焰那张牌是不卡住了，但是灾厄也没上

## 定位（用你这次给的存档 `examplemod.json`（453155 B）逐字对内）

死焰 = `NecrassCard20`，第三条效果是「**施加灾厄，数值 = `ThisCardDamage`**」（本张牌这一次打出的伤害）。
生成出来读的是卡片自己的字段：

```csharp
await PowerCmd.Apply<DoomPower>(choiceContext, foe, ((decimal)this.ForgeThisCardDamage), base.Owner.Creature, this);
```

而 `ForgeThisCardDamage` 只在**普通攻击**（`DamageCmd.Attack(...)` 自己打）那条路上记账 ——
死焰的伤害**全在伙伴身上**（`PetAttack` → `.FromPetAttacker(__necrassPet)`），
伙伴攻击那条路**从来没往这个字段里加过任何东西** ⇒ `ForgeThisCardDamage` 恒为 `0`
⇒ 施加灾厄 **0 层** = 看起来「灾厄没上」（不报错、不卡牌，就是静默 0）。

## 修法

伙伴攻击（`EmitPetAttack`）现在和普通攻击**同一套记账**：

```csharp
MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? __atk1 = await DamageCmd.Attack(base.DynamicVars["PetDamage"].BaseValue)
    .FromCard(this, cardPlay)
    .TargetingAllOpponents(base.CombatState)
    .FromPetAttacker(__necrassPet) ...
    .Execute(choiceContext);
if (__atk1 is not null) ForgeThisCardDamage += (decimal)__atk1.Results.SelectMany(hits => hits).Sum(r => r.TotalDamage);
```

* 全体敌人 / 随机敌人 / 单体 / 全体单位 / 全部友方 / 自己 —— **每一档都记**；
* 只在**这张牌真的用到 `ThisCardDamage`** 时才生成记账（没用到的一张牌，生成文本一个字符都不变）；
* 记账发生在「施加灾厄」那句**之前**（自检里钉死了这个顺序），所以灾厄拿到的是这次伙伴打出的伤害。

修好之后死焰那条链是：伙伴打全体（伤害记账）→ 灾厄 = 本次总伤害 → 大限已至按灾厄层数额外打（V0.2.243 修的那条）。

## 验收

* 自检：开发版 **4337 PASS / 0 FAIL**（新增 3 条：伙伴攻击会记账 / 记账在施加灾厄之前 /
  没用到 `ThisCardDamage` 的伙伴攻击不生成记账）。
* 用你这份存档实跑：`NecrassCard20` 现在生成 `__atk1 = await DamageCmd.Attack(...)` +
  `ForgeThisCardDamage += ...`，再读 `this.ForgeThisCardDamage` 施灾厄 ✓。
* 对拍：Sample 113 / WhiteCloak 412 个文件**逐字节相同**（这两份没有「伙伴攻击 + ThisCardDamage」的牌）。
