# V0.2.180 更新说明

> 这一版修的是**同一个根因的三个症状**（你连着发来的三份日志），外加一条**「模组加载失败 → 进游戏找不到这个角色」**的硬伤。
> 两件事都已经在本地验过（探针 + `--uicheck` + 生成出来的工程真编译 + 样例工程逐字节对拍）。

---

## 一、类名里出现「3 个以上连续大写」时，本地化键 / 资源文件名全都会错位

### 你看到的现象（三份日志、三张面孔）

| 你的原话 / 日志 | 真身是哪一处 |
|---|---|
| 「**选人页看的是对的，但选之后变成其他角色了**」（点他那个角色就直接变铁甲战士） | 遗物键 `EVERelic1` → 游戏查 `E_VE_RELIC1.title`，我们写的是 `E_V_E_RELIC1.title` |
| 「**卡牌信息丢失**」（卡没名字、没描述、卡面空白） | 卡键 `EVECard19` → 游戏查 `E_VE_CARD19.title`，我们写的是 `E_V_E_CARD19.title`；卡面图集条目名同样错位 |
| 「打出「随手创作」→ **这场战斗卡住、必须重开房间**」 | 卡键 `LingSiQuanYONG` → 游戏查 `LING_SI_QUAN_YO_N_G.title`，我们写的是 `LING_SI_QUAN_Y_O_N_G.title` |

日志里对应这几行：

```
LocException: Key=E_VE_RELIC1.title not found in table=relics
  at NCharacterSelectScreen.SelectCharacter(…)          ← 点角色那一下
LocException: Key=E_VE_CARD19.title not found in table=cards
AtlasResourceLoader: Missing sprite 'eve/e_ve_card19' in card_atlas
[ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck until the room is restarted
```

### 真因

工具把 **C# 类名 → 本体 Entry**（本地化键、`cards.json` / `relics.json` 的键、图集条目名、资源文件名都靠它）
算错了 —— 我们自己写成了「**每个大写字母前都插一个下划线**」，
而本体是这一对正则（`StringHelper.CamelCaseRegex`）：

```csharp
([A-Za-z0-9]|\G(?!^))([A-Z])   →   "$1_$2"
```

它是**成对消费**的 —— 连续大写里**每隔一个**才插下划线：

| 类名 | 本体（= 游戏真正查的键） | 旧版工具 |
|---|---|---|
| `EVERelic1` | `E_VE_RELIC1` | `E_V_E_RELIC1` ❌ |
| `ABCRelic1` | `A_BC_RELIC1` | `A_B_C_RELIC1` ❌ |
| `LingSiQuanYONG` | `LING_SI_QUAN_YO_N_G` | `LING_SI_QUAN_Y_O_N_G` ❌ |
| `EVE`（角色自己） | `E_V_E` | `E_V_E` ✅ |
| `AnonCard10` / `VemiuRelic1` / `SevenCrush` / `TachiShrimpCard3` / `WhiteCloakHeaven` / `UIButton` … | — | 与本体**逐字相同** ✅ |

⇒ 只有「类名里出现 **3 个以上连续大写**」的存档会坏（`EVE` / `ABC` / `…YONG`）。
也解释了为什么这个坑藏了这么久：**角色自己**（`EVE` 单独一个词）两边算出来一样（选人页正常），
一旦后面接上 `Relic1` / `Card19` 就分道扬镳。

### 这一版改成

`Naming.Slug()` 逐字复刻本体那对正则（再大写、再滤掉 `[^0-9A-Z_]`），
并把注释里那句「连续大写时每个大写前都插」的**错解释**一并改掉（就是它把人带偏的）。

### 已经验过

* 探针 `_probe\slugcheck`：24 个类名（含 `EVE` / `ABC` / `LingSiQuanYONG` / 带数字 / 带下划线）**工具 == 本体**全一致；
* **只隔离这一处改动**做对拍：把 `Slug()` 临时还原成旧实现再生成一遍样例工程 → 与修复版**逐文件 112/112 逐字节一致**
  （常规驼峰命名的输出一个字节没动）；
* `--uicheck` 新增 6 条（4 个类名的期望 Entry + 常规驼峰红线 + 端到端「`CharClass=EVE` 的存档，`relics.json` 里必须写 `E_VE_RELIC1.title`」）。

> **受影响的存档**：只有类名里带「3 个以上连续大写」的（`EVE`、`ABC`、`…YONG` 这类）。
> 用新版本**重新生成 + 一键构建**即可；急着验证也可以先把**角色英文类名**从 `EVE` 改成 `Eve`（两边算法对 `Eve` 结果一致）。

---

## 二、「找不到 Ainodi」= 模组加载失败，被一个 Harmony 重载歧义连坐

### 日志

```
[ERROR] AinodiMod - 加载失败
[ERROR] Ambiguous match for HarmonyMethod[(class=MegaCrit.Sts2.Core.Models.CardModel,
        methodname=GetDescriptionForPile, type=Normal, args=undefined)]
   at AinodiMod.AinodiModInitializer.Initialize()
```

### 真因

本体（v0.111）给 `CardModel.GetDescriptionForPile` **加了第二个重载**：

```
重载①：2 参 (pileType, target)
重载②：3 参 (pileType, previewType, target)
```

而「被给予自定义关键词」那两个补丁是**带 `[HarmonyPatch(typeof(CardModel), "GetDescriptionForPile")]` 特性**、
统一由 `harmony.PatchAll(...)` 打的 —— 只写方法名 ⇒ Harmony 抛 `Ambiguous match`；
而模组入口**只有一个大 try/catch** ⇒ PatchAll 一抛异常，**后面全部不执行** ⇒
角色 / 卡池 / 遗物**全都没注册** ⇒ 游戏里就是「找不到这个角色（存档里的进度也跟着像没了）」。

### 这一版改成

1. 那两个补丁**不再带 `[HarmonyPatch]`**，改由模组入口**逐个重载手动打、各自一个 try/catch**：
   本体以后**再加/删重载也不会**再连坐整个模组（这个做法工具里另外三处补丁早就在用了）。
2. 顺手把初始化器的失败日志改成**打全量**（解包 `TargetInvocationException` 的 `InnerException` 并 `inner.ToString()`）：
   这次日志里只有「加载失败」四个字，真因得往下翻 50 行才看到 —— 以后第一屏就能看到真因。
   ⚠ 这一条会改动**所有存档**生成的 `cs/ModEntry.cs`（多几行日志），属于有意的输出变更。

### 已经验过

* 探针 `_probe\gkcheck`（新）：造一个用「给自定义关键词」的存档 → 生成 → **8 条断言全 PASS**；
* **生成出来的工程直接 `dotnet build`：0 错误**（手写的 `foreach`/`if` 花括号经真编译器验过）；
* `--uicheck` 两条断言改成新口径（不再带 `[HarmonyPatch]` + 入口逐重载手动打）。

---

## 三、锻造卡：两块布都设「这张牌被抽到时」→ 两个 `AfterCardDrawn` → CS0111

### 你发来的构建日志（最后几行）

```
C:\...\自定义角色存档\WhiteCloakMod\cs\Cards\WhiteCloakTensionMax.cs(99,32):
    error CS0111: 类型“WhiteCloakTensionMax”已定义了一个名为“AfterCardDrawn”的具有相同参数类型的成员
生成失败。
```

（更早那份日志里还夹了一层烟雾：同一张卡的两个 42 MB 的 `ForgeGraph=` base64 注释挤在一起，
肉眼像是「被报错拦住」，其实真因就是这一行 `CS0111`。）

### 真因

「升级前」「升级后」是**两块布**、各自独立生成。两块布都设成【触发时机 = 这张牌被抽到时】时，
两边**各写了一个** `public override async Task AfterCardDrawn(...)` ——
一个类里两个同名同参的 `override` = `CS0111`，整个模组编译不过。

V0.2.169 处理过同一类坑（图的时刻撞上**卡牌自己的**「卡牌时机」→ 改私有方法），
但那一条只在「卡牌自己也有这个钩子」时生效 —— **「两块布互相撞」这一种没覆盖到**。

### 这一版改成

两块布都撞上、而卡自己不需要那个钩子时，只留**一个壳 override**，两块布各自变成私有方法、
运行期按 `base.IsUpgraded` **二选一**：

```csharp
// 两块布（升级前 / 升级后）都用了「这张牌被抽到时」这个时刻：
// 它们各自只能变成私有方法，override 只留这一个 —— 按有没有升级二选一（两个都调 = 两套效果各跑一遍）。
public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw) {
    if (card != this || base.CombatState is null) return;
    if (base.IsUpgraded) await ForgeGraphOnAfterCardDrawnUpgraded(choiceContext);   // 升级后走「升级后」那张布
    else await ForgeGraphOnAfterCardDrawn(choiceContext);                           // 没升级走「升级前」那张布
}

public async Task ForgeGraphOnAfterCardDrawn(PlayerChoiceContext choiceContext) { … }          // 升级前那张布
public async Task ForgeGraphOnAfterCardDrawnUpgraded(PlayerChoiceContext choiceContext) { … }   // 升级后那张布
```

* 守卫照「卡牌时机」那份**逐字同款**（`card != this` + 战斗中；「被保留」那档走
  `player != base.Owner` + `retainedCards.Contains(this)`，和本体 `AfterFlush` 判据一致）；
* 覆盖**五档**：被抽到 / 被消耗 / 被丢弃 / 被保留 / 被自动打出（「打出时」本来就私有方法，不受影响）；
* **只有一块布时一个字符都不变**（那时图自己写一个 `override` 就够了）。
  ⚠ 我第一版把「升级后没单独配 → 退回升级前那块布」也判成「两块都撞上」，凭空多出一个壳 override ——
  自检里那条「不许生成 `AfterFlush(PlayerChoiceContext)` 这种不存在的签名」当场 FAIL，已改对并加了反例断言。

### 已经验过

* 探针：5 张卡的**两块布都设同一个时刻**（上面五档）+ 5 张「卡自己也有那个钩子」的对照卡
  → 真生成 → 每个钩子的 `override` 都是 **0 或 1 个**（修之前「被抽到」那档是 2 个）→ 生成的工程 `dotnet build` **0 错误**；
* `--uicheck` 新增 4 条：① 只生成一个 `AfterCardDrawn`；② 里面按 `base.IsUpgraded` 二选一 + 两个私有方法都在；
  ③ 壳 override 的守卫写法；④ **只有一块布时不多出壳**（反例）。

---

## 四、「被保留」时「消耗自身」不生效：消耗卡牌填 0 被当成「不生效」整条丢了

### 你报的

> 卡牌时机「被保留」时，状态牌应当触发的「消耗自身」没有触发。疑似因为消耗卡牌，数值填充 0
> 不会消耗自身而是跳过。

**你猜得完全对。** 真因就在这一句：

```csharp
// CSharpCodeGen.AmountIsStrength（数值 = 力度的种类名单）里以前有 "ExhaustCard"
or "GenerateCard" or "ExhaustCard" or "TransformCard" or "DiscardCard"
```

名单里的种类 **0 = 不生效**，而 `IsInertZero` 会把整条效果**从生成里丢掉**（卡面也不写）——
所以「消耗卡牌 数值 0」连一行代码都没有生成，自然什么都不会发生。

工具里本来就有「**数值 0 = 这张牌自己**」这一档，但只给了「丢弃卡牌」（`AmountZeroIsSelf` 只认 `DiscardCard`）。

### 这一版改成

`AmountZeroIsSelf` 现在同时认 **`DiscardCard` 和 `ExhaustCard`**：

```csharp
// 卡牌时机「被保留」里勾的「消耗卡牌」填 0 →
await CardCmd.Exhaust(choiceContext, this);      // 消耗这张牌自己
```

* 走本体 `CardCmd.Exhaust`，所以「被消耗时…」那类钩子（`AfterCardExhausted`）照常触发；
* 卡面写「**消耗这张牌自己。**」（照「丢弃这张牌自己。」同款口径，不写张数）；
* 填 ≥ 1 的**一切照旧**（仍然是「从某一摞挑 N 张消耗」），老存档逐字不变；
* 只有**卡牌**上有「这张牌」：遗物 / 药水 / 自定义状态上填 0 保持「不生效」，但校验器现在会**明确警告**
  （以前是静默什么都不做 —— 你这次遇到的就是这种静默）。

### 已经验过

* 探针：① 卡牌时机「被保留」+ 消耗 0 → 生成 `await CardCmd.Exhaust(choiceContext, this);`（`AfterFlush` 里）；
  ② 同档填 2 → 仍然是「随机从手牌消耗 2 张」；③ 遗物上填 0 → 不生成 Exhaust 语句 + 校验器出警告
  → 生成工程 `dotnet build` **0 错误**；
* `--uicheck` **3799 PASS / 0 FAIL**（新增 3 条：0 = 自己的判据、「被保留」那条链的生成文本、卡面文案；
  另把原来那条「消耗卡牌填 0 仍然是不生效」的老断言改成新口径）。

---

## 怎么用这一版

1. 用新版本打开存档 → **一键构建 + 安装** → **完全退出游戏**再进（构建时游戏要关着，否则旧 pck 还在内存里）。
2. 生成后可以 5 秒自查（确认新生成器真的生效了）：
   ```powershell
   $p='<你的生成工程目录>'
   Select-String -Path "$p\cs\GivenKeywords.cs" -Pattern 'HarmonyPatch\(typeof\(CardModel'   # 期望：无输出
   Select-String -Path "$p\cs\ModEntry.cs"      -Pattern 'GetDescriptionForPile'           # 期望：有输出（逐重载手动打）
   ```
3. 类名带连续大写的存档（`EVE` 之类）重新生成后，遗物 / 卡牌 / 状态的**名字、描述、卡面**就都能取到了。
