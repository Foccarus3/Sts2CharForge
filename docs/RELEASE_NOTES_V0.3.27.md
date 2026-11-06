# V0.3.27 — 卡牌时机新增「因预见而被丢弃时」

用户要求：把「因预见而被丢弃时」加到卡牌的打出时机（卡牌时机）。

## 新增

`卡牌时机` 下拉新增一项：

```
因预见而被丢弃时
```

它和原来的「被丢弃」是**同一个** `AfterCardDiscarded` 钩子，区别只在触发条件：

- 「被丢弃」→ 任何来源的丢弃；
- 「因预见而被丢弃时」→ 只在工具自己生成的「预见」把牌丢进弃牌堆的那一小段里触发
  （运行时标记 `__inScryDiscard`，由生成的预见代码置 true/false）。

卡面描述会写成：

```
当这张牌因预见被丢弃时，…
```

## 两种都选时不会 CS0111

如果一张牌同时选了「被丢弃」和「因预见而被丢弃时」，生成端只写**一个**
`AfterCardDiscarded` override，用标记二选一：

```csharp
if (ForgeXxxCombatCounters.__inScryDiscard) {
    // CET:CardTiming=DiscardedByScry
    ...
} else {
    // CET:CardTiming=Discarded
    ...
}
```

只选一个时，输出与以前一致（「被丢弃」那条一个字符都不变）。

## 回读

`ProjectRecovery` 里同一个方法名（`AfterCardDiscarded`）只会拼一次方法体，
里面的两个 `CET:CardTiming=` 标记分别把两组效果认回去 —— 不会因为两个子时机共用钩子而把效果翻倍。

## 验证

| 项 | 结果 |
|---|---|
| 卡牌时机候选数 | 8（新增「因预见而被丢弃时」） |
| 只选新时机 | 生成 1 个 `AfterCardDiscarded` + `__inScryDiscard` + 标记，PASS |
| 两种丢弃都选 | 生成 1 个 `AfterCardDiscarded`，两个标记齐全，不会 CS0111，PASS |
| `CombatCountersSource` | 只选新时机时也生成 `__inScryDiscard`，PASS |
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4869 PASS / 0 FAIL** |
| 真编译探针 `sacprobe vars` | 「被丢弃 + 因预见被丢弃」两张布真编译 **0 错误** |
| 部署版 `--uicheck` | **4868 PASS / 0 FAIL** |

⚠ 老存档要重新生成才会出现新时机；已经生成过的工程不会自动多出来。
