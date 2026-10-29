# V0.2.323 — 变化的牌飘在界面上 / 状态变掉的牌回抽到手里卡死

用户原话：

> 变化的牌直接飘在界面上，状态变化的卡牌只要回抽到手里，还是会把游戏卡死

## 真因：战斗中造新牌用错了 API（一条根因，两种症状）

「变化卡牌」要造一张新牌再 `CardCmd.Transform(旧牌, 新牌)`。造牌有两种写法：

| 写法 | 结果 |
|---|---|
| `player.RunState.CreateCard(...)` | 造出来的是**牌组范围**的牌：没进当前战斗、没 `AddCard`、没 `AfterCreated` —— 变动画播完，那张牌**飘在界面上**；等它被抽到手里，战斗进程**卡死** |
| `base.CombatState.CreateCard(...)` | ✅ 本体写法（`Begone` / `Guards` 都是这么写的）：`ToMutable` + `AddCard` + `AfterCreated`，新牌真的挂在战斗里 |

我们有两处用错了：

1. **V0.2.320 新加的「变化卡牌 = 0（变自己）」** 那条（`__madeSelf`）用的是 `RunState`；
2. **锻造图上「变化卡牌」**（状态里的那条链）用的是 `__fgPlayer.RunState`。

卡牌页正常的「变化卡牌 ≥ 1 张」那条老路本来就写对了（`CombatState.CreateCard`），所以只在你最近用到的这两条路上出问题。

## 改法

两处都改成战斗范围造牌：

```csharp
CardModel __madeSelf = base.CombatState.CreateCard(ModelDb.Card<目标卡>(), base.Owner);
await CardCmd.Transform(this, __madeSelf);

// 图上：
await CardCmd.Transform(__fgSelC, __fgPlayer.Creature.CombatState!.CreateCard(ModelDb.Card<目标卡>(), __fgPlayer));
```

## 验收

* 你存档里的 **猩红创痕 / 郁蓝创痕** 现在生成 `base.CombatState.CreateCard(...)`。
* 探针 `sacprobe transform0`（列表链 + 图链各一张，数值 0 + 指定卡）：**`dotnet build` 0 错误**。
* 自检：**4542 PASS / 0 FAIL**（+2 条 V0.2.323 断言：卡牌链 / 图链都必须是 `CombatState.CreateCard`，且不再出现 `RunState.CreateCard`）。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）—— 没用到「变化卡牌」的老存档不受影响。
* 只出更新包（`-NoBundle`）。

## 顺带

「变化牌组（全局）」那一档仍然用 `RunState.CreateCard` —— 它改的是**牌组**里的牌（不在战斗里），那条是正确的写法，本次没动。
