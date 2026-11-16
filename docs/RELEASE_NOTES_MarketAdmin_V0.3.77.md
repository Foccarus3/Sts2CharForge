# MarketAdmin V0.3.77 — RAR 转 ZIP 入口

## 新增

顶栏：

```text
RAR 转 ZIP
```

流程：

1. 点「RAR 转 ZIP」选择 `.rar`；
2. 解压到临时目录；
3. 检查：
   - 路径穿越；
   - 解压后 >100 MB；
   - 必须有 `plugin.json`；
4. 重新压成标准 ZIP；
5. 自动填入 `选择插件 ZIP` 并解析。

市场包最终仍然只能是 ZIP；RAR 只是输入格式。

## 依赖

MarketAdmin 现在带：

```text
SharpCompress.dll
```

由它读取 RAR。发布包从约 0.8 MB 变为约 1.9 MB。

## 安全

- 不信任 RAR 路径；
- 拒绝 `..` / 绝对路径；
- 解压后上限 100 MB；
- 必须包含 `plugin.json`；
- 转换后的 ZIP 仍走原有检查：
  - 移除 `.bat / .ps1`；
  - 拒绝 `.dll / .exe`；
  - SHA256；
  - 5 MB 市场包上限。

## 验证

| 项 | 结果 |
|---|---|
| RAR 解压 / 转 ZIP | 用真实 RAR 测试通过 |
| ZIP 内有 plugin.json 检查 | 有 |
| 路径穿越 / 大小限制 | 有 |
| `--selftest` | exit 0 |
