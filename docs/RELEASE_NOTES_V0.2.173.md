# V0.2.173 更新说明

> 回答你报的两条：
> ①「**每打出 3 张技能 3 张攻击获得增益，但实际没生效**」—— 生成出来的判据本身写错了，已修（第一节）。
> ②「**多人催促文本不是宝箱房的，是多人联机时右下角的 ping**」—— 你指正得对：V0.2.172 加的那一栏
> **绑错了数据**（绑到了宝箱房那句「开抢！！！」），已按 ping 那句改正（第二节）。

---

## 一、真因：计数用的是「打完之后」的历史，而钩子跑在「打之前」


本体打一张牌的顺序（`MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper`，反编译原文）：

```csharp
await Hook.BeforeCardPlayed(combatState, cardPlay);          // ← 我们的「打出一张牌时」挂在这里
if (…) CombatManager.Instance.History.CardPlayStarted(combatState, cardPlay);
Task task = OnPlay(branchingPlayerChoiceContext, cardPlay);  // 这张牌真正生效
await branchingPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion(task);
…
if (…) CombatManager.Instance.History.CardPlayFinished(combatState, cardPlay);
if (…) await Hook.AfterCardPlayed(combatState, choiceContext, cardPlay);
```

也就是说：**「打出一张牌时」那一刻，这一张牌还没被记进 `CardPlaysFinished`。**
而 V0.2.172 生成的是：

```csharp
// 触发时机：打出一张牌时（这张牌还没生效）
public override async Task BeforeCardPlayed(CardPlay cardPlay) {
    if (cardPlay.Player != base.Owner) return;
    if (cardPlay.Card.Type != CardType.Skill) return;
    if (CombatManager.Instance.History.CardPlaysFinished.Count(…) % 3 != 0) return;   // ← 差了一张
    …
```

于是「累计数」永远比玩家看到的少 1：

| 你打出的第几张牌 | 代码里读到的累计数 | `% 3 != 0` |
|---|---|---|
| 第 1 张 | 0 | 挡住 |
| 第 2 张 | 1 | 挡住 |
| 第 3 张 ← 本该在这里响 | 2 | 挡住 |
| 第 4 张 | 3 | **放过**（但这时 `cardPlay` 是第 4 张） |

更要命的是**牌型筛选的判据在别处**：`if (cardPlay.Card.Type != CardType.Skill) return;` 只筛
「当前这一张」，计数器本身仍然在数**所有**打出的牌。两条一起作用，就是：

> 第 3 张（技能）时累计 = 2 → 挡住；第 4 张（不是技能）时累计 = 3 会放过，但那张牌先被类型判据挡住了
> → **填 3 之后一次都不会触发**。这就是你看到的「实际没生效」。

（填 1 的时候看着是好的：`0 % 1 == 0` 永远成立，所以每次都能过 —— 这也是这个坑一直没被发现的原因。）

**修法**（`CSharpCodeGen.CountExpr`）：

1. 「打出一张牌时」改用本体的 **`CardPlaysStarted`**（`OnPlay` **之前**记，**含正在打出的这一张**）——
   和你工具箱里那条内置读数「本回合打出的牌数（含正在打出的这一张）」同一口径；
2. 「只对哪类牌生效」**也拼进计数表达式**，不再只筛当前这一张：

```csharp
// 触发时机：打出一张牌时（这张牌还没生效）
public override async Task BeforeCardPlayed(CardPlay cardPlay) {
    if (cardPlay.Player != base.Owner) return;
    if (cardPlay.Card.Type != CardType.Skill) return;
    if (CombatManager.Instance.History.CardPlaysStarted
            .Count(e => e.Actor == base.Owner.Creature
                     && e.CardPlay.Card.Type == CardType.Skill          // ← V0.2.173 新增
                     && e.HappenedThisTurn(base.Owner.Creature.CombatState)) % 3 != 0) return;
    …效果…
```

现在「每 3 张技能牌」数的是**技能牌**了：第 3 张技能 → 累计 3 → `3 % 3 == 0` → 触发。

> ⚠ **不需要改你的存档**（配置本来就是对的），但**必须重新生成 + 重新编译安装一次模组**才生效 ——
> 改的是生成出来的那份 C#。

---

## 二、「多人催促文本」= 联机右下角那个 **Ping** 按钮那句（V0.2.172 绑错了数据，已改正）

你说的对，我先把两件事分清楚（本体源码里是**两套完全无关**的东西）：

| | 触发方式 | 本体键 | 显示在哪 |
|---|---|---|---|
| **多人催促文本**（本栏，你指的那句） | 战斗里你**结束回合后**、还有别人没结束回合时，右下角滑出那个 **Ping** 按钮，按一下 | `characters` 表的 `<角色>.banter.alive.endTurnPing` | **你头顶**冒 1.5 秒的对话气泡（所有玩家都看得到） |
| 宝箱房那句 | 联机开宝箱房、抢遗物时 | `gameplay_ui.TREASURE_FIGHT_TEXT`（本体「开抢！！！」） | 宝箱房**屏幕中间**那行字 |

本体的链路（反编译确认）：`NPingButton.OnRelease` → `RunManager.Instance.FlavorSynchronizer.SendEndTurnPing()`
→ 广播 `EndTurnPingMessage` → 每个客户端 `CreateEndTurnPingDialogueIfNecessary(player)` →
`new LocString("characters", player.Character.Id.Entry + ".banter." + (死了?"dead":"alive") + ".endTurnPing")`
→ `NSpeechBubbleVfx.Create(…, 1.5 秒, 角色气泡色)`。本体每个角色都有一句
（铁甲战士 `Make haste.` / 静默猎手 `...` / 故障机器人一串 jitter 乱码 / 亡灵契约师 `Any day...`）。

**V0.2.172 我加的那一栏绑到了 `Profile.Art.TreasureFightText`**（宝箱房那句「开抢！！！」）——
所以你在那一栏里写的东西，联机按 Ping 时**根本没变**（变的只有宝箱房那行字）。这一版改正：

1. 新增 `Profile.EndTurnPingText`（存档字段，留空 = 沿用默认）；
2. 角色页那一栏**改绑它**，标题与说明也都改成「联机时右下角『Ping』按钮按下去冒出的那句」；
3. `characters.json` 里 `<角色>.banter.alive.endTurnPing` 不再写死「……继续。」——
   你填了就写你填的，没填仍然写「……继续。」（**老存档 / `--samplegen` 生成结果逐字不变**）；
4. 宝箱房那句**照旧**在「美术」页那个框里（`Profile.Art.TreasureFightText`），一个字没动；
   两栏现在是**两份数据**，不再是同一份；
5. `--recover` 跟着认：读出来正好是「……继续。」→ 当作「没填」（否则恢复出来的存档会
   无中生有多一句，再生成一次 `characters.json` 就跟第一次不一样 —— 和「角色说明」那个坑同一个道理）。

> 这一项**不需要任何补丁**（就是我们自己角色本地化表里的一行），也不用改存档格式。

---

## 三、同批修掉的另外两处（同一个「计数族」家族）

### 3.1「一张牌结算完毕时」填了「每累计 N 次」一个字符都不生成（静默失效）

`CountExpr` 的 switch 里根本没有 `CardResolved`（旧版的「打出一张牌后」）这一档 →
`TriggerAmountLines` 认不出来就返回空数组（老行为是「一个字符都不生成」），
**而界面上那一栏照常显示、你也照常能填**。已补上：

```csharp
// 结算完毕时 = AfterCardPlayedLate：本体先记 CardPlayFinished 再调钩子 → 这一张已经在里面了
if (CombatManager.Instance.History.CardPlaysFinished
        .Count(e => e.Actor == base.Owner.Creature && e.CardPlay.Card.Type == CardType.Skill
                 && e.HappenedThisTurn(base.Owner.Creature.CombatState)) % 3 != 0) return;
```

顺带把**「只对哪类牌生效」那一栏在「一张牌结算完毕时」也放出来**：生成端一直在写那句
`cardPlay.Card.Type != CardType.Power` 守卫，但界面上只有「打出一张牌时」才显示这一栏
（`RelicTriggerSpec.ShowCardFilterField` 只认 `CardPlayed`）—— 你能生成、却看不到也改不了。

### 3.2 状态侧：「每累计 N 次」会把**施加这个状态的那一张牌**也算进去

状态侧从 V0.2.168 起有「从这个状态被施加那一刻开始数」的基准值，但它用的也是 `CardPlaysFinished`
（少算这一张）；换成 `CardPlaysStarted` 之后那一张牌就被算进基准了 —— 于是
「上状态前已经打了 3 张、状态是打第 4 张施加的」这种情形会**一上状态就当场响一次**。
所以判据加了一条 `__cnt > 基准`：**施加这个状态的那一张牌不算**。

```csharp
int __cnt = …CardPlaysStarted.Count(…含正在打出的这一张…);
if (__cntCardPlayedTurn < 0 || __cnt <= __cntCardPlayedTurn) __cntCardPlayedTurn = __cnt - 1;
if (__cnt > __cntCardPlayedTurn && (__cnt - __cntCardPlayedTurn) % 3 == 0) {   // ← V0.2.173 新增前半句
```

### 3.3 回读（`--recover`）跟着认新写法

- 卡牌条件「至少打出 N 张牌」生成的是 `CardPlaysStarted.Count(…) >= N` 了 ——
  回读正则两个都认（老工程里仍是 `CardPlaysFinished` 的照旧认出来），否则老工程回读会**静默丢掉**这条条件。

---

## 四、验证

- `dotnet build Sts2CharForge.sln -c Debug` → **0 错误**；
- `--uicheck`：Debug **3771 PASS / 0 FAIL**（V0.2.172 是 3762；新增/改写 11 条：
  打出一张牌时用 `CardPlaysStarted` 且**不含** `CardPlaysFinished.Count`、计数里按牌型筛、
  没填牌型时计数里不带类型判据、状态侧 `__cnt > 基准`（含敌人死亡那一档）、
  角色页那一栏绑的是 `Profile.EndTurnPingText` 且**不再**绑宝箱房那句、
  留空时 `characters.json` 仍是「……继续。」（红线）、填了写进 `banter.alive.endTurnPing`、
  催促文本的存档往返 + 富文本校验器）；
- **生成 → 回读往返**：探针存档（遗物 = 打出一张牌时 + 每 3 次 + 只对技能牌）走
  `ModGenerator.Generate` → `ProjectRecovery.FromProject`，回读结果
  `触发时机=CardPlayed 次数=3 范围=Turn 牌型=Skill` —— 逐项一致；
- **逐条比对生成出来的判据**（7 组探针：打出一张牌时×3 + 结算完毕时 + 消耗 / 抽到 / 弃掉）：

| 配置 | 生成出来的计数行 |
|---|---|
| 打出一张牌时 · 每 3 次 · 只技能 | `CardPlaysStarted.Count(e => e.Actor == base.Owner.Creature && e.CardPlay.Card.Type == CardType.Skill && e.HappenedThisTurn(…)) % 3 != 0` |
| 打出一张牌时 · 每 3 次 · 不过滤 | `CardPlaysStarted.Count(e => e.Actor == base.Owner.Creature && e.HappenedThisTurn(…)) % 3 != 0` |
| 结算完毕时 · 每 3 次 · 只技能 | `CardPlaysFinished.Count(… && e.CardPlay.Card.Type == CardType.Skill && …) % 3 != 0`（*以前一个字符都没有*） |
| 消耗 / 抽到 / 弃掉 · 每 3 次 | 原样（本体这四条都是**先记账再调钩子**，本来就含这一次，未改动） |

> 反编译依据（本机 `sts2.dll`）：
> · `CardModel.OnPlayWrapper`：`Hook.BeforeCardPlayed` → `History.CardPlayStarted` → `OnPlay`
>   → `History.CardPlayFinished` → `Hook.AfterCardPlayed(Late)`；
> · `CombatHistory`：`CardPlaysStarted` / `CardPlaysFinished` 分别就是 `CardPlayStartedEntry` /
>   `CardPlayFinishedEntry` 两条流；`HappenedThisTurn` 在基类 `CombatHistoryEntry` 上
>   （所以 `CardPlaysStarted` 也能用「本回合」范围）；
> · `CardPileCmd`：`History.CardExhausted` / `CardDrawn` / `CardDiscarded` / `CardGenerated`
>   都是**紧挨着写在那条 `Hook.AfterX` 之前**。
