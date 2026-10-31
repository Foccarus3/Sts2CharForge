# V0.2.351 — 撞名的延迟状态，改成「构建之前」就报出来

## 你这次贴的报错，其实是同一个坑的**旧版本**结果

`LunaForgeDelayedTempPowerSoarPowerSelf` 又写了两遍（CS0101 + CS0111）。用你附的这份 `LunaMod.json` 实测：

里面有 **4 条**「下回合生效的临时增益（SoarPower）」——
3 条是「下回合才开始撤」、1 条是默认「本回合结束撤」。

* **V0.2.350 之前**：这两族算出的类名一模一样 → `DelayedPowers.cs` 里同一个类写两遍 → 你现在看到的报错。
* **V0.2.350 之后**：类名分别是 `…SoarPowerSelf` / `…SoarPowerSelfNextTurn`。我拿你的存档跑了一遍，输出是 **3 个互不相同的类**：
  `LunaForgeDelayedSoarPower`、`LunaForgeDelayedTempPowerSoarPowerSelfNextTurn`、`LunaForgeDelayedTempPowerSoarPowerSelf`。

⇒ 那次构建用的还是**旧版工具**。请先看一眼窗口标题栏的版本号：不是 `V0.2.350` 以上就会继续报这个错。
确认版本后重新「一键生成 · 构建 · 安装」即可（也可以打开 `…\LunaMod\cs\DelayedPowers.cs` 搜一下有没有 `SelfNextTurn`——
有就说明是新版，没有就是旧版）。

## 这一版加的保护

以前这类问题只能等 `dotnet build` 报 CS0101 才知道。现在**构建之前**校验器就会报：

> 有两条「下回合生效」的效果会算出**同一个延迟状态类**「…」：一条是「临时增益 / 临时减益」（状态 X、数值 2、对象 Self、下回合才撤 否）。
> 生成时**只会保留一条**（另一条被跳过）——想让两条都生效，就改成不同的「数值正负 / 什么时候撤 / 作用对象」，或者把其中一条的「下回合生效」关掉。

实现：`ProfileValidator.ValidateDelayedClassCollisions`（按 `CollectDelayedEffects` 分组，同一类名出现两次就报「提示」）。

## 自检

**4638 PASS / 0 FAIL**（+2 条：撞名会报提示 / V0.2.350 修过的「只差什么时候撤」不再报）。
