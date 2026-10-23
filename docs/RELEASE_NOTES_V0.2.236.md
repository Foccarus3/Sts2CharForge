# V0.2.236 — 调试日志补一行「这一次是第几次打出」（投斧 / 回响这类改 playCount 的东西能验证了）

## 用户报的

「序号 48 卡无法生效头斧」+ 存档 `magete.json`。

## 查证：48 号卡是什么

存档里 48 号（卡牌页第 48 行）= **禁忌·多重存在**（`MargotScarletCard48`），能力牌 / 稀有 /
1 能量 + 3 额外资源量，效果只有一条：**施加 `EchoFormPower`（多层存在）1 层**。生成出来的源码是：

```csharp
protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) {
    await PowerCmd.Apply<EchoFormPower>(choiceContext, base.Owner.Creature, (1m), base.Owner.Creature, this);
}
```

即：这张牌的两次「打出」之间没有任何差别 —— 第二次也会照样执行这行 `PowerCmd.Apply`。

## 查证：头斧（本体 `ThrowingAxe`，中文名「投斧」）到底怎么加成

- `ThrowingAxe.ModifyCardPlayCount(...)` —— **每场战斗第一张**打出的牌 `playCount + 1`，
  **不看牌的类型**（Attack / Skill / Power 都加），打完就 `UsedThisCombat = true`（本场不再加）。
- 本体 `CardModel.OnPlayWrapper`（CardModel.cs:1896-1971）：
  `for (int i = 0; i < playCount; i++) { … await OnPlay(...); }` ——
  `Type == CardType.Power` 只是换了个飞牌特效（`PlayPowerCardFlyVfx`），**没有跳过后续几次**。
- `EchoFormPower` 的 `StackType` 是 `Counter`，`PowerCmd.Apply` 第二次走
  `FindExistingInstanceForStacking` → `ModifyAmount(+1)` → **层数从 1 变 2**。

所以「投斧 + 48 号卡」在机制上应当是**多层存在 2 层**（如果身上本来已经有 1 层，则是 3 层 ——
playCount 是在进 OnPlay 循环**之前**算的）。既然用户实际只看到 1 层，那就只剩一种可能：
**这一次 `playCount` 压根没被改成 2**（最典型：那一场战斗里已经先打出过别的牌，
投斧的「每场战斗只加成第一张」已经被用掉了），而不是「效果被吞掉」。

问题是：这件事**在界面上看不出来**——能力牌打出后就进 `PileType.None`，
多打的那一次只有「牌飞两遍」这种一闪而过的表现（用户上一轮报「能力牌不触发头斧」就是同一个坑）。

## 这一版做了什么

「生成调试日志」开着时，每张卡的 `OnPlay` **开头**多打一行：

```
[模组调试] 卡牌「禁忌·多重存在」(MargotScarletCard48) → 打出第 2/2 次
```

- 只有 `1/1` = `playCount` 没被改过（投斧当场的加成已经被别的牌用掉 / 没这个遗物）；
- 出现 `2/2` = 多打的那一次**真的执行了 OnPlay**，那么「看不出效果」只能是
  效果本身不可叠加（例：单一实例的状态第二层看不出来），而不是被吞掉。

实现：`DebugLogGen.PlayCount(w)` 读本体的 `cardPlay.PlayIndex` / `cardPlay.PlayCount`
（CardModel.cs:1915-1925 赋的值，只读不改），
由 `CSharpCodeGen.CardSource` 在 `OnPlay` 里发射一次。

## 红线

- 开关**关着**时一个字符都不多（`PlayCount` 第一句就是 `if (!On) return;`）——
  老存档生成结果与 V0.2.235 **逐字节相同**（Sample 113 / WhiteCloak 412 实测）。
- 这一行同样带 `// CET:DebugLog` 标记 —— 「从工程反推存档」按行跳过它，不会变成假效果。
- 自检 +3 条（开着时有「打出第 k/K 次」、带标记、关着时干净）。
