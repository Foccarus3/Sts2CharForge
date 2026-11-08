# V0.3.43 — 插件效果声明 `needsTarget` 后，卡牌会正确推出「要选目标」

## 现象

插件效果「碎甲」：

- 「作用对象」选 `Enemy`；
- 但卡牌没有敌人准星，玩家只能拖到自己 / 队友身上；
- 生成代码里的 `{foe}` / `{target}` 拿到的是 `cardPlay.Target`，而这张卡的 `TargetType` 是 `Self`，所以永远是 null。

## 真因

`CSharpCodeGen.NeedsChosenUnit` 只认内置效果：

```csharp
HitsEnemyKind(e) || e.Kind is "Energy" or "Gold"
```

插件效果种类不在 `HitsEnemyKind` 里，所以：

- `ResolveTargetType` 推成 `Self`；
- 卡牌没有 `AnyEnemy` 目标选择；
- 插件模板里的 `{foe}` 取 `cardPlay.Target` = null。

## 修法

`NeedsChosenUnit` 增加：

```csharp
|| PluginRegistry.EffectOf(e.Kind) is { NeedsTarget: true }
```

于是插件在 `plugin.json` 里写：

```json
{
  "kind": "ShatterArmor",
  "needsTarget": true,
  "emit": "await PowerCmd.Apply<...>(..., {foe}, ...);"
}
```

卡牌就会按 `TargetSide` 推出：

- `Enemy` → `TargetType.AnyEnemy`（敌人准星）
- `Ally` → `AnyAlly`
- `AllEnemies` / `AllAllies` → 对应全体类型

`{foe}` / `{target}` 也就能拿到 `cardPlay.Target`。

## 验证

| 项 | 结果 |
|---|---|
| 构建通过 | 0 错误 |
| 开发版 `--uicheck` | **4893 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4892 PASS / 0 FAIL** |

⚠ 插件必须声明 `"needsTarget": true`；只在「作用对象」里选 Enemy 不够，因为那是**效果参数**，不是卡牌目标类型。重新生成工程后生效。
