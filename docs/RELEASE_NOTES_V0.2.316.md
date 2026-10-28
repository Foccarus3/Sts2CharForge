# V0.2.316 — 插件补齐：触发时机 / 条件 / 锻造模块 都接进生成端了

上一版（V0.2.315）只接了「效果种类」，这三类当时只登记不生效。这一版按同样的「数据 + 模板」思路补齐，
**插件不需要写 DLL、也不依赖编辑器内部类型**，所以升级编辑器照样能用。

## 1. 触发时机 = 本体已有钩子的别名 + 一句额外守卫

```jsonc
"triggers": [
  { "id": "PluginEnemyHurt", "display": "插件示例：敌人受伤后",
    "hook": "EnemyDamaged",                                        // 必须是编辑器已知的钩子
    "guard": "if (target is null || target.IsPlayer) return;" }     // 可空；能用的变量 = 该钩子签名里的参数
]
```

* 生成时把这条时机换成本体那个钩子（`AfterDamageReceived(...)`），并在效果体第一行落下你的守卫；
* 状态链 / 遗物链都接了（遗物多时机那条路也接了：插件时机先换成本体钩子的副本，守卫单独记着落）；
* 下拉里能选到（状态页 / 遗物页），`Hook` 写错会被拒绝加载并写明原因；
* `hook` 只能是本体已有的钩子 —— 这是「编辑器升级不失效」的代价：插件不新增钩子，只改守卫 / 名字。

## 2. 条件 = 一段布尔表达式

```jsonc
"conditions": [ { "id": "PluginHasBlock", "display": "插件示例：自己有格挡", "expr": "{self}.Block > 0" } ]
```

* 卡牌 / 遗物 / 药水 / 状态四条链通用，出现在条件下拉里；
* 表达式里可用 `{self}` `{player}` `{foe}` `{amount}` `{power}`：
  * `{self}`：卡牌 / 遗物 / 药水 = `base.Owner.Creature`，状态 = `base.Owner`；
  * `{player}`：卡牌 / 遗物 / 药水 = `base.Owner`，状态 = `base.Owner.Player`；
  * `{foe}`：卡牌 = `cardPlay.Target`，遗物 = 第一个可打的敌人，状态 = 触发器的目标 / 第一个敌人。

## 3. 锻造模块 = 图上的【效果】节点 + 一段模板

```jsonc
"forgeModules": [ { "kind": "PluginForgeHeal", "display": "插件示例模块：回复生命", "emit": "await CreatureCmd.Heal({self}, {amount});" } ]
```

* 模块登记后**同时是一个效果种类**：所以卡牌页的效果下拉里有它，锻造图的【效果】节点里也能选它；
* 图上渲染时 `{self}` = `__fgSelf`、`{player}` = `__fgPlayer`、`{amount}` = 图上算出来的数值，
  和「效果种类」完全同一套模板（不需要为图单独写一份）。

## 验收

* 自检：**4525 PASS / 0 FAIL**（+7 条 V0.2.316 断言：三类都能登记 / 进下拉 / 状态链生成宿主钩子+守卫 /
  遗物链同上 / 卡牌链条件表达式 / 图上插件模块生成）。
* 探针 `sacprobe pluginfx`（真读 `程序目录\plugins` 的示例插件）：效果 + 时机 + 条件 + 模块四种一起上，
  整份工程 **`dotnet build` 0 错误**，插件自带 `cs` 也进了工程。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）——
  插件相关分支在没有插件时一行都不多写。
* 只出更新包（`-NoBundle`）。

## 附：示例插件的 v1.1

`plugins\示例插件` 现在同时演示四样：效果种类、触发时机（挂 `EnemyDamaged` + 守卫）、条件、锻造模块，
外加 `cs\PluginDemo.cs`（随模组一起编译）。删掉整个文件夹就恢复成「没有插件」。
