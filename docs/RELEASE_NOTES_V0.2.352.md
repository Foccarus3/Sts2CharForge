# V0.2.352 — 修「转化为先古卡」不生效（拿类名比 `Id.Entry`，永远不相等）

## 真因（有源码依据）

游戏里 `CardModel.Id.Entry` **不是类名**，是 `ModelDb.GetEntry(type)` = `StringHelper.Slugify(type.Name)`
—— 驼峰拆开 + **全大写**（`Sesai` → `SESAI`）。

而生成器里「按组 / 关键词筛牌」那段（`CSharpCodeGen.SpawnPoolCond`，专门给「转化为先古卡」用）以前写的是：

```csharp
new[] { "Sesai" }.Contains(c.Id.Entry)      // ❌ 类名 vs 全大写 slug，永远不相等
```

于是**这一组一张都匹配不上** → `CardCmd.Transform` 拿到的是空列表 → 什么都不变、也不报错（静默失效）。
你在游戏里看到的就是「变化先古牌不生效」。

## 改法

牌的身份改用 **ModelId** 比（和生成器里其它「组条件」同一个口径）：

```csharp
// 单张
c.Id == ModelDb.Card<Sesai>().Id
// 一组
(c.Id == ModelDb.Card<A>().Id || c.Id == ModelDb.Card<B>().Id)
```

本体按类名算 Id、战斗里的牌是规范模型的克隆（Id 相同），所以这个比较是稳的。

## 拿你的存档验过

`sacprobe relicsrc rukayukiking.json Ysekai`（这轮新加的探针模式）生成出来的两行现在长这样：

```csharp
var __fgAncient1_0_src = PileType.Deck.GetPile(base.Owner).Cards
    .Where(c => c.IsRemovable && (c.Id == ModelDb.Card<Sesai>().Id)).ToList();
var __fgAncient1_1_src = PileType.Deck.GetPile(base.Owner).Cards
    .Where(c => c.IsRemovable && (c.Id == ModelDb.Card<Hikii>().Id)).ToList();
```

改之前是 `new[] { "Sesai" }.Contains(c.Id.Entry)`（永远匹配不到）。

## 注意（影响面）

这段条件只有「转化为先古卡」在用（`SpawnPoolCond` 唯一调用点），所以别的效果不受影响；
但如果你的**遗物触发时机**不是「获得时」，它也不会在战斗里跑 —— 那属于另一件事，改完这版先进游戏验一次。

## 自检

**4639 PASS / 0 FAIL**（新增 2 条：组条件必须是 `c.Id == ModelDb.Card<…>().Id` 且不能出现 `Id.Entry`；
生成的先古替换代码同样如此。老的那条断言原本就是把这个 bug 当成正确行为在验，已一并改正。）
