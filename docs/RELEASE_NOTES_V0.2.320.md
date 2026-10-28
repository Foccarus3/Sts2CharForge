# V0.2.320 — 变化卡牌「数值 0」：把这张牌自己变掉（以前整条被丢掉）

用户原话：

> 变化卡牌填0虽然不会像之前一样弹界面了，但是卡牌本体没有发生变化

## 真因

「变化卡牌」属于「**数值 = 力度**」那一类（`AmountIsStrength`），所以数值 0 会落进
`IsInertZero` → **整条效果被丢掉**：卡面不写、代码不生成。

* 之前会弹界面，是因为代码里还留着「按 1 张去变」的老路径（数值被 `Math.Max(1, 0)` 兜成 1）；
  V0.2.286 为了修「0 张会让选择框一直等 → 卡死」，在**锻造图**那条链上加了一条
  「取 0 张 → 整段跳过」—— 于是变成「不弹界面、也什么都不做」。
* 你存档里的两张卡（`CarmenCrimsonScar` 猩红创痕 / `CarmenCobaltScar` 郁蓝创痕）用的正是
  **锻造图**里的「变化卡牌 = 0 + 指定卡 = xxxAlteration」，所以踩到的就是这条。

## 改法

「变化卡牌」 数值 0 现在和其它「0 = 这张牌自己」的效果（消耗 / 丢弃 / 复制 / 删牌）**语义统一**：

```csharp
// 列表链 & 图链都生成同一句
CardModel __madeSelf = base.Owner.RunState.CreateCard(ModelDb.Card<目标卡>(), base.Owner);
await CardCmd.Transform(this, __madeSelf);
```

* 指定了「目标卡」→ 把这张牌变成那张；
* 「按范围随机 / 多选1 / 自己挑一张」→ 从范围池里随机（或按你选的那档）取一张当新牌；
* 「仅本回合升级 / 升级」这些附加处理照旧；
* 锻造图上不再被「取 0 张 → 跳过」拦住（那一档只留给 `TransformCardGlobal` / `UpgradeCard` / `Scry`）。

## 验收

* 你存档里的 **猩红创痕 / 郁蓝创痕** 现在生成：
  `CardModel __madeSelf = __fgPlayer.RunState.CreateCard(ModelDb.Card<CarmenCrimsonScarAlteration>(), __fgPlayer);`
  `await CardCmd.Transform(this, __madeSelf);`
* 探针 `sacprobe transform0`（列表链 + 图链各一张，数值 0 + 指定卡）：两条都生成 `Transform(this, __madeSelf)`，
  整份工程 **`dotnet build` 0 错误**。
* 自检：**4537 PASS / 0 FAIL**（+2 条 V0.2.320 断言；顺带把「0 一律不生效」那条清单里的 TransformCard 拿掉）。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）。
* 只出更新包（`-NoBundle`）。

## 说明

「数值 0 = 这张牌自己」只有在**卡牌**（含卡牌的锻造图）上成立；遗物 / 药水 / 自定义状态的布上没有
「这张牌」，那里填 0 会写一句注释提醒改成 ≥ 1 张（或改用「变化牌组（全局）」）。
