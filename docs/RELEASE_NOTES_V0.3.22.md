# V0.3.22 — 触媒 / 间色：触发次数累加、5+5 再扣层、毒死后卡回合

用 `DarkVinci (1).json` + `godot (51).log` 复现后修的三个问题（都出在「间色：绿」+「渲染」这条线上）。

## 一、触发次数每回合累加

**真因**：`渲染` 每回合给敌人挂 1 层 `渲染 `（`EnemyRendering`，`Single = true`），
而公式里的 `W = PowerAmount(..., "EnemyRendering")` 读的是 `Power.Amount`。
本体的 `Single` 只影响数字显示，`PowerCmd.Apply` 仍会把 `Amount` 累加（1 → 2 → 3…），
于是 `1+W` 每回合越滚越多。

**修法**：生成的 `ForgeFormulaRuntime.PowerAmount` 对 `PowerStackType.Single` 一律返回 `1m`。
`W` 恒为 1，`1+W` 恒为 2，不再逐回合涨。

## 二、5 层毒结算成 5+5 再扣 2 层（不是 5+4）

**真因**：`间色：绿` 的触发里有两条效果（扣血、扣 1 层），两条都写了同一个「生效次数公式 `1+W`」。
生成器以前是**每条效果各自循环**：先把扣血循环完（5+5），再循环扣层（-2），所以层数从 5 直接到 3。

**修法**：同一触发时机里**连续、次数公式相同、无条件、无概率**的效果，合并成一个循环，
按次数**逐次交替执行**：

```
第 1 次：扣 5 血 → 层数 -1（到 4）
第 2 次：扣 4 血 → 层数 -1（到 3）
```

生成代码长这样：

```csharp
for (int __repSeq = 0; __repSeq < (int)Math.Clamp(1 + W, 1m, 20m); __repSeq++) {
    await CreatureCmd.Damage(choiceContext, base.Owner, base.Amount, ...);
    await PowerCmd.ModifyAmount(choiceContext, base.Owner.GetPower<SecondaryGreen>(), -1m, ...);
}
```

（老存档里没有这种「连续多条 + 同一公式」的形状时，输出不变。）

## 三、怪被毒死之后卡结算 / 结束不了回合

**真因**：日志里 `Combat #1 turn loop died ... NullReferenceException at DarkVinci.SecondaryGreen.AfterSideTurnStart`。
扣血把怪打死之后，`base.Owner.CombatState` 变成 `null`，第二条「扣层」还去访问
`base.Owner.CombatState.HittableEnemies` → NRE，回合循环直接死掉。

**修法**：

* 合并循环里每次迭代先判断 `if (!base.Owner.IsAlive || base.Owner.CombatState is null) break;`；
* 每条效果外面再包一层 `if (base.Owner.IsAlive && base.Owner.CombatState is not null)`；
* 取敌人的地方改成 `base.Owner.CombatState?.HittableEnemies.FirstOrDefault()`（空安全）。

## 四、顺手修正：扣自己层数不再扣错人

`间色：绿` 的「扣 1 层」在存档里写的是「作用对象 = 单体敌人」。对**挂在敌人身上的状态**来说，
「自己」应该是 `base.Owner`，而不是「玩家视角的第一个敌人」——多敌人时会扣错。
现在：如果一条效果扣的正是**当前这个状态**的层数（`PowerId == 当前状态类名` 且数值为负）、
作用对象又是「单体敌人」，生成端自动按 `Self`（`base.Owner`）处理。

## 五、怎么用上

用 V0.3.22 **重新生成** DarkVinci 工程（旧 `.cs` 里还是老代码），再进游戏测：
`间色：绿` 的伤害应该是 5→4→3…，`渲染` 的额外触发次数不再每回合增长，毒死最后一只怪也能正常结束回合。

## 六、验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4857 PASS / 0 FAIL**（+3 条 V0.3.22 断言） |
| 部署版 `--uicheck` | **4856 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 逐次结算 + Single 状态 + 空安全，**0 错误** |
| 真实存档 | 用 `DarkVinci (1).json` 重新生成 `SecondaryGreen`，已确认循环 / 守卫 / 目标都正确 |
