# 给 AI 用的提示词：给 Sts2CharForge 写插件（V0.2.339）

> **怎么用**：把 `=== 提示词开始 ===` 到 `=== 提示词结束 ===` 之间的内容整段发给 AI（Claude / GPT / DeepSeek 都行），
> 在最末尾「我想做的插件」里写上你的需求，它就能按规范写出一个能直接用的插件。
> 里面用的都是**相对位置**（都以「程序目录」= 编辑器 exe 所在目录为基准），换台机器 / 换安装位置都不用改。

---

=== 提示词开始 ===

你是「Sts2CharForge（杀戮尖塔2 自定义角色生成器）」的插件作者。
这个编辑器用「**JSON 清单 + C# 模板**」扩展，**不加载 DLL**。请按下面的规范，给我写一个能直接用的插件。

## 0. 先做这三件事（顺序别换）

1. 完整读一遍 `程序目录\docs\插件开发规范.md` —— 那份是准的，本提示词是摘要 + 索引。
2. 打开 `程序目录\plugins\示例插件\plugin.json`（以及同目录的 `cs\PluginDemo.cs`），照着看形状。
3. 需要看「最终产物长什么样」时，用编辑器「构建 / 日志」页 →「打开输出目录」，
   进存档名那个文件夹：`cs\GlobalUsings.cs` 是生成代码能直接用的 using，`cs\Cards\*.cs` 就是模板被塞进去的那个方法体。

## 1. 文件位置（硬信息，先按这些相对位置找，找不到再问我）

以**程序目录**为基准（= `Sts2CharForge.exe` 所在目录，也就是启动器旁边的 `程序文件` 文件夹）：

| 要找的东西 | 位置 |
|---|---|
| 插件要放的地方（每个插件一个子文件夹） | `程序目录\plugins\` |
| **插件开发规范（完整字段 / 逐链占位符 / 页面框架 / 打包规则 / 兼容约定）** | `程序目录\docs\插件开发规范.md` |
| 可以照抄的示例插件（清单 + 自带 cs + 三种页） | `程序目录\plugins\示例插件\` |
| 生成的模组工程长什么样 | 编辑器「构建 / 日志」页 →「打开输出目录」→ 存档名那个文件夹 |
| 生成代码里能直接用的 using | 生成工程的 `cs\GlobalUsings.cs` |

按需再找：

* **本体（游戏）API 参考** —— 「路径」页里填的「本体工程目录」（解包后的原版工程，含 `src\Core\Commands\`），
  例如 `src\Core\Commands\CreatureCmd.cs`、`PowerCmd.cs`、`CardPileCmd.cs`。生成的代码只能调用这里的 API。
* **编辑器源码**（想看它怎么生成代码、自带 65 种效果各长什么样）—— 只有开发机上才有：
  `Sts2CharForge.Core\Generation\CSharpCodeGen.cs`（搜 `case "Damage"` 之类）、`CustomPowerGen.cs`、`ForgeGraphGen.cs`。
  你没有源码也能写插件：规范文档 + 示例插件够用。

## 2. 插件能做什么（五种扩展点）

| 扩展点 | 写在清单的 | 编辑器里出现在哪 |
|---|---|---|
| **效果种类** | `effects[]` | 卡牌 / 遗物 / 药水 / 自定义状态 / 锻造图的「效果种类」下拉 |
| **触发时机** | `triggers[]` | 「自定义状态」页和「遗物」页的「触发时机」下拉 |
| **条件** | `conditions[]` | 卡牌 / 遗物 / 药水 / 自定义状态的「条件」下拉 |
| **锻造模块** | `forgeModules[]` | 锻造图的【效果】节点（本质 = 【效果】节点 + 模板，同时也是效果种类） |
| **页面** | `pages[]` | ① 说明页 / ② 表单页（编辑器画控件、按模板生成文件）/ ③ `tab` = 挂到某个生成器页签上 |

自带文件：`csFiles[]`（跟 mod 一起编译）、`assetFiles[]`（跟 mod 一起打包）。

## 3. 最小可用清单

```jsonc
{
  "id": "myplugin",              // 必填：插件唯一标识（英文/数字/下划线，字母开头）
  "name": "我的插件",             // 显示名
  "version": "1.0",
  "apiVersion": "1",             // 必填：本编辑器只认 "1"
  "author": "你",
  "description": "一句话说明",
  "readme": "Markdown，编辑器「插件」页里显示",
  "effects": [
    {
      "kind": "MyHealAndBlock",                       // 必填：英文/数字/下划线，字母开头，不能和自带 65 种重名
      "display": "我的插件：回血并给等量格挡",           // 下拉里显示的名字
      "unit": "点", "min": 0, "max": 99,               // 数值框
      "needsTarget": false,                          // 要不要「作用对象」那一栏
      "needsPower": false,                           // 要不要「增益 / 减益」那一栏
      "needsCard": false,                            // 要不要「目标卡」那一栏
      "note": "说明（可选）",
      "emit": "await CreatureCmd.Heal({self}, {amount});\nawait CreatureCmd.GainBlock({self}, {amount}, ValueProp.Move, null);"
    }
  ]
}
```

## 4. `emit` 模板的硬规则（最容易错，先看这条）

1. `emit` 是**塞进方法体的 C# 语句**，不是完整类：**不写** `using` / `namespace` / `class`（要加类就放 `csFiles`）。
2. 只能调用**本体 API**（`CreatureCmd` / `PowerCmd` / `CardPileCmd` / `PlayerCmd` / `SfxCmd` …）或你自己
   `csFiles` 里的类。生成工程顶部已经 using 过常用命名空间（见 `cs\GlobalUsings.cs`）。
3. 占位符**大小写不敏感**，用 `{ }` 包起来，生成时替换（见第 5 节）。
4. 模板里的换行会原样写进生成代码 —— 多句就分行写（JSON 里写 `\n`）。
5. 每条效果执行一次；「生效次数 / 概率」这些通用栏位对插件效果**不生效**，「升级增量」也不显示（要自己判 `base.IsUpgraded`）。

## 5. 逐链占位符表（五条链各不相同，**按链写模板**）

| 占位符 | 卡牌链 | 遗物链 | 药水链 | 自定义状态链 | 锻造图 |
|---|---|---|---|---|---|
| `{amount}` | 数值框里的数字（`3m`）；卡牌上「数值 = X」时是那个 `x` 变量 | 数值框里的数字 | 同遗物 | 勾「数值 = 本状态层数」时 `(int)base.Amount`，否则数值框里的数字 | 图上【数值】入口算出来的（公式 → 局部变量） |
| `{amountFormula}` | 同 `{amount}`（更明确的名字） | 同 | 同 | 同 | 同 |
| `{self}` | `base.Owner.Creature` | `base.Owner.Creature` | `base.Owner.Creature` | `base.Owner` | `__fgSelf` |
| `{player}` | `base.Owner` | `base.Owner` | `base.Owner` | `base.Owner.Player` | `__fgPlayer` |
| `{foe}` / `{target}` / `{targetOrFirst}` | `cardPlay.Target` | 第一个可打的敌人 | 第一个可打的敌人 | 触发器目标 / 第一个敌人 | `TargetOrFirst(__fgTarget, __fgPlayer).FirstOrDefault()` |
| `{choice}` | `choiceContext` | 该钩子的 context（没有时 `null!`） | `choiceContext` | `choiceContext` | 该执行点的 context |
| `{cardSelf}` | `this`（这张牌） | `this`（遗物自己） | `this`（药水自己） | `null` | `this` |
| `{state}` | `this`（牌自己，别当状态用） | `this`（遗物） | `this`（药水） | `this`（**只有这条链有意义**） | `this` |
| `{combatState}` | `base.Owner.Creature.CombatState` | 同 | 同 | `base.Owner.CombatState` | `base.Owner.Creature.CombatState` |
| `{cardPlay}` | `cardPlay` | ✗ 用了编不过 | ✗ | ✗ | ✗ |
| `{power}` | 选的状态类名（**没选 = 空串 → 编不过，用之前必须让用户选**） | ✔ | ✔ | ✔ | ✔ |
| `{card}` | 「目标卡」的类名 | ✔ | ✔ | — | ✔ |
| `{spawnCardType}` | `ModelDb.Card<目标卡>()` 表达式 | ✔ | ✔ | ✔ | ✔ |

要点：

* **同一段模板跨链复用就要按链判断**（比如状态链上不能出现 `{cardPlay}`）。
* `{power}` / `{card}` / `{spawnCardType}` 只有在用户真的选了对应的那一栏才有值；
  模板里要用它们，就把 `needsPower` / `needsCard` 设成 `true`，并在 `note` 里写清「必须选」。
* `{foe}` 在遗物 / 药水 / 状态链上是「兜底第一个可打的敌人」，**不是**玩家刚才点的那个目标。

## 6. 数值 / 公式 / 对象 / 上下文

* **数值**：插件效果只认**数值框里的数字**。卡牌页「数值公式」那一栏对插件效果**只认光秃秃的 `X`**
  （=「数值 = X」，这时 `{amount}` 是 `x` 变量）；写别的公式会被校验器拦住 —— 要按公式算，请在模板里自己写表达式。
* **状态层数**：状态链勾「数值 = 本状态的层数」时 `{amount}` = `(int)base.Amount`。
* **对象**：`{self}` 自己、`{player}` 玩家、`{foe}` 指定敌人、`{cardSelf}` 这张牌、`{state}` 这个状态。
* **上下文**：`{combatState}` 战斗状态、`{cardPlay}` 本次出牌（只有卡牌链）、`{choice}` 出牌用的 `choiceContext`。

## 7. 变化这张牌：`{transformSelf:类名}` 宏

「把这张牌变成另一张」**别自己写** `CardCmd.Transform` —— 打出的牌此刻躺在「打出」那一摞里，
本体 Transform 会把新牌塞回同一摞、战斗结束就看不见（这个坑踩过）。写宏：

```jsonc
"emit": "{transformSelf:MyAlteredCard}"
```

展开成验证过的写法（1 张换 1 张：新牌进弃牌堆 + 把这张牌消耗掉）：

```csharp
CardModel __plugMade1 = base.Owner.Creature.CombatState.CreateCard(ModelDb.Card<MyAlteredCard>(), base.Owner);
await CardPileCmd.Add(__plugMade1, PileType.Discard);
if (this.Pile is not null) await CardCmd.Exhaust(choiceContext, this);
```

状态 / 遗物 / 药水链（以及不是卡牌的锻造图）没有「这张牌」，只做前半句（新牌进弃牌堆）——
那些链的 `this` 不是卡牌，别指望它 `Pile` 存在。一个模板里最多写 16 个。

## 8. 触发时机 / 条件 / 锻造模块

### 8.1 触发时机 = 「本体已有钩子 + 一句额外守卫」

```jsonc
{ "id": "MyEnemyHurt", "display": "我的插件：敌人受伤后", "hook": "EnemyDamaged",
  "guard": "if (target is null || target.IsPlayer) return;", "hasChoiceContext": true }
```

* `hook` 必须是编辑器**已知的钩子 id**（写错会被拒绝加载）。直接从「自定义状态」页或「遗物」页的
  「触发时机」下拉里抄英文 id。常用的一张表（`guard` 里能用的变量就是右侧签名里的参数）：

| hook | 显示名 | 钩子签名（`guard` 可用的变量） |
|---|---|---|
| `Obtained` | 获得时 | `AfterObtained()` |
| `CombatStart` | 战斗开始时 | `BeforeSideTurnStart(choiceContext, side, participants, combatState)` |
| `PlayerTurnStart` | 玩家回合开始时 | `AfterPlayerTurnStart(choiceContext, player)` |
| `PlayerTurnStartEarly` | 回合开始后（抽牌前） | `AfterPlayerTurnStartEarly(choiceContext, player)` |
| `PlayerTurnStartLate` | 回合开始后（最晚） | `AfterPlayerTurnStartLate(choiceContext, player)` |
| `PlayerTurnEnd` | 玩家回合结束时 | `AfterSideTurnEnd(choiceContext, side, participants)` |
| `EnemyTurnStart` | 敌人回合开始时 | `AfterSideTurnStart(side, participants, combatState)` |
| `EnemyTurnEnd` | 敌人回合结束时 | `BeforeSideTurnEnd(choiceContext, side, participants)` |
| `Shuffle` | 抽牌堆打乱洗牌时 | `AfterShuffle(choiceContext, shuffler)` |
| `DamageReceived` | 自己受到伤害后（每一下） | `AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource)` |
| `Attacked` | 自己受到攻击后（连击算一次） | `AfterAttack(choiceContext, command)` |
| `DamageDealt` | 自己造成伤害后 | `AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)` |
| `EnemyDamaged` | 敌人受到伤害后 | `AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource)` |
| `EnemyDealtDamage` | 敌人造成伤害后 | `AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)` |
| `HpLost` | 自己失去生命时（每一下） | `AfterCurrentHpChanged(creature, delta)` |
| `BlockGained` | 自己获得格挡后 | `AfterBlockGained(creature, amount, props, cardSource)` |
| `EnemyBlockGained` | 敌人获得格挡后 | `AfterBlockGained(creature, amount, props, cardSource)` |
| `BlockBroken` | 突破格挡时 | `AfterBlockBroken(choiceContext, target, breaker)` |
| `EnergySpent` | 花掉能量后 | `AfterEnergySpent(card, amount)` |
| `StarsSpent` | 花掉额外资源量后 | `AfterStarsSpent(amount, spender)` |
| `CardPlayed` | 打出一张牌时（还没生效） | `BeforeCardPlayed(cardPlay)` |
| `CardResolved` | 一张牌结算完毕时 | `AfterCardPlayedLate(choiceContext, cardPlay)` |
| `CardExhausted` | 消耗一张牌后 | `AfterCardExhausted(choiceContext, card, causedByEthereal)` |
| `CardDrawn` | 抽一张牌后 | `AfterCardDrawn(choiceContext, card, fromHandDraw)` |
| `CardDiscarded` | 弃掉一张牌后 | `AfterCardDiscarded(choiceContext, card)` |
| `CardGenerated` | 生成卡牌时 | `AfterCardGeneratedForCombat(card, creator)` |
| `EnemyDeath` | 一个敌人死亡后 | `AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength)` |
| `GoldGained` | 获得金币时 | `AfterGoldGained(player)` |
| `CombatVictory` | 战斗胜利后 | `AfterCombatVictory(room)` |
| `CombatEnd` | 战斗结束时（房间结算之前） | `AfterCombatEnd(room)` |
| `PowerReached` | 某个状态层数到达 N 层时 | `AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)` |
| `PowerChanged` | 某个状态层数变化后 | `AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)` |
| `PowerRemoved` | 某个状态被移除时 | 工具补丁（本体没有全局钩子） |

* `hasChoiceContext`：这个钩子签名里**有没有** `choiceContext`。没有就写 `false`
  （写错会让生成端不造 / 多造上下文，容易 CS0103）。

### 8.2 条件 = 一段布尔表达式

```jsonc
{ "id": "MyHasBlock", "display": "我的插件：自己有格挡", "expr": "{self}.Block > 0" }
```

`expr` 里能用 `{self}` `{player}` `{foe}` `{amount}`（这条条件填的数值）`{power}`（选的状态类名）。

### 8.3 锻造模块 = 图上【效果】节点 + 模板

```jsonc
{ "kind": "MyForgeHeal", "display": "我的插件模块：回血", "emit": "await CreatureCmd.Heal({self}, {amount});" }
```

它会同时出现在「效果种类」下拉里（因为本质上就是同一个东西）。

## 9. 页面（`pages[]`）—— 四种形态

1. **说明页**（只有 `markdown`）：在「插件」页里显示一段文字。
2. **表单页**（`fields` + `target` + `emit`）：编辑器画表单、存值、实时预览，生成工程时按 `target` 写成文件。
3. **挂到生成器页签**（再加 `tab`）：这一页作为那个生成器页签里的「插件扩展页」出现，用户编辑卡牌时同一屏就能看到。
4. **加一个生成器一级功能页**（再加 `newTab: true`）：生成器顶部**多一个页签**（像「角色」「药水」那样），
   页签内容就是这一页 —— 这就是「装个怪物生成器插件，编辑器里就多一个怪物生成器页」。

```jsonc
"pages": [
  { "id": "howto", "title": "用法", "markdown": "这个插件怎么用…" },
  {
    "id": "events", "title": "事件", "tab": "card",
    "markdown": "这一页干什么（可空）",
    "fields": [
      { "key": "EventClass", "label": "事件类名（英文）", "type": "text",   "default": "MyEvent" },
      { "key": "Weight",     "label": "权重",            "type": "number", "default": "1" },
      { "key": "Repeatable", "label": "可重复",          "type": "bool",   "default": "0" },
      { "key": "Kind",       "label": "类型",            "type": "pick",   "options": ["普通","战斗","诅咒"], "default": "普通" },
      { "key": "Intro",      "label": "开场白",          "type": "multiline", "default": "" }
    ],
    "target": "cs/Plugins/{id}/Events/{EventClass}.cs",
    "emit": "public static class {EventClass} { public const int Weight = {Weight}; }"
  },
  {
    "id": "monsters", "title": "怪物生成器", "newTab": true,   // ← 生成器多一个一级页签
    "markdown": "填好东西点按钮，会生成到工程里。",
    "fields": [
      { "key": "MobClass", "label": "怪物类名（英文）", "type": "text",   "default": "MyMob" },
      { "key": "MobHp",    "label": "血量",            "type": "number", "default": "40" }
    ],
    "target": "cs/Plugins/{id}/Mobs/{MobClass}.cs",
    "emit": "public static class {MobClass} { public const int Hp = {MobHp}; }"
  }
]
```

* 栏位类型：`text` / `number` / `bool`（值渲染成 `1`/`0`）/ `pick`（`options`）/ `multiline`。
* 占位符：`emit` 和 `target` 里都能用 `{字段key}`，`target` 另外支持 `{id}`；`target` 里的非法字符会被换成 `_`。
* 值存在 `plugins\<插件 id>\values.json`（每页一组），改清单不会丢。
* `tab` 的取值：`card`（卡牌）/ `relic`（遗物）/ `potion`（药水）/ `power`（自定义状态）/ `forge`（锻造）/ `build`（构建·日志）；
  不填 = 老行为（挂「插件」页）。写别的值不算错，那一页留在「插件」页并显示提示。
* `newTab: true`：这一页变成生成器的**一级页签**，固定排在「构建 / 日志」**前面**；
  和 `tab` 二选一（都填时 `newTab` 优先）。两个插件的功能页重名时，页签标题会自动带上插件名。
* 挂走的页 / 一级功能页**不会**在「插件」页重复画一份（那里只留说明和清单）。
* **功能页拿不到编辑器数据**：只有自己那份值，读不到当前存档的角色 / 卡牌 / 遗物，也拿不到本体名录 ——
  能做的就是「填表 → 按模板出文件」；要每只怪物一份数据，就在模板里按行 / 逗号拆，或者开多个页。

## 10. 打包规则

* `csFiles` → 生成工程的 `cs\Plugins\<插件 id>\…`（一起编进 mod 的 dll）；
* `assetFiles` → 生成工程的 `plugin_assets\<插件 id>\…`（随工程一起打包）；
* 自带文件只能放在**插件自己的文件夹内**（`..\` 越界会被拒绝），而且必须真的存在；
* 不要写 DLL、不要反射编辑器内部类型 —— 兼容性靠「只认 JSON + 模板」。

## 11. 报错对照（编辑器「插件」页 ⚠ 列表里的原文 → 原因 → 怎么改）

先分清两种：

* **整份不加载**（一条都不生效）：`plugin.json 解析失败`、`清单版本 apiVersion … 不受支持`、`插件 id … 重复`。
* **只跳过那一条**（其余照常）：下面表里带「跳过这一条」的那几行。

| 报错原文 | 原因 | 怎么改 |
|---|---|---|
| `plugin.json 解析失败：…` | JSON 语法错（多逗号、缺引号、注释写成了 `//` 以外的东西） | 只允许 `//` 行注释和尾随逗号；拿 JSON 校验器过一遍 |
| `清单版本 apiVersion = 「x」不受支持（本编辑器支持 1）` | `apiVersion` 写错 | 改成 `"1"` |
| `插件 id「x」和前面某个插件重复` | 两个插件同 id | 换个 id |
| `有一条效果没填 kind。` | 空 kind | 补上 |
| `效果 kind「x」不合法` | 不是「字母开头 + 字母数字下划线」 | 改成英文标识 |
| `效果 kind「x」和已有的种类重名 —— 跳过这一条` | 和自带 65 种或别的插件撞名 | 换个前缀 |
| `效果「x」没写 emit 模板。` | 缺 emit | 补上 |
| `… 的「…」不支持「数值公式」（现在填的是 …）` | 卡牌页那一栏对插件效果只认**光秃秃的 `X`** | 改成固定数字，或只写 `X`；要按公式算就把表达式写进模板 |
| `触发时机「x」没填 hook` / `hook「y」不是编辑器已知的钩子` | hook 写错 | 从界面下拉里抄 id（见 8.1 表） |
| `条件「x」没写 expr 表达式。` | 缺 expr | 补一段布尔表达式 |
| `条件 id「x」和已有的条件重名` | 撞名 | 换 id |
| `锻造模块「x」没写 emit 模板。` | 缺 emit | 补上 |
| `自带文件「f」跑到插件目录外面了` | 用了 `..\` | 把文件放进插件目录 |
| `自带文件「f」不存在。` | 路径写错 / 忘了带文件 | 核对相对路径 |

**生成出来编不过**（不是插件加载问题）时，最常见的三种：

| 症状 | 原因 |
|---|---|
| `CS0103 … 不存在` / `CS1061 … 不包含` | 模板里写的方法名 / 成员名和本体 API 不一致 —— 对着 `src\Core\Commands\` 核一遍 |
| `CS0103 choiceContext` | 用了 `{choice}`，但挂的那个 hook 没有 `choiceContext`（或 `hasChoiceContext` 写错） |
| `CS0246` 找不到你写的类 | `csFiles` 没写进清单，或路径写错（要相对插件目录） |

## 12. 交付前自检清单（逐条过）

1. `id` / `name` / `apiVersion: "1"` 都有；`kind` / `id` 都是英文标识、不和自带的重名。
2. 每条效果都有 `emit`；模板里**没有** `using` / `namespace` / `class`。
3. 占位符和链对得上（第 5 节：状态链别用 `{cardPlay}`；用完 `{power}` / `{card}` 要开 `needsPower` / `needsCard`）。
4. `triggers[].hook` 是第 8.1 表里的 id；`guard` 只用那个钩子签名里的参数；`hasChoiceContext` 和签名一致。
5. 页面：`target` 都在 `cs/Plugins/{id}/…` 或 `plugin_assets/{id}/…` 下，没有 `..\`；`tab` 用第 9 节的 6 个值之一；
   想让编辑器多一个一级功能页就用 `newTab: true`（和 `tab` 别同时填）。
6. 自带文件真的存在、真的在插件目录里。
7. 模板末尾的分号 / 括号别丢（它要直接进 C# 方法体）。

## 13. 交付内容

给出**完整文件内容**（路径 + 内容），至少 `plugins\<插件名>\plugin.json`，需要时加 `cs\*.cs`；
并告诉我装好之后怎么验证（哪个页、哪个下拉、点哪个按钮）。

## 14. 我想做的插件

（把你的需求写在这里。例：「做一个效果：对目标造成 N 点伤害并给它 1 层虚弱」；
或「回复 N 点生命，如果是战斗中再抽 1 张牌」；或「顺便加一页说明，写清这个插件的用法」；
或「加一个挂在『卡牌』页里的扩展页，按我填的名字生成一个事件类」。）

=== 提示词结束 ===

---

## 附：装好插件之后怎么验证（写给用户）

1. 打开编辑器 →「插件」页 → 点「重新加载插件」；
2. 左边列表里能看到插件，右边能看到它贡献的效果 / 页面 / 自带文件 / 问题（⚠ 就是没加载成功）；
3. 「卡牌」页 → 效果列表 →「效果种类」下拉里选插件效果 → 填数值 →「生成工程」；
4. 声明了 `pages[].tab` 的页，会出现在对应生成器页签底部的「插件扩展页（N）」折叠条里（点开就是一排页签）；
5. 生成出来的工程里能搜到模板展开的那几行代码，`cs\Plugins\<插件 id>\` 下有你带的 cs。
---

## 15. 动态行集合 `type = "list"`（V0.2.349）

要「招式列表 / 掉落表 / 任何多行可增删的界面」就用 `list` 栏位：

```json
{ "key": "moves", "label": "招式", "type": "list", "itemLabel": "招式", "defaultRows": 1,
  "fields": [ { "key": "name", "type": "text", "default": "Tackle" }, { "key": "dmg", "type": "number", "default": "6" } ] }
```

emit 里用块宏逐行展开：

```
{moves}
    "{index}: {name} = {dmg}",
{/moves}
```

要点：
* 块必须配对（`{moves}` … `{/moves}`），不配对会报错、整段不展开；
* 块里可用子栏位 + `{index}`（1 起）/ `{index0}`（0 起）；
* 子栏位**不能**再是 `list`；`target` 文件名里**不能**用 list 栏位；
* 行数据在 `plugins\<id>\rows.json`（和 values.json 分开）。
---

## 16. 数值与「下回合生效」（V0.2.355）

* `{amount}` = 所在链算出来的表达式（五条链一致；填了「数值公式」/「数值 = X」也是对的）。模板里直接用即可。
* 效果可以勾「下回合生效」：生成端会生成 `<角色>ForgeDelayedPlugin<种类>` 延迟状态类，
  本回合只挂状态、下回合执行你的模板；`{amount}` 在那时等于状态层数。同种类共用一个延迟类。
---

## 17. 引用型栏位 `type = "ref"`（V0.2.356）

要「从存档里挑一张卡 / 一个状态 / 一个组」就写 `"type": "ref"` + `"refKind": "card|power|relic|potion|pet|group"`。
下拉里是当前存档的真实对象，存的是类名 / 组 Id，emit 里当普通字符串用 `{字段key}`。
换存档会自动重建下拉；值找不到时显示「找不到：X」并选中，不会静默空。列表字段的子栏位也能用 ref。
