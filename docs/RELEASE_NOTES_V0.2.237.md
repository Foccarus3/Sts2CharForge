# V0.2.237 — 临时增益的还原不再造出「-1 层」的状态

## 用户报的

「自带减层的效果加上临时之后只要层数是 1，回合结束就会扣成 -1。」
（附了 `godot…22.19.06.log`；那份日志里没有临时状态的调试输出，问题描述本身已经足够定位。）

## 病因（本体两个细节撞在一起）

1. **临时状态的本体写法是「盲还原」**。本工具生成的临时状态类照抄了本体的
   `TemporaryStrengthPower`（`src\Core\Models\Powers\TemporaryStrengthPower.cs:147-155`）：

   ```csharp
   await PowerCmd.Remove(this);
   await PowerCmd.Apply<StrengthPower>(choiceContext, base.Owner, -Sign * base.Amount, base.Owner, null);
   ```

   本体这么写是安全的，因为本体那三个临时状态只会配 **力量 / 敏捷 / 专注** —— 它们自己**不扣层**、
   层数也不为负。而本工具允许把**任何**状态做成临时（虚弱 / 易伤 / 脆弱，或自定义状态）。

2. **虚弱 / 易伤 / 脆弱自己会扣层**：`WeakPower.cs:49`、`VulnerablePower.cs:60`、`FrailPower.cs:35`
   都在自己的 `AfterSideTurnEnd` 里 `PowerCmd.TickDownDuration(this)`。

于是「临时 +1 层虚弱」在回合结束时按这个顺序被扣了两次：

| 步骤 | 虚弱层数 |
|---|---|
| 施加临时 +1（本来 0 层） | 1 |
| 虚弱自己的回合结束 tick（`TickDownDuration`，1 → 0 → 本体移除该状态） | 0（状态没了） |
| 我们那句盲还原 `Apply<WeakPower>(-1)` | **-1** |

`PowerModel.ApplyInternal` 是直接 `SetAmount((int)amount)`，**不夹 0**（`PowerModel.cs:564-571`），
所以目标身上会凭空出现一个「虚弱 -1」——就是用户看到的那一格。

## 修法

临时状态「还原」那几句从盲写改成带两条守卫（`CSharpCodeGen.EmitTempPowerRevert`）：

```csharp
PowerModel? __back = base.Owner.GetPower<WeakPower>();
if (__back is not null) {
    decimal __amount = -base.Amount;
    decimal __now = __back.Amount;
    if (!__back.AllowNegative && __now + __amount < 0m) __amount = -__now;   // 不跨过 0
    if (__amount != 0m) await PowerCmd.Apply<WeakPower>(choiceContext, base.Owner, __amount, base.Owner, null);
}
```

- **状态已经被它自己的回合结束扣光、移除了 → 什么都不做**（这次临时变化已经被吃掉了）；
- 还在 → 还原量夹到**不跨过 0**；
- `AllowNegative` 的状态（力量 / 敏捷 / 专注）**不夹** —— 负层数是它们的正常形态（本体「尖啸」就靠这个扣出负力量）；
- 「回合结束撤」和「下回合开始撤」两档走的是同一份守卫（`AfterSideTurnEnd` / `AfterPlayerTurnStart`）。

判定用的是**运行时**的 `__back.AllowNegative`，所以本体状态和本工具生成的自定义状态都适用，
不需要在生成期猜哪一类允许负层数。

## 说明：同回合两处扣层是正常的

「临时 +1 层」遇到「自己每回合结束 -1 层」的状态时，这一层本来就会在这一回合结束时到期，
所以**合计扣到 0**（而不是留着 1 层）是对的 —— 这一版只把「扣成 -1」这个越界挡住。

## 验收

- 自检：开发版 **4225 PASS / 0 FAIL**（新增 4 条：先取实例 / 夹到 0 / 不再盲还原 / 两档都带守卫）。
- 对拍（老存档生成结果）：Sample 113 个文件、WhiteCloak 412 个文件 —— **逐字节相同**，
  差异只有 `.sln` 的 GUID。这两份存档都没有「临时增益」，所以这一版对它们等于零改动。
- 含临时增益的存档：只有 `TempPowers.cs` 里那几个临时状态的还原段变了（多出上面那 4 行守卫）。
