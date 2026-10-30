# V0.2.336 — 「花掉能量后 / 花掉额外资源量后」的计数从**施加那一刻**起算

用户报：自定义状态「花掉能量后」的计数起点是**战斗开始**，不是挂上这个状态的时候，怀疑别的时机也有同样问题。

## 排查结果

| 时机 | 计数起点 | 结论 |
|---|---|---|
| 打牌 / 消耗 / 丢弃 / 抽到 / 生成 / 敌人死亡 | 施加那一刻（V0.2.168 起就有每实例基线字段） | ✅ 一直是对的 |
| **花掉能量 / 花掉额外资源量** | ❌ 以前是**战斗（或本回合）总量** | 本次修好 |

原因：这两条时机走的是**独立的发射器**（`EmitSpendTriggers` / 遗物那条），直接用本体战斗历史的
`EnergySpentEntry` 总量取余，没减「施加时已经花了多少」。所以上状态之前花掉的能量也被算进去 ——
「每花 2 点触发一次」在上状态前已花 3 点时，上状态后第 1 点就触发。

## 改法

按范围各记一份基线（懒记法：第一次看到消耗时把「已花掉多少」写进去，换回合 / 换战斗合计变小时自动重记）：

```csharp
private int __spentBaseCombat = -1;                                  // 状态 / 遗物上都有
int __spentNowCombat = __spentBefore + amount;                        // 含这一次一共花掉多少
if (__spentBaseCombat < 0 || __spentNowCombat <= __spentBaseCombat)
    __spentBaseCombat = __spentBefore;                                // 施加那一刻的基准
if ((__spentBefore - __spentBaseCombat + __energySpentIdx + 1) % 2 == 0) { … 触发 … }
```

* 填 N（每累计花 N 点触发一次）→ 只数基准之后的部分；
* 填 0（只触发第一次）→ `__spentBefore - __spentBase == 0 && amount > 0`；
* **遗物**那条链同样加了（`SpendTickLine` 的 `!= 0) continue;` 结构保留，只把基准补进判断）。

## 验收

* 探针 `sacprobe spenddump`（状态 0 档 / 状态 N 档 / 遗物 0 档）：
  三条都生成 `__spentBase*` + `__spentNow*` + 带基准的判断。
* 自检：**4552 PASS / 0 FAIL**（几条写死旧文本的断言按新写法更新）。
* 只出更新包（`-NoBundle`）。
