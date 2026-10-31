# V0.2.359 — 插件效果能出现在「卡面文字」和「悬停说明」里了

以前插件效果是**看不见**的：卡面上什么都不写（只能自己在「自定义描述」里手抄一遍），悬停也没有任何说明。
这一版给 `effects[]` 加了两个可选字段：`text` 和 `hoverTip`。

## `text`：卡面 / 遗物 / 药水 / 状态描述里那句话

```json
{ "kind": "PluginHealAndBlock", "text": "回复 {amount} 点生命并获得 {amount} 点格挡。" }
```

* 占位符**只认 `{amount}`**：换成这条效果的数值；「数值 = X」时写 **X**；填了「数值公式」时写**公式文本**；
* 写了别的 `{…}` → 加载时**报问题**（否则会原样印在卡面上，那种错最难查）；
* 留空 = 和以前完全一样（不产生文字）。

## `hoverTip`：悬停说明

```json
{ "hoverTip": "MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<...DexterityPower>()" }
```

原样写进那件东西的 `ExtraHoverTips` 列表。写错是**编译期报错**（不会静默失效）。

## ⚠ 一个如实说明

插件效果**还不会声明动态变量**，所以 `text` 里是**字面数字**（不会跟着力量 / 敏捷实时变）。
想要「实时数字」目前只有两条路：让玩家看 `hoverTip`，或者你在「卡牌 → 自定义描述」里手写。
真正的「动态变量」需要把插件效果接进 `CanonicalVars` 生成，属于下一档（已记在待办里）。

## 自检

**4667 PASS / 0 FAIL**（+3 条：text 进卡面自动描述且 `{amount}` 被替换 / hoverTip 进 ExtraHoverTips /
「数值 = X」时卡面写 X 而不是 0）。示例插件 `PluginHealAndBlock` 已带上这两个字段，可直接当模板。

文档：`插件开发规范.md` 第 16 节、`插件AI提示词.md` 第 20 节。
