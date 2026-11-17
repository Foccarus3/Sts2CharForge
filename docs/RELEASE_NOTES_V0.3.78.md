# V0.3.78 — 自定义状态效果下拉显示插件效果

## 问题

插件贡献的效果种类，在「卡牌 / 遗物 / 药水」页能看到，但在：

```text
自定义状态 → 触发时机 → 添加效果
```

的下拉里看不到。

## 原因

`PowerKinds` 只筛：

```csharp
EffectCatalog.EffectKinds.Where(k => PowerTriggers.Supports(k.Kind))
```

`PowerTriggers.Supports` 只有内置白名单，插件效果不在里面；而生成端其实早就支持状态链插件效果（`PluginGen.TryEmit`）。

## 修复

`PowerKinds` 改成：

```csharp
EffectCatalog.AllEffectKinds
    .Where(k => PowerTriggers.Supports(k.Kind)
        || (PluginRegistry.EffectOf(k.Kind) is { NeedsCard: false }))
```

行为：

- 内置效果：和以前一样按白名单；
- 插件效果：`needsCard = false` 的显示出来；
- 需要卡牌上下文的插件效果（`needsCard = true`）仍不放进状态页，避免选完才被校验拦下。

## 验证

| 项 | 结果 |
|---|---|
| 自定义状态效果下拉含插件效果 | 有 |
| 需要卡牌的插件效果仍隐藏 | 保持 |
| 开发版 `--uicheck` | **4917 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4916 PASS / 0 FAIL** |
