# V0.3.75 — 市场版本号后显示上传日期

## 改动

插件市场列表：

```text
卡牌 GIF 替换 (card_gif_swap) 作者 Sts2CharForge 最新 1.5.0（2026-11-15）
```

- 日期取自 `versions[].publishedAt`；
- 转成本机时区；
- 缺字段时显示：
  ```text
  （日期未标注）
  ```
- 不改变排序：仍按 `publishedAt` / 版本号排最新。

## 说明

`publishedAt` 由 MarketAdmin 生成版本时写入；旧条目没有这个字段就显示“日期未标注”。

## 验证

| 项 | 结果 |
|---|---|
| 最新版本后显示日期 | 有 |
| 缺 publishedAt 的回退 | 日期未标注 |
| 开发版 `--uicheck` | **4916 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4915 PASS / 0 FAIL** |
