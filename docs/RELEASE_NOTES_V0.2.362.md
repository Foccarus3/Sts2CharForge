# V0.2.362 — 插件「自定义触发时机」：钩子不再只能挑编辑器收录的那些

这是插件 B 组的最后一条。以前插件触发时机只能填**编辑器认识**的钩子 id（填别的直接拒绝加载），
于是本体有、我们没收录的钩子，插件作者一点办法都没有。

## 现在可以这样写

```json
{ "id": "PluginCustomHook", "display": "回合开始最早那一刻",
  "hook": "AfterPlayerTurnStartEarly",
  "signature": "async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)",
  "guard": "if (player != base.Owner.Player) return;" }
```

* 写了 `signature` → 生成端把这段 **override 原样写进状态类**，方法体 = 你的 `guard` + 这条时机上的效果；
* 这时 `hook` 只当**方法名**用：要有英文/数字/下划线，且**必须出现在 signature 里**（否则加载时报问题）；
* 签名里已经声明了 `PlayerChoiceContext choiceContext` → 生成端**不会**再声明一次（否则 CS0128 重复定义）；
* 写错方法名 / 参数对不上 → **编译期**报错（CS0115 / CS0111 之类），不会静默失效。

**意义**：本体以后加新钩子、或者某只钩子我们没收录，插件作者自己就能用，不用等编辑器升级。

## 自检

**4678 PASS / 0 FAIL**（+3 条：生成的状态类里出现插件给的那段 override / 守卫写进去了 /
签名里已有 choiceContext 时不会重复声明）。示例插件新增 `PluginCustomHook`（`AfterPlayerTurnStartEarly`）可直接照抄。

顺带修了两处：
* 自检里「补 choiceContext」原来是个**假断言**（`Contains("choiceContext")` 被签名本身命中），
  已改成核对「`PlayerChoiceContext choiceContext` 在整份代码里只出现一次」；
* 示例插件 JSON 插入时漏了对象之间的逗号 → `ConvertFrom-Json` 直接报错（这已经是第二次了，
  交接里写了：**改完示例 plugin.json 必须先跑一遍 ConvertFrom-Json**）。

## 插件体系到此的状态

| 能力 | 状态 |
|---|---|
| 效果种类（5 条链 + 18 占位符 + `{transformSelf}`） | ✅ |
| 触发时机（别名式 / **自定义签名**） | ✅ |
| 条件表达式 | ✅ |
| 锻造模块 | ✅ |
| 自定义页（7 种字段、`ref` 引用存档、`list` 动态行、条件宏、一页多文件 / 按行产出） | ✅ |
| 卡面文字 / 悬停说明 / 动态变量 | ✅ |
| 随 mod 打包 cs / 资源 | ✅ |
| 启用禁用 / 值按存档隔离 / apiVersion 自动迁移 | ✅ |

也就是说：**你之前列的插件缺口，现在全部清了。**
