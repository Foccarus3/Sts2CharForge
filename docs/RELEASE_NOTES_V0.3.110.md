# V0.3.110 — 状态 tooltip 补丁失败时回退本体

## 现象

V0.3.108/109 之后，骇入状态完全不出 tooltip。

## 原因

`ForgePowerHoverPatch` 是 Harmony 前缀，直接接管 `PowerModel.HoverTips`：

- 本体 / 第三方 mod（日志里的 `PctPowerHide`）只要有一处 API 行为不一致；
- 或 `ToMutable` / `Description` / `EnergyIconHelper` 任一处抛异常；
- 前缀没有兜底，tooltip 整条消失。

## 修复

V0.3.110 给补丁加 `try/catch`：

```csharp
try
{
    // 当前层数重建
    ...
    return false;
}
catch
{
    // 失败就交回本体，至少让 tooltip 能显示
    return true;
}
```

同时保留：

- `isSmart: false`（和本体无 smartDescription 时一致）；
- 空文本也回退本体。

## 结果

- 补丁成功：2 层显示 `(2)` / `(4)`；
- 补丁失败：显示本体原本的 tooltip，而不是“完全没状态”。

## 生效方式

更新到 V0.3.110，重新生成 **rukayukiking** 工程并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 补丁异常回退本体 | 通过 |
| 空文本回退本体 | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
