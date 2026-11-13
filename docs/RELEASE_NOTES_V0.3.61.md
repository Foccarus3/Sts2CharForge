# V0.3.61 — 插件 PlayerChoiceContext 自动补 GameActionType

## 问题

v0.113 给 `PlayerChoiceContext` 加了抽象属性：

```csharp
public abstract GameActionType? GameActionType { get; }
```

插件里自己继承它的包装器（例如 `ShuffleHelper.NullSafeChoiceContext`）没有实现，编译报：

```text
CS0534 “NullSafeChoiceContext”不实现继承的抽象成员
“PlayerChoiceContext.GameActionType.get”
```

## 处理

`PluginCompatGen` 在复制插件到生成工程时，扫描：

```text
class Xxx : PlayerChoiceContext { ... }
```

如果正文里没有 `GameActionType`，在类体末尾补：

```csharp
public override global::MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType? GameActionType => null;   // v0.113
```

- 默认返回 `null`：这种空安全包装器本身不产生游戏动作；
- 只改生成工程里的插件副本，不改用户插件源目录；
- 已经实现了 `GameActionType` 的类不会重复插入；
- 普通类 / 没有继承 `PlayerChoiceContext` 的类不碰。

## 验证

| 项 | 结果 |
|---|---|
| 模拟 `NullSafeChoiceContext` 的插件探针 | 自动补属性后 **0 错误** |
| 开发版 `--uicheck` | **4914 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4913 PASS / 0 FAIL** |

⚠ 旧工程需要重新生成。
