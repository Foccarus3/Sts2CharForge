# V0.3.92 — 插件可贡献通用【对象】

## 新增插件字段

`plugin.json` 现在可以写：

```json
"units": [
  {
    "id": "ExceptTarget",
    "display": "指定目标以外的所有敌人",
    "foes": "{combatState}.HittableEnemies.Where(c => c != {foe})",
    "single": "({combatState}.HittableEnemies.Where(c => c != {foe})).FirstOrDefault()",
    "note": "说明"
  }
]
```

- `foes`：列表表达式，返回值必须是 `IEnumerable<Creature>`；
- `single`：单个目标表达式，可省略；省略 = `foes.FirstOrDefault()`；
- 占位符：
  - `{self}` / `{player}` / `{foe}` / `{combatState}`。

## 接进锻造图

插件对象会出现在：

- 锻造图【对象】节点；
- 锻造图【指向对象】节点。

选「指定单体敌人」后，插件对象会把那个敌人排除。

## 生成端

`ForgeGraphGen.ResolveUnit` 识别插件对象：

```csharp
_ => PluginRegistry.UnitOf(node.Get("Pick", "Self")) is { } pu
    ? PluginUnitRef(pu)
    : fb,
```

占位符替换成锻造图局部变量：

```text
{self}        → __fgSelf
{player}      → __fgPlayer
{foe}         → __fgTarget
{combatState} → __fgSelf.CombatState
```

## 范围

- 已接：**锻造图**的【对象】/【指向对象】。
- 未接：卡牌 / 遗物 / 药水页的「作用对象」下拉；那一套仍是内置档位。

## 独立插件

「除指定目标外的敌人」插件已带 `units`，可单独放进 `plugins`：

- `Sts2CharForge_插件_除指定目标外的敌人.zip`

## 验证

| 项 | 结果 |
|---|---|
| 插件对象注册 | 通过（uicheck） |
| 对象出现在 UnitPicks | 通过 |
| 锻造图生成排除目标 | 通过（探针） |
| 开发版 `--uicheck` | **4935 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4934 PASS / 0 FAIL** |
