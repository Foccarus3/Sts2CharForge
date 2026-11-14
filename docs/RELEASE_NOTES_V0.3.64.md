# V0.3.64 — 生成代码警告归零 + 插件命名空间隔离

## B：338 个警告 → 0

生成文件统一改成：

```csharp
#nullable enable annotations
#nullable disable warnings
```

生成工程 csproj 使用：

```xml
<Nullable>annotations</Nullable>
<NoWarn>$(NoWarn);CS1998</NoWarn>
```

- 保留 `Player?` / `Creature?` 这些注解写法；
- 关闭生成代码里的可空警告（CS8600/CS8602/CS8604/CS8625/CS8669）；
- `__fgTarget` 声明后补 `_ = __fgTarget;`，解决 CS0219；
- `CS1998` 是生成钩子故意没有 await（空效果 / 只挂状态），统一静音，避免淹没真实警告。

用 `Sparkle` 探针工程实测：**0 错误 / 0 警告**。

## C：插件代码全局命名空间

`PluginCompatGen.WrapPluginNamespaces` 在生成工程里：

```text
cs/Plugins/<插件id>/X.cs
→ namespace Sts2CharForge.Generated.Plugin_<插件id> { ... }
```

并在 `cs/GlobalUsings.cs` 追加：

```csharp
global using Sts2CharForge.Generated.Plugin_<插件id>;
```

所以：

- 两个插件都定义 `RandBuffPool` 不再 CS0101；
- 插件模板里裸写 `PluginDemo.Half(...)` 仍然能解析（global using 兜底）；
- 已经自己写了 `namespace` 的插件文件不强行改。

插件自定义页产出的 `.cs` 也会在 `WritePages` 后补包 namespace。

## 验证

| 项 | 结果 |
|---|---|
| 生成工程警告 | **0**（Sparkle 探针） |
| 两个插件同名类 | 不再 CS0101 |
| 插件裸类名引用 | global using 可解析 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |
