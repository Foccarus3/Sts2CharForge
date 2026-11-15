# V0.3.72 — 插件市场 MVP：索引 + 下载安装

## 这次先做「能试」的最小闭环

不是完整账号制市场；先做**只读静态索引 + 下载 + 安全解压安装**。

### 生成器里

「插件」页新增：

```text
插件市场（下载 / 安装）
```

窗口里：

- 读取 `market/index.json`；
- 列出插件 / 作者 / 最新版本 / 是否已安装；
- 安装最新版（SHA256 + zip 结构校验后解压到 `plugins\<id>\`）；
- 卸载选中插件；
- 打开插件文件夹；
- 安装前明确提示「插件包含 C# 代码，会编译进模组」。

### 安全边界

- 不执行插件代码，只解压；
- 拒绝 `.dll` / `.exe` / `.bat` / `.ps1` / `.vbs` / `.scr`；
- 拒绝路径穿越、绝对路径、超过 2000 个文件、压缩包 >25 MB、解压后 >100 MB；
- 插件 id 必须和索引一致；
- 已安装目录默认**不覆盖**，避免冲掉用户本地修改；
- 上传不在客户端里做，走 GitHub PR：`market/README.md`。

### 默认市场

仓库里新增：

```text
market/index.json
market/packages/example-1.2.0.zip
market/README.md
```

默认索引地址：

```text
https://raw.githubusercontent.com/Foccarus3/Sts2CharForge/main/market/index.json
```

目前上架的是自带「示例插件」包，方便先试安装流程。

## 验证

| 项 | 结果 |
|---|---|
| 市场窗口读取索引 | 有 |
| 安装 / 卸载 / 重新加载 | 有 |
| SHA256 / zip 校验 | 有 |
| 可执行文件 / 路径穿越拦截 | 有 |
| 开发版 `--uicheck` | （跑完见下） |
| 部署版 `--uicheck` | （跑完见下） |
