# V0.2.384 — 插件页新布局：`master-detail`（左侧行列表 + 右侧选中行详情）

缺口文档第 5 节（「左侧列表 + 右侧选中对象详情」）。这是老 EnemyForge 的核心交互，现在插件页也能用。

## 怎么用

```json
{
  "id": "moves", "title": "招式", "target": "Moves.txt",
  "layout": "master-detail",
  "masterList": "moves",
  "emit": "{moves}{name}/{dmg}{/moves}",
  "fields": [
    { "key": "moves", "label": "招式", "type": "list",
      "fields": [
        { "key": "name", "label": "名字", "type": "text" },
        { "key": "dmg",  "label": "伤害", "type": "number" }
      ] }
  ]
}
```

* `layout: "master-detail"` —— 这页换成主从布局；
* `masterList` —— 左边用哪个 list 栏位（省略 = 取这页第一个 list 栏位）；
* 左边：行列表（每行显示「1. 横扫」这种，取该行第一个非空标量值），下面是 `+ 添加一行` / `✕ 删除` / `↑` / `↓`；
* 右边：选中那一行的子栏位编辑器（子列表还是走递归子表格；`visibleWhen` 在行内照常生效）；
* 每次改动立刻 `SaveRows` 落盘并刷新 —— **不会再出现「改了没保存」**（老的列表编辑器也有这条）。

## 兼容性

* 不写 `layout` 的页面**逐像素不变**（老布局）；
* `masterList` 指向的栏位不存在 / 不是 list 时，退回普通表单布局（不会白屏）；
* 数据仍是 `rows.json` 里的活列表，没有新格式。

## 自检

**4730 PASS / 0 FAIL**（+2 条：左边确实是 ListBox 且列出「1. 横扫 / 2. 重击」/ 右边显示选中行的字段标签）。

## 还没做的

* 左侧列表**没有搜索框 / 分组**（行多了要靠滚动）；
* 右侧只支持子栏位编辑，不支持「复制这一行」；
* 自定义控件 / 按钮事件、资源预览仍属「编辑器扩展插件」级，另立项。
