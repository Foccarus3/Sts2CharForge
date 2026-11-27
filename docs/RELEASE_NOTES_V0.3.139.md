# V0.3.139 — 「斩杀死才回到手牌」不再被拦住（跨钩子判斩杀）

## 一、问题（用户报）

用户把「斩杀」条件挂在「将此牌放回手牌」上（卡牌「剑舞」），生成被校验器拦住：

```text
卡牌「剑舞」这条「将此牌放回手牌」的效果（实现在 AfterCardPlayedLate 钩子里）用了「斩杀」条件 ——
那个条件判的是上一条攻击的结果（生成出来引用 __lastAttack / __fatalOk），
而那两个变量只在「打出时」那一段里有声明，这个钩子里既没有声明、也不会有上一条攻击
⇒ 生成出来是 CS0103（整个模组编译不过）。
```

这条拦截本身是对的（V0.2.169 加的）：`__lastAttack` / `__fatalOk` 是 **OnPlay 里的局部变量**，
而「将此牌放回手牌」跑在 `AfterCardPlayedLate` —— 那个方法里既看不到它们、也没有「上一条攻击」。
但「**这一下把敌人打死了，就把这张牌收回手牌**」是很正常的卡牌设计，以前在这个工具里做不出来。

## 二、改法：把斩杀结果跨钩子带过去

生成端（`CSharpCodeGen`）：

```csharp
public sealed class Xxx : CardModel
{
    /// <summary>这次打出有没有「斩杀」：给「将此牌放回手牌」的斩杀条件用（那个钩子读不到 OnPlay 的局部变量）。</summary>
    private bool __fgFatalThisPlay;          // ← 只有真的用到才声明（别的牌一个字符都不多出来）

    protected override async Task OnPlay(...)
    {
        __fgFatalThisPlay = false;            // ← 每次打出从头算
        ...
        __lastAttack = await DamageCmd.Attack(...) ...;   // 原来就有
        ...
        __fgFatalThisPlay = __lastAttack is not null && __fatalOk
            && __lastAttack.Results.SelectMany(hits => hits).Any(r => r.WasTargetKilled);   // ← OnPlay 末尾写入
    }

    public override async Task AfterCardPlayedLate(...)
    {
        if (__fgFatalThisPlay) { ... await CardPileCmd.Add(this, PileType.Hand); }
    }
}
```

- 判据和本体的 Fatal 完全一样（`Results` 里 `WasTargetKilled`，`__fatalOk` 还是「打之前算的、目标允许触发斩杀」）；
- 多段伤害时取**最后一条攻击** —— 和「上一条攻击」的原语义一致；
- 字段在 `OnPlay` 开头清零，所以不会把上一回合 / 上一次打出的结果带过来；
- 回调钩子里带 `cardPlay.Card == this` 的旧守卫仍然在，两张同名牌不会串。

## 三、校验器

- 「将此牌放回手牌」+ 斩杀：**不再报错**（现在真的能生成）；
- 「抽到这张牌时它本场战斗的费用 -N」+ 斩杀：**照旧报错** —— 那个钩子是 `AfterCardDrawn`，
  抽牌那一刻根本没有「这一次打出的攻击」，语义上不成立；
- 「斩杀前面没有造成伤害」那条：对回手效果放宽成「**牌里**（OnPlay 里）有伤害即可」（伤害排在它前面
  或后面都行，因为结果是在 OnPlay 末尾统一记的）；
- 但如果那条伤害带着「卡牌时机」（跑在别的钩子里，根本不会写 `__lastAttack`），仍然报错 ——
  不给「静默永远不触发」留口子。

## 四、验证

`--uicheck` 新增 / 改写 5 条断言：

| 断言 | 结果 |
|---|---|
| 卡上声明 `private bool __fgFatalThisPlay;` | 通过 |
| OnPlay 开头清零 + 末尾写入（判据 = Results.WasTargetKilled） | 通过 |
| 回手钩子里读字段、不再引用 `__lastAttack`（不会再 CS0103） | 通过 |
| 这种配置不再被校验器拦住 | 通过 |
| （对照）「抽到这张牌时费用 -N」+ 斩杀仍然报错 | 通过 |
| （改写）V0.2.169 那条「回手 + 斩杀 → 报错」改成「不再报错」 | 通过 |

```text
V0.3.139 开发版 --uicheck：5008 PASS / 0 FAIL
```

另外用探针复刻了用户那张牌（造成伤害 + 斩杀死才回手），生成出来的工程 `dotnet build`：**0 错误**。
生成代码里三个位置都对：

```text
字段声明        private bool __fgFatalThisPlay;
OnPlay 开头     __fgFatalThisPlay = false;
OnPlay 末尾     __fgFatalThisPlay = __lastAttack is not null && __fatalOk && ...Any(r => r.WasTargetKilled);
回手钩子        if (__fgFatalThisPlay) { ... }
```

## 五、老存档

没有「回手 + 斩杀」这种组合的存档：**生成文本一个字符都不变**（字段 / 清零 / 写入三行都不会生成）。
有这种组合的老存档：以前根本生成不出来（被拦住），现在能生成 —— 行为就是「这次打出的最后一条攻击把目标打死，就把这张牌收回手牌」。

## 版本

```text
V0.3.139
```
