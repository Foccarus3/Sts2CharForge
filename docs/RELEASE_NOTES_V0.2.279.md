# V0.2.279 — 两块【触发时机】落在同一个本体钩子上：**合并生成，两块都跑**

## 你报的

> 这块布上第 1 块和第 2 块【触发时机】落在**同一个本体钩子**上（「每回合开始时」和「每回合开始时」其实是同一个时刻）——生成出来会是同一个类里两个签名一样的方法（整个模组编译不过），所以**只会生成第 1 块**，第 2 块白配了。

以前是「报错 + 只留第一块」。现在**不再丢块**：同一个钩子上的多块**合并进同一个方法体**，每块各包一层 `{ }`
（局部变量 `__fgTarget` / `foe` / `__fgTake` 同名也不冲突），语义上也正确 —— 「每回合开始时」那一刻本来就该两块都跑。

## 生成出来的样子（探针实测：一张卡两块都选「每回合开始时」）

```
AfterPlayerTurnStart override 个数 = 1（期望 1）
public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
{
    ... 第 1 块整条链 ...
    // ---- 同一个时刻（每回合开始时）的第 2 块 ----
    {
        ... 第 2 块整条链 ...
    }
}
```

## 改了什么

| 位置 | 改动 |
|---|---|
| `ForgeGraphGen.SplitByHook` | 不再把重复钩子的块丢进 `Skipped`（返回全部） |
| `ForgeGraphGen.GroupByHook`（新增） | 按本体钩子分组，组内保持图上顺序 |
| `ForgeGraphGen.EmitHook` | 新增 `extra` 参数：第一块照旧，其余各包一层 `{ }` 追加进**同一个方法体**（卡牌 / 遗物 / 药水 / 状态四条链共用） |
| `ForgeGraphGen.CardHooks` / `RelicHooks` | 循环改成「每个钩子发一个方法」，其余块作 `extra` 传进去 |
| `ForgeGraphGen.PowerHookBodies`（状态布） | 同一个钩子的正文**追加**而不是 `continue` 跳过 |
| `ForgeGraphGen.Issues` | 那条**错误**降级成**提示**：「V0.2.279 起这两块会合并进同一个方法，各自包一层 { } 各跑各的」 |

## 验收

* 自检：**4453 PASS / 0 FAIL**（把旧断言「同钩子必须报错」改成「不再报错 + 仍给提示」）。
* 探针实测（真实生成）：两块同钩子 → **只有 1 个 `AfterPlayerTurnStart` override**，方法体里**两段链都在**。
* 对拍：老存档（每个钩子只有一块）走的是 `extra == null` 那条路，输出**逐字不变**。
