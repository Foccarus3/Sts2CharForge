# V0.2.380 — 页面间共享数据：`{页面id:栏位key}`

缺口文档第 12 节（「页面间共享项目数据」）。以前每个页面各填各的，同一个敌人名字 / ID 要在「敌人页」「遭遇页」「动画页」各填一遍，改了容易不同步。现在可以跨页取。

## 写法

```json
// 页面 1
{ "id": "enemy", "title": "敌人", "target": "Enemy.txt",
  "emit": "class {name} {}",
  "fields": [ { "key": "name", "label": "名字", "type": "text" } ] }

// 页面 2
{ "id": "encounter", "title": "遭遇", "target": "Encounter.txt",
  "emit": "spawn {enemy:name} x{count}",
  "fields": [ { "key": "count", "label": "数量", "type": "number" } ] }
```

* `{enemy:name}` = 「敌人页」里 `name` 那一格的值；
* 本页自己的占位符照旧 `{key}`（两者不冲突，正则只吃「id:key」这种带冒号的）；
* 引用不到（页面 id 或栏位 key 不存在）时**原样保留**、不会被静默清空 —— 生成结果里能一眼看出漏了哪个。

## 自检

**4721 PASS / 0 FAIL**（+3 条：`{enemy:name}` 取到 GoblinKing / 本页 `{name}` 不受影响 / 取不到的引用原样保留）。

## 还没做的（缺口文档的下一项）

**插件自定义校验器**（文档第 9 节）：让插件声明自己的校验规则（例如「同一只敌人的动作里 damage 与 block 不能同时为 0」），
现在只有 CharForge 内置的那几条结构校验。这一项需要引入表达式或规则表，等确认形态再做。
