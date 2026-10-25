# V0.2.282 — 状态「盯消耗牌数」把队友的也算进去（联机）

## 你报的

> 状态盯消耗牌数会把队友消耗的也算进去

## 真因

「自定义状态」那条链的 `AfterCardExhausted` 钩子**没有归属判断**：

```csharp
public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
{
    ... 直接就开始计数 / 跑效果 ...
}
```

本体这个钩子是**全局广播**（谁的牌被消耗都会派发到所有状态），所以联机时队友消耗的牌也会让你的状态 +1。
同一条链上另外两个钩子有同样的问题：`AfterCardDrawn`（抽到牌）和 `AfterCardDiscarded`（弃掉牌）。

遗物那条链一直是对的（`CSharpCodeGen` 里写着 `if (card?.Owner?.Creature != base.Owner.Creature) return;`），
只有状态这条链漏了。

## 修法

三个钩子开头都加同一句归属守卫：

```csharp
if (card?.Owner?.Creature != base.Owner) return;   // 只认自己的牌 —— 联机时队友的牌不算
```

（状态里 `base.Owner` 本身就是 Creature，所以不用再 `.Creature`。）

## 验收

* 自检：**4453 PASS / 0 FAIL**。
* 生成端对比：单机存档的生成结果只多这一句守卫（原来这里一句都没有，等于「谁消耗都算」）。
