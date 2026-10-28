# V0.2.309 — 锻造图里的「即死」接上了（卡牌「监视」生成被拦的那条）

用户截图：

> 卡牌「监视」（升级后「先见」）第 1 张的「即死（直接杀死目标）」【节点图】里还没接上…生成被拦住了

## 真因

「即死」这条效果在**卡牌 / 遗物 / 药水**的效果列表里一直是支持的（生成 `CreatureCmd.Kill`），
但**锻造节点图**那份支持名单（`ForgeGraphGen.SupportedEffectKinds`）漏了它 → 校验器按规矩报
「节点图里还没接上（选了不会做任何事）」把整张卡拦住。

## 改法

* `SupportedEffectKinds` 加上 `InstantKill`；
* 图的【效果】分支补上实现（和卡牌 / 遗物 / 药水链同一句）：

```csharp
foreach (Creature __fgFoe in ForgeFormulaRuntime.TargetOrFirst(__fgTarget, __fgPlayer))
    await CreatureCmd.Kill(__fgFoe);
```

打谁由【对象】模块决定：接「指定单体敌人」就是**打出时选中的那个敌人**；不接就默认「可打的第一个敌人」；
接「全部敌人」就是全杀。

## 验收

* 探针 `sacprobe graphkill`（锤造卡 + 图：触发时机 →【效果】即死）：
  图问题 **0 条**、生成 `await CreatureCmd.Kill(__fgFoe)`、整份工程 **`dotnet build` 0 错误**。
* 自检：**4498 PASS / 0 FAIL**（+2 条 V0.2.309 断言：支持名单里有它 / 生成 `CreatureCmd.Kill` + 目标表达式）。
* 只出更新包（`-NoBundle`）。

## 注意

「即死」对**有死亡抗性 / 会复活的**目标可能不生效（本体 `CreatureCmd.Kill` 自己会判），
小怪 / 复活类被打不死时属于本体机制，不是这里没生成。
