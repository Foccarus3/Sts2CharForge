# V0.3.105 — 骇入 tooltip 的 `{Amount}` / `{=Amount*2}` 同步

## 截图现象

```text
每层使造成的伤害-1%(1)%，受到的伤害+2%(0)%。
```

`{Amount}` 显示 1，但 `{=Amount*2}` 显示 0 —— 两个数不是同一次取层数。

## 原因

卡面 tooltip 走 `HoverTipFactory.FromPower<Hakking>(amount)`：

1. `GetDumbHoverTip` 先取 `Description`；
2. `Description` 里 `{=Amount*2}` 被生成成固定值；
3. 之后 `AddDumbVariablesToDescription` 才把 `Amount` 改成悬停传入的层数。

所以 `{Amount}` 是新的、`{=Amount*2}` 还是 0。

## 修复

卡牌里用数值公式的「施加状态」悬停，改为**先做一份当前层数的可变副本**再取 tooltip：

```csharp
ModelDb.Power<Hakking>()
    .ToMutable(((int)base.DynamicVars["Hakking"].PreviewValue))
    .DumbHoverTip
```

这样：

- `Amount` 和 `=Amount*2` 在同一次 `Description` 里读到同一个层数；
- 2 层 → `(2)` / `(4)`；
- 1 层 → `(1)` / `(2)`。

战斗状态图标自己的 tooltip 原本就读当前 `Amount`，不受影响。

## 验证

| 项 | 结果 |
|---|---|
| Hikii 生成 `ToMutable(...).DumbHoverTip` | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
