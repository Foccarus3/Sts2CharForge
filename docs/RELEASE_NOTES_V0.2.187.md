# V0.2.187 更新说明

> 这一版修你发的两份日志里那件**确定是我们这边的问题**的事，外加一条针对「牌抽不上来」的提醒。
>
> 两份日志（`godot2026-10-15T08.23.42.log` / `08.25.44.log`）都是**猎虫人（Bugkiller）**的局。
> 里面唯一一条真报错是：
>
> ```
> ERROR: System.Collections.Generic.KeyNotFoundException:
>        The given key 'CHARACTER.BUGKILLER (28743066)' was not present in the dictionary.
>    at MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary.NCardLibrary.OnSubmenuOpened_Patch1(NCardLibrary this)
>    at MegaCrit.Sts2.Core.Nodes.Screens.NMainMenu.NSubmenuStack.Push(NSubmenu screen)
>    at ...NCompendiumSubmenu.OpenCardLibrary(NButton _)
> ```
>
> —— **点「卡牌图鉴」就崩在那里**（异常抛在 `NSubmenuStack.Push()` 里，图鉴停在打开到一半）。
> 另外 08.25 那份日志里还有一条很关键的：
>
> ```
> [WARN] Unknown ModelId 'CARD.BUGKILLER_CARD18' during serialization, writing NONE
> ```

---

## 一、卡牌图鉴一打开就崩（**每个 mod 角色都会**）—— 已修

**真因（本体源码，不是我猜的）**：`NCardLibrary._Ready()` 里那张
「角色 → 卡池筛选器」的表只 Add 了**5 个官方角色**：

```csharp
// src/Core/Nodes/Screens/CardLibrary/NCardLibrary.cs:290-294
_cardPoolFilters.Add(ModelDb.Character<Ironclad>(), _ironcladFilter);
_cardPoolFilters.Add(ModelDb.Character<Silent>(),   _silentFilter);
_cardPoolFilters.Add(ModelDb.Character<Defect>(),   _defectFilter);
_cardPoolFilters.Add(ModelDb.Character<Necrobinder>(), _necrobinderFilter);
_cardPoolFilters.Add(ModelDb.Character<Regent>(),   _regentFilter);
```

而打开图鉴时它**直接按下标取**（第 470 行）：

```csharp
CharacterModel characterModel = LocalContext.GetMe(_runState)?.Character;
...
key.IsSelected = _cardPoolFilters[characterModel] == key;   // ← mod 角色在这里 KeyNotFoundException
```

所以**只要这一局玩的是模组角色，一进图鉴就抛异常** —— 和我们生成的角色是谁无关，人人都会踩。

**修法**：在打开图鉴之前，把我方角色补进那张表，并且**给它一个真能用的筛选器按钮**
（`cs/ForgeCardLibrary.cs`，每个存档都会生成）：

1. 按钮直接实例化本体那 8 个筛选器用的同一个场景
   `res://scenes/screens/card_library/library_pool_toggle.tscn`；
2. 头像换成本体的 `images/ui/top_panel/character_icon_<你的角色>.png`（模组自己那张，
   没上传角色图标时是本体占位图）；
3. 卡池谓词 = `c.Pool is <你的卡池>` → **图鉴打开时默认列出来的就是你自己角色的卡**
   （以前这张表里根本没有我们的池，图鉴里压根看不到自己角色的卡）；
4. 点它的行为和本体那 8 个一模一样（接上本体私有的 `UpdateCardPoolFilter`：radio 式互斥 + 刷新）；
5. **任何一步出岔子都退化成「挂到本体『杂项』那个筛选器上」** —— 少一个按钮可以，崩不行。

## 二、`Unknown ModelId 'CARD.BUGKILLER_CARD18'` + 「牌抽不上来」—— 加了提醒（这条我们修不了）

日志里 `BUGKILLER_CARD18` 这个名字，在**当前存档里已经不存在了**：
卡片列表是 `…BugkillerCard17`、**「腐烂」(Fulan)**、`BugkillerCard19`…（18 那一格现在叫 `Fulan`）。

也就是说：**有一个模型的英文类名被改过**。而存档（正在进行的那一局里的卡组 / 牌堆 / 遗物栏）
存的是**类名字符串**，改名之后那一局里那张牌就成了「不存在的模型」：
游戏序列化时写 NONE（就是上面那条 `Unknown ModelId … writing NONE`），
**抽到那张牌时拿不到模型 → 卡住**（08.25 日志里还有 `POTION.SEVEN_FLASK`、`RELIC.B_UG_KI_LL_RELIC4`
两条同类警告，都是一样的情况）。

这条**我们改不了**（存档在游戏那边），所以这一版改成**报出来**：
生成时会打一条警告，直接告诉你是哪个模型、从哪个类名变成了哪个、以及两条出路：

```
[警告] 「腐烂」的英文类名从 BugkillerCard18 变成了 Fulan：**正在进行的那一局 / 旧存档**里
引用的还是 BugkillerCard18，那张牌在游戏里会变成「不存在的模型」（日志里是 `Unknown ModelId`，
抽到它可能直接卡住）。想保住旧存档就把它的英文类名改回 BugkillerCard18；
不想要那局了就用新版重新开一局。
```

> 想保住那一局：把那张卡的**英文类名改回原来的**（自定义卡列表里那个「英文类名」框），
> 重新生成 —— 旧存档就能认出它。不想要那一局了：直接开新局即可。

## 已经验过

| 项 | 结果 |
|---|---|
| 图鉴补丁：生成物**真编译** | 带自定义状态 + 覆盖血条的探针存档 → 生成的工程 `dotnet build` **0 错误**；样例工程（ExampleMod）也 **0 错误** |
| `--uicheck` | **3815 PASS / 0 FAIL**（新增 4 条：会写出 `cs/ForgeCardLibrary.cs` / 只碰自己角色 / 按钮+谓词 / 有退路） |
| 改名警告 | 造了一份「上一次的类名表里 `SparkleOldName` = 假面愚者，而它现在叫 `SparkleMaskedFoolPower`」的探针 → 生成时**真的打出那条警告**（文本见上） |
| `--samplegen` | 对齐 V0.2.143 基线：**113 → 114 个文件，5 处差异** —— 新增 `cs/ForgeCardLibrary.cs`（本版）＋ 4 处既有差异（`cs/Patches.cs` V0.2.185、`cs/ModEntry.cs` V0.2.179、`cs/Cards/SevenGuard.cs` V0.2.170、`ExampleMod.sln` 随机 GUID） |
| 上一版（V0.2.186） | 仍是同一份包里的：覆盖血条的诊断日志 + 12px 下限 + 两个入口；没上传图标的自定义状态给中性占位图 |

## 还想要你确认一句

「**变出来的牌抽不上来，会卡死**」这句我还没 100% 对上：

* 如果是**点「卡牌图鉴」卡死** → 就是上面的第一条，这版修好了；
* 如果是**战斗里**（某个状态 / 卡牌效果「生成 / 变化」出来的牌抽不上来、回合走不下去）→
  那是另一条链，麻烦说一句是**哪张卡 / 哪个状态**、卡在哪一步（比如「生成提示框不消失」「回合结束按钮不响应」），
  或者直接截个图 —— 我照那条链去查。
