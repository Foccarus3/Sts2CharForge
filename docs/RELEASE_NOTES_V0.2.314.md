# V0.2.314 — 「升级牌组 / 变化牌组（全局）」数值 0 也只动一张

上一版（V0.2.313）把「删除卡牌（全局）」数值 0 改成只删一张，你确认另外两个也要「只动一张」，
这一版补上。

## 改了什么

| 效果（数值 0 = 这张牌自己） | 以前 | 现在 |
|---|---|---|
| 删除卡牌（全局） | 同名副本**全删** | 只删一张（V0.2.313） |
| 升级牌组（全局） | 同名副本**全升** | 只升一张 |
| 变化牌组（全局） | 同名副本**全变** | 只变一张 |

生成出来的差别就一处 `Take(1)`：

```csharp
// 以前：Where(c => c.IsUpgradable && c.Id == this.Id).ToList()
foreach (CardModel c in PileType.Deck.GetPile(...).Cards
             .Where(c => c.IsUpgradable && c.Id == this.Id).Take(1).ToList()) CardCmd.Upgrade(c);

// 以前：Where(c => c.Id == this.Id).ToList()
foreach (CardModel c in PileType.Deck.GetPile(...).Cards
             .Where(c => c.Id == this.Id).Take(1).ToList()) { … CardCmd.Transform … }
```

**想一次处理全部**仍然可以：把那条效果的「取几张」切成 **「全部」**（三档都有这一项，
生成的是不带 `Id ==` 的整批版本，这次没动）。

## 验收

* 自检：**4511 PASS / 0 FAIL**（+2 条 V0.2.314 断言：升级只升一张 / 变化只变一张）。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）——
  这两份存档里没有「数值 0 升级 / 变化自己」的牌，所以文本不变。
* 只出更新包（`-NoBundle`）。

## 说明

「数值 0」= 这张牌自己，删 / 升 / 变都只动**一张**同名副本。
「数值 ≥ 1 + 自己选 / 随机」那几档本来就是一招一张，没动。
