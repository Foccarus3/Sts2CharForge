# V0.3.30 — 「依赖」升级后，游戏里描述还是升级前的

用 `ArknightJessica.json` 的 **依赖（JessicaCard50）** 复现：

- 基础自定义描述：`升级手中所有的牌。`
- 升级后自定义描述：`对自己施加 {JessicaPower10:diff()} 层子弹。升级手中所有的牌。`
- 游戏里升级后仍显示基础那句。

## 真因

`LocalizationGen` 在生成 `.description` 时，发现升级前 / 升级后两段不同，本来要包成本体的：

```text
{IfUpgraded:show:升级后那段|升级前那段}
```

但旧代码加了一条守卫：

```csharp
// 含占位符就不套 IfUpgraded（内层 } 会破坏结构）
bodyBase.Contains('{') || bodyUp.Contains('{') ? bodyBase : IfUpgradedText(...)
```

「依赖」的升级后描述里有 `{JessicaPower10:diff()}`，于是**升级后那段被直接丢掉**，
`.description` 只剩基础那句 —— 所以游戏里升级后也不变。

## 修法

删掉这条守卫。本体的 `ShowIfUpgradedFormatter` 用的是：

```csharp
formattingInfo.Format.Split('|')   // 只按顶层 | 切两段
formattingInfo.FormatAsChild(...)  // 子段再走 SmartFormat
```

SmartFormat 3.0 本来就支持嵌套 `{...}`；实测同一段：

```text
{IfUpgraded:show:对自己施加 {JessicaPower10:diff()} 层子弹。升级手中所有的牌。|升级手中所有的牌。}
```

能正确输出 `对自己施加 4 层子弹。升级手中所有的牌。`。

## 实测

用 `ArknightJessica.json` 重新生成：

```json
"JESSICA_CARD50.description": "{IfUpgraded:show:对自己施加 {JessicaPower10:diff()} 层子弹。升级手中所有的牌。|升级手中所有的牌。}"
```

升级后游戏会走 `UpgradeDisplay.Upgraded` 分支，显示「对自己施加 … 层子弹。升级手中所有的牌。」。

## 验证

| 项 | 结果 |
|---|---|
| 生成端：带 `{Block:diff()}` 的升级后描述仍包 `{IfUpgraded:show:…|…}` | PASS |
| 真实存档 `JessicaCard50` 的 `.description` 已是 `IfUpgraded` | 是 |
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4874 PASS / 0 FAIL**（+1 条 V0.3.30 断言） |
| 部署版 `--uicheck` | **4873 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧 `cards.json` 里 `JESSICA_CARD50.description` 还是基础那句。
