# V0.3.68 — 「清除指定状态」改成按类型删除，修复蓝状态清不掉

## 现象

「混色：绿」的蓝色触发分支里，`ClearPowerOne ColorBlue` 看起来执行了，但 `染色：蓝` 还留在敌人身上。

## 原因

生成的清除代码是：

```csharp
PowerModel? __clrPower = base.Owner.GetPower<ColorBlue>();
if (__clrPower is not null) await PowerCmd.Remove(__clrPower);
```

在 `AfterPowerAmountChanged` 里，尤其是这个状态自己正在被 Apply / 层数变化的那一下，`GetPower<T>()` 可能拿不到实例，于是整句静默跳过，状态就留下了。

## 修改

「清除指定状态」统一改成按类型删除：

```csharp
await PowerCmd.Remove<ColorBlue>(base.Owner);
```

覆盖：

- 自定义状态的触发效果（`CustomPowerGen`）；
- 卡牌 / 遗物 / 药水链（`CSharpCodeGen.ClearPowerOneBody`）；
- 锻造节点图（`ForgeGraphGen`）。

`PowerCmd.Remove<T>(Creature)` 是本体已有 API（现有伙伴守卫测试就在用），不再依赖 `GetPower<T>()` 的实例查找。

## 注意

如果你的条件本身写着「拥有黄色至少 1 层」，那么**黄色不在时**这条清除仍然不会执行 —— 那是配置条件，不是清除代码。要「蓝自己触发就一定清蓝」，请把 `ClearPowerOne ColorBlue` 这条效果的条件去掉。

## 验证

| 项 | 结果 |
|---|---|
| `MixGreen` 生成源码 | `await PowerCmd.Remove<ColorBlue>(base.Owner);` |
| 卡牌 / 遗物 / 节点图 | 同一套按类型删除 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |
