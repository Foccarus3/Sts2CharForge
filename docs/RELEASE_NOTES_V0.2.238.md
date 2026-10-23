# V0.2.238 — 「生效次数」也能写公式了（遗物 / 药水 / 自定义状态 / 额外资源量）

## 用户问的

「生效次数可以填写公式吗」

## 以前的实际情况（三种）

| 位置 | 以前能不能写公式 |
|---|---|
| 卡牌页的效果 | **能** —— 「生效次数（公式）」框（`TimesFormula`） |
| 锻造【效果】模块 | **能** —— 那个「生效次数」文本框本身就吃公式 / 参数 |
| 遗物 / 药水 / 自定义状态 / 额外资源量 | **不能** —— 界面上只有固定数字框；就算改了存档 JSON，校验器还会报错拦住（「次数公式只做在卡牌上」） |

在校验器拦人的那条后面其实还有一个技术原因：卡牌的次数公式是用 `CountFormulaExpr` 算的，
生成的表达式要读 `__fgPlayer` / `__pM` / `__x` 这些**卡牌前置**局部变量
（`ForgeGraphGen.EmitCardFormulaPrologue` 才声明它们）—— 遗物 / 药水 / 状态的执行点没有，
直接塞进去就是 `CS0103`。所以这一版把它们改成和「数值公式」同一套**内联**求值
（`ForgeFormula.InlineResolver`），三条链都能算。

## 这一版做了什么

1. **生成端**
   - `CSharpCodeGen.EmitRepeated` 多了 `inlineProfile / inlinePlayer / inlineSelf` 三个参数：
     遗物 / 药水传 `base.Owner`（Player）/ `base.Owner.Creature`，公式内联展开；
   - 新增 `CSharpCodeGen.InlineCountFormula(...)`（次数公式的内联版本）；
   - 自定义状态链（`CustomPowerGen.EmitEffect`）的 `for (int __rep …)` 上界也改成现算 + 夹 1~20；
   - 上界一律 `(int)System.Math.Clamp(表达式, 1m, 20m)`（和卡牌那条同一个上限，公式算出 999 不会卡死一局）。
2. **回读标记**：`// CET:CountFormulas=Times=<公式>` 现在遗物 / 药水 / 状态三条链也会写
   （以前只有卡牌写），否则「从工程反推存档」会把公式丢掉。由 `EmitCountFormulaMarker` 统一发。
   没写公式时一行都不写 → 老存档输出逐字节不变。
3. **界面**：遗物 / 药水 / 自定义状态 / 额外资源量那一行的「生效次数」从「单框」改成和卡牌一样的
   `[固定次数] 生效次数（公式）[公式框]`（`App.xaml` 第 5 行）。
4. **校验器**：不再一律拦。现在按栏位分开：
   - 「生效次数」→ 这五条链都放行；
   - 「命中次数 / 复制份数 / 重放次数 / 概率」→ 这几条链的生成端**确实不读**，照旧报错拦住，
     并在文案里说清「只做在卡牌和锻造【效果】里」，不再含糊地说「只做在卡牌上」。
5. 说明文案（`EffectSpec.CountFormulaHint`）也跟着改成「哪几处支持、哪几处不支持」。

## 能写什么

和「数值公式」同一套语法：自定义参数（`M / N…`）、`X`（当前能量）、`Y`（当前额外资源量）、
内置只读量（`Turn` 这一场第几回合、`HandCount` 手牌张数、`DiscardedThisTurn`、`PowersOnMe`…，
完整清单在启动时那个「参数参考」窗）、`min / max` 之类的函数。

例：遗物「每回合对随机敌人造成伤害」的生效次数写 `Turn` → 第 3 回合就重复 3 次；
自定义状态写 `max(1, HandCount)` → 手牌越多重复越多。

## 验收

- 自检：开发版 **4231 PASS / 0 FAIL**（新增 6 条：遗物放行 / 遗物生成内联 Clamp 循环 /
  遗物上「命中次数」公式仍然拦 / 状态链放行 / 状态链生成内联 Clamp 循环 + 回读标记 / 公式框可见性）。
- 对拍（老存档生成结果）：Sample 113 个文件、WhiteCloak 412 个文件 —— **逐字节相同**，
  差异只有 `.sln` 的 GUID（这两份都没写次数公式）。
- 生成样例（探针实测）：
  ```csharp
  // 遗物
  // CET:CountFormulas=Times=Turn
  for (int i = 0; i < (int)System.Math.Clamp((base.Owner?.PlayerCombatState?.TurnNumber ?? 0), 1m, 20m); i++) { … }
  // 自定义状态
  for (int __rep = 0; __rep < (int)System.Math.Clamp((base.Owner.Player?.PlayerCombatState?.TurnNumber ?? 0), 1m, 20m); __rep++) { … }
  ```
  都没有 `__fgPlayer` 这类卡牌前置变量。
