# V0.2.327 — 变化自己不该「消耗 + 新增」：改回**原位变**

用户原话：

> 有点怪，为什么我 sparkle 的模组变化自己后显示的是消耗自己，然后在弃牌堆生成一张其他牌，并且弃牌堆显示的牌数还是 0

## 你看到的三件事，逐条对上

| 你看到的 | 原因 |
|---|---|
| 显示的是**消耗自己** | V0.2.325/326 的实现是「新牌进弃牌堆 + `CardCmd.Exhaust(自己)`」—— 走的是本体消耗流程，动画/文字当然是「消耗」 |
| 弃牌堆**生成一张其他牌** | 那句 `CardPileCmd.Add(__madeSelf, PileType.Discard)` |
| 弃牌堆牌数**还是 0** | 同一摞上先 Add 再 Exhaust 的顺序 + 正在打出的这张牌还挂在「打出」摞里，计数没变成你预期的 1 |

这条设计本身不理想（「变化」被做成了「消耗 + 新增」），我改了。

## 现在的实现：同一张牌**原地变**

```csharp
// 数值 0 = 这张牌自己：先落进弃牌堆，再原地变成新牌
if (this.Pile is null || this.Pile.Type != PileType.Discard)
    await CardPileCmd.Add(this, PileType.Discard, CardPilePosition.Bottom, null, skipVisuals: true);
await CardCmd.Transform(this, __madeSelf);   // 同一张牌，1 张换 1 张
```

* **不消耗**：不再出现「消耗」字样/动画，走的是本体的**变化**动画；
* **不新增**：弃牌堆里就那一张（原来的位置被换掉），数量 +1 正常；
* 先把它落到弃牌堆，是因为本体 `CardCmd.Transform` 要求旧牌**有 pile**（正在打出的牌在 `PileType.Play` 里，
  直接 Transform 会把新牌塞回「打出」摞 → 就是 V0.2.320 那个「没变化」）；
* 升级 / 仅本回合升级 / 加关键词这些附加处理照旧。

## 验收

* 探针 `sacprobe transform0`（列表链 + 图链各一张、数值 0 + 指定卡）生成：

  ```csharp
  if (this.Pile is null || this.Pile.Type != PileType.Discard)
      await CardPileCmd.Add(this, PileType.Discard, CardPilePosition.Bottom, null, skipVisuals: true);
  await CardCmd.Transform(this, __madeSelf);
  ```

  整份工程 **`dotnet build` 0 错误**。
* 自检：**4543 PASS / 0 FAIL**（断言同步改成新写法）。
* 只出更新包（`-NoBundle`）。

## 想「额外多一张」怎么办

用**生成卡牌**（数值 ≥ 1 + 选目标卡 / 按范围随机）。「变化卡牌」这一档现在固定是「替换」语义。
