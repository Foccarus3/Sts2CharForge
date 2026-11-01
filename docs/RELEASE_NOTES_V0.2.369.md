# V0.2.369 — 图上「击晕」补 `await`；顺便把「击晕全部敌人」那件事说清

## 一、修了什么

锻造图上的【效果】= 击晕，以前生成的是：

```csharp
foreach (Creature __fgFoe in ...) CreatureCmd.Stun(__fgFoe);      // ← 返回的 Task 被丢掉了
```

现在：

```csharp
foreach (Creature __fgFoe in ...) await CreatureCmd.Stun(__fgFoe);
```

单体击晕**目前**真正生效的那一步是同步的，所以以前看起来也能用；但把返回的 `Task` 丢掉是个随时会咬人的坑
（本体如果改成本体那种「晕眩过场 + 回调」的写法，不 await 就会在效果还没跑完时继续往下走）。
卡牌页 / 遗物页 / 药水页 / 状态页那四条链一直都是 `await` 的，只有图上这一处漏了。

## 二、你报的「对自己击晕 → 变成击晕所有敌人」：查过了，是配置

用你给的 `rukayukiking.json` 生成出来逐条核对：

1. **「跪下」(`RukayukiPowerhizama`) 的击晕效果确实存的是「自己」**，生成的是
   `await CreatureCmd.Stun(base.Owner);` —— 只晕「挂着这只状态的那只怪」，**这条没 bug**。
2. **「跪下」是被「遥拜之人」(`RukayukiPower17`) 用「全部敌人」施加的**：
   `TargetSide: AllEnemies` + `PowerId: RukayukiPowerhizama` → **每只敌人都挂上了「跪下」**，
   各自被攻击时各自晕自己 → 看起来就是"击晕所有敌人"。
3. 另外「遥拜之人」自己的图里，那个击晕节点的目标**也是「全部敌人」**
   → 生成 `foreach (Creature __fgFoe in EnemyList) Stun(__fgFoe)` —— 突破任意一只敌人的格挡会晕全体。

想要「只晕被打 / 被破防的那一只」，改两处目标即可：
* 「跪下」的施加目标：**指定敌人**；
* 图上击晕节点：目标改成 **指定敌人**（或用【对象】模块接到「指向对象」口，生成端给的 `__fgTarget`
  **正好就是被打破格挡的那只**）。

## 自检

**4701 PASS / 0 FAIL**（+1 条：图上击晕必须生成 `await CreatureCmd.Stun(`，且不允许出现不带 await 的旧写法）。
