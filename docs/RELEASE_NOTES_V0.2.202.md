# V0.2.202 —— 「全部友方」现在真的能找到友方，而且能勾「包括自己」

> 用户先问：「对象的全体友方包括自己吗」→ 不包括。
> 再问：「那我要怎么选择全部友方包括自己」→ 这一版给了勾选框，同时修掉了一个更严重的病根。

## 1. 病根：「全部友方」以前在单人游戏里一个人都打不到

生成端一直是这句（V0.2.131 起）：

```csharp
foreach (Creature ally in base.Owner.Creature.CombatState.PlayerCreatures
        .Where(c => c.IsAlive && c != base.Owner.Creature).ToList())
```

但本体的 `PlayerCreatures` 是 **`Creatures.Where(c => c.IsPlayer)`**，而 `Creature.IsPlayer => Player != null` ——
**召唤物的 `Player` 是 `null`**（它只有 `PetOwner`，见 `PlayerCmd.AddPet` → `CreateCreature(..., null)`）。

于是那个列表里只有「玩家本体」：

- 单人游戏里 `PlayerCreatures` = 只有你 → 被 `c != 自己` 滤掉 → **循环体一次都不跑**（「全部友方」等于空转）；
- 多人游戏里 = 其他玩家，**召唤物永远不在里面**；
- 顺带「不包括召唤物」那个勾一直无效（列表里本来就没有召唤物，`c.PetOwner is null` 是空过滤）。

现在改成遍历 **`CombatState.Allies`**（玩家这一边的全部单位：玩家本体 + 召唤物）：

```csharp
foreach (Creature ally in base.Owner.Creature.CombatState.Allies
        .Where(c => c.IsAlive && c != base.Owner.Creature).ToList())
```

默认那一档生成的文本**一个字都没变**，但语义从「空转」变成「真的打召唤物 / 队友」——
它以前就不生效，所以不存在「老 mod 行为被改坏」的问题。

## 2. 新勾选框：「全部友方：包括自己」

作用对象选「全部友方」时，除了原来的「不包括召唤物」，现在多一行：

```
[ ] 全部友方：不包括召唤物（只算玩家本体）
[x] 全部友方：包括自己
```

| 作用对象 | 包括自己 | 不包括召唤物 | 生成的过滤条件 |
| --- | --- | --- | --- |
| 全部友方 | — | — | `c.IsAlive && c != base.Owner.Creature`（队友 + 召唤物） |
| 全部友方 | — | ✔ | `… && c.PetOwner is null`（只算玩家本体类） |
| 全部友方 | ✔ | — | `c.IsAlive`（**自己 + 队友 + 召唤物**） |
| 全部友方 | ✔ | ✔ | `c.IsAlive && c.PetOwner is null`（自己 + 其他玩家） |

- 只管**自己这一边**；两边的所有单位（含敌人）仍然用「全体单位」那一档。
- 卡面 / 列表措辞跟着走：列表行尾「→ 全体友方（含自己）」、卡面
  「你、队友和所有召唤物」/「队友和所有召唤物」/「你和所有队友」/「所有队友」。
- 工程回读（`ProjectRecovery`）认得 `// CET:TargetSide=AllAllies CET:AllySelf=1`，反读工程不会丢掉这个勾。
- 默认 false **不写进 JSON**（`JsonIgnoreCondition.WhenWritingDefault`）—— 老存档的 JSON 和
  生成出来的 `// CET:ForgeGraph=` 标记段保持逐字节一致。

## 3. 三条链对齐

| 链 | 「全部友方」实际打谁 |
| --- | --- |
| 卡牌 / 遗物 / 药水 / 自定义状态（本版起） | `CombatState.Allies`：玩家本体（可选含自己）+ 召唤物 |
| 锻造节点图 | `ForgeFormulaRuntime.AllAllies(player)` = **自己 + 召唤物**（一直如此；单人场景下等价于上面「含自己」那一档） |

## 4. 验收

- 探针（`sacprobe gen`）：`SacAllySelfBlock / SacAllySelfBoth / SacAllySelfDamage` + 五种「牺牲伙伴」作用对象
  → 生成工程 `dotnet build` **0 error**；生成出来的循环正是
  `.Allies.Where(c => c.IsAlive)` / `.Where(c => c.IsAlive && c.PetOwner is null)`。
- 老存档逐文件对拍：示例 **113** / WhiteCloak **412** 个文件 **0 差异**。
- `--uicheck`：**3960 PASS / 0 FAIL**（新增 11 条：循环换成 `Allies`、不再出现 `PlayerCreatures`、
  含自己 / 含自己+不含召唤物 的过滤条件、标记 `CET:AllySelf=1`、那一行的显隐与刷新、措辞）。

## 5. 版本

- `V0.2.202`（V0.2.201 是另一条修复：锻造图同一块【数值】模块被两个分支各用一次时的 `CS0103`）
