# V0.2.197 —— 数值公式补齐了 .NET 的 Math 那一套（常量、幂根对数、三角、取整…）

> 用户原话：贴了一张 .NET `Math` 成员表（`Math.PI` / `Math.Pow` / `Math.Sqrt` / `Math.Clamp` /
> `Math.Round(x, AwayFromZero)` / 三角 / 双曲 / `double.IsNaN` …）+ 一句「这些能用吗」。

## 1. 现在能用的（照 C# 写法直接写就行）

**常量**：`Math.PI`、`Math.E`、`Math.Tau`（`MathF.PI` / `MathF.E` 也认，值是同一个 —— 本工具是 decimal 运算，
没有单精度这一说）。

**函数**：

| 分类 | 能写 | 怎么生成 |
|---|---|---|
| 基础 | `min` `max` `abs` `sign` `clamp` | 直接调 `System.Math.Xxx`（**decimal 重载**，最精确） |
| 取整 | `floor` `ceiling`(=`ceil`) `round` `round(x, 位数)` `truncate`(=`trunc`) | 同上（`round` 默认银行家舍入，和 C# 一致） |
| 传统四舍五入 | `Math.Round(x, AwayFromZero)` 或 `roundaway(x[, 位数])` | 拼成 `x>=0 ? floor(x*p+0.5) : ceiling(x*p-0.5)`（2.5 → 3） |
| 幂 / 根 / 对数 | `pow` `sqrt` `cbrt` `exp` `log` `log(x,底)` `log10` `log2` | `(decimal)System.Math.Xxx((double)(…))`（这几个 .NET 只有 double 重载） |
| 三角 | `sin` `cos` `tan` `asin` `acos` `atan` `atan2` | 同上（参数/结果是弧度；只有 double 重载） |
| 双曲 | `sinh` `cosh` `tanh` `asinh` `acosh` `atanh` | 同上 |
| 特殊 | `maxmagnitude` `minmagnitude` `copysign` `ieeeremainder` | 前三个按 .NET 语义拼表达式，`IEEERemainder` 走 double |
| 角度 | `Math.DegreesToRadians(x)` `Math.RadiansToDegrees(x)` | 拼成 `x*π/180` / `x*180/π`（.NET 9 的 `Math` 上没有这两个静态方法） |
| 判断 | `double.IsNaN(x)` `IsInfinity` `IsPositiveInfinity` `IsNegativeInfinity` `IsFinite` | 本工具的数值是 decimal：**没有 NaN 也没有无穷**，所以 IsNaN 那几个恒为假、IsFinite 恒为真（生成 `false` / `true`） |
| 转换 | `Convert.ToInt32(条件)` | 布尔按真 1 / 假 0，数值取整 |

**常量折叠**：全是常数时照旧在生成期算好（`Math.Pow(2,3)+Math.Sqrt(16)+Math.Cbrt(27)` → 15），
所以「卡牌费用 / 升级后费用」这种**要编译期常量**的地方也能用这些函数。

## 2. 用不了 / 不适用的（会给出中文原因，不丢给 dotnet）

* `Math.DivRem(a, b, out r)` —— **out 参数**在公式（一个表达式）里没有位置；用 `truncate(a/b)` 取商、`a % b` 取余；
* `Math.BigMul` —— decimal 没有溢出这回事，用不上；
* `Math.ScaleB` / `Math.FusedMultiplyAdd` —— 浮点位运算专用，decimal 没有对应语义；
* `MathF.PI` 的**单精度值** —— 认这个写法，但取的是 decimal 精度的 π（不是 3.1415927f）；
* `double.NaN` / `double.PositiveInfinity` 这类**字面量** —— decimal 里不存在，写不出来（也没意义）。

## 3. 验过的

* 用户表里能用的每一条都写进探针逐条跑「解析 / 生成 C# / 常量折叠」（`_tmp\fmprobe`，25 条 + 2 条故意写错的）；
* 五处（卡牌 / 遗物 / 药水 / 自定义状态 / 锻造图数值）各写一条带 `Math.` 的公式 → 整份工程 `dotnet build`
  **0 error**（顺带夹掉一个 `roundaway` 括号不配的 bug —— 生成的算式少一层括号，编译不出）；
* **老存档生成结果一个字节都没变**：示例配置 113 个文件、WhiteCloak 存档 412 个文件，只差 `.sln` 的随机 GUID；
  公式的旧写法（四则运算、`min/max/floor/ceil/round/abs`）生成的文本逐字不变（自检里钉了 `2*3 → ((2m) * (3m))`）；
* 自检：本次新增 **9 条**断言（常量 / decimal 直调 / double 转手 / 三角双曲 / 传统四舍五入 / 拼出来的那几个 /
  判断族 / 常量折叠 / 中文报错），整轮 `Sts2CharForge.exe --uicheck` **3929 PASS / 0 FAIL**。
