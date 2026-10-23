# V0.2.208 —— 「敌人回合结束时卡死」：状态 / 遗物 / 药水的布上不再拼 `.FromCard(null, null)`

> 用户贴的日志（`godot (23).log`）里那条：

```
[ERROR] Combat #5 turn loop died while its combat is in progress; the combat is stuck until the room is restarted:
System.NullReferenceException
   at MegaCrit.Sts2.Core.Commands.Builders.AttackCommand.FromCard(CardModel card, CardPlay cardPlay)
   at AinodiMod.AinodiPower11.BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable`1 participants)
   …
   at MegaCrit.Sts2.Core.Combat.CombatManager.EndPlayerTurnPhaseOneInternal(…)
```

## 1. 真因：三条攻击类效果在「没有这张牌」的布上仍然拼了 FromCard

本体 `AttackCommand.FromCard(CardModel card, CardPlay? cardPlay)` 的第一句就是 `Player owner = card.Owner;` ——
**传 null 必炸**，而且是在**钩子**里炸的：`BeforeSideTurnEnd` 一抛异常，整个回合循环就停了
（日志那句 `the combat is stuck until the room is restarted`）。

V0.2.200 已经修过「锻造图【造成伤害】在状态 / 遗物 / 药水档」这一处，但同样的写法还漏在**另外三处**：

| 效果 | 谁在漏 | 以前生成 |
| --- | --- | --- |
| **大限已至**（Time's Up） | 锻造图的【效果】在状态 / 遗物 / 药水档 | `.FromCard(null, null)` |
| **伙伴攻击 / 伙伴攻击（按生命值）** | 同上（三条分支） | `.FromCard(null, null)` |
| **牺牲伙伴 → 收益是伤害** | 同上（单体 / 群体两条分支） | `.FromCard(null, null)` |

（这次日志里踩的就是「大限已至」这一档，钩子是 `BeforeSideTurnEnd` = 锻造图的「敌人回合结束时」。）

## 2. 修法

新增 `CSharpCodeGen.FromCardClause(cardExpr, cardPlayExpr)`：**上下文是字面量 `null` 就不拼这一段**。

```csharp
await DamageCmd.Attack(((decimal)__fgSelf.GetPowerAmount<DoomPower>() * (3m)))
 .Targeting(__fgSelf)          // 来源留空即可：攻击者本来就靠反射设，不要求先调 FromCard
```

- 卡牌那条链的 `cardExpr` 是 `this` → **逐字节不变**（对拍已证）；
- 状态 / 遗物 / 药水那几条链不再生成 `.FromCard(null, …)`。

## 3. 验收

- 探针（`sacprobe gen`）：状态锻造图「敌人回合结束时 → 大限已至」→ 生成的 `BeforeSideTurnEnd` 里
  **没有** `.FromCard(`，`dotnet build` **0 error**。
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（卡牌链一字未变）。
- `--uicheck`：**3985 PASS / 0 FAIL**（新增 2 条：状态链不再有 `.FromCard(null`、卡牌链照旧 `.FromCard(this, cardPlay)`）。

## 4. 另外一条（不在本版修，先记着）

日志里还有：

```
[ERROR] Localization formatting error! No source extension could handle the selector named "VulnerablePower" at 9
[WARN]  [BaseLib] GetRawText: Key 'AINODI_FORGE_TURN_END_PLAY_POWER.title' not found in table 'powers'
```

- 前者是**描述文案**里把状态名当变量写进了 `{}`（`{VulnerablePower}`）—— 要定位是哪张卡 / 哪个状态的那句描述；
- 后者是「回合结束时自动打出」那个辅助 Power 的**标题本地化键**没写进 `powers.json`（悬停时标题是空的）。

需要的话把那份存档发我，我照这两个线索继续修。

## 5. 版本

- `V0.2.208`（用这一版重新生成一次工程即可，存档不用改）
