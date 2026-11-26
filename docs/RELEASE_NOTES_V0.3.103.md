# V0.3.103 — 骇入状态悬停数值仍显示 1 层（卡面 tooltip）

## 问题

「骇入」状态：

```text
每层使造成的伤害-1%({Amount})%，受到的伤害+2%({=Amount*2})%。
```

实际 2 层时，从卡面 / 状态悬停看到的说明仍是 1 层的数值。

## 根因

生成卡的 `ExtraHoverTips` 一直写：

```csharp
HoverTipFactory.FromPower<Hakking>()
```

`FromPower<T>()` 不传 amount 时用的是**本体 canonical 模型**（层数 1），
所以 tooltip 的 `{Amount}` 永远是 1 —— 即使战斗里这只怪已经有 2 层骇入。

卡面自己的 `{Hakking:diff()}` 读的是卡牌动态变量，但悬停说明读的是 canonical 状态，两者不是同一个对象。

## 修复

对于**卡牌里的「施加状态」且写了数值公式**，悬停说明改为读这张卡的动态变量预览值：

```csharp
HoverTipFactory.FromPower<Hakking>(
    ((int)base.DynamicVars["Hakking"].PreviewValue))
```

于是：

- 破解获得 3 格挡 × 2 次 → 6 层骇入；
- 悬停骇入说明显示 6 层的数值；
- 升级后为 8 层，显示 8 层。

延迟状态（下回合生效）和临时状态仍走原来的固定数量，避免取不存在的动态变量。

## 说明

这只修**卡面 / 卡牌描述里对状态的悬停说明**；战斗里状态图标自己的 tooltip 一直读当前 `Amount`，不受影响。

## 验证

| 项 | 结果 |
|---|---|
| Hikii 生成 `FromPower<Hakking>(((int)base.DynamicVars...))` | 通过 |
| 延迟状态不会引用不存在的变量 | 通过 |
| 开发版 `--uicheck` | **4949 PASS / 0 FAIL** |
| 部署版 `--uicheck` | （打包后跑） |
