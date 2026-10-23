# V0.2.245 — 「翱翔」的减伤只对**攻击伤害**生效（工具现在会直接告诉你）

## 用户报的

「翱翔减伤给自己没用」

## 查证：这是本体的规则，不是工具漏了

`SoarPower`（中文名「翱翔」）的减伤写在这里（`SoarPower.cs:20-31`）：

```csharp
public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, …)
{
    if (target != base.Owner) return 1m;          // ① 挨打的必须就是持有者
    if (!props.IsPoweredAttack()) return 1m;      // ② 这次伤害必须是「攻击伤害」
    return base.DynamicVars["DamageDecrease"].BaseValue / 100m;   // 默认 50%
}
```

`IsPoweredAttack()`（`ValuePropExtensions.cs:5-13`）= **带 `Move` 且不带 `Unpowered`**。
也就是说：**「失去生命 / 状态造成的伤害 / 自伤」这类 Unpowered 伤害一律不减。**

而本工具生成的伤害正是这个口径：

| 我们生成的 | 用的 ValueProp | 吃不吃翱翔/力量/虚弱 |
|---|---|---|
| 打敌人（普通攻击） | `DamageCmd.Attack(...)`（powered） | 吃 |
| 给**自己**造成伤害 | `Unblockable \| Unpowered \| Move` | **不吃** |
| 失去生命 | `Unblockable \| Unpowered \| Move` | **不吃** |
| 状态（自定义状态）造成的伤害 | `Unpowered` | **不吃** |

第二条和本体完全一致 —— 本体自己的自伤牌（献血 Bloodletting / 血墙 BloodWall / 突破 Breakthrough /
恶魔之盾 DemonicShield …）用的都是 `ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move`。
状态造成的伤害之所以也 Unpowered，是既有设计（不计入「你造成的伤害」统计，也不吃力量/虚弱）。

所以如果你是用「给自己造成伤害 / 失去生命 / 自定义状态跳伤」去试翱翔，**它本来就不会减** ——
要验证它有用，请让**敌人普通攻击**打你（那一下会减 50%）。

另外两点容易误会的：

* 翱翔在本体里是「猫头鹰执政官 OwlMagistrate」的机制，**落地时由它自己 `PowerCmd.Remove<SoarPower>`**；
  工具施加的这一枚不会自动消失，会一直挂着（只对攻击伤害减 50%）；
* 它是 `StackType.Single`，重复施加只刷新，不会叠成 100%。

## 这一版做了什么

新增 `EffectCatalog.PowerMechanicNote`（「这个状态的效果在什么条件下才触发」的说明表），
校验器在「施加状态 / 临时增益」时把这段直接打出来。目前两条：

* **翱翔 SoarPower**：只减攻击伤害（原文见上），并说明本体是猫头鹰执政官那套、工具施加的不会自动移除；
* **昏眩 RingingPower**（上一轮那条）：它不改层数，而是给**施加那一刻你身上的卡牌**挂「耳鸣」标记，
  本回合里**带标记的牌**只有第一张打得出去；玩家回合结束时它自己移除。

提示级别 = **提示**，不拦生成，也不改任何生成结果。

## 验收

- 自检：开发版 **4296 PASS / 0 FAIL**（新增 3 条：翱翔那条提示真的出现 / 说明表只收翱翔与昏眩 /
  我们生成的「自伤」确实是 `ValueProp.Unpowered`）。
- 对拍（老存档生成结果）：只动校验器文案与说明表，生成文本一个字节都不变。
