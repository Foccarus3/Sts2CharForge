# V0.2.188 更新说明

> 你给的那张卡（**挖肉补疮 / BugkillerCard56**）我把它生成出来看了一遍 —— **两条效果都是坏的**，
> 而且坏在同一个地方：**目标卡选的是本体卡，节点图那条链认不出来**。
>
> 你的配置（存档里就是这个）：
>
> ```
> 效果①：变化卡牌  3 张 · 目标卡 = MinionSacrifice（本体「昆虫防御」）· 自己选 · 放到手牌
> 效果②：生成卡牌  3 张 · 目标卡 = Infection（本体「感染」）      · 放到弃牌堆
> ```
>
> **旧版生成出来的代码**（就是这个 bug 的样子）：
>
> ```csharp
> // 效果①：目标卡没认出来 → 退化成「随机变化」
> foreach (CardModel c in 选中的3张) await CardCmd.TransformToRandom(c, base.Owner.RunState.Rng.CombatCardSelection);
>
> // 效果②：整条被丢掉，只剩一行注释
> // ⚠【效果】生成卡牌：这条效果**缺必填的东西**（状态 / 目标卡…），生成时被跳过了 —— 见「配置检查」里那条错误。
> ```
>
> **这一版生成出来的代码**：
>
> ```csharp
> // 效果①：变成指定的本体卡
> foreach (CardModel c in 选中的3张)
>     await CardCmd.Transform(c, base.Owner.RunState.CreateCard(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.MinionSacrifice>(), base.Owner));
>
> // 效果②：照常生成 3 张
> for (int i = 0; i < 3; i++) { CardModel gen = base.CombatState.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Infection>(base.Owner);
>                               await CardPileCmd.AddGeneratedCardToCombat(gen, PileType.Discard, base.Owner); }
> ```
>
> 升级后那张布（「昆虫防御+」）同样修好了 —— 它以前也是「随机变化变成随机卡 + 生成效果整条消失」。

---

## 一、真因：节点图的「目标卡」只在**自己存档的卡**里找

生成端那一行是这样的（`ForgeGraphGen.cs`）：

```csharp
// 旧版
var targetCard = p.AllCards.FirstOrDefault(c => c.ClassName == spawnCardId);   // ← 只看自己的卡
if (targetCard is not null) spawnCardClass = ...;                              // 本体卡 → 找不到 → 留着 null
```

`spawnCardClass` 是 null 之后：

* 【变化卡牌】走「没有目标卡」那条分支 → `CardCmd.TransformToRandom`（**随机变化**）；
* 【生成卡牌】在 switch 里 `spawnCardClass is null ? null : …` → **整条丢掉**，
  只在代码里留一句「缺必填的东西」的注释（而且这句话会把人带偏：你明明选了目标卡）。

**这一版**：目标卡要么是自己的卡（用它的类名），要么是**本体卡**（按你选的那个类名，交给
`CardTypeExpr` 写成全名 `MegaCrit.Sts2.Core.Models.Cards.<X>`）—— 所以本体卡目标三个都认：

| 你的存档里用到的本体卡目标 | 现在是 |
|---|---|
| `MinionSacrifice`（昆虫防御） | 认得出 |
| `Infection`（感染） | 认得出 |
| `Dazed`（眩晕） | 认得出 |

（你存档里另外两处「生成卡牌」本来就写的是自己的卡 `BugkillerAncient63` / `BugkillerCard20`，一直是对的。）

## 二、顺带：这类「目标卡」写错名字现在生成前就报错

以前这种情况是**静默**的（退化成随机 / 整条丢掉），名字写错了也只在编译时才炸
（`CS0246 未能找到类型`，报错信息完全看不出是配置问题）。
这一版给节点图的【变化卡牌】/【生成卡牌】也接上了卡牌页早就有的那条检查：

* 名字在本存档和本体卡牌表里都找不到 → **错误**（说清是哪个模块、哪个名字）；
* 只差大小写 → **错误**并给出正确写法；
* 本机读不到本体卡牌表（没配解包工程）→ **提示**（不拦生成）。

## 三、关于「抽不上来 / 会卡死」

这一版修的是「**卡根本不是你配的那张**」这件事（变成随机卡、第二条效果整条没生成）。
「卡死」我在这两份日志里没抓到直接证据（`godot (14).log` 里你打出这张牌的下一行就是正常的
「选择了 3 张牌」，之后战斗照常继续）。所以麻烦装上这版之后：

1. 重新生成 + 一键构建 + 替换 mod（**必须重做这一步**，否则跑的还是旧代码）；
2. 再打一次这张牌看看；
3. 如果还会卡死，把新的日志发我，并说一句卡在哪一步 —— 是「选牌界面不消失」、
   「牌打出去了但回合走不下去」，还是「点图鉴/别的界面卡住」。我按那一处再查。

> 顺带一提：旧版那条「随机变化」是会去本体卡池里随机抓一张的，
> 抓到的卡和你配的完全无关 —— 在装了很多 mod 的存档里，这也确实可能是「卡死」的来源之一
> （本体 `CardCmd.Transform` 在「变出来的卡拿不到手牌节点」「候选为空」时是直接抛异常的，
> 抛在卡牌打出的中间就是一轮卡死）。这版把这条随机路径从你这张牌上拿掉了。

## 已经验过

| 项 | 结果 |
|---|---|
| 你的存档（`BugkillerMod.json`）**真生成 + 真编译** | 生成工程 `dotnet build` **0 错误**（33 个警告），`BugkillerCard56.cs` 里两条效果都按上面的方式生成 |
| `--uicheck` | **3818 PASS / 0 FAIL**（新增 3 条：变化目标=本体卡时生成本体卡 / 生成目标=本体卡时照常生成 / 校验器不误报） |
| `--samplegen` | 对齐 V0.2.143 基线：**113 → 114，5 处差异**（新增 `cs/ForgeCardLibrary.cs`(V0.2.187) ＋ 4 处既有差异）—— 样例存档没有锻造卡，本版对它零影响 |
| 上一版（V0.2.187） | 同一份包：卡牌图鉴对 mod 角色的补丁（点图鉴就 `KeyNotFoundException` 卡死）＋ 改类名会让旧存档失效的提醒 |
| 上上版（V0.2.186） | 同一份包：覆盖血条的诊断日志 + 12px 下限 + 两个入口；没上传图标的自定义状态给中性占位图 |
