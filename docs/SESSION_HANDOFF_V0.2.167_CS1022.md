# 交接说明：用户日志里的 `PowerRemoval.cs(64,1) CS1022`（V0.2.167）

> 写给**其他会话**看的：这份是「另一个会话已经查完的结论」，照抄结论即可，不用再查一遍。
> 触发材料：用户贴的构建失败截图 + 构建日志 `构建日志_20261013_215854.txt`（679 行）。

## 一、用户报的是什么

`一键构建 + 安装` 走到「编译 DLL（dotnet build）」时报唯一一个错、0 个警告 1 个错误：

```
C:\Users\bzbl\Desktop\sts2CharForge\自定义角色存档\WhiteCloakMod\cs\PowerRemoval.cs(64,1):
error CS1022: 应输入类型、命名空间定义或文件尾
[WhiteCloakMod.csproj]
```

日志里这轮的顺序是：`生成完成 ✅` → dotnet build → 上面这条 → `生成失败`。生成阶段本身没有红色错误，
`生成` 那一段明确写了「已生成『某个状态被移除时』的通知补丁（本体没有全局的『状态被移除』钩子）」——
也就是**这个 `cs\PowerRemoval.cs` 是工具自己生成的**，不是用户手写的。

## 二、根因（已定位，不是用户存档配错）

`CSharpCodeGen.PowerRemovalSource(profile)` 收尾时**多写了一个 `.Close()`**：

- 正确收尾 = 关 `Notify(...)` + 关 `internal static class ForgePowerRemoved`（**两层**）；
- 旧代码写了三层 → 生成的文件 `{` 17 个、`}` 18 个，**末尾多一行孤立的 `}`**；
- 行号正好落在 **64**（正文 62 行 + `Notify` 的 `}` = 63 行，第 64 行就是那个多余的 `}`），
  和用户日志的 `(64,1)` 完全对上；
- 只要存档里配了**任意一条**「某个状态被移除时」，这个文件必生成、必编不过 → `CS1022`。
  用户的 WhiteCloakMod（30 个自定义状态 / 5 个遗物）正好配了这条时机。

复现样本：`_probe\bracecheck\out_PowerRemoval.cs`（19:33 写的，**修复前**生成物）——63 行正文、
第 63 行 `}`、第 64 行多一个 `}`。`:64:1 CS1022` 与它逐字对应。

## 三、修复状态：源码已修好，但**还没提交**

- 修复位置：`src\Sts2CharForge.Core\Generation\CSharpCodeGen.cs` 的 `PowerRemovalSource()`，
  现在只留两个 `.Close()`（旁边有 ⚠ 注释解释「以前多写一个 → CS1022」）。
- `git log -1` = `b8273f7 发版 V0.2.144 ~ V0.2.165`；`git status` 里
  `M src/Sts2CharForge.Core/Generation/CSharpCodeGen.cs`（含这条修复 + 若干 V0.2.167/V0.2.168 改动）、
  `?? docs/RELEASE_NOTES_V0.2.167.md`——**都还在工作区，没提交**。
- `CSharpCodeGen.cs` 最后写入时间 **10/13 21:57:04**，比已打包的 V0.2.167（`程序文件\` 里
  `Sts2CharForge.exe`/`Sts2CharForge.dll` = 20:18、`Sts2CharForge.Core.dll` = 20:16:47）**更新**。
- 发版记录见 `docs\QUEUE.md` 第 52 批（V0.2.167），⑤「同批带上的另一批修复」第一条就是这条花括号配平；
  发版命令 `tools\release_all.ps1 -Version V0.2.167 -NoRelease -NoCommit -Yes`。
- 自检断言：`src\Sts2CharForge.App\MainWindow.xaml.cs` 约 20521 行
  「V0.2.167①：PowerRemoval.cs 花括号配平」；独立探针 `_probe\fixcheck` ① 同款断言。

## 四、验证证据（已跑过，别重复造）

| 验证 | 命令要点 | 结果 |
|---|---|---|
| 源码修复后平衡 | `_probe\fixcheck\bin\Debug\net9.0\fixcheck.exe`（把它 bin 里的 `Sts2CharForge.Core.dll` 换成 `t3\src\Sts2CharForge.Core\bin\Debug\net9.0\Sts2CharForge.Core.dll` 再跑） | `① {=17 }=17，行数=64`（64 行含末尾空行）、**ALL PASS** |
| **已发版二进制**里也带修复 | 同上，但换成 `D:\ds\s\4\程序文件\Sts2CharForge.Core.dll`（20:16:47） | `① {=17 }=17`、**ALL PASS** → 已打包的 V0.2.167 生成器是好的 |
| 报告里的坏形状 | 直接读 `_probe\bracecheck\out_PowerRemoval.cs` | 末尾多一个 `}` → CS1022，行号 64 |
| 生成探针现状 | `forge.exe --remprobe <out.cs>` | 全文 `{=32 }=32`，PowerRemoval 段以 `    }` / `}` 正常收尾 |
| 本机装的是哪版 | `(Get-Item 'D:\ds\s\4\程序文件\Sts2CharForge.exe').VersionInfo` | `ProductVersion = V0.2.167`；该 exe 与 `Sts2CharForge_更新包_V0.2.167.zip` 内 `程序文件/Sts2CharForge.exe` SHA256 一致 |

注意：`Sts2CharForge.Core.dll` 是 **net9.0**，不能被 Windows PowerShell 5.1 直接
`Assembly.LoadFrom` 调方法（`Could not load type 'System.Object' ... parent does not exist`）；
要**借 `fixcheck.exe` 的运行时**去替换 bin 里的 Core.dll 来验证，`_tmp\braceprobe\` 下留了脚本（见第六节）。
`--uicheck` 用 `Start-Process` 重定向 stdout 会 **0 行输出**（WPF 不落地），别用这个方式验，改用 fixcheck。

## 五、给用户/其他会话的结论与动作

1. 这不是存档配错，是**工具生成端的 bug**；「某个状态被移除时」配了就必炸，改存档没用。
2. 让用户确认自己跑的是哪版程序：`Sts2CharForge.exe` → 属性 → 详细信息 → `产品版本` 应为 **V0.2.167**。
   已确认 V0.2.167 的二进制里生成器是修好的；用户日志大概率是**更新前的旧程序**跑出来的
   （修复落地 19:44、打包 20:16，日志 21:58 仍在报 64 行，形状=旧生成器）。
3. 拿到 V0.2.167 后**直接再点一次「一键构建 + 安装」**即可：每次生成都会重写 `cs\PowerRemoval.cs`，
   而且这一步会「已清掉上一次生成的工程」，旧坏文件不会残留，**不需要手动删文件**。
4. 旧程序临时兜底：手改 `cs\PowerRemoval.cs`，**删掉最后一行那个孤立的 `}`**（改完 63 行）再 build。
5. 修好这条之后，用户还会撞到（都已确认，不是新 bug）：
   - 前一轮失败是**配置校验**挡下的：卡牌「幻觉」的「复制卡牌」张数填了 `0`，合法 1~5，工具会中止生成 → 要让用户改存档；
   - 日志里成批「自定义状态 X 里用了『玩家侧』的东西」是**警告**：挂到敌人身上会运行时异常/卡战斗，
     建议把「作用对象」改成「自己」或去掉玩家侧效果（抽牌 / 能量 / 金币 / 生成卡牌…）；
   - 同一批修复还含：同一钩子多个时机只生成一个 override（CS0111）、不带上下文的钩子上自造
     `choiceContext`（CS0103）、生成卡牌目标卡不存在提前报错（CS0246）、「获得时」+打敌人拦住（开局 NRE）、
     纯小写类名 CS8981 提示、输出目录在游戏 `mods\` 里警告、`sts2.dll` 签名漂移检测、
     锻造图「逐个敌人」开调试日志时 `__fgFoe` 作用域。

## 六、还没做 / 待决策

- **这条修复尚未 commit**（`CSharpCodeGen.cs` 仍是 `M`）。要不要提交、要不要再打一个含 21:57 之后
  工作区改动的包（V0.2.168），由用户点头；本会话被明确要求「只给其他会话一份说明」，没有动 git。
- 本次为查证在 `D:\ds\s\_tmp\braceprobe\` 留了东西（可直接删）：`run.ps1`/`run2.ps1`（PS 5.1 反射探测、
  因 net9 加载失败而作废）、`fixcheck\`（源构建版 Core 替换副本）、`fixcheck_ship\`（已发版 Core 替换副本，
  就是「已发版也带修复」那条证据）、`remprobe_out.cs`、`uicheck.txt`（空）。
- 结构上还有个缺口（没做）：`dotnet build` 之前**没有**「生成出来的 C# 花括号配平」的预检，
  所以这类 bug 只能靠用户日志发现。若要收口，可在生成后加一条全量 `{`/`}` 配平自检，
  比逐文件加断言更省事（`docs\QUEUE.md` 的红线风格也偏好这种「谁都能跑」的自检）。
