# V0.2.184 更新说明

> 你报的「**遗物的触发有点问题**」+ 你选的「打出一张牌时 / 结算完毕 / 消耗·丢弃·抽到」这一族、
> 症状「**触发了但次数不对**」+ 你发来的 `BugkillerMod.json`。
> 用你那份存档真生成 + 真编译查出来的，**是两个叠在一起的 bug**。

---

## 一、bug ①（主因）：多时机（全部满足才触发）时，守卫条件**取反取错**了

### 你那只「杀虫剂」配的是

| 触发时机 | 数值 | 范围 |
|---|---|---|
| 战斗开始时 | 1 | — |
| 打出一张牌时（这张牌还没生效） | **2** | 技能牌 |
| 打出一张牌时（这张牌还没生效） | **2** | 攻击牌 |

并且是「**全部满足才触发**」（All 模式，窗口 = 本场战斗）。

### 生成出来的（修之前）

```csharp
// 触发时机：战斗开始时
public override async Task BeforeSideTurnStart(...) {
    // 只在本次战斗自己的第一个回合开始前触发一次
    if (participants.Contains(base.Owner.Creature) || base.Owner.PlayerCombatState.TurnNumber > 1) {   // ← 逻辑反了
        __andHit0 = true;   // 这条成立（第 1/3 条）
```

正常（单时机）那条路的守卫是：

```csharp
if (!participants.Contains(base.Owner.Creature) || base.Owner.PlayerCombatState.TurnNumber > 1) return;
```

——`!` 被吃掉了、`return` 换成 `{` 之后**整个条件反了**：
「不是我的回合 / 不是第 1 回合就跳过」变成了「**是我的回合、或者不是第 1 回合**」。

### 后果

All 模式下这条几乎**永远成立**（只要轮到你，或回合数 > 1）→ 于是「三条时机凑齐」实际上只需要
「技能牌打过 2 张 + 攻击牌打过 2 张」，而且**任何回合**都能凑齐 →
表现就是「触发了但次数 / 时机都不对」。

### 真因

`CSharpCodeGen.NegateRelicGuard` 把 `if (条件) return;` 取反成条件表达式时，对
**「以 `!` 开头、后面还接着别的运算符」**的表达式处理错了：

```csharp
if (s.StartsWith("!(", …) && s.EndsWith(")")) return s[2..^1];
if (s.StartsWith("!", …)) return s[1..].Trim();      // ← 无条件剥掉开头的 !（错）
```

`!(!A || B)` 应当是 `A && !B`，而旧代码直接返回 `A || B`（把 `!` 剥掉、**没有**做 De Morgan）。

### 修法

取反前先按「**开头的 `!` 是不是唯一的顶层运算符**」判断：

```csharp
if (IsFullyWrappedExpr(s)) return NegateConditionExpr(s[1..^1]);      // (A) → A 再取反
if (s.StartsWith("!(") && s.EndsWith(")")) return s[2..^1];          // !(A) → A
if (s.StartsWith('!') && IsSingleAtom(s[1..].Trim())) return s[1..].Trim();   // !a / !a(b) → a
// 其余情况不再乱剥，交给下面的「按运算符归类 + De Morgan」递归
```

修之后生成的是（真生成 + 真 `dotnet build` **0 错误**）：

```csharp
if (participants.Contains(base.Owner.Creature) && base.Owner.PlayerCombatState.TurnNumber <= 1) {
    __andHit0 = true;   // 这条成立（第 1/3 条）
```

> 验证方式是**逐例对拍**（新探针 `NegateConditionExpr` 的 22 个例子）：
> 你这一条 + 之前就对的那 10 条（`player != base.Owner` / `amount <= 0 || card?.Owner… != …` /
> `is null` 那一串…）全部 PASS；另外几条只差在 `!(b)` 与 `!b` 这种等价写法上。

## 二、bug ②（同一个遗物上叠着）：多时机时「施加增益/减益」被**静默丢掉**

修之前那只遗物里还有 **4 处**：

```csharp
// 该触发时机不支持「施加增益/减益（负数 = 扣除层数）」（该触发时机没有 choiceContext，施加增益/减益无法实现），已忽略。
```

而同一个方法体上面明明**已经**造了一个 choiceContext：

```csharp
// 这个钩子本体没给 choiceContext（战斗里那几条）：按本体 Hook.cs 的做法造一个 HookPlayerChoiceContext
var choiceContext = new HookPlayerChoiceContext(this, base.Owner, __netId, GameActionType.Combat);
```

真因：多时机那条路把「有没有 context」判成 `plan.HasContext`（= 本体这个钩子**签名里**带不带），
而**单时机**那条路用的是 `HasContext(id) || needCtx`（= 生成端自己造了也算）。
「打出一张牌时」正好是「本体不给、但生成端自己造」的那一档 → 多时机时整条效果被丢掉。

**修法**：多时机那段改成传 `plan.HasContext || plan.NeedsCtx`（和单时机同口径）。
现在那只遗物里 **0 处「已忽略」**，`PowerCmd.Apply<…>` 真的会执行。

## 三、验证

* 用**你那份 `BugkillerMod.json`**真生成（只把输出目录改到我的临时区，你的存档一个字节没动）
  → 8 只遗物 → 生成工程 `dotnet build` **0 错误**；
* 那个遗物：守卫正确 + 「已忽略」0 处 + 两个 `PowerCmd.Apply` 都在；
* `--uicheck` **3804 PASS / 0 FAIL**；
* `--samplegen` 对 V0.2.143 基线：113 个文件里仍是那 3 个已知差异
  （`.sln` 随机 GUID / `ModEntry.cs`（V0.2.179 有意改的）/ `SevenGuard.cs`）—— **没有扩大**：
  没用「多时机（All/Any）」的存档生成结果一个字都不变。

## 四、你要做的

更新到 V0.2.184 → **重新生成 + 一键构建·安装**（改的是生成出来的 C#）。

> 还想请你顺带确认一句：那只「杀虫剂」你本来想要的是什么？按现在这份配置（All 模式 + 三条时机），
> 它的语义是「**战斗开始时** + **本场战斗里技能牌累计打出 2 张** + **攻击牌累计打出 2 张** 全都满足过
> 之后，跑一次那两个效果」—— 凑齐之后不重置（窗口 = 本场战斗），所以**一场战斗只会触发一次**。
> 如果你的本意是「每打出 2 张技能 / 攻击牌各给一次」，那模式应当从「全部满足」改成「任意一条满足」，
> 并且两条时机的数值各自留在 2。
