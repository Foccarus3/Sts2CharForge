# V0.3.79 — 「自己 / 指定队友」不再作用到全体友方

## 问题

多人里：

- 「匹夫之勇1-3」的遗物效果会对所有队友生效；
- 「拟态」的回复、「失乐园」的最大生命等本该只作用自己的效果，也会作用到队友；
- 指定队友时，效果又落回自己 / 全体友方。

## 根因

锻造节点图里：

```csharp
public static readonly UnitRef Self =
    new("__fgSelf", "ForgeFormulaRuntime.AllAllies(__fgPlayer)");
```

`Self` 的单个目标写的是自己，但 `Foes`（按单位遍历时用的列表）写成了**全体友方**。

所以只要效果走：

```csharp
foreach (Creature __fgFoe in <Foes>)
```

就会把「自己」效果遍历到所有友方。

同时：

- `ChosenAlly`（指定队友）也映射到 `UnitRef.Self`；
- `EffectSpec.TargetSide == "Ally"` 也映射到 `UnitRef.Self`。

于是「指定队友」也变成自己 / 全体友方。

## 修复

```csharp
Self  => new("__fgSelf", "new[] { __fgSelf }")
```

- 自己：只遍历自己；
- 指定队友：用 `TargetOrFirst(__fgTarget, __fgPlayer)`，即卡牌选中的那个队友；
- `Ally` / `ChosenAlly` 不再落到 `Self`。

## 生成结果对比

修复前，遗物「匹夫之勇1」：

```csharp
foreach (Creature __fgFoe in ForgeFormulaRuntime.AllAllies(__fgPlayer))
    await PowerCmd.Apply<StrengthPower>(..., __fgFoe, 2m, ...);
```

修复后：

```csharp
foreach (Creature __fgFoe in new[] { __fgSelf })
    await PowerCmd.Apply<StrengthPower>(..., __fgFoe, 2m, ...);
```

`MaxHp -5` 同样只扣自己。

「拟态」回复也从 `AllAllies` 改成 `new[] { __fgSelf }`。

## 用户侧

必须重新生成「卡门mod」工程；旧 DLL 不会变。

如果确实想让某条效果作用全体友方，把「作用对象」明确选成**全部友方**，不要选自己。

## 验证

| 项 | 结果 |
|---|---|
| 自己 → 只作用自己 | 生成 `new[] { __fgSelf }` |
| 指定队友 → 用选中目标 | `TargetOrFirst(__fgTarget, ...)` |
| 匹夫之勇1-3 | 力量/敏捷/最大生命只作用自己 |
| 拟态回复 | 只回复自己 |
| 开发版 `--uicheck` | **4918 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4917 PASS / 0 FAIL** |
