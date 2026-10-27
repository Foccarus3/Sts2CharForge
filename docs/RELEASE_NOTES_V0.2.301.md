# V0.2.301 — 自定义状态里施加「百分比增伤」：描述变英文 + 不生效

## 你问的「新版本改了吗」——刚改完（就是这一版）

这两个症状是**同一处漏扫**造成的，两个都在 V0.2.301 修好。

## 原因

`PercentPowers.UsedIn(p)`（决定「要生成哪个百分比状态的类 / 哪张紫图标 / 哪三条本地化」）以前只扫三处：

```csharp
foreach (var c in p.Cards)   Scan(c?.Effects);
foreach (var r in p.Relics)  Scan(r?.Effects);
foreach (var s in p.Potions) Scan(s?.Effects);
```

**漏了自定义状态的触发效果**（`CustomPowerSpec.Triggers[].Effects`）和**锻造图上的效果**。于是只有在「状态里用百分比增伤」的存档：

* 那四个类**不生成** → 施加那一句引用了不存在的类型（CS0246），或者（旧版把类名写错时）根本不生效；
* `powers.json` 里**没有它的 title / description** → 游戏取不到键，就显示成 `SEVEN_PERCENT_DAMAGE_UP_POWER` 这种英文串。

## 修法

`UsedIn` 改成直接走 `CSharpCodeGen.AllEffects(p)` —— 那是「全存档所有效果」的唯一口径，覆盖
卡牌 / 遗物 / 药水 / **自定义状态触发** / **锻造图**：

```csharp
foreach (var e in CSharpCodeGen.AllEffects(p))
    if (CanonicalIdOf(e.PowerId) is { } key && !used.Contains(key)) used.Add(key);
```

同时给状态链和锻造链的生成入口补上 `PercentPowers.Current = profile`（类名是靠它拼的），
否则深处的发射器拿不到存档、会退回成别名。

## 探针实测（状态里施加「百分比增伤」，数值公式 100）

```
UsedIn = PercentDamageUp
本地化: "SEVEN_PERCENT_DAMAGE_UP_POWER.title": "百分比增伤"
        "SEVEN_PERCENT_DAMAGE_UP_POWER.description": "百分比增伤（造成伤害 +N%）：层数就是百分比（50 = 50%），只作用于有来源的攻击。"
生成:   await PowerCmd.Apply<SevenPercentDamageUpPower>(choiceContext, other, (100m), base.Owner, null);
```

类名对、数值是公式算出来的 **100**（不是字段里那个 1 —— 就是之前「挂了状态但没效果」的原因）。

## 验收 / 发布

* 自检：**4454 PASS / 0 FAIL**（4682 行，无异常）
* 只出更新包（`-NoBundle`）

## 要用的话

换到 V0.2.301 → **重新一键构建 + 安装**（老的生成物里没有那两个类，必须重新生成）。
