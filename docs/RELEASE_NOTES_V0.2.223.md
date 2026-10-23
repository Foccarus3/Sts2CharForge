# V0.2.223 —— 状态牌真的能局内升级了（「可被无限升级」那一栏以前在状态牌页根本找不到）

> 来源：用户反馈「状态牌能不能像『凋萎』一样局内可以被升级」→ 我答「勾『可被无限升级』就行」→
> 用户实测「但是好像升级没生效」。

## 1. 真因（两条，都是静默失效）

### ① 「诅咒 / 先古 / 状态」那一页没有「可被无限升级」这个开关

状态牌（稀有度 = Status）**只能**在「诅咒 / 先古 / 状态」页编辑；而那个勾选框以前只摆在
「卡牌」页的详情栏里。于是状态牌永远生成：

```csharp
// 状态牌不能升级（本体「粘液 Slimed」也是 MaxUpgradeLevel => 0）
public override int MaxUpgradeLevel => 0;
```

`CardCmd.Upgrade` 第一句就是 `if (!card.IsUpgradable) continue;` → **升级效果什么都没做**。
那一页的说明文字还写着「想让它能升级就勾下面的『可被无限升级』」——说的却是一个不在这一页的勾选框。

### ② 锻造图里的「升级卡牌」不筛「能升级的牌」

卡牌页那份 `EmitUpgradeCard` 一直带 `c.IsUpgradable` 过滤；锻造图那份是**另写的**
（`ForgeGraphGen.EmitCardPick`），`filter` 里没有它 → 选牌界面把状态牌 / 诅咒 / 已经升满的牌
都列出来，玩家选了以后 `CardCmd.Upgrade` 自己跳过 → 观感就是「点了没反应」。

## 2. 修法

1. **「诅咒 / 先古 / 状态」页加了「可被无限升级」**（`CardSpec.ShowInfinitelyUpgradable`：
   诅咒不显示 —— 生成端 `CurseSource` 里写死 `MaxUpgradeLevel => 0`；状态 / 先古 / 普通卡都显示），
   勾上就是 `MaxUpgradeLevel => int.MaxValue` + 每条效果的「升级增量」在 `OnUpgrade()` 里累加，
   和本体「凋萎 Wither」的 `FakeUpgrade()` 是同一个效果（那边每次也是 `DynamicVars.Damage.UpgradeValueBy(3m)`），
   卡面标题自动显示「名字+N」；
2. 那一页的说明文字改成「默认 MaxUpgradeLevel = 0（和本体『粘液』一样）—— 想让它局内能被升级
   （像本体『凋萎』那样）就勾下面那个『可被无限升级』」，勾选框带完整 ToolTip（含「营火也能升」的提醒）；
3. 锻造图「升级卡牌」现在和卡牌页**同一口径**：自己选 → `filter: c => c.IsUpgradable`；
   随机 / 取几张=全部 → 候选后面 `.Where(c => c.IsUpgradable)`；和「只对这张卡」同时填时是
   `(c.IsUpgradable) && (只对这张卡)`；
4. 顺带修一个显隐不刷新：`CardSpec.Rarity` 变化以前只发 `Display`，现在会通知
   `IsCurseCard` / `IsStatusCard` / `IsAncientCard` / `ShowInfinitelyUpgradable` ——
   把稀有度改成「状态 / 诅咒」时那一行会**当场**出现 / 收起（`InfinitelyUpgradable` 也同理刷新提示行）。

## 3. 验收

- 自检新增 11 条（并改 2 条因这次修复而过时的旧断言）：
  - 界面：「诅咒 / 先古 / 状态」页有一行显隐绑在 `ShowInfinitelyUpgradable` 上，且值真的绑在
    `InfinitelyUpgradable` 上；
  - 判据：诅咒不显示、状态 / 先古 / 普通卡显示；改稀有度会通知该属性；
  - 生成：状态牌 + 勾选 → `MaxUpgradeLevel => int.MaxValue` **且不再出现 `=> 0`**，
    `OnUpgrade()` 里 `UpgradeValueBy(3m)`；不勾仍然是 `=> 0`；
  - 锻造图：升级卡牌（自己选 / 随机 / 全部 / 只对这张卡）四种组合都筛 `c.IsUpgradable`；
- 探针 `sacprobe statusup`（含状态牌 ×2 + 图上「升级卡牌」×2）生成出来的工程
  `dotnet build --no-incremental` **0 error**；
- 老存档逐文件对拍（新 Core ↔ 已发布 V0.2.222 的 Core，同一份输入各生成一遍）：
  示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的 GUID**（这两份存档没有状态牌、图上也没有升级效果）；
- `--uicheck`：**4133 PASS / 0 FAIL**。

## 4. 用法（状态牌局内升级）

1. 「诅咒 / 先古 / 状态」页选中那张状态牌 → 勾「**可被无限升级**」；
2. 每条效果填「**升级增量**」（例：造成伤害 6、升级增量 3）；
3. 局内用「**升级卡牌**」效果（自己选 / 随机 / 全部；手牌 / 抽牌堆 / 弃牌堆都行）或营火升级它 ——
   每升一级数值再涨一次，标题 +1 / +2 / +3…

## 5. 版本

- `V0.2.223`（用这一版重新生成一次工程；状态牌先勾「可被无限升级」才会生成成能升级的牌）
