# V0.2.403 — 修「百分比易伤清除状态去不掉」（清除所有减益）

## 根因

你那张「真正的救赎」给敌人挂的是**百分比易伤**（`PercentVuln`，生成成 `<角色>PercentVulnPower`），
而 `CarmenFearThouNot` 这类牌的「清除所有减益」在运行期是这样判的：

```csharp
if (DebuffPowerNames.Contains(power.GetType().Name)) await PowerCmd.Remove(power);
```

`DebuffPowerNames` 是**生成期写死**的一份清单，原来只来自 `EffectCatalog.Debuffs`（本体那批减益）。
我们自己生成的百分比状态**不在清单里** → 类名对不上 → 永远清不掉。

（V0.2.36x 修的是「清除指定状态」那条：它已经会把 `PercentVuln` 归一成本角色的类名；
这次是「清除**所有**减益」那条，漏了。）

## 改法

`ForgeGraphGen.DebuffNameLines(p)` 现在会把**本角色生成出来的、Type=Debuff 的两个百分比状态**
一并写进那份清单：

* `PercentVuln` → `<角色>PercentVulnPower`
* `PercentTakenReduce` → `<角色>PercentTakenReducePower`

（`PercentDamageUp` / `PercentDamageReduce` 是 Buff，**故意不加**——「清除所有减益」不该清掉增益。）

## 自检

**4790 PASS / 0 FAIL**（+1 条：运行时清单里确实含 `SevenPercentVulnPower` / `SevenPercentTakenReducePower`——
样例角色的前缀是 `Seven`）。

## 你需要做的

更新到 **V0.2.403** → 重新生成 → 构建安装。再打那张「清除所有减益」的牌，百分比易伤应该会被一起清掉。

如果还有清不掉的，请把那张牌的**效果种类**（是「清除指定状态」还是「清除所有减益」）和目标的**状态名**告诉我，
我按那条链路再查（现在两条链我都动过了，但只对「生成出来的百分比状态」这四种有把握）。
