# V0.2.273 — 「从消耗牌堆拿牌」的范围界定真的生效了（你报的那条，终于修对）

## 先认错

上一版我判断成「这张牌是锻造图做的」——**错了**。核对你的存档：

```
状态 守望过去 (TheShadowPower9)：ForgeGraph = null
卡   守望过去 (TheShadowCard99) ：ForgeGraph = null
（整份存档：状态里只有 3 个有图、13 个没图）
```

## 真正的原因

「自定义状态」那条链里，「从某一摞拿牌到手牌」的生成代码是**单独一份**（`CustomPowerGen.cs:1949`），
它拼 `CardSelectorPrefs(...)` 时**一个过滤谓词都没传**：

```csharp
// 修之前（范围界定整段丢掉 → 选牌界面什么牌都能选）
CardSelectCmd.FromCombatPile(choiceContext, PileType.Exhaust.GetPile(base.Owner.Player), base.Owner.Player,
    new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, (int)(1m)))
```

同一个文件里别的选牌档（升级牌那档、随机目标那档）都传了谓词，只有这一档漏了 —— 所以你设的「范围界定 = 状态」白设。

## 修好之后（拿你的 `Shadow.json` 重新生成，原文照抄）

```csharp
var __fgTake = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.Exhaust.GetPile(base.Owner.Player),
    base.Owner.Player, new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, (int)(1m)),
    c => c.Type == CardType.Status || c.Rarity == CardRarity.Status)).ToList();
//  ↑ 第 5 个参数：只列状态牌
```

* 谓词用的是**和别处同一份** `CardScopePredicate`（类型 / 自定义关键词 / 组 / 「只对这张卡」全都认）；
* 没设范围的老配置 → 调用**一个字都不变**（多传参数的情况只在你真的填了范围时出现）；
* 「范围界定」里选**组 / 自定义关键词**同样有效（生成期解析成显式判断）。

## 验收

* 自检：**4436 PASS / 0 FAIL**（新增 2 条：范围界定 = 状态 → 选牌调用带 `c => c.Type == CardType.Status`；
  对照「没设范围」→ 不多传参数）。
* **用你的存档实测生成**：那一行已经带上过滤谓词（上面贴的就是生成结果原文）。
* 对拍：Sample 113 / WhiteCloak 412 个文件**逐字节相同**（只有 `.sln` 的 GUID 和 `_files.txt` 不同）。
* 编译安全：`CardRarity.Status` / `CardType.Status` 都在本体枚举里（`CardRarity.cs`:12）。
