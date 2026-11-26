# V0.3.108 — 战斗状态图标 tooltip 的 Amount / Amount*2 同步

## 截图现象

骇入状态图标上写着 **2 层**，但悬停说明：

```text
每层使造成的伤害-1%(1)%，受到的伤害+2%(0)%。
```

`{Amount}` 是 1，`{=Amount*2}` 是 0。

## 原因

本体 `PowerModel.HoverTips` 在 **canonical** 模型上取 `Description`：

1. canonical 的 `Amount` 是 0 / 1；
2. 生成的自定义 `Description` 先把 `{=Amount*2}` 算成固定值；
3. `AddDumbVariablesToDescription` 之后才把 `Amount` 改成 1。

所以状态图标上有 2 层，tooltip 仍然来自 canonical 的 1 层。

## 修复

给所有生成的自定义状态加标记接口 `IForgePowerHover`，并生成一个 Harmony 前缀：

```csharp
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
```

对带标记的状态：

1. 用反射取 canonical；
2. `ToMutable(__instance.Amount)` 做一份**当前层数**的副本；
3. 用副本的 `Description` 取文本；
4. 补 `singleStarIcon` / `energyPrefix`，再带原状态自己的 `ExtraHoverTips`。

这样 `Description` 被调用时 `Amount` 就是真实层数：

```text
2 层 → (2) / (4)
1 层 → (1) / (2)
```

## 生成文件

有自定义状态时，工程会多一个：

```text
cs/ForgePowerHoverPatch.cs
```

里面是上面的 Harmony 前缀，随模组 DLL 一起编译。

## 生效方式

更新到 V0.3.108，重新生成模组、重装 DLL。旧的 `ForgePowerHoverPatch.cs` 不会自动进旧工程。

## 验证

| 项 | 结果 |
|---|---|
| 自定义状态实现 `IForgePowerHover` | 通过 |
| 生成 `ForgePowerHoverPatch.cs` | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
