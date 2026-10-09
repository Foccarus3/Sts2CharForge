# V0.2.92 更新说明

> 队列第③项做完：**无色卡** —— 卡牌页勾一下，这张牌就进本体的无色卡池、用本体的无色卡框。

## 「卡牌」页多了「无色卡」

勾上「**无色卡**」之后这张牌会走一套完全不同的归属：

- **进本体的无色卡池**（`ColorlessCardPool`），和本体那些无色牌放在一起；
- **卡框 / 能量图标用本体无色那套**（`card_frame_colorless`、`"colorless"` 能量前缀）——
  这张牌的卡框颜色、牌堆底色都由**本体无色卡池**决定，「卡牌」页的卡框颜色对它不生效（设计如此）；
- **不进你角色的卡池**：战斗奖励 / 商店抽的是你角色的卡池，所以它不会自己冒出来，
  只有「无色卡池」相关的来源（本体那些按无色卡池发牌的遗物 / 事件 / 药水）才会给到玩家。
  所以「加入卡池」那个勾对无色卡没作用（校验器会给一句提示，不静默）；
- 照样能被你自己的效果引用：「生成卡牌」的范围、「目标卡」下拉（生成 / 获得卡牌（全局）/ 获得卡牌奖励 /
  指定卡 / 升级卡牌）里都搜得到它。

## 怎么做进去的（以及为什么只能这么做）

本体的 `ColorlessCardPool` 是 `sealed` 的、成员表**硬编码在类里**，卡池成员只能靠
`ModHelper.AddModelToPool(池类型, 卡类)` 在**游戏初始化之前**登记 —— 晚了本体自己会抛
`it's too late! You must add content before the game is initialized.`。

所以生成器会在 `cs/ModEntry.cs` 的 `Initialize()` 里为每张无色卡写一句：

```csharp
var __colorlessPool = typeof(MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool);
MegaCrit.Sts2.Core.Modding.ModHelper.AddModelToPool(__colorlessPool, __t你的卡类);
```

包在 `try/catch` 里、按类名反射查找（找不到只记一行警告），绝不连累角色注册。
这是「卡池成员一律在自己池类的 `GenerateAllXxx()` 里给出」那条规矩的**唯一例外**，
`cs/ModEntry.cs` 里也写了原因。**没勾无色卡的存档，生成结果一个字符都不变。**

## 配套（这一版一起做的）

- **生成端**：无色卡从「本角色卡池的成员表」里排除（一张卡只能属于一个池，否则
  `CardModel.Pool` 反查结果不确定 —— 本体会直接抛 `InvalidProgramException`），
  改由 `cs/ModEntry.cs` 登记进本体无色卡池；
- **回读**：`--recover` 扫 `cs/ModEntry.cs` 里那些 `AddModelToPool(__colorlessPool, …)` 语句，
  把对应的卡还原成「无色卡」标记，不会丢配置、也不会刷「没认出来」；
- **校验器**：稀有度是 **Ancient / Event / Quest / Token** 时**报错拦住**
  （本体 `CardFactory` 对这几档有特殊处理，会把它们当成「无色」用在别的用途上，放进无色卡池会出怪事；
  无色卡请用 Common / Uncommon / Rare）；另外两条**提示**说清「不进你角色卡池 / 用本体无色卡框」；
- **界面**：卡牌页「无色卡」勾选框；
- **自检**：8 条断言（进/不进哪个池、`cs/ModEntry.cs` 那句登记、老配置零变化、
  两条校验器规则、生成 → 真编译 → 回读往返 + 「没认出来」为 0）。
