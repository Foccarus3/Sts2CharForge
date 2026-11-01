# V0.2.383 — 条件组合：`&&` / `||` / `!`

`visibleWhen` 和 `checks[].when` 共用同一套条件求值（`PluginRegistry.EvalCondition`），这一版把它从「单个比较」扩成小表达式。

## 现在能写

```json
"visibleWhen": "kind=attack && dmg=0"
"visibleWhen": "kind=block || kind=attack"
"visibleWhen": "!kind=block"
"checks": [ { "when": "kind=attack && dmg=0", "field": "note", "rule": "required",
              "message": "攻击且伤害为 0 时必须写附注" } ]
```

* 优先级和 C# 一致：`!` > `&&` > `||`；
* 原子照旧：`key`（真值：有值/非 0/非 false/非「否」）、`key=值`、`key!=值`，忽略大小写；
* 不做括号嵌套 —— 需要更复杂的组合时，用**多条 checks 规则**表达更清楚（也是刻意的：不做脚本）。

## 影响范围

* `visibleWhen`：界面隐藏 + 生成时按空值（V0.2.378/379 的既有行为，判据换成了这套）；
* `checks[].when`：页面级和行内（`list`）校验都用它；
* 老写法（单个 `key` / `key=值`）**完全等价**，老插件不受影响。

## 自检

**4728 PASS / 0 FAIL**（+2 条：`&&` / `||` / `!=` / `!` / 真值 五种组合的求值结果；`visibleWhen: "kind=attack && dmg=0"`
在 dmg=0 时显示「附注」、dmg=5 时不显示）。
