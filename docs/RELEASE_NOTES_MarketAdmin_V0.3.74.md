# MarketAdmin V0.3.74 — 本机插件市场发布器

## 定位

只给维护者本机使用，独立 exe，不放进普通用户更新包。

```text
Sts2CharForge.MarketAdmin.exe
```

## 功能

1. 选择第三方插件 ZIP；
2. 自动读取 `plugin.json`：
   - id
   - 名称
   - 作者
   - 版本
   - 简介
3. 所有字段可编辑；
4. 分类多选、关键词编辑；
5. 自动推断分类 / 关键词（可改）；
6. 校验并显示：
   - 安装辅助脚本（会被移除）
   - 包结构 / SHA256
7. 生成市场包：
   ```text
   market/packages/<id>-<version>.zip
   ```
8. 改写包内 `plugin.json` 的 id / 名称 / 作者 / 版本 / 简介；
9. 移除 `.bat / .cmd / .ps1 / .vbs / .scr`；
10. 原始 ZIP 备份到：
    ```text
    market/_inbox/
    ```
11. 更新 `market/index.json`：
    - 新插件插入；
    - 已有插件追加版本；
    - 同 id + 同版本拒绝覆盖；
    - 写索引前备份 `index.json.bak`；
12. 市场包超过 5 MB 直接拒绝，提示走 GitHub Release（第一版不做大包自动上传）；\n13. 可选「提交并推送 git」：
    - 只 stage 市场包和索引；
    - 不提交别的改动；
    - commit message：
      ```text
      market: add/update <id> <version>
      ```

## 配置

首次启动可填：

```text
市场仓库根目录
下载 URL 前缀
```

保存到：

```text
%APPDATA%\Sts2CharForge\marketadmin.json
```

默认仓库：

```text
D:\ds\s\t3
```

默认下载前缀：

```text
https://raw.githubusercontent.com/Foccarus3/Sts2CharForge/main/market/packages
```

## 不做

- 不做账号；
- 不调 GitHub API；
- 不自动传大包 Release；
- 不执行插件代码；
- 不改变普通用户的市场页面。

## 验证

| 项 | 结果 |
|---|---|
| ZIP 解析 / 自动填字段 | 有 |
| id / 版本 / 名称 / 作者 / 简介改写 | 有 |
| 脚本自动移除 | 有 |
| 索引追加 / 重复版本拦截 | 有 |
| git 提交推送 | 有 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
