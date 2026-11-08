# V0.3.38 — 灰阶 / 低饱和能量图标：左侧能量球也换成上传图

## 现象

用户上传了能量图标（`Unbreakable Coin.png`）：

- **卡牌上的能量图标正常**（因为卡面直接引用上传图）；
- **左侧能量球还是本体铁甲战士的橙色球**。

## 真因

`ArtGenerator.WriteEnergyCounterRecolor` 以前先做：

```csharp
if (!PngUtil.TryGetDominantHue(icon, out double hue))
{
    log?.Invoke("灰阶/太透明，取不到主色相 → 保持本体配色");
    return;   // ← 直接返回，后面「把球体换成上传图」的代码根本没跑
}
```

`Unbreakable Coin.png` 是灰阶 / 低饱和图，取不到主色相 → 生成器直接保留本体能量球场景，所以左侧一直是战士的橙色球。

## 修法

灰阶 / 低饱和不再整体放弃：

- 取不到色相 → **不做色相转换**，但仍然把 `Layer1` 换成上传的图标（缩成 128×128）；
- 本体的 `RotationLayers` / `Layer4` / `Layer5` 隐藏；
- 粒子的 `EnergyVfxBack` / `EnergyVfxFront` 隐藏，避免留下本体橙色光；
- 有色相时照旧：5 层贴图 + 粒子光效一起转色相。

这样卡面图标和左侧能量球都使用同一张上传图。

## 验证

用用户的 `SparkleMod.json` 真跑 `ArtGenerator.Generate`：

```text
左侧能量球：能量图标是灰阶/太透明，取不到主色相 → 球体直接用这张图，粒子光效隐藏
左侧能量球：球体贴图换成了你上传的能量图标（128×128，本体的另外 4 层与粒子光效已隐藏）
```

生成场景里：

```text
[ext_resource ... path="res://images/ui/combat/energy_counters/sparkle/sparkle_energy_orb.png"]
```

不再是 `ironclad/ironclad_orb_layer_1.png`。

| 项 | 结果 |
|---|---|
| 灰阶图标 → 左侧球体用上传图 | PASS（真实生成验证） |
| 有色图标 → 色相转换照旧 | 保留 |
| 开发版 `--uicheck` | **4889 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4888 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里左侧能量球场景还是本体贴图。
