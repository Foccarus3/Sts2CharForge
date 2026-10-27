# V0.2.305 — 遗物链上「从牌堆 / 消耗牌堆拿牌」真的生效了（范围界定也带上了）

## 交接待办②：「从牌堆/消耗牌堆拿牌」的范围限定，只修了状态链

先把四条链 × 两种发射器（手填效果 / 锻造图）全摆一遍看生成结果（探针 `sacprobe takepile`）：

| 链 | 生成出来的取牌语句 | 范围界定 |
|---|---|---|
| 卡牌（手填） | ✅ 带谓词 | ✅ |
| 卡牌（锻造图） | ✅ | ✅ |
| **遗物（手填）** | ❌ **整条被丢掉**：「该触发时机不支持…（该触发时机没有 choiceContext…），已忽略」 | ❌ |
| 遗物（锻造图） | ✅ | ✅ |
| 药水 | ✅ | ✅ |
| 自定义状态（手填 / 图） | ✅ | ✅ |

所以不是「范围」没传，是**遗物这一档整条效果根本没生成**。

## 根因：`RelicNeedsChoiceContext` 那份清单漏了几档

遗物的触发时机「打出一张牌时」在本体里是 `BeforeCardPlayed(CardPlay)`，**签名里没有 choiceContext**——
生成端会在需要的时候自己造一个 `HookPlayerChoiceContext`（`BootstrapInCombat`）。要不要造，看
`CSharpCodeGen.RelicNeedsChoiceContext` 那份清单。清单里 **有** `TakeFromDraw` / `TakeFromDiscard`，
偏偏**漏了 `TakeFromExhaust`** → `hasContext = false` → `case "TakeFromExhaust"` 的守卫直接「已忽略」。

顺手把 70 个效果种类 × 2 个触发时机 × 6 种配置（默认 / 负数 / 下回合 / 自己选 / 全部 / 范围）全扫了一遍
（探针 `sacprobe relicctx`，判据是「生成的代码里出现 `choiceContext`、但没先声明」= 真编译 CS0103），
一共抓出 **6 处真问题**：

| 效果 | 以前会怎样 |
|---|---|
| `TakeFromExhaust` | 遗物上整条被静默丢掉（用户报的这条） |
| `ScrapeDraw` / `CheapenDraw` / `PlaceCard` | 同上，被「已忽略」 |
| `TurnEndPlay` / `Outbreak` | 反过来：**没有守卫却写 `choiceContext`** → 生成出来 CS0103，整个模组编不过 |
| 「下回合生效」× 任意种类 | 那一档挂的是 `PowerCmd.Apply<Delayed…>(choiceContext, …)`，也不在清单里 → 战斗胜利 / 战斗结束这种钩子上 CS0103 |
| 格挡填负数（失去格挡）/「下回合生效」的额外资源量 | 分支里写了 `choiceContext` 却没守卫 → 同上 |

## 改法

1. `RelicNeedsChoiceContext` 补齐：`TakeFromExhaust` / `ScrapeDraw` / `CheapenDraw` / `PlaceCard` / `TurnEndPlay` /
   `Outbreak`；`Block` 在「负数（失去格挡）」时也要；**「下回合生效」不分种类一律要**
   （`UsesDelayedEffectBranch(e)` 优先级最高）。
   重放卡牌按档位判（只有「自己选」那条路弹选牌界面，随机 / 牌顶走 Rng，一个字都不写）——
   这样「随机重放」的默认档生成文本逐字不变。
2. 四条没有守卫的分支补上守卫（说清原因，不生成引用不到变量的代码）：`Outbreak`、`TurnEndPlay`、
   重放（自己选）、失去格挡、下回合生效的额外资源量、「下回合生效」那一整档。

## 验收

* 探针 `sacprobe takepile`：**七条链全部**生成
  `CardSelectCmd.FromCombatPile(choiceContext, PileType.Exhaust.GetPile(…), …, c => c.Type == CardType.Skill)`
  （`c => …` 就是范围界定；遗物那一档现在也有）。
* 探针 `sacprobe relicctx`：70 个效果种类 × 2 个时机 × 6 种配置 = 840 种组合，
  **「用到 choiceContext 但没声明」= 0 条**（修之前 6 处）。
* 真编译：一张卡 + 一只遗物 + 一瓶药水 + 一个自定义状态全带「从消耗牌堆拿牌 + 范围界定」→
  `dotnet build` **0 错误**。
* 自检：**4476 PASS / 0 FAIL**（+4 条，其中一条就是上面那张全表扫描）。
* 红线：Sample 113 个文件 / WhiteCloak 412 个文件逐字节相同（只有 `.sln` 的 GUID 不同）。
* 只出更新包（`-NoBundle`）。

## 说明

改的都是「以前会静默丢掉 / 编不过」的配置：正常用着的存档生成文本一个字都没变（逐字节对拍过）。
