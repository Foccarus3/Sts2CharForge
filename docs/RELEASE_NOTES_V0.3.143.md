# V0.3.143 — 状态描述里的「无主参数」不再把悬停面板 / 事件卡死

> 这一版在 V0.3.142（召唤伙伴 +「次数 = 全部」漏声明 `PetHp`）之上，包含那条修复。

## 一、问题（用户报）

> 如果状态的描述里引用了自定义参数，而且自定义参数是「某某身上的某状态层数」那种的话，
> 引用了这个状态的遗物会出 bug —— 有关这个遗物的事件卡死，遗物点开后卡死。
> 原因好像是这个自定义参数是无主参数，然后没有成功的自动返回 0。

## 二、真因

状态描述里的 `{=公式}` / `{参数}`（V0.3.88 起）会在**每次取描述时**求值。生成出来是这样：

```csharp
private LocString __ForgePowerDescription()
{
    var __loc = new LocString("powers", "XXX.description");
    __loc.Add("Amount", Amount);
    __loc.Add("__fgPowerDesc0", ForgeFormulaRuntime.PowerAmount(base.Owner.CombatState?.HittableEnemies.FirstOrDefault(), "VulnerablePower"));
    return __loc;                    // ← base.Owner 为 null 时这里抛 NullReferenceException
}
```

「某某身上的某状态层数」这个参数会被内联成 **`base.Owner.CombatState?…`** —— `?.` 只保护了 `CombatState`，
**没有保护 `base.Owner` 本身**。而：

- 遗物悬停 / 状态图鉴 / 事件里展示状态时，用的是**规范实例**（`ModelDb.Power<X>()`），它的 `Owner` 是 **null**；
- 悬停面板每次取描述都会抛一次 NRE → 反复重试 / 打断 Godot 的界面流程 → 表现就是**遗物点开卡死、事件卡死**。

（用户说的「无主参数没有自动返回 0」就是这个：不是参数的值算成 0，而是求值前就 NRE 了。）

顺带发现的第二个问题：**只用了「描述公式」的存档生成出来会缺 `cs/ForgeFormulaRuntime.cs`**
（那个文件以前只在「节点图 / 数值公式 / 触发公式」用到时才写），生成的工程直接 `CS0103` 编译不过。

## 三、改法

**① 描述公式先判「有归属」再算，并且兜底返回 0**（和卡牌描述里 `{=…}` 早就有的
`if (card.Owner is not null)` 同一个规矩）：

```csharp
private LocString __ForgePowerDescription()
{
    var __loc = new LocString("powers", "XXX.description");
    __loc.Add("Amount", Amount);
    // 描述里的公式只在**有归属**时算：无主实例（状态图鉴 / 遗物悬停）一律显示 0，绝不抛异常
    decimal __fgDescVal0 = 0m;
    if (base.Owner is not null) {
        try {
            __fgDescVal0 = (decimal)(ForgeFormulaRuntime.PowerAmount(base.Owner.CombatState?.HittableEnemies.FirstOrDefault(), "VulnerablePower"));
        }
        catch { __fgDescVal0 = 0m; }   // 算不出来就显示 0，不能因为悬停面板把游戏卡死
    }
    __loc.Add("__fgPowerDesc0", __fgDescVal0);
    return __loc;
}
```

- **没有描述公式**的状态：生成的描述方法**一个字都不变**（还是 `__loc.Add(...)` 那三行）；
- 有描述公式的状态：多一层「有归属 + try/catch」的保护。

**② 补上 `ForgeFormulaRuntime.cs` 的生成判据**（`ForgeGraphGen.AnyNonCardFormula`）：
除了原来那几种「数值公式」，现在还算：

- 触发时机的**数值公式**（例：到达 N 层填 `X` / 某个参数 → 内联出 `ForgeFormulaRuntime.Energy/PowerAmount…`）；
- 状态**描述**里的 `{=公式}`（同上）。

纯常数公式（`2+1`）仍然**不写**那个文件（不用不写）。

## 四、验证

`--uicheck` 新增 7 条断言：

| 断言 | 结果 |
|---|---|
| 描述公式先判 `if (base.Owner is not null)` 再求值 | 通过 |
| 外面还有 `catch { __fgDescVal0 = 0m; }` 兜底 | 通过 |
| 只用了「描述公式」的存档 → 要生成 `ForgeFormulaRuntime.cs` | 通过 |
| （对照）没有描述公式 → 描述方法还是老文本（没有 `__fgDescVal` / `try`） | 通过 |
| 只用了「触发公式（X）」的存档 → 也要生成那个文件 | 通过 |
| （对照）纯常数公式 `2+1` → 不写那个文件 | 通过 |

```text
V0.3.143 开发版 --uicheck：5026 PASS / 0 FAIL
```

另外真生成了一份「状态描述引用『指定敌人身上的易伤层数』参数 + 引用这个状态的遗物」的存档：

- 修之前：生成的描述里是 `base.Owner.CombatState?…`（无主实例 NRE），
  而且工程 `CS0103`（缺 `ForgeFormulaRuntime`）；
- 修之后：描述方法带上归属判断 + try/catch，工程 `dotnet build` **0 错误**，`ForgeFormulaRuntime.cs` 在。

## 五、影响面

- 只有「自定义状态的描述里写了 `{=公式}` / `{参数}`」的存档，生成的描述代码会变（多那几行保护）；
  其它状态的生成文本一个字符都不变。
- 以前这种配置在游戏里会卡死 / 编译不过，现在正常显示算出来的值；**无主实例（图鉴 / 悬停）显示 0**。

## 版本

```text
V0.3.143
```
