# V0.2.368 — 插件两个扩展点：锻造模块的图栏位、公式变量

按你指出的两条做的，两条都做成**可选**：不写就完全是老行为。

## ① `forgeModules[].fields[]` —— 插件锻造模块能带自己的参数了

以前插件模块在图上只有【数值】一栏，做不出「变化卡牌」那种带参数（目标卡 / 取牌方式 / 范围…）的节点。

```json
{
  "kind": "PluginForgeHeal",
  "fields": [
    { "key": "HealTimes", "label": "重复几次", "type": "number", "default": "1",
      "hint": "emit 里写 {HealTimes} 就会换成这里填的数" }
  ],
  "emit": "for (int i = 0; i < {HealTimes}; i++) await CreatureCmd.Heal({self}, {amount});"
}
```

* `type`：`text` / `number` / `formula` / `pick` / `check`（`pick` 支持 `"值|显示"`）；
* 节点上没填 → 用 `default`；声明了几个栏位，【效果】模块上就多几行（标签带「插件：」）；
* 实现上是复用了现成的 `ForgeNodeField`（你说的那个方案）：`ForgeGraphCatalog.All` 每次现算，
  把插件栏位拼进【效果】模块；生成端新增 `PluginGen.TryEmitNode(...)`，把 emit 里的 `{字段key}` 换成节点值；
* **重名会拒绝加载**：`key` 不能和【效果】模块内置栏位重名（`Amount` / `Pick` / `Times`… 会让一个节点出现两行绑同一个键）。

## ② `formulaVariables[]` —— 插件能给「数值公式」加变量

```json
"formulaVariables": [
  { "id": "PluginDiscardAttackCount", "display": "弃牌堆里的攻击牌数",
    "csharp": "(decimal)({p}?.PlayerCombatState?.DiscardPile?.Cards.Count(c => c.Type == CardType.Attack) ?? 0)" }
]
```

* `{p}` = 玩家、`{c}` = 自己那个 Creature；别的 `{…}` 拒绝加载；
* 和内置变量同一张表（重名时内置优先），生成端的 `{p}/{c}` 替换也走同一套；
* `FormulaBuiltins.All` 从静态字段改成「内置 + 现算插件」—— 插件是启动时加载的，静态字段会漏掉（和 V0.2.367 同一个坑）。

**顺带把内置变量补全了**：抽牌堆 / 弃牌堆 / 消耗牌堆 × 攻击/技能/能力/状态/诅咒，共 15 条
（`DrawAttackCount`、`DiscardSkillCount`、`ExhaustCurseCount` …）。「按弃牌堆里的攻击牌数造成伤害」现在不写插件也能直接选。

## 自检

**4700 PASS / 0 FAIL**（+5 条：插件 fields 查得到 / 【效果】模块栏位里出现它们 /
生成时 `{HealTimes}` 换成节点上填的 3 / 插件公式变量进了变量表 / 内置三摞×五种牌型都在）。

一个坑记一下：示例插件一开始用了 `Times` 当字段名，和【效果】模块内置的 `Times` 撞了 ——
自检输出的栏位列表里能看到两个 `Times`，于是把重名检查改成**从静态表现推**（不再手抄一张键名单），示例也改名 `HealTimes`。
