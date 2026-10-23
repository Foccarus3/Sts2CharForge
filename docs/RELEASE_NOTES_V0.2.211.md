# V0.2.211 —— 「触发次数填 3+3，但第一次只要 1+1」：两条计数时机共用一份基准值

> 来源：用户实测（自定义状态里两条「每累计几次触发」：打出攻击牌 3 次 + 打出技能牌 3 次）：
> 「触发次数设置的 3+3 但是第一次触发只需要 1+1 就行，后续触发就是 3+3 了」。

## 1. 病因：基准值字段**两条共用一份**

「每累计 N 次触发一次」是按**相对基准的差值**判的（V0.2.168 加的：本体战斗历史是整个回合 /
整场战斗的累计，不减去「刚上这个状态时已经有多少次」，就会把上状态之前发生的事也算进去）：

```csharp
int __cnt = …CardPlaysStarted.Count(e => e.Actor == base.Owner && e.CardPlay.Card.Type == CardType.Attack …);
if (__cntCardPlayedTurn < 0 || __cnt <= __cntCardPlayedTurn) __cntCardPlayedTurn = __cnt - 1;   // ← 基准
if (__cnt > __cntCardPlayedTurn && (__cnt - __cntCardPlayedTurn) % 3 == 0) { … }
```

字段名以前只有「时机 + 范围」（`__cntCardPlayedTurn`），所以**同一个状态上两条都是「打出一张牌时」、
只是「只对哪类牌生效」不同**的计数时机共用同一个字段。而两条的累计值本来就不一样：

- 攻击那条把字段记成 `攻击次数 - 1`；
- 技能那条接着执行，它的累计值更小 → 命中 `__cnt <= 字段` → **把字段改写成 `技能次数 - 1`**（更小的值）；
- 攻击那条下一张牌时拿到的基准已经是被冲掉的值 → 差值虚高 → 第一次只要 1 张就凑够了 3。

## 2. 修法：筛选不同就各记一份

字段名在这种情况下把「只对哪类牌生效」也编进去：

```csharp
private int __cntCardPlayedTurnAttack = -1, __cntCardPlayedTurnSkill = -1;
…
if (__cnt > __cntCardPlayedTurnAttack && (__cnt - __cntCardPlayedTurnAttack) % 3 == 0) { … }
if (__cnt > __cntCardPlayedTurnSkill  && (__cnt - __cntCardPlayedTurnSkill)  % 3 == 0) { … }
```

判据（`CustomPowerGen.CountBaseField` / `CountFieldNeedsFilter`）：同一个状态上**同一条时机 +
同一个范围**的计数时机里出现了**不同的筛选**才带后缀；否则一律沿用老名字：

| 形状 | 字段名 | 说明 |
| --- | --- | --- |
| 只有一条计数时机 | `__cntCardPlayedTurn` | **老工程逐字不变** |
| 两条，筛选相同（都 Attack，只是次数 3 / 5） | `__cntCardPlayedTurn` | 计数表达式一样，共用一份是对的（两条各自按时点到） |
| 两条，筛选不同（Attack / Skill） | `__cntCardPlayedTurnAttack` / `…Skill` | 各记一份（以前被互相冲掉） |
| 留空 与 `Any` | 视为同一个 | 都不筛，不算「不同」 |

## 3. 顺手修掉的同类问题：两条「每 N 个敌人死亡」会**重复计数**

「敌人死亡」本体战斗历史里没有记录，是工具自己 `BumpEnemyDeath()` 数的（每次 +1）。
同一个钩子上有两条「每 N 个敌人死亡」时，以前每条都会调一次 Bump → **一次死亡被数成 2 个**，
「每 2 个敌人死亡触发」实际变成「每 1 个」。现在同一个钩子只 Bump 一次（`int __deathsNow = …`），
两条都用这个数。

## 4. 验收

- 探针存档（同一状态两条计数时机：攻击 3 次 / 技能 3 次）生成 → 两个字段各一份、两条各用各的差值；
  `dotnet build` **0 error**；
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（只差 `.sln` 的 GUID）；
- 你那份 `AinodiMod.json` 重新生成 + `dotnet build` **0 error**（三个带计数时机的状态只多了一段注释，
  说明这份状态为什么各记一份基准值）；
- `--uicheck`：**4009 PASS / 0 FAIL**（新增 5 条：各记一份 / 各用各的 / 单条照旧 / 筛选相同仍共用 /
  双死亡计数只 Bump 一次）。

## 5. 版本

- `V0.2.211`（用这一版重新生成一次工程；**存档不用改**，配置里的 3 就是 3）
