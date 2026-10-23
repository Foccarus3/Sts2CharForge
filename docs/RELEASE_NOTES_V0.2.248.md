# V0.2.248 — 「减少指定卡牌类别的费用」（像腐化 / 刺破帷幕那样）

## 用户要求

> 减少指定卡牌类别的费用（类似腐化，刺破帷幕这些）

## 在哪配

「**自定义状态**」页 → 那只状态下面多了一块：

> **费用修正（像腐化 / 刺破帷幕那样，让某一类牌变便宜）**
> 哪一类牌［范围下拉］ 减［数值］费 ☐ 按层数（每层各减这么多）

* **哪一类牌**：和别处的「范围」**同一份候选** —— 不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 / 任务 /
  **自定义关键词那一组** / **每个组**。
* **减 N 费**：默认 1；**填 0 = 直接变成 0 费**（腐化那种）。
* **按层数**：勾上就按这个状态的层数算（3 层 × 每层 1 费 = 减 3 费）；不勾就是「有这个状态就减固定那么多」（腐化）。
* **范围留空 = 不启用**：一个字符都不生成（老存档逐字不变）。

## 生成的是什么

本体同一个钩子（腐化 Corruption / 刺破帷幕 Veilpiercer / 免费攻击 FreeAttackPower 用的都是它）：

```csharp
public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
{
    modifiedCost = originalCost;
    if (card.Owner.Creature != base.Owner) return false;   // 只改你自己的牌
    if (!(card.Type == CardType.Skill)) return false;      // ← 你选的范围（组会展开成 card.Id == ModelDb.Card<…>().Id）
    modifiedCost = 0m;                                     // ← 填 0 时；填 1 时是 Math.Max(0m, originalCost - 1m)
    return true;
}
```

两条和本体一致的硬规矩：

* **只改你自己的牌**（`card.Owner.Creature != base.Owner` 直接放行）—— 敌人身上的牌不受影响；
* **最低减到 0**，不会变成负费（`Math.Max(0m, …)`）。

## 不静默

* 范围选了**空组**（组里一张牌都没勾）→ 校验器**警告**：「判据永远不成立，这类牌的费用根本不会减」；
* 填了负数 → **警告**：按「变成 0 费」处理（减费只往 0 走，不会加费）；
* 填 0 又勾了「按层数」→ **提示**：「0 乘几都是 0，已按不变处理」；
* 范围 Id 认不出来（手改 JSON）→ 校验器报错，生成端不写这段（不静默生成错判据）。

## 验收

* 自检：开发版 **4312 PASS / 0 FAIL**（新增 6 条：没配时不生成 / 技能变 0 费 / 减 1 费 / 按层数 /
  组范围展开 / 空组警告）。
* 真编译：把「技能牌变 0 费」这只状态塞进探针工程 → 生成整套工程 → `dotnet build` **0 个错误**。
* 对拍：Sample 113 / WhiteCloak 412 个文件**逐字节相同**（这两份都没配费用修正）。
