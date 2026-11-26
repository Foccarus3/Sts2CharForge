# V0.3.111 — 状态图标 tooltip 按参考插件改成「只读场上实例」

## 参考

```text
状态图标描述修复.zip
cs/PluginPowerIconHover.cs
```

它的做法：

```csharp
[HarmonyPriority(Priority.First)]
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
static bool Prefix(PowerModel __instance, ref IEnumerable<IHoverTip> __result)
{
    if (__instance is not IForgePowerHover) return true;
    LocString desc = __instance.Description;   // 场上实例，层数真实
    string text = desc.GetFormattedText();
    __result = new List<IHoverTip> { new HoverTip(__instance, text, isSmart: true) };
    return false;
}
```

## 之前为什么还是不显示

V0.3.108 用：

```csharp
canonical.ToMutable(__instance.Amount)
```

创建了 `Owner = null` 的副本，并触发 `set_Amount`。  
第三方打在 `SetAmount` 上的补丁可能对空 Owner 抛 `NullReferenceException`；异常被吞掉后回到本体路径，而本体用的是 canonical，层数固定为 1 —— 严重时整条 tooltip 不显示。

## V0.3.111

生成器内置的 `ForgePowerHoverPatch.cs` 改成和参考插件一致：

- `[HarmonyPriority(Priority.First)]`，先于低优先级过滤补丁；
- **直接读 `__instance.Description`**，不创建副本、不调用 `SetAmount`；
- `isSmart: true`；
- `try/catch`，任何异常都 `return true` 交回本体；
- 只接管实现了 `IForgePowerHover` 的编辑器生成状态。

同时把卡面 tooltip 里的 `ToMutable` 也去掉了，避免同样触发第三方 `SetAmount` 补丁。

## 结果

- 状态栏 2 层 → 描述里 `{Amount}` / `{=Amount*2}` 都是当前层数；
- 不创建副本，不影响玩法状态；
- 补丁失败时仍显示本体 tooltip，不会再整条消失。

## 验证

| 项 | 结果 |
|---|---|
| 生成补丁直接读 `__instance.Description` | 通过 |
| `HarmonyPriority(Priority.First)` | 通过 |
| 不调用 `ToMutable` / `SetAmount` | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
