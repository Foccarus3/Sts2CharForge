# V0.2.357 — 插件页：一页多文件 / 按行产出 / 条件宏

接着上一轮的 B 组，把「一页只能产出一个文件 + 模板没有条件」这两条补上了。

## 1. `outputs[]`：一页写多个文件

```json
"outputs": [
  { "target": "cs/Plugins/{id}/Mobs/{Mob}.cs",     "emit": "public static class {Mob} { }\n" },
  { "target": "cs/Plugins/{id}/Mobs/{Mob}Info.cs", "emit": "// 说明\n" }
]
```

写了 `outputs` 就以它为准（老的 `target` / `emit` 被忽略，老插件一个字都不用改）。

## 2. `forEach`：按 list 每一行各出一个文件

```json
{ "target": "cs/Plugins/{id}/Moves/{Prefix}{name}.cs", "forEach": "moves",
  "emit": "public static class {Prefix}{name} { public const int Damage = {dmg}; }\n" }
```

* `target` / `emit` 里都能用该行的**子栏位**，以及 `{index}`（1 起）/ `{index0}`（0 起）；
* 空列表 = 一个文件都不生成（不算错）；
* `forEach` 写成不存在的字段 → 报「页面问题」，**不会**静默生成 0 个文件。

## 3. 条件宏

| 写法 | 含义 |
|---|---|
| `{#字段}…{/字段}` | 有值才渲染 |
| `{^字段}…{/^字段}` | 为空才渲染（写「没填就用默认值」的兜底） |

「有值」= 非空、且不是 `0` / `false` / `否`；**行内子栏位也能当条件**。

## 4. 生成与预览

* `PluginGen.WritePageTo` 统一负责写文件，**生成工程**和界面「生成到当前工程」按钮走同一份（以前按钮只写一个文件）；
* 预览按文件分段显示，每段前一行 `// ---- 文件：… ----`（按行的还会写「按『字段』第 N 行」）。

## 自检

**4661 PASS / 0 FAIL**（+7 条：合法 outputs 页不报问题 / forEach 按行出两个文件且文件名用行内子栏位 /
`{#}` 没填时不渲染而 `{^}` 兜底出现 / 填了之后反过来 / `WritePageTo` 真写进工程 /
`forEach` 指向不存在字段会报 / 预览按文件分段）。

顺手修了一个自检当场抓到的问题：`PluginPageSpec.Produces` 以前只认老的 `target`+`emit`，
写了 `outputs` 的页会被当成「不产出」而**根本不写**（也就是新功能完全不生效）。

## 还没做的（B 组剩下）

页面值仍按插件全局存（不跟存档走）、插件效果拿不到动态变量 / 悬浮提示 / 本地化、
触发时机只能挑本体钩子、没有启用开关 / apiVersion 迁移。
