# V0.3.128 — 初始打防副本在百科里再兜一层隐藏

## 现象

`RUKAYUKI_STRIKE_COPY2/3/4`、`RUKAYUKI_DEFEND_COPY2/3/4` 仍然出现在百科 / 已发现卡牌里。

## 原因

副本卡构造时本来已经带：

```csharp
shouldShowInCardLibrary: false   // V0.3.85 起
```

但有两种情况会绕过它：

1. **实际加载的 DLL 是旧生成器编译的** —— 老 DLL 里的副本没有这个参数；
2. 某些百科 / 图鉴 mod 直接读 `ProgressState.DiscoveredCards`，不看 `ShouldShowInCardLibrary`。

## V0.3.128

生成的 `cs/ForgeCardLibrary.cs` 里增加两个运行时补丁，按**副本 ID** 再隐藏一次：

```text
SEVEN_STRIKE_COPY2 ...（按当前存档实际生成的副本 ID）
```

- `CardModel.ShouldShowInCardLibrary` getter → 副本返回 `false`；
- `ProgressState.DiscoveredCards` getter → 过滤掉副本 ID。

这样即使：

- 副本构造参数没生效；
- 百科 mod 直接读 `DiscoveredCards`；

也不会在百科里列出多张打击 / 防御。

## 重要：必须重新生成

生成器只能修改**新生成的工程**。已经装进游戏的：

```text
rukayukiking.dll
```

如果它是旧版生成器编出来的，里面仍然是旧构造函数；更新工具后**必须重新生成 + 重新构建 + 重装 DLL/PCK**，否则百科还是旧行为。

## 验证

| 项 | 结果 |
|---|---|
| 生成代码包含副本 ID 列表 | 通过 |
| `ShouldShowInCardLibrary` / `DiscoveredCards` 两个补丁 | 通过 |
| 生成的补丁对 v0.114 `sts2.dll` 编译 | **0 errors** |
| 开发版 `--uicheck` | **4974 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4973 PASS / 0 FAIL** |
