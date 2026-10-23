# V0.2.243 — 「死焰」打出后浮空：`大限已至` 对「全体敌人」还在要 `cardPlay.Target`

## 用户报的

「死焰这张牌打出后浮空」+ 存档 `examplemod.json`（Necrass 角色）。

## 诊断（照生成的源码就能看出）

「死焰」= 卡 20 `NecrassCard20`（攻击 / 基础 / 0 费 / 消耗 + 保留），三条效果：

| # | 效果 | 作用对象 |
|---|---|---|
| 1 | 伙伴攻击（万火归一）3 点 × 6 段 | **全体敌人** |
| 2 | 施加状态「灾厄」，数值 = `ThisCardDamage` | **全体敌人** |
| 3 | **大限已至**（伤害 = 目标身上灾厄层数） | **全体敌人** |

三条都是「全体敌人」⇒ 这张牌的 `TargetType` 是 `TargetType.AllEnemies` ⇒ **打出时不会弹选目标**，
`cardPlay.Target` 是 `null`。而生成端给第 3 条写的是：

```csharp
// 大限已至
ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");   // ← 第一句就炸
await DamageCmd.Attack(base.DynamicVars.CalculatedDamage)
    .FromCard(this, cardPlay)
    .Targeting(cardPlay.Target)
    …
```

于是打出瞬间抛 `ArgumentNullException`，异常从 `OnPlay` 里冒出来、出牌队列中断：
**牌停在出牌位浮空、这一回合也打不出别的牌**。日志里那句
`Combat #N turn loop died while its combat is in progress; the combat is stuck` 就是它的伴生现象
（你附的 `godot…23.14.43.log` 是「主菜单 → 开一局 → 尼奥事件 → 退出」那份，没有战斗段，所以里面查不到 —— 这次靠存档 + 生成的源码定位）。

这就是 V0.2.217 那个「判空和 TargetType 打架」的同一个洞，只犯在 `大限已至` 这一条上：
`EmitTimesUp` 从来不看「作用对象」，永远写 `cardPlay.Target`。

## 修法

1. **`EmitTimesUp` 按作用对象展开**（`CSharpCodeGen.EmitTimesUp`）：
   * 单体敌人 / 队友 → 老样子（`ThrowIfNull(cardPlay.Target)` + `.Targeting(cardPlay.Target)`，**逐字节不变**）；
   * 全体敌人 → `foreach (Creature __tuFoe in base.CombatState.HittableEnemies)` 逐个 `.Targeting(__tuFoe)`
     （每个敌人各按**自己身上**的灾厄层数算 —— 伤害倍率本来就是每目标求值的）；
   * 随机敌人 / 全体单位 / 全部友方 / 自己 → 各按自己那一档展开。
2. **同一个洞的另一半：`PetAttack`**（`EmitPetAttack`）。它以前只有「全体敌人 / 随机敌人」两档，
   其余全落进 `default` 读 `cardPlay.Target` —— 「全体单位 / 全部友方 / 自己」这三种伙伴攻击同样会
   一打出去就抛异常。现在一并按作用对象展开。
3. **补一条结构性自检**，堵死这一类问题（这是关键，不然下次换个种类又冒出来）：
   对 **19 种会碰生物的效果 × 5 个多目标档**（全体敌人 / 全体单位 / 全部友方 / 随机敌人 / 自己）
   逐格生成一张卡，断言「生成的源码里**不出现** `cardPlay.Target`」——因为多目标档的 `TargetType`
   永远不会弹选目标，读了就是 null。实测第一遍就抓出了 `PetAttack` 那三格。
4. 再加一条**真编译**自检：拿「大限已至（自己 / 单体敌人 / 随机敌人 / 全体敌人）+ 回复生命·失去生命·格挡（全部友方 / 全体敌人）」
   生成整个工程 → `dotnet build` **0 个错误**（防止新展开的循环只是「看起来对」）。

## 你那张牌修好之后长这样

```csharp
// 大限已至：造成等于目标身上灾厄层数的伤害（本体 Time's Up 的写法）
// 全体敌人：逐个打（每个敌人各按**自己身上**的灾厄层数算）
foreach (Creature __tuFoe in base.CombatState.HittableEnemies) {
    await DamageCmd.Attack(base.DynamicVars.CalculatedDamage)
        .FromCard(this, cardPlay)
        .Targeting(__tuFoe)
        .WithHitFx("vfx/vfx_attack_slash")
        .Execute(choiceContext);
}
```

## 验收

- 自检：开发版 **4291 PASS / 0 FAIL**（新增 3 条：多目标档不读 `cardPlay.Target`（19×5 格全干净）/
  「大限已至 + 全体敌人」逐个打 / 新形状真编译 0 错误）。
- 对拍（老存档生成结果）：Sample 113 个文件、WhiteCloak 412 个文件 —— **逐字节相同**，差异只有 `.sln` 的 GUID
  （这两份都没有「大限已至 / 伙伴攻击」走在多目标档上的牌）。
- 用你这份 `examplemod.json` 单独生成 `NecrassCard20` 已确认：不再有 `ThrowIfNull`，改成逐个敌人打。

> 只改了这一张牌会走到的两条生成分支，其余种类的行为与 V0.2.242 完全一致。
