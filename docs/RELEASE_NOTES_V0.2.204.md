# V0.2.204 —— 范围界定里补上「任务牌」：穷举「非攻击牌」时任务牌再也不会漏

> 用户原话：「当前消耗指定牌的范围界定中不包含任务牌，使得穷举达成『消耗非攻击牌』一类效果时，任务牌无法被消耗。」

## 1. 本体一共 7 档卡牌类型，我们只摆出 6 档

本体 `CardType`（`src/Core/Entities/Cards/CardType.cs`）：

```
None / Attack / Skill / Power / Status / Curse / Quest
```

而「范围限定 / 范围界定」那张下拉只有：不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 —— **少了 Quest（任务牌）**。
所以「除攻击以外全都算」这种穷举永远覆盖不到任务牌：消耗 / 丢弃 / 升级 / 变化 / 生成 …… 全部漏掉它。

## 2. 现在补上

| 位置 | 变化 |
| --- | --- |
| `EffectCatalog.SpawnFilters` | 新增 `("Quest", "任务牌（本体 CardType.Quest 那一类）", "任务")` —— 界面下拉 / 中文名 / 列表行尾全部自动带上 |
| `CSharpCodeGen.SpawnPoolCond` | `"Quest" => "c.Type == CardType.Quest"`（范围界定 / 刮削条件共用的判据） |
| `CSharpCodeGen.SpawnPoolExpr` | 生成 / 变化卡的候选池：`c => c.Type == CardType.Quest` |
| `CSharpCodeGen.ScopeCondForFilter` | 同上（范围界定 + 刮削条件走的就是这一段） |
| `CSharpCodeGen.CardFilterCond` | 状态 / 遗物的「只对哪类牌生效」也认任务牌 |
| `PowerTriggerCatalog.CardFilterZh` | 「任务」短名（列表 / 说明 / 回读） |
| `EffectCatalog.IsKnownScrapeCond` | 不再手写死类型清单（**直接认内置范围限定里除「不限」以外的 Id**）—— 以前这里漏了任务牌，导致界面上能选、校验器却说「认不出来」 |
| 刮削条件候选 | 本来就是「非 0 费 + 不限 + 范围限定整份」，自动多出「任务」 |

> 教训记一笔：这套「卡牌类型 → 生成期判断」以前在 4 处各写了一遍（`SpawnPoolCond` / `SpawnPoolExpr` /
> `ScopeCondForFilter` / `CardFilterCond`），漏一处就静默失效。这次除了补 Quest，还把
> `IsKnownScrapeCond` 改成从同一份清单推导 —— 以后再加类型只改 `SpawnFilters` 一处。

## 3. 现在怎么配「消耗非攻击牌」

「消耗指定牌」的效果里，要穷举就摆这几条（每档一条，作用和目标都照旧）：

技能 / 能力 / 诅咒 / 状态 / **任务** —— 五档合起来才等于「除攻击以外的全部牌」。
以前少的那条「任务」现在在「范围界定」下拉的最后一项。

## 4. 验收

- 探针（`sacprobe gen`）：一张卡上同时放「消耗 / 丢弃 + 范围界定 = 任务牌」、「生成 + 范围限定 = 任务牌」、
  「刮削 + 条件 = 任务牌」→ 生成的三处判据全是 `c.Type == CardType.Quest`，生成工程 `dotnet build` **0 error**。
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（老存档没用到 Quest，文本一字未变）。
- `--uicheck`：**3969 PASS / 0 FAIL**（新增 7 条：下拉里有任务牌、刮削条件里有、四路判据都生成 `CardType.Quest`、
  「除攻击以外」= 技能 / 能力 / 诅咒 / 状态 / 任务）。

## 5. 版本

- `V0.2.204`
