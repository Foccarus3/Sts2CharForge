# V0.2.289 — 四个「百分比」状态（数值 50 = 50%），图标用原版染紫

## 在哪选

「施加增益/减益」的**状态下拉**里多了四项（`EffectCatalog.PercentPowers`）：

```
百分比易伤（受到伤害 +N%）         ← 本体「易伤」图标染紫
百分比减伤（受到伤害 −N%）         ← 本体「虚弱」图标染紫
百分比增伤（造成伤害 +N%）         ← 本体「力量」图标染紫
百分比减弱（造成伤害 −N%）         ← 本体「敏捷」图标染紫
```

数值就是百分比（**50 = 50%**），作用对象照旧选（自己 / 指定敌人 / 全体…）。

## 生成出来的类（探针实测）

```csharp
public sealed class SevenPercentVulnPower : PowerModel {
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    private decimal Pct => base.Amount / 100m;
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay) {
        if (target != base.Owner) return 1m;              // 只看自己受到的
        if (!props.IsPoweredAttack()) return 1m;          // 和本体易伤同口径
        return System.Math.Max(0m, 1m + Pct);             // 50 → ×1.5
    }
}
```

施加那一句用的是**字面数值**（`Apply<SevenPercentVulnPower>(..., 50m, ...)`）——
刻意没走 `base.DynamicVars["..."]`：`PowerVar<T>` 的键是由类名推的，键名对不上会 `KeyNotFoundException`（这是上一版我撤回的那个坑）。

## 图标怎么染的

`PngTint`（新增，**不加任何 NuGet 依赖**）：用 .NET 自带的 `ZLibStream` 解 IDAT → 还原扫描线（filter 0-4）→ 按亮度映射成紫色
（`R=0.55L+70, G=0.30L+20, B=0.95L+120`，保留 alpha）→ 自己拼 CRC 重新编码。
实测本体 `vulnerable_power.png`（256×256）染出来是一张正常紫色爱心裂纹图（我肉眼比对过）。

目标文件名必须是本体认的那个：`images/powers/<Id.Entry>.png`（= 类名 slug 小写），否则游戏里是「Missing sprite … in power_atlas」+ 紫问号占位。
找不到本体图或格式不认识时，图标保持原色并在日志里说明 —— 不产出坏图。

## 已知边界（写进发版说明）

* **只作用于「有来源的攻击」**（`IsPoweredAttack`，同本体易伤 / 虚弱）—— 毒、灼烧、失去生命不吃这个倍率；
* **先用固定数值**：这四项目前不吃「数值公式」（校验器那层还没加提示，用法上请填数字）；
* 同一种叠两层 = 两个实例的倍率**相乘**（本体规则）；想直接给到 80% 就把层数写成 80。

## 验收

* 自检：**4453 PASS / 0 FAIL**
* 探针：四个 Id 都能映射到 `SevenPercent*Power`，施加语句带字面数值；PNG 染色实测成功（13,929 字节有效 PNG）
* 只出更新包（`-NoBundle`）
