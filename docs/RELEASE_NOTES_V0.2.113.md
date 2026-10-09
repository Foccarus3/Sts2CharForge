# V0.2.113 更新说明

> 这批需求里的第 ③ 条：**额外资源量那只遗物在遗物页看不到 + 要能勾选「在游戏中隐藏」**。

## 一、遗物页现在能看见它、也能决定它在游戏里藏不藏

「额外资源量」启用后，工具会自动生成一只**载体遗物**（`<角色>ExtraResourceRelic`，稀有度 `Starter`）——
它只负责「每场战斗开始时发资源量」和「记录跨战斗继承」，不是藏品。这一版在遗物页左上角那块
「额外资源量遗物（自动生成的隐藏起始遗物）」里补了两样东西：

1. **它是谁**：`SevenExtraResourceRelic　｜　RelicRarity.Starter　｜　自动生成，不在下面的遗物列表里`
   —— 下面的遗物列表只列你自己配的遗物，所以以前你会觉得「怎么没显示」；
2. **「在游戏中隐藏（不显示在遗物栏里）」勾选框** + 一句说明。

## 二、「在游戏中隐藏」是怎么做的

本体**没有**「隐藏某个遗物」的接口：遗物栏（`NRelicInventory.Initialize`）就是

```csharp
foreach (RelicModel relic in _player.Relics) Add(relic, startsShown: true);   // 没有任何可见性判断
```

所以勾上之后，生成端会多写一个 `cs/HideCarrierRelic.cs`：

```csharp
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory), "Add")]
internal static class ForgeHideCarrierRelicPatch {
    [HarmonyLib.HarmonyPrefix]
    private static bool Prefix(RelicModel relic) {
        // 返回 false = 不跑原方法：这只遗物不进遗物栏（别的遗物照常）
        return relic is not SevenExtraResourceRelic;
    }
}
```

- **功能一个字都不变**：遗物照旧挂在角色身上（`Seven.cs` 的起始遗物里还在），
  开场发资源量、跨战斗继承、存档全都兼容 —— 只是不往遗物栏里塞节点；
- **只挡这一只**：`return relic is not <角色>ExtraResourceRelic;`，别的遗物（含你配的）照常显示；
- 顺带说明一件以前没写清的事：这只载体**必须**留在遗物池里（本体 `RelicModel.Pool` 是 `First(...)`
  查找，找不到会在选人界面直接报错），但它是 `Starter` 稀有度，奖励只会抽
  Common / Uncommon / Rare / Shop，所以不会被当成奖励发出来；
- **默认不勾**（和以前一样看得见）→ 没勾的存档生成结果**逐字不变**（红线）；勾了才多生成那个文件。

## 三、验证

- `--uicheck`：开发版与发布版都是 **2616 PASS / 0 FAIL**（新增 9 条：红线「没勾不生成」/
  启用但没勾照旧 / 勾了才生成 / 补丁打在 `NRelicInventory.Add` 上 / 只挡那一只 / 说明写清功能照常 /
  遗物页看得到它的身份 / 有那个勾选框 / 勾上真的改到配置）；
- 探针 `RunExtraResource` 追加「隐藏」→ 生成出 `cs/HideCarrierRelic.cs`（前缀补丁 + `return relic is not
  SevenExtraResourceRelic;`）、角色里照旧注册那只载体遗物 → 工程 **真 `dotnet build` 0 错误**
  （同时验证了 `MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory` 这个类型名在本体里存在）；
  整轮探针 **27 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.113.zip`。

## 四、这批剩余的

| # | 需求 | 状态 |
|---|---|---|
| ① ② ④ ⑤ ⑥ | 见前几版 | ✅V0.2.108~112 |
| ③ | 额外资源量的隐藏遗物在遗物页列出来 + 「在游戏中隐藏」勾选 | ✅ 本版 |
| ⑦ | 无色牌卡面丢失 | 下一版 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | 待做 |
| ⑨ | 新效果「放置卡牌」（数值 0 = 自己；否则从五处挑一张放到五处） | 待做（做法已确认） |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 待做（做法已确认） |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
