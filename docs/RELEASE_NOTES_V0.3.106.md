# V0.3.106 — 插件条件支持运行时公式数值

## 现象

锻造图【条件】选插件条件 + 数值 N 填公式：

```text
95 - (U + V) * 5
```

生成：

```csharp
decimal __fgNAmount... = (95m - ((U+V)*5m));
// △ 条件种类还不支持运行时判断
while (false)
```

公式被提成局部变量，但**没有任何地方引用**；外层条件写死 `false`。

## 根因

`ForgeGraphGen.RuntimeCondition` 只认 7 个内置条件：

```text
HandAtLeast / HandAtMost / PlayedAtLeast / ExtraResourceAtLeast /
HpBelowPercent / HpInRange / HasPowerAtLeast
```

插件条件落进 `_ => null`，`BoolVar` 拿到 null 就按 `"false"` 生成。

## 修复

`RuntimeCondition` 增加插件分支：

- 按条件 id 取 `PluginRegistry.ConditionOf(kind)`；
- 把 `{amount}` 换成已经声明好的运行时局部变量 `nExpr`；
- `{amount2}` 换成 `mExpr`；
- `{self}` / `{player}` / `{foe}` / `{combatState}` / `{power}` 按当前链展开；
- 返回真实布尔表达式，不再 `false`。

生成结果：

```csharp
decimal __fgNAmount... = __X;   // 公式：X
if ((((__fgNAmount...)) > 0))
{
    ...
}
```

## 说明

插件条件要支持公式，`Expr` 里用 `{amount}` 即可：

```json
{
  "id": "PluginUvAbove",
  "display": "插件条件：数值大于 0",
  "expr": "({amount}) > 0"
}
```

- 常数公式仍会走现有常量判断路径；
- 运行时公式（X / 状态层数 / 手牌数等）现在走插件分支；
- 不再出现 `while (false)` 死代码。

## 验证

| 项 | 结果 |
|---|---|
| 插件条件 + 公式 `X` 生成 `if (((__fgN...)) > 0)` | 通过 |
| 不再生成 `while (false)` | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
