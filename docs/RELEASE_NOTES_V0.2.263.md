# V0.2.263 — 欧洛巴斯之触 / 古老牙齿真正生效 + 牙齿卡不再进尘封魔典 + 下拉不再全是「范例卡」

## 你报的三件事

1. 「欧罗巴斯的两个遗物都没办法正常生效」
2. 「勾选替换古老牙齿的先古反而会出现在达弗的尘封魔典里」
3. 「替换初始卡的下拉选项中怎么全是范例卡」

## ① 为什么「没生效」：我只改了道具的**展示**，没接住**真正发效果**的那一步

本体的流程是两段：

* `SetupForPlayer(player)` —— 只在道具上写「我的初始卡是 X / 会给 Y」（给悬停提示用）；
* `AfterObtained()` —— **真正动牌组 / 换遗物**的地方。

上一版（V0.2.260）我只挂了前一段，而后一段里本体自己又查了一次它那张写死的表：

```csharp
// ArchaicTooth.AfterObtained（本体）
CardModel starter = GetTranscendenceStarterCard(base.Owner);   // 只认原版 5 张，我们的卡 → null
await CardCmd.Transform(starter, GetTranscendenceTransformedCard(starter));
```

`GetTranscendenceStarterCard` 找不到我们的卡 → 返回 `null` → 换牌那一步什么也没换；
`TouchOfOrobas` 同理（`UpgradedRelic ?? 查表` → 查不到 → 给你「头环 Circlet」，日志里那条
`Key 'CIRCLET' not found` 就是它）。

**这一版把 `AfterObtained` 也接过来**（两个都补）：

```csharp
// 古老牙齿：直接把指定初始卡变成这张先古卡（升过级的初始卡，先古卡也跟着升级）
CardModel ancient = tooth.Owner.RunState.CreateCard<你的先古卡>(tooth.Owner);
if (starter.IsUpgraded) CardCmd.Upgrade(ancient);
await CardCmd.Transform(starter, ancient);

// 欧洛巴斯之触：把初始遗物换成你指定的那只
await RelicCmd.Replace(starter, ModelDb.Relic<你的遗物>().ToMutable());
```

原来的 `SetupForPlayer` 前缀保留（悬停提示里显示的还是你这张卡 / 这只遗物）。

## ② 尘封魔典为什么会给到「古老牙齿专用」的那张先古卡

本体的尘封魔典是从卡池里随机挑一张先古卡，只排掉**它自己**那 5 张「古老牙齿换出来的卡」
（`DustyTome.cs:53` `!ArchaicTooth.TranscendenceCards.Contains(c)`）—— 我们的卡不在那 5 张里。

**修法**：只勾了「替换古老牙齿…」、没勾「作为尘封魔典给的先古卡」时，额外生成一个前缀，
按本体同一套逻辑重挑一次，把**古老牙齿专用**那张排掉（一张都不剩就交回本体）。

## ③ 下拉「全是范例卡」

那个「被古老牙齿换掉的初始卡」下拉以前把**所有牌**按存档顺序平铺，新建存档自带的
`打击 / 防御`（本体基础牌）排在最前，看起来就像「全是范例卡」。现在：

* 排序：**你的初始牌** → 你的其它牌 → 本体基础牌；
* 每一行都标清 `（类名）｜ 你的初始牌 / 你的牌（不在初始卡组）/ 本体基础牌（打击 / 防御这种）`；
* 第一项仍然是「（自动：初始卡组里第一张）」。

## 验收

* 自检：**4382 PASS / 0 FAIL**（新增 3 条：古老牙齿有 `AfterObtained` 补丁 / 欧洛巴斯之触有
  `AfterObtained` 补丁 / 尘封魔典随机池排掉了牙齿专用卡）。
* 真编译：探针工程同时勾上「古老牙齿 + 尘封魔典 + 欧洛巴斯」→ `dotnet build` **0 个错误**。
* 对拍：Sample 113 / WhiteCloak 412 个文件**逐字节相同**。

> 你这份 `godot (32).log` 里只有别的 mod 的 VFX 报错和 `CIRCLET` / `TOUCH_OF_OROBAS` 的本地化缺键
> （那是另一个 mod 覆盖了 relics 本地化表，不是我们的），没有我们代码里的异常 ——
> 所以这次是按本体源码 + 生成端复现定位的：只挂 `SetupForPlayer` 时，`AfterObtained` 仍然会走本体那张表。
