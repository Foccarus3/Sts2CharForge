# V0.3.5 — 修「两张布都用同一个**插件**触发时机 → 静默失效」

按你转来的缺陷报告（`Sts2CharForge_V0.2.401_锻造触发时机_两张布静默失效_缺陷报告.md`）修的，报告里的定位是对的。

## 根因（和报告一致）

1. 两张布命中同一个钩子时，生成端会把两块写成**私有方法**，再补一个「按 `base.IsUpgraded` 二选一」的 `override` 去分派（避免 CS0111）；
2. 但那个分派 `override` 的**签名表是写死的 5 个本体钩子**（`AfterCardExhausted / AfterCardDiscarded / AfterCardDrawn / AfterCardPlayed / AfterFlush`）；
3. 插件的触发时机（`hook = OnTurnEndInHand` 之类）不在表里 → 签名取到 `null` → **分派 `override` 不生成** → 两块布成了**没人调用**的私有方法。生成 0 错误、编译 0 错误、游戏里永不触发。

## 改法（比报告的建议更通用一点）

* 签名表之外，再加一层回退：**插件触发时机自带 `signature` 的，就用它自己的签名当壳**
  （`ForgeTimings.All` 里挑同 `hook` 且 `signature` 非空的那条）；
* 插件写了 `guard` 的，照它写守卫；没写就只判 `if (base.CombatState is null) return;`；
* `OnTurnEndInHand` 这种还会看布尔属性的钩子：**在同层补一次** `public override bool HasTurnEndInHandEffect => true;`
  （放进方法体里是语法错误；两块各写一次是重复成员）。

这样不只是 `OnTurnEndInHand`——**任何带 `signature` 的插件时机**在两张布上都能用了。

## 兜底（报告的建议 B 也做了）

`ProfileValidator` 新增一条**错误**级检查：两张布用了同一个钩子，而它既不是内置那 5 个、插件又**没写 `signature`** 时，直接报：

> 卡牌「X」的两张布都用了同一个触发时机「…」，而这个时机既不是内置的那 5 个卡牌钩子、插件也没给它写 `signature` —— 生成端无法为它写分派用的 override，两张布会变成**没人调用**的私有方法……（解决办法：只在一张布用它，或让插件补 `signature`）

→ 以后任何同类遗漏都会在生成前被看见，而不是「装上去没反应」。

## 自检

**4799 PASS / 0 FAIL**（+2 条）：

* 用随程序发的示例插件里那个带 `signature` 的插件时机，**两张布都用**它 → 生成的卡里
  **恰好一个** `public override async Task AfterPlayerTurnStartEarly(…)`，且含 `if (base.IsUpgraded)` 分派；
* 普通卡（没有插件时机）不会被这条新检查误报。

## 你需要做的

更新到 **V0.3.5** → 重新生成 → 构建安装。再用「两张布都选『回合结束前（这张牌还在手牌里）』」的存档生成一次：

* 卡牌类里应出现**一个** `override`（不是两个孤儿方法）；
* `HasTurnEndInHandEffect => true` 只出现一次；
* 进游戏结束回合、牌还在手牌时，效果应该执行。

如果还不行，把生成的 `Capitalmonster.cs` 里那段（搜 `OnTurnEndInHand`）发我 —— 我看看壳长什么样。
