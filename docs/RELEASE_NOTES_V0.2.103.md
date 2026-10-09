# V0.2.103 更新说明

> 你问的「**参数页**对回合数 / 本回合·本场战斗丢弃·消耗·生成·复制牌数 / 获得的额外资源量 / 身上状态数量的监听」
> —— 上一版只把它们做进了**公式**（效果编辑器下面那块「可用量」清单），**参数页的「从哪来」下拉里确实没有**。
> 这一版把它们**加进参数页**了。

## 一、「自定义参数 / 组」页的「从哪来」多了 19 条「实时读数」

在参数页新建/编辑一个参数时，「**从哪来**」下拉原来只有 7 项（固定数字 / 当前血量 / 最大生命 / 金币数 /
能量数 / 最大能量数 / 状态层数）。现在后面接上了**实时读数**那一整批（和公式里「可用量」清单**同一份表**，
名字、生成、回读三处一致）：

| 你点名的 | 参数页里选这个 |
|---|---|
| 回合数 | `Turn`（自己这一侧的第几回合）、`Round`（当前战斗轮数） |
| 本回合 / 本场战斗**丢弃**的牌数 | `DiscardedThisTurn` / `DiscardedThisCombat` |
| 本回合 / 本场战斗**消耗**的牌数 | `ExhaustedThisTurn` / `ExhaustedThisCombat` |
| 本回合 / 本场战斗**生成**的牌数 | `GeneratedThisTurn` / `GeneratedThisCombat` |
| 本回合 / 本场战斗**复制**的牌数 | `CopiedThisTurn` / `CopiedThisCombat`（见下面的说明） |
| 本回合 / 本场战斗获得的**额外资源量** | `StarsGainedThisTurn` / `StarsGainedThisCombat`（只数正数 = 获得） |
| 身上状态数量 | `PowersOnMe`（不含本体内部状态） |
| （另有）四摞张数 | `HandCount` / `DrawCount` / `DiscardCount` / `ExhaustCount` |

用法和别的参数一样：例如 `M = DiscardedThisTurn`，然后在效果的「数值公式」里写 `M*3`
（或任何能用参数的地方），生成出来就是

```csharp
decimal __pM = ((__fgPlayer?.Creature?.CombatState) is null ? 0m : (decimal)CombatManager.Instance.History.Entries
    .OfType<…CardDiscardedEntry>().Count(e => e.Actor == __fgSelf && e.HappenedThisTurn(…))) ;   // M= 本回合丢弃的牌数
```

## 二、两处要说清的（不许糊过去）

- **「复制的牌数」和「生成的牌数」是同一个数**：本体把复制出来的牌也记成「生成」
  （复制走的是同一个 `CardPileCmd.AddGeneratedCardToCombat` → `History.CardGenerated`，历史里没有单独的「复制」条目）。
  所以我**两个名字都列了**（方便你在参数页找得到「复制」），但它们的表达式完全一样；
  回读时会认成「生成的牌数」（同一个数，不影响结果）。
- **「获得的能量」这一版还没做**：本体在战斗历史里**只记「花掉」**（`EnergySpentEntry`），
  没有「获得」的记录，也没有现成的计数器 —— 要做的话得由工具**自己打一个补丁记一份**
  （和「抽牌堆洗过牌」那个记录器同一套做法）。你要的话我就加；同时「花掉的能量」
  （`EnergySpentThisTurn` / `EnergySpentThisCombat`）现在就有。

## 三、验证

- 探针 `RunParamLiveSources`：参数 `M = DiscardedThisTurn`、`N = CopiedThisTurn`、`T = Turn`，
  在卡牌公式里写成 `M + N + T` → 生成 → 真 `dotnet build` **0 错误** → 回读
  `M=DiscardedThisTurn / T=Turn`（`N` 认成 `GeneratedThisTurn`，同一个数）；整轮探针 **21 处 `HasUnparsed = False` / 0 处 True**；
- `--uicheck`：开发版与发布版都是 **2513 PASS / 0 FAIL**（新增 7 条断言：候选里含全部实时读数、
  19+7 的条数、用户点名的两条在、生成的是运行时表达式、回合数表达式、UI 提示文字、生成 → 回读往返）。
