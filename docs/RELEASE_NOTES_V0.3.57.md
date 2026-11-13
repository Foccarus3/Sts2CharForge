# V0.3.57 — 自定义状态效果种类 42 → 65

## 问题

自定义状态的「效果种类」下拉比卡牌页少很多：状态链只有 42 种，卡牌页 71 种。

## 处理

状态链补齐 **23 种**，现在 **65 种可用**：

```text
DoublePower / ForbidBlock / ForbidHeal / Expose / InstantKill
CheapenDraw / CostDown / OverdraftEnergy / RaiseResourceCap / EndTurn
Scry / ScrapeDraw / FillHand / TransformCard / PlaceCard / GiveKeyword / TimesUp
AncientTranscend / AncientRelicSwap
PetAttack / PetDamageByMaxHp / PetDamageByCurHp / PetDamageByMissingHp
```

生成端在 `CustomPowerGen.EmitEffect` 里复用卡牌 / 遗物链已有的发射器，
状态钩子的上下文按 `base.Owner`（Creature）+ `base.Owner.Player` 适配。

## 仍然保留的 6 种「卡牌专属」

```text
CheapenSelf / CheapenSelfOnDraw / CopyCard
ReplayCard / TurnEndPlay / ReturnSelfToHand
```

这 6 种要的是「刚刚打出的这张牌」本身，状态触发器里没有这个对象。
现在会明确拦下并说明原因，不再显示成「生成端没实现」。

## 验证

| 项 | 结果 |
|---|---|
| 状态效果种类 | **65**（原 42） |
| 新效果状态探针真编译 | **0 错误** |
| 伙伴攻击 / 按生命值攻击探针 | PASS |
| 先古遗物替换探针 | PASS |
| 开发版 `--uicheck` | **4911 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4910 PASS / 0 FAIL** |

⚠ 旧工程需要重新生成；状态的 `CustomPowerGen` 代码在生成时写入。
