# V0.3.18 — 修两处：变量重置时机（起始遗物被抹掉）+ 临时增益的友方目标

这一版是 V0.3.17 之后的自查修复，两处都是「选了却没按选的作用对象 / 时机生效」的真 bug。

## 一、变量重置从 `RunManager.Launch` 提前到 SetUp 阶段

**问题**：本体新开一局的顺序是

```
SetUpNewSingleplayer / SetUpNewMultiplayer
  → FinalizeStartingRelics()      ← 起始遗物的「获得时」效果在这里跑
  → Launch()
```

V0.3.16 把 `ResetRun()` 挂在 `Launch` 的**后缀**上 —— 于是起始遗物「获得时 变量 +5」先生效、紧接着被重置抹成初值。
玩家看到的就是「起始遗物上写了加变量，进游戏却是初始值」。

**修法**：改成在四个入口的**最前面**复位（Harmony Prefix）：

| 入口 | 时机 |
|---|---|
| `RunManager.SetUpNewSingleplayer` | 新开一局（单机） |
| `RunManager.SetUpNewMultiplayer` | 新开一局（联机） |
| `RunManager.SetUpSavedSingleplayer` | 读档（单机） |
| `RunManager.SetUpSavedMultiplayer` | 读档（联机） |

这样复位发生在**起始遗物「获得时」之前**，遗物上的变量写操作不会再被抹掉。
读档也复位：变量是静态字段、本来就不进存档，不复位会拿到上一次进程里的残值。

生成文件仍是 `cs/<角色>VarsReset.cs`；`RunManager.Launch` 那条补丁已删。

## 二、临时增益的「全体友方 / 指定队友」

**问题**：临时增益（TempPower）在三条链上只写了「自己 / 全体敌人 / 随机敌人 / 全体单位 / 其余 = 第一个敌人」：

* **遗物链**：选「全体友方」或「指定队友」→ 掉进「第一个敌人」，给敌人上临时力量；
* **自定义状态链**：同样掉进「第一个敌人」；
* **药水链**：只写了「全体敌人 / 指定敌人 / 其余 = 自己」—— V0.3.6 加的「全体友方 / 指定队友」两档被当成自己。

**修法**：

* 遗物链：补 `AllAllies`（遍历 `CombatState.Allies` + `AllyLoopWhere` 过滤）和 `Ally/AnyAlly`（没有选中目标 → 按自己，和 V0.3.12 伤害那条同口径）；
* 状态链：补同样的两档（`base.Owner` 就是这只状态挂着的生物）；
* 药水链：直接复用 `EmitPotionPerCreature`（自己 / 指定敌人 / 全体敌人 / 全体友方 / 指定队友五档一份实现）。

## 三、自检 / 验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4840 PASS / 0 FAIL**（新增 5 条 V0.3.18 断言 + 1 条重置时机改写） |
| `--uicheck`（部署版） | **4839 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 四链变量 + 锻造节点 + 重置补丁 + 遗物 / 状态 / 药水的临时增益友方目标，**0 错误** |

## 四、还没做（如实）

* 变量在**多人联机**下仍是「各算各的」：静态字段不同步，等实测后再收口。
* 变量**不随存档保存**：读档会回到初值（这一版明确在 `SetUpSaved*` 复位，避免拿到上一次进程的残值）。
