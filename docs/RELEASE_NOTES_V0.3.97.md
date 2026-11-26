# V0.3.97 —— 百科（卡牌图鉴）里公式卡的数值全是 0

> 用户问：「为什么百科里面卡的数值都是 0」

## 怎么回事

主菜单的「百科大全（卡牌图鉴）」是在**没有进行中的一局**时打开的，那时卡片实例的 **`Owner` 是 null**。
而工具生成出来的「公式型数值」变量（`ForgeFormulaDamageVar / BlockVar / HpLossVar / HealVar / PowerVar`）
预览逻辑是：

```csharp
public override void UpdateCardPreview(...)
{
    if (card.Owner is not null)                       // ← 百科里这里不成立
        BaseValue = _formula(card, target) + 升级增量;
    base.UpdateCardPreview(...);
}
```

百科里公式**一次都不跑**，显示的就是构造函数里那个基准值 —— 而基准值以前**写死 `0m`**，
所以百科里所有公式卡都是 0；战斗内 / 卡组里有 Owner，数字是对的。
固定数值的卡不受影响（`new DamageVar(6m, …)` 本身带值）。

## 改成了什么（按你定的规则）

**公式里所有标识符（自定义参数、变量 A/B、`Turn`/`HandCount`/`X` 这类运行时读数）一律当 0 折一遍**，
折出来的数当百科里显示的基准值：

| 公式 | 百科显示 |
|---|---|
| `2+A*2` | **2** |
| `6*A` | **0** |
| `A*0+5` | **5** |
| `2+3*2`（无变量） | **8**（照常算） |
| `6/A` | **0**（除零按 0，不报错） |

实现：

1. 新增 `ForgeFormula.PreviewBaseOf(formula, p)` —— 用「标识符恒为 0」的常量求值器折值，折不出来（除零 / 对 0 取余 / 语法错）→ 0；
2. 生成变量时把它当**构造基准**：`new ForgeFormulaDamageVar(2m, static (card, target) => …)`；
   **折出来正好 0 时不写这个参数**，那类卡的生成文本与以前逐字一致；
3. 生成的 `ForgeFormula*Var` 类增加带 `previewBase` 的构造函数重载（旧重载保留，旧工程照样能编译）；
4. `ProjectRecovery` 的「普通变量」正则加 `(?!ForgeFormula)` 前瞻 —— 否则带基准值的新写法会被当成普通变量读进来，
   回读会多一个假变量、后面所有效果的数值错位。

**战斗内数值不受影响**：`UpdateCardPreview` 仍会用真公式覆盖这个基准值。

## 验收

- 探针 `_probe/prevbase` **10/10 PASS**：
  - 求值：`2+A*2`→2、`6*A`→0、`2+3*2`→8、`A*0+5`→5、`6/A`→0、语法错→0
  - 生成文本：`2+A*2` → `ForgeFormulaDamageVar(2m, static …)`；`6*A` → 不带基准参数；`2+3*2` → 带 `8m`
- `dotnet build` 全解决方案 **0 error**
- `--uicheck` **4941 PASS / 0 FAIL**（新增 3 条断言；另更新 2 条钉在旧文本上的断言）

## 版本

- `V0.3.97`
