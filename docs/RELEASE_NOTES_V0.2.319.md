# V0.2.319 — 插件自定义页框架：「事件页」这类编辑器不认识的内容，插件自己定义

你要的「先加一页事件页的框架，内容完全看插件实现」做好了。做法是**声明式表单 + 模板产物**：

## 插件声明（`plugin.json` 的 `pages`）

```jsonc
"pages": [
  {
    "id": "events", "title": "事件",
    "markdown": "这页干什么（可空）",
    "fields": [
      { "key": "EventClass", "label": "事件类名", "type": "text",   "default": "MyEvent" },
      { "key": "EventName",  "label": "事件名",   "type": "text",   "default": "我的事件" },
      { "key": "Weight",     "label": "权重",     "type": "number", "default": "1" },
      { "key": "Repeatable", "label": "可重复",   "type": "bool",   "default": "0" },
      { "key": "Kind",       "label": "类型",     "type": "pick",   "options": ["普通","战斗","诅咒"], "default": "普通" }
    ],
    "target": "cs/Plugins/{id}/Events/{EventClass}.cs",
    "emit": "public static class {EventClass} { public const int Weight = {Weight}; }"
  }
]
```

## 编辑器提供了什么（框架）

* 「插件」页右边现在是**页签区**：第 1 页「信息」，后面是插件自己声明的每一页；
* 带 `fields` 的页渲染成**表单**（text / number / bool / pick / multiline），下面有**实时预览**；
* 按钮：「保存这些值」（写 `plugins/<id>/values.json`）、「生成到当前工程」（立即写文件）；
* 点「生成工程」时，所有带 `fields` + `target` + `emit` 的页会被渲染成文件写进工程 ——
  `cs/...` 下的 `.cs` 会编进模组 dll，于是**随 mod 一起打包**。

## 编辑器不需要懂「事件」

这条扩展点的意义就是这个：像事件、遭遇、场景这种**生成器本来不认识**的内容，
插件可以把「页面（表单）」和「产物（代码文件）」都定义好，编辑器只负责画控件 / 存值 / 写文件。
以后要给生成器加「事件」这种一等公民，那才需要改生成器本体（另说）。

## 验收

* 自检：**4535 PASS / 0 FAIL**（+4 条 V0.2.319 断言：目标路径占位符 / emit 渲染 / 生成时写文件 / 值能存回 values.json）。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）——
  没有插件页时生成结果一个字节都不变。
* 只出更新包（`-NoBundle`）。

## 示例

`plugins\示例插件` 多了一页「事件（示例）」：填类名 / 名称 / 权重 / 可重复 / 类型，
生成工程时会产出 `cs\Plugins\example\Events\<类名>.cs`。文档也更新了：
`程序目录\docs\插件开发规范.md` 第 8 节「自定义页框架」+ `docs\插件AI提示词.md`。

## 边界

* 页面**不产出本地化表 / 不注册内容**（那些要改生成器本体）；
* 插件页面里填的值只存本地（不跟着存档走）——想每个存档一套，得再做成进存档。
