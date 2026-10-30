# V0.2.338 — 插件模板上下文扩全（对象 / 战斗状态 / 出牌 / 状态 / 变化卡牌宏 / 扩展页字段）

接着 V0.2.337（插件效果能落在状态链上）继续把「插件太受限」那条补齐。

## 新增占位符

| 占位符 | 换成什么 | 备注 |
|---|---|---|
| `{target}` / `{targetOrFirst}` | 当前「指向对象」表达式 | V0.2.337 加的，和 `{foe}` 同源 |
| `{combatState}` | `CombatState` 表达式 | 状态链默认 `base.Owner.Creature.CombatState` |
| `{cardPlay}` | 本次出牌的 `CardPlay` 变量 | 只有卡牌链真的有；别的链用它会编不过，模板里按链判断 |
| `{state}` | 这个状态实例（`this`） | 状态链专属 |
| `{spawnCardType}` | 「目标卡」的 `ModelDb.Card<…>()` 表达式 | 写「变化成某张牌」不用自己拼 |
| `{amountFormula}` | 这条效果的数值（含数值公式 / = 本状态层数） | 和 `{amount}` 同源，语义更明确 |

## 新增宏：`{transformSelf:类名}`

「变化这张牌」是本工具踩过坑的动作（打出的牌躺在 Play 摞里，直接 `CardCmd.Transform` 会把新牌塞回去 → 看不见）。
所以插件可以写：

```jsonc
"emit": "{transformSelf:CarmenCrimsonScarAlteration}"
```

展开成验证过的写法（1 张换 1 张）：

```csharp
CardModel __plugMade1 = base.CombatState.CreateCard(ModelDb.Card<CarmenCrimsonScarAlteration>(), base.Owner);
await CardPileCmd.Add(__plugMade1, PileType.Discard);
if (this.Pile is not null) await CardCmd.Exhaust(choiceContext, this);
```

没有「这张牌」的链（状态 / 遗物 / 药水）只做前半句（新牌进弃牌堆）。

## 新增清单字段：`pages[].tab`（扩展生成器页的**数据**部分）

```jsonc
{ "id": "my-card-extra", "title": "我的卡牌补充", "tab": "card", "markdown": "…" }
```

`tab` 空 = 挂在「插件」页（旧行为）；填 `card` / `relic` / `potion` / `power` / `forge` / `build`
= 声明这一页是**那个生成器页的扩展页**。

> 如实说明：**这一版只做了数据字段（清单能读、能存、文档写了），界面挂载还没接** ——
> 页签路由要改「插件」页那套构建逻辑，我放在下一版单独做，避免和这版的占位符改动混在一起回归。

## 验收

* 自检：**4552 PASS / 0 FAIL**。
* 只出更新包（`-NoBundle`）。

## 还没做（下一版）

1. `pages[].tab` 的**界面挂载**（把扩展页塞进对应生成器页签）；
2. `插件AI提示词.md` 重写成分类长文档（逐链变量表 / 公式 / 对象 / 变化卡牌 / 打包 / 报错对照 / 完整示例）。
