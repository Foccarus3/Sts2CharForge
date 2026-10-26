# V0.2.292 — 百分比状态：类没生成（写在 if 里）+ 描述占位符对不上

## 你贴的错（新的一轮）

```
SparkleCard43.cs(8,34):  error CS0246: 未能找到类型 SparklePercentDamageUpPower
SparkleCard43.cs(18,64): error CS0246: 同上
SparkleCard43.cs(28,34): error CS0246: 同上
```

名字这次是**统一**的了（三处都叫 `SparklePercentDamageUpPower`），但**那个类文件没被写出来** —— 全工程 `cs/Powers/` 里搜不到 `*Percent*`。

## 真因

V0.2.289 我加「写四个百分比类」那四行时，插错了位置：它被塞进了

```csharp
if (CSharpCodeGen.UsesExtraTurn(profile))
{
    ProjectFilesGen.WriteText(... ExtraTurnPower.cs ...);
    foreach (string pct in PercentPowers.UsedIn(profile))   // ← 我的代码在这里
        ...
}
```

你这个存档**没用「额外获得一回合」**，所以整块 `if` 不执行 → 四个类一个都没写。
（也就是说这不是「名字映射」的问题，是「写文件那一步被条件挡住了」。）

## 修法

把那段**移出 if 块**，无条件执行：

```csharp
// 四个「百分比」状态的类 —— 存档里用到哪个就写哪个（必须无条件写，不能挂在别的 if 里）
foreach (string pct in PercentPowers.UsedIn(profile))
    ProjectFilesGen.WriteText(Path.Combine(cs, "Powers", PercentPowers.ClassOf(pct, profile) + ".cs"),
        PercentPowers.Source(profile, pct));
```

## 顺手修掉的两个连带问题

1. **描述里的占位符**：PowerId 解析成类名后，卡面描述里写着的 `{PercentDamageUp:diff()}` 就对不上了
   （校验器报「用了这张牌没有声明过的变量」）。现在读档时**把描述里的占位符一起改名** ——
   `{PercentDamageUp:…}` → `{SparklePercentDamageUp:…}`（= 类名去掉结尾 `Power`，和 `PowerVar<T>` 的键一致）。
2. **空描述导致的崩溃**：改名函数没判 null，自检里 `Load` 一个描述为空的存档时 `NullReferenceException`
   （自检只跑了 1742 行就崩了）。已加 null 守卫。

## 验收

* 自检：**4453 PASS / 0 FAIL**（跑满 4679 行，无异常）
* 用你的 `SparkleMod.json` 验证：`UsedIn()` 能列出 `PercentDamageUp`；
  「新卡」那条效果解析后是 `PowerId=SparklePercentDamageUpPower`
* 只出更新包（`-NoBundle`）

## 你要做的

换上 V0.2.292 → 重新点「一键构建 + 安装」。
这次 `cs/Powers/SparklePercentDamageUpPower.cs` 会真的写出来（你可以在生成后去那个目录确认一下）。
