# V0.2.276 — 自定义描述里能填「能量图标」

## 你要的

> 能不能再自定义描述增加填入能量图标的公式

能。卡牌 / 遗物 / 药水的**自定义描述**框里，点 **「填入参数…」** 现在多一条：

```
{Energy:energyIcons()} — 能量图标（本体按你角色的能量颜色画一个）
```

## 为什么是这个写法

本体卡面文案就是这么写的（我在本体 `localization/eng/cards.json` 里核过）：

```
ADRENALINE.description : "Gain {Energy:energyIcons()}.\nDraw 2 cards."
ALIGNMENT.description  : "Gain {Energy:energyIcons()}."
```

* `energyIcons` 是本体自带的 SmartFormat 格式器（`EnergyIconsFormatter`），
  它会按**你角色卡池的能量颜色**画图标 —— 所以这里只给写法，不写死任何图片路径（换成别的角色也不串色）；
* 自定义描述是**原样**写进 `.description` 的，所以这段花括号会被本体求值成图标，不会被转义。

## 顺带说明（校验器）

遗物 / 药水 / 状态的描述变量集和卡牌不一样，`{Energy:energyIcons()}` 取不到值时会**原样印出花括号**。
所以校验器加了一条提示：**遗物的自定义描述里用了它** → 提醒你「悬停时如果看到花括号，就改成普通文字」。
卡牌那边不打扰（卡面一定有 Energy 变量）。

## 验收

* 自检：**4438 PASS / 0 FAIL**（新增 2 条：「填入参数…」菜单里确实多这一条 / Core 的变量清单不混入它，
  免得校验器把卡面占位符比对弄乱）。
* 菜单里其余候选（`{Damage:diff()}` / `{Block:diff()}` …）一个字没动。
