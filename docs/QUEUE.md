# 待办队列（用户要求「全做完，不用一步步报备」）

## 第 27 批需求（V0.2.141：**修「回读把『数值公式 + 升级增量』的增量算重一倍」**，用户实测报的 BUG「在生成器填负值的力量不生效」；**未发版**）

> 派活 HEAD = `65960db 发版 V0.2.140`；`--uicheck` 基线 = **3277 PASS / 0 FAIL**（Debug；本批改动前实测，3277 + 新增 20 条 = 3297）。
> 任务书：`_tmp\待发任务书_V0.2.141_回读升级增量算重.md`。**本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**
> （连 `status` 都只读跑；改动前的基线用**已完成产物副本** `_tmp\v02141_主代理\rec\proj` 对拍，**没用 `git stash` / `worktree`**）。
> 红线⑧ 照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；一切验证只用 `ProfileFactory.Sample()` +
> `_tmp` 自造探针（探针只用 `--build`，**绝不 `--install`**）。**「断言必须能 FAIL 在修好之前」这条用「临时把核心修复改回旧写法、重编 Core、跑一遍自检」实测过**（见下表 ① 的验收数字）。

> **用户原话（2026-10-10）**：「在生成器填负值的力量不生效。」
> 主代理已 100% 复现（不是猜）：用户存档里唯一用「力量」的牌是**闪耀登场 `SparkleCardEntrance`**
> （力量 +1 / 升级增量 1 / 作用于自己）；它「从工程恢复存档」回来后**升级增量被算成了 2**。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **回读时同一个升级增量不能加两次**（核心 BUG） | **真因（在回读端，生成端一直是对的）**：`ProjectRecovery.FillExpr` 认打出时的三元式 `(base.IsUpgraded ? A : B)` → `Amount=B` / `UpgradeAmount=A-B`（对），而 `ParseUpgrade` 又认 `OnUpgrade` 里的 `base.DynamicVars["X"].UpgradeValueBy(N)` → **无条件 `e.UpgradeAmount += delta`**。生成端对「数值公式 + 升级增量」的 5 种效果（伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态）把**同一个增量写三处**（变量构造第二参 / 打出时三元式 / `OnUpgrade`），于是 1+1=2 → 再生成出来 `? 3m : 1m`；公式改成 `-2` 之后升级过的牌 = `-2+2 = 0` 层 = 打出去什么都不发生。<br>**修**：`EffectSpec` 新增 `[JsonIgnore] UpgradeFromTernary`（**只在一次回读里有意义、不写进存档、不参与逐字节对拍**），`FillExpr` 命中三元式时置 true；`ParseUpgrade` 遇到带标记的效果：两边**相等**（生成端就是这么写的）→ **跳过**（保留三元式那个值）；两边**不等**（老产物 / 手改过的工程）→ **以 `UpgradeValueBy` 为准覆盖**（`e.UpgradeAmount = delta`）—— **绝不 `+=`**。判据是**标记**，不用「值是不是 0」猜。<br>**不许改坏的两条**：非公式路（`new DamageVar(6)` + `UpgradeValueBy(3m)`，无三元式）照旧累加到 3；真·现算公式（`(base.IsUpgraded ? ((M*2)+3m) : (M*2))`，走不到 `FillExpr` 的三元式分支）照旧靠 `UpgradeValueBy(3m)` 得到 3。<br>**验证（真产物）**：`--recover _tmp\v02141_主代理\rec\proj` → `ApplyPower/StrengthPower` 的 `UpgradeAmount` 从 **2 → 1**（同卡 `StarsVar(3)+UpgradeValueBy(1m)` 照旧 1）；再生成 → `? 2m : 1m`、`UpgradeValueBy(1m)`，与回读前的原文**逐字一致**。 | ✅ 待发 |
| ② | **把这个坑变成看得见的警告**（校验器，**不得**新增任何错误） | 判据 `CSharpCodeGen.UpgradeCancelsToZero`：**纯常数公式**时「常数 + 升级增量 == 0」（= 升级后那一档是 0）；非卡牌链（遗物 / 药水 / 状态）上「数值 + 升级增量 == 0」（那些用固定 `Amount`，折不出公式 → 落在同一个判据的另一支）同理。判据与生成端**共用** `ForgeNumbers.TryEffectFormulaConst`（新加的公开小助手 = `ResolveText` + `Place.PrologueVars` 折常数，和 `CSharpCodeGen.IsNegativeFormulaAmount` 同一口径），**不另写一套「看着像数字」的正则** —— 否则会出现「界面说升级后 0、生成端却走了运行时公式那一支」。<br>文案说三件事：谁抵掉了谁 / 后果 / 怎么改 ——「升级增量 +2 把数值公式里的 -2 抵成了 **0** —— **升级后的这张牌打出去不会有任何效果**（没升级的那张才是 -2 层）。想让升级后照样有 2 层，请把「升级增量」清成 0；想让升级后更强，请把「升级增量」改成正数方向。」**级别 = 警告**（不是错误：老存档 / 用户就想这么配都得能继续生成，也避免给老存档冒出一堆新错误）。<br>「真·现算公式」折不出常数 → **不报**（升级后是不是 0 要打出时才知道，不乱猜）。<br>没有另加「ApplyPower 填负数且状态不允许负层数」那条（V0.2.139 已有），按要求只保证它在**效果列表摘要行**上也看得见（见 ③）。 | ✅ 待发 |
| ③ | **界面：效果列表摘要行写出「升级后最终值」** | `EffectSpec.Display`：常数公式 + 非零增量时把**升级后的最终值**写在括号里 —— `增益/减益 StrengthPower 数值公式 -2（升级后 0） → 自己`（用户在**列表里**就能看见，不用点开逐条看）；非公式那条路照旧写「（升级 +N）」（老文本一字不变）；真·现算公式折不出常数 → **不写**这个尾巴（写个假数字不如不写），照旧 `伤害 数值公式 M*2 → 单体敌人`。<br>`DisplayPlain`（遗物 / 药水列表用）跟着剥掉 `（升级后 N）`，否则那两页会莫名多出一段「升级后」；剥不掉就原样返回（不乱删字符）。 | ✅ 待发 |

> **① 的验收数字（真产物副本 + 自造探针）**
> - **修之前**：`--recover _tmp\v02141_主代理\rec\proj` → `Amount=1 / Formula='1' / **UpgradeAmount=2**`（错）；
>   再生成 `--profile` → `(base.IsUpgraded ? **3m** : 1m)`。
> - **修之后**：`UpgradeAmount=**1**`；再生成 → `(base.IsUpgraded ? **2m** : 1m)`、`UpgradeValueBy(1m)`，
>   与 `rec\proj\cs\Cards\SparkleCardEntrance.cs` **逐行一致**。
> - **「断言必须能 FAIL 在修好之前」实测**：把 `ParseUpgrade` 那一支临时改回旧写法 `if (e is not null) e.UpgradeAmount += delta;`、
>   只重编 `Sts2CharForge.Core`、把那份 Core dll 拷进一份**应用产物副本**（`_tmp\v02141probe_tmp\negdump`）再跑 `--uicheck`
>   → **3293 PASS / 2 FAIL**，两条正是核心断言：
>   `回读不重复累加 … → UpgradeAmount=2（期望 1）`、`拿回读结果再生成 → … → 出现了 3m（增量算重了）`；
>   改回来即 **3297 PASS / 0 FAIL**。（临时改动**已还原** —— `git status` 里只有本批该改的 9 个文件。）
>
> **红线实测数字（本批）**
> - ① `--samplegen` 改动前后逐文件对拍（行尾归一化后逐字节比）：**113 个文件里 112 个逐字节一致**，
>   唯一差异 = `ExampleMod.sln` 的**随机 GUID**（两边文件集合也完全相同）。生成端**一个字节都没动**。
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**（371 警告，与本批前同数）。
> - ③ `--uicheck`：Debug **3297 PASS / 0 FAIL**；发布版（`dotnet publish -c Release -r win-x64 --self-contained true`
>   出的**全新** exe，时间戳 20:41:03 **晚于**全部改动源码的最新时间 20:37:10）**3297 PASS / 0 FAIL**。
> - ④ 探针真 `--build`（`Paths` 原样照抄 `_tmp\v02140\probe_cardslot_6.json`，只改 `OutputDir` + Godot / dotnet）：
>   `dotnet build` **0 错误**（"已成功生成。0 个错误"）+ 导出 **`CardSlotProbe6.pck` 44304 字节**；
>   对该工程 `--recover` → **0 条「没认出来」**（输出里连那一段都没有）。
> - ⑤ 端到端（真产物）：`--recover _tmp\v02141_主代理\rec\proj` → `SparkleCardEntrance` 的
>   `ApplyPower/StrengthPower` 变成 `Amount=1 / Formula='1' / UpgradeAmount=**1**`；
>   用回读结果再生成 → `cs\Cards\SparkleCardEntrance.cs` 与回读前那份**逐字节一致**（CRLF 归一后 true），
>   三行形态：`new ForgeFormulaPowerVar<StrengthPower>(static (card, target) => (1m), 1m)` /
>   `(base.IsUpgraded ? **2m** : 1m)` / `UpgradeValueBy(1m)`。


## 第 26 批需求（V0.2.140：**卡牌左侧序号改成和遗物页一样 —— 每 5 个一块 + 空槽直接显示灰色序号**，用户对 V0.2.130 那次的更正；**未发版**）

> 派活 HEAD = `f906f2c 发版 V0.2.139`；`--uicheck` 基线 = **3241 PASS / 0 FAIL**（Debug 与发布版都是）。
> 任务书：`_tmp\待发任务书_V0.2.140_卡牌序号五格一块.md`。**本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**
> （只读 `status/diff/log/show` + `git worktree add`；改动前基线用 `git worktree add ..\_tmp_v02140_base f906f2c --detach` 取 HEAD 源码，**没用 `git stash`**）。
> 红线⑧ 照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；一切验证只用 `ProfileFactory.Sample()` +
> `_tmp` 自造探针；探针只用 `--build`；禁区（动画四个 `.cs`、`MainWindow.xaml` 动画页 + `AnimationSpec`、刮削 `Scrape`）一个字没碰。

> **用户原话（2026-10-10，这是对 V0.2.137/V0.2.130 那次的更正）**
> 「不是说卡牌左侧的序号做的像遗物一样，五个五个列，直接显示出来，比如只有一张卡时，有1-5序号，当有6张卡时，出现6到10序号」
> **范围**：用户已确认「要」→ **三个卡牌列表页全做**（卡牌 / 诅咒卡 / 先古卡）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **卡牌左侧序号完全照遗物页 / 药水页那套**（每 5 个一块 + 空槽直接显示灰色序号），**三个卡牌列表页全做** | **行数** = `5 × ceil(max(N,1)/5)`（N = 该页真卡数）：1 张 → 1~5 行；5 张 → 1~5；**6 张 → 1~10**（7~10 空槽）；11 张 → 1~15。**序号仍是位置号**（空槽不占号、不跟随卡牌移动）。**三页共用一份实现** —— 新增 `SlotRowMirror<T>`（`src\Sts2CharForge.App\SlotRowMirror.cs`），三页各 new 一个实例（只有「读哪个真集合」不同），**不复制三份**；`SlotPlaceholder` / `ISlotRow` 沿用 V0.2.127 的通用类型（一个字没改）。**界面**：`ItemsSource` 从 `Profile.Cards` 改成**界面镜像集合** `CardRows` / `CurseRows` / `AncientRows`（`ObservableCollection<ISlotRow>`，**public** —— WPF `PropertyPath` 读不到 internal，遗物页实测踩过）；三页各自的 `ItemTemplate` 加序号列 + `DataTrigger IsPlaceholder`（`#AAAAAA` + `FontWeight=Normal` + 卡名那列 `Collapsed` + `ToolTip=null`）。**详情栏**：`CardDetail` 的 DataContext 改 `CardUiSelection`、显隐改 `HasCardSelection`（诅咒 / 先古同理 `CurseUiSelection`/`HasCurseSelection`、`AncientUiSelection`/`HasAncientSelection`）—— 点空槽时 `UiSelection` 保持上一张真卡、详情不清空，真取消选中才回空态（照 V0.2.125/V0.2.127 先例）。**拖动**：`CardRowAtPoint` 已给行号，空槽可当落点（`MoveCardTo` 会把行号夹到合法范围）；`OnAddCard` 不再用 `SelectedIndex = Count-1`（Count 是真卡数、不是行数，会落到空槽上）。**两个坑照先例挡**：`Rows.Clear()` → `SelectionChanged → SyncDetail → 重铺` 的**重入**（dirty 循环 + `IsSyncing` 期间不递归进 `SyncDetail`）；有几处历史代码直接给 `Profile` 字段赋值（不走 setter）→ 「挂错存档就重挂」自愈。**`CardList.SelectedItem` 逐处核**（本轮做了全量审计，分 A 写入 / B 天然过滤 / C 潜在 NRE 硬转 / D 口径失效 / E 注释）：**顺手修掉 3 个用户可见缺陷** —— (a) `SpecialCard` 只读 `AncientCardList.SelectedItem as CardSpec`，点空槽会让**先古卡那栏右侧详情整块塌掉** → 改 `AncientUiSelection`；(b) `CardEffectsAreForged` / `CardCoreFieldsVisible` 同理会**解锁锻造卡的效果编辑器** → 改 `CardUiSelection?.IsForged`；(c) `ForgeEnergyCostText` 同理会**让只读费用提示消失** → 改 `CardUiSelection`。`SpecialCardOf()` 的诅咒 / 先古两档也一并改读镜像属性。**生成端零改动**：`SlotRowMirror` / `SlotPlaceholder` 只被界面用，`Generation\*.cs` 一处没引用；`CardSpec` 新增的 `IsPlaceholder => false` 是 `[JsonIgnore]`（和 `RelicSpec` / `PotionSpec` 同形），存档面零变化 | ✅ 待发 |

> **① 的验收数字**：`--samplegen` 改动前后逐文件 SHA256（只有 `ExampleMod.sln` 的随机 GUID 不同）；
> `dotnet build Sts2CharForge.sln -c Debug --no-incremental` **0 错误**；`--uicheck` Debug + 发布版各 0 FAIL（基线 3241）；
> 新增断言按用户原话三档钉：1 张 → 5 行（序号 `1.,2.,3.,4.,5.`，2~5 空槽）、6 张 → 10 行（1..10，7~10 空槽）、11 张 → 15 行、
> 删回 5 张 → 5 行、空槽点得到但 `Card = null` 且不改任何卡、不进全选、`Profile.*` 里没有占位项、
> 拖到空槽顺序真变且序号仍 1..N 连续、空槽序号实测 `#AAAAAA`、真生成一遍零「空槽」痕迹、行宽 ≤ 列表宽 + 2px；
> 诅咒 / 先古两页各自同形（2 张 → 5 行、6 张 → 10 行、前缀 `Curse<n>` / `Ancient<n>` 各自从 1 起）。
> **遗留（等用户定夺）**：诅咒卡 / 先古卡两栏目前**没有**接拖动（XAML 里本来就没有那四个拖放事件）——
> 它们的空槽行**暂时不能当拖放落点**；要的话下一版补。

## 第 25 批需求（用户 2026-10-10 报的**两条**；**未发版**，版本号仍是 V0.2.138）

> 派活 HEAD = `d3d7b60 发版 V0.2.138`；`--uicheck` 基线 = **3229 PASS / 0 FAIL**（Debug 与发布版都是）。
> 任务书：`_tmp\待发任务书_V0.2.139_回血复活与负值力量.md`。**本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**
> （只读 `status/diff/log/show` + `git worktree add`；改动前基线用 `git worktree add` 取 HEAD 源码，**没用 `git stash`**）。
> 红线⑧ 照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；一切验证只用 `ProfileFactory.Sample()` +
> `_tmp` 自造探针；探针只用 `--build`；禁区（动画四个 `.cs`、`MainWindow.xaml` 动画页 + `AnimationSpec`、刮削 `Scrape`）一个字没碰。

> **用户原话（两条）**
> ① 「状态：当你受到伤害或失去生命时，你回复3点生命。BUG：多人模式，有队友存活时，被敌人打死后可以回复生命从而复活。」
> ② 「在生成器填负值的力量不生效」（用户补充：卡牌「施加增益/减益」、数值 -2、升级增量 0、未升级牌）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **多人模式：致死伤害 / 失去生命后靠回血「复活」** | **根因**：`DamageReceived` / `HpLost` / `Attacked` 三条时机的守卫里没有「自己已经死了就别再跑」。本体 `AfterDamageReceived` / `AfterCurrentHpChanged` 是**无条件广播**的（致死那一下照样进），`AfterAttack` 是**一次攻击所有命中结束后**跑一次（致死那一击也在这时点）—— 效果里的「回复生命」把 HP 从 0 拉回正数，多人模式下队友还活着 = 看起来复活。**修**：三条时机 × 三条链共 **9 处**插入点全部加一句守卫（位置在归属守卫之后、效果之前，用各自链**正确**的实例表达式）：<br>· 状态链（`CustomPowerGen.cs` 的 `DamageReceived` / `HpLost` / `Attacked` 三个 case）→ `if (base.Owner.IsDead) return;`（这条链的 `base.Owner` 本身就是 `Creature`）；<br>· 遗物链 + 锻造链（`CSharpCodeGen.RelicTriggerGuardLines` 与 `ForgeTriggerGuardLines` 的同名 case）→ `if (base.Owner.Creature.IsDead) return;`（那两条链的 `base.Owner` 是 `Player`）。<br>单人模式同样生效（致死那一下不再触发这只遗物 / 这个状态）—— 预期语义，写进 README。<br>⚠ 顺手把 `IsRelicGuardLine`（把前置行分成「守卫 / 声明」的判据）改成**先剥掉行尾 `//` 注释**再找 `return;` —— 它原来只看整行里有没有 `return;`，注释里出现那三个字就会被**静默挪进守卫**那一拨、再被 `NegateRelicGuard` 取反塞进 `if (…)`。 | ✅ 待发 |
| ② | **填负值的「力量」不生效** | **根因（两层）**：<br>**(a)** `CSharpCodeGen.ApplyPowerLine` 的负数分支**一律**走 `GetPower + ModifyAmount`（目标身上没有该状态就什么都不做），而本体有 5 个状态 `AllowNegative => true`（力量 / 敏捷 / 专注 / 尖啸 / 萎缩）—— 本体「尖啸」扣敌人力量就是 `PowerCmd.Apply<StrengthPower>(目标, -6)`。**修**：按 `EffectCatalog.AllowsNegativeLayers` 分流，允许负层数 → 直接 `Apply(目标, 负值)`（与「临时增益」那条**已有**的正确写法对齐，同一条实现），不允许 → 保持 `GetPower + ModifyAmount`。**四条链 + 锻造链**统一走这一份（锻造链原来内联了自己的一套 `if (amount < 0m) { GetPower → ModifyAmount(amount) }`：传进去的 amount 本身就是负的 = **ModifyAmount(-2) 等于加 2 层**，方向反了）。<br>**(b)** 卡牌链上那 5 种支持公式的效果只要数值 ≠ 0，`ProfileFactory.MigrateAmountsToFormulas` 就会把固定数值搬进「数值公式」栏（界面从此只编辑公式）→ `IsNegativeAmount` 里那个 `!UsesAmountFormula` 直接把负数分档**关掉**：用户从存档载入的卡（`-2` 已经变成公式 `"-2"`）在卡牌链上被当成**正数**生成。**修**：`IsNegativeAmount` 增加「公式在生成期能折成负数常数」这一档（`IsNegativeFormulaAmount`，拿不到配置 / 含运行时量时返回 false，不乱猜）。<br>**(c) 回读**：`ProjectRecovery` 原来只认 `ModifyAmount + __negPower`（而且判据写死 `choiceContext`）→ 不认新的 `Apply<T>(…, 负值)` 写法，回读会把符号读**反**。新增一条分支（判据看**第 2 个实参** = 数值；第一版写成第 1 个实参 = 目标，永远判不成负数、等于整支没生效）＋ `FxConstantAmount` 认 `=> -((2m))` 这种「负数公式」形态。<br>**校验器 / 文档**：`ProfileValidator` 两处负数提示文案按两档改写，`README.md` / `tools\使用说明.template.txt` 同步。 | ✅ 待发 |

> **② 的生成行对照（力量 -2 / 敏捷 -3 / 虚弱 -1，四条链）**
>
> | 链 | 力量 -2 | 虚弱 -1 |
> |---|---|---|
> | 卡牌（从存档载入，数值已迁进公式） | `await PowerCmd.Apply<StrengthPower>(choiceContext, base.Owner.Creature, -((2m)), base.Owner.Creature, this);` | `{ PowerModel? __negPower = base.Owner.Creature.GetPower<WeakPower>(); if (__negPower is not null) await PowerCmd.ModifyAmount(choiceContext, __negPower, -(System.Math.Abs(base.DynamicVars["WeakPower"].BaseValue)), base.Owner.Creature, this); }` |
> | 遗物 | `await PowerCmd.Apply<StrengthPower>(choiceContext, base.Owner.Creature, base.DynamicVars["StrengthPower"].BaseValue, base.Owner.Creature, null);` | 同上（`cardSource` = `null`） |
> | 药水 | 同遗物 | 同遗物 |
> | 自定义状态 | `await PowerCmd.Apply<StrengthPower>(choiceContext, base.Owner, -2m, base.Owner, null);` | `{ PowerModel? __negPower = base.Owner.GetPower<WeakPower>(); … }` |
> | 锻造图 | `foreach (Creature __fgFoe in ForgeFormulaRuntime.TargetOrFirst(__fgTarget, __fgPlayer)) await PowerCmd.Apply<StrengthPower>(choiceContext, __fgFoe, (-2m), __fgSelf, null);` | `foreach (…) { PowerModel? __negPower = __fgFoe.GetPower<WeakPower>(); if (__negPower is not null) await PowerCmd.ModifyAmount(choiceContext, __negPower, -(System.Math.Abs((-1m))), __fgSelf, null); }` |
>
> **老存档影响**：只有「负数 + 那 5 种状态」的配置会变（从「什么都不做」变成「真的扣成负数」）—— 这正是修复；
> 其余状态（虚弱 / 易伤 / 中毒…）的生成文本与以前**逐字相同**。

> **红线实测数字（本批，Debug 与发布版各一遍）**
> - `--samplegen` 改动前后逐文件 SHA256：**112 个文件，只有 `ExampleMod.sln` 不同**（随机 GUID，允许）。
> - `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误 / 365 警告**。
> - `--uicheck`：Debug **3241 PASS / 0 FAIL**（基线 3229，本轮 +12）、发布版 **3241 PASS / 0 FAIL**。
> - 两份 `_tmp` 自造探针（`probe_revive.json` = 受到伤害 / 失去生命时回复 3 点生命；`probe_neg.json` = 力量 -2 / 敏捷 -3 / 虚弱 -1）
>   → `--profile … --out … **--build**` 各 **0 错误**、PCK 导出成功；`--recover` 0 条「没认出来」。
> - `--buildcheck`：**① 不给 `--install` → 陷阱目录 0 个文件**（红线⑧ 的机器判据）。

## 第 24 批需求（用户 2026-10-10 当天报的**四条**；**未发版**，版本号仍是 V0.2.136）

> 派活 HEAD = `daf8a95 发版 V0.2.136`；`--uicheck` 基线 = **3200 PASS / 0 FAIL**（Debug 与发布版都是）。
> 任务书：`_tmp\待发任务书_V0.2.137.md`。**本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**
> （只读 `status/diff/log/show` + `git worktree add/remove`；两处删除的源文件用 `git reset` 把索引退回
> HEAD、工作区仍是「已删」—— 没有产生提交）。
> ⚠ 本轮**新加的红线⑧**（不许改用户存档目录、不许写用户游戏安装目录、探针只用 `--build`）：
> - **不许改 `D:\ds\s\4\自定义角色存档\` 下的任何东西** —— 要拿真实存档做输入**先复制到 `_tmp`**
>   （生成端会在存档旁边刷新 `<存档名>.forge_classmap.json`）；
> - **不许写用户游戏安装目录**（`d:\download\steam\…\Slay the Spire 2\mods` 等）；
> - **探针只用 `--build`、绝对不用 `--install`** —— CLI 会把「不像游戏 mods 目录」的目标**自动改写成真实
>   游戏 mods 目录**，V0.2.135 那轮真把探针模组装进了用户的游戏目录；
> - 不许发版 / 不许跑 `release_all.ps1` / 不许任何 git 写操作（只读 `status/diff/log/show` 与 `git worktree add/remove` 可以）；
> - 禁区（动画四个 `.cs`、`MainWindow.xaml` 动画页 + `AnimationSpec`、刮削 `Scrape`）一个字不许碰；改 `.ps1` 保持 BOM。

> **用户原话（四条）**
> ① 「额外资源量右侧那个添加效果点了没反应。」
> ② 「卡牌的那个排序有点问题，可以做的和遗物左侧的排序一样。」
> ③ 「自定义描述的高亮字段指的是把游戏本体中显示高亮的字符直接显示在生成器的升级后自定义描述中，让用户可以自己填。」
> ④ 「遗物右侧效果栏怎么变那么奇怪，没有可以精简的效果栏吗，药水也是」
> **用户已拍板**：②= **A**（每页列表各自 1..N、自动英文类名跟这个号、诅咒 / 先古卡用各自前缀）；
> ③= **A**（撤掉弹窗多选，`{Damage:diff()}` 当普通文字让用户自己填）；④= **A**（每条效果默认折叠成一行、点开才展开）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **额外资源量页「添加效果 / 删除效果」点了没反应** | **根因**：效果栏那三个按钮走公用处理器 `OnAddEffect` / `OnRemoveEffect`，它们用 `listBox.DataContext` → `EffectsOf(obj)` 找效果列表；而 `MainWindow.EffectsOf`（`MainWindow.xaml.cs:7105` 一带）只认 `CardSpec / RelicSpec / PotionSpec / PowerTriggerSpec` —— **没有 `ExtraResourceSpec` 分支** → `return null` → 只弹一句「请先在左边选中要编辑的条目。」什么都不做。**修**：给 `EffectsOf` 加 `ExtraResourceSpec` 分支（返回 `ExtraResourceSpec.Effects`）。**同类漏网全查了一遍**（清单见下）：`OnRemoveEffect` 的「归属 / 撤回栈」switch 也补了 `ExtraResourceSpec` 一条（撤回栈仍是 `_cardUndo`，因为那一页的「撤回删除」按钮走的就是 `OnUndoCard`；以前掉进兜底分支、文案只有光秃秃的「效果」）；`RefreshVisibleLists` 原来只刷卡牌 / 遗物 / 药水三栏效果列表，**漏了额外资源量那一栏** → 补上（换存档 / 撤回删除后那一栏不重挂）。**自检「真点按钮」**：`ClickButtonByContent("添加效果"…)` → 断言 `Effects.Count +1`、选中项变成新加的那条、默认值照旧、再点一次 +2、选中后点「删除效果」真的删掉、`CanUndoCard` 变 true、点「撤回删除」那一条回到原位；另加「列表空着时点删除效果安全无副作用」。**顺手给额外资源量页的「撤回删除」按钮补了 `ToolTip` + `IsEnabled` 绑 `CanUndoCard`**（以前没有 `ToolTip`，看不出撤回的是哪一条）。 | ✅ 待发 |
| ② | **卡牌序号口径改成「和遗物页一样」（拍板 A）** | **序号**：`RefreshCardOrdinals` 改成**每页各自 1..N**（`Ordinal` = 它在自己那一页列表里的位置；`LastAutoOrdinal` 同值）。**三套前缀**：`Naming.CardStem`/`CurseStem`/`AncientStem` + `CardStemOf(CardSpec)`（按**稀有度**判，和生成端分流同一口径；状态卡住 `Cards` 列表、用 `Card` 前缀）+ `CardListOf` / `CardRowOf` / **`AutoCardClassName(p,c)`（唯一口径来源）**。`Naming.CardClassName(p,c)` 与 `RenumberCardClassNames(自己那一页列表)` 都走它；`IsAutoCardClassNameFor` 三个前缀都认（少了任何一个都会出现「自动名被当成手填、永远不跟列表走」）。`NameStabilizer.RenumberCardNames` 改成对 **`Cards` / `Curses` / `AncientCards` 分别**跑（传 `AllCards` 会退回老口径）。**校验器**那条「长得像自动编号 → 下次会改名」的提示改成调 `n.AutoCardClassName(p,c)`（并写清是**哪一页**的第几位）。**界面**：三处「英文类名」标题 / 小字跟着改（卡牌页 + 诅咒页 + 状态卡页），卡牌页多一行说明三套前缀；`CardSpec` 新增 `[JsonIgnore]` 的 `AutoClassNameStem` / `CharClass`（由 `RefreshCardOrdinals` 按稀有度填 —— 模型层看不到「角色类名」和「它在哪个列表里」），`AutoClassNameHint` 因此能拼出 `SparkleCurse1` 这种真名字（顺手修掉「`<角色类名>Card1` 占位符」那个老缺口）。**引用一起改**：`NameStabilizer.RepairCardClassReferences` 原有那套（效果「目标卡」/「转化为先古卡」/ 锻造图「目标卡」字段 / 上传的卡面键）**一行没改就继续生效**（`renamed` 表还是旧类名→新类名）。**回读 / 本地化 / 素材**：`ProjectRecovery`（按稀有度回各自列表）、`LocalizationGen`（entry 键）、`ArtGenerator`（素材文件名）本来就统一调 `CardClassName(p,c)`，**零改动**。**结论：「本体卡（打击 / 防御）还占不占号」→ 占**（它们就在「卡牌」列表里、仍是第 1、2 号）；变的是诅咒 / 先古卡：老口径接着普通卡往下数，新口径各自从 1 起、换前缀 | ✅ 待发 |
| ③ | **高亮改成用户自己填（拍板 A）** | **撤掉**：`MainWindow.xaml` 里「高亮字段…」按钮 + 按钮右边那行汇总 + 那段注释；`CardUpgradedHighlightSummary` / `RefreshCardHighlightSummary` / `OnPickUpgradedHighlights` 三个成员**删掉**（`SyncDetail` 里那次调用也删）；`HighlightPickerDialog.xaml(.cs)` **整个文件删除**（唯一入口就是那个按钮）；`OnUpgradedCustomDescChanged` 留成**空处理器**（XAML 里的 `TextChanged` 挂钩不动，少改一处）。**改成**：那一栏底下摆一句说明「直接写 `{Damage:diff()}` 就高亮、写 `{Damage}` 就普通色」+「填入参数…」的 ToolTip 写明「卡牌给的就是带 `:diff()` 的高亮写法」。**`ParamHints` 到底给不给 `:diff()`**：**本来就给**（`CSharpCodeGen.ParamHints` 里 `isCard ? $"{{{key}:diff()}}" : $"{{{key}}}"`，V0.2.135 就是这么写的）→ **一处代码都没改，口径是「卡牌给带 `:diff()` 的、遗物 / 药水给不带 `:diff()` 的」**，自检新增两条把这个事实钉住。**保留**校验器那两条（`:diff()` 字段不存在 → 错误；整段没有 `{}` 却写了 `:diff()` → 警告；裸 `{X}` 不存在 → 警告）。`Core\Generation\HighlightDiffText.cs` **留着当工具方法**（现在界面不再调用它；注释里写明了保留理由与「以后要用就从这里拿」），自检不再测弹窗、改为测「按钮真的没了 / 说明在 / 来源带 `:diff()` / 存读一圈逐字不变 / 生成端原样写进 cards.json / 校验器两条仍在」 | ✅ 待发 |
| ④ | **效果栏默认折叠成一行、点开才展开（拍板 A）** | **新模板**：`MainWindow.xaml` 窗口资源里加 `EffectEditorCollapsible` —— 折叠态 = 一个小三角 + **一行摘要**（`EffectSpec.Display`：效果种类 + 数值 / 升级增量 / 作用对象 / 条件…，**不另算一份文案**）+ 右边「展开 / 收起」；整行是一个 `Button`（`Click="OnToggleEffectRow"`，用 Button 而不是 ToggleButton 是为了**自检里能真点**）→ 翻 `EffectSpec.IsExpanded` → 下面的 `EffectRowDetails` 显隐。**展开区里用的还是原来那套编辑器**：`App.xaml` 把 `EffectEditorPlain` / `EffectEditorPotion` 各拆成「外壳（ScrollViewer）+ 内容（`…Inner`）」两层，三个页面各自的 `ListBox.Resources` 里放一份 `EffectRowEditorTemplate`（`x:Shared="False"`）指向对应的 Inner —— **一个字都没重写**。**模型**：`EffectSpec.IsExpanded`（`[JsonIgnore]`、默认 false，不写存档、生成端不读）。**作用范围**：遗物页 / 药水页 / 额外资源量页右栏；卡牌页 / 诅咒页 / 先古卡页**不动**。**V0.2.124 那个坑**：三个 ListBox 的 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` + `HorizontalContentAlignment="Stretch"` 原样保留，自检钉「折叠态与展开态行宽都 ≤ 列表宽 + 2px」（展开区多 16px 左边距也过了）。**新增转换器** `BoolToTextConverter`（`bool → 文字`，参数 `展开/收起`；分隔符用 `/` 而不是 `|`，因为 XAML 标记扩展里 `|` 不稳）。**自检**：遗物页 15 条 + 药水页 6 条 + 额外资源量页 1 条，全部是**可视树证据**（`EffectRowDetails.Visibility` / `ActualHeight` / 三角文字 / 摘要文本含关键参数 / 展开后改「数值」框真的写回模型 / `IsExpanded` 不出现在存档 JSON 里） | ✅ 待发 |

> **① 的「同类漏网」清单（本轮逐个查过，按类型分支却没带 `ExtraResourceSpec` 的地方）**
> - `MainWindow.EffectsOf`（`MainWindow.xaml.cs:7105` 一带）—— **真 bug**，已修（用户报的那条）。
> - `OnRemoveEffect` 的「归属 / 撤回栈」switch（同文件再上几行）—— **真漏**（掉兜底分支），已修。
> - `RefreshVisibleLists`（`MainWindow.xaml.cs:4636` 一带）—— **真漏**（少刷第四栏），已修。
> - `MainWindow.xaml` 那一页「撤回删除」按钮—— 原来**没有 `ToolTip` / `IsEnabled`**（另外两页都有），已补绑 `CanUndoCard` / `UndoCardHint`。
> - 其余按类型分支的地方**都查过、都不需要加**：`SyncDetail`（按 `CardList` / `RelicUiSelection` / `PotionList` 选中项分支，额外资源量页那条效果走 `OnExtraResourceEffectSelectionChanged` 清遗物选中，和它无关）、
>   `OnCardEffectSelectionChanged`（只管「卡牌链」标记，额外资源量 / 遗物 / 药水本来就不该被打上）、
>   `NameStabilizer.RepairCardClassReferences`（已经单独处理 `p.ExtraResource.Effects`，见 `AllEffects` + 那一支显式循环）、
>   `OnAddEffect`（只依赖 `EffectsOf`，修一处即可）。
> - ⚠ `ExtraResourceSpec` **不是 `SpecBase` 子类**（没有 `PropertyChanged`）——它的 `Effects` 集合本身有通知，所以列表能刷新；
>   但 `HideInGame` 这类标量属性改了界面不会自己更新（**既有状况**，不在本轮四条范围内，留给以后）。

> **② 的「老 → 新类名映射表」（口径：普通卡 `<角色>Card<n>` 不变；诅咒 / 先古卡从「接着普通卡连续编号」改成「各自从 1 起 + 换前缀」）**
>
> | 卡的种类 | 老口径（V0.2.130 ~ V0.2.136） | 新口径（V0.2.137） | 说明 |
> |---|---|---|---|
> | 普通卡（含本体卡 / 打击 / 防御 / 状态卡） | `<角色>Card<它在 Cards 列表里的行号>` | **一模一样** | 普通卡的号＝列表行号，两版相同（本体卡仍占第 1、2 号） |
> | 诅咒卡（`Curses` 第 k 行） | `<角色>Card<Cards.Count + k>`（接着普通卡往下数） | `<角色>Curse<k>` | 前缀换成 `Curse`、号从 1 起 |
> | 先古卡（`AncientCards` 第 k 行） | `<角色>Card<Cards.Count + Curses.Count + k>` | `<角色>Ancient<k>` | 前缀换成 `Ancient`、号从 1 起 |
> | 手填的类名（认不出是自动名的） | 一个字不动 | **一个字不动** | 两版相同；`MyOwnSlash` / `SparkleCardMask` / `Abb2` 都不算自动名 |
> | 没填类名（留空） | 类名留空、只记 `LastAutoOrdinal`（= 连续号） | 类名留空、只记 `LastAutoOrdinal`（= **本页**号） | 生成端读 `AutoCardClassName` 自己拼；`LastAutoOrdinal` 是 `[JsonIgnore]`、不写存档 |
>
> **置换规则**（`RenumberCardClassNames`）：`<角色>Card<x>`（任意 x）→ 按**该卡现在所属那一页**重算 = 普通卡 `<角色>Card<行号>`、
> 诅咒卡 `<角色>Curse<行号>`、先古卡 `<角色>Ancient<行号>`。所以老存档里 `SparkleCard45`（其实是诅咒）会变成 `SparkleCurse2` 之类；
> 同一批 `renamed`（旧→新）会**同一次**改掉按类名存的引用与上传的卡面键（`RepairCardClassReferences`）。
> **幂等**：跑第二遍 `renamed` 为空、一个字节都不动（自检里连跑三遍比签名）。

> **红线实测数字（本批，Debug 与发布版各一遍）**
> - ① **默认档逐字节不变**：`--samplegen` **112 个文件 111 个逐文件 SHA256 一致**（唯一差异 = `ExampleMod.sln` 的随机 GUID；两边文件集合也完全相同：113 个）。
>   两份**真实存档**对拍：`DarkVinci.json`（源 = 用户目录里的 `DarkVinci.json.bak`，任务书写的 `DarkVinci.json` 实际不存在）
>   与 `SparkleMod.json`（**先复制到 `_tmp\v02137\` 再当输入**，绝不动 `D:\ds\s\4\自定义角色存档\`）：
>   `DarkVinci` **125 个文件 → 行尾归一化后 124 个逐字节一致，唯一差异是 `DarkVinci.sln` 的随机 GUID**；
>   `SparkleMod` **304 个文件 → 303 个逐字节一致，唯一差异是 `SparkleMod.sln`**。
>   **⚠ 这两份存档里一张自动编号的卡都没有**（生成日志里 `卡牌英文类名已按当前序号重新编号…` 那一行只出现在
>   SparkleMod、且 `renamed` 表为空 —— 用户一直手填类名），所以**没有文件被改名**，不需要套映射。
>   真正会改名的那一类给在红线 ④ 的探针里（见那张映射表）。
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**（365 个警告，比改动前的 361 个多 4 个，
>   全部是**自检代码里**新加的 `FrameworkElement?` 可空流分析警告，不是生成端）。
> - ③ `--uicheck`：Debug **3223 PASS / 0 FAIL**（基线 3200/0，**净 +23 条**：第 ①~④ 条新增 21 条 +
>   红线⑧ 根因修复那 2 条；同时删掉旧的弹窗自检 15 条等）；发布版 **3223 PASS / 0 FAIL**（`dotnet publish src\Sts2CharForge.App -c Release -r win-x64
>   --self-contained true -o D:\ds\s\_tmp\v02137_pub` → `Unblock-File` → 先删旧 `uicheck_result.txt` →
>   `[System.Diagnostics.Process]::Start(...)`（`UseShellExecute=$false`、`TEMP`/`TMP`=`D:\ds\s\_tmp\apppub_tmp`、
>   `DOTNET_DISABLE_GUI_ERRORS=1`）+ `WaitForExit()`；**没有用 `Start-Process`**）。
> - ④ **探针**：`_tmp\v02137\probe137.json`（SparkleMod 存档 + 手填 / 老自动名 / 留空三类各若干 +
>   诅咒 2 + 先古 2 + 跨类引用：`GenerateCard.SpawnCardId`、`TransformCard.AncientRows`、额外资源量效果的引用）：
>   老版（`git worktree` @ `daf8a95`）与新版各生成一遍（331 个文件）→
>   **旧→新映射表**：`ProbeCard49 → ProbeCurse1`、`ProbeCard50 → ProbeCurse2`、`ProbeCard52 → ProbeAncient2`、
>   `ProbeCard53 → ProbeAncient3`、`ProbeCard37 → ProbeCard45`（普通卡跟着位置重排）；手填的 `ProbeHandSlash` /
>   `ProbeRefGen` / `ProbeTrans` / 老存档里那些 `SparkleCard*` **一个字没动**；没有新老对应关系的**多出 0 / 缺失 0**。
>   **把「旧→新」映射套回 base 侧（文件名 + 正文 + 本地化键三种大小写写法）再与 new 侧逐字节比 →
>   331 / 331 全部一致、0 处不一致**（`.sln` 的随机 GUID 掩码、行尾归一化）。
>   **`--build`（只用 `--build`）**：真 `dotnet build` **0 错误**、Godot 导入 / 导出 PCK **DONE**、构建成功。
>   **`--recover`：0 条「没认出来」**，48 张卡 / 2 诅咒 / 3 先古逐项还原成
>   `ProbeCard45 / ProbeCard46 / ProbeCurse1 / ProbeCurse2 / ProbeAncient2 / ProbeAncient3`，
>   `GenerateCard.SpawnCardId` 也还原成 `ProbeCard45`（跨类引用跟着新类名）。
>   **幂等证据**：① 同一份存档「纯生成」与「`--build` 之后」的同一批 330 个文件**逐字节一致**；
>   ② 把 `--recover` 出来的存档连跑两次（`recA` / `recB`，只换存档名与输出目录）→ **319 个文件全部逐字节一致、0 处差异**。
>   ⚠ **本轮踩到一次红线⑧（已修正根因 + 已当场删掉误装的文件）**：探针存档的 `Paths.InstallDir` 里带着真实
>   游戏 mods 目录，而 CLI 里写的是 `ModBuilder.Build(..., installDir ?? profile.Paths.InstallDir, ...)` ——
>   **不给 `--install` 时会退回用存档里的 InstallDir**，于是 `Probe137.dll/pck/json` 真的被装进了
>   `d:\download\steam\…\Slay the Spire 2\mods`。**当场删掉那三个文件**（用户自己的 `SparkleMod.*` / `lv.*`
>   一个字节没动，复核过目录里只剩它们）。
>
>   **根因修复（本批的一部分）**：CLI 改成**显式才装** —— `ModBuilder.Build(result.ProjectRoot, …,
>   **installDir**, …)`（去掉 `?? profile.Paths.InstallDir` 兜底）；不给 `--install` 时连
>   `profile.Paths.InstallDir` 都不写回（那个字段**只给图形界面用**）；`ModBuilder.Build` 的文档注释里
>   写明**调用方不许在这里替用户兜底填目录**。
>   **有没有人依赖旧行为**：逐处查过 —— GUI 的 `RunBuild` 传自己的 `installTo = (install ? text : null)`
>   （**不走**这条兜底，行为不变）；`tools\*.ps1` 没有一处调 `forge --build/--install`；
>   `--buildtest` 传的是临时目录（`forge_buildtest\out\install`），不受影响；
>   文档里那些历史探针记录（V0.2.127 / V0.2.132 / V0.2.135 等写的 `--build --install`）是**显式给了
>   `--install`** 的真安装、不受影响，旧记录也不改写历史。
>   **用法说明**：`Program.cs` 顶部注释 / `--help` / `README.md`（新增「命令行」小节）/
>   `tools\使用说明.template.txt` 四处都写明**要安装必须显式给 `--install <mods 目录>`**。
>   **自检**：① CLI 新增 `forge --buildcheck`（可加 `--godot <exe>`）—— 造一份 `Paths.InstallDir` 指向
>   **陷阱目录**的存档，构建前后各数一遍：不给 `--install` → 陷阱目录 **0 → 0 个文件**、`InstalledTo=null`；
>   给了 `--install`（指向另一个临时目录）→ 真装进去 3 个文件。② `--uicheck` 加 2 条静态护栏
>   （**去掉注释行后**源码里不许出现那串兜底表达式 + `installDir` 必须直接透传；GUI 侧 `install ? text : null` 那道闸）。
>   **实测**：`forge --buildcheck` → `[OK] … 通过（① 没给就不装；② 给了就装）`（exit 0）；
>   端到端 `--build`（**不给 `--install`**，存档里写着陷阱目录 + 真 Godot）→ **0 错误**、
>   `[OK] 构建完成：…\build`，陷阱目录 **0 → 0**、用户游戏 `mods` 目录**逐文件时间戳一个都没变**；
>   同一存档 `--build --install <陷阱目录>` → `[已安装] Probe137.dll/.pck/.json`、陷阱目录 3 个文件、
>   游戏 `mods` 目录仍然没变。`--uicheck` 因此从 3221 变 **3223 PASS / 0 FAIL**（净新增这 2 条）。
> - ⑤ **五处配套**：① 界面 + 自检；② 模型 + 生成（编号口径）+ 校验器 + 界面 + 文档；③ 界面（撤）+ 校验器（留）+ 文档；
>   ④ 模型（`IsExpanded`）+ 界面模板 + 自检。文档：`README.md`、`tools\使用说明.template.txt`（**CRLF、无 BOM**）、本文件。
> - **行尾约定**：本轮改过的 8 个既有源文件逐个统计过 —— `MainWindow.xaml` / `MainWindow.xaml.cs` / `App.xaml` /
>   `Naming.cs` / `NameStabilizer.cs` / `ProfileValidator.cs` / `Profile.cs` / `HexToBrushConverter.cs`
>   **全 CRLF、裸 LF = 0、无 BOM**（`HexToBrushConverter.cs` 原本在工作区是 LF，本轮统一成 CRLF 以和同目录其它文件一致）；
>   `README.md` 与 `docs\QUEUE.md` 本来就是 **LF**、`tools\使用说明.template.txt` 是 **CRLF + 无 BOM**（复核过字节）。
>   `git diff --numstat` 无「整文件重写」的行尾噪声。两个源文件删除（`HighlightPickerDialog.xaml(.cs)`）用
>   `git rm` 时索引被暂存过，已用 `git reset HEAD <那两条路径>` 把索引退回 HEAD（**没有提交、没有别的 git 写操作**）。

## 第 25 批需求（V0.2.138：**修「额外资源量遗物·在游戏中隐藏」导致游戏卡死**，用户真游戏日志定位）

> 派活 HEAD = `d657ba9 发版 V0.2.137`（工作区干净、无游离 worktree）；`--uicheck` 基线 = **3223 PASS / 0 FAIL**（Debug 与发布版都是）。
> 任务书：`_tmp\待发任务书_V0.2.138_修隐藏遗物卡死.md`。**本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**。
> 红线⑧：**不读 / 不复制 / 不使用用户存档**（`D:\ds\s\4\自定义角色存档\`）、**不写也不盘点用户游戏安装目录**、
> 探针**只用 `--build`**（不给 `--install`）。禁区（动画四个 `.cs`、`MainWindow.xaml` 动画页 + `AnimationSpec`、刮削 `Scrape`）一个字没碰。

> **用户真游戏日志（根因，别重新论证）**
> ```
> System.ArgumentOutOfRangeException: Index must be within the bounds of the List. (Parameter 'index')
>    at System.Collections.Generic.List`1.Insert(Int32 index, T item)
>    at ...NRelicInventory.Add_Patch2(NRelicInventory this, RelicModel relic, Boolean startsShown, Int32 index)
>    at ...NRelicInventory.OnRelicObtained(RelicModel relic)
>    at ...Player.AddRelicInternal_Patch1(Player this, RelicModel relic, Int32 index, Boolean silent)
>    at ...RelicCmd.Obtain(...) → 先古事件的「选遗物」
> ```
> **根因**：`CSharpCodeGen.HideCarrierRelicSource()`（V0.2.113 起）给 `NRelicInventory.Add` 打的是**前缀补丁**，
> 遇到载体遗物就 `return false` **跳过原方法** → 遗物栏的节点列表 `_relicNodes` 比 `Player.Relics` **少一项** →
> 之后再获得任何遗物时，本体 `OnRelicObtained` 用 `Player.Relics.IndexOf(relic)` 算出来的 `index`
> 插进这个短列表就**越界**（`List.Insert` 抛异常）→ 异步任务死掉 = 玩家眼里**卡死**。
> 从 V0.2.113 潜伏到 V0.2.137（老探针只验「生成 + 编译」，验不出运行期越界）。

### 查证结论（决定走哪条路，附证据）

| 问题 | 结论 | 证据（`_tmp\v02138_mlc` 的 MetadataLoadContext 探针 + `_tmp\v02138_il` 的 IL 反汇编，读的都是 `_tmp` 里的 `sts2.dll` 副本） |
|---|---|---|
| `RelicModel` 上有没有「别在遗物栏显示我」的**可覆写虚属性**？ | **没有** | `RelicModel` 全部 `virtual`/`abstract` 成员里跟显示有关的只有 `Rarity` / `IconBaseName` / `PackedIconPath` / `BigIconPath` / `ShowCounter` / `ShouldFlashOnPlayer` / `IsAllowed*` / `IsStackable` 等；**没有** `ShouldShow*` / `Hidden` / `Visible` 这一类。`grep ShouldShow` 命中的全是 `MonsterModel.ShouldShowInCompendium` / `CardModel.ShouldShowInCardLibrary` / 地图 / 光标这些**别的**东西 |
| `NRelicInventory` 上有没有 `IsHidden` / `_isHidden` / `startsShown` 这种隐藏入口？ | **没有** | `NRelicInventory` 的字段只有 `_player` / `_relicNodes` / `_originalPos` / `_curTween` / `_debugHideTween` / `_isDebugHidden`（那个是开发者快捷键藏顶栏用的）。`IsHidden` / `_isHidden` 全库唯一命中是 `CrystalSphereEvent.CrystalSphereCell`（和遗物栏无关） |
| 那 `Add` 到底干了什么？ | **必须原样跑** | `NRelicInventory.Add(RelicModel relic, bool startsShown, int index)`（private，void）：`Create(relic)` → `set_Inventory` → `index < 0 ? _relicNodes.Add(holder) : _relicNodes.Insert(index, holder)` → `AddChildSafely` + `MoveChildSafely` → 按 `startsShown` 决定要不要把 icon 的 `Modulate.a` 先置 0 → `UpdateNavigation()` → 接 3 个信号 → 发 `RelicsChanged`。**列表长度/顺序就是在这里和模型对齐的** |
| 两条入口都走 `Add` 吗？ | **都走** | `Initialize(runState)`：`foreach (relic in _player.Relics) Add(relic, true, -1)`；`OnRelicObtained(relic)`：`Add(relic, false, _player.Relics.IndexOf(relic))`。`Remove(relic)` 也只有 `OnRelicRemoved` 一个调用方 → **只要 `Add` 不被跳过，`_relicNodes` 与 `Player.Relics` 永远同长同序** |
| 隐藏会不会打断「新获得动画」状态机？ | **不会** | `Add` 之后本体自己会调 `AnimateRelic(relic, null, null)`（`LocalContext.IsMine` → `_relicNodes.First(按 Relic.Model 找)` → `NodeUtil.RunSafely(holder.PlayNewlyAcquiredAnimation(...))`）。那个动画只 tween **icon 的 `Modulate.a` / `Position` / `Scale`**，**不碰 `Visible`**，而且 `First(...)` 是按 `Relic.Model` 引用找节点 —— 节点还在列表里就一定能找到 |
| 隐藏节点会不会踩到别的 UI 逻辑？ | **不会** | 遗物栏唯一的「索引敏感」代码是 `UpdateNavigation()`（按 `RelicNodes[i]` 设 focus 邻居）和 `_Ready` 的 `TryGrabFocus(RelicNodes[0])`；`NodeUtil.TryGrabFocus` 自己 `IsVisibleInTree()` 判过可见性（不可见就走 `CallDeferred`，不 `GrabFocus`）。「遗物收藏册」画面走的是另一套 `NRelicCollectionCategory.LoadRelicNodes`，和 `_relicNodes` 无关 |

**→ 选定的路：后缀（postfix）+ 纯视觉隐藏**（不覆写虚属性 —— 本体没有；也不判「不安全」—— 查证下来是安全的）。
`NRelicInventory.Add` 的原方法**一个字都不改地照跑**，跑完才把刚建出来的那个节点 `Visible = false`。
**「新获得动画」不单独挡**：见下面第 ③ 条（第一版多挂过一个 `AnimateRelic` 后缀，是空操作 + 注释与事实相反，**已删**）。
**兜底没用到**（原任务书第 5 条那条「不生成补丁 + 界面警告 + 校验器报错」的分支**不适用**，所以界面文案没说「不支持」）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **绝不再跳过 `NRelicInventory.Add` 的原方法** | `HideCarrierRelicSource()` 重写成 `[HarmonyPatch(typeof(NRelicInventory), "Add")]` + `[HarmonyPostfix] Postfix(NRelicInventory __instance, RelicModel __0)`：`__0` 只当**只读判据**，`index` / `startsShown` 一律不碰。反查节点用本体的同一口径（`nodes[i].Relic.Model` 引用相等，和 `Remove` / `AnimateRelic` 的 `First(...)` 一致），从后往前找、找到就 `Visible = false; return;` | ✅待发 |
| ② | **隐藏只做视觉**：遗物照旧挂在角色身上 | 生成的补丁里**只有**一句 `nodes[i].Visible = false;`；不 `Remove`、不动 `_relicNodes`、不动 Godot 树、不动模型。`Player.Relics` 一个字节不变 → 发放 / 跨战斗继承 / 存档兼容全不变 | ✅待发 |
| ③ | **新获得动画不要为它播放** | **不需要额外补丁，已把第一版多挂的那条删掉。** 理由：`AnimateRelic` 上的**后缀**里 `return` 只是从它**自己**的 Postfix 返回（原方法早跑完了），**拦不住动画、也不改任何状态 = 空操作**；要真拦住只能用前缀 skip，而那是**多余**的 —— 那个动画只 tween 节点里 **icon 的 `Modulate.a` / `Position` / `Scale`**（IL 逐条核过 `PlayNewlyAcquiredAnimation` 的状态机），**不碰 `Visible`**；父节点已经 `Visible = false` ⇒ 整棵子树都不渲染，动画自己在不可见节点上跑完、**没有任何可见效果**。所以最终 `cs/HideCarrierRelic.cs` 里**只有 `Add` 那一个补丁类**，自检新增一条护栏钉死「补丁类数 == 1 且源码里不出现 `AnimateRelic`」 | ✅待发 |
| ④ | **校验器**：勾了隐藏要能挡住「生成了也编译不过」的配置 | 新增 `ProfileValidator.ValidateHideCarrierRelic`：勾了隐藏但**没启用额外资源量** → **报错**（补丁引用的 `<角色>ExtraResourceRelic` 类不存在 → CS0246），并说清怎么修。**不**加噪音提示（隐藏的边界写在界面文案里） | ✅待发 |
| ⑤ | **界面文案与实际一致** | `Profile.HideInGameHint` + `MainWindow.xaml` 那个勾选框的 `ToolTip` + `README.md` + `tools\使用说明.template.txt` 全部改成「**后缀补丁 / 纯视觉隐藏 / 列表长度与模型一致 / 只有一个补丁类**」，并把 V0.2.113~V0.2.137 那条**前缀补丁 = 卡死**的来龙去脉写明（不再出现「跳过 Add」的误导说法） | ✅待发 |
| ⑥ | **自检钉住新补丁源码形状** | `--uicheck` 里那条旧断言（要求含 `[HarmonyLib.HarmonyPrefix]` + `return relic is not …`）换成：必须含 `[HarmonyPostfix]`；**不许**出现 `[HarmonyPrefix]` / `return false;` / `return relic is not`；必须含 `nodes[i].Visible = false;` + `ReferenceEquals(relicNode.Model, __0)` + `RelicNodes`；只认那一只载体遗物；**补丁类数 == 1 且不出现 `AnimateRelic`**（防止第二个空操作补丁被写回来）；注释必须说明「动画不用管」的依据；文案钉「遗物照旧挂在角色身上 / 存档兼容」；校验器那条错误必须报出来 | ✅待发 |

### 红线实测（本轮）

> - ① **没勾隐藏的默认档逐字节不变**：`--samplegen` 改动前后逐文件 SHA256 对比 —— **112 个文件 / 112 行 SHA256，111 个逐文件 SHA256 一致**，唯一差异 = `ExampleMod.sln` 的随机 GUID（磁盘上两边都 113 个文件、集合完全相同）。
>   另做了一次「同一份探针存档（Probe137，ExtraResource 启用）**只切 HideInGame**」的对拍：**唯一差异 = 多/少 `cs/HideCarrierRelic.cs` + `.sln` 的随机 GUID，其余 529 个文件逐字节一致**（其余多出来的 `.uid` / `.import` 是 hideON 那次跑了 Godot 导入导出产生的，不是生成端差异）。
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**（365 个警告，比 V0.2.137 的 361 个多 4 个 —— 全是既有源码的空引用告警，本轮没引入新的）。
> - ③ `--uicheck`：Debug **3229 PASS / 0 FAIL**、发布版 **3229 PASS / 0 FAIL**（基线 3223 / 0，净 +6）。
>   ⚠ 自检踩过**两次「查注释」的假红**，都改成「**先按行剥掉注释、只查可执行部分**」（`hideCode`）：
>   ① 回归钉用 `hideSrc.Contains("return false")`（**不带分号**）去查，而生成出来的注释里正大光明写着「绝不能改成前缀补丁 return false 跳过原方法」→ 把自己写的说明文字误判成违规；
>   ② 删掉第二个补丁后，若直接查 `Contains("AnimateRelic")`，会被 `Add` 补丁里那句「和本体自己的 Remove / AnimateRelic 一样按 Relic.Model 找」的注释再次判红。
>   两版复跑都 0 FAIL。
>   **主代理复核后删掉了第二个补丁**（`ForgeHideCarrierRelicAnimPatch`）：挂在 `AnimateRelic` 上的**后缀**里 `return` 只结束它自己（原方法早跑完了），**拦不住动画也不改状态 = 空操作**，注释还与事实相反。现在文件里**只有 `Add` 一个补丁类**；自检那条断言改成「**可执行部分里补丁类数 == 1 且不出现 `AnimateRelic`**」+ 一条「注释里说明动画不用管的依据（只 tween icon、不碰 Visible）」。这就是 3228 → 3229 的来源（-1 条旧断言，+2 条新断言）。
> - ④ **探针真编译**（只用 `--build`，CLI 打出「没给 --install：… **不会安装**到任何地方」、日志里没有「安装到」那一步）：`_tmp\v02138_hide\` 自造「启用额外资源量 + 勾隐藏」的存档 → 生成出 `cs/HideCarrierRelic.cs` → 真 `dotnet build` **0 错误**（27 个警告，都是既有生成代码的空引用告警）、Godot 导入 / 导出 PCK DONE、构建成功。生成出来的补丁全文贴在实现报告里。
>   顺手验了 `--recover`：从工程反推回存档认得「游戏里隐藏载体遗物」那条（输出行「…始终显示计数器、游戏里隐藏载体遗物…」）；用恢复出来的存档再生成一遍，`cs/HideCarrierRelic.cs` **SHA256 完全相同**（`6E906A63…F00127`）。
> - 行尾约定：本轮改动的既有文件逐个统计 —— `.cs` / `.xaml` / `tools\使用说明.template.txt` 全 **CRLF、裸 LF = 0、无 BOM**；`README.md` / `docs\QUEUE.md` 本来就是 **LF**。

## 第 23 批需求（V0.2.136：**升级后获得 / 失去自定义关键词**，《卡牌页优化.md》第 2 条 ④）

> 需求原文：「**升级后的关键字底下增加对自定义关键词的处理**」。
> 预研：`D:\ds\s\_tmp\升级后关键字与自定义关键词_预研.md`（含已核实机制与落点，按实际代码复核过）。
> **本轮实测确认的机制事实（别再重新论证）**：自定义关键词**不是**本体 `CardKeyword` 枚举
>（枚举封闭、模组加不了值），它只有三块 —— 卡面描述里那行 `[gold]名字[/gold]。`、生成的
> `ExtraHoverTips` 覆写、`card_keywords` 本地化键。**游戏侧没有任何按自定义关键词判定的逻辑**，
> 所以这两栏**只影响显示**；这一点**界面上必须写明**（否则用户会以为机制变了）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **「升级后的关键字」底下加「自定义关键词：不变 / 升级后获得 / 升级后失去」两栏** | **模型**：`CardSpec.UpgradeAddedCustomKeywords` / `UpgradeRemovedCustomKeywords`（`List<string>`，默认空列表；老字段一个都没动）+ `UpgradeCustomKeywordsAny` / `UpgradeCustomKeywordsDisplay`（列表右侧小结）。**生成端**：`LocalizationGen.CardsJson` 里关键词那两段走新增的 `KeywordGen.CardTextBoth`（没填那两栏 ⇒ 两段逐字节相同 ⇒ **走的还是老路径**），不一样时把「关键词行 + 正文」整段用 `IfUpgradedText(升级后, 没升级)` 包起来（⚠ 顺序固定「升级后\|没升级」；描述里自己写的字面量 `\|` 由既有的 `EscapePipe` 换全角）；`CSharpCodeGen.HoverTipsOverride` 新增 `upgradedKeywordTips` 参数，**只在真填了**时把表达式体改成条件版本 `..(base.IsUpgraded ? new IHoverTip[] { … } : Array.Empty<IHoverTip>()),`（写 `yield return` 那种会 CS1513 —— 生成端要自己数大括号、而它嵌在类里，实测踩过）。**回读**：卡牌 / 诅咒两条源码路径都写 `// CET:UpgradeKeywordAdd=` / `// CET:UpgradeKeywordRemove=`（**只在真填了才写**、空的那一栏不写；**两个标记各自一行** —— 同一行上「空值 + 下一个标记」会被贪婪匹配整段吃进值里，实测 `Add= CET:UpgradeKeywordRemove=VOID` 恢复成了 `FATE CET:UpgradeKeywordRemove=VOID`），`ProjectRecovery.ParseCard` 认这两行，并把这两个键从 `ExtraHoverTips` 扫出来的 `KeywordIds` 里摘掉（不然「升级后获得的」会被当成一直都有）。**升不了级的牌（诅咒 / 状态牌）也照写标记** —— 不写会让往返把关键词搬进普通那一栏（实测踩过）。**校验器**：那两栏引用不存在的自定义关键词 → **错误**（点名卡牌 + 哪一栏）；同一个词同时进「获得」「失去」→ 警告；填了但 `MaxUpgradeLevel = 0`（诅咒 / 状态牌）→ 提示「永远不会生效」并说清只影响显示。**界面**：`UpgradeAddedKeywordRows` / `UpgradeRemovedKeywordRows`（新增 `UpgradeCustomKeywordRow`，和既有的 `CustomKeywordRow` 同一套勾选写法）+ `UpgradeCustomKeywordHint`（没定义关键词 → 指路；升不了级 → 警示；填了 → 说清升级后会多显示 / 不显示哪些词）；两栏底下那句橙色说明写清「**只影响显示**」。**文案**：`README.md` 新增「升级后获得 / 失去自定义关键词」一节 + 「升级后的关键字」那节补一句；`tools\使用说明.template.txt` 新增一条（并把「暂不支持」那句改掉） | ✅V0.2.136 |
| ② | **卡牌「类型 / 稀有度」英文 → 中文**（《卡牌页优化.md》第 2 条 ①，用户原话「**将类型和稀有度的英文替换成中文**」） | **中文照抄游戏本体**（`localization\zhs\gameplay_ui.json` 的 `CARD_TYPE.*` / `CARD_RARITY.*`，与本体 `CardTypeExtensions.ToLocString()` / `CardRarityExtensions.ToLocString()` 一一对应）：类型 = 攻击 / 技能 / 能力 / 状态 / 诅咒 / 任务（本体 `CardType` 枚举：Attack / Skill / Power / Status / Curse / Quest），稀有度 = **基础** / **普通** / 罕见 / 稀有 / 先古 / 状态 / 诅咒（本体 `CardRarity` 枚举：Basic / Common / Uncommon / Rare / Ancient / Status / Curse；⚠ 与任务书草稿里的「初始 / 常见」**故意不同** —— 以游戏为准，任务书那套措辞其实是遗物页的）。**档位一个都没加、一个都没减**（本体 `CardRarity` 还有 `Event` / `Token`，那是本体内部档、工具从来没给过 —— 与药水页同样的取舍；`None` 是空档）。**做法照抄遗物 / 药水页**：`EffectCatalog.CardTypeOptions` / `CardRarityOptions`（新 `RarityOption(Id, Display)`：`Id` = 英文 id、`Display` = 中文）+ `CardTypeZh` / `CardRarityZh`（列表摘要用）。**界面**：三处下拉（锻造卡那一栏 `MainWindow.xaml:19/22`、中间栏 `x:Name="CardTypeCombo"/"CardRarityCombo"`、诅咒 / 先古 / 状态页 `x:Name="CurseCardTypeCombo"`）全部改成 `ItemsSource=CardTypeOptions/CardRarityOptions` + `DisplayMemberPath="Display"` + `SelectedValuePath="Id"` + `SelectedValue="{Binding CardType/Rarity}"`；**卡牌列表那一行也换中文**（`CardSpec.Display` 里改成 `CardTypeZh(CardType) / CardRarityZh(Rarity)`，和 `RelicSpec.Display` / `PotionSpec.Display` 同一套写法）。**生成端 0 行改动**：生成代码写的一直是本体枚举名 `CardType.Power` / `CardRarity.Uncommon`，读的也是 `CardSpec.CardType` / `Rarity` 这两个**英文 id 字段**（新增的中文映射只被 `CardSpec.Display` 与界面用到，`Generation\*.cs` 一处都没引用）。**校验器 / 回读零改动**：两边本来就按英文 id 判 / 认（把中文写进模型仍会报「类型非法 / 稀有度非法」，自检有断言钉住）。**文案**：`README.md` 新增「卡牌「类型 / 稀有度」显示中文」一节 + 「致死性」那个例子改写成「类型选能力、稀有度选罕见」；`tools\使用说明.template.txt`「卡牌与效果」开头新增一条 + 同上那个例子同步 | ✅V0.2.136 |

> **⚠ 第 ② 项里一处「以游戏为准」的措辞差异（请用户过一眼）**
> 任务书草稿给的中文是「**初始** / **常见** / 罕见 / 稀有 / 先古 / 状态 / 诅咒」，而**游戏本体自己的词**是
> 「**基础**（`CARD_RARITY.BASIC`）/ **普通**（`CARD_RARITY.COMMON`）/ 罕见（`CARD_RARITY.UNCOMMON`）/
> 稀有（`CARD_RARITY.RARE`）/ 先古 / 状态 / 诅咒」。同页的**遗物 / 药水**用的是「初始 / 常见 / 罕见 / 稀有」
> （那是本体 `RelicRarity` / `PotionRarity` 的档，**另一个枚举**）。本版按任务书「**以游戏为准**」的指示取了
> **游戏本体的卡牌稀有度词**（基础 / 普通），并在文档里写明「与遗物 / 药水页措辞故意不同」。
> 若用户希望三页用同一套词（常见 / 初始），改 `EffectCatalog.CardRarityOptions` 里两个 Display 即可（一行）。

> **红线实测数字（V0.2.136，①②两项合并后各跑一遍）**
> - ① **默认档逐字节不变**：`--samplegen` 112 个文件 **111 个逐文件 SHA256 一致**（唯一差异 = `ExampleMod.sln` 的随机 GUID）；
>   两份**真实存档**（`DarkVinci.json` 150568 字节 / `SparkleMod.json` 314299 字节，
>   先复制到 `_tmp\v02136\` 再当 `--profile` 输入 —— **绝不直接拿用户存档跑**（生成端会在存档旁边刷 `<存档名>.forge_classmap.json`））
>   用 `git worktree`（HEAD `b87a284`，两轮改动过的 **9 个源文件**按 t3 工作区各自的行尾风格归一化后覆盖）与本版各生成一遍：
>   `DarkVinci` **125 个文件 / 124 个逐字节一致**、`SparkleMod` **304 个文件 / 303 个逐字节一致**（各只差 `.sln` 的随机 GUID）。
>   **行尾噪声怎么排除**：`robocopy t3\src → _tmp\v02136\ref\src`（继承 t3 当前行尾约定，两版同源）
>   + 只把那 9 个改过的文件用 `cmd /c "git show HEAD:<path> > tmp"`（**字节级**，避免 PowerShell 管道按错误代码页解码）
>   取回、再按每个文件在 t3 里的行尾（`KeywordGen.cs` 与 `EffectCatalog.cs` 是 **LF**、其余 7 个是 **CRLF**）归一化后覆盖；
>   两版都在 t3 工作区里跑 ⇒ 没有 `core.autocrlf` 噪声。
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**（361 个警告，与改动前同数）。
> - ③ `--uicheck`：Debug **3200 PASS / 0 FAIL**（基线 3156/0，**新增 44 条**）、发布版
>   （`dotnet publish src\Sts2CharForge.App\Sts2CharForge.App.csproj -c Release -r win-x64 --self-contained true -o D:\ds\s\_tmp\v02136_pub`
>   → `Unblock-File` → 先删旧 `uicheck_result.txt` → `[System.Diagnostics.Process]::Start(UseShellExecute=$false,
>   TEMP=TMP=D:\ds\s\_tmp\apppub_tmp, DOTNET_DISABLE_GUI_ERRORS=1)` + `WaitForExit()`；**没有用 `Start-Process`**）
>   **3200 PASS / 0 FAIL**。
>   第 ① 项新增 28 条：描述两段分叉与 `show:` 后的顺序 / 获得与失去各自只出现在该出现的那段 /
>   ExtraHoverTips 的条件展开（含「一直在的 + 升级后获得的」都在条件里）/ 没填那两栏的卡**老写法一字不改** /
>   两个标记各自一行 / 诅咒也写标记 / 回读两栏逐项还原 + `KeywordIds` 没被带跑 / 「生成→恢复→再生成」逐字节 /
>   老形状（只勾普通关键词）没有 `{IfUpgraded}`、没有标记、ExtraHoverTips 还是老写法 /
>   校验器「不存在 → 错误」「诅咒升不了级 → 提示（且写明只影响显示）」/ 界面三属性 + 「只影响显示」文案在页面上。
>   第 ② 项新增 16 条：两张选项表的 Id（**必须与老英文表逐项相同**）与 Display（必须等于游戏本体的词）/
>   `CardTypeZh` / `CardRarityZh` 映射与「认不出来原样返回」/ 三处下拉的 `DisplayMemberPath=Display` +
>   `SelectedValuePath=Id` + `SelectedValue` 绑 `CardType`/`Rarity` / 下拉里显示的全是中文 /
>   **点「攻击」/「先古」之后模型里写的还是 `Attack` / `Ancient`** / 模型写英文 id 时下拉认得出来 /
>   卡牌列表 `Display` 是中文、没有英文 id / 生成端仍写 `CardType.Power` / `CardRarity.Uncommon` /
>   校验器把「模型里写中文档位」报成错误（口径没变）/ 遗物与药水页那两张表没被改动。**外加 1 条老断言改写**
>   （「选中本体卡时类型 / 稀有度下拉变灰」原本读 `SelectedItem`，绑定换成 `SelectedValue` 后跟着改）。
> - ④ **探针**（第 ① 项那轮跑的，仍在 `_tmp\v02136\`）`probe136.json`（示例存档 + 3 条自定义关键词 + 5 张牌覆盖四种组合：
>   获得+失去+一直在 / 只获得 / 只失去 / 中文名当引用 / 诅咒）：
>   CLI `--profile … --out … **--build**`（**只用 `--build`、不用 `--install`**）→ 真 `dotnet build`
>   **0 错误**（8 个既有可空警告）、Godot 导入 / 导出 PCK **DONE**；`--recover` → **0 条「没认出来」**、
>   两栏逐项还原（`+[FATE] -[VOID]` / `+[FATE]` / `+[FATE,ECHO]` / `-[]` / 诅咒 `+[ECHO]`），
>   中文名引用被规范化成键（`命定 → FATE`，与「界面勾选 → 存档 → 生成 → 回读」同一套）。
>   **幂等**：恢复出的存档再生成 → **109 个文件只有 `.sln` 不同**；再回读一遍两栏不变，再生成 **119/120 逐字节一致**（同样只差 `.sln`）。
>   ⚠ 探针只写 `_tmp\v02136\probe_mods` 与产物目录，**没碰**用户的游戏 mods 目录（实测那个目录里今天没有新文件）。
>   **第 ② 项没有单独跑探针**：它一行生成端代码都没动（见 ⑤），上面「两份真实存档 + `--samplegen` 逐字节对拍」已经覆盖；
>   新增那 16 条自检里有一条直接断言「生成端仍写 `CardType.Power` / `CardRarity.Uncommon`」。
> - ⑤ **五处配套**：第 ① 项 = 模型 / 生成（描述 + `ExtraHoverTips`）/ 回读 / 校验器 / 界面 + 文档；
>   **第 ② 项 = 界面 + 文案（生成端 / 回读 / 校验器零改动 —— 它们本来就按英文 id 判）**。
>   文档：`README.md`、`tools\使用说明.template.txt`、本文件。
> - **行尾约定（本轮改过的 9 个源文件）**：逐个统计过 —— `KeywordGen.cs` 与 `EffectCatalog.cs` 本来就是 **LF**
>   （`git cat-file` 核过 HEAD 也是 LF），其余 7 个（`MainWindow.xaml` / `MainWindow.xaml.cs` / `CSharpCodeGen.cs` /
>   `LocalizationGen.cs` / `ProfileValidator.cs` / `ProjectRecovery.cs` / `Profile.cs`）**全 CRLF、裸 LF = 0、无 BOM**；
>   `README.md` 与 `docs\QUEUE.md` 本来就是 **LF**、`tools\使用说明.template.txt` 是 **CRLF + 无 BOM**（复核过字节）。

> **已知边界 / 留给用户定**
> - 「升级后获得 / 失去」**只影响显示**（卡面那行 + 悬停说明）。这是本体机制决定的（封闭枚举），
>   不是本版的取舍 —— 界面上已经写明。
> - 同一个词**同时**填进「获得」和「失去」时两栏互相抵消（升级前后都不显示它）：校验器给警告；
>   回读靠标记（那两栏都在标记里），所以往返不会把它搬到「一直都有」那一栏，但**用户原意（它本来一直有）回不来** ——
>   这种自相矛盾的配置本来就是错的，想要「一直都有」请只用普通那一栏勾。
> - 回读仍然**不能**从工程反推出「这张牌原来还有哪些词是一直都有的」之外的信息（老口径靠 `ExtraHoverTips`
>   里那句 `LocString("card_keywords", …)` 反查）——本轮只是把「升级后获得 / 失去」那部分**摘干净**了。

## 第 22 批需求（未发版：**升级后自定义描述的逐字段高亮**，用户原话「把能造成高亮的做成按钮放在升级后自定义描述底下，用户可以选择具体字段高亮」）

> **只做了第 1 档（零风险的界面 + 文本改写）；第 2 档（高亮选择进存档）刻意没做**，见文末「没做 / 待用户定夺」。
> 设计依据：`_tmp\升级描述高亮控制_方案.md`（只读调研产物）。机制 = 本体现成的
> `{X:diff()}`（`HighlightDifferencesFormatter`，键名 `diff` 在真 `sts2.dll` 字符串里坐实；
> 真本体 `SlayTheSpire2.pck` 里有真实卡面写法 `Füge {Damage:diff()} Schaden zu.`）。
> 落点 = 卡牌页「升级后的自定义描述」输入框**底下**的新按钮「高亮字段…」，照抄旁边「填入参数…」那套
> （`Tag` 绑那个 `TextBox`）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **「升级后的自定义描述」底下加「高亮字段…」按钮，点开多选要「高亮」的字段** | **界面**：`MainWindow.xaml` 在「升级后的自定义描述」那一栏的按钮行下面加一行（`Button x:Name="PickUpgradedHighlightsButton"` + 一行实时汇总 `CardUpgradedHighlightSummary`，绑窗口级属性，`RefreshCardHighlightSummary()` 在 `SyncDetail` 与 `TextChanged` 两处刷新）。**弹窗**：新文件 `HighlightPickerDialog.xaml(.cs)`（照 `StartupProfileDialog` 那套独立 `Window`）：框顶一句话说明 + 可滚动的 `CheckBox` 列表 + 底部「确定 / 全部不勾（保持原样）/ 取消」+ 实时汇总。**候选** = `CSharpCodeGen.ParamHints(card.Effects, isCard:true)`（这张牌**每一条效果各自声明过**的动态变量，第二列是那条效果的中文名）——**粒度不是「数值 / 关键词 / 文字」那种粗分类**（粗分类映射不到任何可写语法）。**初值从正文反推**（正文里已经是 `{X:diff()}` 的预先勾上，绝不「没勾就把用户写好的高亮去掉」）；正文里**没用到**的字段那一行是灰的（勾它不会加字）。 | ✅ |
| ② | **确认时只做精确文本替换**：`{X}` ↔ `{X:diff()}` | 新文件 `Core\Generation\HighlightDiffText.cs`：一个正则 `\{(?<key>[A-Za-z_][A-Za-z0-9_]*)(?<diff>:diff\(\))?\}` 负责扫描/改写（`Scan` / `Apply` / `HighlightedKeys` / `UsedKeys` / `KeyOfToken` / `StripDiff`）。**不主动加字、不新增占位符**；已是目标写法的一个字符都不动 → **幂等**（连点两次结果一样）；只认「变量名字符」的键，`{IfUpgraded:show:A\|B}` 这类本体格式化器原样留着。**生成端 0 行改动**：`{X:diff()}` 就是用户正文的一部分，`CET:UpgradedCustomDescription=` 原样带回 → 回读天然幂等，**不加任何新 `CET:` 标记**、不动 `LocalizationGen.CardsJson`。 | ✅ |
| ③ | **这段里一个 `{}` 占位符都没有时必须明确提示**（用户现场：纯中文 + 直接写死的数字） | 弹窗里显示一条黄底提示条，原文：「这段里没有可高亮的字段：一个 {} 占位符都没有（你现在写的是直接的数字 / 中文）。请先点上面的「填入参数…」把数字换成占位符（例：造成 {Damage:diff()} 点伤害），再回来勾。下面这些字段这段文字里都没用到 —— 勾它们不会自动加字。」；按钮那行的实时汇总与 `SetStatus` 同时给同一句指路（不用点开就知道）。 | ✅ |
| ④ | **校验器：填了 `:diff()` 但那个变量在这份配置里不存在（效果删了）→ 报出来并说清是哪张牌** | `ProfileValidator.ValidateRichText` 里新增 `ValidateHighlightVars`：① 正文里 `{X:diff()}` 的 X 不在「这张牌真的声明过的变量」里 → **错误**（点名卡牌 + 说清「本体取不到值会把 `{X:diff()}` 原样印在卡面上」）；普通 `{X}` 写错 → **警告**（可能是用户手写的本体自己的键，口径放宽一档）。判定集合用新增的 `CSharpCodeGen.DeclaredVarKeysOf`（**含重名别名 `Cards2`**；`ParamHints` 每种变量只列一条、不含别名，拿它比对会把合法的 `{Cards2:diff()}` 误报）；② 「升级后的自定义描述」里写了 `:diff()` 却整段一个 `{}` 都没有 → 警告。 | ✅ |
| ⑤ | **文档** | `README.md`（描述富文本那节 + 「卡牌：自动填入 / 升级后的自定义描述」那节）、`tools\使用说明.template.txt`（「遗物 / 药水想自己写描述」后面新增一条 + BBCode 那节一句话）。 | ✅ |

> **红线实测数字（第 22 批）**
> - ① **默认档逐字节不变**：`--samplegen` 112 个文件**只有 `ExampleMod.sln` 的随机 GUID 不同**（其余 111 个逐文件 SHA256 一致，**0 个只差行尾** —— 因为两版都在 t3 工作区里跑，没有 `core.autocrlf` 噪声）；
>   两份真实存档对拍（**同一版代码**的改动前产物当基线，`--out` 生成到 `_tmp\v02135_base_*` / `_tmp\v02135_after_*`）：
>   `DarkVinci.json` **124 个文件只有 `.sln` 不同**、`SparkleMod.json` **303 个文件只有 `.sln` 不同**（内容 0 处差异）。
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**（361 个警告，与改动前逐条同数）。
> - ③ `--uicheck`：Debug **3156 PASS / 0 FAIL**（基线 3134/0，**新增 22 条**）、发布版
>   （`dotnet publish src\Sts2CharForge.App -c Release -r win-x64 --self-contained true -o D:\ds\s\_tmp\v02135_pub`
>   → `Unblock-File` → 先删旧 `uicheck_result.txt` → `[System.Diagnostics.Process]::Start(UseShellExecute=$false,
>   TEMP=TMP=D:\ds\s\_tmp\apppub_tmp, DOTNET_DISABLE_GUI_ERRORS=1)` + `WaitForExit()`；**没有用 `Start-Process`**）
>   **3156 PASS / 0 FAIL**。
> - ④ **探针**（`_tmp\v02135_probe.json` = 真实存档 `DarkVinci.json` + `DarkVinciSpark` 的自定义描述写成
>   `对指定敌人造成 {Damage:diff()} 点伤害。`）：CLI `--build --install` → 真 `dotnet build` **0 错误**、
>   Godot 导入 / 导出 PCK / 安装全 OK；`--recover` → 报 **1 条「没认出来」**
>   （`DarkVinciDyeing: 升级增量找不到对应效果（变量 Cards2）`，**这是源存档 `DarkVinci.json` 既有的回读缺口**：
>   用 HEAD 的 `git worktree` 对同一批产物跑基线 → **同一条、一字不差**，与本版无关；`{Damage:diff()}` 的往返 **0 条**）；
>   恢复出的 `UpgradedCustomDescription` **原样带回** `对指定敌人造成 {Damage:diff()} 点伤害，并施加 3 层间色：紫。`；
>   生成产物 `cards.json` 里 `{Damage:diff()}` / `{Cards2:diff()}` / `{Mix:diff()}` 全部原样保留。**幂等证据**：
>   把恢复出的存档（顺手清掉那个既有缺口的空组引用）**分别用 HEAD 的 `git worktree` 与本版**各生成一遍到同一路径，
>   123 个文件里 **122 个逐字节一致**，唯一差异是 `DarkVinci.sln` 的随机 GUID（行尾差异归一化后统计，见下）。
>   ⚠ 探针的 `--install` 目录被 CLI 判成「不是游戏的 mods 目录」并**自动改成了真实游戏 mods 目录**，
>   所以 `DarkVinci.dll/pck/json` 被写进了 `D:\download\steam\...\Slay the Spire 2\mods`（探针内容 = 真实存档 + 一处描述改写）——
>   发版前用户重启游戏前请确认那份安装要不要还原（`--uicheck` / `--samplegen` **不碰**那个目录）。
> - ⑤ **生成端确实 0 行改动**（`LocalizationGen` / `CSharpCodeGen` 的写出路径一字未动；`CSharpCodeGen` 只新增了一个**只被校验器调用**的 `DeclaredVarKeysOf`）→ 所以「默认档逐字节不变」不是靠开关保证的，而是**这条路径根本没被碰过**。
> - **行尾约定**：本轮改过的 4 个既有文件（`MainWindow.xaml` / `MainWindow.xaml.cs` / `ProfileValidator.cs` / `CSharpCodeGen.cs`）逐个统计过 —— **全是 CRLF、裸 LF = 0、无 BOM**；新增 3 个文件也归一化成 **CRLF、无 BOM**；`README.md` 与 `docs\QUEUE.md` 本来就是 **LF**、`tools\使用说明.template.txt` 是 **CRLF + 无 BOM**（复核过字节）。

> **没做 / 待用户定夺（第 2 档）**
> - **把「高亮选择」存进存档**（`CardSpec.UpgradedHighlightVars` + `[JsonIgnore]` 或新 `CET:` 标记）：本版**刻意没做** —— 界面勾选状态可以从正文里的 `\{(\w+):diff\(\)\}` **反推**，加字段/标记只会带来「字段与正文不一致」的二义性，且与本版「零存档面改动」的取舍冲突。要不要做请用户定夺。
> - `:inverseDiff()` 语义探针、`:diff()` 有没有参数 —— **未实测**（方案第 5 节 R3/R4），本版界面**一个都不写**。
> - 「一键把升级后那段里的数字换成占位符」这类批量改写 —— 方案里的第 2 档，**没做**。
> - 用 Harmony 补丁去关本体的整段高亮 —— 方案判定「大且高风险」，**明确不做**。

## 第 21 批需求（V0.2.134：**锻造节点图能从工程恢复**，用户 2026-10-10 拍板「要」）

> 拍板依据：`docs\需求\已拍板决定.md` **第五节第 ② 条**（用户选「要做这一版」）。
> 背景：`ProjectRecovery` 从 V0.2.103 起只回读「效果列表 + 卡牌/遗物/药水的时机与描述」，
> **从来不认锻造节点图**；工程里只有编译结果，节点图只存在存档里。用户真实存档已受影响
> （`DarkVinci.json` 6 张、`SparkleMod.json` 2 张锻造卡，`--recover` 后 `IsForged` 全 false、图全空）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **锻造节点图能从工程恢复**（`IsForged=true` + `ForgeGraph` 非空 + 逐字段还原） | **生成端**：`ForgeGraphGen` 新增 `MarkerView=1` / `MarkerPrefix` / `MarkerOf` / `ApplyGraphMarker`，由 `TimingFields`（三个调用点 = 卡牌 / 遗物 / 药水，且本来就在「图非空」的 if 里、每个执行点各调一次）统一追加一行 **`// CET:ForgeGraph=1:<图的紧凑 JSON 的 Base64>`**（写在类级、紧贴「===== 锻造节点图」那一行前面）；`TimingFields` 原体抽成 `TimingFieldsCore`，**老存档图空时一个字符都不多**。选 Base64 而不是裸 JSON：图上任何一栏都可能带引号 / 反斜杠 / 换行 / 中文，Base64 之后保证单行、无歧义、`Convert.FromBase64String` 直接读回。**回读端**：`ProjectRecovery` 在 `ParseCard` / 遗物 / 药水三处调 `ForgeGraphGen.AttachMarker` → `ReadMarker`（支持 `skip` 读第二行 = 升级后那块布）→ `ForgeGraphSpec` 整个装回来 + `IsForged=true`。**老工程（V0.2.133 及以前、没有这行）照旧**：`ReadMarker` 返回 null、**不报错、不产生「没认出来」**，图仍空、`IsForged` 仍 false。**不许静默**：版本不认识 / Base64·JSON 坏了 → 进 `RecoveryResult.Unparsed` 说清是哪个条目的哪块布；`ModGenerator` 生成后自检把每个带标记的文件当场解开验一遍，解不开直接**错误级中止生成**。**界面**：`OnRecoverProfileFromProject` 日志单独一行 + 状态栏 / 确认框带上「含锻造图 N 张」。**端口版本（这一版的关键修复）**：恢复出来的存档 `ForgeGraphPortVersion` 以前是 0 → 下次一 Load 就被 `MigrateIfPorts` 当老存档**再迁一次**（实测 6 张图各丢一条线、换一条线）→ 现在认到标记就抬到 `ForgeGraphGen.PortVersion`，`CloneForTemplate` 也照抄模板那份 | ✅V0.2.134 |
| ② | **顺带补掉端到端往返暴露的三处既有回读缺口**（都不是这一版引入的，但都会让「恢复出的存档再生成一次」跟第一次不一样） | ① `mod_manifest.json` 里的 `author` / `version` / `description` 本来就写着、以前**没读**（恢复后静默变空串）→ 现在读回来（空串不回写，别冲掉模板带来的值）；② 角色「说明」留空时生成端写的**兜底文案**（抽成 `LocalizationGen.DefaultCharacterDescription` = 「一个自定义角色。」）以前会被当成用户填的说明读回来 → 现在「文案 == 兜底那句 **且** 清单里 description 是空的」当没填；③ 上面那条端口版本 | ✅V0.2.134 |

> **图标记格式定义（发版说明用）**
> ```
> // CET:ForgeGraph=<版本>:<Base64(UTF-8(图的紧凑 JSON))>
> ```
> - `<版本>` 现在是 `1`（`ForgeGraphGen.MarkerVersion`）；**改格式就 +1，回读端遇到不认识的版本一定报错说清**（不许静默丢图）。
> - JSON 就是 `ForgeGraphSpec` 用存档同一套 `ProfileFactory.JsonOpts` 序列化的结果（`Nodes[].Id/Kind/X/Y/Fields/EffectSpec/EffectFromLegacy` + `Links[]` + `Note`）。
> - 位置：生成出来的类里，紧贴在 `// ===== 锻造节点图…` 那一行**前面**；每个条目**每块布一行**（卡牌升级前 / 升级后各一行，遗物 / 药水各一行）。`ReadMarker` 的 `skip` 参数读的就是第几行。
> - **只在图非空时写**；图空 / null 的条目一个字符都不加。

> **代价清单（必须让用户知道：从这一版起，锻造图会写进产物）**
> - **逐字节不变**：`--samplegen`（示例存档，没有锻造图）112 个文件里 **111 个逐文件 SHA256 一致**，唯一差异是 `ExampleMod.sln` 的**随机 GUID**（去掉 GUID 后逐字相同）。任何**没有锻造图**的存档同理。
> - **多出标记的清单（改动前 → 改动后，逐文件逐字节对拍；「删掉新增的标记行后有内容行逐字节相同」= 证明别的一字未改）**：
>
> | 存档 | 文件 | 原字节 | 新字节 | Δ | 新增行 |
> |---|---|---|---|---|---|
> | `DarkVinci.json` | `cs/Cards/DarkVinciDoubleFlash.cs` | 4957 | 28173 | +23216 | 2 |
> | | `cs/Cards/DarkVinciFlash.cs` | 3759 | 30015 | +26256 | 2 |
> | | `cs/Cards/DarkVinciIgnite.cs` | 5112 | 40368 | +35256 | 2 |
> | | `cs/Cards/DarkVinciImpastoForm.cs` | 3922 | 14522 | +10600 | 2 |
> | | `cs/Cards/DarkVinciPhotosynthesis.cs` | 5277 | 28373 | +23096 | 2 |
> | | `cs/Cards/DarkVinciTide.cs` | 4714 | 23306 | +18592 | 2 |
> | `SparkleMod.json` | `cs/Cards/SparkleCard41.cs` | 4825 | 28037 | +23212 | 2 |
> | | `cs/Cards/SparkleCard42.cs` | 2980 | 14212 | +11232 | 1（没有升级后那块布） |
>
> 其余文件**一字未改**（`DarkVinci` 124 个文件里 117 个逐字节一致 + 6 个卡牌文件只多标记 + `.sln` 随机 GUID；
> `SparkleMod` 303 个文件里 300 个逐字节一致 + 2 个卡牌文件只多标记 + `.sln`）。
> 新增行**全部**以 `// CET:ForgeGraph=` 开头（逐文件核过），删掉这些行之后 `before` / `after` 的**有内容行逐字节相同**；行尾一律 CRLF、无 BOM 变化。
> 标记长度：Base64 之后一行 10~40 KB（含【效果】模块 `EffectSpec` 的图最大，`DarkVinciIgnite` 17 模块 = 35 KB）。

> **红线实测数字（V0.2.134）**
> - ② `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**；
> - ③ `--uicheck`：Debug **3134 PASS / 0 FAIL**（基线 3115/0，**新增 19 条**）、
>   发布版（`dotnet publish src\Sts2CharForge.App -c Release -r win-x64 --self-contained true -o D:\ds\s\_tmp\v02134_pub`
>   → `Unblock-File` → 先删旧 `uicheck_result.txt` → `[System.Diagnostics.Process]::Start(UseShellExecute=$false,
>   TEMP=TMP=D:\ds\s\_tmp\apppub_tmp, DOTNET_DISABLE_GUI_ERRORS=1)` + `WaitForExit()`；**没有用 `Start-Process`**）
>   **3134 PASS / 0 FAIL**；
> - ④ **端到端往返（本轮的验收核心）**：
>   · 含锻造图的真实存档 `DarkVinci.json` → 生成 → `--recover` → **`IsForged` 6/6 张（源 6 / 恢复 6）**、
>     **非空图块数 12/12**、**逐字段对照 42 个字段块 / 不一致 0**（节点 Id·种类·坐标·每一格 `Fields`·`EffectSpec`·`EffectFromLegacy`·连线·`Note` 全过）；
>   · **「恢复出的存档再生成一次」逐字节**：**124 个文件 / 103 个逐字节一致**，剩 21 个是**既有回读缺口**
>     （**与图无关**：卡牌升级增量写在公式变量末位参数上、组引用 `Group:<id>`、状态外观覆盖挑错状态 —— 详见下面「已知未修」），
>     这 21 个里**没有任何一个含 `CET:ForgeGraph=` 行**；
>   · **「图标记本身逐字节幂等」用一份干净探针 `_tmp\v02134\uc\UcProbe134.json` 全量证明**：
>     生成 → `--recover` → 再生成，**64 个文件 / 差异 0**（含 `.sln`）；
>   · `--uicheck` 里也有一条同样的自检断言：**两次生成逐文件对拍 = 逐字节一致 63 个文件**（只放过 `.sln` 的随机 GUID）；
>   · **老工程（改动前生成、没有标记）→ `--recover` 仍然 0 条「没认出来」**（图 0 张，其余字段照旧；
>     与改动前逐条比对，「没认出来」都是同 1 条既有的 `DarkVinciDyeing: 升级增量找不到对应效果（变量 Cards2）`）；
> - ⑤ 五处配套：生成端（`ForgeGraphGen` 写标记 + `ModGenerator` 生成后自检）、回读端（`ProjectRecovery`）、
>   文案（README + `tools\使用说明.template.txt`，**无 BOM** 字节复核）、校验器（版本 / 损坏一定说清，均进「没认出来」清单）、
>   界面（恢复日志 + 状态栏 + 确认框带「含锻造图 N 张」）；
> - **行尾约定**：本轮改过的 6 个源文件（`ForgeGraphGen.cs` / `ProjectRecovery.cs` / `ModGenerator.cs` /
>   `LocalizationGen.cs` / `Program.cs` / `MainWindow.xaml.cs`）逐个统计过 —— **全是 CRLF、裸 LF = 0、无 BOM 变化**；
>   `README.md` 与 `docs\QUEUE.md` 本来就是 **LF**、`tools\使用说明.template.txt` 是 **CRLF + 无 BOM**（复核过字节）。

> **已知未修 / 边界**：① **「组」（`CardGroups` / 卡牌 `GroupIds`）仍然不能从工程恢复** ——
> 工程里只有「范围限定 = 组：`<id>`」那一处痕迹，组名和成员表只存在存档里。后果：恢复出的存档里那个组是空的，
> 还有效果引用它时**下次生成会被校验器拦下并说清**（`DarkVinci` 实测命中：卡「染色」的「生成卡牌」范围限定引用了组 `b8009bf2…`）。
> 有意**不**按猜出来的成员生成（那会让产物悄悄变样），请到「自定义参数 / 组」页补一下；
> 用旧存档当模板时组名会跟着带过来。
> ② **「恢复出的存档再生成一次」在真实存档上还不逐字节一致**（探针 / 自检那种干净配置是逐字节一致的）。
> 剩下的 21 处差异全是**既有**回读缺口，本轮只做了「锻造节点图 + 三处顺手补的」这一批：
> 卡牌效果的**升级增量**（公式变量的末位参数 `UpgradeValueBy` 映射不回某条效果）、
> **组**（同 ①）、**状态外观覆盖**（`ForgeHpBarOverlay` 挑错了颜色档）、
> **先古对话行数**、**延迟状态的作用对象**（退成「自己」）。这些都记在上面各批的「已知」里，与锻造图无关。
> ③ 图标记让**含锻造图的存档**产物变大（见代价清单），用户已拍板接受这一版。

## 第 20 批需求（V0.2.133：「下回合生效」适配全部效果，用户拍板 Q4=B 的下半段）

> 需求原文：`docs\需求\卡牌页优化.md` 右侧④：「其下为是否下回合生效，**这个很重要，让这个适配所有的效果**」。
> **用户拍板**：`docs\需求\已拍板决定.md` **Q4=B**（一次做齐）+ **第四节**（那 8 种「从此生效」）。
> 预研：`D:\ds\s\_tmp\下回合生效_补齐清单（预研）.md`（那是 V0.2.132 之前的状态，按实际代码复核过）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **「下回合生效」补齐到全部效果**（`HonorsNextTurn` 12 → 65 里可做的 57 种） | **判据只有一份**：`EffectKindOption` 的 `SupportsNextTurn` 那一栏**删掉构造参数**、改成算出来的属性 `=> EffectCatalog.HonorsNextTurn(Kind)`（同一个类里，引用本类型是合法的静态上下文）—— 两者**不可能再漂**（历史上就是那一栏手写 true、生成端没分支 → 用户报的「勾了没反应」）。`HonorsNextTurn = 不是 NextTurnMeaningless(kind) 且在 EffectKinds 里`。**做不了的 8 种**（`NextTurnMeaningless`）：`CheapenSelf`（本回合这张牌自己费用变 0）/ `CheapenSelfOnDraw` / `ReturnSelfToHand` / `CheapenDraw` / `EndTurn` / `ExtraTurn` / `ScrapeDraw` / `TurnEndPlay` —— 语义绑死在打出那一瞬间，界面上**不显示**勾选框、校验器报错说清。**生成端**：新增**通用延迟分支** `CSharpCodeGen.IsDelayedEffectKind`（= HonorsNextTurn 且不是本体四种 / 生成类七种 / ApplyPower）+ `EmitDelayedPowerApply`（打出时只挂 `<角色>ForgeDelayed<种类><状态/关键词><作用对象>` 延迟状态，带一行 `CET:Effect=… CET:NextTurn=1` 回读标记）+ `EmitDelayedEffectBody`（延迟状态类的方法体 = 把**同一套发射器** `EmitCardEffectOnce` 发射一遍再替换上下文表达式）+ `DelayedVarMap`/`LiteralAmountOf`（延迟状态里没有 `base.DynamicVars`，数值一律折成常量）。`EmitCardEffectOnce` 里那一大段 `switch` 原样抽成 `EmitCardEffectSwitch`（**一个字都没改**，只是让延迟那条路也能跑同一套）。**四条链**（卡牌 / 遗物 / 药水 / 自定义状态）都接上。**界面/校验器**：勾选框自动跟着 `SupportsNextTurn` 走；校验器措辞改成「语义绑死在打出那一瞬间」并列出那 8 种；**顺手删掉**「生成类只支持做在卡牌上」那条错误（四条链都接了） | ✅V0.2.133 |
| ② | **那 8 种「从此生效」**（用户已定：老存档里勾过的开始真的延迟生效） | 透支能量 / 翻倍状态层数 / 暴露 / 清除指定状态 / 清除全部减益 / 禁止获得格挡 / 禁止回复生命 / 伙伴攻击 —— 这 8 种走**通用延迟分支**（`IsDelayedEffectKind` 为 true），老存档里 `NextTurn=true` 的从「校验器报错、根本生成不出来」变成「真的生成延迟分支」。**其余 52 种**在 V0.2.132 及以前界面上根本没有勾选框 ⇒ 老存档里必然 `NextTurn=false` ⇒ 生成输出不变 | ✅V0.2.133 |

> **红线实测数字（V0.2.133）**
> - `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**；
> - `--uicheck`：Debug **3115 PASS / 0 FAIL**（基线 3040/0，**新增 75 条**）、
>   发布版（`dotnet publish -c Release -r win-x64 --self-contained true` → `Unblock-File`
>   → `[System.Diagnostics.Process]::Start(UseShellExecute=$false)` + `WaitForExit`，跑前先删旧结果文件）
>   **3115 PASS / 0 FAIL**（新增断言：判据逐条对齐 / 那 8 种支持且显示 /
>   每一种支持的种类都真的生成延迟状态类 / 语义上做不了的那几种报错 / 四条链都接上 / 没勾走老路径）；
> - **默认档逐字不变**：`--samplegen` 112 个文件 **80 个逐字节一致 + 31 个只差行尾**、内容 0 处差异
>   （唯一内容差异 = `ExampleMod.sln` 的随机 GUID）。行尾噪声来源：基线在 `git worktree`（HEAD）里跑，
>   检出的源码是 **CRLF**（`core.autocrlf=true`），而 t3 工作区的 `.cs` 是 **LF** ——
>   `Lit.Str(...)` 把内嵌字符串的行尾一路带进产物；**CRLF→LF 归一化后 0 处内容差异**；
> - **老存档对拍**（`_tmp\v02133\legacy8_off.json` = 那 8 种 + 其余代表种类，**全部 `NextTurn=false`**）：
>   105 个文件 **81 个逐字节一致 + 23 个只差行尾 + 1 个 `.sln` 随机 GUID**，内容 0 处差异；
> - **探针** `D:\ds\s\_tmp\v02133`：**59 张卡（覆盖 57 种延迟种类 + 老 12 种）＋ 遗物 / 药水链**
>   → CLI `--build --install` **真 `dotnet build` 0 错误** → `--recover` **0 条「没认出来」**；
>   字段逐项还原 59 条里 **52 条逐字段一致**，剩 7 条是**既有**回读缺口（见下）；
> - **幂等**：同一份存档连跑两次 379 个文件 **378 个逐字节一致**（只差 `.sln`）；
> - **行尾约定**：本轮改过的文件里 `EffectCatalog.cs` 是 **LF**、其余（`CSharpCodeGen.cs` /
>   `Naming.cs` / `ProfileValidator.cs` / `ProjectRecovery.cs` / `MainWindow.xaml.cs` /
>   `使用说明.template.txt`）是 **CRLF**，逐个统计核对过、无混合行尾、无 BOM 变化。

> **已知边界 / 留给用户定**：① 延迟状态里没有「这张牌」，所以延迟执行的数值一律取**生成期折好的常量**
> （`Amount + UpgradeAmount`；「数值 = X」在延迟里退成基础数值）；② 「打到打出时选中的那个目标」靠
> `PowerCmd.Apply` 的 target 参数带走，延迟时用 `base.Target` 取回，**目标不在了就退成第一个可打的敌人**；
> ③ **遗物 / 药水 / 自定义状态链上的「生成类」七种**还没有延迟分支（那七种需要「这张牌」），
> 只接上了通用延迟分支的 45 种；④ 回读仍有 7 处**既有**缺口（不是本轮引入）：
> `Block/Draw/Energy/ExtraResource + NextTurn` 那几个本体现成状态没写回读标记（`Amount` / `PowerId` 还原不全）、
> `ApplyPower + NextTurn` 的 `TargetSide` 退成「自己」、`PetSacrifice` 的延迟 `PowerId` 缺尾；
> ⑤ 那 8 种现在真的会改老存档输出（用户已明确接受，发版说明要点名）。


## 第 19 批需求（用户 2026-10-XX 提，按《卡牌页优化.md》**右侧④**里的作用对象扩档 + 概率独立 + 卡牌链 UI 重排）

> 需求原文：`docs\需求\卡牌页优化.md` 右侧④。**用户拍板**：`docs\需求\已拍板决定.md` **Q4=B**（一次做齐）——
> 但按用户排期，本轮只做**作用对象 + 概率独立 + UI 重排**；「下回合生效适配全部效果」（`HonorsNextTurn` 12 → 65）
> 是**下一版**，本轮一个字没动。盘点报告：`D:\ds\s\t3\_tmp\需求盘点_锻造页与卡牌页.md` §2.1 / §2.3 / §6.1。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **作用对象扩到 7 档**（自己 / 指定队友 / 全部友方（可选含召唤物）/ 单体敌人 / 随机敌人 / 全部敌人 / **全体单位**），**共用一张表**（卡牌·遗物·药水·自定义状态·锻造链五处下拉一起走） | `EffectCatalog.TargetSides` = 7 档（顺序 = 需求里的顺序）+ 新档 `AllUnits` + 新中文名表 `TargetSideZh` + 「哪些种类真的实现了这一档」`SupportsAllUnits` / `SupportsAllUnitsOnPower`。**生成端**：`EmitPerCreature`（共用目标展开）/ `EmitAttack`（伤害）/ 卡牌 `ApplyPower`·`TempPower` 两个 switch / `EmitStun` / 遗物链 `Damage`·`ApplyPower`·`TempPower` / `CustomPowerGen` 的 `EmitDamage`·`ApplyPower`·`TempPower` —— 全部新增 `AllUnits` 分支（循环体 = `<生物表达式>.CombatState.Creatures.Where(c => c.IsAlive).ToList()`：卡牌 / 遗物是 `base.Owner.Creature.CombatState`、自定义状态是 `base.Owner.CombatState` —— 状态链上写 `.Creature` 会 CS1061，见下面探针那段；本体 `CombatState.cs:71` = `_allies.Concat(_enemies)`）。**「全部友方」子选项**：`EffectSpec.AllAlliesExcludePets`（默认 false = 老行为），勾上才在过滤条件尾巴补 `&& c.PetOwner is null`（`CSharpCodeGen.AllyFilterSuffix`）。**卡面文案**：`LocalizationGen` 的 target / who 两个 switch 加 `AllUnits`（「对全体单位」/「所有单位」），「全部友方不含召唤物」写「对所有队友（不含召唤物）」。**校验器（不许静默）**：`ProfileValidator.ValidateAllUnitsTarget` —— 没实现这一档的种类**报错**并列出支持名单（卡牌/遗物/药水 8 种；自定义状态那条链更窄：伤害 / 施加状态 / 临时增益 / 击晕 / 清除状态×2），不拦的话生成端会把它当成「对自己」。**回读**：卡牌链写 `// CET:TargetSide=AllUnits`（勾了不含召唤物再补 ` CET:AllyNoPets=1`）；遗物链 / 状态链 / 药水链**只在出现新档位时才补这一行标记**（老存档一个字符都不多）；`SideOfTarget` 也认 `CombatState.Creatures`。**界面**：五处下拉自动跟着走（共用表）+ 「全部友方」底下新增「不包括召唤物（只算玩家本体）」勾选框（`ShowAllyNoPetsOption`）。**锻造节点图**：`TargetSideToPick` / `TargetSideUnit` 加 `AllUnits` → `ForgeFormulaRuntime.AllUnits(__fgPlayer)`，「不含召唤物」= 只剩 `__fgSelf`（不新增运行时助手，免得动已生成工程的 `ForgeFormulaRuntime.cs`） | ✅V0.2.132 |
| ② | **「不同效果的概率之间是否独立」**（新语义，**默认必须 = 老行为**） | 新字段 `EffectSpec.ChanceIndependent`（默认 false = 同一张牌 / 同一只遗物里勾了概率的效果**共用一次掷骰、各占区间**，老行为逐字不变）。**生成端**：`ChanceSlices` 把勾了独立的**排除**出区间划分；`EffectConditionGuard` 新增 `else if (e.ChanceEnabled && e.ChanceIndependent)` → `{player}.RunState.Rng.Niche.NextDouble() * 100.0 < N`（这一条自己掷一次）；全部独立时不声明 `__chanceRoll`（没有区间、没公式就不需要它）。**回读**：`EmitCardEffect` 只在**显式勾了独立**时写 `// CET:ChanceIndependent=1 CET:ChancePct=<百分比>`（百分比也带上 —— 概率本身**从来没回读过**，老缺口，见下），`ProjectRecovery` 按它还原 `ChanceEnabled/ChanceIndependent/ChancePercent`。**校验器**：勾了给一条提示说清与老行为的区别。**卡面 / 列表文案**：`ChanceText` 补一句「，各自独立判定」。**界面**：勾选框长在**卡牌链**的「概率」那一行里（`ShowCardChanceRow`） | ✅V0.2.132 |
| ③ | **卡牌链效果编辑器按需求顺序重排**（升级增量 → 效果种类 → 数值 → 作用对象 → 命中次数 → 生效次数 → 概率 → 下回合生效 → 其余按种类） | `App.xaml` 的 `EffectCommon`：**命中次数 / 生效次数**改成「固定值框 + 公式框并排」（新增 `ShowCardRepeatRow` / `ShowCardTimesRow`；老的那两个固定框改绑 `ShowPlainFixedRepeatBox` / `ShowPlainFixedTimesBox`，**只在非卡牌链显示**）；**概率**（勾选 + 百分比 + 概率公式 + 独立开关）挪到 Grid 第 6 行、**下回合生效**挪到第 7 行（`ShowCardChanceRow` / `ShowCardNextTurnRow`，只有卡牌链显示；StackPanel 里那两行老控件改绑 `ShowPlainChanceRow` / `ShowPlainNextTurnRow`）；「增益 / 减益」「目标卡」两行从 Grid 5/6 挪到 8/9（**位置没变**：5、6 行对四条老链是 Collapsed 的 Auto 行、高度 0）。`EffectEditorCard` 的「次数公式」组只剩「复制的份数 / 重放次数」（其余三格搬到上面各自的并排行里）。**遗物 / 药水 / 自定义状态 / 锻造链的控件树、绑定路径、行位置一个都没动**（新摆的元素对它们恒为 Collapsed） | ✅V0.2.132 |
| ④ | **「命中次数」不再考虑「对群数」**（核对有没有残留） | 核对结果：**界面上一处 `AmountIsX` / `TimesIsX` / `RepeatIsX` 入口都没有了**（V0.2.131 撤掉三个「= X」勾选框，本轮复查 App.xaml 无绑定）；**没有**独立的「对群数」字段 —— 从头到尾只有 `RepeatCount` 一个数字（「造成伤害」= 段数 /「随机敌人」= 挑几个目标），生成端这个数字照旧全读；卡牌链那一行的标题改成「命中次数（公式）」，**其余四条链的老标题「对群数/命中次数」保持原样**（免得动那四处外观，要不要一起改名留给用户定）。等价性对拍：含「随机敌人 + 命中次数 2」的老存档，改动前后生成的 `cs/Cards/LegacyRandomEnemies.cs` **SHA256 完全相同**（`7414ba79…`，`.TargetingRandomOpponents(...).WithHitCount(2)`） | ✅V0.2.132（核对项，没有能力被删） |

> **红线实测数字（V0.2.132）**
> - `dotnet build Sts2CharForge.sln -c Debug --no-incremental` → **0 错误**；
> - `--uicheck`：Debug **3040 PASS / 0 FAIL**、发布版（`dotnet publish -c Release -r win-x64 --self-contained true`
>   → `Unblock-File` → `[System.Diagnostics.Process]::Start(UseShellExecute=$false)` + `WaitForExit`，跑前先删旧结果文件）
>   **3040 PASS / 0 FAIL**（基线 3001/0，**新增 39 条**：7 档表 / 全体单位五条链生成 / 校验器拦住不支持的种类 /
>   全部友方不含召唤物 / 概率独立三档 / 新标记回读 / 卡牌链重排与遗物链对照 / 状态链不出现 `base.Owner.Creature`）；
> - **默认档逐字不变**：`--samplegen` 112 个文件 **111 个逐文件 SHA256 一致**（只差 `ExampleMod.sln` 的随机 GUID）；
>   另用两份**老形状存档**对拍（`_tmp\v02132\legacy_targets.json` = 6 档作用对象齐全；`legacy_chance.json` = 概率生效）：
>   136 个文件 **135 个逐字节一致**、127 个文件 **126 个逐字节一致**（两处唯一差异都只有 `.sln` 的随机 GUID）；
> - **探针** `D:\ds\s\_tmp\v02132`：16 张卡 + 1 只遗物（7 档各一张、4 张全体单位、全部友方两档、概率三档）→
>   CLI `--build --install` **真 `dotnet build` 0 错误**（2 条既有 warning）→ `--recover` **0 条「没认出来」**、
>   作用对象 / 不含召唤物 / 命中次数 / 状态 / 概率独立 + 百分比**逐项还原**；另一份探针 `probe132_cp.json`
>   把**自定义状态（能力）那条链**的「全体单位」也真编译一遍（伤害 / 施加状态 / 临时增益 / 击晕四条）→
>   `--build` **0 错误** + `--recover` **0 条「没认出来」**、四条作用对象全还原成 `AllUnits`。
>   ⚠ **这份补的探针当场抓到一个真 bug 并已修**：状态链的 `base.Owner` 本身就是 `Creature`，
>   而「全体单位」最初写死 `base.Owner.Creature.CombatState` → 生成出来 `CS1061`
>   （`cs/Powers/<状态>.cs`）—— 现在 `EmitPerCreature` 从 `selfExpr` 取、`EmitStun` 从 `playerExpr` 取，
>   `--uicheck` 新增 4 条断言把「状态链上不出现 `base.Owner.Creature`」钉死。
>   **幂等**：同一份存档连跑两次 113 个文件**只有 `.sln` 不同**；
>   「生成 → 回读 → 再生成 → 再回读 → 再生成」的**第 2、3 轮产物逐字节一致**（同样只差 `.sln`）；
>   同一个工程回读两次**输出字节相同**（73204 字节，同一 SHA256）。
>
> **本轮明确不做**（留给下一版 / 等用户拍板）：右④ 里的「**下回合生效适配全部效果**」（`HonorsNextTurn` 12 → 65，
> Q4=B 已拍板要一次做齐，但按排期单独一版）、右①「升级增量填公式」、④「升级后关键字 + 自定义关键词」、
> ⑤「升级描述变绿」；把其余四条链的「对群数/命中次数」也改名；「全部友方含不含召唤物」之外的组合档。
>
> **已知边界 / 留给用户定**：① 概率的**百分比**（`ChanceEnabled` / `ChancePercent`）**老版本就不回读**
> （`--recover` 后固定 50%），本轮只把**新档「独立」**的百分比用 `CET:ChancePct=` 带回来了 ——
> 要不要顺手做「从 `__chanceRoll >= a && < b` 反推百分比」（区间被缩放时会有歧义）？② 卡牌链的「命中次数」标题
> 改了、其余四条链没改；③ 「全体单位」在**药水**上只影响卡面描述（药水实际打谁由药水自己的「作用目标」决定，
> 校验器给警告）—— 要不要给「全体单位」也开一档**药水作用目标**？④ 自定义状态那条链的「获得格挡 / 回复生命 /
> 失去生命 / 最大生命」是**手写只打自己**的（不读作用对象），所以它支持「全体单位」的种类比卡牌链窄
> （校验器会报错说清）—— 要不要把那条链也改成按作用对象展开（会动老存档生成文本，要单独一版）。
> ⑤ **存档形状多了两个字段**：`EffectSpec.AllAlliesExcludePets` / `ChanceIndependent`（都是 `bool`，默认 false）。
> `ProfileFactory` 的 `JsonOpts` 没有「默认值不写」的开关，所以**保存一次存档就会给每条效果写上这两个键**
> （和以前新增 `TargetSideCustom` / `ForgeAmountText` / `ChanceFormula` 时一样）。老存档读得回来、
> **生成结果逐字节不变**，但存档 JSON 会多两行；要不要给它们加 `[JsonIgnore(Condition = WhenWritingDefault)]`？
> ⑥ 卡牌链的「命中次数」那一行的**表头**现在只写「命中次数（公式）」（固定框没有单独的表头 —— 它跟着公式框那一行），
> 要不要给固定框也补一个「命中次数」小标题？

## 第 18 批需求（用户 2026-10-XX 提，按《卡牌页优化.md》**中间② + 右侧②③**；其余留给后面几版）

> 需求原文：`docs\需求\卡牌页优化.md`。**用户拍板**：`docs\需求\已拍板决定.md` **Q3 = A「两者都要」**
> —— `A+3` 这类**生成期折叠成常量**（`A` 必须来自「固定数值」参数来源）＋ `X` **沿用本体那套**
> （`ResolveEnergyXValue()` / `ResolveStarXValue()`）。
> 盘点报告：`D:\ds\s\t3\_tmp\需求盘点_锻造页与卡牌页.md` §4② / §6.1（行号是中间态快照，按实际代码复核过）。
> 先例复用：**V0.2.128 给锻造页 12 个数字框做的三档解析**（`Core\Generation\ForgeNumbers.cs` +
> `ForgeFormula.TryEvalConst`）—— 费用栏**复用同一套解析器与可用量口径**，没有另造。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **卡牌费用（中间②）**：「去掉 X 费用、额外资源量费用为 X、升级后 X+1 这三项，在原本费用框内支持填 `A+3` 或 `X`；**费用框同侧右边加额外资源量费用框**（同样可填 `A+3` / `Y`）；**这两下方加「升级后费用」和「升级后额外资源量费用」**」 | **模型**（`Profile.cs` `CardSpec`）：新增 `CostFormula` / `StarCostFormula` / `UpgradeCostFormula`（都是「框里的原文」字符串）+ `UpgradeStarCost`（升级后额外资源量费用，`int?`）+ `UpgradeStarCostFormula`（解析不了时留原文给校验器报错）；外加**文本代理** `CostText` / `StarCostText` / `UpgradeCostText` / `UpgradeStarCostText`（写纯数字→落回老字段 `Cost` / `UpgradeCost`，写 `X` / `Y` / `X+1`→落回老勾选框字段 `CostIsX` / `StarCostIsX` / `XPlusOnUpgrade`，写式子→落在新字段）。派生判据 `CostIsXEffective` / `StarCostIsXEffective` + `CSharpCodeGen.IsCostX` / `IsStarCostX` / `CostXPlus`。**解析器**（`ForgeNumbers.ResolveCost`，三档：纯整数 → 生成期常量折叠（`TryEvalConst` + 固定数值参数）→ 只含 `X`/`Y` 一次项 ⇒ X 费用；**运行时表达式一律 `Ok=false` + 中文原因**）。**生成端**：`base(cost,…)` 用折叠值、`HasEnergyCostX` / `HasStarCostX` 走本体的 X 那套、`CanonicalStarCost => N`、`OnUpgrade()` 里 `EnergyCost.UpgradeBy(差值)` / **`UpgradeStarCostBy(差值)`**（本体依据：`sts2.dll` 的 `CardModel` 上 `protected void UpgradeStarCostBy(int addend)` 与 `OnUpgrade()` 同级可见性，另有 `BaseStarCost`/`CurrentStarCost`/`WasStarCostJustUpgraded` 那套机制 —— 所以照 `EnergyCost.UpgradeBy` 的同款思路写，不用自己发明）。**校验器**：费用框写运行时表达式 → **错误**并说清是哪张牌哪一栏；X 费牌填绝对升级后费用 / 框与负数效果冲突 → 警告。**回读**：`HasEnergyCostX`/`HasStarCostX` 照旧认；`ResolveEnergyXValue() + (base.IsUpgraded ? N : 0)` 与公式序幕注释 `// X = …（升级后 X + N）` 还原「升级后 X+N」；「额外资源量费用」框的原文写成 `// CET:StarCostFormula=`（老口径会凭空补一条负数效果 → 幂等会破）。**界面**：`MainWindow.xaml` 卡牌页费用那一块换成两行四框（左费用 / 右额外资源量费用；下一行升级后费用 / 升级后额外资源量费用）+ 两句说明。**文案**：「额外资源量费用」框显式写了费用时卡面开头补一句「需要 N 点…/需要 X 点…（打出时消耗全部）」；`CardSpec.Display` 的费用列按有效值显示 | ✅V0.2.131 |
| ② | **右侧②「去除升级后费用（重复了）」** | **是搬家不是删除**：`App.xaml` 的 `EffectEditorCard` 里那一栏（+ 提示行）删掉，同一次把中间那两栏加上（见 ①）—— 能力中途没消失。`MainWindow.xaml.cs` 的 `SelectedCardUpgradeCost` 保留（老自检还在用），`UpgradeCostHint` 改成新口径（也说 `X+1` 那一档） | ✅V0.2.131 |
| ③ | **右侧③「去除 数值=X / 生效次数=X / 命中次数·对群数=X」** | **界面删 3 个勾选框**（`App.xaml`），**模型三个字段一个都没删**（`AmountIsX`/`TimesIsX`/`RepeatIsX`，老存档原样读得回来、生成文本逐字不变）。**X 这个能力没丢**：`ShowAmountFormula` 在卡牌链上对所有种类都显示「数值（公式）」，非那 5 种的**只认光秃秃一个 `X`**（`AmountIsXEffective` 判据，生成端 32 处读 `AmountIsX` 的地方全部换成它 → 与老勾选框**逐字相同**）；生效次数 / 命中次数写 X 走既有的「次数公式」通道（`TimesFormula` / `RepeatFormula`，解析成 `__X`）。**等价性逐条对拍**见下面那段 | ✅V0.2.131 |

> **三开关能力等价性（逐条：原勾选配置 → 新写法 → 生成结果）**
>
> | 原勾选 | 新写法 | 生成结果对不对拍 |
> |---|---|---|
> | 「数值 = X」+ 非那 5 种（抽牌 / 生成 / 消耗…） | 「数值（公式）」框写 `X` | **逐字节相同**（`--uicheck` 断言「抽牌 + 公式写 X」与「抽牌 + 勾 AmountIsX」生成文本 `==`，都出 `CardPileCmd.Draw(choiceContext, x, base.Owner)`） |
> | 「数值 = X」+ 那 5 种（伤害 / 格挡 / 失去生命 / 回复 / 施加状态） | 「数值（公式）」框写 `X` | **语义等价、写法不同**：新写法走既有的公式通道（`decimal __X = ForgeFormulaRuntime.Energy(base.Owner)` + `ForgeFormulaDamageVar` 现算），老写法直接取 `x`。值一样（X 费牌打出时「当前能量」=「消耗掉的能量」）。⚠ 这是 V0.2.112 起就有的既有设计，本版只是把「删掉勾选框」之后的替代写法讲清楚 |
> | 「生效次数 = X」 | 「次数公式 · 生效次数」框写 `X` | **语义等价**：新写法 `for (int i = 0; i < (int)Math.Clamp(__X, 1m, 20m); i++)`，老写法 `for (int i = 0; i < x; i++)` —— 次数公式通道一律夹到 1~20（既有规矩），X>20 时会不同 |
> | 「命中次数 / 对群数 = X」 | 「次数公式 · 命中次数」框写 `X` | **语义等价**：`.WithHitCount((int)Math.Clamp(__X, 1m, 20m))` vs `.WithHitCount(x)`，同样多一层 1~20 夹取 |
> | 「升级后 X +1」 | 「升级后费用」框写 `X+1` | **逐字节相同**（`--uicheck` 断言两种写法生成文本 `==`，都是 `int x = ResolveEnergyXValue() + (base.IsUpgraded ? 1 : 0);`） |
> | 「X 费用」 | 费用框写 `X` | **逐字节相同**（同一段 `HasEnergyCostX => true` + `base(0, …)`） |
> | 「额外资源量费用为 X」 | 额外资源量费用框写 `Y` | **逐字节相同**（同一段 `HasStarCostX => true`） |
>
> ⚠ **顺手修正一个老 bug**：以前「升级后 X +1」只对**老写法**（勾「数值 = X」→ 变量 `x`）生效，
> 对「数值（公式）」里写的 `X`（变量 `__X`）**不生效** —— 两处都是同一个 X（打出时消耗掉的能量），
> 少了 `+1` 就是静默丢能力。现在公式序幕那句也带上 `+ (base.IsUpgraded ? N : 0)`。
> 只影响「老存档既勾了升级后 X+1、又用了引用 X 的公式」这种组合（以前那种组合本来就是错的）；
> 本轮的默认档对拍与含卡老形状存档对拍都实测**逐字节一致**（见下）。
>
> **老字段迁移规则 + 幂等**：`CostIsX` / `StarCostIsX` / `XPlusOnUpgrade` / `AmountIsX` / `TimesIsX` / `RepeatIsX`
> **一个都没删、生成端先读它们**，所以「老存档加载前后」的生成文本是**逐字节一样**（不需要迁移写回，
> 也就不会出现「迁移一次变一次」）。界面靠文本代理显示/写入：`CostIsX=true` → 费用框显示 `X`；
> 写纯数字/`X`/`Y`/`X+1` 时**落回老字段**（存档形状与老版本一致），只有写真正的式子才落到新字段。
> 实测：「生成 → `--recover` → 再生成 → 再 `--recover` → 再生成」，第 2/3 轮产物**逐字节一致**（只差 `.sln` 的随机 GUID），
> 两份恢复出来的存档**字节相同**（43026 字节）。
>
> **本轮明确不做**（留给后面几版）：右④ 重排 UI / 作用对象 7 档 / 概率独立 / 下回合生效 65 种、
> ④ 升级后关键字与自定义关键词、⑤ 升级描述变绿、右①「升级增量填公式」（`OnUpgrade()` 里没有玩家上下文，
> 根本障碍没解决，等用户拍板）。
>
> **红线实测数字**：`dotnet build Sts2CharForge.sln -c Debug --no-incremental` **0 错误**；
> `--uicheck` Debug **3001 PASS / 0 FAIL**、发布版（`dotnet publish -c Release -r win-x64 --self-contained true`
> → `Unblock-File` → `[System.Diagnostics.Process]::Start(UseShellExecute=$false)` + `WaitForExit`，跑前先删旧结果文件）
> **3001 PASS / 0 FAIL**（基线 2975/0，新增 26 条）；**默认配置逐字不变**：`--samplegen` 113 个文件
> **112 个逐文件 SHA256 一致**（只差 `ExampleMod.sln` 的随机 GUID）、文件名一个都没变；
> **含卡（X 费 / 额外资源量费 / 升级后费用）的老形状存档对拍**：91 个文件里 **89 个逐字节一致**
> （同样只差 `.sln`）。探针 `D:\ds\s\_tmp\v02131`：新费用语义存档（费用常量公式 / 费用 X / 额外资源量费用式子 /
> 额外资源量费用 Y / 升级后费用 X+1 / 升级后额外资源量费用 / 三种 X 的等价新写法）→ CLI `--build --install`
> **真 `dotnet build` 0 错误** → `--recover` **0 条「没认出来」**、字段逐项还原（费用式子回读成折叠后的
> `Cost` / `CostIsX` / `StarCostIsX` / `UpgradeStarCost`，语义等价、再生成逐字节一致）；**幂等**：
> 同配置连跑两次 + 「生成→回读→再生成→再回读→再生成」两轮产物逐字节一致、两份恢复存档字节相同。
> **行尾噪声怎么排除**：本机 `core.autocrlf=true`，直接 `git worktree` 检出的源码行尾会和 t3 工作区不一致
> → 生成物行尾跟着变、SHA 全对不上。做法 = `robocopy t3\src → _tmp\v02131\ref\src`（继承 t3 当前行尾约定）
> + 只把这一轮改过的 **8 个文件**用 `cmd /c "git show HEAD:<path> > tmp"`（**字节级**，避免 PowerShell 管道
> 按错误代码页解码）取回来，再**按每个文件在 t3 工作区里的行尾风格**（逐个统计 CRLF/LF：8 个里 6 个 CRLF、
> `ForgeNumbers.cs` 与 `ProfileFactory.cs` 是 LF）归一化后覆盖 → 编译 → 同一份存档跑两版 CLI 逐文件对拍。
> ⚠ `edit` 工具这轮没把文件写成 LF（改完逐个复核过），但每次编辑后仍逐文件统计了行尾。
>
> **已知未修 / 边界**：`ProjectRecovery` 仍然不支持锻造节点图（`ForgeGraph` 恢复后为空，见「已知未修」）；
> `--recover` 恢复不出「费用框里写的是 `1+1` 还是 `4`」这种**原文**（本体 `base(cost,…)` 是编译期常量，
> 生成代码里没有可标记的地方）—— 恢复的是**折叠后的值**，语义与再生成结果一致；同理
> 「额外资源量费用」框的原文靠新加的 `// CET:StarCostFormula=` 保住了，费用框没有加标记（会动老存档生成文本），
> 如果用户要求「原样还原费用式子」，要单独一版 + 明确的红线重新界定。

## 第 17 批需求（用户 2026-10-XX 提，按《卡牌页优化.md》**只做第 1 条 + 中间 ③**；其余留给后面几版）

> 需求原文：`docs\需求\卡牌页优化.md`。**用户拍板**：`docs\需求\已拍板决定.md` **Q5 = A —— 卡牌左侧序号按「列表里全部行」编号（含本体卡 / 初始打击防御）**。
> 盘点报告：`D:\ds\s\t3\_tmp\需求盘点_锻造页与卡牌页.md` §2 / §4①③ / §6.1（行号是中间态快照，按实际代码复核过）。
> 先例照抄：遗物页 V0.2.124/125（序号列 + 拖动 + `Naming.RenumberRelicClassNames`）、药水页 V0.2.127（`ISlotRow` / `SlotPlaceholder` + `Naming.RenumberPotionClassNames`）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **卡牌页左侧：序号列 + 拖动换位置 + 按序号自动英文类名**（用户强调「关键：左侧序号、可拖动位置、对应序号英文类名」） | **口径（Q5=A）**：`Naming.CardClassName(p,c)` 从「只数自有卡」改成**「AllCards 里连续编号」** —— 普通卡的号正好等于它在「卡牌」列表里的行号（本体卡 / 初始打击防御也占第 1、2 号），诅咒 / 先古卡接在普通卡后面连续编号（**不能各从 1 开始**，否则和普通卡撞类名 → DuplicateModelException）。新增 `Naming.IsAutoCardClassName(For)`（照遗物 / 药水那套**按名字形状认**：整串正好是 `<角色类名>Card<数字>` 才算工具自动名）+ `Naming.RenumberCardClassNames(cards, renamedOut)` + `CardSpec.Ordinal/LastAutoOrdinal/OrdinalText/AutoClassNameHint/ClassNameHint/RowTip/ClassNameIsAuto`（**全 `[JsonIgnore]`，存档面零变化**）。**界面**：`CardList.ItemTemplate` 加序号列（`OrdinalText`，`#1F4E79` 加粗）+ `DisplayMemberPath="{x:Null}"` + `AllowDrop` + 四个拖放事件；`RefreshCardOrdinals()`（`OnCardsChanged` + `SyncDetail` + `Profile` setter 三处调）+ `CardRowAtPoint` / `CardAtPoint` / `MoveCardTo`（纯逻辑，自检直接调）。⚠ **卡牌页不需要界面镜像集合**（没有空槽行）→ `ItemsSource` 仍然直接绑 `Profile.Cards`，`CardList.SelectedItem` 照旧是 `CardSpec`（那 129 处引用一处都不用改）。**生成前**：`NameStabilizer.RenumberCardNames`（不是直接调 `Naming.RenumberCardClassNames`）—— 卡牌是**唯一一种会被别的模型按类名引用**的东西（效果里的「目标卡」`SpawnCardId`、「转化为先古卡」的类名清单、锻造图【效果】模块的「目标卡」字段），改号必须**同一次**把那些引用一起改掉；**只靠 `RepairReferences` 那张「上一次构建的类名表」不幂等**（实测：CLI 连跑两次、存档没保存 → 第一次引用改对、第二次跑到同名的新主人身上，`CardProbeMate.cs` 与 `cards.json` 逐字节不同）。**校验器**：手填的类名长得像自动编号但序号对不上 → **提示**「下次生成会改名」（判据用**带角色类名**的 `n.IsAutoCardClassName`，和生成端一字不差；用「只按形状认」那版会对手打的 `MyCard1` 误报）。**卡面 / 文案**：`Display` 不变，序号走模板单独一列。**回读**：`ProjectRecovery` 照旧按**类名**认卡（改号后回读出来的就是新名，实测逐字段还原） | ✅V0.2.130（`--uicheck` Debug **2974/0**、发布版 **2974/0**（基线 2953/0）；`dotnet build Sts2CharForge.sln -c Debug --no-incremental` **0 错误**；**默认配置逐字不变**：`--samplegen` 112 个文件 **111 个逐文件 SHA256 一致**（只差 `ExampleMod.sln` 的随机 GUID）、**文件名一个都没变**；**含卡牌的老形状存档对拍**（`git worktree` 同源做法：`robocopy t3` + 按 t3 的 CRLF 约定把 8 个改过的文件还原成 HEAD 内容再编译，见下面「行尾噪声」）：89 个文件里 **77 个逐字节一致**，差异全部是**类名 / 文件名跟着序号变**（`CardProbeCard3→5`、`5→7`、`6→8`、`7→4`）—— 把「旧类名→新类名」映射应用到旧版本全部文件（含文件名、含小写 slug 与大写本地化键）后**只剩 `.sln` 的随机 GUID 一处**，证明**内容本身没变**。**幂等**：新版本连着生成两次，89 个文件里 **88 个逐字节一致 + 类名表完全一致**（只差 `.sln` 的随机 GUID）。**探针** `D:\ds\s\_tmp\v02130`：CLI `--build --install` 真 `dotnet build` **0 错误**；`--recover` **0 条「没认出来」**，8 张卡 / 诅咒 1 / 先古卡 1 / 遗物 1 / 药水 1 的类名·类型·稀有度·费用·效果逐项还原（含被改号的 `CardProbeCard4` 和**跟着改过来的** `SpawnCardId=CardProbeCard4`）；从恢复出来的存档再生成，**11 个卡牌相关文件逐字节一致**） |
| ② | **去掉「卡面显示牌堆张数（{DrawPile}/{DiscardPile}/{ExhaustPile}）」**（中间 ③，用户原话「不需要这个」） | 整块删干净（**五处配套**）：① 界面勾选框（`MainWindow.xaml` 卡牌属性那一栏）；② 模型 `CardSpec.ShowPileCounts` + `_showPileCounts`；③ 生成端 `CSharpCodeGen.CardSource` 里那 3 行 `ForgePileCountVar` 声明 + `CSharpCodeGen.PileCountHints` 占位符表；④ `ForgeGraphGen` 的 `PileCountFile` / `NeedsPileCountVars` / `PileCountSource` / `ForgePileCountVar` 类 + `ModGenerator` 写 `cs/ForgePileCountVar.cs` 那一段；⑤ 「填入参数…」菜单里那三个候选与「先去勾它」那句提示（`MainWindow.xaml.cs`）。**`--uicheck` 那 9 条断言同批改写**（不许留红）：改成「勾选框没了 / 属性没了 / 占位符表没了 / 辅助类三件套没了 / 类型没了 / **老存档带 `"ShowPileCounts": true` 读进来不报错**（真写一份 JSON 跑 `ProfileFactory.Load`）/ 真生成一遍**工程里不再有 `cs/ForgePileCountVar.cs`** / 卡牌源码里一个 `ForgePileCountVar` 都没有 / 菜单里不再列这三个占位符」9 条。**老存档口径**：`ProfileFactory.JsonOpts` 没开 `UnmappedMemberHandling.Disallow` → 未知成员**直接忽略、不报错**，再次保存时字段自然消失；自定义描述里已经写下的三个占位符**原样留着**（本体替换不了，卡面按字面显示），要清只能手动删 —— 文档写清（README / 使用说明） | ✅V0.2.130（同上一行的实测数字） |

> **本轮明确不做**（按用户排期留给后面几版）：中间② 费用与额外资源量量费用（Q3）、右① 升级增量公式、右② 删右侧「升级后费用」（**要和中间新增的两栏一起做**，单独删会让能力暂时消失）、右③ 删 3 个「=X」开关、右④ 重排 UI / 作用对象 7 档 / 概率独立 / 下回合生效全种类（Q4，最大一块）、④ 升级后关键字、⑤ 绿色描述。
>
> **行尾噪声怎么排除**（本机 `core.autocrlf=true`，踩过多次）：直接 `git worktree` 检出的源码行尾是 CRLF，而 `git cat-file blob HEAD:<path>` 给的是 LF —— 生成的 `cs/ForgeFormulaRuntime.cs` 是**裸字符串字面量**，行尾会一路带进产物，SHA 全对不上。做法：`robocopy t3 → _tmp\...\ref`（**继承 t3 当前的行尾约定**）+ 只把这一轮改过的 **8 个文件**用 `git show HEAD:<path>` 的内容**LF→CRLF 归一化**后覆盖 → `dotnet build` 0 错误 → 同一份存档跑两版 CLI 逐文件对拍。⚠ 另外：`edit` 工具会把被编辑的文件整体写成 **LF**（实测 `ForgeGraphGen.cs` 中招，其余 7 个没中），改完必须复核行尾并归一化回 CRLF，否则生成物里所有裸字符串字面量的行尾都会变。
>
> **改卡牌类名牵到的引用面已核过**：`ArtGenerator`（`n.CardClassName(p, c)` 算卡面素材路径与 `.tres`）、`CardPoolSource`、`LocalizationGen`、`PatchesGen`、`NameStabilizer`、`ProfileValidator`、`ForgeGraphGen`（锻造图「目标卡」）全部走 `Naming.CardClassName` / `EmittedCardClass`，**没有按类名硬编码的地方**；动画页（`AnimFramesGen` / `FrameAnimGen` / `AnimTypes` / `GifFrames`）与 `AnimationSpec` 一个字都没碰。

## 第 16 批需求（用户 2026-10-XX 提，按《锻造优化.md》第 3 条；**第 1、2 条已于 V0.2.128 做完、本轮没推翻**）

> 需求原文：`docs\需求\锻造优化.md` 第 3 条「【效果】模块里面每一条效果的选择出现的可填写项与卡牌页处的效果保持一致」。
> 用户拍板：`docs\需求\已拍板决定.md` **Q1=A —— 锻造【效果】模块直接复用卡牌那套 `EffectSpec` 编辑器**
> （不走「给 `ForgeNodeField` 一栏一栏补」那条路，会永远漂）。
> 盘点报告：`D:\ds\s\t3\_tmp\需求盘点_锻造页与卡牌页.md` §2.3 差集表 / §5.4 结论（行号是中间态快照，按实际代码复核过）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ③ | **锻造【效果】模块 = 卡牌那套效果编辑器** | **数据层**：`ForgeNodeSpec` 新增 `EffectSpec`（+ `EffectFromLegacy` / `EffectTitleBound`），新建 `Generation/ForgeEffectBridge.cs` 当桥 —— 老存档（只有 `ForgeNodeField` 那一串老字段）打开时**幂等**迁出一份 `EffectSpec`（`ProfileFactory.Normalize` 里跑，已经迁过的整段跳过），**不回写老字段**；只有**用户在编辑器里改过**（挂 `PropertyChanged` → `EffectFromLegacy=false`）才把 spec 回写成老字段，生成端照旧只读老字段（`ModGenerator.Generate` / `ProfileValidator.Validate` / `EmitEffect` / `Issues` 开头都先 `SyncGraph`）。锻造「数值」那一栏的原文单独存 `EffectSpec.ForgeAmountText`（卡牌那套 `AmountFormula` 只对 5 种生效，锻造图所有种类都能写公式 —— V0.2.128 的能力原样保住）。**界面层**：`App.xaml` 新增 `EffectEditorForge`（= 共用模板 `EffectCommon` + 「可用量」清单 + 说明）；`EffectCommon` 里**只给 4 栏**加 scope 分支（`EffectSpec.IsForgeChain`）——「数值 / 生效次数 / 命中次数 / 概率」在锻造链换成**能填公式的文本框**（保住 V0.2.128），卡牌 / 遗物 / 药水 / 自定义状态四处判据与绑定一字不变（新增的那些元素对它们恒为 Collapsed）；`ForgeCanvasView` 把【效果】模块整块换成模板宿主（`EffectEditorForge`）、模块宽度 210 → 470（`WidthOf(node)`，连线锚点 / 框选命中 / 缩放适应 / 画布尺寸四处同步）、标题跟着效果种类就地刷新（订阅只挂一次）。**生成层**：「作用对象」这一栏**真的有用了** —— 新增 `EffectSpec.TargetSideCustom`（用户改过才接管），优先级 = 显式接在【效果】「指向对象」口上的【对象】 > 改过的「作用对象」 > 老规矩（这张牌选中的目标）；老存档是 false，生成文本逐字不变。**校验器（不许静默）**：`ForgeGraphGen.UnsupportedEffectSettings` 逐栏报错说清「这一栏在节点图里不生效 + 怎么改」（编辑器上有、但节点图不生成的：按范围随机 / 范围限定 / 多选1 / 附加处理 / 加关键词 / 取几张 / 从哪里选牌 / 复制的份数 / 缓慢% / 召唤物 / 升级增量 / =X / =层数 / 条件选项）；「下回合生效」那一档**只警告**（老存档里勾过它，报错会让老存档突然生成不出来）；**没接上的 35 种效果种类**照旧报错拦住（报错措辞改成「换成支持的种类」或「移出锻造页去效果列表配」，因为锻造条目的卡牌页效果栏是锁死的）。**回读 / 文案**：`ProjectRecovery` 本来就不回读节点图（老边界，见「已知未修」），本轮没动；卡面文案 / 图标收集读的都是老字段，跟着回写自动一致 | ✅V0.2.129（`--uicheck` Debug **2953/0**、发布版 **2953/0**（基线 2935/0，新增 18 条）；`dotnet build Sts2CharForge.sln -c Debug --no-incremental` **0 错误**；**老存档逐字不变**：`--samplegen` 112 个文件 **111 个逐文件 SHA256 一致**（只差 `ExampleMod.sln` 的随机 GUID）+ 一份**含锻造图的纯数字老形状存档** 159 个文件 **158 个一致**（同样只差 `.sln`）；另用 `git worktree`（HEAD `7d30159`）跑同一份存档对拍：原始字节 47 个不同**全部是行尾**（`core.autocrlf=true` 把检出的源码行尾改成 CRLF，裸字符串字面量把它带进产物），**按 CRLF→LF 归一后 0 个内容差异**（只差 `.sln`）；探针 `D:\ds\s\_tmp\v02129\cli`：**26 PASS / 0 FAIL**（迁移幂等 + 老字段一个字节不动 + 老模块生成后仍不回写 + 新编辑器各栏 → 回写 → 生成 → 校验器拦未支持项），CLI `--build --install` 真 `dotnet build` **0 错误** + PCK + 安装成功、`--recover` **0 条「没认出来」**） |

## 第 15 批需求（用户 2026-10-XX 提，按《锻造优化.md》第 1、2 条往下做；**第 3 条没动**）

> 需求原文：`docs\需求\锻造优化.md`。
> 用户拍板：`docs\需求\已拍板决定.md`（Q1 决定第 3 条「锻造【效果】模块直接复用卡牌那套 `EffectSpec` 编辑器」——
> 那是大改，**主代理会单开一版**，本轮一个字都没碰；Q2=A = 保留数字框、允许里面直接填公式文本）。
> 盘点报告：`D:\ds\s\t3\_tmp\需求盘点_锻造页与卡牌页.md`（本轮只读参考）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **锻造页 12 个数字框都能填参数 / 公式**（用户原话：「锻造页中所有模块里能填入数值的地方，都能填入参数或者公式」） | 用户选 Q2=A：**保留单行数字框**，里面可以直接填公式。新增 `ForgeNumbers`（解析三档：纯数字 → 生成期常量折叠 → 运行时表达式；失败 = `Ok=false` + 中文原因，**不再静默回落默认值**）+ `ForgeFormula.TryEvalConst`（生成期折叠，卡牌费用那种「要编译期常量」的位置只收前两档）。12 处逐处接上：`GetNumber` 先按公式解析、`UsedParams` 收公式里用到的参数、`EmitEffect`（数值 / 生效次数 `EmitRepeatLoop` / 命中次数 `WithHitCount` / 概率 `Roll`）、`RuntimeCondition`（【条件】的 N / M，顺带补了 `HpInRange` 的运行时判据）、`RelateExpr` 右值、`EmitTimingGate`（血量阈值 / 生效次数写了运行时公式时**挪到序幕之后**，默认档位置文本一个字不变）、`ForgeTriggerGuardLines`（「到达 N 层」运行时表达式）。界面：数字框旁「…」插入参数 / 内置只读量按钮、布顶那行**可用量清单**、填错描红 + 运行时公式标蓝 + ToolTip 写出表达式；积木视图摘要也不再吞掉公式文本。校验器：逐栏报「哪个模块 · 哪一栏」，费用栏写运行时公式 → 错误（本体 `base(cost, …)` 要编译期常量）。**回读**：锻造节点图这一阶段本来就不从工程回读（字段只存在存档里），本轮核实并把边界写进报告 | ✅V0.2.128 |
| ② | **【触发时机】不可被删除 + 新增「卡牌被保留时」** | 锁定照【能量】那套机制：`ForgeCanvasView.IsLocked`（= 【能量】+【触发时机】：删不掉、不被全选带走）+ 新的 `IsPinned`（= 只有【能量】还额外不可选中 / 不可拖动）；`LockedNote` 按模块给说明文案，标题栏不摆「×」，`RemoveSelected` / `RemoveNode` / `SelectAll` / `Rebuild` / `BeginDrag` / `SelectNode` 六处判断一起改（**「一块布只能有一个触发时机」的既有逻辑没动**）。新时机 = `ForgeTimings.CardRetained`（Id `Retained`、Display「这张牌被保留时」、Hook `AfterFlush`，和卡牌页「卡牌时机」那份同名同语义）。生成端：`Signature` 补 `AfterFlush` 分支（不补会落到默认分支生成不存在的签名 → CS0114/CS0115）+ `TimingEntryGuardLines` 写进门守卫 `if (!retainedCards.Contains(this)) return;` + **顺手修掉一个老 bug**：卡牌档挂在「没有 cardPlay 参数」的钩子上（被消耗 / 被丢弃 / 被抽到 / 被保留）时，`__fgTarget = cardPlay.Target` 会 CS0103 —— 现在按生成出来的文本判断，需要时补一句 `CardPlay? cardPlay = null;` 并用 `cardPlay?.Target`。回读：锻造节点图不从工程回读（见 ①），但**顺手修掉**「图里那几段方法体被当成卡牌时机 / 遗物触发时机去解析、平白留下 6 条『没认出来』」的老坑（`ProjectRecovery.ReadForRecovery` 整段跳过带「锻造节点图」标记的方法）。老存档安全：`Plan()` 本来在「图里没有 Trigger」时返回 null（整张图不生成），锁界面不改生成语义 | ✅V0.2.128（`--uicheck` Debug **2933/0**、发布版 **2933/0**（基线 2899/0，新增 34 条断言）；`dotnet build` 0 错误；`--samplegen` 112 个文件里 **111 个逐文件 SHA256 一致**、只差 `ExampleMod.sln` 的随机 GUID；**锻造页老形状存档**（在 `git worktree` 的 HEAD 版本上跑同一份探针存档）160 个文件里 **159 个逐字节一致**、同样只差 `.sln`；探针 `D:\ds\s\_tmp\v02128`：新特性存档 → CLI `--build --install` 真 `dotnet build` **0 错误** + `--recover` **0 条「没认出来」**） |

## 第 14 批需求（用户 2026-10-XX 提，按《遗物、药水优化.md》往下做；**药水页照抄 + 额外资源量页那条**）

> 需求原文：`docs\需求\遗物、药水优化.md` 第 1 条（额外资源量页）+ 第 6 条（药水方面参考遗物的优化方案）。
> 《锻造优化.md》《卡牌页优化.md》两条**本轮没动**（下一批）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **额外资源量/状态页改名为额外资源量页 + 「是否隐藏额外资源量遗物」的选项 + 右侧效果栏**（用户原话：「额外资源量/状态页改名为额外资源量页，增加选择是否隐藏额外资源量遗物的选项。在右侧增加效果栏。」） | 页头 `ExtraResourceTab.Header` 改「额外资源量」，**所有按名字定位页面的代码一起改**（`SelectTabRoot("额外资源量")` 5 处 + 两处选项卡顺序断言的 `case`）；「在游戏中隐藏」勾选框（V0.2.113 就有）核对为在、绑得对、可用；版面重排成 **左 = 额外资源量本体 + 那只载体遗物 / 右 = 效果栏**（效果列表改成 `ItemTemplate="{StaticResource EffectEditorPlain}"`（行内编辑）+ 右下角 `ExtraResourceEffectConditionBox` 绑那条效果的 `Condition.Kind`），效果仍**只有一份**（`Profile.ExtraResource.Effects`）。**顺手修掉两个只在「启用额外资源量 + 有效果」时才炸的老坑**（示例存档的额外资源量是关的，所以一直没暴露）：`ExtraResourceSource` 的 `CanonicalVars =>` 用 `.Open(...)` 多开了一层 `{` 且从没关上（生成的 `cs/ExtraResource.cs` 编译不过，实测一串 CS1002/CS1519）→ 改成照遗物链的 `.Line(...)` 写法；「每场战斗只触发一次」用到的 `_condUsedThisCombat` **压根没声明**（CS0103）→ 用到了才声明 + 跑过置位 + 换战斗复位（复位写进已有的 `AfterCombatEnd` 方法体，不能另开一个同名 override） | ✅V0.2.127 |
| ② | **药水页照抄遗物页**（用户原话：「药水方面参考遗物的优化方案。」） | 照 V0.2.124（序号 / 拖动 / 自动类名 / 稀有度中文 / 右侧效果栏 + 右下角条件）+ V0.2.125（每 5 个一块预留空槽）**逐条搬**：`ISlotRow` 抽成通用接口（`IRelicSlotRow` 保留成别名，遗物页代码一个字不动）、`SlotPlaceholder` 通用空槽行、`PotionRows` 界面镜像集合（`public`，WPF 反射读不到 internal）、`PotionUiSelection` / `HasPotionSelection` 替掉绑 `SelectedItem` 的两处、`PotionRowAtPoint` / `MovePotionTo`、`Naming.RenumberPotionClassNames` + `PotionSpec.Ordinal` / `LastAutoOrdinal`（都 `[JsonIgnore]`，存档面零变化）、`EffectCatalog.PotionRarityOptions`（Id 英文 / Display 中文：常见 / 罕见 / 稀有）。**不抄**：遗物的「触发时机列表 / 关系开关 / 重置范围」（药水没有这个概念）。**顺手补的老缺口**：(a) 药水效果的条件以前「界面没入口 + 校验器一律报错 + 生成端不生成」→ 新增 `CondCtx.Potion` + `EmitPotionEffect` 包 `if` + 校验器改成允许遗物那一档；(b) 药水的「概率生效」「命中次数」勾了不生成且无提示 → 校验器**明确报警告**（不补生成，补了会动老存档）；(c) 回读补上「在游戏中隐藏 / 始终显示计数器 / 数量上限 / 加入药水池 / 额外资源量自定义描述」五项（都用既有规则 / 既有文件，**无新标记**） | ✅V0.2.127（`--uicheck` Debug **2899/0**、发布版 **2899/0**；`dotnet build` 0 错误；改动前/后 `--samplegen` 113 个文件里 **112 个逐文件 SHA256 一致**、只差 `ExampleMod.sln` 的随机 GUID；探针 `_tmp\v02127\cli` 新存档 + 老存档各一份 → CLI `--build --install` 真 `dotnet build` **0 错误 / 0 错误** + `--recover` **0 条「没认出来」**、药水类名重编号 / 入池 / 中文稀有度 / 条件 / 额外资源量 8 项字段逐项还原） |

## 第 13 批需求（用户 2026-10-XX 提，接着遗物页往下做）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **遗物 = 一份效果 + 一组触发时机**（用户原话：「中间的触发时机并不是每条各配效果，而是决定整只遗物的触发时机，只有同时满足或者满足其中一个才能触发遗物的效果，另外效果右侧应能添加多个效果种类。」） | 用户已确认三档：①**整只遗物一个关系开关**——「满足其中一个就触发（**默认**）」/「全部满足才触发」；②选「全部满足」时保留「**重置范围**」下拉（本回合默认 / 本场战斗）；③老存档迁移把各条时机原有的效果**按顺序合并**进遗物效果列表（**完全重复的只留一份**），以前勾过「同时成立」的自动切到「全部满足」、重置范围沿用原值。做法：`RelicSpec.Effects` 恢复成**真字段**（不再代理第 1 条时机）、新增 `RelicSpec.TriggerMode`（Any/All）+ `TriggerModeIsAny/IsAll`（界面单选按钮）+ `ShowAndScope = IsAllTriggers`；`RelicTriggerSpec.Effects` / `RequireAllTriggers` **降级成「只读老存档的遗留字段」**（**不能加 [JsonIgnore]**，否则老存档效果读不进来），`ProfileFactory.NormalizeRelicEffects` 幂等迁移（去重口径 = 紧凑 JSON 字符串相等，见那里的注释）；生成端 Any = 每条时机的钩子跑同一份共享效果、All = 每条打 `__andHit{i}` 标记 +「凑齐跑一次 + 清标记」（Turn 档每段都写「换回合清标记」、Combat 档不写）；回读标记 `// CET:TriggerMode=Any\|All CET:AndScope=…`（写在 `if` **外面**，回读靠它切段 + 认共享效果在哪一段）；老五档 `CET:RequireAll=1` 仍能回读成 All；旧工程 / 极老存档（单时机 + Any）生成文本与描述**逐字不变**（不写新标记）。**顺手修掉两个老坑**：`SplitRelicTriggerSegments` 会把「没有段落标记的那一段」整段丢掉（Any 模式第二条时机回读只剩 1 条）→ 段首补到第一行注释；`RelicTriggerFromBody` 判「这段算不算一条时机」以前只看效果/盯状态/数值/筛选 → 挂钩名一对一的那几个也算 | ✅V0.2.126（`--uicheck` Debug **2850/0**、发布版 **2850/0**；`dotnet build` 0 错误；`ProfileFactory.Sample()` 112 个文件只有 `ExampleMod.sln` 的随机 GUID 不同、其余 **111 逐文件 SHA256 一致**；改前/改后遗物生成文本 + 校验消息 + 遗物描述全部逐字一致；探针 `_tmp\relicshared\cli` 4 只遗物真 `dotnet build` **0 错误** + `--recover` **0 条「没认出来」**、时机条数/字段/关系/共享效果逐项还原） |

## 第 12 批需求（用户 2026-10-XX 提，接着遗物页那条往下做）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | 遗物页左侧序号**按「每块 5 个」预留显示**（原话：「左侧序号直接显示，可以每次多生成五个序号，比如有 3 个选项时，左侧序号显示 1~5，加第 6 个的时候，显示 6~10.」） | 列表里显示的**行数** = `5 × ceil(max(N,1)/5)`（N = 真遗物数）：3 只 → 1~5（4、5 是空槽）；5 只 → 1~5；6 只 → 1~10（7~10 空槽）；11 只 → 1~15。**序号仍是位置号 1..N 连续**，空槽只把当前这一块凑满显示。做法：列表 `ItemsSource` 改绑**界面镜像集合** `MainWindow.RelicRows`（`ObservableCollection<IRelicSlotRow>`）= 真遗物 + 末尾 `RelicSlotPlaceholder`；`IRelicSlotRow`（新，放 Core/Profile）+ `RelicSpec.IsPlaceholder => false`；空槽行模板靠 `DataTrigger IsPlaceholder` 变灰、隐藏遗物名、去掉 ToolTip。**跳过占位项**：`RelicUiSelection`（详情栏 DataContext）/ `HasRelicSelection`（详情栏与「请选择遗物」的显隐）替掉原来绑 `RelicList.SelectedItem` 的两处；`SelectedOf<T>`/`as RelicSpec` 天然过滤；`SelectAll` 显式跳过 `IsPlaceholder`；拖动改成 `RelicRowAtPoint` 给**行号**（空槽也能当落点，`MoveRelicTo(dragged, over, overRowIndex)` 把行号夹到合法范围）；`OnAddRelic` 不再用 `SelectedIndex = Count-1`。`Profile.Relics` 仍是唯一事实来源（占位项只在界面集合里，生成/存档/回读/校验都看不到）。**两个实测坑**：(a) `RelicRows` 必须是 **public** —— WPF `PropertyPath` 读不到 internal 成员，绑 internal 字段时绑定状态直接 `PathError`、`ItemsSource` 永远 null；(b) `RelicRows.Clear()` 会触发 `SelectionChanged → SyncDetail → SyncRelicRows` **重入**，两次调用各按自己那份快照铺集合 → 差一行陈旧（加第 6 只列表还是 5 行），加重入 dirty 循环 + 「挂错存档就重挂」自愈（有几处历史自检直接给 `Profile` 字段赋值，不走 setter）。**自检 8 条**：3 只 → 5 行（含 4/5 空槽、界面上真画出 5 个「N.」、选中空槽不改任何遗物）；6 只 → 10 行；删回 5 只 → 5 行；空槽行点得到但 `Relic = null` + 拖到空槽顺序真变且序号仍 1..N 连续；占位项不进 `Profile.Relics` / 生成结果逐字一致 / 真生成无「空槽」文件；空槽序号实测灰 `#FFAAAAAA`（真遗物 `#FF1F4E79`）+ 全选不含空槽 | ⏳已实现待发版（Debug + 发布版 `--uicheck` 均 **2820 PASS / 0 FAIL**；`dotnet build` 0 错误；`ProfileFactory.Sample()` 生成 112 个文件里 111 个逐文件 SHA256 一致、只有 `ExampleMod.sln` 的随机 GUID 不同） |

## 第 11 批需求（用户 2026-10-XX 提「遗物页现在有问题，先把遗物页优化了给我一版」，一条一版）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | 遗物页：**先修「现在的问题」**，再做「去掉左上角额外资源量那块 + 左侧序号 + 拖动换位置 + 自动类名跟序号走 + 稀有度中文 + 中间底下触发时机列表/自定义描述 + 右侧效果栏 + 右下角单条效果条件」 | **根因**：V0.2.122 把「整块效果编辑器」塞进了 `RelicEffectList` 的 `ItemTemplate`，而 ListBox 默认允许横向滚动 → 虚拟化面板按**无限宽度**量每一行 → 行里那个 `EffectEditorPlain` 的 ScrollViewer 把内容排成自然宽度（实测 **8260px**）→ 每行 507px 高、视口只有 160px + 一条横向滚动条，效果编辑器（含底下的「条件」）被切掉。**修**：`RelicEffectList` / `RelicTriggerList` 加 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`（+ `HorizontalContentAlignment="Stretch"`），行宽夹回 414。**布局重排**：中间 = 属性 + 触发时机列表/该条编辑器 + 自定义描述（两栏）+ 整只遗物条件；右侧 = 效果栏 + 「单条效果的触发条件」；额外资源量那块整块搬回「额外资源量/状态」页底部。**新模型**：`RelicSpec.Ordinal`（`[JsonIgnore]`，位置序号，拖动/增删后重算）、`RelicSpec.LastAutoOrdinal`（`[JsonIgnore]`，自动名对应第几号）、`Naming.RenumberRelicClassNames`（生成前跑：手填的不动、自动名跟序号走、没填的**类名保持留空**只记序号 —— 一写实就再也分不出手打的和工具生成的）、`EffectCatalog.RelicRarityOptions`（Id 英文 / Display 中文）。**自检**：序号 1..N 跟位置变、拖动后顺序真变、自动名跟序号走 + 手填不动 + 幂等、稀有度中文、中间底下触发时机列表/自定义描述、右侧效果栏 + 右下角条件、行宽 <= 列表宽、无 0 尺寸/跑出面板 | ✅ 本轮（`--uicheck` Debug 2806/0、发布版 2806/0 FAIL；`dotnet build` 0 错误；`ProfileFactory.Sample()` 生成 114 个文件只有 `.sln` 的随机 GUID 不同、其余逐文件 SHA256 一致）。**药水页照抄 + 额外资源量页那条已做 → 见第 14 批 ✅V0.2.127** |

## 第 10 批需求（用户 2026-10-09 提，一条一版）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | 「在哪选择多个触发时机，我怎么看不到，能不能在遗物页底下新增触发时机列表，并且可以添加多个触发时机」 | 用户已选：**每条时机各自配自己的效果** + **也要能勾「要和其他时机同时成立」**（都和「自定义状态」页同一套）。做法：`RelicTriggerSpec`（Kind / PowerId / Amount / Scope / CardFilter / RequireAllTriggers / Effects + 界面派生属性）+ `RelicSpec.Triggers` 列表（`First` 按需造一条；`Trigger` / `Effects` / `TriggerPowerId` / `TriggerAmount` / `TriggerScope` / `TriggerCardFilter` 全变成**第 1 条时机的代理** → 老存档逐字不变）；生成端**按钩子分组**（同一钩子多条时机放进一个 override，各自包守卫，避免 CS0111）+ 同时成立标记；回读扫类里所有钩子各成一条时机；校验器 / 描述文案 / 图标收集都遍历所有时机；遗物页照「自定义状态」页那套加时机列表 + 编辑器 | ⏳已实现待发版（`_tmp\relicmulti` 55/0、`_tmp\relicmulti_cli` 真 build 0 错误 + 回读 0「没认出来」、`--uicheck` 2776/0 FAIL） |

## 第 9 批需求（用户 2025 一次列 4 条）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ② | 效果种类选「放置卡牌」时编辑器底下全都无法选择和填写 | **根因**：`EffectCommon` 那块 Grid 只声明了 15 行（0~14），而「召唤物」在 Row=15、「放到哪一摞」在 Row=16 —— 越界行被 WPF **夹到最后一行**，几块控件叠在同一格互相挡住。修：行定义补到 17 行；自检加「两行 Y 坐标必须不同」 | ✅V0.2.119 |
| ③ | 用户找不到「多个触发时机同时成立」在哪勾 | 它在「自定义状态」页那条时机的编辑器里；在**时机列表正下方**加一句指路的话，自检加「勾选框可见可用 + 勾 2 条时『重置范围』看得见」 | ✅V0.2.119 |
| ① | 「临时增益」不生效（参考本体「尖啸」PiercingWail / TemporaryStrengthPower） | 生成端已照本体那套写（BeforeApplied 静默施加真状态 / AfterPowerAmountChanged 补增量 / AfterSideTurnEnd Remove+还原）；✅V0.2.120：`ITemporaryPower`（`OriginModel` / `InternallyAppliedPower`）生成端早已补上，本版用探针端到端验证 —— `_tmp\tempprobe` 生成 5 个临时状态类，对着**真 `sts2.dll`**（与游戏目录 DLL 同源）`dotnet build` **0 错误**；四种档位（正数 / 负数且本体允许负层数 → 照尖啸直接施加 / 负数且本体不允许 → `GetPower`+`ModifyAmount` / 「下回合开始才撤」）在卡牌 / 遗物 / 药水三条链都生成正确，`--recover` 六条效果逐字段一致、TempPower 相关「没认出来」0 条；「数值 0」「没选状态」两条都是**错误**级拦住并说清原因 |
| ④ | 遗物的触发时机「只对哪类牌生效」没有范围（要和状态侧同步）+ 锻造页【触发时机】落后要全部同步 | ✅V0.2.120：**前半段**遗物页加 `RelicSpec.TriggerCardFilter`（同一套候选：任意/攻击/技能/能力/诅咒/状态/关键词组/组）+ 生成守卫 + `// CET:CardFilter=` 回读标记（老四档没标记，靠 `cardPlay.Card.Type != CardType.X` 那句守卫反推）+ 遗物描述文案（「打出一张牌后（只对攻击牌生效）：…」，选「任意」一个字不加）+ 校验器（空组 → 错误；时机不对 → 提示不生效）+ 界面（遗物属性里、触发时机下拉正下方）；**后半段**锻造页遗物档从 11 条补齐到 29 条（与遗物页双向相等）+ 2 条专属 = 31 条，签名/归属守卫都和遗物页**同一份**（`RelicTriggerSignature` / `ForgeTriggerGuardLines`），没 choiceContext 的自己造（造不出来的由校验器报错拦住），【触发时机】模块加「盯哪个状态 / 到达 N 层」两栏；顺带修 4 个既有生成 bug（抽牌调不存在的 `CardPileCmd.DrawCards`、两只点击遗物 `__fgRelic` 重名 CS0128、遗物图里 `base.CombatState` 不存在、生效次数填 0 时 `__fgUses >= 0` 恒成立导致图从不执行）。验收：`--uicheck` 2756/0 FAIL、三个工程 0 错误、默认输出 25 个文件哈希逐字一致、探针 `_tmp\relicfilterprobe` 与 `_tmp\forge_menu` 真编译 0 错误 + 回读 0「没认出来」 |

## 第 8 批需求（用户一次列 9 条 → 拆成 11 项，2025）—— **一条做一版**

> 用户原话 + 已确认的做法都在这里；做完把该项状态改成 ✅V<版本>。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | 锻造页【能量】填的费用没反映到卡面 / 游戏（用户猜是「模块没连入」） | **不是连线问题**：`ForgeGraphGen` 以前只在打出时 `PlayerCmd.LoseEnergy` / `SetStars` 扣一次，从没声明成卡牌费用。现在能量 → 构造函数 `base(N,…)`、额外资源量 → `CanonicalStarCost`、两块布不同 → `OnUpgrade` 里 `EnergyCost.UpgradeBy(差值)`；打出时不再扣第二次；X/Y 数值不变（本体是先 `SpendResources` 再 `OnPlay`）；费用为 0 的老写法逐字保留 | ✅V0.2.108 |
| ② | 触发时机增加「移除某状态时」 | 本体**没有**全局的「状态被移除」钩子（`PowerModel.AfterRemoved` 是那个状态自己的实例钩子；`AfterPowerAmountChanged` 只覆盖层数扣到 0 那一半，`PowerCmd.Remove` 直接清除不走它）→ 生成 `cs/PowerRemoval.cs`：后缀补丁打在本体所有移除的唯一出口 `PowerModel.RemoveInternal()` 上，转发给「该玩家的遗物 + 该生物身上还留着的自定义状态 + 被移除的那个状态自己」；遗物 / 状态两侧生成的都是普通 `public async Task OnForgePowerRemovedAsync(PowerModel power)`（不是 override），`TaskHelper.RunSafely` 起安全任务；回读靠 `// CET:PowerWatch=<类名>` | ✅V0.2.109 |
| ③ | 额外资源量那两只隐藏遗物在遗物页看不到 + 要能勾「在游戏中隐藏」 | 用户已选：遗物页列出来（只读）+ 勾选。实际做法：那块 GroupBox 里补上「类名 + 稀有度 + 不在下面的列表里」和勾选框 `ExtraResourceSpec.HideInGame`（默认 false = 老样子，红线保住）；勾上时生成 `cs/HideCarrierRelic.cs` = 给本体 `NRelicInventory.Add`（private，签名 `(RelicModel relic, bool startsShown, int index)`，遗物栏就是遍历 `Player.Relics` 建的、没有可见性判断）打前缀补丁 `return relic is not <角色>ExtraResourceRelic;` —— 遗物照旧挂在角色身上，发放 / 继承 / 存档全兼容；顺带在文档里写清载体必须留在遗物池（`RelicModel.Pool` 是 First 查找）但 Starter 不会被当奖励发 | ✅V0.2.113 |
| ④ | 命中次数公式不生效（`2+A`，A = 身上易伤层数）：实际只打一次、卡面也没次数 | **根因**：指定敌人 / 全体敌人两条伤害分支只判 `RepeatCount > 1`（写公式时固定值还是 1）→ 整条 `.WithHitCount` 都不生成。修：新增 `CSharpCodeGen.RepeatIsMulti(e, useX)`（把「写了公式」也算进去），4 处 `multi` 判据全走它；卡面那边本来就印公式，所以修完两边终于一致。随机敌人那条一直是好的 | ✅V0.2.110 |
| ⑤ | 公式在卡面上对数值的描述有问题 | 用户选：「卡面直接印 M/2 玩家看不懂，要算出来的数字」→ 生成 `cs/FormulaVars.cs`：5 种支持公式的效果各一个动态变量子类（Damage/Block/HpLoss/Heal/`ForgeFormulaPowerVar<T>`），`UpdateCardPreview` 里**先** `BaseValue = 公式(card, target)` **再**走 `base`（力量 / 敏捷加成照旧叠）；卡面改回 `{X:diff()}` 占位符（游戏填现算数字）；参数在 lambda 里内联展开；变量名与本体一致（占位符 / 升级增量 / 回读都不用改）；没用公式不生成该文件 | ✅V0.2.111 |
| ⑥ | 生成器效果**去掉「数值」框**，只留公式 | 用户已选：全部编辑器都去掉、公式留空 = 0。实际做法：`EffectSpec.ShowAmountBox = !(FormulaSupports(Kind) && IsCardChain)` —— 只有**卡牌链上那 5 种**才收起来（公式只做在卡牌上；遗物 / 药水 / 状态那儿收起来就没法填了，其余种类也没有公式）；`IsCardChain` 由界面在卡牌效果选中时设置（`OnCardEffectSelectionChanged`，不写存档）；老存档在 `ProfileFactory.Load` 里把卡牌那 5 种的**正数**固定值迁成公式（负数「失去格挡 / 扣层数」公式表达不了，留在固定值上）；顺手修了生成后自检不认 `ForgeFormula*Var` 导致迁移后整次生成失败的 bug | ✅V0.2.112 |
| ⑦ | 卡牌种类用「无色牌」时卡面丢失 | **根因**：本体 `CardModel.PortraitPngPath/PortraitPath` 按 `Pool.Title.ToLowerInvariant()` 找卡面，而无色卡登记进的是本体无色卡池（`ColorlessCardPool.Title = "colorless"`）→ 卡面素材必须写到 `images/packed/card_portraits/colorless/` 与 `images/atlases/card_atlas.sprites/colorless/`；`ArtGenerator` 以前一律写在自己的池目录 → 游戏找不到 = 卡面无。修：`poolTitle = c.IsColorless ? "colorless" : n.PoolTitle`（png / tres / tres 里的 ext_resource 三处一起改） | ✅V0.2.114 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | 用户要求：`SelectPiles` 加一档 `Top` = 抽牌堆**最上面那张**（本体 `CardPile.Cards` 索引 0 就是顶：抽牌取 `FirstOrDefault()`、预见取前 N 个、MoveToTop 是 Insert(0)）；八种「选牌」效果（消耗/丢弃/复制/升级/变化/重放/给关键词/回合结束打出）在 Top 这一档**不弹选牌界面、也不随机**，直接 `PileType.Draw.GetPile(...).Cards.FirstOrDefault()` / `.Take(N)`；`PileExpr` 把 Top 映射成抽牌堆（⚠ 给关键词那边原来写的是 `PileType.{SelectPile}`，Top 会生成非法类型名）；回读：自包含效果靠 `CET:Pile=Top`，无标记的三条（消耗/丢弃/变化）靠新增的 `PileFromSelectCode` 规则（`PileType.Draw.GetPile` 与 `FirstOrDefault()`/`.Take(` **同一行**才算牌顶）；校验器「复制卡牌只能从战斗牌堆」白名单收进 Top | ✅V0.2.115 |
| ⑨ | 新效果「放置卡牌」 | 用户已确认：数值 0 = 自己，否则从五处挑一张放到五处。实现：`EffectKindOption("PlaceCard")` + `EffectSpec.PlaceTo`（`EffectCatalog.PlacePiles` = 手牌/抽牌堆底/弃牌堆/消耗牌堆/牌顶）+ `CSharpCodeGen.EmitPlaceCard`（0 → `CardPileCmd.Add(this, PileType.X, CardPilePosition.Y)`；≥1 → `CardSelectCmd` / 随机 / 牌顶 + `Add(cards, …)`）；卡牌/遗物/药水三条链都接（状态链不支持，走默认 Warn）；标记 `CET:PlaceTo=` + 回读（跳过名单补 `__place` 与 `this` 那条 Add）；卡面文案（0 = 「把这张牌放到X。」）；校验器（非卡牌链填 0 → 错误、卡牌填 0 → 提示）；界面「放到哪一摞」下拉 | ✅V0.2.116 |
| ⑩ | 同一状态的几条触发时机可以勾「要和其他时机同时成立」 | 用户已选：同一状态的几条时机可勾 + 选重置范围（本回合 / 本场战斗）。实现：`PowerTriggerSpec.RequireAllTriggers`（界面勾选框 + 回读标记 `// CET:RequireAll=1 CET:AndScope=`）+ `CustomPowerSpec.AndScope/AndTriggers/ShowAndScope`（重置范围挂在整只状态上，触发时机通过 `OwnerPower` 转发，界面在选中时机时点一下）；生成端在 `CustomPowerGen.EmitItemsWithAmountGate` 里一条条包门（`__andHit{i}` 标记 + 「都齐了才跑 + 跑完清标记」，**不用 return** —— 同一个钩子方法里还有别的时机，return 会把它们跳过）；「本回合」按 `TurnNumber` 自动清零，「本场战斗」不需要（状态本来就是每场一个新实例）；只勾 1 条 → 校验器提示不生效 | ✅V0.2.117 |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 用户要求。实现：`PowerTriggers.CardFiltersFor(p)` = 「任意」+ `EffectCatalog.SpawnFiltersFor(p)`（**和卡牌那边的「范围界定」共用同一份选项表与 `ScopeCondForFilter` 算法**）→ 新增 诅咒 / 状态 / 关键词:XXX / 组:XXX；界面 ComboBox 换成 `CardFilterOptions`（Display/SelectedValue）；生成端老四档判据**逐字不变**、新档用同一套谓词并写 `// CET:CardFilter=` 供回读；校验器：组里一张牌都没有 → 错误拦住；顺手修掉「一个方法里多条『打出一张牌后』回读只认第一条、其余悄悄丢」的老坑（`SplitCardPlayedBody` 按筛选切段，每段一条时机各自解析效果，头部守卫行不再多出一条空时机，只有一段时走老路径） | ✅V0.2.118 |


> 这份文件是**跨轮次的耐久记忆**：每轮开始先读它，做完一项就把该项挪到「已完成」并写清版本号。
> 每项的验收红线（用户长期要求）：① 默认配置生成结果逐字不变；② 三个工程 `dotnet build` 0 错误；
> ③ `--uicheck` 0 FAIL（发布版也要跑一遍）；④ 探针「生成 → 真 `dotnet build` → `--recover`」0 条「没认出来」；
> ⑤ 五处配套（生成 + 回读 + 卡面/文案 + 校验器 + 界面）+ 文档（README / tools/使用说明.template.txt / docs/RELEASE_NOTES_V<新版本>.md）；
> ⑥ 打包：`powershell -NoProfile -ExecutionPolicy Bypass -File D:\ds\s\t3\tools\release_all.ps1 -Bump patch -NoRelease -Yes`；
> ⑦ 禁区：`AnimFramesGen.cs` / `GifFrames.cs` / `AnimTypes.cs` / `FrameAnimGen.cs`、`MainWindow.xaml` 动画页 + `AnimationSpec`、刮削 `Scrape` 的逻辑不要动；子代理不许 git add/commit。
> ⑧ **用户数据禁区（2026-10-10 用户两次明确要求：「以后不需要处理我的存档」「我存档和游戏目录我会自己处理」）**：
>    · 用户存档目录 `D:\ds\s\4\自定义角色存档\`：**不要读、不要复制、不要拿去当 `--profile` 输入**（连只读都不做）——用户自己管；
>    · 用户游戏安装目录（`d:\download\steam\steamapps\common\Slay the Spire 2\mods`、`C:\Program Files (x86)\Steam\...`）：**一个字都不许写、也不要去核对/盘点**；
>    · **一切验证只用 `ProfileFactory.Sample()` 与 `_tmp` 下自造的探针存档**；「真实存档回归」这类结论交给用户自己跑，不要替他做。
>    · **探针/验证只用 `--build`，不要用 `--install`**；`--install` 只在你**显式**想装到某个 `_tmp` 目录时才用（V0.2.137 起 CLI 不给 `--install` 就绝不安装 —— 此前两次误装都源于「不给就退回用存档里的 `InstallDir`」这条兜底）。
> 探针目录约定：`D:\ds\s\t3\_tmp\<name>`，csproj 要带 `<Compile Remove="out/**">`；uicheck 运行方式：
> `TEMP=TMP=D:\ds\s\_tmp\apppub_tmp <AppBase>\Sts2CharForge.exe --uicheck`，结果读 `<AppBase>\uicheck_result.txt`（UTF8）。
> ⚠ 自动化里**不要**用 `Start-Process` / `start` 起发布出来的 exe：未签名 exe 走 ShellExecute 会被
> Windows 的「打开文件 - 安全警告（无法验证发布者）」框卡住（实测卡死过一条自检命令）。
> 直接 `& $exe --uicheck`（走 CreateProcess，不弹框）—— 见 `README.md`「双击时弹『打开文件 - 安全警告』」。
> ⚠ **改完 `tools\*.ps1` 必须确认 BOM 还在**：本项目的 .ps1（还有 `使用说明.txt`）是 **UTF-8 带 BOM**，
> 而编辑器/补丁工具常常把 BOM 写掉 —— 无 BOM 的 UTF-8 会被 PowerShell 5.1 按 GBK 读，脚本**连解析都过不去**
>（实测：V0.2.120 打包时 `release_all.ps1` 就是这么挂的，报一堆乱码语法错）。补回来的办法：
> `$t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8); [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))`
> （`.cs` / `.md` / `README.md` 本来就是无 BOM，别给它们加）。
> ⚠ **每轮开工前 / 发版前先跑 `tools\backup_src.ps1`**（源码快照 + 编码体检，快照进 `..\_备份\源码\`，
> 默认留 30 份）。2026-10-08 那次「MainWindow.xaml.cs 被按错代码页重写」之所以难救，就是因为
> **68 个版本没提交过 git、手边没有干净副本**：现在 `release_all.ps1` 会自动先跑这一步，
> 体检命中（非法 UTF-8 / U+FFFD / PUA）会**直接中止发版**。
> 同一版起 `release_all.ps1` **发布完默认自动把源码提交到本地 git**（`发版 V<版本>（版本号 + 发版说明）`），
> 默认**不推送**（要推加 `-Push`，不想提交加 `-NoCommit`）—— 「每个版本一条提交、随时能回退」是默认行为，
> 别再用「只本地提交、忘了 commit」那种做法（V0.2.52~V0.2.120 那 68 个版本就是这么攒起来、最后一次性补的）。

## 已知未修（V0.2.120 记录，待定）

**~~「额外资源量遗物·在游戏中隐藏」会让游戏卡死~~**（V0.2.113 引入，潜伏到 V0.2.137；**✅V0.2.138 已修**，见「第 25 批需求」）：
V0.2.113 起给 `NRelicInventory.Add` 打的隐藏补丁是**前缀补丁**（遇到载体遗物就 `return false` 跳过原方法），
于是遗物栏的节点列表 `_relicNodes` 比 `Player.Relics` **少一项**；之后再获得任何遗物时，本体
`OnRelicObtained` 用 `Player.Relics.IndexOf(relic)` 算出来的 `index` 往这个短列表里 `Insert` 就**越界**
（`ArgumentOutOfRangeException`，异步任务死掉 = 玩家眼里**卡死**，用户真游戏日志命中的是「先古事件选遗物」）。
为什么老探针没抓到：以前的探针只验「生成 + 编译」，而这种越界只在**运行期**、且要「勾了隐藏 + 之后真的再拿到一个遗物」才触发。
V0.2.138 的修法：**后缀补丁 + 纯视觉隐藏**（`Add` 原方法一个字不改地照跑，跑完才 `Visible = false`）；
文件里**只有这一个补丁类**（实现中途多挂的那个 `AnimateRelic` 后缀是空操作 —— 后缀里的 `return` 只结束它自己、
原方法早跑完了 —— 已在同一版里删掉）；并由「生成端源码形状自检 + 校验器 + 真编译」三处一起钉住
「**绝不跳过 `Add` 的原方法**」这条不变量。

**用户报「命中次数填 `2+A`，游戏里只实现 A 次」—— 代码侧没查出问题，等现场信息（V0.2.121 记录）**：
- 已核过：那张牌（`SparkleCard45`，伤害 5 / 命中次数 `2+A`）生成的是
  `.WithHitCount((int)System.Math.Clamp(((2m) + __pA), 1m, 20m))`，和本体多段攻击同一套写法；
  `A` = 自己的最大能量数（`__pA = (__fgPlayer?.MaxEnergy ?? 0m)`）绑对；
  卡面文案是「…造成 {Damage:diff()} 点伤害 2+A 次。」；游戏里 `mods\SparkleMod.dll` 与工程
  `build\SparkleMod.dll` **SHA256 相同**（跑的就是这段代码）→ 生成端没丢那个 +2。
- 待用户确认三条（任一）：① 那场战斗的最大能量数 / 实际几下；② 把命中次数改成**固定数字**（如 5）打一次对不对
  （分开「公式路径」与「命中次数本身」）；③ 是怎么数「几下」的（卡面 / 伤害段数 / 伤害统计 mod）。
- 另注意：敌人有**格挡**时前几下的伤害会被吃掉，可能把「5 下」看成「3 下」。

**锻造图用「当遗物被点击时 / 血量低于 N」+「仅生效 N 次（N > 0）」时会 CS0111**（生成不出模组）：
遗物自己的触发时机是**每回合开始时 / 战斗胜利时**时，`TimingFields` 会为「生效次数的复位」生成一个
override，而遗物自己那条触发时机也生成同一个 override —— 防重名逻辑只比了**图那条钩子**，没比遗物自己那条，
于是同一个类里两个同名方法。
- 触及条件（三条同时）：① 遗物自己的【触发时机】= 每回合开始时（或战斗胜利时）；② 节点图里用了
  「当遗物被点击时 / 血量低于 N」；③ 那张图的「仅生效 N 次」填了 **> 0** 的数。
- 规避：把「仅生效次数」留 0（无限次），或把图挪到别的触发时机上。
- 修法会动既有生成行为（要给复位 override 起个不撞的名字 / 把判据扩到遗物自己那条钩子），
  需要单独一版 + 回归，所以本版只记录、没动。

**~~锻造节点图（`ForgeGraph`）不能被「从工程恢复」还原~~**（V0.2.128 记录 → **✅V0.2.134 已修**，见「第 21 批需求」）：
`ProjectRecovery` 从 V0.2.103 起只回读「效果列表 + 卡牌/遗物/药水的时机与描述」，**从来不认锻造节点图** ——
工程里只有编译结果，节点图本身只存在存档里。V0.2.128 的探针实测：源存档 18 张卡里 4 张 `IsForged=true` 且
`ForgeGraph` 非空；`--recover` 出来的 18 张卡 **`IsForged` 全是 false、`ForgeGraph` 全空**（V0.2.127 及以前同样如此，不是新引入）。
- V0.2.128 只顺手修掉附带假报警：图体代码以前被当成「卡牌时机 / 遗物触发时机」解析 → 平白 6 条「没认出来」，现在整段跳过（0 条）。
- **V0.2.134 已真支持**：生成端写一行 `// CET:ForgeGraph=1:<Base64(紧凑 JSON)>`（**只在图非空时写**），
  回读端按它重建整张图（含升级后那块布）并置 `IsForged=true`；老工程（没有这行）照旧 0 条「没认出来」、只是图仍空。
  **代价**：含锻造图的存档生成结果会多出这行标记（每个条目每块布一行，10~40 KB），
  用户真实存档实测 `DarkVinci` 6 张卡 / `SparkleMod` 2 张卡命中；**没有锻造图的存档逐字节不变**（`--samplegen` 111/112 逐字节一致，只差 `.sln` 随机 GUID）。
  验收：真实存档 `DarkVinci` 往返逐字段对照 **42 个字段块 / 不一致 0**；干净探针「生成 → 恢复 → 再生成」**64 个文件 / 差异 0**。

**「数值 0 效果」的升级增量回读认不出来**（V0.2.131 在**用户真实存档**上实测发现；小而具体，建议单开一小版修）：
现象 —— 从工程恢复时报 `⚠ 有 1 处生成代码没认出来：<卡类名>: 升级增量找不到对应效果（变量 Cards2）`。
根因（已定位到具体文件与行）—— 用户存档 `DarkVinci.json` 的卡「染色」有两条效果：`GenerateCard Amount=1` 与
**`Draw Amount=0 UpgradeAmount=1`**（数值 0、靠升级增量才有值）。生成端对这种效果会**额外造一个合成动态变量**：
`DarkVinciDyeing.cs` 里 `new CardsVar("Cards2", 0)` + `OnUpgrade()` → `base.DynamicVars["Cards2"].UpgradeValueBy(1m);`。
`ProjectRecovery` 是靠**变量名**把 `UpgradeValueBy` 映射回某条效果的，而 `Cards2` 不属于任何一条效果自己声明的变量
（它是为「数值 0」造的合成变量）→ 映射失败，这条升级增量还原不出来。
- 修法（二选一）：① 回读时把 `<Kind>Var<数字>` 形状的合成变量按**声明顺序**归给「数值 0 的那条效果」；② 生成端在那个合成变量旁写一个标记注释（如 `// CET:SyntheticVar=<效果序号>`），回读按标记认。
- 影响面：只在「数值 0 + 填了升级增量」的效果上；用户真实存档已实际命中（`DarkVinci` 1 处；`SparkleMod` 0 处）。


## ① 「下回合生效」补齐生成 / 变化 / 复制 / 全局加牌 —— **已完成（V0.2.88 ~ V0.2.92）**

**已完成**：生成卡牌 / 复制卡牌 / **变化卡牌（战斗内）** / 获得卡牌（全局）/ 变化卡牌（全局）/ 删除卡牌（全局）/
升级牌组（全局）（`CSharpCodeGen.IsDelayedSpawnKind` + `CollectDelayedEffects` + `DelayedPowersSource` 里的生成类分支 +
`Naming.DelayedPowerSuffix` 的种类前缀 + `EmitCardEffectOnce` 顶部的延迟分支（写 `SpawnMarker` 标记行）+
`ProjectRecovery.DelayedSpawnStem`（类名尾巴还原卡名、标记行还原张数/范围）+ 校验器（只允许卡牌 + 不支持的种类报错））。
四个发射器都做了参数化：`EmitGenerateCard(playerExpr/creatureExpr/playerArg)` / `EmitCopyCard(playerExpr/creatureExpr/sourceExpr)` /
`EmitTransformCard(playerExpr/creatureExpr/sourceExpr/combatStateExpr)` / `EmitGlobalCardEffect(..., playerExpr)`
（`combatStateExpr` 默认 `base.CombatState`，卡牌链里生成的字符一个都没变）。
探针：`_tmp\selfreturnprobe`（`RunDelayedSpawn`）→ 生成 → 真 `dotnet build` 0 错误 → 回读
`NextTurn=true` + 卡名 + 张数 + 范围/附加处理全对（含 `ProbeDelayedTrans` 变化卡牌）。

**顺手补的（用户新提，V0.2.89）**：「生成卡牌 + 按范围随机」多了「**从范围里自己挑一张**」勾选框
（`EffectSpec.SpawnChosen` + `ShowSpawnChosen` + `CET:Chosen=1` 标记 + `EmitGenerateCard` 里那段
`__chosenCand`（≤3 走 `FromChooseACardScreen`，>3 走 `FromSimpleGrid`，整段用 `SelfContainedMarker` 收尾）+
回读跳过名单 `__chosenCand`）。⚠ 新变量名不要和「多选1」的 `__candidates` 重名（会让「多选1」的回读失效）。

**现状**：真正实现的只有五种（格挡 `BlockNextTurnPower` / 抽牌 `DrawCardsNextTurnPower` /
能量 `EnergyNextTurnPower`（负数走生成的欠费状态）/ 额外资源量 `StarNextTurnPower` /
施加扣除状态 `ForgeDelayed<状态>`）；判据是 `EffectCatalog.HonorsNextTurn(kind)`（V0.2.87 加的），
界面只给这五种显示勾选框，校验器（`ProfileValidator.cs` 里 `e.NextTurn && !EffectCatalog.HonorsNextTurn`）会拦别的。

**要做**：
1. 复用延迟链：`CSharpCodeGen.CollectDelayedEffects(p)`（约 6151 行，现在只收 `ApplyPower && NextTurn`）+
   `DelayedPowersSource(p)`（约 6561 行）+ `DelayedPowerClassName(e, owner)`（约 1527 行）。
   让它也能收「生成卡牌 / 变化卡牌 / 复制卡牌 / 获得卡牌（全局）/ 变化卡牌（全局）」这几种 `NextTurn`。
2. 生成的延迟状态在 `AfterPlayerTurnStart`（或本体现在那条钩子）里调用**现成的**发射器：
   生成卡牌 = `EmitGenerateCard`（`AddToCombatAndPreview` / `CardFactory.GetDistinctForCombat` + 「池子空 / 候选不足不卡死」守卫）；
   变化 = `EmitTransformCard`；复制 = `EmitCopyCard`；全局加牌 = `EmitGlobalCardEffect`。
   层数 = 张数（和 `ForgeDelayed<状态>` 同一套：`base.Amount` 当数量，运行完自毁）。
   ⚠ 这些发射器都要求 `choiceContext`：钩子里没有就按「获得时」那套造一个
   （`BlockingPlayerChoiceContext` / `HookPlayerChoiceContext`，见 `EmitChoiceContextBootstrap` 与 `CanBootstrapContext`）。
3. `EffectCatalog.HonorsNextTurn` 把这几种加进去（界面勾选框自动跟着出现）；
4. 回读：延迟状态类里写 `// CET:DelayedKind=<Kind>`（或复用 `DelayedPowerReverseForCheck` 那类反查函数，
   现有 `ProjectRecovery.TempPowerReverseForCheck` / `TempPowerIsNegativeForCheck` 是同一模式的先例）；
5. 校验器：把「路径不支持」的那条错误里「生成卡牌…还在做」的措辞改掉；
6. uicheck：勾上之后生成的延迟状态类存在、钩子签名对、回读后 `NextTurn==true` 且种类一致。

## ② 额外资源量隐藏遗物 → 「遗物」页的界面

**已完成（V0.2.85）**：`ExtraResourceSpec.Effects`（存档字段）+
`CSharpCodeGen.ExtraResourceSource` 里生成 `CanonicalVars` + `BeforeSideTurnStart(choiceContext, …)` 钩子跑这些效果 +
`ProjectRecovery.ParseExtraResource(..., nameToPowerId)` 从 `cs/ExtraResource.cs` 的 `BeforeSideTurnStart` 体里读回。

**还缺界面**：
- 在 `MainWindow.xaml` 的 `<TabItem Header="遗物">` 左栏（`DockPanel Grid.Column="0"`）里，给 `RelicList` **上面**加一块
  `DockPanel.Dock="Top"` 的 GroupBox：「额外资源量遗物（隐藏起始遗物）」+ 一句说明 + 一个矮一点的 `ListBox x:Name="ExtraResourceEffectList"`
  （`ItemsSource="{Binding Profile.ExtraResource.Effects}"`、`DisplayMemberPath="DisplayPlain"`）
  + 「添加效果 / 删除效果」按钮（`Click="OnAddEffect"/"OnRemoveEffect"`、`Tag="{Binding ElementName=ExtraResourceEffectList}"`）
  + 「撤回删除」（`OnUndoCard`）；
  容器 `DataContext="{Binding Profile.ExtraResource}"`（`OnAddEffect` 走 `listBox.DataContext` → `EffectsOf(obj)` 反射找 `Effects`）。
  整块的 `Visibility` 绑一个新属性 `ExtraResourceRelicVisible`（= `Profile.ExtraResource.Enabled`），
  并在 `RefreshAll()` / 切换页签时 `Raise` 它。
- 右侧详情：复用现成的效果编辑模板（照 `RelicEffectList` 的 `<Border …><ContentControl Content="{Binding}" ContentTemplate="{StaticResource EffectEditorPlain}"/></Border>`）。
- `ProfileValidator`：资源没启用但 `ExtraResource.Effects` 非空 → 提示「这些效果不会生成」；启用时给一条「这只遗物是 Starter、不占遗物栏位置」的说明。
- uicheck：`Profile.ExtraResource.Enabled=true` 时这块可见；效果列表绑定到 `ExtraResource.Effects`；生成 → 回读一致。

## ② 额外资源量隐藏遗物 → 「遗物」页的界面 —— **已完成（V0.2.91）**

遗物页左栏新增「额外资源量遗物（自动生成的隐藏起始遗物）」块：效果列表
（`ExtraResourceEffectList`，`DataContext = Profile.ExtraResource`、`ItemsSource = Effects`）+
添加/删除/撤回 + 右侧复用 `EffectEditorPlain` 的编辑区（选中它的效果时把遗物列表的选择清掉、
并把 `RelicEmptyHint` 用 `ClearValue(VisibilityProperty)` 交回绑定）；显隐绑 `ExtraResourceRelicVisible`
（= `Profile.ExtraResource.Enabled`，在 `RefreshAll` 里 Raise）。校验器加了「没启用资源量却留着效果」的警告。
自检 4 条断言（显隐 / 列表绑定 / 隐藏 / 警告）。生成端与回读在 V0.2.85 已完成。

## ③ 无色卡 —— **已完成（V0.2.92）**

**研究结论（已核对本体）**：`CardModel.Pool` 是**反查**——`ModelDb.AllCardPools.FirstOrDefault(p => p.AllCardIds.Contains(Id))`，
不在任何池里会抛 `InvalidProgramException`；`FrameMaterial => VisualCardPool.FrameMaterial`，
无色池 = `ColorlessCardPool`（`card_frame_colorless` / `EnergyColorName="colorless"` / `DeckEntryCardColor=A3A3A3FF` / `IsColorless=true`）。
mod 加无色卡的唯一入口：**游戏初始化之前**调用 `ModHelper.AddModelToPool(poolType, cardType)`
（定义在 `src/Core/Modding/ModHelper.cs:49-69`，晚了会抛 "it's too late"）。

**做了**：`CardSpec.IsColorless` + `CSharpCodeGen.CardPoolSource` 把无色卡排除出本角色池 +
`PatchesGen.ModEntrySource` 在 `Initialize()` 里写 `AddModelToPool(__colorlessPool, __t<卡类>)`（包 try/catch、按类名反射）+
校验器（Ancient/Event/Quest/Token 报错 + 两条提示）+ 界面勾选框 + `ProjectRecovery` 扫 `cs/ModEntry.cs` 回读 +
README / 使用说明 / `RELEASE_NOTES_V0.2.92.md` + 自检 8 条断言（含生成 → 真编译 → 回读，0 条「没认出来」）。
⚠ 那句话是「卡池成员一律在自己池类的 `GenerateAllXxx()` 里给出」的**唯一例外**，`cs/ModEntry.cs` 里写了原因。

## ④ 诅咒 / 先古卡页 → 诅咒 / 先古 / 状态（三类都能调费用）—— **已完成（V0.2.93）**

**做了**：
- 页签 `CurseTab` → `Header="诅咒 / 先古 / 状态"`，左栏三行（诅咒 / 先古卡 / **状态卡**）+ 两个 8 的间隔行；
- 状态卡列表 `StatusCardList` 绑到窗口的 `StatusCards`（`ObservableCollection<CardSpec>`），
  **不复制数据**：`RebuildStatusCards()` 从 `Profile.Cards` 里筛稀有度 Status 的牌铺一遍，
  在 `RefreshAll` / `OnCardsChanged` / `OnCardChanged(Rarity)` 里调；添加 / 删除 / 撤回 / 全选照诅咒那套写
  （`_statusUndo`）；删除是真删 `Profile.Cards`（撤回时放回同一位置）。
- 详情：先古卡 + 状态卡**共用一块**面板（`DataContext="{Binding SpecialCard, RelativeSource=…}"` +
  `ShowSpecialCardDetail`），里面按 `IsAncientCard` / `IsStatusCard` 切换标题与说明文字；
  三个列表的选择互斥（`OnSpecialCardSelectionChanged` 清另外两个 + `CardList`）。
- 费用：诅咒不再是写死的 `-1` —— `CurseSource` 写 `base(<用户填的>, …)`；`Unplayable` 只在费用 < 0 时强制
  （≥0 时用户勾了就照用）。效果仍在 `OnTurnEndInHand`，所以「能打出去的诅咒」打出去不做事，校验器有提示。
- 顺手修：`MerchantMissingTypes` 只查本体真的摆货架的三种牌型（Attack / Skill / Power）——
  以前「入池的状态卡」会被误报「进商店黑屏」（`MerchantInventory._coloredCardTypes` 里没有 Status）。
- 自检 20 条 + `ClickButtonInGroup`（同一页有多个同名「删除」按钮时按 GroupBox 标题定位）。
- 探针：`_tmp\selfreturnprobe`（`RunStatusAndCostCurse`）→ 生成 → 真 `dotnet build` 0 错误 →
  回读 `0费诅咒 cost=0/Unplayable=False`、`-1费诅咒 cost=-1/Unplayable=True`、`状态卡 rarity=Status`，0 条「没认出来」。

## ⑤ 遗物 / 状态的新触发时机 —— **已完成（V0.2.94）**

**做了**：
- `EffectCatalog.RelicTriggers` 四条新时机：`DamageDealt`（造成伤害时（每一下））/ `EnergySpent`（每消耗一个能量）/
  `StarsSpent`（每消耗一个额外资源量）/ `CardGenerated`（生成卡牌时）；
  `PowerTriggers.All` 加 `CardGenerated`。
- `CSharpCodeGen`：`RelicTriggerSignature` 四条签名 + `HasContext("DamageDealt")` +
  新 `BootstrapInCombat`（EnergySpent / StarsSpent / CardGenerated：战斗里没有 choiceContext →
  自造 `HookPlayerChoiceContext(this, base.Owner, __netId, GameActionType.Combat)`，**不能**用 Blocking 那个）+
  新 `PerPointTrigger`（EnergySpent / StarsSpent → 整个效果体包一层 `for (int __XIdx = 0; __XIdx < amount; __XIdx++)`，
  掷骰留在循环外）+ `RelicSource` 四条守卫。
- `CustomPowerGen`：`CardGenerated` 分支（`AfterCardGeneratedForCombat` + `creator.Creature == base.Owner` 守卫）+
  `TriggerHasChoiceContext` 把它算作「没有 context」。
- 条件：`PowerJustReached`（某状态层数刚好达到 N 层）—— 表达式
  `power is XPower && power.Amount >= N && power.Amount - amount < N`；只给自定义状态
  （`ForPower: true`、`ForCard/ForRelic: false`），且校验器拦住「不挂在『某个状态层数变化后』上」/「没选状态」。
  界面新增 `PowerEffectConditions` + `EffectConditionPower` 模板（状态那份候选比遗物多出这一条）。
- 回读：`TriggerOfHook` 补 `AfterCardGeneratedForCombat`；`ConditionFromExpr` 认回新条件的表达式。
- 自检 24 条；探针 `RunNewTriggers` 生成 → 真 `dotnet build` 0 错误 → 回读四条时机全部对上、0 条「没认出来」。

## ⑥ 参数监听（写进「数值公式」的可读量）—— **已完成（V0.2.95）**

**做了**：新 `FormulaBuiltins`（17 条：`Turn` / `Round` / `HandCount` / `DrawCount` / `DiscardCount` /
`ExhaustCount` / `DiscardedThisTurn·ThisCombat` / `ExhaustedThisTurn·ThisCombat` /
`GeneratedThisTurn·ThisCombat`（含复制）/ `EnergySpentThisTurn·ThisCombat` /
`StarsGainedThisTurn·ThisCombat` / `PowersOnMe`），全部用**本体现成 API**（CombatHistory 的各 Entry /
`PileType.X.GetPile` / `Creature.Powers.IsVisible`）—— **不需要生成记录器 / 补丁**（原计划里的 tracker 不用做了）。
- `ForgeFormula` 认多字母标识符（只认名单里的），错误文案列出全部名字；
- 新 `ForgeFormula.CardResolver(p)` 统一「X / Y / 内置量 / 自定义参数」的解析，
  卡牌公式（`ForgeGraphGen.CardFormulaExpr`）与锻造【数值】（`ValueVar`）共用一份；
- 内置量写成 `__fgPlayer` / `__fgSelf` 表达式 → 生成端「用到才声明」那套自动生效（生成端**不用改**）；
- `AmountFormulaHint`（界面提示）与 ToolTip 列出名单；校验器不再把内置量当「没定义的参数」+ 报错列名单；
- ⚠ 本体把**复制**记成同一个 `CardGenerated` 记录 → 没有单独的「复制张数」，`Generated*` 含复制（文档写清）；
- 自检 33 条 + 探针 `RunFormulaBuiltins`（生成 → 真编译 0 错误 → 回读公式原样、0 条「没认出来」）。

## ⑦ 「所有能填数值的地方都能写公式」—— **已完成（V0.2.96 / 97 / 98 三批）**

**批次 1（V0.2.96）次数类**：生效次数 / 命中次数对群数 / 复制份数 / 重放次数 ——
`EffectSpec.CountFormulas` 字典 + `CountFormulaExpr` + 四处发射器（`EmitRepeated` / `RepeatExpr` /
`__copyIdx` / `BaseReplayCount`）+ 卡面印公式 + `CET:CountFormulas` 回读 + 校验器。自检 17 条。

**批次 2（V0.2.97）条件数值**：`ConditionSpec.AmountFormula` / `Amount2Formula` +
`ForgeFormula.ConditionResolver`（**内联**：条件要写进 `IsPlayable` / `ShouldGlowGoldInternal`
那两个表达式体属性，没法先声明局部变量）+ `ConditionFormulaExpr(withPlayTarget)` +
`ConditionFormulaNeedsPlayTarget`（公式用到「打出的目标」时跳过 gate/glow）+ `CET:CondFull=` 整条条件标记 +
界面两个框（只在卡牌模板里）+ 校验器。自检 13 条。
⚠ 坑：`Match(text, "…=(.*)$")` 在**整份源码**上不是逐行匹配，要用 `([^\r\n]*)`。

**批次 3（V0.2.98）概率 + 收尾**：`ChanceFormula`（复用 `CountFormulas` 的 `Chance` 键）-
写了公式的那条**退出「同组共用一次掷骰 + 各占区间」**、改成 `__chanceRoll < (double)(现算)` 独立判一次
（校验器提示语义变化）；牌时机钩子里同样处理；界面写明「卡牌费用 / 升级增量 / 页面常量不能写公式」。自检 10 条。

**做不了运行时（已在界面说明里写明）**：卡牌**费用**（本体 `base(cost, …)` 要编译期常量）、
升级增量、页面常量（初始血量 / 初始金币 / 额外资源量上下限 / 触发时机的次数上限）。

### ⑦ 实现摘要（动手时看这几条就够）

- **生成期变量** `CSharpCodeGen._formulaOwner`（`[ThreadStatic]`，`CardSource` 进出时设置 / 还原）：
  公式求值要 `__fgPlayer` / `__fgSelf`，而这些公式埋在很深的发射器里 —— 用它把「当前是哪张卡」传下去，
  不然要给几十个调用点加参数。非卡牌链上是 null = 公式不参与（校验器本来也会拦住）。
- **两套解析器**：
  · `ForgeFormula.CardResolver(p)` —— 用**局部变量**（`__pM` / `__X` / `__Y` / `__fgPlayer` / `__fgSelf`），
    适用于效果体（那里的 prologue 会按「用到才声明」补齐）；
  · `ForgeFormula.ConditionResolver(p, playTarget)` —— **全内联**（`base.Owner` / `base.Owner.Creature` /
    `ForgeFormulaRuntime.Energy(...)` / 参数表达式文本替换），适用于 `IsPlayable` / 描金边这种表达式体属性。
- **回读标记**：次数类 `/CET:CountFormulas=Times=…|Repeat=…|Chance=…`；条件类 `/CET:CondFull=Kind=…|Amount=…|…`
  （整条条件都带上，因为守卫已经变成算完的表达式，反推不回来）。
  - ⚠ V0.2.106 修的坑：`CET:CondFull=` 只由**效果**那条条件写；挂在**整张牌**上的条件写了公式时
    以前**没有标记** → 回读悄悄把公式丢了，而且效果那条标记还会被认成「整张牌的条件」。
    现在整张牌的条件写自己的 `// CET:CardCondFull=…`，回读时两种标记各认各的；
    没有「// 条件：…」注释的牌就是没有整张牌条件（不再误报「条件没认出来」）。
- **默认档逐字不变**：没写公式时表达式还是固定字面量、也不写任何新标记（自检里有红线断言）。


**还差（下一批）**：
1. **概率 `ChancePercent`**：本体是「同组效果共用一次掷骰 + 各占一段区间」（`ChanceSlices`，区间在生成时算），
   要做公式得改成「每条各自掷一次」的语义 —— 要么接受语义变化，要么只给「单条效果勾概率」时开公式。
2. **只支持固定值 + 参数**（做不了运行时，界面要标注清楚）：卡牌费用 `CardSpec.Cost`（本体 `base(cost, …)` 要编译期常量）、
   升级增量 `UpgradeAmount`、页面常量（初始血量 / 初始金币 / 额外资源量初始与上限、触发时机的次数上限…）。

## 追加需求（用户后来的口头要求，也按同一套红线做）

### 参数页也能监听「实时读数」（用户要求）—— **已完成（V0.2.103）**

- `ParamSources.All` = 原来 7 条固定来源 + `FormulaBuiltins.All`（19 条实时读数）——**同一份表**，
  参数页下拉、公式「可用量」清单、生成端、回读四处一致（不会漂移）。
- `ForgeFormula.ParamExpr` 遇到这类来源直接借 `FormulaBuiltins` 的表达式（`__fgPlayer` / `__fgSelf` 版本）；
  `ProjectRecovery.ParamFromExpr` 按表达式原文比对认回来（不需要新标记）。
- 新增两条给用户找得到的别名：`CopiedThisTurn` / `CopiedThisCombat`（**和 Generated 同一个数** ——
  本体把复制记成「生成」，历史里没有单独的复制条目；文档里写清了）。
- **「获得的能量」已完成（V0.2.106，用户说「做吧」）**：本体战斗历史只记「花掉」（`EnergySpentEntry`）、
  没有「获得」记录，也没有现成计数器 → 工具自己打补丁记一份：
  - 生成的 `cs/EnergyGainTracker.cs`（**用到才生成**，红线照旧）= 静态记录器 +
    打在**同步**方法 `PlayerCombatState.GainEnergy(decimal)` 上的 Harmony postfix
    （`PlayerCmd.GainEnergy` 是 `async`，只走它内部的这个同步方法）；
  - **补满不算「获得」**：`ResetEnergy()` / `AddMaxEnergyToCurrent()` 不经过 `GainEnergy`，
    正好是要的语义（否则每回合莫名多 3 点）；
  - 按 `PlayerCombatState` 实例记（本体每场战斗 `new` 一个 → 换战斗自动从头算），
    「本回合」按 `TurnNumber` 认；键用 `ConditionalWeakTable`（弱引用，不拖住打完的战斗状态）；
  - 两条读数 `EnergyGainedThisTurn` / `EnergyGainedThisCombat` 进 `FormulaBuiltins` →
    自动进「可用量」清单 + 参数页「从哪来」（`ParamSources.All` 是拼出来的）→ 生成 / 回读都走同一份文本；
  - `CSharpCodeGen.EnergyGainTrackerSource/UsesEnergyGainTracker`（后者按存档 JSON 文本找这两个名字，
    公式写在哪一处都不会漏；序列化失败就宁可多生成）。
- 自检 7 条 + 探针 `RunParamLiveSources`（参数选实时读数 → 公式里用 → 真编译 0 错误 → 回读来源）。
### 触发时机上的数值：已确认的批次（用户 2025 选择）

> ⚠ **V0.2.107 修的界面坑（用户报「在哪填」）**：这一栏的显隐 / 标题 / 说明都是按触发时机算出来的，
> 但 `RelicSpec.Trigger` / `PowerTriggerSpec.Kind` 的 setter 只通知了「盯哪个状态」那几项 ——
> 于是**在编辑器里换完触发时机，那一栏不会当场出现**（要重新在列表里点一次遗物 / 状态才行）。
> 现在两个 setter 一并通知 `ShowTriggerAmountField` / `ShowTriggerScopeField` / `TriggerAmountLabel` /
> `TriggerSpendHint`（状态那边还有 `NeedsAmount`），`TriggerAmount` 改动也刷新 `TriggerSpendHint`。
> **自检教训**：以前那批断言只测了属性算得对不对，没真的去动界面上那个下拉 —— 现在补了
> 「打开页面 + 改下拉 + 用 `IsVisible` 判那一行到底显示没有」的用例（`TextsIn` 会把 Collapsed 里的
> 文字也收进来，判显隐必须用 `VisibleTextsIn`）。改前 2 条 FAIL、改后 PASS。

**已完成（V0.2.102）**：
- 「花掉能量后 / 花掉额外资源量后」→ **每累计花掉 N 点触发一次** + 范围**本回合 / 本场战斗**
  （`RelicSpec.TriggerAmount/TriggerScope` + `PowerTriggerSpec.TriggerAmount/TriggerScope`；
  `CSharpCodeGen.SpentSumExpr/SpendBeforeLine/SpendTickLine/SpendTickCond/SpendThresholdComment`；
  无状态实现：读 `EnergySpentEntry` / `StarsModifiedEntry`(Amount<0)，`- amount` 得「之前花了多少」；
  回读按那句注释还原；遗物 + 状态两条链都做）。
- **用户说「这个不用」**：「第几回合开始 / 结束时能填第几回合」**不做**。

**下一批（用户已勾选，语义已确认）—— 批次 2 已完成（V0.2.104），只剩「敌人死亡」+「获得能量」需要自记计数器**：
| 触发时机 | 数值含义 | 范围 |
|---|---|---|
| ~~CardPlayed~~ ✅V0.2.104 | 每打出 N 张触发一次 | 本回合 / 本场战斗 |
| ~~CardExhausted / CardDiscarded / CardDrawn~~ ✅V0.2.104 | 每 N 张触发一次 | 本回合 / 本场战斗 |
| ~~CardGenerated~~ ✅V0.2.104 | 每生成 N 张触发一次 | 本回合 / 本场战斗 |
| ~~DamageReceived / DamageDealt~~ ✅V0.2.104 | 伤害 ≥ N 才触发 | — |
| ~~HpLost / BlockGained~~ ✅V0.2.104 | 数值 ≥ N 才触发 | — |
| ~~EnemyDeath~~ ✅V0.2.105 | 每 N 个敌人死亡触发一次（工具自己记的计数器 `cs/CombatCounters.cs`，按 ICombatState 自动清零） | 本场战斗 |

实现提示（照 V0.2.102 那套）：
- 计数类（打牌 / 消耗 / 弃掉 / 抽到 / 生成 / 敌人死亡）都能用**战斗历史**无状态算：
  `CardPlaysFinished` / `CardExhaustedEntry` / `CardDiscardedEntry` / `CardDrawnEntry` / `CardGeneratedEntry`，
  再 `- 1`（或能量的 `- amount`）得「这一次之前已经多少」，然后「% N == 0 才跑」；
  ⚠ **敌人死亡没有历史条目**（`CombatHistory` 里没有死亡记录）→ 这一条需要按遗物/状态各自记一个计数器
  （遗物：私有字段 + 战斗开始清零；状态：`PowerModel` 自己也是实例，同法），或者只做「每 N 个」用计数器。
- 阈值类（伤害 ≥ N / 失去生命 ≥ N / 获得格挡 ≥ N）直接把钩子给的数值拿来比：
  伤害是 `result.UnblockedDamage`（`DamageReceived`）/ `result.TotalDamage`（`DamageGiven`），
  失去生命是 `-delta`，格挡是 `amount`；写进各自钩子守卫里即可。
- 两边（遗物 / 状态）都要做，保持菜单与能力一致；回读沿用同一句注释格式。


- 遗物侧补齐 **13 条**（CardPlayed / CardExhausted / CardDrawn / CardDiscarded / HpLost / BlockGained /
  EnemyDeath / EnemyTurnStart / EnemyTurnEnd / EnemyDamaged / EnemyDealtDamage / EnemyBlockGained / PowerChanged），
  守卫全部按遗物归属写（`base.Owner` 是 Player → 比 `base.Owner.Creature`）；没有 choiceContext 的四条
  （HpLost / BlockGained / EnemyTurnStart / EnemyBlockGained）走 `BootstrapInCombat` 自造上下文。
- 状态侧补 **2 条**（GoldGained / Shuffle）；`TriggerHasChoiceContext` 把 GoldGained 算作「没有 context」。
- **显示名统一**：受伤 / 攻击 / 造成伤害 / 回合开始结束 / 能量 / 资源量 / 战斗胜利 / 状态层数变化
  这八处两边现在同名；老工程里改名前的那句 `// 触发时机：<老显示名>` 由新加的老名字对照表认回来。
- `RelicTriggerOfHook` 改成「只在钩子唯一确定时给 id，含糊的（AfterDamageReceived / AfterDamageGiven /
  AfterBlockGained / BeforeSideTurnEnd / AfterPowerAmountChanged）返回空 → 落到注释兜底」。
- 顺手修：触发时机是「战斗结束时」+「每场战斗只触发一次」时会生成两个 `AfterCombatEnd` → **CS0111**；
  现在清标记那一句直接写在触发体末尾。
- 只差三处（有原因，已写进文档）：遗物的「获得时」「战斗结束时（房间结算之前）」「某状态层数到达 N 层时」；
  **卡牌侧没动**（卡牌是 OnPlay +「卡牌时机」条件，不是模型钩子）—— 要不要给卡牌也做一套钩子时机，等用户定。
- 自检 16 条 + 探针 `RunMergedRelicTriggers`（13 条各生成一只遗物 → 真编译 0 错误 → 回读全对）。

### 「命中次数的公式不生效」（用户报的问题）—— **已查清并修掉（V0.2.101）**

- 根因：「命中次数 / 对群数」生成端只有三种场合会读（造成伤害多段 / 作用对象=随机敌人 / 伙伴攻击），
  但这一栏以前对**所有种类**都显示、填了也**不报错** → 用户填了公式却没反应（静默忽略）。
- 修法：`EffectSpec.UsesRepeatCount` + `ShowRepeatCountInput`（用不到的种类那一栏**自动收起**）+
  校验器两条（用不到却填公式 → **错误**；只填固定数字 → **警告**）+ 生成端不变（会用到的地方照旧生效）。
- 自检 5 条 + 探针 `RunRepeatFormulaCases`（会用到 / 用不到两组，用不到的走校验断言）。


- 起因：⑥ 的 17 条内置只读量只写在**说明文字**里（`AmountFormulaHint` + ToolTip），界面上没有清单 → 用户找不到。
- 做法：窗口资源新模板 `FormulaBuiltinList`（蓝色小框，名字 + 中文说明，`WrapPanel` 自动折行），
  数据源 `MainWindow.FormulaBuiltinRows => FormulaBuiltins.All`（**和生成端同一份**，不会漂移），
  在 `EffectEditorCard` 里挂在 `EffectCommon` 之后 + 条件区之前 → 数值公式 / 次数公式 / 条件公式三处共用一块。
- 三处说明文案都改成「完整清单见下面那个『可用量』小框」；`AmountFormulaHint` 补了「⚠ 只做在卡牌上」。
- 自检 7 条（含「遗物编辑器里没有这块清单」的对照断言）。
- ⚠ 顺手修：加这块时误删了 `EffectConditionCard` 的引用（卡牌条件区整块消失）—— 已恢复并纳入自检范围内的界面检查。


- `EffectCatalog.RelicTriggers` 加 `PowerReached`（显示名「某个状态层数到达 N 层时」，
  钩子 `AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)`；`HasContext` = true）。
- 参数放在 `RelicSpec.TriggerPowerId`（盯哪个状态，留空 = 任意）/ `TriggerAmount`（到达多少层），
  界面只在选了这个时机时显示这两栏 + 一句**实时**说明（`TriggerHint`）。
- `RelicSource` 那条分支写三条守卫：只看自己身上的状态 / 只盯选的那个状态（留空不写）/
  `power.Amount >= N && power.Amount - amount < N`（**跨过** N 那一下才算，一次上 3 层只算一次）。
- 回读：类里写 `// CET:PowerWatch=<类名> CET:PowerReach=<N>m`；并新增 `RelicTriggerOfHook`
  —— 同一个钩子在状态那边叫 `PowerChanged`、遗物这边叫 `PowerReached`，不能共用一张表。
- 校验器三条：N ≤ 0 报错 / 没选状态给提示 / 没选这个时机却填了值给提示（不静默）。
- 自检 12 条 + 探针 `RunRelicPowerReached`（生成 → 真编译 0 错误 → 回读全对）。

- V0.2.80 美术页联机宝箱房手势 ×5 + 自定义催促文本；「将此牌放回手牌」。
- V0.2.81 抽到这张牌自减费；复制出来的卡可免费/升级；致死性做法（`LethalityPower` 50 层）；
  自定义状态「每消耗一个能量」补守卫 + 两种消耗类触发按点数循环；卡面「打不出的叉」不再被误换。
- V0.2.82 「丢弃卡牌」数值 0 = 丢弃这张牌自己；额外资源量自定义描述；「资源不够」提示改用自己的键 + 只在玩我方角色时替换
  （`UnplayableReasonExtensions.GetPlayerDialogueLine` / `internal` 类 → 手动打补丁，见 `PatchesGen`）。
- V0.2.83 遗物触发时机「战斗结束时（房间结算之前）」= `AfterCombatEnd(room)`；回读靠 `// 触发时机：<显示名>` 注释兜底。
- V0.2.84 卡牌效果作用对象「指定队友 / 全部队友」（`TargetType.AnyAlly` / `AllAllies`）+ 四道拦截。
- V0.2.85 修「锻造里不能填范围」（界面按种类显示，不再按数值藏行）；锻造卡费用在卡牌页只读显示（`ForgeEnergyCostText`）。
- V0.2.86 遗物稀有度「先古」（`RelicRarity.Ancient`）+ 提示本体机制。
- V0.2.87 「下回合生效」真实支持范围收窄为五种 + 假勾选框消失（`EffectCatalog.HonorsNextTurn`）。
- V0.2.88 「下回合生效」支持生成类：生成卡牌 / 获得·变化·删除·升级牌组（全局）→
  `IsDelayedSpawnKind` + `ForgeDelayed<Stem><卡名>` 延迟状态（层数 = 张数，`AfterPlayerTurnStart` 里调同一套发射器）
  + 回读（类名尾巴认卡名、`CET:SpawnPick=` 标记行认张数/范围/附加处理）+ 校验器（只允许卡牌）。
- V0.2.89 生成卡牌「从范围里自己挑一张」（`SpawnChosen` + `__chosenCand` + `CET:Chosen=1`）；「下回合生效」接上**复制卡牌**。
- V0.2.90 「下回合生效」接上**变化卡牌（战斗内）**→ 队列①收官（`EmitTransformCard` 的 `combatStateExpr` 参数化）。
- V0.2.91 「遗物」页新增「额外资源量遗物」块（效果列表 + 添加/删除/撤回 + 复用效果编辑器）；
  校验器新增「没启用额外资源量却留着效果」的警告。
- V0.2.92 **无色卡**：卡牌页勾选框 → 进本体无色卡池（`ModHelper.AddModelToPool(__colorlessPool, __t<卡类>)`，
  写在 `cs/ModEntry.cs` 的 `Initialize()` 里）+ 用本体无色卡框 + 不进本角色卡池；校验器拦 Ancient/Event/Quest/Token；
  回读扫 `cs/ModEntry.cs` 还原；自检 8 条断言。
- V0.2.93 「诅咒 / 先古卡」页 → **「诅咒 / 先古 / 状态」**：状态卡并进来（`StatusCards` 镜像 `Profile.Cards`，
  不复制数据）+ 三类费用都能调（诅咒不再写死 -1，费用 ≥0 时不强制 `Unplayable`，校验器说清「打出去本身不做事」）+
  顺手修「入池状态卡被误报进商店黑屏」（`MerchantMissingTypes` 只查 Attack / Skill / Power）+ 自检 20 条。
- V0.2.94 遗物 / 状态新触发时机：遗物加「造成伤害时（每一下）/ 每消耗一个能量 / 每消耗一个额外资源量 / 生成卡牌时」，
  自定义状态加「生成卡牌时」；没有 choiceContext 的钩子按本体做法自造 `HookPlayerChoiceContext`；
  两条「每消耗一个」按点数循环；条件加「某状态层数刚好达到 N 层」（只给状态、只在那条钩子上成立）+ 自检 24 条。
- V0.2.95 参数监听：公式内置 17 条实时只读量（回合 / 轮 / 四摞张数 / 本回合与本场战斗的丢弃·消耗·生成 /
  花掉的能量 / 获得的额外资源量 / 身上状态数），全部走本体现成 API（不生成补丁）；
  `ForgeFormula.CardResolver` 统一解析；界面提示与校验器名单跟着走 + 自检 33 条。
- V0.2.96 队列⑦批次 1：**次数类公式**（生效次数 / 命中次数对群数 / 复制份数 / 重放次数）——
  `EffectSpec.CountFormulas` + `CountFormulaExpr` + 四处发射器 + 卡面印公式 + `CET:CountFormulas` 回读 +
  校验器（只做在卡牌上、与「= X / = 层数」互斥）+ 自检 17 条。
- V0.2.97 队列⑦批次 2：**条件数值公式** —— `ConditionSpec.AmountFormula` / `Amount2Formula` +
  `ForgeFormula.ConditionResolver`（内联）+ `ConditionFormulaExpr` + `CET:CondFull` 整条条件回读 +
  界面两个框 + 校验器 + 自检 13 条。
- V0.2.98 队列⑦批次 3（收尾）：**概率公式**（`ChanceFormula`，写了公式的那条退出「同组共用掷骰 + 区间」、
  改成独立判一次）+ 界面写明「费用 / 升级增量 / 页面常量不能写公式」+ 自检 10 条。
  **队列 ①~⑦ 全部完成**（① V0.2.88~90 / ② V0.2.91 / ③ V0.2.92 / ④ V0.2.93 / ⑤ V0.2.94 /
  ⑥ V0.2.95 / ⑦ V0.2.96~98）。
- V0.2.99 追加需求：遗物触发时机「**某个状态层数到达 N 层时**」（`AfterPowerAmountChanged` +
  盯哪个状态 / 到达 N 层两个参数 + 跨过才算 + `CET:PowerWatch/PowerReach` 回读 + 三条校验器）+ 自检 12 条。
- V0.2.100 追加需求：效果编辑器加**一直显示**的「可用量」清单（17 条内置只读量 + X/Y/参数名说明，
  界面与生成端同一张表）+ 三处公式说明指向它 + 自检 7 条。
- V0.2.101 追加需求：**触发时机合并**（遗物补 13 条 / 状态补 2 条 / 八处显示名统一 / 老名字回读兜底 /
  含糊钩子走注释兜底 / 顺手修 CombatEnd+每场一次 的 CS0111）+ **命中次数公式不生效**的修复
  （用不到的种类收起那一栏 + 校验器报错/警告）+ 自检 21 条。

## 已完成（按版本）
