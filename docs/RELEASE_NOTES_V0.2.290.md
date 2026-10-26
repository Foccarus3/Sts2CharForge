# V0.2.290 — 「百分比增伤」选了却被提示「未填入状态」

## 原因

`EffectCatalog.FindPower(Id)` 只查了**本体那 200+ 个状态的清单**，而 V0.2.289 新加的四个百分比状态是
**生成端内置写的那四个类**（`PercentPowers`，不在本体清单里）。

于是校验器这一句：

```csharp
else if (e.Kind is "ApplyPower" or "TempPower"
    && EffectCatalog.FindPower(e.PowerId) is null
    && !EffectCatalog.IsCustomPower(e.PowerId))
    issues.Add(new("错误", "…增益/减益还没选状态：未选择有效的 Power…"));
```

明明选好了 `PercentDamageUp`，`FindPower` 仍然返回 null → 判成「未选择」→ 构建被拦。

## 修法

`FindPower` 现在**同时查本体清单和四个百分比状态**：

```csharp
: Powers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
  ?? PercentPowers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
```

一处修好，所有依赖 `FindPower` 的地方（校验器、`SlugFor`、`PowerName`、界面显示）都跟着正确。

## 验收

* 探针：`FindPower("PercentDamageUp")` = `＋ 百分比增伤（造成伤害 +N%）`；用「施加百分比增伤 50」的卡跑校验 →
  **不再报「未填入状态」**（剩下的两条是关于解包目录 / sts2.dll 的环境提示，和这个无关）。
* 自检：**4453 PASS / 0 FAIL**。
