# V0.2.340 — 插件能给生成器加**一级功能页**了（`pages[].newTab`）

你要的那个东西：**装上「怪物生成器」插件，生成器顶部就多一个「怪物生成器」页**。
V0.2.339 只能做到「钻进已有页签的底部」，这一版补上了「插件自己开一个页」。

## 用法

```jsonc
"pages": [
  {
    "id": "monsters",
    "title": "怪物生成器",     // 顶部一级页签的标题
    "newTab": true,            // ← 就这一行
    "markdown": "填好东西点下面的按钮，会生成到工程里。",
    "fields": [
      { "key": "MobClass", "label": "怪物类名（英文）", "type": "text",   "default": "MyMob" },
      { "key": "MobHp",    "label": "血量",            "type": "number", "default": "40" }
    ],
    "target": "cs/Plugins/{id}/Mobs/{MobClass}.cs",
    "emit": "public static class {MobClass} { public const int Hp = {MobHp}; }"
  }
]
```

* 页签固定插在「构建 / 日志」**前面**（不动现有页的相对顺序）；多个插件都有功能页时按插件加载顺序 + 页顺序排。
* 页里的内容就是原来那套「自定义页」：说明 / 表单（text / number / bool / pick / multiline）/ 实时预览 /
  「保存这些值」/「生成到当前工程」；生成工程时也会自动按 `target` + `emit` 写成文件 → 随 mod 编译 / 打包。
* 两个插件给同名页 → 页签标题自动带插件名（`重名页（我的插件）`），不会出现两个一模一样的页签。
* `tab` 和 `newTab` 一起填 → 按 `newTab` 算（`tab` 忽略），「插件」页的详情里写一行说明。
* 插件删掉 / 重新加载 → 页签跟着消失 / 重建；如果当时正停在这个页签上，退回第一个页签。

## 边界（和上一版说的一样，没有藏）

* **拿不到编辑器数据**：功能页只有自己那一份 `values.json`，读不到当前存档的角色 / 卡牌 / 遗物，
  也拿不到本体名录。所以做得到的是「**填表 → 按模板出文件**」，做不到「点开某张卡、右边多一栏」。
* **一份值对应一页**：不是「每个怪物一份」；要多个条目就在模板里按行拆，或者开多个页。
* 插件仍然不能执行编辑器里的代码（不加载 DLL、不反射编辑器类型）—— 页面长什么样只能由清单描述。

## 代码

* `PluginRegistry`：`PluginPageSpec.NewTab` + `PluginRegistry.NewTabPages`（`AllPages` 里 `NewTab` 的那些）。
* `MainWindow`：`RebuildPluginTopLevelTabs()`（在 `LoadPlugins()` 里跟 `RebuildPluginExtensionPages()` 一起调）——
  先把上次加的整批撤掉，再按清单往 `MainTabs` 里「构建 / 日志」前面插；被撤掉的页签若是选中项就退回第一个。
  `IsShownOutsidePluginPage()` 统一判断「这页已经在别处显示」，于是「插件」页不会重复画一份。
* XAML 不用改（三个页签的 `x:Name` 是 V0.2.339 加的，动态页签只靠 `MainTabs.Items.Insert`）。

## 示例插件

`示例插件` 多了一页「示例功能页」（`"newTab": true`，纯说明页、不产出文件），装上就能看到效果；
`readme` 里也写清楚了三种页 + 功能页的区别。

## 验收

* 开发版自检：**4577 PASS / 0 FAIL**；部署版 4576 PASS / 0 FAIL（+7 条 V0.2.340 断言：`NewTabPages` 挑页 /
  页签真的出现且紧贴「构建 / 日志」前面 / 内容是真表单 / 同名页带插件名 / 不在「插件」页重复画 /
  产物照旧写进工程 / 撤插件后消失）。
* 只出更新包（`-NoBundle`）。
