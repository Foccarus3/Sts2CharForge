# V0.3.73 — 插件市场：搜索 / 关键词 / 分类 / 本机收藏 / 最新排序

## 已做

「插件市场」窗口新增：

```text
搜索：[                    ]  分类：[全部 ▼]  [ ] 只看收藏
```

- 搜索匹配：名称 / id / 作者 / 简介 / 关键词 / 分类；
- 多个词用空格分隔，AND 匹配；
- 分类单选筛选（插件本身可属于多个分类）；
- 本机收藏：`%APPDATA%\Sts2CharForge\market_favorites.json`；
- 只看收藏；
- 排序固定为**最新**（`publishedAt` 降序，缺失时按版本号）；
- 安装 / 卸载后保留当前搜索和分类。

## 索引字段

新增：

```json
"categories": ["animation", "assets"],
"keywords": ["gif", "动画", "逐帧"],
"versions": [
  {
    "publishedAt": "2026-11-15T19:00:00Z"
  }
]
```

分类 id：

```text
effects / conditions / triggers / forge / animation / ui / content / tools / assets / other
```

旧索引没有这些字段时按「其他 / 无关键词」处理。

## 为什么没有点赞

静态 `index.json` 收不到投票。真点赞需要轻量后端，当前版本先不做：
收藏只保存在本机，不冒充全市场热度；排序只做最新，不做假的热门。

## 验证

| 项 | 结果 |
|---|---|
| 搜索 / 关键词 / 分类 | 有 |
| 本机收藏 / 只看收藏 | 有 |
| 最新排序 | 有 |
| 点赞 / 热门 | 不做 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |
