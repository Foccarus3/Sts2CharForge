# Sts2CharForge 插件市场（MVP）

这是生成器「插件市场」读取的**静态索引**。生成器只下载 zip、校验 SHA256、解压到
`plugins\<id>\`；不会执行插件代码，也不会自动上传。

## 安装（用户）

生成器 →「插件」页 →「插件市场（下载 / 安装）」→ 选中插件 →「安装选中的最新版」。
安装后点「重新加载插件」或重新生成工程。

插件可以包含 C# 代码，会编译进你的模组。市场只保证包结构和 SHA256，不保证第三方插件安全。

## 发布插件（作者）

当前不做客户端上传，走 GitHub PR：

1. 把插件目录打成 zip：`plugin.json` 在 zip 根或一层子目录内；
2. 把 zip 放到 `market/packages/<id>-<version>.zip`；
3. 在 `market/index.json` 的 `plugins[]` 里加条目 / 版本；
4. 提交 PR。

CI / 维护者会检查：

- `id` 合法且唯一；
- `apiVersion` 与生成器兼容；
- `minGeneratorVersion` 合理；
- zip 内 `plugin.json` 的 id 和索引一致；
- 不准携带 `.dll` / `.exe` / `.bat` / `.ps1` 等可执行文件；\n- **安装辅助脚本**（例如卡牌 GIF 替换的 `安装GIF到用户目录.bat`）不进市场包：市场只装插件主体，脚本放到仓库 / Release 的附加文件里，README 写手动步骤；
- `sha256` 与实际包一致；
- 插件有明确许可证；
- 不包含游戏本体资源或别人的付费素材。

仓库不托管任意大文件；单个插件包上限 25 MB（生成器也会拒绝更大的包）。

## 索引字段

```json
{
  "schemaVersion": 1,
  "plugins": [
    {
      "id": "example",
      "name": "示例插件",
      "author": "作者",
      "description": "一句话",
      "license": "MIT",
      "apiVersion": 2,
      "minGeneratorVersion": "V0.3.72",
      "homepage": "https://...",
      "versions": [
        {
          "version": "1.0.0",
          "download": "https://.../example-1.0.0.zip",
          "sha256": "小写十六进制",
          "notes": "更新说明",
          "gameVersion": "v0.113.x"
        }
      ]
    }
  ]
}
```

## 安全边界

- 生成器不加载插件 DLL；但插件的 `.cs` 会随模组编译，等于可执行代码；
- 未审核插件不应放进「已验证」列表；
- 发现恶意插件时直接删索引条目 + 保留 PR 记录；
- 不上架账号 / 付费体系前，不做自动更新，安装前必须弹确认。
