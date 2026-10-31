# V0.2.366 — 修「引用了没有声明的动态变量 Value」（别人存档的插件效果）

你截图里那条生成拦截：

```
生成出来的模型里引用了**没有声明**的动态变量 …：ChixiaoTianwei → Value；FormsofHell → Value
```

用你给的 `Swire.json` 复现了：这两张卡都用了插件效果 `PluginPercentDamageRandomCurrentHp`，
而那个插件在 `plugin.json` 里**没写 `var`**。

## 为什么会这样

V0.2.360 我加了「插件效果可以声明动态变量」：写了 `var` 就真的声明 `new DynamicVar(...)`，
卡面用 `{X:diff()}`、emit 里用 `base.DynamicVars["X"]` —— 这条是对的。

但**没写 `var`** 的那种：生成端确实**不声明**变量（`HasNoDynamicVar` 那条规则），
可是数值表达式还走的是 `VarAccess`，于是卡牌类里出现了：

```csharp
await CreatureCmd.Heal(base.Owner.Creature, base.DynamicVars["Value"].BaseValue);   // ← 没有 "Value" 这个键
```

游戏里一取就 **KeyNotFoundException**（牌堆界面打不开 / 卡面显示不出来）。校验器现在会拦（你看到的正是它），
但**拦不是修**——这一版把根因修了。

## 改法

新增 `CSharpCodeGen.PluginAmountExpr(e, amt)`：

* 插件效果**没写 `var`** → 数值退成**字面量**（`6m` 这种）；填了「数值公式」或用 X 的照旧走表达式；
* 写了 `var` → 和以前一样用 `base.DynamicVars["<名字>"]`（V0.2.360 的行为一点没变）。

四个传数值的地方（卡牌 / 遗物 / 药水 / 自定义状态）都换成了它。

## 验证

用你的存档再生成：

```csharp
// 修之前
await CreatureCmd.Heal(base.Owner.Creature, base.DynamicVars["Value"].BaseValue);
// 修之后
await CreatureCmd.Heal(base.Owner.Creature, 6m);
```

* 这个存档跑 `validate`：**不再报「引用了没有声明的动态变量」**（只剩几条无害的「同名变量自动起别名」提示）；
* 自检 **4692 PASS / 0 FAIL**（+3 条：没写 var 时不引用 `Value` / 数值是字面量 / 写了 var 的仍走 `DynamicVars["Heal"]`）。

## 给插件作者的一句话

想让卡面数字跟着力量 / 敏捷 / 升级实时变 → 在 `plugin.json` 的 effect 里加 `"var": "某个名字"`；
不加也行，那就用字面数值（这一版起不会再把工程卡死）。
