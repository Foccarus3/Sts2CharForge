# V0.2.179 更新说明

> 你报的「**额外资源量 · 跨战斗保存，怎么会跨局保存，导致多人会不同步**」—— 是同一个根因，两个症状。
> 另外这一版还带你上一条要求的「触发时机加一个回合开始后」（两条一起发，见文末）。

---

## 一、先答「为什么会跨局」

它本来是**不该**跨局的 —— 但上一版把余量放在了一个 **`static` 字段**里：

```csharp
// 旧版生成出来的（有问题）
private static int _carried;                      // ← static：属于**整个进程**
public override Task AfterCombatEnd(CombatRoom room) {
    _carried = CarryOver ? (base.Owner.PlayerCombatState?.Stars ?? 0) : 0;
}
```

`static` 跟「哪一局 / 哪个玩家」没有任何关系，它只跟**这个程序还活着**有关系。所以：

* **跨局残留**：退出到主菜单，`_carried` 还在；再开新的一局（哪怕又选了同一个角色），
  第一场战斗开场拿到的是**上一局**剩下的数 —— 你看到的「怎么会跨局保存」就是它。
* **多人不同步**：一台机器上（本地多人）两个玩家**共用同一个 `_carried`**；
  联机时更要命 —— 房主和客机**各自**跑一遍那个钩子，而 `_carried` 是**自己这台机器上**
  上一次战斗结束时的余量（客机没跑过那场战斗 → 是 0）→ 两个人算出来的开场数量不一样。

## 二、改成了本体自己的做法

本体**唯一一份**「模组状态怎么进存档、怎么联机同步」的扩展点就在这儿（例：本体遗物
`GalacticDust` 的 `StarsSpent`）：

```csharp
// 现在生成出来的
/// 每玩家一份 + 进存档 + 随 SerializableRelic 走联机同步 + 新的一局归零
[MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
public int CarriedAmount { get; set; }

public override Task AfterCombatEnd(CombatRoom room) {
    CarriedAmount = CarryOver ? (base.Owner.PlayerCombatState?.Stars ?? 0) : 0;
    return Task.CompletedTask;
}
```

为什么这样就对了（都对着本体源码核过）：

| 你的疑问 | 为什么解决 |
|---|---|
| 怎么会跨局？ | 它是**这只遗物的一个属性**，不是进程级的。新的一局会重新造遗物 / 读存档 —— 新局的存档里没有这个值 → **天然是 0**。 |
| 联机为什么不同步？ | `SerializableRelic.Props`（= 这个属性）**本来就是本体联机包的一部分**；`ModelIdSerializationCache.Init` 会把所有模型（含模组里的遗物，只要它在 `ModelDb.AllRelics` 里）上带 `[SavedProperty]` 的属性登记进同步表。房主存什么、客机收到什么，一模一样。 |
| 每个玩家各算各的？ | 起始遗物是**每个玩家各一份**的模型实例（`base.Owner` 就是它属于谁），不是全局单例。 |
| 为什么不直接存到资源计数器上？ | 计数器是 `PlayerCombatState.Stars`，而 `PlayerCombatState` 是**每场战斗新造**的对象（本体连存档都不存它）—— 战斗一结束它就没了，正是「跨战斗」要解决的那件事。 |

> 一句话：**跨战斗 = 存「跨战斗的东西」**（遗物上的存档属性），**不能存「每场战斗的东西」**
>（`PlayerCombatState`），**更不能存「整个程序的东西」**（`static`）。

## 三、界面文案也改了

「额外资源量」页那个勾选框现在写明了范围（以前只写「跨战斗继承」，容易被理解成跨局）：

> 跨战斗继承（下一场开场数量 = 初始数量 + 上一场结束时剩下的）
> 悬停：只在**当前这一局**里继承：下一局重新开始时归零。存的是这只载体遗物的一个
> `[SavedProperty]` 属性（本体自己的存档 + 联机同步机制），所以多人联机时每个玩家各算各的、不会互相串。

## 四、验证

* 真生成一份开了「额外资源量 + 跨战斗继承」的存档 → `dotnet build` **0 错误**
  （新生成的 `[MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]` 属性编译通过，本体那个命名空间不用加 using）；
* `--uicheck` → **3786 PASS / 0 FAIL**，新增 2 条断言把这件事钉死：
  「余量是 `[SavedProperty]` 实例属性（**不是** static 字段）」＋「这份生成代码里**一个 static 成员都没有**」
  —— 以后再往这里写 `static` 会直接 FAIL；
* `--samplegen` 与 V0.2.143 基线对拍：113 个文件里仍然只有 2 个不同
  （`ExampleMod.sln` 的随机 GUID、`SevenGuard.cs` 的 `GainsBlock`），**没有扩大**：
  没开额外资源量（或没开继承）的老存档生成结果逐字节不变。

## 五、顺带：上一条要求的「回合开始后」（也在这个版本里）

「触发时机加一个回合开始后」加好了，而且是本体真实存在的**三个**时刻（`Hook.AfterPlayerTurnStart`
一个函数里连着派发 Early → 主 → Late，而它是「回能量 → 抽牌」之后的最后一步）：

| 下拉里显示 | 本体钩子 |
|---|---|
| 回合开始后（抽牌前） | `AfterPlayerTurnStartEarly` |
| **回合开始后（抽牌后）** | `AfterPlayerTurnStart`（原来那一档「玩家回合开始时」就是它，旧存档零改动） |
| 回合开始后（最晚） | `AfterPlayerTurnStartLate` |

遗物页 / 锻造节点图 / 自定义状态页三个页面都能用，id 与显示名两侧逐字对齐；
顺手修掉了新时刻没登记进 `HasContext` 导致「抽牌效果没有 choiceContext、只能传 null!」的问题。

## 六、你要做的

**重新生成 + 一键构建·安装**（改的是生成出来的 C#）。
