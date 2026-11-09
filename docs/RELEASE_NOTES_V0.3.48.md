# V0.3.48 — 紧急修复：V0.3.47 的 `NCard._Process` 补丁导致模组加载失败（角色消失）

## 现象

V0.3.47 构建后**角色都看不到**。

## 日志

```text
SparkleMod - 加载失败
HarmonyException: Patching exception in method null
---> System.ArgumentException:
Undefined target method for patch method static void SparkleMod.ExtraResourceCardIconKeepPatch::AfterProcess(NCard __instance)
```

## 真因

V0.3.47 为了「卡面资源图标每帧兜底」给 `NCard._Process` 打补丁：

```csharp
[HarmonyPatch(typeof(NCard), "_Process")]
```

但 **`NCard` 自己没有 `_Process` 方法**（它继承 Node，但没 override），Harmony 找不到目标 → `PatchAll` 抛异常 →
`SparkleModInitializer.Initialize()` 失败 → 整个模组没加载 → 角色自然看不到。

## 修法

改挂 `NCard.UpdateVisuals`（NCard 自己的公开方法，每次刷新卡面都会走）：

```csharp
[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
public static class ExtraResourceCardIconRefreshPatch
{
    [HarmonyPostfix]
    private static void AfterUpdateVisuals(NCard __instance)
        => ExtraResourceCardIconPatch.ApplyCardIcon(__instance);
}
```

V0.3.47 的 Y 修复保留（`HasStarCostX` 不再走 `base`，Y 已能正确取到 3）。

## 验证

| 项 | 结果 |
|---|---|
| 生成补丁目标为 `NCard.UpdateVisuals` | PASS |
| 不再生成 `NCard._Process` | PASS |
| 生成工程真编译 | 0 错误 |
| 开发版 `--uicheck` | **4900 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4899 PASS / 0 FAIL** |

⚠ 请用 V0.3.48 重新生成 + 重新构建；V0.3.47 生成的工程必须重做，否则模组仍然加载失败。
