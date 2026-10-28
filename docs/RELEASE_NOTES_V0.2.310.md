# V0.2.310 — 「升级本场战斗中临时增加的牌」在自定义状态里能用了

你这次的一键构建日志里两条拦路的错误，第一条是**生成端没接**（真 bug，这一版修了），
第二条是**配置没选**（下面写了怎么修）。

## ① 修好的：自定义状态「操控现实」→「生成卡牌时」→「升级本场战斗中临时增加的牌」

```
[错误] 自定义状态「操控现实」「生成卡牌时」的第 1 条效果是「升级本场战斗中临时增加的牌」，
       生成端还没给「自定义状态」那条链实现这个效果种类（会什么都不做），所以先拦下来。
```

真因：「升级本场战斗中临时增加的牌」这条**卡牌的效果列表里一直有**（读战斗历史里的
`CardGeneratedEntry` 然后 `CardCmd.Upgrade`），但**自定义状态**那条链既没实现、支持名单也没放行
（`PowerTriggers.SupportedEffectKinds`），所以校验器按规矩把整次生成拦住。

这段本体 API 其实**一个卡牌上下文都不碰**（只读战斗历史 + `CardCmd.Upgrade`），状态钩子里照样能做。
改法：

* 复用卡牌链的同一份实现（`CSharpCodeGen.EmitUpgradeGenerated` 开成 `internal`，不抄第二遍）；
* 状态链 `case "UpgradeGenerated"`，并且勾了「数值 = 本状态的层数」时上界换成 `(int)base.Amount`；
* 支持名单加上它（状态 + 锻造节点图两处）。

生成出来的样子（状态链）：

```csharp
int __upgGen = 0;
foreach (var __gen in CombatManager.Instance.History.Entries.OfType<CardGeneratedEntry>().ToList()) {
    CardModel? __genCard = __gen.Card;
    if (__genCard is null || __genCard.IsUpgraded || !__genCard.IsUpgradable) continue;
    if (__upgGen >= 1) break;              // 最多升 1 张（勾了「数值 = 层数」时是 (int)base.Amount）
    CardCmd.Upgrade(__genCard);
    __upgGen++;
}
```

## ② 你要自己修的一条：卡牌「"还真当我在你身后了"」的「暴露」没选状态

```
[错误] 卡牌「“还真当我在你身后了”」 的「暴露（清空格挡与人工制品，再施加 N 层状态；0 = 不生效）」
       没有选「增益 / 减益」（要施加哪个状态）。
```

「暴露」= 先清掉目标身上的格挡与人工制品，**再施加 N 层某个状态** —— 那个状态必须你选。
打开那张卡 → 那条「暴露」效果 → 在「增益 / 减益」那一栏选一个状态（例如虚弱 / 易伤，或你自己的自定义状态）。

## 验收

* 探针 `sacprobe upggenpower`（自定义状态「生成卡牌时」→ 升级临时牌，另加一块同样的锻造图）：
  两条链都支持、校验器**相关错误 0 条**、生成 `CardCmd.Upgrade`、整份工程 **`dotnet build` 0 错误**。
* 自检：**4502 PASS / 0 FAIL**（+4 条 V0.2.310 断言：两处放行 / 不再拦 / 生成 CardCmd.Upgrade / 按层数当上界）。
* 红线：Sample 113 个文件 / WhiteCloak 412 个文件**逐字节相同**（只有 `.sln` 的 GUID 不同）—— 卡牌链那份输出一个字没变。
* 只出更新包（`-NoBundle`）。
