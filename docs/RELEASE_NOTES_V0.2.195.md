# V0.2.195 —— 条件选项新增「上一张打出的牌是 攻击 / 技能 / 能力 / 状态 / 诅咒 牌」

> 用户原话：「在条件选项增加『上一张打出的卡是否为技能/攻击/状态/诅咒/能力牌』」。

## 1. 条件选项里多了 5 条

在**任何能用条件的地方**（卡牌效果的条件 / 整张牌的条件 / 遗物 / 药水 / 自定义状态 / 锻造页的
【条件】模块）现在都能选到这 5 条：

* 上一张打出的牌是**攻击牌**
* 上一张打出的牌是**技能牌**
* 上一张打出的牌是**能力牌**
* 上一张打出的牌是**状态牌**（本体「粘液」那种）
* 上一张打出的牌是**诅咒牌**

它们不需要填数值、不需要选状态、也不需要选「指向对象」—— 就是一个是 / 否判断。

## 2. 判的是「哪一张」

查本体自己的战斗历史 `CombatManager.Instance.History.CardPlaysFinished`（**最后一张结算完的牌**，
本场战斗从头数，不限回合）：

* **卡牌上用**（「打出这张牌时」的效果 / 「能否打出」/ 描金边）：本体是一张牌**结算完**才记进这份历史，
  所以这时读到的正好是**你打出这张牌之前最后打出的那张**；
* **遗物 / 自定义状态在「打出一张牌后」这类钩子上用**：那条牌已经记上了，读到的是**刚才那张**；
* 本场战斗还一张牌都没打过（或者那条历史里没有你的牌）→ 条件**不成立**；
* 生成出来的表达式带两层 null 防护（`base.CombatState` / 玩家对象），所以在牌库、奖励界面这些
  没有战斗状态的地方不会炸；状态挂在敌人身上（`base.Owner.Player` 是 null）也不会炸。

## 3. 生成出来的代码长什么样

```csharp
base.CombatState is not null && base.Owner is not null
    && CombatManager.Instance.History.CardPlaysFinished
           .LastOrDefault(e => e.CardPlay.Player == base.Owner)?.CardPlay.Card.Type == CardType.Skill
```

（遗物 / 药水那条链把 `base.CombatState` 换成 `base.Owner.Creature.CombatState`；自定义状态把玩家那一侧
换成 `base.Owner.Player`。四条链都跟各自原有的条件写法完全同形，没有新运行时、没有新补丁。）

## 4. 顺带做到的两件事

* **卡面 / 悬停文案**：自动描述里会写「条件：上一张打出的牌是技能牌（不满足时这张牌的效果不生效）」
  （和别的条件同一套写法）；
* **从工程恢复**：生成出来的这段表达式能被反推回这 5 条条件（不会「回读之后条件不见了」）。

## 5. 验过的

* 四条链 + 锻造图各配一条条件，整份工程 `dotnet build` **0 error**；
* 卡牌：整张牌条件 + 效果条件同时用时，`IsPlayable` / `ShouldGlowGoldInternal` / OnPlay 里三处都对；
* 从工程恢复：`card.whole=LastCardAttack / card.effect=LastCardSkill / relic=LastCardCurse /
  potion=LastCardPower / power=LastCardStatus` 全部原样认回来；
* **老存档生成结果一个字节都没变**：示例配置 113 个文件、WhiteCloak 存档（31 只自定义状态）412 个文件，
  逐文件对拍只差 `.sln` 里的随机 GUID；
* 自检：本次新增 **11 条**断言（条件表 / 锻造候选 / 四条链的表达式 / 卡面文案 / 校验放行 / 真回读），
  整轮 `Sts2CharForge.exe --uicheck` **3911 PASS / 0 FAIL**。
