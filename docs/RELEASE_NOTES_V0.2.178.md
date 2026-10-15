# V0.2.178 更新说明

> ⚠ **这份说明对应的功能实际是随 V0.2.179 一起发出去的**（当时先起了 V0.2.178 这个号，
> 紧接着又收到「额外资源量跨局」那条 bug，两条合并成一个版本发布）。
> 以 `RELEASE_NOTES_V0.2.179.md` 为准。

> 你要求「**触发时机加一个回合开始后**」—— 加好了，而且顺手把你以后一定会踩的两个相邻时刻也一起给上。
> 三个页面（遗物 / 锻造节点图 / 自定义状态）都能用。

---

## 一、先看本体到底有几个「回合开始」

本体 `Hook.AfterPlayerTurnStart` 这**一个函数**里连着派发**三次**（`Hook.cs:894-917`）：

```csharp
foreach (… ) await model.AfterPlayerTurnStartEarly(choiceContext, player);   // ①
foreach (… ) await model.AfterPlayerTurnStart      (choiceContext, player);   // ②
foreach (… ) await model.AfterPlayerTurnStartLate  (choiceContext, player);   // ③
```

而它整个是 `CombatManager.SetupPlayerTurn` 的**最后一步**（`CombatManager.cs:880-926`）：

> 回能量 → `BeforeHandDraw` → **抽牌** → `AfterPlayerTurnStart`（①→②→③）

所以三个时刻的真实含义是：

| 新增的时机 | 本体钩子 | 什么时候 |
|---|---|---|
| 回合开始后（抽牌前） | `AfterPlayerTurnStartEarly` | 牌**还没抽**（想改这回合抽几张 / 抽牌前先上状态就用它） |
| **回合开始后（抽牌后）** | `AfterPlayerTurnStart` | 手牌已经到手（最常用的一档） |
| 回合开始后（最晚） | `AfterPlayerTurnStartLate` | 同一时刻的最后一轮（本体「血瓶 BloodVial」用的就是它） |

**它们是三个不同的方法名**，所以同一只遗物 / 同一个状态上可以同时各挂一块，不会撞签名（不是「同一个时刻」那条校验拦的对象）。

> 原来那一档「玩家回合开始时 / 每回合开始时」的 id 仍然是 `PlayerTurnStart`、钩子仍然是 `AfterPlayerTurnStart`
> —— 也就是上表中间那一行。**旧存档一个字都不用改**。

## 二、三个页面都接上了

| 页面 | 结果 |
|---|---|
| **遗物**页（触发时机下拉） | 新增 2 条（抽牌前 / 最晚），抽牌后那一档就是原来的「玩家回合开始时」 |
| **锻造节点图**（【触发时机】模块） | 新增 2 条，下拉里一共三条「回合开始后…」 |
| **自定义状态**页（触发时机下拉） | 新增 2 条（和遗物侧**逐字同名同 id**） |

归属守卫三条一样：`if (player != base.Owner) return;`
（联机时别人开局、敌人回合都不会触发你的东西 —— 造得出 choiceContext，所以「抽牌 / 上状态 / 选牌」这类效果也能用，
**不会**像「战斗胜利时」那几档一样只能传 `null!`。）

## 三、验证（真生成 + 真编译）

* 造了一份真存档：**4 只遗物**（1 只走遗物页那条链 + 3 只锻造各一个时刻）+ **3 只自定义状态**（各一个时刻）
  → 真生成 → `dotnet build` **0 错误**；
* 逐个核对生成出来的方法：
  * 遗物页链 → `public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)` + `if (player != base.Owner) return;`；
  * 锻造链 → `AfterPlayerTurnStartEarly` / `AfterPlayerTurnStart` / `AfterPlayerTurnStartLate` 各一份，守卫都在；
  * 状态链 → 同样三个钩子，守卫是 `if (player != base.Owner.Player) return;`；
* **顺手抓到一个真 bug 并修掉**：「抽牌前」那块布上放**抽牌**效果时，一开始生成的是
  `await CardPileCmd.Draw(choiceContext, …)` 却**没有** `choiceContext` 这个参数（因为新 id 没登记进
  `CSharpCodeGen.HasContext`），会生成一句「这个钩子本体没给 choiceContext，也造不出来 —— 需要它的效果会传 null!」。
  实测那个钩子**是带** `choiceContext` 的，登记进去之后就正常了（现在那几个时刻要用 context 的效果都拿得到真对象）；
* `--uicheck` → **3784 PASS / 0 FAIL**（V0.2.177 是 3778，新增 6 条：两条新时机的守卫 / 签名 / 两个菜单逐字同名，
  外加一条「自检清单和 `ForgeTimings.For("Relic")` 一一对应」的兜底 —— 以后往目录里加时机而忘了往自检清单里加，会直接 FAIL）；
* `--samplegen` 与 V0.2.143 基线对拍：113 个文件里仍然**只有 2 个不同**
  （`ExampleMod.sln` 的随机 GUID、`SevenGuard.cs` 的 `GainsBlock` —— 后者是 V0.2.170 起就有的既知差异），**没有扩大**：
  没用到这几档的老存档生成结果逐字节不变。

## 四、你要做的

**重新生成 + 一键构建·安装**（改的是生成出来的 C#）。然后在【触发时机】下拉里就能看到
「回合开始后（抽牌前） / 回合开始后（抽牌后） / 回合开始后（最晚）」。
