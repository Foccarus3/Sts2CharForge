# V0.2.209 —— 日志里那两条：遗物描述里的 `{VulnerablePower}` 格式化报错 + `FORGE_TURN_END_PLAY_POWER.title` 缺键

> 来源：用户贴的 `godot (23).log` + `AinodiMod.json`（545 KB，直接用这份存档复现并验证）。
>
> ```
> [ERROR] Localization formatting error! message=Error parsing format string:
>         No source extension could handle the selector named "VulnerablePower" at 9
>         table=relics key=AINODI_RELIC2.description
> [WARN] [BaseLib] GetRawText: Key 'AINODI_FORGE_TURN_END_PLAY_POWER.title' not found in table 'powers'
> ```

## 1. 遗物描述写 `{XxxPower:diff()}`，但那件遗物**没有动态变量**

`AINODI_RELIC2` 是「锻造图说了算」的遗物 —— 它的**效果全在节点图上**，所以生成出来的源码是：

```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => [ ];   // ← 空
```

而描述那边照旧用了卡牌那套 `{变量名:diff()}` 占位符：

```json
"AINODI_RELIC2.description": "对所有敌人施加 {VulnerablePower:diff()} 层易伤。…"
```

游戏格式化遗物 hover 文本时会把 `DynamicVars` 灌进 SmartFormat，`VulnerablePower` 这个变量根本不存在 →
`No source extension could handle the selector` （SmartFormat 的 `:diff()` 只对认识的变量生效）。

**修法**：`LocalizationGen.DescribeEffect` 新增 `literalAmount`；主人是「锻造图说了算」的遗物时，
描述里的数值一律写**数字字面量**（和「锻造卡不声明动态变量」那条老规矩同一个道理）。现在生成：

```json
"AINODI_RELIC2.description": "对所有敌人施加 1 层易伤。\n对所有敌人施加 1 层虚弱。\n给予所有敌人 1 层[gold]中毒[/gold]，并立即触发[gold]中毒[/gold]。"
```

普通遗物（有自己的 `PowerVar<T>`）照旧用 `{…:diff()}`（升级后游戏会自动显示升级值），对拍也证明没变。

## 2. 「回合结束时自动打出」的辅助 Power 没有标题

`<角色>ForgeTurnEndPlayPower` 挂在玩家身上、界面上不显示图标，但游戏 / BaseLib 仍会去 `powers` 表查 title，
以前 `PowersJson` 里根本**没写**这一条 → 每次都要刷一条 `Key not found` 警告。

**修法**：`PowersJson` 补上这些「辅助 Power」的 title / description / smartDescription，**用到哪个才写哪个**：

| 辅助 Power | 什么时候写 |
| --- | --- |
| `<角色>ForgeTurnEndPlayPower`（回合结束自动打出） | 存档里用到「回合结束时自动打出」 |
| `<角色>ForgeTempKeywordPower`（临时关键词） | 用到「临时关键词」 |
| `<角色>ForgeTempUpgradePower`（临时升级） | 用到「仅本回合升级」 |
| `<角色>ForgeNoHealPower`（禁止回复生命） | 用到「禁止回复生命」 |
| `<角色>ForgeDrawCostPower`（抽到就减费） | 用到「抽到就减费」 |
| `<角色>ForgeStarCapPower`（额外资源量上限） | 用到「提高额外资源量上限」 |
| `<角色>ForgeEnergyNextTurnDebtPower`（透支能量） | 用到「透支能量」 |

没用到的存档一个字符都不会多（对拍已证）。

## 3. 验收（用你这份 AinodiMod.json 真跑的）

- 生成成功；`AINODI_RELIC2.description` 变成字面量，不再有 `:diff()`；
  `AINODI_FORGE_TURN_END_PLAY_POWER.title/.description/.smartDescription` 三条都在；
- 生成工程 `dotnet build` **0 error**；
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**（这两份没用到锻造图遗物 / 辅助 Power）；
- `--uicheck`：**3989 PASS / 0 FAIL**（新增 4 条：锻造图遗物写字面量、普通遗物照旧占位、
  辅助 Power 用到才写、没用到不写）。

## 4. 版本

- `V0.2.209`（用这一版重新生成一次工程即可，存档不用改）
