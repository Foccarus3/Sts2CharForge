# V0.3.109 — 状态 tooltip 补丁不再把 tooltip 弄没

## 现象

V0.3.108 之后，骇入状态**完全不显示 tooltip**，而不是显示 1/0。

## 原因

V0.3.108 的 Harmony 前缀重建了 `HoverTip`，但：

- 把 `isSmart` 设成了 `true`（本体原路径在没有 smartDescription 时是 `false`）；
- 某些第三方 `HoverTips` 过滤补丁会把 `IsSmart` 的条目当另一类处理，结果整条被过滤掉；
- 重建出的文本若为空，也直接返回空列表。

## 修复

1. `isSmart` 改回 `false`，和本体没有 smartDescription 时的路径一致；
2. 重建文本为空时 **不接管**，直接返回 `true` 让本体原逻辑生成 tooltip；
3. 仍然保留当前层数重建：2 层 → `(2)` / `(4)`。

## 生效方式

更新到 V0.3.109，重新生成模组并重装 DLL；旧 DLL 不会自动修。

## 验证

| 项 | 结果 |
|---|---|
| 重建路径 isSmart=false | 通过 |
| 空文本回退本体 | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
