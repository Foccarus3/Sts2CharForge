# V0.2.185 更新说明

> 你发的那个构建失败弹窗里的**两个错误**（`CS0104 Void` / `CS8121 Defect`）是**同一个病因**：
> 生成出来的代码引用**本体类型**时写的是**短类名**，一旦作用域里有同名类型就被抢走。
> 两个都修了，并且用一个探针把「修之前的报错」和「修之后编译通过」都验过。

---

## 一、`cs\Cards\TheShadowCard12.cs(36,53): error CS0104："Void" 是不明确的引用`

```
error CS0104: "Void" 是 "MegaCrit.Sts2.Core.Models.Cards.Void" 和 "void" 之间的不明确的引用
```

本体里**真有一张卡叫 `Void`**（`src\Core\Models\Cards\Void.cs`），而我们那张牌的效果指定了这张本体卡，
生成出来的是**短名**：

```csharp
// 旧版生成出来的（有问题）
await CardPileCmd.AddToCombatAndPreview<Void>(base.Owner.Creature, PileType.Hand, 1, base.Owner);
//                                       ^^^^ 和 System.Void 撞名
```

编译器的 `using System;` 里有 `Void`，本体的卡也叫 `Void` —— 它不知道该用哪个。

**这一版改成写全名**（`MegaCrit.Sts2.Core.Models.Cards.Void`），并统一了一个新的
`CardTypeExpr()`：**我们自己的卡**照旧写短名（同一个命名空间，加了前缀反而编译不过），
**本体卡一律写全名**。所有「目标卡」的地方都走它：

* `ModelDb.Card<…>()`：初始卡组里的本体卡、生成到牌组、变化卡牌、先古卡池、生成卡牌效果；
* `CardPileCmd.AddToCombatAndPreview<…>()`（就是上面那一处）；
* 锻造图「变化成指定卡」两处。

## 二、`cs\Patches.cs(121,54): error CS8121："Defect" 类型的模式无法处理 "CharacterModel" 类型的表达式`

那行是生成端写的「**这是不是本体那 6 个官方角色**」判断：

```csharp
// 旧版生成出来的（有问题）
character is Ironclad or Silent or Regent or Defect or Necrobinder or Deprived;
```

**你自己的 mod 里只要有一个类叫 `Defect`**（卡 / 状态 / 遗物都算），编译器就会把这里的 `Defect`
当成**你的那个类**（就近优先），于是这个模式跟 `CharacterModel` 对不上 → CS8121。

**这一版改成 6 个全名**（`MegaCrit.Sts2.Core.Models.Characters.Defect` 等），不管本 mod 里有什么同名类都不会再撞；
同一个文件里另外两处 `ModelDb.Character<Ironclad>()` 也一并改成全名。

## 三、顺带：产物回读跟着放宽

生成端开始写全名之后，**从工程回读**（「从工程恢复」那条链）也得跟着放宽，否则它会把
`MegaCrit.Sts2.Core.Models.Cards.Void` 整个当成类名写回存档：

* `AddToCombatAndPreview<(\w+)>` / `ModelDb\.Card<(\w+)>` → `([\w\.]+)`；
* 新增 `ShortCardName()`：回读时去掉命名空间前缀 —— 存档里的「目标卡」一直是**短名**，
  界面下拉、校验器、老存档全都按短名走，行为不变。

## 已经验过

| 项 | 结果 |
|---|---|
| 新探针 `_probe\nqcheck` | 造一份「目标卡 = 本体 `Void`」＋「本 mod 自己有个类叫 `Defect`」的存档：**修前**生成出来的工程 `dotnet build` 报的正是 `CS0104 Void`；**修后** 6/6 断言 PASS，且**生成出来的工程 `dotnet build` 0 错误** |
| `dotnet build Sts2CharForge.sln` | 0 错误 |
| `--uicheck` | **3807 PASS / 0 FAIL**（新增 3 条：本体卡全名 / 自己的卡仍短名 / 角色判断全名） |
| 输出影响面 | 样例工程逐文件对拍 **110 个一致 / 2 个不同**：`cs\Patches.cs`（本版）＋ `cs\ModEntry.cs`（上一版的异常日志）。**卡牌文件一个都没变** |

## 这一版之前如果急着过编译（可绕开，非必须）

1. 那张牌引用的目标卡从本体「**虚空 `Void`**」换成别的（或删掉这条效果）；
2. 把你 mod 里那个叫 `Defect` 的类**改个名**（比如 `ShadowDefect`）。

用新版重新生成 + 一键构建之后，两条都不用做了。
