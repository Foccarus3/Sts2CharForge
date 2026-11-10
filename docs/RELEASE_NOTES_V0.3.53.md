# V0.3.53 — 锻造卡「因预见而被丢弃时」不触发

## 现象

「借势」的锻造图里有一条：

```text
触发时机：因预见而被丢弃时
→ 抽 2 张牌
```

游戏里把这张牌用「预见」丢进弃牌堆时，抽牌不触发。

## 真因

生成代码里，`AfterCardDiscarded` 的守卫会读：

```csharp
if (__inScryDiscard) { ... }
```

这个标记由「预见」丢牌那一小段负责置 `true`。

卡牌页的预见（`CSharpCodeGen.EmitScry`）有置标记；
但**锻造图的预见**（`ForgeGraphGen` 的 `Scry` 分支）只生成了：

```csharp
await CardCmd.Discard(choiceContext, __fgDrop);
```

**没有** `__inScryDiscard = true/false`，所以锻造图的「因预见被丢弃」守卫永远看到 `false`，抽牌永远不触发。

## 修法

锻造图的 Scry 与卡牌页保持一致：丢牌前打标记，`finally` 复位：

```csharp
__inScryDiscard = true;
try
{
    await CardCmd.Discard(choiceContext, __fgDrop);
}
finally
{
    __inScryDiscard = false;
}
```

只在真的用到「因预见被丢弃」这条时机 / 条件时才生成这段（老存档输出不变）。

## 验证

| 项 | 结果 |
|---|---|
| 锻造图 Scry 生成 `__inScryDiscard = true` + `finally` 复位 | PASS |
| 「因预见而被丢弃」的 AfterCardDiscarded 守卫仍保留 | PASS |
| 开发版 `--uicheck` | **4907 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4906 PASS / 0 FAIL** |

⚠ 必须重新生成「借势」所在的工程；旧工程里锻造图的 Scry 仍然没有标记。
