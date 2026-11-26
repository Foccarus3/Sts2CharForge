# V0.3.126 — 全局变量在存档 / 读档后不再被强行重置

## 现象

变量页里勾了「全局」的变量（例如 `ExtraArmor`）在：

```text
打一场 → 存档 → 读档
```

之后回到初值 0。

## 原因

生成的重置补丁把四个入口都当成「新开一局」：

```text
SetUpNewSingleplayer
SetUpNewMultiplayer
SetUpSavedSingleplayer   ← 读档
SetUpSavedMultiplayer    ← 读档
```

读档时也调用：

```csharp
TigerVars.ResetRun();
```

而全局变量是静态字段，本来就在内存里；无条件 ResetRun 就会把本局累计值清掉。

## 修复

生成的变量类增加运行种子：

```csharp
private static string __runKey = "";

public static void EnsureRun(string key)
{
    if (string.IsNullOrEmpty(key)
        || !string.Equals(__runKey, key, StringComparison.Ordinal))
        ResetRun(key);
}
```

重置补丁改成：

| 入口 | 行为 |
|---|---|
| 新开一局（单 / 联机） | `ResetRun(seed)`，清空并记录种子 |
| 读档（单 / 联机） | `EnsureRun(seed)`，**同一种子保留内存里的值**；不同局才重置 |

## 范围

- 同一个游戏进程内的「存档 → 读档」：全局变量保留；
- 新开一局：仍然清零；
- 完全退出游戏再启动：静态字段本来就不在存档里，仍从初值开始（这不是本次改的范围；要做跨进程持久化需要另存文件）。

## 生效方式

更新到 V0.3.126，重新生成工程并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 生成的变量类有 `__runKey` / `EnsureRun` | 通过 |
| 新开一局调用 `ResetRun(seed)` | 通过 |
| 读档调用 `EnsureRun(seed)` | 通过 |
| 开发版 `--uicheck` | **4972 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4971 PASS / 0 FAIL** |
