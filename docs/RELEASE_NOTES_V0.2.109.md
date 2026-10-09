# V0.2.109 更新说明

> 这批需求里的第 ② 条：触发时机增加「**某个状态被移除时**」（遗物 + 状态两边都有）。

## 一、新的触发时机：「某个状态被移除时」

- 位置：**遗物页** / **自定义状态页**的「触发时机」下拉（两边同一套，同一个 id、同一个显示名）；
- 下面同样有「**盯哪个状态**」那一栏：**留空 = 自己身上任意状态被移除都触发**，填了只认那一个；
- 「**层数掉到 0 自然消失**」和「**被『清除状态』直接清掉**」**都算**被移除。

### 为什么这条必须自己打补丁

本体**没有**任何「状态被移除」的全局钩子：

| 看起来能用的东西 | 为什么不行 |
|---|---|
| `PowerModel.AfterRemoved(Creature oldOwner)` | 是**那个状态自己**的实例钩子 —— 遗物 / 别的状态都收不到 |
| `AfterPowerAmountChanged`（现成的「层数变化」钩子） | 只覆盖「层数被扣到 0」那一半；被 `PowerCmd.Remove` **直接清除**的根本不走那条路（`PowerCmd.Remove` = `RemoveInternal()` + `AfterRemoved()`，中间没有钩子广播） |

所以用到这条时机时，工具会多写一个 `cs/PowerRemoval.cs`：后缀补丁打在**同步**的
`PowerModel.RemoveInternal()` 上 —— 它是本体**所有**移除的唯一出口（`PowerCmd.Remove`、
「清除全部减益」、战斗结束清状态、死亡清状态全都走它），然后把
「**哪个状态从谁身上被移除了**」转发给：

1. 该生物所属**玩家身上的遗物**里配了这条时机的那些；
2. 该生物身上**还留着**的自定义状态里配了这条时机的那些；
3. **被移除的那个状态自己**（它已经从 `Powers` 里摘掉了，所以单独判一次 ——「我这个状态被移除时」也能用）。

生成的方法是个**普通 async 方法**（不是 `override`）：

```csharp
// 遗物侧（base.Owner 是 Player）；状态侧一样，只是 base.Owner 是 Creature
public async Task OnForgePowerRemovedAsync(PowerModel power) {
    if (power.Owner is null || power.Owner != base.Owner.Creature) return;   // 只看自己身上的状态
    if (power is not VulnerablePower) return;                                 // 只盯「易伤」（留空 = 任意状态）
    // …这条时机的效果（需要 choiceContext 的效果会自动造一个 HookPlayerChoiceContext）
}
```

- 补丁是**同步**的、方法是 `async` 的 → 转发用本体自带的 `TaskHelper.RunSafely(...)`
  （fire-and-forget，本体自己也是这么处理这类情况的）；
- 「盯哪个状态」写一行 `// CET:PowerWatch=<状态类名>` 标记，回读按它还原（守卫以后改写法也不丢配置）；
- **没用到这条时机的存档不会生成 `cs/PowerRemoval.cs`**（默认存档输出逐字不变，红线照旧）。

## 二、验证

- `--uicheck`：开发版与发布版都是 **2579 PASS / 0 FAIL**（新增 19 条：两边的菜单条目 / 「盯哪个状态」/
  遗物与状态两侧的生成（普通方法 + 守卫 + 标记 + choiceContext）/ 补丁内容（打在哪个方法、转发给谁、
  TaskHelper）/ 红线「没用到不生成」/ 生成 → 回读（触发时机 + 盯哪个状态 + 效果 + 0 没认出来））；
  - 顺带被这条时机**逮到一个真 bug**：`CustomPowerGen` 的「这条钩子有没有 choiceContext」白名单没算上
    新时机 → 生成「造成伤害」这类效果时会 CS0103（`choiceContext` 用了但没声明）。
    已有用例当场 FAIL（`★用了但没声明★`），已修（加进白名单），探针真编译复查通过；
- 探针新增 `RunPowerRemoved`（遗物盯「易伤」+ 状态盯「虚弱」，状态那边还带一条需要 `choiceContext` 的造成伤害）→
  生成 → 真 `dotnet build` **0 错误** → 回读 `trigger=PowerRemoved / watch=VulnerablePower / 效果 1 条`、
  `状态 triggers=PowerRemoved watch=WeakPower`；整轮探针 **25 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.109.zip`。

## 三、这批剩余的（按顺序继续）

| # | 需求 | 状态 |
|---|---|---|
| ① | 锻造页【能量】费用没反映到卡面 / 游戏 | ✅V0.2.108 |
| ② | 触发时机增加「移除某状态时」 | ✅ 本版 |
| ③ | 额外资源量的隐藏遗物在遗物页列出来 + 「在游戏中隐藏」勾选 | 待做（做法已确认） |
| ④ | 命中次数公式不生效（如 `2+A`）、卡面也不显示次数 | 待做 |
| ⑤ | 公式在卡面上对数值的描述有问题 | 待做 |
| ⑥ | 生成器效果去掉「数值」框、只留公式（四处编辑器；公式留空 = 0） | 待做（做法已确认） |
| ⑦ | 无色牌卡面丢失 | 待做 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | 待做 |
| ⑨ | 新效果「放置卡牌」（数值 0 = 自己；否则从五处挑一张放到五处） | 待做（做法已确认） |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 待做（做法已确认） |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
