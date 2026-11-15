# V0.3.66 — 旧卡牌 API + 插件自定义遗物触发时机

## 1. `ExactSelectionPrompt` / `.Count`

截图里的：

```text
CardSelectorPrefs 未包含 ExactSelectionPrompt 的定义
无法将类型“方法”转换为“decimal”
```

是 **旧生成工程**（V0.3.61 之前）留下的代码。当前生成端已经改用：

```text
CardSelectorPrefs.TransformSelectionPrompt
CardSelectorPrefs.ExhaustSelectionPrompt
```

并且列表计数使用 `.Count()` / `.Count` 中与类型匹配的写法。重新用 V0.3.66 生成即可，不需要手动改旧 cs。

## 2. 插件自定义遗物触发时机

之前 `ResolveRelicTrigger` 只认：

- 插件 `hook` 是编辑器已知的钩子 id；
- 否则回退到内置的某个时机（旧版表现为 `BeforeCombatStart` / `AfterObtained`）。

插件写了自定义 `signature` 时，这个回退会丢掉签名里的变量，于是守卫里的：

```text
player / power / amount
```

在生成的方法里根本不存在 → `CS0103`，重新生成多少次都一样。

V0.3.66：

- 插件 `signature` 非空时，遗物生成直接使用插件签名（不在内置表里找宿主）；
- 自动补 `public override`（插件签名写 `async Task ...` 也能用）；
- 跳过内置钩子的守卫 / 计数 / choiceContext bootstrap，交给插件签名和 `guard` 自己负责；
- 没有自定义签名的插件时机仍走原来的「宿主钩子别名 + guard」逻辑。

用示例插件 `PluginCustomHook` 探针生成遗物，得到：

```csharp
public override async Task AfterPlayerTurnStartEarly(
    PlayerChoiceContext choiceContext, Player player)
{
    if (player != base.Owner.Player) return;
    ...
}
```

## 验证

| 项 | 结果 |
|---|---|
| 插件 `signature` 自定义遗物钩子 | 生成对应 override，不再回退 |
| `player / power / amount` 变量 | 由签名提供 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

⚠ 旧工程必须重新生成；旧 `RukayukiRelic4/5.cs` 不会自己变。
