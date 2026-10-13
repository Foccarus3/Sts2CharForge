# V0.2.159 更新说明

> V0.2.158 的收尾修复：**`XSpent` / `YSpent` 在「不是那种费用」的牌上会让游戏抛异常**。

## 一、V0.2.158 埋的坑

V0.2.158 给公式加了两个「这张牌**花掉**的」读数（`XSpent` / `YSpent`），生成的是直接调用：

```csharp
(decimal)base.ResolveEnergyXValue()   // XSpent
(decimal)base.ResolveStarXValue()     // YSpent
```

而本体这两个方法**不是 X 费就直接抛异常**（`CardModel` 第一句就是）：

```csharp
if (!EnergyCost.CostsX) throw new InvalidOperationException("This card does not have an X-cost.");  // ResolveEnergyXValue
if (!HasStarCostX)      throw new InvalidOperationException("This card does not have an X-cost.");  // ResolveStarXValue
```

所以只要在**普通牌**（费用不是 `X`、额外资源量费用不是 `Y`）上写一次 `YSpent`，
这张牌一打出去就会在 `OnPlay` 里抛 `InvalidOperationException` —— 战斗中直接出错。

（同一个坑 V0.2.131 在「费用填 `X`」那条路上踩过一次，那边的修法是「不是 X 时按 0 兜底」；
这次照同一条路修。）

## 二、修法：**先判再调**

生成出来的表达式改成三元（本体现成的判据：`CardModel.EnergyCost.CostsX` / `CardModel.HasStarCostX`，
两个都是 `public`）：

| 写法 | 现在生成的是 |
|---|---|
| `XSpent` | `(base.EnergyCost.CostsX ? (decimal)base.ResolveEnergyXValue() : 0m)` |
| `YSpent` | `(base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m)` |

⇒ **不会崩**；「不是那种费用」的牌上读出来是 **0**（这两个数本来也只有 X 费 / Y 费牌才有意义）。

## 三、不静默：用错地方会警告

读数恒为 0 = 这条公式白写，所以「配置检查」补了两条**警告**：

- 效果公式里用了 `XSpent`，但「费用」不是 `X` → 「它不是『X 费』牌，XSpent 读出来**恒为 0**……想让它有意义：把『费用』填成 X」；
- 效果公式里用了 `YSpent`，但「额外资源量费用」不是 `Y` → 同理。

（V0.2.158 已有的那两条「X 费 / Y 费 + 公式里用了 `X` / `Y` → 那一刻读到 0」的警告照旧。）

## 四、验证

- `dotnet build Sts2CharForge.sln -c Debug` → **0 错误**；
- `--uicheck`：Debug **3493 PASS / 0 FAIL**（新增 3 条：两个读数生成的是「先判再调」的三元、
  `XSpent` 用在非 X 费牌上给警告、`YSpent` 用在非 Y 费牌上给警告）；
- **默认输出不变（红线①）**：`--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**
  （只差 `ExampleMod.sln` 的随机 GUID —— 示例配置里没人用 `XSpent` / `YSpent`，所以生成端一个字节都没动）。
