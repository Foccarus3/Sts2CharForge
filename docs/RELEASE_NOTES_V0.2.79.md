# V0.2.79 更新说明

> 你问的两件事都在这一版里：
> ① **多人联机**下多个角色模组的冲突（英文类名 / 额外资源量 / 状态）—— 先说清本体的硬规则，再修掉会互相盖的东西；
> ② **「生成卡牌的升级增量好像没反应」** —— 确实是 bug，卡面写升级后的数字、代码却还是老数字。

## ① 多人联机：先说本体的硬规则（这决定了「能不能各自装各自的」）

本体握手时（`Core/Multiplayer/Connection/HandshakeManager.cs`）要三项都对得上：

1. **游戏版本一致** → 不一致 `VersionMismatch`；
2. **「影响玩法的模组」列表完全一致**（`manifest.id` + 版本，双向都不许缺） → 不一致 `ModMismatch`；
3. **模型 ID 库哈希一致**（`ModelIdSerializationCache.Hash` = 所有注册模型 ID 的集合） → 不一致还是 `VersionMismatch`。

所以**「你只装你的角色、我只装我的角色」是连不上的** —— 两个角色模组都会往模型库里加东西。
**要一起玩，所有人都得装同一整套模组**（A 的和 B 的都放进 mods）。非玩法模组可以不一样（本体只记一条警告）。

## ② 装齐之后「各看各的」：已经做到的 vs 这一版修的

**本来就做到了**（都是**运行时补丁 + 只在「这一局本地玩家玩的正是我方角色」时生效**）：

- 本体状态改名 / 换描述 / 换图标 / 换血条颜色 → 你看你的名字，朋友看他的（`LocalContext.GetMe().Character`）；
- 额外资源量的**计数器**（图标 + 名字条）→ 补丁按「这个计数器归谁」判断（`NStarCounter.Initialize(player)`）；
- 卡牌 / 遗物 / 药水 / 状态 / 图标 / 场景 → 类名与资源路径都带角色类名前缀；能量球用模组自己的图集页（不动本体 tpsheet）。

**这一版修的**：额外资源量的**悬停提示**以前是**覆盖本体的 `STAR_COUNT.title` / `.description`** ——
那是全局键，两个模组都改就是谁后加载谁赢（而且两个客户端加载顺序还可能不同：你看到「戏法」、队友看到「星尘」）。
现在改成写**我们自己的键**：

```
"SPARKLE_EXTRA_RESOURCE.title": "戏法",
"SPARKLE_EXTRA_RESOURCE.description": "[img]…[/img]戏法：每场战斗开始时获得 2 点…"
```

由生成的计数器补丁在「这个计数器归我方角色」时换上去（`AccessTools.FieldRefAccess<NStarCounter, HoverTip>("_hoverTip")`），
**本体键一个字都不动**：本体 / 别的角色 / 主菜单百科里的「辉星」文案保持原样。卡面那条资源提示也改用自己的键了。

## ③ 还需要你自己注意的两件事（工具做不到自动判断）

**（a）类名必须全世界唯一。** 本体的模型 ID 是**用类名算的**（`ModelDb.GetId` → `Slugify(类型名)`，**不含命名空间**），
两份模组有一个同名类，加载模型库时直接抛 `DuplicateModelException` —— 表现是**游戏打不开**（不是战斗里出错）。

- 工具自动生成的类名都带角色前缀（`SparkleStrike` / `SparkleCard40` / `SparklePower1`…）✅；
- **手填的类名**（卡牌 / 遗物 / 药水 / 自定义状态 / 角色类名）要自己保证唯一：两个人都把角色类名起成 `Seven`、
  或者都留默认的 `MyCharacter`，就会撞。**「配置检查」现在会提示「类名没带角色前缀」**，建议改成带前缀的。

**（b）两个功能天生是全局的**（改的是本体本地化键，做不到各看各的）：
「本体卡牌 / 遗物 / 药水**描述**里的旧名字替换」和「本体**关键词**改名」。
一起玩时建议所有人配成一样，或者干脆不用（本体的**状态**改写不受影响，那条是运行时按角色判断的）。

## ④ 「生成卡牌的升级增量没反应」—— 真 bug，已修

现象：卡面写 `{Cards:diff()}`（升级后显示 2 张），实际打出去还是只生成 1 张。

根因：升级增量确实写进了 `OnUpgrade()`（`base.DynamicVars["Cards"].UpgradeValueBy(1m)`），
**但真正决定生成几张的那一行还是字面量**：

```csharp
// 修之前（数值 1 / 升级增量 1）
await CardPileCmd.AddToCombatAndPreview<Shiv>(base.Owner.Creature, PileType.Hand, 1, base.Owner);
// 修之后
await CardPileCmd.AddToCombatAndPreview<Shiv>(base.Owner.Creature, PileType.Hand, (int)base.DynamicVars.Cards.BaseValue, base.Owner);
```

同一类问题一共 **12 种效果**（都是「取几张」）：生成卡牌（指定卡 / 按范围随机）、消耗、丢弃、升级、变化、
复制、重放卡牌、预见、从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌 —— 全都改成「填了升级增量就读动态变量」。

- **没填升级增量时仍然写字面量** → 老存档 / 默认配置生成出来的代码**逐字不变**（自检里每个种类都有一条对照断言）；
- 注释也改成人类可读的：`// 生成 1（升级后 2） 张 Shiv 放进手牌`，不会把表达式印进注释。

## 自检

- `--uicheck`：**2199 项 0 FAIL**（新增：12 种「取几张」各自「有增量读变量 / 没增量是字面量」两条、
  类名没带前缀的提示与两条对照、资源名走自己的键、卡面资源提示用自己的键）；
- 真生成一份带「生成 / 消耗 / 预见 / 抽牌 / 伤害」升级增量的工程 → **真 `dotnet build` 0 个错误**，
  并在生成代码里核对过：`UpGenFixed.cs` 是 `(int)base.DynamicVars.Cards.BaseValue` + `UpgradeValueBy(1m)`，
  `PlainGen.cs`（没填增量）逐字还是 `1`；
- 拿你的存档副本生成 → 额外资源量的名字现在是 `SPARKLE_EXTRA_RESOURCE.title`，`static_hover_tips.json` 里
  **没有 `STAR_COUNT`**（不再动本体的键）；工程 `dotnet build` 0 错误；
- 其余探针一起回归：积木树 28、关系符/指向对象 28、数值公式 56、覆盖血条 46、节点图种类 14、状态效果 36、
  自定义参数 7、命中次数 7、卡牌描述合成 9、节点图→卡面文案 7 —— 全过。
