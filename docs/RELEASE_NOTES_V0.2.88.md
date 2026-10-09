# V0.2.88 更新说明

> 「下回合生效」这一版补上了**生成类**的四种半（生成卡牌 / 获得·变化·删除·升级牌组里的牌）。
> 变化卡牌、复制卡牌还差一点（它们的发射器要先参数化），下一轮收尾。

## 新增：「下回合生效」支持生成类效果

以前只有五种（格挡 / 抽牌 / 能量 / 额外资源量 / 施加扣除状态）。现在这几种也能勾了：

| 效果 | 延迟状态里做的事 |
|---|---|
| **生成卡牌**（指定卡 / 按范围随机 / 多选1，含「生成出来的卡怎么处理」那一整套） | 下回合开始时 `CardPileCmd.AddToCombatAndPreview<卡>`（或 `CardFactory.GetDistinctForCombat` 那条随机路），张数 = 延迟状态的层数 |
| **获得卡牌（全局）** | 下回合开始时 `RunState.CreateCard(...)` + 加进牌组 |
| **变化卡牌（全局）** | 同上（换成 `CardCmd.Transform` 那条路） |
| **删除卡牌（全局）** | 下回合开始时走 `CardSelectCmd.FromDeckForRemoval` |
| **升级牌组里的牌（全局）** | 下回合开始时按张数升级牌组 |

实现方式：勾上之后，卡这边**不再立刻生成**，而是给自己挂一个延迟状态
`<角色>ForgeDelayed<Gen|AddGlobal|TransGlobal|RemoveGlobal|UpGlobal><卡类名>`：

```csharp
public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
{
    if (player != base.Owner.Player || base.Amount <= 0) return;
    await CardPileCmd.AddToCombatAndPreview<Shiv>(base.Owner, PileType.Hand, base.Amount, base.Owner.Player);
    await PowerCmd.Remove(this);
}
```

要点：

- **层数 = 张数**（所以「生成 2 张」就是挂 2 层）；
- 延迟状态里 `base.Owner` 是 **Creature**，所以发射器统一传 `base.Owner.Player` / `base.Owner`（和自定义状态那条链同一套）；
- **类名里带种类 + 那张卡的类名**（`ForgeDelayedGenShiv` / `ForgeDelayedAddGlobalShiv`…）——
  不然几个生成类效果会撞成同一个 `ForgeDelayedPower`（同一个类名生成两次 = CS0102）；
- **回读**：卡这边那句 `PowerCmd.Apply<…>` 上写着种类（从类名前缀认），
  张数/范围/附加处理从紧挨着的 `CET:SpawnPick=…` 标记行认，**目标卡**从类名尾巴认
  （探针实测：生成 → 真 `dotnet build` → 回读，种类 / 下回合 / 目标卡 / 张数全都对得上，0 条「没认出来」）；
- **遗物 / 药水 / 自定义状态**那三条链还没接生成类的延迟 → 勾了会被配置检查**拦住并说清**
  （不是静默改成「立刻生成」）。

## 还差一点

**变化卡牌 / 复制卡牌**的「下回合生效」：这两个发射器里的 `base.Owner` 是写死的 Player，
要先把它们参数化（和生成卡牌一样接受「玩家表达式」）才能放进延迟状态。下一轮做完这两条，
「下回合生效」就齐了。

## 顺带

- 勾选框的显隐、校验器的判据都读同一个 `EffectCatalog.HonorsNextTurn(kind)`，不会再出现「勾了没反应」。
