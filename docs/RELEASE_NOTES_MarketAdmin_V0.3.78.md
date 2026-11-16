# MarketAdmin V0.3.78 — 删除提示 & 上传日期说明

## 删除后远程仍可见的原因

删除按钮只改**本机仓库**：

```text
market/index.json
market/packages/*.zip
```

生成器市场读取的是远程：

```text
raw.githubusercontent.com/.../main/market/index.json
```

所以必须再点「提交并推送 git」。

V0.3.78 删除成功提示改成：

```text
注意：现在只删了本地；点「提交并推送 git」后，远程市场才会消失。
```

## 其他

- 生成器 V0.3.75 在市场列表的版本号后显示 `publishedAt` 日期；
- 旧条目没有日期时显示「日期未标注」。

## 验证

| 项 | 结果 |
|---|---|
| 删除后提示本地/远程区别 | 有 |
| `--selftest` | exit 0 |
