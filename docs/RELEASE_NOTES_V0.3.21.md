# V0.3.21 — 单字母变量 `a` 被参数 `A` 抢走（伤害一直 0 的真因）

这一版修的是用户那张卡「伤害公式写 `a`、变量 `a` +1，但伤害一直 0」的**真正原因**。
（上一版 V0.3.20 只看到截图里的「+0」，那只是第一层；把值改回 1 之后仍然 0，是因为名字解析错了。）

## 一、真因：解析器把单字母 `a` 改成了 `A`

`ForgeFormula` 的解析器以前对**单字母**标识符统一 `ToUpperInvariant()`：

```
公式 a → VarNode("A")
```

于是 `CardResolver` 拿到的是 `A`，按顺序找到的是**自定义参数 A**（你的「自己的破绽层数」），
生成的伤害就是：

```csharp
decimal __pA = ForgeFormulaRuntime.PowerAmount(__fgSelf, "VulnerablePower");
await DamageCmd.Attack(__pA)
```

而不是：

```csharp
await DamageCmd.Attack(SparkleVars.a)
```

没有破绽层数时 `__pA` 就是 0 → 伤害永远 0。变量 `a` 其实一直在 +1（只是没人读它）。

## 二、修法

1. **解析器保留单字母的大小写**（X / Y 仍不区分大小写，因为它们不能用做变量名）：
   `a` → `VarNode("a")`，`A` → `VarNode("A")`。
2. **变量按精确大小写匹配**，**参数不区分大小写**（老习惯，公式里写 `m` 仍认参数 `M`）：
   * `CardResolver` / `InlineResolver`：先按精确名字找变量，再按忽略大小写找参数；
   * `ForgeVarsGen.ResolveConst`、`ForgeNumbers.ConstParam` 同步；
   * 校验器把「变量名（区分大小写）」和「参数名（不区分大小写）」拆成两份，不再混成一个集合。
3. **卡牌公式序幕**（`EmitCardFormulaPrologue`）：公式里出现的是**精确同名的变量**时，不再为它声明同名参数；
   小写 `m` 仍然会正确声明 `__pM`。
4. 锻造图【数值】/【效果】的公式校验也补上变量（以前只认参数，变量会被误报「没有这个参数」）。

## 三、实测生成结果（修后）

同一张卡（变量 `a` + 参数 `A`，公式都写 `a`）：

```csharp
new ForgeFormulaDamageVar(static (card, target) => SevenVars.a),
await DamageCmd.Attack(SevenVars.a)
SevenVars.a += (int)(1m);
```

小写参数 `m` 仍然正确：

```csharp
decimal __pM = 5m;   // M= 固定数值 5
await DamageCmd.Attack(__pM)
```

## 四、你那张卡的文件

生成出来的卡在：

```
<部署目录>\自定义角色存档\SparkleMod\cs\Cards\SparkleCard43.cs
```

本机就是 `D:\ds\s\4\自定义角色存档\SparkleMod\cs\Cards\SparkleCard43.cs`。
修之前里面确实是 `decimal __pA = … VulnerablePower` + `Attack(__pA)`；
用 V0.3.21 重新生成后会变成 `Attack(SparkleVars.a)`。

## 五、验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4854 PASS / 0 FAIL**（+4 条 V0.3.21 断言） |
| 部署版 `--uicheck` | **4853 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 变量 a + 参数 A、小写 m + 参数 M、四链变量写、锻造节点，**0 错误** |
