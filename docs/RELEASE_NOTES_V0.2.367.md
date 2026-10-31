# V0.2.367 — 插件触发时机现在**能进锻造图的【触发时机】下拉**了

你转的那段解释是对的：以前 `ForgeTimings.For(scope)` 是**静态表**（卡牌只有 6 档），
插件的 `triggers` 只接进了「状态 / 遗物」两条链，**锻造图那排下拉根本没扫插件** —— 所以那条路是堵死的。
这一版把生成器源码改了，不用再绕。

## 改了什么

1. **`ForgeTimings.All` 不再是静态快照**：它是「内置那张表 + 现算的插件时机」。
   为什么必须现算：插件是启动时扫描的，静态字段会在插件还没加载时算一次然后永远少那几条
   —— 这正是「下拉里从来没有插件那几条」的根因。
2. `TimingOption` 多了两个字段：`Signature`（插件给的完整 override 签名）和 `PluginGuard`（插件那句守卫）。
3. `ForgeGraphGen.Signature()`：时机带 `Signature` 就**原样**写 `public override <签名>`（不走内置那张钩子表）。
4. `ForgeGraphGen.TimingEntryGuardLines()`：时机带 `PluginGuard` 就用插件那句（内置的「按钩子名猜守卫」表对插件钩子不适用）。
5. 插件时机在**卡牌 / 遗物 / 状态 / 药水**四类布上都能选（显示名前面带「插件：」）。

## 你那边怎么用

* 插件里 `triggers` 写 `hook` + `signature`（写法见 `插件开发规范.md` 第 19 节）；
* 打开锻造图 → 【触发时机】下拉 → 多出「插件：xxx」那几条；
* 选它 → 生成出来的类里就是那段 override + 你的守卫 + 这块布上的效果。

自检里就是拿示例插件的 `PluginCustomHook` 走了一遍，生成结果：

```csharp
public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
{
    if (player != base.Owner.Player) return;   // 插件示例：只认自己回合
    …这块布上的效果…
}
```

## 自检

**4695 PASS / 0 FAIL**（+3 条：插件时机出现在「卡牌」布的下拉里 / 生成的 override 方法名+参数和插件写的一致 /
插件守卫写进去了）。

另外 6 条老断言原本写死「锻造页时机 == 页面上那份，一条不多」，现在把**插件那几条排除后**再比
（口径变了就改断言，不改行为）—— 内置那些档位一条都没动。
