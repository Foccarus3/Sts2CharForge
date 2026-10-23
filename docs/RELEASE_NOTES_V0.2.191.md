# V0.2.191 更新说明

> 你问：「**所以现在失去金币能生效了吗**」—— 分开说：
>
> | 效果写在哪 | 失去金币（数值填负数） |
> |---|---|
> | 卡牌 / 遗物 / 药水 | **一直就能**（生成的是 `PlayerCmd.LoseGold`）✓ |
> | 自定义状态 | V0.2.190 修好 ✓ |
> | **锻造节点图** | 刚才**还不能** —— 这一版（V0.2.191）修好 ✓ |
>
> 也就是说：上一版我修的是**状态链**，节点图那条链还漏着（你八成是在节点图上试的）。
> 这一版把节点图上同族的五种一起补齐了。

---

## 一、节点图上漏掉的那五种（V0.2.174 只做了「失去格挡」）

```csharp
// 旧版（节点图）：一律写「获得」，而本体 Gain* 对非正数什么都不做（额外资源量那条更狠：直接抛异常）
await PlayerCmd.GainGold(-6m, __fgPlayer);        // 一点金币都不掉
await PlayerCmd.GainEnergy(-6m, __fgPlayer);      // 一点能量都不掉
await PlayerCmd.GainStars(-6m, __fgPlayer);       // 本体直接抛 ArgumentException！
await CreatureCmd.Heal(__fgFoe, -6m);             // 什么都不发生
await CreatureCmd.GainMaxHp(__fgFoe, -6m);        // 什么都不发生
```

**改后**（判据和「失去格挡」那一档完全一致：数值是常数且 < 0；运行时公式 / X 的正负生成期不知道，照旧按「获得」生成）：

```csharp
await PlayerCmd.LoseGold(6m, __fgPlayer);                                        // 失去金币 ✓
await PlayerCmd.LoseEnergy(6m, __fgPlayer);                                      // 失去能量 ✓
await PlayerCmd.LoseStars(6m, __fgPlayer);                                       // 失去额外资源量 ✓
await CreatureCmd.Damage(choiceContext, __fgFoe, 6m, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this, cardPlay);   // 失去生命 ✓
await CreatureCmd.LoseMaxHp(choiceContext, __fgFoe, 6m, isFromCard: true);        // 失去最大生命 ✓
```

（本体的 API 语义都核过：`GainGold` / `GainEnergy` 对非正数**直接 return**；`GainStars` 对负数**抛异常**；
`Heal` 对非正数不做事。金币 / 能量 / 额外资源量三者都会被夹在 0，不会扣成负数。）

## 二、已经验过

| 项 | 结果 |
|---|---|
| 六种负数（金币 / 能量 / 额外资源量 / 回复生命 / 最大生命 / 格挡）**逐条生成** | 全部是上面那几行「Lose*」✓ |
| 生成物**真编译** | 探针工程 `dotnet build` **0 错误** |
| **从工程恢复** | 六张探针卡**原样认回来**（`Gold=-6 / Energy=-6 / ExtraResource=-6 / Heal=-6 / MaxHp=-6 / Block=-6`），没有「没认出来」 |
| `--uicheck` | **3830 PASS / 0 FAIL**（新增 5 条钉住节点图这五种） |
| `--samplegen` | 113 → 114，**5 处既有差异**（新增 `cs/ForgeCardLibrary.cs`(V0.2.187) ＋ 4 处旧差异）—— 默认档零影响 |
| 上一版（V0.2.190） | 同一份包：**状态链**上的五种负数（失去格挡 / 失去生命 / 失去能量 / 失去金币 / 失去额外资源量） |

## 三、小结：现在「负数 = 反向」的全貌

| 效果 | 卡牌 | 遗物 | 药水 | 自定义状态 | 锻造节点图 |
|---|---|---|---|---|---|
| 失去格挡 | ✓ | ✓ | ✓ | V0.2.190 | V0.2.174 |
| 失去生命（回复生命填负数） | ✓ | ✓ | ✓ | V0.2.190 | V0.2.191 |
| 失去能量 | ✓ | ✓ | ✓ | V0.2.190 | V0.2.191 |
| 失去金币 | ✓ | ✓ | ✓ | V0.2.190 | V0.2.191 |
| 失去额外资源量 | ✓（卡牌上按「费用」处理） | ✓ | ✓ | V0.2.190 | V0.2.191 |
| 失去最大生命 | ✓ | ✓ | ✓ | 一直就对 | V0.2.191 |
| 扣除状态层数 | ✓ | ✓ | ✓ | ✓ | ✓ |

**记得重新生成 + 一键构建 + 替换 mod。**
