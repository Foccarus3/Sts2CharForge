# V0.2.244 — 「狩猎」那个状态本身没有玩法效果（工具现在会说出来）

## 用户报的

「一层仪式鹿给的那个 debuff 晕眩和狩猎不会生效」+ 两份 godot 日志。

## 先分清三个名字（它们不是一回事）

| 用户说的 | 本体里到底是什么 | 效果 |
|---|---|---|
| **晕眩** | 状态**卡** `Dazed`（`DAZED.title = 晕眩`） | 一张打不出去的废牌（塞进抽牌堆 / 弃牌堆占地） |
| **昏眩**（仪式兽给的 debuff） | 状态 `RingingPower`（`RINGING_POWER.title = 昏眩`），1 层 | 给你**所有卡牌**挂上「耳鸣」标记；本回合你只能再打出 **1 张带标记的牌**，玩家回合结束时它自己消失 |
| **狩猎** | ① 卡牌 `TheHunt`（静默猎手稀有攻击）；② 状态 `TheHuntPower`（`THE_HUNT_POWER.title = 狩猎`） | 卡牌本体：**打死目标**时加一份额外卡牌奖励 + 挂这枚状态当图标 |

## 查证结果

### ① 「狩猎」这条状态**天生没有玩法效果** —— 不是工具的问题

本体源码里 `TheHuntPower.cs` 的类注释原文：

> *This power acts as a **visual indicator** to the player that the Hunt card was successful.
> The actual behavior lives in `TheHunt`.*

全项目**只有 `TheHunt.cs` 施加它、没有任何地方读它** —— 所以单独施加这枚状态（你的 zou 存档里是卡牌
18「问号盒子」第 4 条 `ApplyPower / TheHuntPower / 自己 / 1 层`）**什么都不会发生**：它就是个图标。

**以前工具一声不吭**（你只会觉得"施加了却没反应"），所以这一版加了一条校验器提示：

> 卡牌「问号盒子」施加的是「狩猎」：这个状态本体**只是一枚视觉标记** —— 真正的行为写在「狩猎 TheHunt」
> 那张牌里（打出它、把目标打死时才加额外卡牌奖励，并在卡里自己发奖励）。单独施加这个状态不会发生任何事。
> 想做「战斗结束多一份卡牌奖励」，请用效果「**获得卡牌奖励**」，触发时机选「**战斗胜利后**」。

（`EffectCatalog.PowerNoGameplayNote` —— 这张表目前只有 `TheHuntPower` 一条，别的状态不会误报。）

### ② 「昏眩」是本体 仪式兽 的 debuff，你的配置施加它是对的

zou 存档里施加它的是：**自定义状态 #5「天使形态」→ 时机「玩家回合开始时」→ 施加 `RingingPower` 自己 1 层
→ 随即把这个状态自己移除**。生成出来的代码是：

```csharp
public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player) {
    if (player != base.Owner.Player) return;
    await PowerCmd.Apply<RingingPower>(choiceContext, base.Owner, 1m, base.Owner, null);
    { PowerModel? __clrPower = base.Owner.GetPower<ZouPower6>(); if (__clrPower is not null) await PowerCmd.Remove(__clrPower); }
}
```

这是**对的**（目标 = 玩家自己、1 层、给了 choiceContext）。所以它"看起来没生效"只可能是本体的规则：
**它只挂标记给"施加那一刻身上已有的卡牌"，而且只拦"带耳鸣标记"的牌** ——
也就是说：
- 标记是挂在**卡牌**上的（卡面上会多一行耳鸣文字），状态本身不写「只能打 1 张」以外的规则；
- 你在那一回合如果打的是**没被挂上标记**的牌，照样能打；
- 它是「玩家回合结束时移除」的，所以下回合不残留（要靠「天使形态」每回合重新施加）。

这一点我没法从日志里证实（两份日志都是 `zou` 角色在打 `KNIGHTS_ELITE`，里面只有
`ringing_power.png` / `the_hunt_power.png` 两张图标被加载的记录，没有层数/打牌数），
需要你确认一句：**那一回合你身上有没有出现「昏眩」图标、卡牌上有没有「耳鸣」那一行？**
（打开「生成调试日志」重生成，日志会逐条打「施加状态：执行前层数 / 执行后层数、目标是谁」。）

## 本次改动只有一条

* `EffectCatalog.PowerNoGameplayNote`（「这枚状态没有玩法效果」的说明表，目前 `TheHuntPower` 一条）
* `ProfileValidator` 在「施加状态 / 临时增益」时不声不响 → 现在给一条**提示**（不是错误，不拦生成）
* 自检 +2 条：施加「狩猎」会出这条提示 / 这张表只收它会误报的状态

## 验收

- 自检：开发版 **4293 PASS / 0 FAIL**，部署版见下
- 对拍（老存档生成结果）：Sample 113 / WhiteCloak 412 个文件**逐字节相同**（这一版只动校验器文案，不动生成）
