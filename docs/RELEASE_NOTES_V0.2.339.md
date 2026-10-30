# V0.2.339 — 插件的页能挂到生成器页签上了；`插件AI提示词.md` 重写成分类长文档

把 V0.2.338 留下的两件事做完（那版只立了 `pages[].tab` 这个字段，界面还没接）。

## ① `pages[].tab`：扩展页挂到对应生成器页签（界面部分）

```jsonc
{ "id": "cardnote", "title": "卡牌备注", "tab": "card", "markdown": "…" }
```

`tab` 认得 6 个值：`card`（卡牌）/ `relic`（遗物）/ `potion`（药水）/ `power`（自定义状态）/
`forge`（锻造）/ `build`（构建·日志）；不填 = 老行为（挂「插件」页）。

实现上刻意绕开了「逐页改 XAML」：**只有真的有扩展页时**，才把那个页签原来的内容包进一个两行 Grid
（原内容占满第一行，第二行是一条**默认收起**的 `插件扩展页（N）— 页1、页2` 折叠条，点开是一排页签）。
于是：

* 没装插件的人：6 个生成器页签**连 DOM 都不动**（连包都不包）；
* 插件删掉 / 重新加载：只换折叠条里的页，收起即可，原内容一直是同一个对象（自检钉住这点）；
* 挂走的页**不再**在「插件」页重复画一份（两个控件编辑同一份值、预览会各说各话），
  「插件」页的插件详情里改成列一行「〈标题〉→ 已挂到「卡牌」页签里的「插件扩展页」」；
* `tab` 写错（例如 `cards`）不算错误：那一页留在「插件」页，并提示「可用：card / relic / potion / power / forge / build」。

代码：`PluginRegistry.KnownTabs` / `IsKnownTab` / `ExtensionPagesOf`，
`MainWindow.RebuildPluginExtensionPages`（在 `LoadPlugins` 之后调）/ `AttachPluginExtHost` / `FillPluginExtHost`。

## ② `docs\插件AI提示词.md` 重写成分类长文档

按 14 个小节重排，都是**可以照着写**的东西：

| 节 | 内容 |
|---|---|
| 0~2 | 先读哪三样、文件位置、五种扩展点 |
| 3 | 最小可用清单（可直接抄的 JSON） |
| 4 | `emit` 的硬规则（塞进方法体、不写 using/class…） |
| 5 | **逐链占位符总表**（卡牌 / 遗物 / 药水 / 状态 / 锻造图 × 14 个占位符） |
| 6 | 数值 / 公式 / 对象 / 上下文 |
| 7 | `{transformSelf:类名}` 宏（把「变化卡牌」验证过的写法展开） |
| 8 | 触发时机（33 个 hook 的 id + 显示名 + **签名里 guard 能用的变量**）/ 条件 / 锻造模块 |
| 9 | `pages[]` 三种形态（说明页 / 表单页 / 挂到生成器页签） |
| 10 | 打包规则（`csFiles` / `assetFiles` / 越界禁止） |
| 11 | **报错对照**（编辑器里会出现的原文 → 原因 → 怎么改）+ 生成后编译不过的三种常见症状 |
| 12~13 | 交付前自检清单 / 交付内容 |

同时把 `插件开发规范.md` 对齐：占位符表补全（`{amountFormula}` / `{combatState}` / `{cardPlay}` / `{state}` /
`{spawnCardType}` / `{target}`）、新增 3.1 节「`{transformSelf}` 宏」、新增第 9 节「把页面挂到生成器页签」。

## 顺带修的三处（写文档时对代码核出来的）

1. **插件效果以前静默忽略「数值 = X」**：卡牌页的「数值公式」栏对插件效果只认光秃秃的 `X`
   （= 原来的「数值 = X」），可生成端传的是 `Lit.Dec(e.Amount)`（数值框里那一格 = 0）
   → 选了「= X」插件效果就拿到 `0m`。现在卡牌链传算好的 `amt`，`{amount}` 就是那个 `x` 变量。
2. **`{combatState}` 在状态链上编不过**：V0.2.338 的默认值是 `base.Owner.Creature.CombatState`，
   而自定义状态类的 `base.Owner` **本身就是 Creature** → CS1061。状态链现在显式传 `base.Owner.CombatState`。
3. **`{transformSelf:类名}` 在遗物 / 药水链上编不过**：那个宏以前靠「`{cardSelf}` 是不是 `null`」判断
   这条链有没有「这张牌」，可遗物 / 药水的 `{cardSelf}` 是 `this`（遗物 / 药水对象）→ 展开出 `this.Pile` = CS1061。
   现在 `PluginCtx` 多一个 `CardSelfIsCard`（只有卡牌链、以及卡牌上的锻造图为 true），
   宏按它决定要不要写「顺手消耗这张牌」那一句。

## 示例插件

`plugins\示例插件\plugin.json` 顺手整理：

* 删掉**重复的 `pages` 键**（老文件里写了两次 `pages`，后者覆盖前者 —— 说明页「怎么写自己的插件」其实一直没生效）；
* 新增「卡牌页扩展页（示例）」一页（`"tab": "card"` + `fields` + `target` + `emit`），把三种页面形态都演示一遍。

## 验收

* 开发版自检：**4570 PASS / 0 FAIL**（+17 条 V0.2.339 断言：规则表 / 挑页 / 挂载 / 收起 / 不重复画 /
  写错值兜底 / 卸载后重建 / 示例插件 / `{combatState}` 换得对 / 「数值 = X」拿得到 X / `{transformSelf}` 两种链）。
* 只出更新包（`-NoBundle`）。
