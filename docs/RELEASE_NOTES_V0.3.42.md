# V0.3.42 — 先古遗物替换传 canonical，修复 CanonicalModelException

## 反馈

`ForgeAncientRelicReplace.Replace<T>()` 模板里：

```csharp
var relic = ModelDb.Relic<T>().ToMutable();
...
MakeOption(ancient, relic);
```

`AncientEventModel.RelicOption(RelicModel, string, string)` 内部还会再取一次 canonical / mutable，
提前传 mutable 会抛：

```text
CanonicalModelException: Canonical model of type X used in incorrect place
```

结果：`GenerateInitialOptions` 整体失败，先古之民只出台词、没有遗物选项。

## 修法

`AncientPatchGen` 模板改成传 canonical：

```csharp
var relic = ModelDb.Relic<T>();
```

`MakeOption` 内部由本体 `RelicOption` 自己负责 mutable，不再提前转换。

## 验证

| 项 | 结果 |
|---|---|
| 生成源码包含 `ModelDb.Relic<T>()` | PASS |
| 不再包含 `ModelDb.Relic<T>().ToMutable()` | PASS |
| 开发版 `--uicheck` | **4893 PASS / 0 FAIL**（+1 条 V0.3.42 断言） |
| 部署版 `--uicheck` | **4892 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里的 `ForgeAncientRelicReplace` 还是 `ToMutable()`。

## 另：plugins\不死 的动态程序集

这条反馈也成立：`PluginUndying.cs` 里 `AssemblyBuilder.DefineDynamicAssembly` 造出
`undying_runtime_*` 后直接 `ModelDb.Inject`，没有关联到 mod。

修复方向（插件作者侧）：

```csharp
ModManager.AssociateAssemblyWithMod(dynamicAssembly, thisMod);
// 然后再 ModelDb.Inject(...)
```

本机没有 `plugins\不死` 的源码（当前插件目录里也没有这个插件），所以这次只修了生成器模板；
插件那一半需要拿到 `PluginUndying.cs` 后按上面一行补。
