# V0.2.175 更新说明

> 回答你报的：**「格挡怎么不能失去」**——你说是在**锻造节点图**上：【效果】= 格挡、**数值填负数**、目标是**自己**。
> 正是这个坑，已修。

---

## 一、真因：节点图那条链**没有负数处理**

卡牌页那条链一直是**对的**（`CSharpCodeGen` 的 `case "Block"`：负数走 `CreatureCmd.LoseBlock`）：

```csharp
// 卡牌页：格挡 = -5 → 生成
await CreatureCmd.LoseBlock(choiceContext, base.Owner.Creature, System.Math.Abs(base.DynamicVars.Block.BaseValue), base.Owner.Creature);
```

但**节点图**那条链（`ForgeGraphGen.EmitEffect` 的 `"Block"` 分支）只写了一句 `GainBlock`：

```csharp
// 节点图：格挡 = -5 → 以前生成
await CreatureCmd.GainBlock(__fgFoe, (-5m), ValueProp.Move, cardPlay);   // ✗ 本体对非正数什么都不做
```

本体 `CreatureCmd.GainBlock` 拿到非正数是**直接返回**的（不报错、不扣、日志也没有）——
所以你看到的就是「格挡一点都没少，也没任何提示」。正数当然一直是好的。

## 二、修法

节点图上多一条判据，口径**和卡牌页完全一致**（`IsNegativeAmount` 那一套）：

```csharp
"Block" when !amountN.Runtime && amountN.Value < 0m =>   // 常数且 < 0 → 失去格挡
    await CreatureCmd.LoseBlock(choiceContext, __fgFoe, 5m, __fgSelf);   // 负数 = 失去格挡
```

- **常数负数**（图上直接填 `-5`）→ `LoseBlock`；
- **运行时公式算出来的值**（生成期不知道正负）→ 照旧 `GainBlock`（和卡牌页对 X / 公式的处理同一口径，
  不会因为「猜不出来」就乱走一条）；
- **「下回合生效」+ 负数**：本体没有「下回合失去格挡」这个落点，所以按**立即生效**生成
  （卡牌页那条负数额也是立即生效）；
- 正数 / 下回合生效的正数：**一个字符都没变**（老图产物逐字不变）。

## 三、验证

- 探针实测（卡牌页 / 节点图 × 正数 / 负数 × 自己 / 敌人）：

| 配置 | 生成结果 |
|---|---|
| 卡牌页 · 格挡 −5 · 自己 | `CreatureCmd.LoseBlock(…)` ✓（一直是对的） |
| 卡牌页 · 格挡 −5 · 敌人 | `CreatureCmd.LoseBlock(…)` ✓（一直是对的） |
| **节点图 · 格挡 −5** | **`CreatureCmd.LoseBlock(choiceContext, __fgFoe, 5m, __fgSelf)`** ✓（**以前是 `GainBlock(-5)`**） |
| 节点图 · 格挡 +5 | `CreatureCmd.GainBlock(__fgFoe, (5m), …)` ✓（未变） |

- 自检新增一条钉死：节点图负数必须生成 `LoseBlock`；`--uicheck` → **3774 PASS / 0 FAIL**；
- `dotnet build` → **0 错误**。

## 四、你要做的

**重新生成 + 一键构建·安装**（改的是生成出来的 C#，存档不用动）。

> ⚠ 同一份存档里，**别的**「图上负数」类效果这次没一起查（你只报了格挡）。如果你在图上还用负数填过
> 别的（例：获得能量填负数 = 失去能量、最大生命填负数），告诉我，我按同一套口径过一遍
> ——「能量」那一条我记得图上有专门处理，「最大生命 / 回复生命」这些还没逐个确认。
