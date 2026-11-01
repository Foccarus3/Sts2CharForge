# V0.2.378 — 字段联动 `visibleWhen`（**只做了生成端**，界面未完成）

缺口文档第 8 节「字段联动 / 条件显示」。这一版把**数据与生成**这一半做完，界面那一半**没做完**（如实说明）。

## 已完成

* 新增栏位属性 `visibleWhen`（`plugin.json` 里可写）：
  * `"visibleWhen": "kind"` —— 该栏位有值 / 非 0 / 非 false 才显示；
  * `"visibleWhen": "kind=attack"` / `"kind!=attack"`。
* 求值函数 `PluginRegistry.FieldVisible(field, get)`：页面级取同页标量，list 行内取**同一行**的子栏位。
* **生成端**：`PluginGen` 在替换模板占位符时，`visibleWhen` 不成立的栏位按**空值**参与
  （不会把上一次填的值残留进生成的代码）——页面级标量和 list 行内子栏位都做了。

## 没做完（如实说）

* **界面不会隐藏/显示栏位**：我把「页面表单按条件跳过栏位」那段改进了 `MainWindow.xaml.cs`，
  结果把 4000 行的文件改出括号不匹配（`CS1513`），已 `git checkout` 回滚到 V0.2.377。
  ⇒ 现在：`visibleWhen` **只影响生成结果**（隐藏的栏位生成时按空值），界面上仍然全部显示。
* 因此这一版**不能**替代老 EnemyForge 的「选了什么才出现什么」的编辑体验。
* 也没加自检断言（界面回滚时一并撤掉了）；开发版 **4716 PASS / 0 FAIL**（= V0.2.377 的基线）。

## 下次怎么改（教训已写进交接）

`MainWindow.xaml.cs` 这类大文件里**不要**用「按行号插一段」的做法改结构（我这轮两次栽在括号上）。
正确做法：先 `read` 出目标方法，再用 `edit` 以**完整方法**为单位替换；或者把「按条件跳过」写成
一个独立小方法 `ShouldShowField(page, spec, f)`，在 `foreach` 里**只插入一行** `if (!ShouldShowField(...)) continue;`。
