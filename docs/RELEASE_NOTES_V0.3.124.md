# V0.3.124 — 游戏 v0.114 兼容性检查

## 结论

v0.114 没有改掉生成器依赖的公开 API，生成代码可以继续用。

实际验证：

- 用当前生成器生成 `SparkleMod`；
- 把 `libs/sts2.dll` 换成 `D:\download\slay\3`（v0.114）的 DLL；
- `dotnet build`：**0 个错误**。

## v0.114 里看到的改动

| 改动 | 对生成器的影响 |
|---|---|
| `CardModel.UpgradeInternal()` → `UpgradeInternal(bool silent = false)` | 可选参数，旧的无参调用仍能编译 |
| `CardModel.DowngradeInternal()` → `DowngradeInternal(int downgradeAmount = 1)` | 可选参数，旧调用不变 |
| 新增 `CardCmd.RemoveAllUpgrades` | 新 API，不冲突 |
| `CreatureCmd` 内部改用 `DamageResults` | 公开 `Damage` 重载没变；生成代码仍用 `DamageResult` |
| `DamageVar` 预览改成经过 `GetDamageResultsPreview` | 生成的自定义公式变量继续走 `base.UpdateCardPreview`，预览会更准确 |
| `Hook` 新增 `BeforeActEntered` | 新增钩子，不冲突 |
| `HoverTipAlignment.Center` 删除 | **生成器本身没用它**；外部插件如果用了需要改 |

## 已改

`Sts2Metadata.SupportedGameVersion`：

```text
v0.113.0 → v0.114.0
```

插件市场默认 `GameVersion` 也改成 `v0.114.x`。

这样检测到 v0.114 时不会再报「版本不匹配」的警告。

## 注意

`HoverTipAlignment.Center` 是 v0.114 唯一看到的**源码级破坏性改动**：

- 生成器自己的代码不用它；
- 如果某个插件 `csFiles` 里写了 `HoverTipAlignment.Center`，生成工程会编译失败，需要插件作者改成 `Left` / `Right` / `None`。
- 这类插件兼容层没有自动改写：Center 没有等价枚举，硬改会改变界面位置，属于静默行为变化。

## 验证

| 项 | 结果 |
|---|---|
| 生成工程对 v0.114 `sts2.dll` 编译 | **0 errors** |
| 版本常量改为 v0.114.0 | 通过 |
| 开发版 `--uicheck` | **4970 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4969 PASS / 0 FAIL** |
