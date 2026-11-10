# V0.3.51 — 不同存档的 ModId / 安装路径自动带存档名后缀

## 问题

生成器不同存档的 ModId 经常相同（默认 `MyCharMod`、复制存档、同一模板），
所以：

- `mods\<ModId>.dll`、`.pck`、`.json` 是同一个路径；
- 创意工坊包里的清单 `id` 也相同；
- 装在一起时后一个会覆盖前一个，或者同 id 的创意工坊版本直接不显示。

## 修法

生成时统一由 `Naming.From(profile)` 计算**有效 ModId**：

```text
有效 ModId = 基础 ModId + 存档文件名后缀（不合法字符 → 稳定短哈希）
```

例如：

```text
ModId=ExampleMod，存档名=SparkleMod   → ExampleModSparkleMod
ModId=ExampleMod，存档名=星光存档     → ExampleModS91CEAC2C
```

这个有效 ModId 会一起进入：

- `mod_manifest.json` 的 `id`
- `<ModId>.dll` / `.pck` / `.json` 文件名
- 工程命名空间 / csproj / sln / 构建脚本
- `mods` 安装路径
- 本地化目录 `<ModId>/localization/...`

以下情况保持不变：

- 存档名和 ModId 相同（例如 `SparkleMod` + `SparkleMod`）→ 不加重复后缀；
- 从工程恢复出来的 ModId 已经带后缀 → 不会重复追加；
- 没打开存档 / 自检用的内存配置（`SaveName` 为空）→ 保持原样；
- 中文 / 全角存档名 → 用稳定 FNV-1a 短后缀，保证合法且同一存档每次一致。

## 验证

| 项 | 结果 |
|---|---|
| 不同存档 → 不同有效 ModId | PASS |
| 中文存档名后缀合法且稳定 | PASS |
| 从工程恢复再生成不重复追加 | PASS |
| 召唤物 / 关键词 / 动画等旧自检 | PASS |
| 开发版 `--uicheck` | **4905 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4904 PASS / 0 FAIL** |

⚠ 已上传的创意工坊条目如果基础 ModId 与别的存档相同，重新生成后清单 id 会变，
在 Steam 看来是一份“新模组”；需要重新上传或更新工坊条目。
