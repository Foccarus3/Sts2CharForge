# V0.3.86 — 插件效果也能写数值公式；插件触发时机不再被误拦

## 1. 插件效果支持「数值公式」

以前编辑器只给 5 种内置效果（伤害 / 格挡 / 失去生命 / 回复 / 施加状态）显示公式栏，插件效果被当成「不支持」收起来。

但生成端 `PluginGen` 的 `{amount}` 本来就支持 `AmountOverride`，可以吃公式。

修复：

```csharp
FormulaSupports(kind) =>
    PluginRegistry.EffectOf(kind) is not null
    || kind is "Damage" or "Block" or "HpLoss" or "Heal" or "ApplyPower" or "Gold" or "VarWrite";
```

效果：

- 插件效果现在显示「数值公式」；
- 写 `M*2` / `Turn*3` 等会真正传进插件模板的 `{amount}`；
- `X` 仍然可用。

## 2. 插件触发时机不再被误报「没有 choiceContext」

以前 `RelicCtxFrom` 对插件时机直接返回 `None`，于是「插件：休息时（火堆回血）」上放伤害 / 施加状态会被校验器拦住。

现在：

- 插件签名里有 `PlayerChoiceContext` → `Param`；
- 插件声明 `HasChoiceContext` → 局外时机用 `BlockingPlayerChoiceContext`；
- 插件明确没有 context → 仍然 `None`，校验器继续拦真正做不了的效果。

## 注意

- 「插件效果公式」只对**真的使用 `{amount}`** 的插件模板有意义；模板不读 amount 的效果，填了也不会改变行为。
- 插件触发时机若明确声明没有 choiceContext，选需要上下文的效果仍会被拦，这是预期行为。

## 验证

| 项 | 结果 |
|---|---|
| 插件效果显示公式栏 | 通过 |
| 插件 `{amount}` 吃公式 | 通过 |
| 插件时机有 context → 不再误拦 | 生成端已接 |
| 开发版 `--uicheck` | **4931 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4930 PASS / 0 FAIL** |
