# V0.3.63 — 修锻造图收尾括号被行注释吞掉（CS1513）

## A-1 阻断级

**现象**：锻造图效果片段以 `// 注释` 结尾时，包裹的收尾 `} } }` 被拼在同一行，整段被注释掉：

```csharp
if (__fgSelf.CombatState is not null) { foreach (...) { if (...) { await PlayerCmd.LoseEnergy(...);   // 负数 = 失去能量 } } }
```

→ `CS1513 应输入 }`。

**修复**：

- `ForgeGraphGen.PlayerPass` 的「指定队友 / 全部友方」包裹，所有收尾括号改为**另起一行**；
- `ForgeGraphGen` 的召唤物兜底包裹同样分行；
- `FoeLoop` / `FoeLoopApplyPower` 的花括号收尾也分行。

现在即使效果片段最后一行是 `// 注释`，后面的 `}` 也不会被注释掉。

## 验证

| 项 | 结果 |
|---|---|
| 「全部友方（不含召唤物）+ 获得能量（负数 = 失去）」生成源码 | 无 `//` 后接 `}`，语法检查无 CS1513 |
| 召唤物兜底包裹 | 收尾括号分行 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

## 文档同步（D 类）

- `docs/插件开发规范.md`：apiVersion 写成 **2**，标题版本改 V0.3.63；
- `docs/插件AI提示词.md`：apiVersion 2，`EnemyDeath` 的 `AfterDeath` 补 `source` 参数。

⚠ B 类（300+ 可空警告）与 C 类（插件全局命名空间）尚未改动，属于质量问题，后续单独处理。
