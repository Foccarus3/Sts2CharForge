# V0.2.294 — 选角色界面找不到角色（模组整体加载失败）

## 日志里的真凶

```
[ERROR] ExampleMod - 加载失败
[ERROR] HarmonyException: Patching exception in method
        System.Void MegaCrit.Sts2.Core.Models.Relics.DustyTome::SetupForPlayer(Player p…)
  ---> System.Exception: Cannot get result from void method
```

`DustyTome.SetupForPlayer` 在**本体里返回 void**（我先查了源码确认）：

| 遗物 | 本体签名 |
|---|---|
| `ArchaicTooth.SetupForPlayer` | `public bool` |
| `TouchOfOrobas.SetupForPlayer` | `public bool` |
| **`DustyTome.SetupForPlayer`** | **`public void`** |

V0.2.280 我为了修「先古牙齿不显示」，给**三个** SetupForPlayer 补丁都加了
`ref bool __result` + `__result = true` —— 对着 bool 的那两个是对的，
但 `DustyTome` 那个是 **void**，Harmony 在**打补丁那一刻**就抛异常：

> Cannot get result from void method

于是整个模组加载失败 → 角色在选人界面直接消失（不是「不见了」，是模组没起来）。

## 修法

只给两个 **bool** 的补丁保留 `__result = true`；`DustyTome` 那两处（尘封魔典的
「按我们的卡池挑先古卡」和「指定先古卡」两档）恢复成不带 `__result` 的写法。

## 验收

* 自检：**4453 PASS / 0 FAIL**（4679 行无异常）
* 生成端对比：`ArchaicTooth` / `TouchOfOrobas` 仍是 `Prefix(…, Player player, ref bool __result)` + `__result = true;`；
  `DustyTome` 恢复成 `Prefix(…, Player player)`
* 只出更新包（`-NoBundle`）

## 那位朋友要做的

换上 V0.2.294 后**重新一键构建 + 安装**（旧的 `ExampleMod` 里那份补丁是坏的，必须重新生成）。
