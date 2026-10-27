# V0.2.306 — 「获得能量 / 获得金币」的对象可以指定队友了

用户要求：**「让回能和获得金币的对象能指定队友」**。

## 界面

「获得能量」「获得金币」这两条效果现在也有「作用对象」这一栏了，候选只给三档（其它档位对它们没有意义）：

| 作用对象 | 意思 |
|---|---|
| 自己（默认） | 老行为，一个字都没变 |
| 指定队友 | 打出这张牌时选中的那个队友（本体 `TargetType.AnyAlly`） |
| 全部友方 | 同一边的**所有玩家**，默认不含自己（勾「包括自己」就含） |

`数值 N` 那些栏位不变；换过效果种类时留下的旧档位（例：先填了「全体敌人」再改成「获得能量」）仍然会出现在下拉里（不会变空白），校验器会明确说它不生效。

## 生成出来的代码（探针 `sacprobe allyenergy` 原文）

卡牌 · 回能给指定队友（`TargetType` 也跟着变成 `AnyAlly`）：

```csharp
if (cardPlay.Target is not null && cardPlay.Target != base.Owner.Creature
    && cardPlay.Target.Side == base.Owner.Creature.Side && cardPlay.Target.Player is { } __allyPlayer) {
    await PlayerCmd.GainEnergy(base.DynamicVars.Energy.BaseValue, __allyPlayer);
}
```

卡牌 · 金币给全部友方（只遍历**玩家本体** —— 召唤物的 `Player` 是 null，它们没有能量 / 金币）：

```csharp
foreach (Creature __allyUnit in base.Owner.Creature.CombatState.Allies
    .Where(c => c.IsAlive && c != base.Owner.Creature && c.IsPlayer).ToList()) {
    if (__allyUnit.Player is { } __allyPlayer) {
        await PlayerCmd.GainGold(base.DynamicVars["Gold"].BaseValue, __allyPlayer);
    }
}
```

五条链都接了：卡牌、遗物、药水、自定义状态、锻造节点图（状态链里 `base.Owner` 是 Creature，用的是 `base.Owner.CombatState.Allies`）。

## 边界（校验器会说清，不静默）

* **遗物 / 药水 / 自定义状态**没有「玩家选中的目标」→ 选「指定队友」会**报错**拦住，提示改用「全部友方」（「全部友方」在这些链上是支持的）；
* 「下回合生效」那一档挂的是延迟状态，**只作用于自己** → 选了队友会有一条警告；
* 手改 JSON 写成「单体敌人 / 全体单位」这类做不到的档位 → 一条提示说清「生成端按自己处理」；
* 老存档里这两条效果的「作用对象」是默认值 `Enemy`（那一栏以前根本不显示），它等于「没填」——
  所以**不写进列表行尾、不写进卡面、生成文本逐字节不变**（下面有对拍）。

## 卡面文案

* 指定队友 → 「让指定队友获得 2 点能量。」
* 全部友方 → 「让所有队友获得 5 枚金币。」（不谈召唤物——它们拿不到）
* 自己 / 老存档 → 「获得 2 点能量。」（和以前一字不差）

## 验收

* 自检：**4486 PASS / 0 FAIL**（+10 条 V0.2.306 断言：候选三档、卡牌 `TargetPlayer`、Allies 循环、默认档逐字不变、
  卡面措辞、遗物 / 状态链、遗物上选「指定队友」被拦住…）。
* 探针 `sacprobe allyenergy`：五条链的生成代码如上；**真编译 `dotnet build` 0 错误**；
  回读（生成 → 从工程恢复）把「指定队友 / 全部友方」认了回来（卡牌、遗物、状态都对）。
* 红线：Sample 113 个文件 / WhiteCloak 412 个文件**逐字节相同**（只有 `.sln` 的 GUID 不同）。
* 只出更新包（`-NoBundle`）。

## 提醒

「指定队友」依赖本体 `TargetType.AnyAlly` —— 那是**多人联机**才会弹目标选择的一档；
单人游戏里场上活着的玩家单位只有你一个，按本体规则这张牌**直接打不出去**（校验器有一条提示提醒你先改成「自己」试）。
「全部友方」在单人里就等于只给你自己（不含自己时什么都不做），也适合当「联机才生效」的写法。
