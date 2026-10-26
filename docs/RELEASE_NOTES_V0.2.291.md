# V0.2.291 — 「百分比状态」生成后编译不过（CS0246）+ 状态被解析成类名

## 你贴的错

```
SparkleCard43.cs(9,34):  error CS0246: 未能找到类型或命名空间名"PercentDamageUp"
SparkleCard43.cs(19,64): error CS0246: 未能找到类型或命名空间名"PercentDamageUp"
SparkleCard43.cs(29,34): error CS0246: 未能找到类型或命名空间名"SparklePercentDamageUpPower"
```

三个错误其实是**同一个原因**：同一个状态在四处发射点里被写成了**两种不同的名字**。

| 生成出来的位置 | 之前 | 现在 |
|---|---|---|
| `CanonicalVars` 的 `ForgeFormulaPowerVar<…>` | `PercentDamageUp`（别名，不是类型） | `SparklePercentDamageUpPower` |
| `ExtraHoverTips` 的 `FromPower<…>` | `PercentDamageUp` | `SparklePercentDamageUpPower` |
| `PowerCmd.Apply<…>` | `SparklePercentDamageUpPower`（类却没生成） | 同一个类，且**会生成** |
| `DynamicVars["…"]` | `PercentDamageUp`（键对不上） | 同一个键 |

## 修法（不是在四处打补丁，而是**只在一个地方**收口）

`ProfileFactory.Normalize`（读档必经）里把四个百分比状态的 `PowerId` **就地解析成生成端类名**：

```csharp
foreach (var e in CSharpCodeGen.AllEffects(p))
    if (e is not null && PercentPowers.ClassOf(e.PowerId, p) is { } pctCls)
        e.PowerId = pctCls;
```

这样 `PowerVar` / `ForgeFormulaPowerVar` / `FromPower` / `DynamicVars` 的键 / `PowerCmd.Apply` **全都拿到同一个名字**，
不会再出现「一半别名、一半类名」。

配套两处：
* `PercentPowers` 现在**别名和类名都认**（`CanonicalIdOf`），`UsedIn()` 用它去重 → `ModGenerator` 会真的把那四个类写出来
  （你这次的 `cs/Powers/` 里一个 `*Percent*` 都没有，就是这一步没命中）；
* `EffectCatalog.FindPower` 也认「已解析的类名」，否则界面下拉会显示成「没选状态」（就是 V0.2.290 那个坑的翻版）。

## 你要做的

换上 V0.2.291 后**重新点一次「一键构建 + 安装」**。之前那次生成的工程里 `SparkleCard43` 那份代码是坏的，
重新生成会整份覆盖（程序会先清掉旧工程目录）。

## 验收

* 自检 **4453 PASS / 0 FAIL**
* 探针：`ForgeFormulaPowerVar` / `PowerVar` / `FromPower` / `Apply` / `DynamicVars` 五处名字一致，`UsedIn` 能列出用到的状态
