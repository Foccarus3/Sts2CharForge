# V0.2.200 —— 「牺牲伙伴」的收益选了「造成伤害」以后，能自己挑打谁了

> 用户原话：「召唤物即死：收益的造成伤害能不能加个选择对象」

## 1. 以前为什么没得选

「牺牲伙伴换收益」这一条效果，**收益 = 造成伤害**时生成端一直写死成这样：

```csharp
decimal dmg = base.DynamicVars["PetSacrificeDamage"].BaseValue;
await CreatureCmd.Kill(__pet);
await DamageCmd.Attack(dmg)
    .FromCard(this, cardPlay)
    .Targeting(cardPlay.Target)      // ← 永远是「这张牌选中的那个目标」
    .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
    .Execute(choiceContext);
```

界面上也没有「作用对象」那一栏：那张表的判据只按**效果种类**（`EffectFieldPolicy.UsesTargetSide`），
`PetSacrifice` 不在名单里 ⇒ 整行藏起来。于是「牺牲一只伙伴、对所有敌人造成伤害」根本配不出来。

## 2. 现在：收益 = 造成伤害 → 「作用对象」那一栏会出来

- 判据挂在**收益类型**上（不是效果种类）：`收益 = 造成伤害` 才摆这一栏。
  收益是格挡 / 获得增益·减益时不摆 —— 那两档「对谁生效」没有第二种可能（格挡给自己、层数也挂自己身上），
  摆出来只会让人以为能改。
- 能选：**单体敌人（这张牌的目标）/ 全体敌人 / 随机敌人 / 全体单位 / 指定队友 / 全部友方**。
- 生成出来的代码跟着「作用对象」走（一只一只打，和状态链同一个 `EmitPerCreature`）：

```csharp
// 收益的伤害按「作用对象」打（全部敌人）
if (base.Owner.Creature.CombatState is not null) {
    foreach (Creature foe in base.Owner.Creature.CombatState.HittableEnemies) {
        { await DamageCmd.Attack(dmg).FromCard(this, cardPlay).Targeting(foe)
              .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choiceContext); }
    }
}
```

随机敌人 = 挑 `命中次数` 只（那只框对这一档照旧摆着，填 2 就挑 2 只）。
默认档「单体敌人」生成出来的文本**一个字符都没变**（老存档逐字节红线）。

## 3. 卡面描述也跟着说对

以前不管怎么配，卡面一律印「然后它对**指定敌人**造成 N 点伤害」。现在跟着作用对象：

| 作用对象 | 卡面 |
| --- | --- |
| 单体敌人（默认） | 然后它对**指定敌人**造成 … |
| 全体敌人 | 然后它对**所有敌人**造成 … |
| 随机敌人 | 然后它对**随机 2 个敌人**造成 … |
| 全体单位 | 然后它对**场上所有单位**造成 … |
| 指定队友 / 全部友方 | 然后它对**指定队友 / 所有友方**造成 … |

列表行尾那个「→ 谁」也补齐了（宠物类效果本来一律不写，只有这一档写）。
顺手把「造成伤害 + 全体单位」以前印成「→ 自己」的老毛病改对了（`全体单位 / 全部友方 / 指定队友` 现在各有说法）。

## 4. 校验器：不该放行的照旧拦住

- 「牺牲伙伴 → 造成伤害 + 全体单位 / 队友」→ **放行**（生成端真的展开了，探针真编译 0 error）。
- 「牺牲伙伴 → 格挡 / 状态 + 全体单位」→ 照旧**报错拦住**（那一档不读作用对象，放行等于静默变成「对自己」）。
- 队友那两档只放行**卡牌链**（状态钩子里 `base.Owner` 本身就是 `Creature`，放行会生成编译不过的代码）。

## 5. 验收

- 探针：五种作用对象各做一张卡 → `dotnet build` **0 error**（`SacTargetEnemy / AllEnemies / RandomEnemies / AllUnits / AllAllies`）
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（默认档没变）
- `--uicheck`：**3949 PASS / 0 FAIL**（新增 12 条：那一行的显隐 / 切收益要通知 / 五种作用对象的生成 / 卡面措辞 / 校验器两侧）

## 6. 同一版里还带上了另一条修复（锻造图「造成伤害」在状态 / 遗物 / 药水档卡死）

> 用户原话：`godot - 副本(1).log`（ReyMod）+「还是会卡死」。

日志最顶上那条不属于引擎的帧：

```
Combat #1 turn loop died … NullReferenceException
   at AttackCommand.FromCard(CardModel card, CardPlay cardPlay)
   ← Rukayukiking.Gokuen.AfterSideTurnStart ← Hook.AfterSideTurnStart ← CombatManager.StartTurn
```

「一回合一次、然后 stuck until the room is restarted」。真因：锻造图的【造成伤害】模块在
**状态 / 遗物 / 药水**作用域里**无条件**拼 `.FromCard(...)`，而那三档没有「自己这张牌」
（`CardSelfExpr` / `CardPlayExpr` 是字面量 `null`）→ 生成 `DamageCmd.Attack(x).FromCard(null, null)`；
本体 `AttackCommand.FromCard` 第一句 `Player owner = card.Owner;` → NRE → `StartTurn` 整条钩子链断掉。

已改成：拼 `FromCard` 之前先看有没有「这张牌」，没有就整段不拼（伤害按无来源处理）。
卡牌作用域生成的文本**逐字不变**。

## 7. 版本

- `V0.2.200`

