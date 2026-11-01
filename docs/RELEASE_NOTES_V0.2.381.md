# V0.2.381 — 插件自定义校验器（`checks` 规则表）

缺口文档第 9 节。以前插件页只有 CharForge 内置的那几条结构校验（list 配对、target 不能用 list 等），
插件作者没法声明自己的业务规则。现在可以在 `plugin.json` 的页面里写 `checks`。

## 写法

```json
{
  "id": "p1", "title": "招式", "target": "Moves.txt",
  "emit": "{kind}/{dmg}",
  "fields": [
    { "key": "kind", "label": "类型", "type": "pick", "options": ["attack", "block"] },
    { "key": "dmg",  "label": "伤害", "type": "number" }
  ],
  "checks": [
    { "when": "kind=attack", "field": "dmg", "rule": "int", "min": "1",
      "message": "攻击动作必须填至少 1 点伤害" }
  ]
}
```

* `when`：空 = 总是检查；否则同 `visibleWhen` 的写法（页面级标量比较），**成立才检查**；
* `field`：要检查的页面级标量栏位；
* `rule`：`required`（默认）/ `number` / `int`；
* `min` / `max`：数值上下限（可选）；
* `message`：空 = 用内置措辞；**写了就优先用插件自己那句**（连类型错误也用这句）。

命中的问题会进页面「这页有配置问题，生成时会跳过」那条提示里 —— 和内置校验同一个出口。

## 一个刻意的取舍

`required` 类规则**只在这一页已经存过值之后才报**（`HasAnyPageValue`）——
否则新建的空页一打开就一片红，作者会被自己的规则挡住。

## 自检

**4724 PASS / 0 FAIL**（+3 条：`kind=attack` + `dmg=0` 命中「至少 1 点伤害」/ `kind=block` 时不命中 / 非数字会按 `int` 规则报出来（自定义 message 优先））。

## 已知限制（如实说）

* 只支持**页面级标量栏位**；list 行内的子栏位校验（「某个动作的数值不能为 0」）还没做；
* `when` 只支持单栏位比较，不支持 `&&` / `||`；
* 规则表形态（不做脚本表达式）——这是刻意的，避免插件里跑任意代码。
