# V0.2.392 — 多文件生成：计划 → 确认 → 写入（需求书 6.4 / 第一期收尾）

以前插件只能「建界面」，不能「按自己的清单写出多个文件」。这一版补上，并且**默认不写盘**。

## 契约

```csharp
// IEditorHost
EditorGeneratePlan   PlanGenerate(IReadOnlyList<EditorOutputFile> files);   // 只出计划，不写
EditorGenerateResult Generate(IReadOnlyList<EditorOutputFile> files);       // 计划 → 确认 → 写入
```

* `EditorOutputFile { Path, Content }` —— `Path` 是**相对输出目录**的路径（可带子目录）；
* `EditorGeneratePlan { Files, Problems, Summary }` —— 界面拿它给用户看「文件清单」；
* `EditorGenerateResult { Confirmed, Written, Problems, Summary }`。

## 安全与原子性

| 点 | 行为 |
|---|---|
| 越界路径 | `../`、绝对路径、带盘符 → 记进 `Problems`，**整个计划不通过**（不会写任何文件） |
| 没有界面环境 | `ConfirmGenerate` 为 null → **一个文件都不写**（安全默认，不静默写盘） |
| 用户取消 | `Confirmed = false`，磁盘不变 |
| 写一半失败 | 先全部写进 `.tmp`，最后逐个替换；失败时清理所有 `.tmp`，**不留半成品** |
| 确认回调注入 | `EditorExtensionRegistry.ConfirmGenerate`（界面层注入：显示清单 → 确定/取消） |

## 自检

**4759 PASS / 0 FAIL**（+4 条）：
越界路径被拒（`../escape.cs`）/ 无确认回调时 0 文件写出 / 用户取消时 0 文件写出 /
确认后按清单写出 2 个文件（含子目录 `sub/B.cs`，内容正确）。

## 还差什么（第一期）

* 主界面里**真正的确认弹窗**（现在 `ConfirmGenerate` 由自检注入；下一步接到插件详情页：一个「生成」按钮 → 文件清单窗口）；
* 插件详情页显示「最近一次生成输出 / 最近一次异常」（审核信息剩下两项）；
* 第二期：资源选择与预览（图片 / 音频）。

## 说明

Core 里没有任何弹窗代码 —— 确认这一步永远是宿主注入的。这样：
① 自检能完全离线验证；② 插件拿不到「不弹窗直接写盘」的能力。
