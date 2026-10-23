# V0.2.217 —— 打出去「牌直接卡住」：`ThrowIfNull(cardPlay.Target)` 和 `TargetType` 两套判据打架

> 来源：用户实测（附 `godot2026-10-17T19.35.16.log`）：
> 「目标为全体敌人时…给灾厄的时候牌直接卡住了；把给灾厄的目标设成单个敌人就不卡了，能触发大限已至，但灾厄没被给予」。
>
> 日志里那一句就是答案：
> ```
> [ERROR] System.ArgumentNullException: Value cannot be null. (Parameter 'cardPlay.Target')
>    at Examplemod.NecrassCard20.OnPlay(...)
> ```

## 1. 病因：两份判据各写一份，而且过滤条件还不一样

- `ResolveTargetType`（决定这张牌的 `TargetType`）会**跳过「数值 0 且升级也不加数值」的效果**
  （那几条整条不生成）→ 于是「全体敌人伙伴伤害 + 施加 0 层灾厄」这种牌被推成 `TargetType.AllEnemies`
  （游戏里**不弹选目标**）；
- `NeedsTargetNullCheck`（决定要不要在 `OnPlay` 开头写 `ThrowIfNull(cardPlay.Target)`）**没做这个过滤** →
  看见那条「施加 0 层灾厄（指定敌人）」就写了判空。

两边一撞：卡牌不弹选目标 → 打出时 `cardPlay.Target` 是 null → 判空抛异常 → 这一回合的回合循环被打断
（游戏里就是「牌卡住、动不了」）。

## 2. 修法：两份判据合并成一份事实

新增 `CSharpCodeGen.HitsEnemyKind(效果)` / `NeedsChosenTarget(效果)`：

```csharp
// 「要对某个单位动手」的种类（含大限已至 / 翻倍 / 暴露 / 清除状态 / 禁止格挡 / 禁止回血 / 伙伴攻击…）
internal static bool HitsEnemyKind(EffectSpec e) => …
// 真的会读 cardPlay.Target：指定单体 / 指定队友 + 代码真的会生成（数值 0 的不算）
public static bool NeedsChosenTarget(EffectSpec e) =>
    !IsInertZero(e) && e.TargetSide is "Enemy" or "Ally" && HitsEnemyKind(e);
```

`ResolveTargetType` 与 `NeedsTargetNullCheck` 现在都走这一份 → **不可能再出现「不弹选目标却判空」**：
只要某条效果要判空，它的 `TargetType` 就一定是 `AnyEnemy` / `AnyAlly`（自检里有一条不变量把这些组合全跑一遍）。

顺带：判空覆盖的种类补齐了（`大限已至` / `翻倍状态层数` / `暴露` / `清除状态` / `禁止获得格挡` / `禁止回复生命`）——
以前这几种在「指定敌人」上**没有**目标判空；你那份 `AinodiMod.json` 里 `AinodiCard63` 会因此多出这一行
（`TargetType` 本来就是 AnyEnemy，多这一行是往正确方向变）。

## 3. 「灾厄没被给予」这一半

那是另一件事：那条「施加状态（灾厄）」的**数值是 0** —— 0 层等于什么都不做，而且生成器把它**整条**丢掉了
（配置检查里有一条提示：「数值是 0、升级也不加数值：不显示、也不会执行」）。
把「数值」填成 ≥ 1（例：2）就会真的挂上灾厄，「大限已至」也就有层可算了。

> 所以：以前「全体敌人 + 0 层灾厄」会让牌卡住；现在不会卡了（那条 0 层的效果本来就该被忽略），
> 但要让灾厄真的挂上去，还是得把数值填上。

## 4. 验收

- 探针（伙伴伤害 3×6 打全体敌人 + 施加 0 层灾厄）：`TargetType=AllEnemies`、**没有** `ThrowIfNull`；
  同一张牌把灾厄填成 2 层 → `TargetType=AnyEnemy`、判空在；
- 不变量自检：12 种「会对单位动手」的效果 × 敌/友 × 数值 0/2 全跑一遍，
  「要判空」⇒ `TargetType` 必为 AnyEnemy/AnyAlly；
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（只差 `.sln` 的 GUID）；
  你那份 `lihuazou.json` 也 0 差异（只有 `.sln`）；`AinodiMod.json` 只有 `AinodiCard63` 多一行判空（见上）；
- `--uicheck`：**4052 PASS / 0 FAIL**（新增 3 条：0 层不再判空 / 对照 2 层照旧 / 不变量）。

## 5. 版本

- `V0.2.217`（用这一版重新生成一次工程；存档里把「灾厄」那条的数值填上即可）
