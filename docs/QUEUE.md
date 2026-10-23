# 待办队列（用户要求「全做完，不用一步步报备」）

## 第 161 批需求（V0.2.267：**自定义参数页「指定敌人 + 自己的当前格挡」的糊话**）

> 用户报：「自定义参数页监听格挡时在其他参数选择指定敌人，再选择监听格挡，会出现使人稀里糊涂的 UI 错误」。
> （详见 `docs/RELEASE_NOTES_V0.2.267.md`）
>
> **根因三处**：① 「要不要选目标」是手写枚举，实时读数（`MyBlock` 等）不在里面 → 那一栏藏起来、旧的
> `Target=ChosenEnemy` 却还留着；② 列表一律拼 `{目标}的{来源}` → 「指定敌人的自己当前格挡」；
> ③ 反而漏了「减益的个数」（需要目标却被藏了下拉）。
>
> **修复**：新增唯一判据 `ParamSources.SelfOnly(id)`（固定数值 / 金币 / 能量 / 最大能量 + `FormulaBuiltins.All`），
> `ParamSpec.NeedsTarget = !SelfOnly(Source)`；`Target` getter 对自己专属的来源一律读「自己」；
> `Source` setter 换到自己专属来源时把目标收回「自己」；`Summary` 对名字本身以「自己」开头的来源不加前缀
> （金币那种保持「自己的金币数」→ 老存档文案逐字不变）。顺手让状态页「层数变化后」列表那一行说出方向。
>
> **验收**：自检 **4411 PASS / 0 FAIL**（+7）；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 160 批需求（V0.2.266：**「某个状态层数变化后」细分上升 / 下降**）

> 用户要求：「发时机的某个状态层数变化后能不能细分是上升了还是下降」。（详见 `docs/RELEASE_NOTES_V0.2.266.md`）
>
> **做法**：`PowerTriggerSpec.PowerDirection` / `RelicTriggerSpec.PowerDirection`（++ `RelicSpec.TriggerPowerDirection`
> 代理到第 1 条时机，和 `TriggerPowerId` 同一个写法；空 = 都算 / `Up` / `Down`）+ 两处 UI 下拉
> （遗物页 & 状态页的触发时机编辑器，只在「某个状态层数变化后」显示，单独一行）。
>
> **生成**：状态链守卫后面接 `&& amount > 0m` / `&& amount < 0m`；遗物链（`RelicTriggerGuardLines`）
> 加 `if (amount <= 0m) return;` / `if (amount >= 0m) return;`。不选方向时一个字都不多写。
>
> **验收**：自检 **4404 PASS / 0 FAIL**（+6）；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 159 批需求（V0.2.265：**音频也能加入组 + 「仅能被下个同组音频顶替」**）

> 用户要求：「音频也能加入组，并且音频效果下新增勾选项『仅能被下个同组音频顶替』，
> 确保这个勾选项不会重叠导致看不见，以及在组那边确实能看到音频能加入组」。（详见 `docs/RELEASE_NOTES_V0.2.265.md`）
>
> **① 组页面**：`CardGroupSpec.AudioIds`（存音频 Key）+ 第三列「这个组里有哪些音频」
> （`AudioGroupMemberRow.Build` / `SetAudioGroupMembership`；切组 / 改名 / 勾选变化都会 `Raise`）。
>
> **② 效果**：`EffectSpec.SfxSameGroupOnly`；`App.xaml` 里**独占第 18 行**（网格真的加到了 19 行 ——
> 上次「重叠」就是行号超界被 WPF 折回最后一行）。
>
> **③ 生成**：`SfxRegistrySource` 里加 `GroupsOf` / `SameGroupOnly` 两张写死的表 + `SameGroup(a,b)`；
> `Playing` 记录 key；顶替循环里 `if (SameGroupOnly.Contains(old.Key) && !SameGroup(old.Key, key)) continue;`。
> 调用处签名不变（仍 3 个参数）；没勾任何一条时逐字不变。
>
> **④ 校验器**：勾了「仅同组」但没勾「会被顶替」→ 提示；音频没进任何组 → 警告；组里记的音频 Key 找不到 → 警告。
>
> **验收**：自检 **4398 PASS / 0 FAIL**（+9）；探针工程现场写最小 `.wav` 走真编译 **0 error**；对拍逐字节相同。

## 第 158 批需求（**「本体没有 `AttackCommand.FromCard(CardModel, CardPlay?)`」这条报错要能自证** —— 顺带修 `HasMethod` 的误判风险）

> 用户转来别人的构建日志（`构建日志_20261023_003718.txt`），并说明：**那个人用的是 Steam 公开版游戏，不是 beta**。
> 日志里 8 次「一键构建」的错误从 6 条一条条减到 1 条，最后卡在：
> ```
> [错误] 本体没有 `AttackCommand.FromCard(CardModel, CardPlay?)` 这个重载（工具生成的所有攻击牌都按这个签名写）
>        —— 你这份游戏的版本和本工具假设的不一样 …
> ```
> 这正对应用户的说法：**公开版和 beta 的本体签名不一样**（工具是按 0.111.x beta 写的；本机 v0.111.0 上
> `AttackCommand.FromCard(CardModel, CardPlay)` 是有的，探针实测）。
>
> **这批不是改生成端，而是把「版本不对」这件事做成一看就懂、而且不用来回问 dll：**
> 1. `Sts2Metadata.MethodOverloads(dll, 类型, 方法)`：把**用户那份 dll 里真实的重载**打出来
>    （形参类型串，例 `FromCard(CardModel, CardPlay)`）→ 那条报错现在直接附「实际只有：…」。
> 2. `Sts2Metadata.HasMethod` 修一处**误判风险**：以前碰到第一个同名类型就 `return false`；
>    本体里同名类型不止一个（换命名空间 / mock / 内部类）时会把「其实有」错判成「漂移」→ 用户版本对却被拦。
>    现在扫完**所有**同名类型，都对不上才算漂移；类型一个都没找到仍然返回 `null`（不知道，不报错）。
> 3. 新增**游戏版本核对**：读游戏目录的 `release_info.json`（本机 `v0.111.0`），
>    和 `Sts2Metadata.SupportedGameVersion`（`v0.111.0`）比**前两位**；不一致给一条警告说清两边版本
>    （`0.111.x` 之间的小修订不打扰）。
>
> **验收**：探针 `_probe/sigdrift` **6/6 PASS**（本机 `HasMethod`=true、`MethodOverloads` 打出
> `FromCard(CardModel, CardPlay)`、方法不在=false / 类型不在=null、版本读得到、只比前两位、
> 拿本机路径跑校验器既不报 FromCard 错也不报版本警告）；
> `dotnet build` 全解决方案 **0 error**；`--uicheck` **4389 PASS / 0 FAIL**（新增 5 条：签名核对 ×3、版本核对 ×2）。
>
> **给那个人的答复**：这条是游戏版本问题 —— 在 Steam「属性 → 测试版」切到工具对应的 0.111.x，
> 或者把 `data_sts2_windows_x86_64\sts2.dll` 发过来适配；他日志里其余 5 条都已按提示改完（见下）。

## 第 157 批需求（V0.2.264：**「替换初始卡」下拉还是只显示范例卡** —— 候选没刷新 + 范例牌排最前）

> 用户报：「被古老牙齿替换的初始卡底下的下拉选项还是只有范例卡」。（详见 `docs/RELEASE_NOTES_V0.2.264.md`）
>
> **① 主因**：`ToothStarterChoices` 按「当前存档的牌」算，但**从来没 `Raise` 过** ——
> ComboBox 只在第一次绑定时取一次，于是它一直显示**启动时那份示例存档**的牌。
> **改**：`RefreshCardChoices()` 里补 `Raise("ToothStarterChoices")`（载入存档 / 增删改牌 / 排序都会走到）。
>
> **② 排序**：新建存档自带的 `打击 / 防御`（工具塞的范例牌）在存档里也算「你的初始牌」，排在了最前。
> **改**：排序 = 你的牌 → 本体牌 → 打击 / 防御；每行标 `（类名）｜ 归属`，范例牌明确写「新建存档自带的打击 / 防御（范例牌）」。
>
> **验收**：自检 **4384 PASS / 0 FAIL**（+2 条：会通知刷新 / 自己的牌排第一、范例牌排最后且带标记）。

## 第 156 批需求（V0.2.263：**欧洛巴斯之触 / 古老牙齿真正生效** + 牙齿卡不进尘封魔典 + 下拉排序）

> 用户报：「发现欧罗巴斯的两个遗物都没办法正常生效，而勾选替换古老牙齿的先古反而会出现在达弗的尘封魔典里。
> 并且替换初始卡的下拉选项中怎么全身是范例卡」（附 `godot (32).log`，里面没有我们的异常）。
>（详见 `docs/RELEASE_NOTES_V0.2.263.md`）
>
> **① 真因**：本体是两段流程 —— `SetupForPlayer`（写展示）+ `AfterObtained`（**真正换牌 / 换遗物**）。
> V0.2.260 只挂了前者，而 `AfterObtained` 里本体又查一次它自己写死的表
> （`ArchaicTooth.GetTranscendenceStarterCard` 返回 null；`TouchOfOrobas.UpgradedRelic ?? 查表` → 头环）。
> **改**：两件道具都再加一个 `AfterObtained` 前缀 —— 古老牙齿走 `CardCmd.Transform(初始卡, 先古卡)`（初始卡升过级则先古卡也升级）、
> 欧洛巴斯之触走 `RelicCmd.Replace(初始遗物, 我们的遗物.ToMutable())`；`SetupForPlayer` 前缀保留（悬停提示）。
>
> **② 尘封魔典**：本体随机池只排自己那 5 张（`DustyTome.cs:53`）。**改**：只勾牙齿、没勾魔典时额外生成
> `DustyTomeExcludePatch`，按本体同一套逻辑重挑并排掉我们的牙齿专用卡（空池就交回本体）。
>
> **③ 下拉**：以前所有牌按存档顺序平铺（新建存档自带的打击 / 防御排最前 → 看着像「全是范例卡」）。
> **改**：排序 = 你的初始牌 → 你的其它牌 → 本体基础牌，每行标 `（类名）｜ 归属`。
>
> **验收**：自检 **4382 PASS / 0 FAIL**（+3）；探针工程三勾全开 → `dotnet build` 0 错误；对拍逐字节相同。

## 第 155 批需求（V0.2.262：**「召唤所有召唤物不生效」** —— 自定义状态链没展开「全部召唤物」）

> 用户报：「召唤所有召唤物不生效」（附 `godot2026-10-22T17.58.35.log`，里面没有相关异常）。
>（详见 `docs/RELEASE_NOTES_V0.2.262.md`）
>
> **真因**：`"*"`（全部召唤物）的逐只展开只写在了**卡牌**和**遗物**两条链上；
> `CustomPowerGen` 的 `case "SummonPet"` 直接把 `e.PetSummon` 丢给单只那套 → `Resolve("*")` 认不出来 →
> 只生成一句「没有可用的召唤物」，一只都不召（编得过、也不报错）。
>
> **改法**：状态链按 `PetGen.ResolveMany` 逐只展开，每份带同一个 `// CET:PetAll=<组号>`（回读合并回一条）；
> 组号按效果内容算（同配置每次一致）；一只都没启用时写成明说的注释。`EmitSummonPetPublic` 加 `petDef` /
> `extraComment` 两个可选参数。
>
> **验收**：自检 **4379 PASS / 0 FAIL**（+2 条：两只都召 / 组号一致）；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 154 批需求（V0.2.261：**「那我如何决定是哪张牌被替换呢」** —— 古老牙齿的被替换初始卡可以自己选）

> 用户问：「那我如何决定是哪张牌被替换呢」（承接第 152 批「替换古老牙齿所替换的初始卡牌」）。
>（详见 `docs/RELEASE_NOTES_V0.2.261.md`）
>
> **改法**：`CardSpec.ToothStarterClass`（存**卡牌类名**，和「目标卡」下拉同一个口径）+
> 先古卡页新下拉「被古老牙齿换掉的初始卡」——第一项是「（自动：初始卡组里第一张）」，
> 其余每一项是「中文名 (类名) ｜ 初始牌 / 不在初始卡组」；`AncientItemPatchGen.StarterCardFor(p, ancient)`
> 先按它找、找不到再退回自动；校验器报「指定的类名找不到（已退回自动）」。
>
> **验收**：自检 **4377 PASS / 0 FAIL**（+3 条：没指定时自动 / 指定了就用那张 / 类名找不到退回 + 警告）；
> 对拍 Sample 113 / WhiteCloak 412 **逐字节相同**。

## 第 153 批需求（V0.2.260 追加：**「目标身上的减益个数」做成自定义参数 `DebuffCount`**）

> 用户原话：「生成器里有减益的个数的参数吗」→「那可以做一个目标身上的减益个数吗」→「做成自定义参数」。
> 起因是本体「撕碎 Rend」= 基础伤害 + **目标身上每有一种负面效果** × N
>（`target.Powers.Count(TypeForCurrentAmount == Debuff)`，见 `src/Core/Models/Cards/Rend.cs`），
> 而卡牌公式里原来只有 `DebuffsOnMe`（**自己**），拿不到目标侧。
>
> **做法**：
> * `ParamSources` 加第 8 个固定来源 `DebuffCount`（下拉文案「减益的个数（几种负面效果）」，
>   提示写明「和层数无关，每种算 1」），可配「从谁身上取」（自己 / 指定敌人 / 随机敌人 / 全体 / 召唤物）；
> * `ForgeFormula.ParamExpr` → `ForgeFormulaRuntime.PowerCount({target}, "Debuff", null)`
>   （复用锻造页【状态列表】那个运行时帮手，判据只有一份）；`NeedsTarget` 把这条来源算进「要用到目标」；
> * `ProjectRecovery.ParamFromExpr` 加正则认回（`PowerCount(…, "Debuff", null)` → DebuffCount + 目标）；
> * 自检把「固定来源 7」改成「8」，并新增 11 条断言（下拉 / 表达式 / 撕碎式复合公式 / 回读）。
>
> **验收**：① `--uicheck` **4374 PASS / 0 FAIL**；② 撕碎探针（`M`=指定敌人的减益个数 + 伤害
> `10 + M * 5`）`--build` **0 错误** + 导出 PCK OK；③ `--recover` 认回「M=指定敌人的减益的个数」、
> 没认出来为 0。
>
> ⚠ 和那 6 条「这一次打出」读数不同：参数是**先算成 `__pM`** 的，所以能参与任何复合公式
>（`10 + M * 5`），不是「只能光秃秃单独写」。

## 第 152 批需求（V0.2.260：**先古卡 / 遗物页新增三个勾** —— 古老牙齿 / 尘封魔典 / 欧洛巴斯之触）

> 用户要求：「在先古卡页中间部分的先古属性底下新增勾选"替换古老牙齿所替换的初始卡牌"，新增勾选
> "作为尘封魔典给的先古卡"，和遗物页的中间遗物属性底下新增勾选"替换欧洛巴斯之触所替换的初始遗物"」。
>（详见 `docs/RELEASE_NOTES_V0.2.260.md`）
>
> **本体机制**（`ArchaicTooth.cs` / `DustyTome.cs` / `TouchOfOrobas.cs`）：这三件道具都只认**自己写死的表**
> （原版初始卡 → 先古卡 / 原版初始遗物 → 强化遗物），自定义角色不在表里 →
> 古老牙齿会给「怀疑 Doubt」、欧洛巴斯之触会给「头环 Circlet」、尘封魔典则是随机挑一张先古卡。
>
> **做法**：`CardSpec.ToothReplace` / `CardSpec.DustyTomeGrant` / `RelicSpec.OrobasReplace` 三个开关；
> 新文件 `Generation/AncientItemPatchGen.cs`（有勾才写 `cs/AncientItemPatches.cs`）生成三个 Harmony 前缀，
> 都走本体自带的 public `SetupForTests(...)` / `AncientCard` 属性，且都 `if (player.Character is not 本模组角色) return true;`。
> 校验器管「同一种勾多张 = 错误 / 不是先古卡 = 警告 / 没有初始卡 = 警告 / 稀有度不搭 = 提示」。
>
> **验收**：自检 **4365 PASS / 0 FAIL**（+7）；探针工程三个勾全开 → `dotnet build` **0 error**；
> 对拍 Sample 113 / WhiteCloak 412 **逐字节相同**。
>
> ⚠ 过程中踩到的坑（已修）：Harmony 特性那一行用 `CodeWriter.Open()` 拼成了 `[...] {`（CS0116），
> 改成 `.Line(属性)` + `.Open(类)` 才编得过 —— 探针工程真编译就是靠这个抓住的。

## 第 151 批需求（V0.2.259：**「临时护卫」加了没用还越来越多** + 负荆棘提示）

> 用户报：「护卫这个 buff 加了临时没有用，而且越来越多。还有一个不知道怎么卡出来的 -2 层荆棘」
> （附 `CarmenMod.json` 1.27 MB + `godot2026-10-22T03.56.36.log`）。（详见 `docs/RELEASE_NOTES_V0.2.259.md`）
>
> **真因**：护卫 = 本体 `GuardedPower`，`PowerStackType.Single`（层数恒为 1、不显示）+
> `PowerInstanceType.Instanced`（每次施加 = 新实例）。工具却按「临时 +1 / 回合结束 -1」处理 →
> 每回合新挂一个实例、撤不干净 ⇒ 越来越多；它本身只是「受攻击伤害 ×0.5」，不给格挡 ⇒ 看着没用。
>
> **改法**：`TempPowersSource` 遇到 `Single` 状态（本体 `PowerEntry.IsSingle` / 自定义
> `IsSingleLayerCustomPower`）走**存在性语义**：没有才挂一个、到点 `PowerCmd.Remove` 整个摘掉；
> `Counter` 状态流程一个字不变。校验器加两条提示（单层标记类临时状态 / 不允许负层数的临时减益）。
>
> **负荆棘**：全文搜索该存档 `ThornsPower` / 荆棘 **各 0 次** ⇒ 不可能出自这份存档的生成结果，
> 多半是上一版 mod 残留（旧版还原会写 `Apply(-2)`，把本体状态推成负数）。这一版还原已一律夹到 0。
>
> **验收**：自检 **4358 PASS / 0 FAIL**（+3 条）；探针工程含「临时护卫 + 临时力量」→ `dotnet build` 0 错误；
> 对拍 Sample 113 / WhiteCloak 412 **逐字节相同**。

## 第 150 批需求（**参数 = 最大生命 / 当前生命 拼进公式时被 `??` 吃掉运算符（`P*0.04` 变成 `P`）**；已修，随 V0.2.259 一起发）

> 用户原话：「参数为最大生命时，造成 参数*0.04 的伤害时，还是最大生命数值的伤害」（另附 `godot2026-10-22T14.51.01.log`）。
>
> **真因**（`ForgeFormula.Emit` 的标识符那一支）：参数（来源 = 最大生命 / 当前生命）生成出来的片段是
> `x?.MaxHp ?? 0`，而 `??` 的优先级**比 `*` 低** —— 直接拼进公式就成了
> `x?.MaxHp ?? (0 * 0.04m)`，非空时等于 **满血值**。
>
> 生成出来的卡面动态变量（`ForgeFormulaDamageVar`）原样把它暴露出来：
> ```csharp
> // 修之前
> new ForgeFormulaDamageVar(static (card, target) => ((card.Owner.Creature)?.MaxHp ?? 0 * (0.04m))),
> // 修之后
> new ForgeFormulaDamageVar(static (card, target) => (((card.Owner.Creature)?.MaxHp ?? 0) * (0.04m))),
> ```
> 同一族写法全都会炸：`P + 5`、`P / 2`、`P > 10`、`P*0.04 + 1`…（只有 `P` 单独用是好的）。
> 实际出伤那一句（`DamageCmd.Attack(__pP * (0.04m))`）本来是对的 —— 错的是卡面 / 伤害预览那一份数值。
>
> **修法**（`ForgeFormula.cs`）：`EmitNode` 拼标识符片段时，**带 `??` 而且整体没被括号包着的**加一层括号
> （新助手 `NeedsParens`）；局部变量 `__pP`、已经带括号的内置量（`(x?.TurnNumber ?? 0)`）、
> `(decimal)(…)` 强转、函数调用一律原样 ⇒ **不含 `??` 的公式生成文本一个字符都不变**（红线）。
> ⚠ 这里第一版写宽了（凡是带 `??` 就加括号）→ 把内置量那几条也套成双括号，自检里 9 条按原文断言的跟着红；
> 收窄成「没被括号包着才加」后全绿。
>
> **验收**：
> * 探针 `_probe/parammul` **5/5 PASS**（① 卡面 `(x?.MaxHp ?? 0) * (0.04m)`；① 出伤那句照旧；② `P+5`、`P/2` 都包住；③ `X*2` 不加多余括号）；
> * `dotnet build` 全解决方案 **0 error**；
> * `--uicheck` **4358 PASS / 0 FAIL**（本次 +3 条：卡面 `??` 被包住 / 出伤那句照旧 / `X*2` 不加多余括号）。
>   另外**改了 3 条旧断言**（V0.2.164 的遗物 / 药水 / 自定义状态「数值公式内联」）——
>   它们原来断言的是**旧（错）文本** `Div(base.Owner.Creature?.MaxHp ?? 0, (2m))`，
>   现在是 `Div((base.Owner.Creature?.MaxHp ?? 0), (2m))`；语义没变，只是把被 `??` 吃掉的括号补上了。
>
> **要确认的一点**：如果那位用户看到的**不只是卡面数字**、敌人实际掉血也等于满血，
> 让他把 `自定义角色存档\<名字>.json` 发过来 —— 那是另一条路径，按他那张牌复现。

## 第 149 批需求（V0.2.258：**「或者清空一个组」和「目标状态」叠在一起**）

> 用户报：「指定状态的目标状态和选组的选项重叠在一起」。（详见 `docs/RELEASE_NOTES_V0.2.258.md`）
>
> **真因**：效果编辑器网格只有 2 列，V0.2.255 把新栏放在 `Grid.Column="2"/"3"` →
> 越界折回最后一列 → 压在「增益 / 减益」那个状态下拉上。
>
> **改**：网格加一行，新栏独占一行（列 0 标签 / 列 1 下拉）；仍然只在「清除指定状态」时可见。
> 生成端零改动。

## 第 148 批需求（V0.2.257：**「还是无法选进组」** —— 状态列表没通知刷新，一直是空的）

> 用户报：「还是无法选进组」。（详见 `docs/RELEASE_NOTES_V0.2.257.md`）
>
> **真因**：右侧状态列表按「当前选中的组」算，但切组时只 `Raise("GroupCardRows")`，
> 没通知 `GroupPowerRows` → 一直停在启动时那次求值（空表）→ 一行都没有，自然勾不了。
>
> **改**：① `OnGroupSelectionChanged` / `RaiseGroupsChanged` / `OnGroupSpecChanged` 都补通知；
> ② `SetPowerGroupMembership` 按 **Id** 在存档里重找组再写（旧实例写入会白写）；
> ③ 勾选后状态栏给出「已把状态「X」加入组「Y」（组里现在有 N 个状态）」。
>
> **自检**：+5 条；端到端实测：勾「争锋」→ 保存 → 存档 `PowerIds: ["NecrassPower1"]`。

## 第 147 批需求（V0.2.256：**组页面「状态列表」看不见** + **存档某项类型写错就整份读不进**）

> 用户报：「右边没有看到有两块勾选列表，还是只有卡牌」+「为什么弹窗 配置解析失败」。
>（详见 `docs/RELEASE_NOTES_V0.2.256.md`）
>
> **① 布局**：V0.2.255 那块「组里有哪些状态」是独立 Border，和卡牌块叠在同一单元格靠底对齐 →
> 窗口一矮就被裁掉。改成**并排两列**（左「有哪些牌 / 全选全不选」、右「有哪些状态」绿字标题），
> 两列各自滚动；已 1920×1080 实机截图确认。
>
> **② 配置解析失败**：`The JSON value could not be converted to System.String. Path: $.Version` ——
> 手写的存档里 `Version` 写成数字就整份读不进。现在 `ProfileFactory.JsonOpts` 加**宽容 string 转换器**
> （数字 / 布尔按原文当字符串读，写出仍是带引号字符串）；弹窗改成写清**文件 + 怎么改**；
> 启动列表里不再把 `*.forge_classmap.json` 当存档（刚才那个弹窗就是点它引起的）。
>
> **自检**：+2 条；实机截图核对；生成结果零变化。

## 第 146 批需求（V0.2.255：**状态进组 + 「清除指定状态」可以选组**）

> 用户要求：「能不能让状态进组，然后在清空指定状态的效果里可以选组」。
>（详见 `docs/RELEASE_NOTES_V0.2.255.md`）
>
> **做法**：`CardGroupSpec.PowerIds`（组里的状态，自定义 + 本体都能放；归属记在组这边 ——
> 本体状态没法往它身上加字段）；「组」页加第二块勾选「这个组里有哪些状态」；
> `EffectSpec.PowerGroup` + 「清除指定状态」右侧新栏「或者清空一个组」；
> 生成期把组展开成「每只目标 × 每个状态」各一句 `GetPower<T>() + PowerCmd.Remove`；
> 没选组时**逐字不变**；校验器管「空组 / 两边都没选 / 组优先」。
>
> **自检**：+4 条；对拍除内嵌图谱注释外逐字节相同。

## 第 145 批需求（**延迟状态类名撞名 → `cs\DelayedPowers.cs` 同一个类写两遍（CS0101 + CS0111）**；已修，随 V0.2.255 一起发）

> 用户转来别人的「构建失败的原因」弹窗：
> ```
> cs\DelayedPowers.cs(28,21): error CS0101: 命名空间"Dddddddog"已经包含"DaqgeForgeDelayedGenRnd"的定义
> cs\DelayedPowers.cs(33,32): error CS0111: 类型"DaqgeForgeDelayedGenRnd"已定义了一个名为
>                               "AfterPlayerTurnStart"的具有相同参数类型的成员
> ```
> （同屏那 5 条 `CS8981`「类型名只含小写 ascii」只是警告：`Cicici` / `talk` / `hahaha` 之类用户自己起的类名，不影响构建。）
>
> **真因**：延迟状态的**类名**和**去重键**不是同一套口径。
> * 类名 = 角色类名 + `ForgeDelayed` + 种类 + （指定卡 / `Rnd`+范围）—— 见 `Naming.DelayedPowerSuffix`。
> * 去重键（`CSharpCodeGen.CollectDelayedEffects`）还带着**生成到哪**（手牌 / 抽牌堆 / 弃牌堆）、**张数**
>   （`SpawnChoice`）、**附加处理**（升级 / 免费 / 本回合免费 / 带关键词）、**数值 = X**。
>
> 于是「下回合生成随机卡」两条效果只在这几个栏位上不同时：去重键认为它们是两条（都生成类），
> 类名却**一模一样** ⇒ 同一个类在同一个文件里写两遍 ⇒ CS0101 + CS0111。
> （代码里以前只修过「不同**种类**撞成同一个 `ForgeDelayedPower`」，没覆盖同一只效果的细分栏位。）
>
> **修法**（只改 `Naming.DelayedPowerSuffix` 的「生成类」分支）：把上面那几栏**只在不取默认值时**编进类名 ——
> `ToDraw` / `C2`（张数）/ `Up` / `Free` / `FreeTurn` / `UpTurn` / `Kw<关键词>` / `X`。
> 默认值（`SpawnTo=Hand`、`SpawnChoice=1`、附加处理全关、不 X）一个字都不加 ⇒
> **默认存档的类名不变**（守「默认档逐字不变」红线）；而会撞名的配置今天本来就编译不过，改名不影响任何能用的存档。
>
> **验收**：
> * 探针 `_probe/delaydup` **7/7 PASS**（① 默认类名仍是 …`ForgeDelayedGenRnd`；② 只差「生成到哪」；③ 只差附加处理；
>   ④ 只差张数 / X；⑤ 真生成一遍 `DelayedPowersSource`：6 条效果 → 类名全不重名、卡牌里引用的类都有定义）
> * 同一份探针挂到**已发布的 V0.2.254 部署版 Core.dll** 上跑 → **4 条 FAIL**，`DelayedPowers.cs` 里
>   `SevenForgeDelayedGenRnd` 重复、四种写法全塌成同一个名字 —— 原样复现用户那条 CS0101/CS0111；
> * `dotnet build` 全解决方案 **0 error**；`--uicheck` **4345 PASS / 0 FAIL**（新增 4 条，都 PASS：
>   `延迟状态类名（红线）：栏位全默认时还是老的 …ForgeDelayedGenRnd`、
>   `延迟状态类名：只差「生成到哪 / 附加处理 / 张数」→ 类名各自不同`、
>   `生成的 DelayedPowers.cs 里没有同名类`、`卡牌里引用的延迟类都在 DelayedPowers.cs 里有定义`）。
>   注：12:37 那次 Release 自检是 **4341 PASS / 0 FAIL**，正好少这 4 条（那份 Release 构建在加自检行之前）；
>   重新构建 Release 后应当是 4345。
>
> **临时办法（给那位作者，不换版本也能先过）**：把撞名的两条「下回合生效 + 生成卡牌（随机）」改成能区分的样子 ——
> 合并成一条 / 其中一条换个「范围限定」/ 一条改成「指定卡」/ 先关掉一条的「下回合生效」。
>
> **发版说明（可直接贴）**：延迟状态类名现在带上「生成到哪 / 张数 / 附加处理 / 数值 = X」——
> 以前两条只在这些栏位上不同的「下回合生成卡牌」会生成**同名类**，编译报
> `CS0101（命名空间已包含该定义）`+`CS0111（同名成员）`；默认配置生成的类名一个字都没变。

## 第 143 批需求（V0.2.254：**「死焰」不卡了但灾厄没上** —— 伙伴攻击没记 ThisCardDamage）

> 用户报：「死焰那张牌是不卡住了，但是灾厄也没上」+ 新存档（`examplemod.json` 453155 B）+ 日志。
>（详见 `docs/RELEASE_NOTES_V0.2.254.md`）
>
> **查证**：死焰第 3 条 = 「施加灾厄，数值 = `ThisCardDamage`」，生成读 `this.ForgeThisCardDamage`；
> 而该字段只在**普通攻击**那条路记账，死焰的伤害全在 `PetAttack`（`.FromPetAttacker(...)`）上 →
> 从没累加过 ⇒ 恒 0 ⇒ 施加 **0 层**灾厄（不报错、不卡牌）。
>
> **做法**：`EmitPetAttack` 按普通攻击同一套接住 `AttackCommand`，打完后
> `ForgeThisCardDamage += Results.SelectMany(...).Sum(r => r.TotalDamage)`（全体 / 随机 / 单体 / 全体单位 /
> 全部友方 / 自己 每一档都记）；只在**这张牌真的用到 `ThisCardDamage`** 时才生成（否则逐字不变）。
>
> **自检**：+3 条（含「记账发生在施加灾厄之前」的顺序断言）；对拍逐字节相同。

## 第 142 批需求（V0.2.253：**状态里的「升级卡牌」无玩家守卫 + 「全部」档 + 调试日志**）

> 用户澄清：「是自定义的状态具有升级卡牌的效果，但是用来升级能够无限升级的状态牌无效果」。
>（详见 `docs/RELEASE_NOTES_V0.2.253.md`）
>
> **查证**：状态链的 `UpgradeCard`（`CustomPowerGen`）对 31 个合法触发时机逐个试过，都能生成
> `CardCmd.Upgrade`，判据也已经是 `c.IsUpgradable && 范围`。所以这次补的是「为什么没反应看得见」：
> ① 状态贴在**没有玩家**的生物身上时 `base.Owner.Player` 为 null → 以前把 null 传进 `GetPile` 会空引用，
> 现在第一句就 `if (base.Owner.Player is null) return;`；
> ② 状态链的「升级卡牌 + 全部」以前被当成「随机挑 N 张」→ 补上 `foreach` 全升级；
> ③ 开「生成调试日志」会打出「这一摞里可升级的牌 N 张」（「没效果」时一眼看出是不是候选 0）。
>
> **自检**：+4 条；探针工程（含该状态 + 调试日志）真编译 0 错误；对拍逐字节相同。

## 第 141 批需求（V0.2.252：**「升级手上的状态牌」不生效** —— 本体状态牌不能升级，工具现在说出来）

> 用户报：「自定义状态为升级手上的状态牌不生效」。（详见 `docs/RELEASE_NOTES_V0.2.252.md`）
>
> **查证**：生成端「升级卡牌」本来就带 `c.IsUpgradable` 过滤（和本体「武装 Armaments」同口径），
> 而本体状态 / 诅咒牌 `MaxUpgradeLevel => 0`（`Dazed.cs:8`），`CardCmd.Upgrade` 会 `continue` 跳过 ——
> 所以对它们「什么都不做」。工具给自建状态牌也照抄了 `=> 0`，除非勾「可被无限升级」（V0.2.223）。
>
> **做法**：`ProfileValidator` 对「升级卡牌 / 升级卡牌（全局）」在范围 = 状态 / 诅咒
> （或「只升级这张卡」点名状态 / 诅咒牌）时给**提示**，说清「天生不能升级」+ 怎么让自建状态牌能升。
> 生成结果零变化。
>
> **自检**：+4 条；对拍逐字节相同。

## 第 140 批需求（V0.2.251：**条件「上一张打出的牌是某种范围的牌」的范围可填组** —— 改名 + 图上校验）

> 用户再提一次：「条件增加『上一张打出的牌是某种范围的牌』，范围可以填组」。
>（详见 `docs/RELEASE_NOTES_V0.2.251.md`）
>
> **查证**：这条条件 V0.2.247 就有（`LastCardScope`，范围与「范围界定」同一份候选，含 `Keyword:` / `Group:`），
> 卡牌页与锻造【条件】模块都有；探针实跑能生成按组展开的判据。
>
> **这一版补的**：① 条件名 / 锻造字段名改成直白写法（好找）；② 锻造页候选兜底
> `Owner ?? EffectCatalog.DisplayProfile`（画布先于存档建好时不会再只剩内置档，看不到组 / 关键词）；
> ③ 图上这两条条件也做校验：关键词没选 → 错误、范围留空 → 提示、空组 → 警告。
>
> **自检**：+3 条；对拍逐字节相同。

## 第 139 批需求（V0.2.250：**锻造页【效果】模块补「范围」** —— 能选组 / 特定关键词）

> 用户报：「这个范围怎么不能选组或者特定关键词」。（详见 `docs/RELEASE_NOTES_V0.2.250.md`）
>
> **查证**：卡牌页那一行「范围界定」是按存档动态取的（含「自定义关键词：…」「组：…」），
> 但**锻造页【效果】模块压根没有这一栏**（`ForgeGraphCatalog` 的效果字段表里没有 `SpawnFilter`）——
> 用户的卡都是锻造的，所以「范围」里永远只有内置那几档。
>
> **做法**：效果模块新增「范围（哪一类牌 / 哪个组）」（候选动态取 `SpawnFiltersFor(Owner)`）+
> 「只持续本回合（减少卡牌费用用）」；两栏按 `EffectFieldPolicy.UsesCardScopeFilter` / `Kind==CostDown` 显隐；
> `ForgeEffectBridge` 读写这两个字段；`ForgeGraphGen` 把 `CostDown` 加进 `SupportedEffectKinds` 并复用
> `CSharpCodeGen.EmitCostDownPublic`（`__fgPlayer` 上下文）。
>
> **自检**：+4 条；探针工程（含锻造卡）真编译 0 错误；对拍逐字节相同。

## 第 138 批需求（V0.2.249：**「减少卡牌费用」改成效果种类** —— 用户纠正位置）

> 用户纠正：「不是给自定义状态，而是增加在效果种类中」。（详见 `docs/RELEASE_NOTES_V0.2.249.md`）
>
> **做法**：`EffectCatalog.EffectKinds` 加 `CostDown`（「减少指定卡牌类别的费用」），
> 「范围界定」那一栏就是「哪一类牌」（复用 `SpawnFilter`，已加进 `EffectFieldPolicy.UsesCardScopeFilter`），
> 数值 = 减几费（0 = 变成 0 费），新增 `EffectSpec.CostDownThisTurn`（只持续本回合）。
> 生成 = 施加带载荷的内部状态 `{角色}ForgeCostDownPower`（按需生成 `cs/CostDownPower.cs`），
> 覆写本体 `TryModifyEnergyCostInCombatLate`（腐化 / 刺破帷幕同一个钩子），`Reduce` / `ScopeKind` /
> `Ids[]`（组与自定义关键词组在生成期展开成 ModelId 名单）/ `ThisTurn`。
> 不支持「下回合生效」（已加进 `NextTurnMeaningless`，自检断言更新为 68 种 / 11 种不支持）。
> V0.2.248 那套「自定义状态页的费用修正」保留（两种入口都在）。
>
> **自检**：+6 条；探针工程真编译 0 错误；对拍除内嵌图谱注释外逐字节相同。

## 第 137 批需求（V0.2.248：**减少指定卡牌类别的费用** —— 腐化 / 刺破帷幕那种）

> 用户原话：「减少指定卡牌类别的费用（类似腐化，刺破帷幕这些）」。（详见 `docs/RELEASE_NOTES_V0.2.248.md`）
>
> **做法**：`CustomPowerSpec` 加 `CostReduceScope`（范围 Id，空 = 不启用）/ `CostReduceAmount`（默认 1，0 = 变成 0 费）/
> `CostReducePerStack`（按层数）；`CustomPowerGen.EmitCostModifier` 生成本体的
> `AbstractModel.TryModifyEnergyCostInCombatLate`（腐化 Corruption / 刺破帷幕 Veilpiercer / 免费攻击 用的是同一个钩子），
> 判据复用 `ScopeCondForFilter`（含自定义关键词组 / 每个组）。界面：「自定义状态」页加「费用修正」一块。
> 只改你自己的牌、最低减到 0；空组 / 负数 / 0+按层数 各有校验提示。
>
> **自检**：+6 条；探针工程真编译 0 错误；对拍逐字节相同。

## 第 136 批需求（V0.2.247：**两条新条件** —— 上一张打出的牌「带有某关键词 / 是某范围（可按组）」）

> 用户原话：「条件增加『上一张打出的牌带有某关键词』，『上一张打出的牌是某范围』，范围内可以填组。」
>（详见 `docs/RELEASE_NOTES_V0.2.247.md`）
>
> **做法**：`EffectCatalog.Conditions` 加两条（`LastCardKeyword` / `LastCardScope`），
> `ConditionOption` 加 `NeedsKeyword` / `NeedsScope` 两个界面标志；`ConditionSpec` 加 `Keyword` / `Scope` 字段；
> 判据走本体 `History.CardPlaysFinished`（按玩家过滤、不限回合）取最后一张牌；
> 范围复用 `ScopeCondForFilter`（攻击/技能/能力/诅咒/状态/任务/自定义关键词组/**每个组**）。
> 界面：三个条件模板各加一行（关键词 / 牌的范围，按需出现）；锻造【条件】模块加同名字段；
> 回读（ProjectRecovery）也认这两条（组按卡集合反查）。
>
> **坑**：判据里不能用模式变量（同一张卡两条效果会 CS0128）→ 改成内联那一长串；真编译自检覆盖。

## 第 135 批需求（V0.2.246：**「形态生效、效果没生效」** —— 只叠一层状态的隐藏层数）

> 用户报：「一层没事，二层 boss 的时候多效卡又识别不了形态一，形态生效，效果没生效」+ 两份 godot 日志。
>（详见 `docs/RELEASE_NOTES_V0.2.246.md`）
>
> **查证**：多效卡 = 锻造图卡（`ZouCard10「自适应攻击-破坏」` 等），分支条件写成「【状态：手刃形态】== 1 层」。
> 本体 `PowerStackType.Single` 只是「层数不显示」（注释说 always 1），`PowerCmd.Apply` 第二次照样
> `ModifyAmount(+1)` → `SetAmount(2)`，`SetAmount` 对 Single 无夹取 ⇒ 内部层数变 2 ⇒ `== 1` 不成立、
> 整段效果被跳过（图标还在 = 「形态生效」）。
>
> **做法**：`ForgeGraphGen` 读「只叠一层」的自定义状态时按语义夹成 0/1
> （`(PowerAmount(...) <= 0m ? 0m : 1m)`），不碰模型本身（衰减/移除不受影响）；
> 另加一条提示：卡牌/遗物/药水的【状态】节点**没接【对象】**时默认读「打出时选中的那个敌人」。
>
> **自检**：+4 条；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 134 批需求（V0.2.245：**「翱翔」的减伤只对攻击伤害生效** —— 校验器说清）

> 用户报：「翱翔减伤给自己没用」。（详见 `docs/RELEASE_NOTES_V0.2.245.md`）
>
> **查证**：`SoarPower.ModifyDamageMultiplicative` 要两条同时成立 —— ① `target == base.Owner`；
> ② `props.IsPoweredAttack()`（= 带 Move 且不带 Unpowered）。而本工具生成的「自伤 / 失去生命 / 状态伤害」
> 全是 Unpowered（自伤与本体 献血/血墙 同口径；状态伤害是既有设计），所以对它们不减是**本体语义**。
> 另外翱翔本体是猫头鹰执政官的机制、落地时由它自己移除；工具施加的不会自动消失。
>
> **做法**：新增 `EffectCatalog.PowerMechanicNote`（「效果在什么条件下才触发」说明表），
> 校验器在施加 翱翔 / 昏眩 时把这段打出来（提示级、不拦生成）。生成结果零变化。
>
> **自检**：+3 条；对拍不受影响。

## 第 133 批需求（V0.2.244：**「狩猎」状态本身没有玩法效果** —— 校验器说出来）

> 用户报：「一层仪式鹿给的那个 debuff 晕眩和狩猎不会生效」+ 两份 godot 日志。（详见 `docs/RELEASE_NOTES_V0.2.244.md`）
>
> **查证**：① 「晕眩」是状态**卡** `Dazed`、仪式兽给的是 `RingingPower`（昏眩，1 层，给你所有卡牌挂耳鸣标记、
> 只拦带标记的牌）；② 「狩猎」= `TheHuntPower`，本体类注释写明它**只是视觉标记**，真正的逻辑在
> `TheHunt` 卡里 —— 全项目只有那张卡施加它、没有地方读它 ⇒ 单独施加当然没反应（zou 存档里是
> 卡牌 18「问号盒子」第 4 条）。昏眩那条配置（自定义状态 #5「天使形态」→ 玩家回合开始施加自己 1 层）
> 生成出来的代码是对的，问题只可能在本体的「只拦带标记的牌」这条规则上。
>
> **做法**：`EffectCatalog.PowerNoGameplayNote`（目前只收 `TheHuntPower`）+ 校验器**提示**：
> 「只是一枚视觉标记……想做『战斗结束多一份卡牌奖励』请用『获得卡牌奖励』+『战斗胜利后』」。
> 不拦生成（只是提示），生成结果一个字节都不变。
>
> **自检**：+2 条；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 132 批需求（V0.2.243：**「死焰」打出后浮空** —— 大限已至在多目标档要了 cardPlay.Target）

> 用户报：「死焰这张牌打出后浮空」+ 存档 `examplemod.json`。（详见 `docs/RELEASE_NOTES_V0.2.243.md`）
>
> **查证**：`NecrassCard20`（死焰）三条效果全是「全体敌人」（伙伴攻击 / 施加灾厄 / **大限已至**）⇒
> `TargetType.AllEnemies` ⇒ 打出时不弹选目标、`cardPlay.Target == null`；而 `EmitTimesUp` 从来不看
> 作用对象，第一句就是 `ThrowIfNull(cardPlay.Target)` → 打出瞬间抛 `ArgumentNullException`，
> OnPlay 中断、牌停在出牌位浮空、回合断掉（V0.2.217 同一个洞，犯在「大限已至」上）。
>
> **做法**：`EmitTimesUp` 按作用对象展开（单体照旧逐字节不变；全体敌人逐个 `.Targeting(__tuFoe)`；
> 随机 / 全体单位 / 全部友方 / 自己 各按自己那一档）；`EmitPetAttack` 的 `default` 分支同样改为按作用
> 对象展开（它以前「全体单位 / 全部友方 / 自己」都读 `cardPlay.Target`）。
> **结构性自检**：19 种效果 × 5 个多目标档逐格生成，断言源码里不出现 `cardPlay.Target`（第一遍就抓出 PetAttack 三格）；
> 另加一条「生成 → dotnet build 0 错误」的真编译断言。
>
> **自检**：+3 条（合计 4291 PASS / 0 FAIL）；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 131 批需求（V0.2.242：**那几条「本张牌这一次打出」的读数找不到**）

> 用户报：「那我怎么没看到 ThisCardBlock / ThisCardExhaust / ThisCardDiscard / ThisCardTransform / ThisCardGenerate」。
>（详见 `docs/RELEASE_NOTES_V0.2.242.md`）
>
> **查证**：功能一直在（`FormulaBuiltins` 有、生成端认、校验器认），是**界面发现性**问题：
> ① 参数参考窗的「内置实时读数」有 80+ 行，这 6 条排在最后，要滚到底才看得见；
> ② 只有锻造页的公式框有「填入参数…」，卡牌页公式框旁边什么都没有（名字只在 tooltip 里）。
>
> **做法**：`ParamReferenceWindow.BuildCategories` 把 `CardOnly` 那 6 条拆成**第一类**（窗口默认选中），
> 并从「内置实时读数」里剔除（每条只出现一次）；卡牌页四个公式框（数值 / 命中次数 / 生效次数两档）
> 各加一个「…」按钮（`App.OnInsertFormulaParam` 转发 → `MainWindow.InsertFormulaParamInto`），
> 菜单里卡牌专用那组排最上面，其次 X / Y、自定义参数、其余读数；「数值（公式）」tooltip 改成点名列出这 6 条。
>
> **自检**：+5 条；本版只动 App 项目，Core 源码零改动（生成结果与 V0.2.241 一致）。

## 第 130 批需求（V0.2.241 追加：**状态触发链的「作用对象」以前被完全忽略**）

> 用户报：「触发时机增加回复生命值时……」（消息只到这儿）。（详见 `docs/RELEASE_NOTES_V0.2.241.md`「追加」那一节）
>
> **查证**：`CustomPowerGen` 里「回复生命值 / 获得·失去格挡 / 失去生命 / 最大生命」四种效果以前
> **只写 `base.Owner`**，完全不读「作用对象」—— 界面那一栏明明摆着（`UsesTargetSide` 对这四种都为 true），
> 选「全体友方 / 单体敌人 / 全体敌人 / 全体单位」照样只动自己，且不报错（静默忽略）。探针一跑就复现：
> 四种作用对象生成的是同一句 `CreatureCmd.Heal(base.Owner, N)`。
> 顺带发现「最大生命」按 `base.Amount`（状态层数）判正负 ⇒ 填负数实际一直是加。
>
> **做法**：新增 `CustomPowerGen.EmitPowerTargets`（状态链专用，`base.Owner` 就是 Creature），
> 档位口径和 `EmitDamage` 一致（自己 / 单体敌人 / 全体敌人 / 全部友方（含两个子勾选框）/ 全体单位 /
> 随机敌人 / 指定队友退回自己）；四种效果共用。「自己」那一档输出逐字节不变；最大生命改按效果数值正负。
>
> **自检**：+8 条；对拍 Sample 113 逐字节相同，WhiteCloak 只有 `WhiteCloakPowerInsanity.cs` 变了
> （那段永远不会走的 `else`）。

## 第 129 批需求（V0.2.241 追加：**四条「张数」读数 —— 这张卡消耗 / 丢弃 / 变化 / 生成的卡牌数**）

> 用户原话：「这张卡丢弃的卡牌数、这张卡消耗的卡牌数、这张卡变化的卡牌数、这张卡生成的卡牌数有没有」。
> —— 以前**没有**：只有「本回合 / 本场累计」那几条（`DiscardedThisTurn` / `ExhaustedThisCombat` …），
> 「变化」连累计都没有。这一版补齐四条「这张牌**这一次打出**」的张数读数。
>
> **为什么不用战斗历史**（关键查证）：本体 `CardExhaustedEntry` / `CardDiscardedEntry` / `CardGeneratedEntry`
> **只带 Card（+Creator）、不带 CardPlay**，分不出是哪一次打出干的，也没有「变化」的记录 ——
> 所以只能沿用 `ThisCardDamage` / `ThisCardBlock` 那套**卡片属性 + 逐次记账**。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.241.md` 的第 6~8 节）：
> * `FormulaBuiltins` 加 4 条（`CardOnly: true`）：`ThisCardExhaust` / `ThisCardDiscard` /
>   `ThisCardTransform` / `ThisCardGenerate`；和另外两条共用 `CardReadingsAll` / `CardReadings` 位标志。
> * 生成端：`EmitExhaustCard` / `EmitDiscardCard` / `EmitTransformCard`（经 `EmitTransformBody`）/
>   `EmitGenerateCard` 各加一个 `countInto` 参数：逐张操作 `+1m`、整批操作按列表张数 `+=`；
>   卡牌页 + 锻造图两条链都记账（图上用 `thisBlockCap` 同款的 `ctx.IsCardPlay` 门）。
> * 卡面：`LocalizationGen` 对「施加状态 + 公式正好是某条张数读数」印成
>   「施加等同于本牌消耗/丢弃/变化/生成张数的 X。」。
> * 校验器：`ValidateCardReadings` 里按读数 → 前置效果种类的映射（Damage / Block / ExhaustCard /
>   DiscardCard / TransformCard / GenerateCard）判「前面有没有」；其余五条规矩照旧共用。
> * 回读：`ProjectRecovery` 的跳过规则扩到 `ForgeThisCard*`（清零点 + 计数行），
>   但**放行** `ForgeThisCardBlock += await CreatureCmd.GainBlock(`（那是格挡效果的识别行）。
>
> **踩过的坑**：计数**不能**写成 `{ stmt; 计数; }` 挤在同一行 —— 回读按「这一行以 `await CardCmd.Exhaust(`
> 开头」认效果，包成块会让那条效果整条认不出来（探针实测 2 处「没认出来」）。现在一律单独一行，
> `foreach` 展开成多行花括号块。
>
> **验收**：① `--uicheck` **4274 PASS / 0 FAIL**（新增 13 条断言）；② 六张卡的探针存档（自己选 / 随机 /
> 全部 各分支）`--build` **0 错误** + 导出 PCK OK；③ `--recover` 四条公式都认得回来、没认出来为 0。

## 第 128 批需求（V0.2.241：**自定义参数加一条「这张牌获得的格挡」`ThisCardBlock`**）

> 用户原话：「自定义参数能不能加一个这张牌获得的格挡」。
> —— 和 V0.2.194 那条 `ThisCardDamage`（本张牌这一次打出造成的伤害）是**一对兄弟读数**：
> 同一套属性机制（打出时清零、每条对应效果之后累加）、同一套限制，取数换成本体
> `CreatureCmd.GainBlock(...)` 的**返回值**（走完 `ModifyBlock` 之后**实际拿到**的量，含敏捷等加成，
> `CreatureCmd.cs:680-700`）。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.241.md`）：
> * `FormulaBuiltins` 加一条 `ThisCardBlock`（`CardOnly: true`，表达式 `((decimal)this.ForgeThisCardBlock)`）；
>   和 `ThisCardDamage` 共用 `CSharpCodeGen.CardReadingsAll` / `MentionsReading` / `CardUsesReading` /
>   `IsExactReading` 这一套（检测 / 校验 / 卡面三处都按同一份表走，不各写一遍）。
> * 生成端：卡片类上生成 `public decimal ForgeThisCardBlock`，打出时清零；每条「获得格挡」之后
>   `+= await CreatureCmd.GainBlock(...)`（返回值就是实际拿到的量）；卡牌页 + 锻造图两条链都记账
>   （图上用 `blockAcc` 前缀，只在【触发时机】=卡牌被打出时那块布上）。
>   **失去格挡 / 下回合生效 / 牺牲伙伴换的格挡都不记账**。
> * 卡面：`LocalizationGen` 对「施加状态 + 公式正好是 ThisCardBlock」→「施加等同于本牌获得格挡的 X。」；
>   「伤害 + 公式正好是 ThisCardBlock」→「造成等同于本牌获得格挡的伤害。」；
>   其它种类给一条**提示**（能生效但卡面印 0，建议自定义描述）。
> * 校验器（与伤害那条共用一份）：非卡牌链 / 图上非卡牌 → 错误；卡牌时机钩子 → 错误；下回合生效 → 错误；
>   整张牌条件 → 错误；和别的量混写 → 错误；前面没有「获得格挡」→ 警告。
> * 回读：`ProjectRecovery` 认 `ForgeThisCardBlock += await CreatureCmd.GainBlock(` 这个前缀、
>   跳过 `ForgeThisCardBlock = 0m;` 这句（公式本体走既有的 `CET:AmountFormula=` 标记）。
>
> **验收**：① `--uicheck` **4261 PASS / 0 FAIL**（新增 22 条断言）；② 手填 + 锻造图两份探针存档
> `--build` **0 错误** + 导出 PCK OK；③ `--recover` 认回来、`Unparsed` 空；④ 红线：没用这条读数的牌一字不多。
>
> ⚠ 环境：本轮构建时 `…\Sts2CharForge.Core\obj\*.tmp` 报「拒绝访问」——是工作区那几层目录对当前用户的
> Windows 文件权限缺了一项，按内置排查脚本补齐（obj / Core / src 三处，报告在 `_acl_repair_out\`）后恢复。

## 第 127 批需求（V0.2.240：**小屏适配** —— 窗口压进屏幕 + 整窗可缩放）

> 用户反馈：「有一些人的屏幕小，就会发生有一些地方被遮挡，怎么办」（截图：效果编辑区底部被切）。
>（详见 `docs/RELEASE_NOTES_V0.2.240.md`）
>
> **查证**：主窗尺寸写死 1420×880，没有 MinWidth/MinHeight、没有按屏幕修正 —— 1366×768 这类屏上
> 右边和下边直接被屏幕边切掉（窗口内滚动条滚不出屏幕外，所以那些控件点不到）。
>
> **做法**：① `FitWindowToScreen()` 按 `SystemParameters.WorkArea` 收窗口 + 补 MinWidth/MinHeight；
> ② 根布局 `RootPanel` 加 **LayoutTransform** 整窗缩放（RenderTransform 不改布局、还会糊文字），
> 顶栏加「界面缩放」下拉（60~125%）并记到 `%LOCALAPPDATA%\Sts2CharForge\ui-scale.txt`；
> ③ `AutoUiScaleFor(宽,高)` 按屏自动挑档（≥900→100、≥800→90、≥700→80、≥620→75、否则 70；窄屏再降，
> 最高 80%）；④ 「选择配置存档」/「参数参考」两个独立窗也按可用区域夹一次。
>
> **自检**：+7 条；本机 1920×1080 强制 80% 截图确认底部完整可见。

## 第 126 批需求（V0.2.239：自定义状态「**允许负层数**」——层数能到负值）

> 用户问：「在自定义状态增加状态层数能否到负值」。（详见 `docs/RELEASE_NOTES_V0.2.239.md`）
>
> **查证**：以前不能。`EffectCatalog.AllowsNegativeLayers` 里只有本体写死的 5 个（力量/敏捷/专注/尖啸/萎缩），
> 自定义状态一律走「先 GetPower 再 ModifyAmount 扣已有层数」（扣到 0 就移除），层数永远到不了负值。
>
> **做法**：`CustomPowerSpec.AllowNegativeLayers`（默认 false）→「自定义状态」页加勾选框；
> 勾了生成的状态类带 `public override bool AllowNegative => true;`，并且 `SetCustomPowers` 第三个字段
> 把它登记进 `AllowsNegativeLayers` —— 于是五条链的「施加负数」都改成「直接施加负值」（本体尖啸那套），
> 「临时增益」的负数分档也一致。校验器那三处「本体允许 / 不允许」的文案按「本体 / 你勾了的 / 你没勾」
> 三类分开说（`ProfileValidator.NegativeLayerNote`）。
>
> **自检**：+6 条；对拍 Sample 113 / WhiteCloak 412 逐字节相同（默认 false，一个字符都不多写）。

## 第 125 批需求（V0.2.238：**「生效次数」也能写公式**（遗物 / 药水 / 状态 / 额外资源量））

> 用户问：「生效次数可以填写公式吗」。（详见 `docs/RELEASE_NOTES_V0.2.238.md`）
>
> **查证**：卡牌链 + 锻造【效果】早就能写（`TimesFormula` / `ForgeTimesText`）；遗物 / 药水 / 状态 /
> 额外资源量只有固定框，而且校验器会拦（报「次数公式只做在卡牌上」）。拦人的背后有真原因：
> `CountFormulaExpr` 生成的表达式要读 `__fgPlayer` / `__pM` / `__x` 这些**卡牌前置**局部变量。
>
> **做法**：`EmitRepeated` 加 `inlineProfile / inlinePlayer / inlineSelf` → 遗物 / 药水走
> `ForgeFormula.InlineResolver` 内联求值（和它们的「数值公式」同一套）；新增
> `InlineCountFormula`；`CustomPowerGen` 的 `for (int __rep …)` 上界也改成 Clamp 现算。
> 回读标记 `CET:CountFormulas` 由 `EmitCountFormulaMarker` 统一发（三条链也写，否则反推存档会丢公式）。
> 界面：那一行从单框改成「固定次数 + 生效次数（公式）」并排。校验器改成**按栏位**拦：
> 「生效次数」五条链放行；「命中次数 / 复制 / 重放 / 概率」这几条链生成端不读，仍然报错并说清。
>
> **自检**：+6 条；对拍 Sample 113 / WhiteCloak 412 逐字节相同。

## 第 124 批需求（V0.2.237：**临时增益的还原造出「-1 层」状态**）

> 用户报：「自带减层的效果加上临时之后只要层数是 1，回合结束就会扣成 -1。」（详见 `docs/RELEASE_NOTES_V0.2.237.md`）
>
> **查证**：临时状态类照抄本体 `TemporaryStrengthPower.cs:153` 的**盲还原**（`Apply(-base.Amount)`）。
> 本体安全是因为它只配 力量/敏捷/专注（自己不扣层）；而本工具允许把任何状态做成临时，
> 虚弱/易伤/脆弱本体在各自 `AfterSideTurnEnd` 里 `TickDownDuration`（`WeakPower.cs:49` 等）——
> 临时这 1 层被它自己扣光、状态归 0 被移除后，我们再 `Apply(-1)`，
> 而 `PowerModel.ApplyInternal` 直接 `SetAmount((int)amount)` **不夹 0** → 凭空出现 -1 层。
>
> **做法**：新增 `CSharpCodeGen.EmitTempPowerRevert` —— 还原前先 `GetPower<T>()`（没了就什么都不做），
> 还原量夹到「不跨过 0」，`AllowNegative` 的状态不夹（负层数是力量/专注的正常形态）。
> 「回合结束撤 / 下回合开始撤」两档共用这一份守卫；判据用运行时的 `AllowNegative`，自定义状态也覆盖。
>
> **自检**：+4 条；对拍 Sample 113 / WhiteCloak 412 逐字节相同（两份都没有临时增益）。

## 第 123 批需求（V0.2.236：调试日志补「第几次打出」，投斧这类改 playCount 的东西可验证）

> 用户报：「序号 48 卡无法生效头斧」+ 存档 `magete.json`。（详见 `docs/RELEASE_NOTES_V0.2.236.md`）
>
> **查证**：48 号 = 禁忌·多重存在 = **施加 `EchoFormPower` 1 层**的能力牌；
> 投斧（`ThrowingAxe`）的 `ModifyCardPlayCount` 是「每场战斗**第一张**牌 +1、不看牌型」，
> 而 `CardModel.OnPlayWrapper` 的 `for (i < playCount)` 对能力牌**照样**跑第二遍 OnPlay
>（Power 只是换飞牌特效）；`EchoFormPower` 是 `Counter`，第二次 `PowerCmd.Apply` 会叠到 2 层。
> ⇒ 机制上「投斧 + 48 号卡」= 多层存在 2 层；只看到 1 层 ⇒ 这一次 `playCount` 没被改成 2
>（最典型：那一场已经先打出过别的牌，投斧的加成被用掉了）——但**界面上看不出来**（能力牌打完就进 PileType.None）。
>
> **做法**：`DebugLogGen.PlayCount(w)` + `CSharpCodeGen.CardSource` 在 `OnPlay` 开头发射一行
> `[模组调试] … → 打出第 k/K 次`（只读 `cardPlay.PlayIndex` / `cardPlay.PlayCount`）。
> 开关关着时一个字都不写（红线①）；行尾带 `CET:DebugLog`，反推存档按行跳过。
>
> **自检**：+3 条（开着 / 带标记 / 关着干净）。

## 第 122 批需求（V0.2.235：**补完「多目标 + 调试日志」的 CS0103**：状态链的 other）

> 用户回：「allunits 也会同款报错」。（详见 `docs/RELEASE_NOTES_V0.2.235.md`）
>
> **查证**：`ally` 与 `unit` 是同一个 bug 的两半（V0.2.233 及以前都有；V0.2.234 修了卡牌 / 遗物链）。
> 这一版补掉最后一处：**自定义状态链** 的「临时增益 + 全体单位 / 全部敌人」——那行日志同样打在
> `foreach (Creature other …)` **外面**、却引用 `other` → `CS0103: other`（全体单位/全部敌人两档都会）。
>
> **做法**：`CustomPowerGen` 的 `TempPower` 那处 `DebugLogGen.Target` 改用 `groupZh`
> （「全体单位（逐个动手）」/「全部敌人（逐个动手）」），不再引用循环变量；「执行前/执行后层数」那种
> 循环内的日志不动；调试日志关着时仍然一个字不写。
>
> **验收**：探针 `sacprobe dbgally` 现在覆盖 卡牌链（全部友方/全体单位 × 格挡·施加状态·临时增益·回复生命·失去生命·
> 造成伤害·击晕）+ 遗物链同套 + 状态链（临时增益 × 全体单位/全部敌人），生成工程 `dotnet build` **0 error**；
> 自检新增 2 条；`--uicheck` **4218 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.234）示例 113 / WhiteCloak 412 只差 `.sln`。
>
> **给用户的说明**：V0.2.233 里 ally/unit 都坏；V0.2.234 修 ally + 卡牌/遗物的 unit；V0.2.235 修状态链，
> 三条链现在全覆盖。要**重新生成工程**（不是只重启程序）。
## 第 121 批需求（V0.2.234：**修「开着调试日志时全部友方 / 全体单位导致 CS0103: ally」**）

> 用户发来构建日志：40 条 error，全是 `CS0103: 当前上下文中不存在名称"ally"`
> （`cs/Cards/VitaCard21.cs(48,160)`、`VitaCard23.cs(23,89)` 等）。详见 `docs/RELEASE_NOTES_V0.2.234.md`。
>
> **真因**：「作用对象 = 全部友方 / 全体单位」是**逐个动手**（`foreach (Creature ally …)`），而
> 「生成调试日志」那一行打在**循环外面**，却把目标名写成了 `(ally)?.Name` → 引用未声明的循环变量。
> 只要开着调试日志又用这两档，整个模组编译不过（全体单位那档同理报 `unit`）。
>
> **做法**：
> 1. `DebugLogGen` 的 `Target / TargetInline / LogText / PrintStatement` 新增 `groupZh` 参数：
>    非空时把「目标」写成一句固定说明（`目标=[全部友方（逐个动手）]`），**不再拼任何 C# 变量**；
> 2. `CSharpCodeGen.MultiTargetGroupZh(e)`（`AllAllies` → 全部友方（逐个动手）、`AllUnits` → 全体单位（逐个动手））+
>    `IsMultiTargetSide` 改成用它判断；
> 3. 卡牌 / 遗物链 11 处 `inLoop: IsMultiTargetSide(e)` + 1 处 `inLoop: e.TargetSide is "AllAllies" or "AllUnits"`
>    （临时增益那档）全部换掉；药师「施加状态 · 执行前/后层数」那种本来就在循环里的日志不动；
> 4. 红线：单目标档形状没动、调试日志关着时一个字符不多写。
>
> **验收**：探针 `sacprobe dbgally`（两种多目标 × 格挡/施加状态/临时增益/回复生命/失去生命/造成伤害 + 单目标对照）
> 生成工程 `dotnet build` 0 error；自检新增 6 条；`--uicheck` **4216 PASS / 0 FAIL**；
> 老存档对拍（新 Core ↔ V0.2.233）示例 113 / WhiteCloak 412 只差 `.sln`。
>
> **备注**：用户那句「能力牌不触发头斧」已查明是本体遗物行为（`ThrowingAxe` 不按牌型过滤，
> 但「多打一次」对只叠一层的状态看不出效果），不需要改工具。
## 第 120 批需求（V0.2.233：**修遗物「获得金币时」不生效**）

> 用户报：「获得金币时，这个判定，无法触发」→「无法正常生效」。（详见 `docs/RELEASE_NOTES_V0.2.233.md`）
>
> **查证**：触发时机本身没问题 —— 生成的是 `public override async Task AfterGoldGained(Player player)`，
> 本体 `PlayerCmd.cs:169` 的 `Hook.AfterGoldGained` 会调它（战斗奖励 / 事件 / 联机同步 / 遗物全走这条路）。
> **真因**：这条签名**没有 choiceContext**，而抽牌 / 消耗 / 丢弃 / 升级 / 变化 / 拿牌 / 预见 / 伤害 / 施加状态 /
> 生成卡牌 / 卡牌奖励 / 召唤伙伴都要它 → 生成端以前只写一行「该触发时机不支持…已忽略」，
> 于是**触发时机触发了、里面什么都没做**。
>
> **做法**：
> 1. 新增 `CSharpCodeGen.BootstrapContextRuntime(triggerId)`（目前只有 `GoldGained`）+
>    `RelicRuntimeContextLines()`：按运行时造上下文 —— 战斗中 `HookPlayerChoiceContext`
>    （本体 Hook.cs 派发同款，联机时序才对）/ 战斗外 `BlockingPlayerChoiceContext`（本体注释点名的场景，空实现不会卡）；
> 2. 单时机（`RelicSource`）与多时机（`EmitRelicCtx`）两条路都接上，同一方法只造一次；
>    不用上下文时**一个字都不多写**（红线）；
> 3. 校验器那条「缺 context → 会被忽略」的提醒改用生成端同一份判据（`RelicNeedsChoiceContext`），
>    并且不再对「获得金币时」误报；「召唤伙伴」那条警告改说「可能在战斗外」。
>
> **验收**：探针 `goldctx` / `goldmulti` 生成工程 `dotnet build` 0 error；自检新增 7 条；
> `--uicheck` **4210 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.232）示例 113 / WhiteCloak 412 只差 `.sln`。
>
> **遗留**：锻造节点图的「获得金币时」仍然拦需要上下文的效果（图里没有可传的上下文），提示让用户挪到别的时机。
## 第 119 批需求（V0.2.232：**修「没看到『会被下一个自定义音频顶替』那个勾」**）

> 用户回：「没看到会被下一个自定义音频顶替（不勾 = 允许几段音频叠着响）这个」。
>（详见 `docs/RELEASE_NOTES_V0.2.232.md`）
>
> **真因**：那个勾的显隐绑 `EffectSpec.ShowSfxReplaceRow`（从 `Kind` 算），而 `Kind` 的 setter 里
> 把「跟着种类变」的属性挨个 `Raise` 时**漏了它** → 把种类改成「播放音频文件」时那一行永远是
> `Collapsed`（WPF 绑定不会自己重算）。「音频文件」那一行因为通知了，所以照常出现。
>
> **做法**：`Kind` 的 setter 里补一句 `Raise(nameof(ShowSfxReplaceRow));`（生成端一个字节没动）。
>
> **验收**：自检新增 3 条 —— ① 改种类时必须发出该通知（修之前必红）② 换回别的种类时同样通知且变回 false
> ③ 界面树上确实绑着它；`--uicheck` **4203 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.231）
> 示例 113 / WhiteCloak 412 只差 `.sln`。
>
> **教训（以后加「按种类显隐」的行必须同步做两件事）**：`EffectFieldPolicy` 那份允许清单 + `Kind` setter 里的
> `Raise`。少一样就是「界面摆了但不出现」或「出现了但生成端不读」。
## 第 118 批需求（V0.2.231：**「播放音频文件」底下加「会不会被下一个自定义音频顶替」**）

> 用户要求：「音频效果底下增加音频是否被下一个自定义音频顶替」。（详见 `docs/RELEASE_NOTES_V0.2.231.md`）
>
> **老行为**：每次播放都新开一个 `AudioStreamPlayer`（挂场景根、放完 QueueFree）→ 几段音频**叠着响**。
>
> **做法**：
> 1. `EffectSpec.SfxReplace`（默认 false；`JsonIgnore(WhenWritingDefault)` 守住老存档逐字不变的红线 ——
>    锻造图节点的快照里内嵌着 `EffectSpec`）；
> 2. `CSharpCodeGen.UsesSfxReplace(profile)`（有任何一条勾了才算「用到」）+
>    `SfxRegistrySource` 生成 `cs/ForgeSfx.cs`（`Replaceable` 列表：新的开始时先停掉旧的）；
> 3. `EmitPlaySoundFile(w, e, p)`：用到时**所有**自定义音频都走 `<角色>ForgeSfx.Play(路径, 音量, replaceable)`
>    （卡牌 / 遗物 / 药水 / 状态 / 锻造图五处调用点都传了 `p`）；没用到时逐字不变；
> 4. 标记行多一项 `CET:SfxReplace=1`（勾了才写），`ProjectRecovery` 认它、并把
>    `ForgeSfx.Play(` 那几行算进「整段跳过」；
> 5. 「效果」编辑器的音频那一栏底下加勾选框（只在 `PlaySoundFile` 上显示）；
>    `EffectFieldUsage` 加一条探针 + `EffectFieldPolicy.UsesSfxReplace` + 自检那张「显示 == 生成端读」的表；
> 6. 探针踩到并修掉的坑：`Baseline` 必须给 `Sfx` 一个有效值（否则「播放音频文件」整条只剩一句注释，
>    这一栏会被测成「界面摆了但没用」）；`Open("{")` 会开两个块（CS1513），必须 `Open("")`。
>
> **验收**：探针 `sacprobe sfxreplace` 生成工程 `dotnet build` 0 error + 回读两个方向都对；
> 自检新增 14 条；`--uicheck` **4200 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.230）
> 示例 113 / WhiteCloak 412 只差 `.sln`。
## 第 117 批需求（V0.2.230：**自定义地图上画线的「蜡笔」颜色**）

> 用户问：「能不能自定义在地图上的蜡笔的颜色」。（详见 `docs/RELEASE_NOTES_V0.2.230.md`）
>
> **引擎依据**：地图上画出的每条线都是 `Line2D`，颜色取自
> `NMapDrawings.cs:321` 的 `line2D.DefaultColor = player.Character.MapDrawingColor;`；
> `CharacterModel.MapDrawingColor`（`CharacterModel.cs:187`）默认 `Colors.Black`，
> 本体各角色的专属色：铁甲 `CB282B` / 静默 `2F6729` / 故障 `0D638C` / 亡灵 `AC0486` / 摄政 `935206`。
> 是虚属性 ⇒ 角色类里一个 override 就够，**不需要补丁**。
>
> **做法**：
> 1. `CharacterProfile.MapDrawingColor`（hex 字符串，默认空）；
> 2. `CSharpCodeGen.CharacterSource` 里条件发射
>    `public override Color MapDrawingColor => new Color("RRGGBB");` —— **留空一个字不写**（老存档逐字不变）；
> 3. 「角色」页新增一行「地图画线颜色（蜡笔）」：文本框 + `HexBrush` 实时色块 + 提示（列出本体五个角色的颜色）；
> 4. 校验器：非法十六进制 → **错误**；8 位但透明度 `00`（画出来看不见）→ **警告**；留空一条都不出；
> 5. 自检 6 条：输入框绑定 / 红线（留空不写）/ 去 `#` 转大写 / 8 位 / 校验器三档 / 存档往返。
>
> **验收**：探针 `sacprobe mapcolor` 生成工程 `dotnet build` 0 error；`--uicheck` **4186 PASS / 0 FAIL**；
> 老存档对拍（新 Core ↔ V0.2.229）示例 113 / WhiteCloak 412 只差 `.sln`。
## 第 116 批需求（V0.2.229：**卡面缩小档重做 —— 上一版勾选框实测等于没缩**）

> 用户回复 V0.2.228 的勾选框：「这个好像没什么区别」。（详见 `docs/RELEASE_NOTES_V0.2.229.md`）
>
> **他说得对**：上一版缩到 1000×760（显示尺寸 4×），而卡面在游戏里只画 250×190（先古卡 299×421），
> 纹理**没有 mipmap**（工程无 `.import`，Godot 默认 `mipmaps/generate=false` + Linear 滤镜）⇒
> 引擎每输出像素只双线性采样 4 个纹素：缩 4 倍时 16 个纹素只用 4 个 —— 和「不缩」完全等价。
> 探针 `sacprobe imgcmp`（新）实测 RMSE vs 理想面积平均：直接缩 **3.42** / 预缩 4× **3.42**（一样）/
> 预缩 3× 5.53 / **预缩 2× 0.27** / 预缩 1× 0.00。
>
> **做法**：
> 1. `ArtSpec.CardArtShrink`（`""` 原样 / `1x` / `2x` / `4x`）+ `ArtGenerator.CardArtShrinkModes` /
>    `CardFaceDisplaySize(ancient)`（普通 250×190、先古 299×421）/ `EffectiveCardArtShrink(art)`；
> 2. 目标尺寸 = 显示尺寸 × 倍率，`ShrinkCardArt` 按 `max()` 保持比例缩（面积平均、**只缩不放大**、原图不动）；
> 3. 老存档兼容：`FitCardArtToFrame = true`（V0.2.228 勾选框）⇒ 映射成 **2×**（不用再点一次）；
> 4. 界面：「美术资源」页换成下拉框（四档，2× 标「推荐」、4× 标「和没缩几乎一样」）+ 下面一行 `CardArtShrinkHint`；
>    预览下面的分辨率说明跟着档位改口；
> 5. 顺带改正注释：`%Portrait` 是 `stretch_mode=5`（KEEP_ASPECT_CENTERED，装进去不裁边），不是 COVERED。
>
> **验收**：自检新增 13 条；`--uicheck` **4180 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.228）示例 113 /
> WhiteCloak 412 只差 `.sln`（默认档仍「原样拷贝」）。
## 第 115 批需求（V0.2.228：**卡面分辨率过高时「进生成器就变糊」**）

> 用户问：「卡牌的卡面如果上传的画质分辨率过高，传进生成器时画质会被压缩导致很模糊怎么办」。
>（详见 `docs/RELEASE_NOTES_V0.2.228.md`）
>
> **查清的结论（工具里**没有**压缩）**：上传只记路径；生成时 `FsSafe.Copy` 逐字节拷贝（自检钉了哈希一致）；
> 生成工程的 PNG 没有 `.import`，Godot 4.5 默认 `compress/mode=0`（无损）/ `mipmaps/generate=false` /
> `process/size_limit=0`（用本机 Godot 实测）。
> **真正的原因**：游戏里卡面那块只有 1000×760（先古卡 606×852），超出的像素是引擎**运行时**用双线性缩掉的
> （没有 mipmap），图越大越糊；另外工具里的预览以前也是 WPF 默认双线性缩进 190px 的框。
>
> **做法**：
> 1. 三处卡面预览加 `RenderOptions.BitmapScalingMode="HighQuality"`（预览不再自己糊）；
> 2. 卡面预览下多一行分辨率说明（只在图比显示区域大 1.25 倍以上时出现）：写清「引擎运行时缩掉」
>    + 两条出路（换 1000×760 的图 / 勾下面那个框）；
> 3. 「美术资源」页新增勾选框 **「卡面过大时高质量缩小到游戏显示尺寸」**（默认关闭 = 老行为逐字不变）：
>    生成时用面积平均把过大的卡面缩到「刚好盖住显示区域」（普通卡 1000×760、先古卡 606×852，
>    比例不同按 `max(frameW/w, frameH/h)`），**只缩不放大**、**原图不动**，日志写清缩了多少；
> 4. `ArtSpec.FitCardArtToFrame`（默认 false）+ `ArtGenerator.FitCardArtToFrame()`；自检 12 条。
>
> **验收**：`--uicheck` **4174 PASS / 0 FAIL**；老存档对拍（新 Core ↔ V0.2.227）示例 113 / WhiteCloak 412
> 只差 `.sln`；探针工程（带逐帧动画那份）用本机 Godot `--headless --import` 实测导入参数无损。
## 第 114 批需求（V0.2.227：**修「篝火会卡死」（联机）**）

> 用户实测报的：「篝火会卡死」，附 Godot 日志。日志里唯一的关键错误：
> `NullReferenceException at NRestSiteCharacter.FlipX() ← NRestSiteRoom._Ready`。
>（详见 `docs/RELEASE_NOTES_V0.2.227.md`）
>
> **真因**：逐帧动画那张篝火补丁以前是「前缀补丁 `return false` 跳过本体 `_Ready`」（怕本体把非 Spine 子节点
> 包成 `MegaSprite` 抛异常）。但本体 4.5.1 那段是按 `GetClass() == "SpineSprite"` 过滤的（非 Spine 是空循环，不会抛），
> 而这个 `_Ready` 还要初始化 `_controlRoot` / `Hitbox`（命中区、悬停、点击）/ `%SelectionReticle` / 两个气泡锚点。
> 跳过 → `_controlRoot` 一直 null；联机时 `NRestSiteRoom._Ready` 对奇数位角色调 `FlipX()` → 空引用 →
> 房间 `_Ready` 后半段（生成选项 / 连继续按钮 / 同步器事件）全被打断 = **篝火没选项、点不了继续**。
>
> **做法**：
> 1. 篝火 `_Ready` 补丁改成「先挂帧动画、再检查场景结构，齐了 `return true` 放行本体」；
>    只有场景结构不齐（自己换过篝火场景）才仍跳过，并写日志说明；
> 2. 新增 `NRestSiteCharacter.FlipX` 兜底补丁：`_controlRoot` 为 null 时跳过翻面（只拦本工具的角色，
>    反射读私有字段失败就退回「照常执行」）；
> 3. 非 Spine 篝火立绘在 `FlipX` 之后补一次水平镜像（本体只翻 `SpineSprite` 子节点）；
> 4. 商店补丁保持「跳过本体」（`NMerchantCharacter._Ready` 只做包 Spine 这一件事）。
>
> **验收**：自检新增 4 条；探针 `sacprobe anim` 生成整份带逐帧动画的工程 `dotnet build` 0 error；
> 老存档对拍（新 Core ↔ 已发布 V0.2.226 的 Core）示例 113 / WhiteCloak 412 个文件只差 `.sln`；
> `--uicheck` **4162 PASS / 0 FAIL**。
>
> **备注**：探针 `_tmp\sacprobe\Program.cs` 在这一轮里被 PowerShell 的 `WriteAllLines` 清空过，
> 已按需要的模式（`gen` / `anim`）重写成 ASCII 单文件；以后只许用文件工具改它。

## 第 113 批需求（V0.2.226：**「复制卡牌」能复制自己 / 自己选从哪一摞复制 / 复制到哪一摞**）

> 用户要求：「复制效果能够复制自己或者可以选择从哪里选择复制并且到哪里」。
>（详见 `docs/RELEASE_NOTES_V0.2.226.md`）
>
> **做法**：
> 1. 数值 0 = 复制**这张牌自己**（`CSharpCodeGen.AmountZeroIsSelf` 加 `CopyCard`）：先 `this.CreateClone()`，
>    再 `CardPileCmd.AddGeneratedCardToCombat(…, 目标那一摞, base.Owner)`；份数 = 「复制的份数」；
> 2. 「放到哪一摞」对复制卡牌生效（`EffectSpec.KindUsesPlaceTo` 加 `CopyCard`）——
>    新 `CSharpCodeGen.CopyDest(e)`：手牌（默认，老文本逐字不变）/ 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶（`PileType.Draw` +
>    `CardPilePosition.Top`），卡牌页效果栏 + 锻造页【效果】模块共用这一栏；
> 3. 锻造图那条链同样接上（`ForgeGraphGen` 里两处死写的 `PileType.Hand` 换成 `CopyDest`，数值 0 走
>    `EmitCopySelfPublic`）；
> 4. 标记行非手牌时多写 `CET:PlaceTo=…`；回读的「整段跳过」名单补 `__selfClone`（复制自己那段实现）；
> 5. 校验器：张数范围放宽成「0 或 1~5」；遗物 / 药水 / 状态上填 0 → 警告「那种布上没有这张牌、这条不会做任何事」。
>
> **顺带修**：`PlaceTo` 两处文案（App.xaml 的 ToolTip、`ForgeGraphCatalog` 的栏目名 / 说明）改成「放置 / 复制卡牌用」。
>
> **验收**：自检新增 11 条；探针 `sacprobe copycard` 生成工程 `dotnet build` 0 error；
> 老存档对拍（新 Core ↔ 已发布 V0.2.225 的 Core）示例 113 / WhiteCloak 412 个文件只差 `.sln`；
> `--uicheck` **4158 PASS / 0 FAIL**。

## 第 112 批需求（V0.2.225：**条件新增「是否预见 / 这回合预见的次数」**）

> 用户要求：「条件新增是否预见」→ 补充「这回合是否预见过，这回合预见的次数」。
>（详见 `docs/RELEASE_NOTES_V0.2.225.md`）
>
> **做法**：「预见」是工具自己拼的效果（本体没有这个机制，战斗历史里也查不到），所以次数由工具自己记：
> 1. 条件表加两条：`ScryThisTurn`（本回合预见过）/ `ScryAtLeast`（本回合已预见至少 N 次，填负数 = 反向）；
> 2. 计数器放进已有的 `cs/CombatCounters.cs`（`<角色>ForgeCombatCounters`）：`BumpScry(Player)` /
>    `ScryThisTurn(Player)`，按 `PlayerCombatState.TurnNumber` 换回合自动清零；
> 3. 「预见」那条效果里 +1 只在**用到这两条条件时**才写（`UsesScryCounter`），老存档生成文本逐字不变；
>    那个类也只写用到的部分（只用预见时不写 `BumpEnemyDeath`）；
> 4. 条件表达式走 `<角色>ForgeCombatCounters.ScryThisTurn(玩家) > 0 / >= N / < N`
>    （条件生成拿不到 profile → `_formulaOwner` / `Naming.AmbientCombatCountersClass` 兜底）；
> 5. 检查器：用了这两条条件却一条「预见」效果都没有 → 警告（计数器永远是 0）。
>
> **顺带修**：`UsesCondition` 现在也扫**锻造图【条件】模块**（以前只扫效果上的「条件选项」，
> 图上只用「洗过牌 / 预见过」时记录器不生成 → CS0103）；回读的「整段跳过」名单补上 `BumpScry(`。
>
> **验收**：自检新增 12 条；探针 `sacprobe scrycond` 生成工程 `dotnet build --no-incremental` 0 error；
> 老存档对拍（新 Core ↔ 已发布 V0.2.224 的 Core）示例 113 / WhiteCloak 412 个文件只差 `.sln`；
> `--uicheck` **4147 PASS / 0 FAIL**。

## 第 111 批需求（V0.2.224：**额外资源量遗物上的效果变成「每回合」触发**）

> 用户实测报的：「额外资源量遗物触发时机不是固定战斗开始时吗，往上面添加效果实际上是玩家回合开始时」。
>（详见 `docs/RELEASE_NOTES_V0.2.224.md`）
>
> **真因**：「额外资源量」页的效果生成在载体遗物的 `BeforeSideTurnStart` 上（**每回合、每一边**
> 都会调），而遗物链「战斗开始时」那一档本来靠**两句守卫**收成「每场战斗一次」：
> `participants.Contains(自己)` + `PlayerCombatState.TurnNumber > 1`。额外资源量那块**只有第一句**，
> 第二句漏了 → 「抽 1 张 / 获得格挡」变成每回合都触发（和页面写死的「触发时机固定战斗开始时」不符）。
>
> **修法**：补上 `if (base.Owner.PlayerCombatState?.TurnNumber > 1) return;`（和遗物链同一口径，
> `?.` 防空）。「开场发初始数量 / 跨战斗继承」那段（`AfterSideTurnStart`）本来就有这两句，没动。
>
> **验收**：自检 2 条（守卫在效果之前 / 没加效果时根本不生成那个方法）；探针 `sacprobe extrares`
> 生成工程 `dotnet build --no-incremental` 0 error；老存档对拍（新 Core ↔ 已发布 V0.2.223 的 Core）
> 示例 113 / WhiteCloak 412 个文件只差 `.sln`；`--uicheck` **4135 PASS / 0 FAIL**。

## 第 110 批需求（V0.2.223：**状态牌局内升级「没生效」**）

> 用户反馈：先问「状态牌能不能像『凋萎』一样局内可以被升级」，答「勾『可被无限升级』」后实测
> 「但是好像升级没生效」。（详见 `docs/RELEASE_NOTES_V0.2.223.md`）
>
> **真因**（两条都是静默失效）：
> 1. 状态牌只能在「诅咒 / 先古 / 状态」页编辑，而 **「可被无限升级」勾选框只在「卡牌」页**
>    （那一页的说明文字却写着「勾**下面**的…」）→ 状态牌永远生成 `MaxUpgradeLevel => 0`
>    → `CardCmd.Upgrade` 第一句 `if (!card.IsUpgradable) continue;` 直接跳过；
> 2. 锻造图里的「升级卡牌」是**另写**的一份（`ForgeGraphGen.EmitCardPick`），`filter` 里**没有**
>    `c.IsUpgradable`（卡牌页那份 `EmitUpgradeCard` 一直有）→ 选牌界面连状态牌 / 诅咒 / 已升满的牌都列，
>    选中后静默跳过 = 「点了没反应」。
>
> **修法**：
> 1. 「诅咒 / 先古 / 状态」页加「可被无限升级」（新 `CardSpec.ShowInfinitelyUpgradable`：诅咒不显示，
>    状态 / 先古 / 普通卡显示）+ 改掉那句错误的「下面」说明 + 完整 ToolTip；
> 2. 锻造图「升级卡牌」三条路（自己选 / 随机 / 全部）都加 `c.IsUpgradable`，和卡牌页同一口径
>    （和「只对这张卡」同时填时 `(c.IsUpgradable) && (只对这张卡)`）；
> 3. 顺手修 `CardSpec.Rarity` 不发通知的问题（现在会 Raise `IsCurseCard` / `IsStatusCard` /
>    `IsAncientCard` / `ShowInfinitelyUpgradable`），并把「可被无限升级」的勾选变化接到
>    `UpgradeCustomKeywordHint` 的刷新上。
>
> **验收**：自检新增 11 条（界面两处绑定 / 显隐判据 / 稀有度通知 / 状态牌生成 int.MaxValue 与 OnUpgrade /
> 不勾仍是 0 / 图上升级四条路都筛），另改 2 条过时断言；探针 `sacprobe statusup` 生成工程
> `dotnet build --no-incremental` 0 error；老存档对拍（新 Core ↔ 已发布 V0.2.222 的 Core）示例 113 /
> WhiteCloak 412 个文件只差 `.sln`；`--uicheck` **4133 PASS / 0 FAIL**。

## 第 109 批需求（V0.2.222：**锻造里「随机敌人」被当成全体 × 命中次数**）

> 用户实测报的：「对群数和命中次数在锻造里写在一起，导致随机多段效果出现异常」。
>（详见 `docs/RELEASE_NOTES_V0.2.222.md`）
>
> **真因**：`ForgeGraphGen` 里【对象】=随机敌人解析出来的 `Foes` 一直是
> `ForgeFormulaRuntime.EnemyList(__fgPlayer)`（**全体敌人**）→「随机敌人 + 命中 4」生成成
> `foreach (全体) Attack(…).WithHitCount(4)` = **每个敌人各挨 4 下**；「随机 1 个」= 全体各挨 1 下。
> 「卡牌」页那条链一直是对的（本体 `TargetingRandomOpponents`），只有锻造图这半边错。
>
> **修法**：
> 1. `UnitRef` 加 `RandomEnemy` / `Dup` 两个标记；随机敌人的 `Foes` 改成「随机挑 N 个」
>    （`Enumerable.Range(0, N).Select(_ => RandomEnemy(…))` / 洗牌 + `Take(N)`），N = 命中次数；
> 2. **伤害**（卡牌布）：加一条 `"Damage" when unit.RandomEnemy` → `.FromCard(…).TargetingRandomOpponents(…)
>    .WithHitCount(hits)`（每一下随机挑一个 = 本体「随机多段」，和卡牌页同一份语义）；
>    遗物 / 药水 / 状态布没有 `Attacker`（该 API 要求先设 Attacker，否则当场抛），
>    所以那边走「随机挑 N 个、每个打 hits 下」的循环；
> 3. 「随机目标允许重复选中同一个敌人」现在真的传进去（伤害 `allowDuplicates:`；其它效果按
>    允许重复 = 同一个可被挑中多次 / 不允许 = 洗牌取前 N 个）；
> 4. 【效果】模块「命中次数」的 ToolTip 写清两种口径；自检里那条**锁旧行为**的断言改掉
>    （它原来断言「随机敌人 + 命中 4 = 逐个动手」，那正是这次的 bug）。
>
> **验收**：自检新增 6 条（并改 2 条旧用例）；探针 `sacprobe randhit` 生成工程
> `dotnet build --no-incremental` 0 error；老存档对拍（新 Core ↔ 已发布 V0.2.221 的 Core）
> 示例 113 / WhiteCloak 412 个文件只差 `.sln`；`--uicheck` **4122 PASS / 0 FAIL**。

## 第 108 批需求（V0.2.221：**「消耗丢弃升级指定卡没生效」**）

> 用户实测报的：「消耗丢弃升级指定卡没生效」。
>（详见 `docs/RELEASE_NOTES_V0.2.221.md`）
>
> **真因**：卡牌 / 遗物 / 药水 / 状态四条链的「消耗 / 丢弃 / 升级 / 拿牌 / 变化 / 复制 / 预见」
> 都走 `CSharpCodeGen` 那几份发射器（一直会读「范围界定 + 只对这张卡」），
> 但**锻造节点图**那条链把它们**内联另写**了：`filter: null` 写死、随机 / 「全部」两条路连
> `.Where` 都没有 —— 界面上两栏能填、生成端没读 = 静默失效（日志里也没报错）。
>
> **修法**：
> 1. `ForgeGraphGen.EmitCardPick`（消耗 / 丢弃 / 升级）、TakeFrom*、复制 / 变化、预见
>    全部改走 `CSharpCodeGen.CardScopePredicate`（变化走 `TransformSourcePredicate`）：
>    自己选 → `filter: c => …`；随机 / 全部 → `.Where(c => …)`；没设范围 → 空串（老图逐字不变）；
> 2. 校验器：`只对这张卡（指定）` 的类名认不出来 → **报错**（以前一路生成到编译那一步，
>    `ModelDb.Card<不存在>()` = CS0234/CS0246，整个模组装不上）；卡牌页 + 锻造图两条路都查
>    （`CardTargetProblem` 加了 `fieldZh` 参数，提示语里的栏位名跟着变）；
> 3. 自检里两处旧用例用的卡名 `Strike` 本作不存在 → 换成真实存在的 `SevenCurse`
>    （顺便让「类名认不出来会报错」这条新检查不会误伤旧用例）。
>
> **验收**：自检新增 15 条；探针 `sacprobe onlycard` 生成工程 `dotnet build --no-incremental` 0 error；
> 老存档对拍（新 Core ↔ 已发布 V0.2.220 的 Core）示例 113 / WhiteCloak 412 个文件只差 `.sln`；
> `--uicheck` **4116 PASS / 0 FAIL**。

## 第 107 批需求（V0.2.220：**抽牌 / 拿牌时「为抽到的牌加永久/临时关键词」**）

> 用户要求：「在抽牌/拿牌等效果下额外填加一条『为抽到的牌添加永久/临时关键词』的选项」。
>（详见 `docs/RELEASE_NOTES_V0.2.220.md`）
>
> **做法**：
> 1. `EffectSpec.DrawnKeyword` + `DrawnKeywordTemp`（新字段，默认空 = 老存档生成文本逐字不变）；
> 2. 只在这几档出现（`EffectFieldPolicy.UsesDrawnKeyword`）：抽牌 / 刮削 / 从三摞拿牌 / 填满手牌
>    —— 其它种类生成端不读，界面上也收起来（自检的机械测量逐条钉住）；
> 3. 生成：`CSharpCodeGen.EmitDrawnKeyword`（复用 `EmitGiveKeywordTo`：本体关键词 `AddKeyword`、
>    自定义 `AddGivenCustomKeyword`、临时走单回合标记 / 生成的临时关键词 Power）+ `EmitDraw`；
>    抽牌接住 `CardPileCmd.Draw` 的返回值，拿牌 / 刮削 / 填满手牌用它们自己那批牌；
> 4. 四条链都接：卡牌 / 遗物 / 药水 / 自定义状态 + 锻造图（【效果】模块的抽牌、拿牌两档）；
> 5. 回读：`// CET:DrawnKeyword=… CET:DrawnKeywordTemp=0/1` 由紧跟着的效果认领，
>    `__drawnKw` 那几行整段跳过；`0` 时一个字都不写；
> 6. 校验器两条警告：「下回合生效」的抽牌（关键词加不上）、配到不抽牌的效果上（会被忽略）。
>
> **验收**：自检 30 条（测量 / 形状 / 校验 / 回读 / 界面 / 锻造图）；探针 `sacprobe drawkw` 生成工程
> `dotnet build --no-incremental` 0 error；老存档对拍（新 Core ↔ V0.2.219 Core）示例 113 / WhiteCloak 412
> 个文件只差 `.sln`；`--uicheck` **4102 PASS / 0 FAIL**。

## 第 106 批需求（V0.2.219：**「每消耗 N 点能量 / 额外资源量」填 0 和填 1 一样**）

> 用户要求：「触发时机的每消耗 N 点能量和额外资源量，数值填 0 的效果和 1 的效果是一样的，
> 把设 0 的效果变成只要消耗便触发，每回合或者每场战斗仅触发一次」。
>（详见 `docs/RELEASE_NOTES_V0.2.219.md`）
>
> **原因**：这两条时机的数值走「按点数循环」，`Math.Max(1, N)` 把 0 顶成 1 →
> 0 和 1 走同一条路（一张 3 费牌触发 3 次）。
>
> **修法**：
> 1. `CustomPowerGen.EmitSpendTriggers`（新）+ `CSharpCodeGen.EmitRelicEffectBlock`：把同一钩子上的
>    时机按数值分成两档 —— 0 档（`TriggerAmount <= 0`）跑在点数循环**外面**，
>    判据是 `__spentBefore == 0`（读本体战斗历史，本体先记账再调钩子 → 这就是「本窗口的第一次消耗」）；
>    N 档（> 0）照旧按点数循环 + `% N == 0`（老文本逐字不变）；
> 2. 0 档多写一行 `// CET:TriggerAmount=0m CET:TriggerScope=…`，否则「从工程恢复」会被默认值 1 顶掉；
>    N 档不写（老存档逐字不变，数值仍由「// 数值：每累计花掉 N 点」回读）；
> 3. `SpendBeforeLine` 多一个变量名参数（同一个钩子上混了本回合 / 本场战斗时才用
>    `__spentBeforeTurn` / `__spentBeforeCombat`，单一范围还是 `__spentBefore`）；
> 4. 说明文字改成「留空 / 填 0 = 只要消耗就触发一次（这个范围里只触发第一次）」。
>
> **验收**：自检 13 条（生成形状 / 混搭两档 / 回读 0 / 说明文字）；探针 `sacprobe spend0`；
> 老存档对拍（新 Core ↔ V0.2.218 Core）示例 113 个只差 `.sln`、WhiteCloak 412 个里只有
> `WhiteCloakPowerNAction.cs`（正是旧 0 = 每 1 点那条）；`--uicheck` **4072 PASS / 0 FAIL**。

## 第 105 批需求（V0.2.218：**锻造里没法做「条件满足不了就打不出去」**）

> 用户要求：「锻造无法『处于某种条件下时，该牌无法被打出』」。
>（详见 `docs/RELEASE_NOTES_V0.2.218.md`）
>
> **原因**：卡牌页有 `Condition.UnplayableWhenUnmet` → `IsPlayable`，锻造图那边只有【条件】/【if】
> （执行时判断），做不出「这张牌根本不能打」。
>
> **修法**：
> 1. 【条件】模块加一栏「不满足时」：跳过这一支（默认，老图逐字不变）/ **整张牌打不出去**；
> 2. `ForgeGraphGen.UnplayableGates(g, p)` 把这一档的条件翻出来，`CSharpCodeGen.CardSource`
>    汇进 `IsPlayable` + `ShouldGlowGoldInternal`（升级后那块布上的门包成 `!base.IsUpgraded || 条件`）；
> 3. 非卡牌布勾这一档 → 校验器报错；数值写运行时公式（`IsPlayable` 里放不下局部变量）→ 警告并跳过；
> 4. 只当「打不出去」用的【条件】不再被「漏接线」警告误报。
>
> **验收**：探针生成 `IsPlayable` / 描金边、工程 0 error；默认档不生成；对拍 0 差异；
> `--uicheck` **4059 PASS / 0 FAIL**（新增 6 条）。

## 第 104 批需求（V0.2.217：**打出去牌直接卡住** —— `ThrowIfNull(cardPlay.Target)` 和 `TargetType` 判据打架）

> 用户实测 + `godot…19.35.16.log`：`ArgumentNullException: Value cannot be null. (Parameter 'cardPlay.Target')`
> at `NecrassCard20.OnPlay`；现象「目标为全体敌人时给灾厄牌卡住；改单个敌人不卡但灾厄没给」。
>（详见 `docs/RELEASE_NOTES_V0.2.217.md`）
>
> **病因**：`ResolveTargetType` 过滤了「数值 0 且升级不加数值」的效果（整条不生成），
> `NeedsTargetNullCheck` 却没过滤 → 「全体敌人 + 施加 0 层状态」被推成 AllEnemies（不弹选目标）
> 却又写了判空 → 打出时 Target 为 null → 抛异常 → 回合循环断掉（牌卡住）。
>
> **修法**：抽出一份事实 `HitsEnemyKind` / `NeedsChosenTarget`（含「代码真的会生成」这一条），
> 两处共用；判空覆盖的种类补齐（大限已至 / 翻倍 / 暴露 / 清除状态 / 禁止格挡 / 禁止回血）。
> 「灾厄没给」是另一件事：那条「施加状态」数值是 0（0 层不执行、整条不生成，配置检查有提示）→ 填 ≥1。
>
> **验收**：探针两种配置各按预期；不变量自检（12 种效果 × 敌/友 × 0/2 层）全一致；
> 老存档对拍（示例 113 / WhiteCloak 412）0 差异；`lihuazou.json` 0 差异；
> `AinodiMod.json` 仅 `AinodiCard63` 多一行判空（正确方向）；`--uicheck` **4052 PASS / 0 FAIL**。

## 第 103 批需求（V0.2.216：**二形态时只剩 24 基础伤害** —— 三层「且」丢条件 + 漏接线静默消失）

> 用户实测（`lihuazou.json` + `godot*.log`）：「处于二形态时触发不了多效卡效果只能打24的基础效果」。
>（详见 `docs/RELEASE_NOTES_V0.2.216.md`）
>
> **查到的两个真 bug**：
> 1. 「且」串「且」时，第二个【且】的条件取的是**它 Out 口上接进来的那一块**（= 第一个【且】），
>    不是它自己的条件 → 第三个条件（`长枪形态 == 1`）被整段丢掉 → 长枪形态落回兜底；
> 2. 图上有「会做事」的模块（效果 / if / 且 / 或 / 条件）没接到【触发时机】这条链上时，
>    生成端**一个字都不输出**（用户那条 27+锋利 的分支就是这个原因：关系符的左值没接线）。
>
> **修法**：
> 1. `EmitChain` 的 And/Or/Condition 分支改用 `BoolVar(node)`（查自己），三层且一个不落；
> 2. 新增 `UnreachableModules(g)`：生成代码里写一段「没接到【触发时机】」的注释，
>    校验器给一条**警告**（不拦生成 —— 留备用模块是正常用法）。
>
> **验收**：用户存档重新生成后兜底变成三条件、出现漏接线警告、工程 0 error；三层且探针全在；
> 老存档对拍（示例 113 / WhiteCloak 412）与 `AinodiMod.json` 均 0 差异；
> `--uicheck` **4049 PASS / 0 FAIL**（新增 5 条）。

## 第 102 批需求（V0.2.215：**锻造卡【能量】写 X 被拦成「必须是编译期常量」** —— X 费 / Y 费接上）

> 用户截图（生成失败）：卡牌「虚化」（升级前 / 升级后）「【能量】的『打出时消耗的能量』只能填生成期
> 就算得出来的数……『X』里用了运行时才算的量」。
>（详见 `docs/RELEASE_NOTES_V0.2.215.md`）
>
> **病因**：那一栏的提示一直写着「X = 消耗掉的能量」（本体 `HasEnergyCostX` 那套），
> 但校验器把它当运行时值一律拦住 —— 自相矛盾，X 费卡建不出来。
>
> **修法**：
> 1. `ForgeGraphGen.IsEnergyCostX` / `IsResourceCostX` / `EnergyCostXPlus`（复用 `ForgeNumbers.ResolveCost`），
>    `CSharpCodeGen.IsCostX` / `IsStarCostX` / `CostXPlus` 把「锻造图里写 X/Y」也算上；
> 2. 图的序幕 `__X` / `__Y` 在 X 费 / Y 费时走 `base.ResolveEnergyXValue()` / `ResolveStarXValue()`
>    （= **消耗掉**的能量，而不是扣完之后的剩余）；
> 3. 校验器只拦真正做不到的量，报错文案补「也可以写 X / Y」；界面那格对 X / Y 改成绿色提示。
>
> **验收**：探针（锻造卡 + 能量=X + 数值=X）生成成功且工程 0 error；老存档对拍（示例 113 / WhiteCloak 412）
> 0 差异；`AinodiMod.json` 只差 `.sln`；`--uicheck` **4044 PASS / 0 FAIL**。

## 第 101 批需求（V0.2.214：**丢弃 / 消耗没法「只对这张卡」** —— 补「只对这张卡（指定）」）

> 用户实测：「这个是丢弃消耗卡牌无法选择目标卡或者组」。
>（详见 `docs/RELEASE_NOTES_V0.2.214.md`）
>
> **病因**：这些「从牌堆里挑牌」的效果只有「范围界定」（类型 / 自定义关键词组 / 卡牌组），
> 没有「就这一张卡」；用户只能先建只含那张卡的卡牌组，等于找不到入口。
>
> **修法**：新增 `EffectSpec.ScopeCardId`（「只对这张卡」，空值不写进 JSON），
> 在丢弃 / 消耗 / 升级 / 复制 / 重放 / 给关键词 / 三摞拿牌 / 预见 上显示（变化卡牌除外 —— 它有自己的两行），
> 与「范围界定」是**与**关系；`CardScopeCond` 统一给出（变化卡牌那条改走 `TransformSourcePredicate`，
> 避免两处各加一次）；标记行追加 ` CET:OnlyCard=`，回读认回来。
> V0.2.213 的旧键名 `TransformCardId` 保留成只读别名（搬到新字段，不再写出）。
>
> **验收**：探针三条语句（filter / && / 全部）都对、生成工程 0 error、回读 3 条都对；
> 老存档对拍（示例 113 / WhiteCloak 412）0 差异；`AinodiMod.json` 只差 `.sln`；
> `--uicheck` **4037 PASS / 0 FAIL**（新增 9 条）。

## 第 100 批需求（V0.2.213：**变化卡牌没有「取几张」/ 没法限定只变化哪些牌**）

> 用户实测：「变化卡牌没有取几张这个选项，也没法选择目标卡或者组内的牌进行变化」。
>（详见 `docs/RELEASE_NOTES_V0.2.213.md`）
>
> **病因**：
> 1. 「取几张」那一行的显隐以前锻造链走**旧的 6 种清单**（没有变化卡牌）→ 锻造页上填不了，
>    而锻造链生成端读的就是同一个 CountMode；
> 2. 变化卡牌只能整摞挑，「范围限定」那个字段是「变成什么」（结果池），拿不来当「变哪些」用。
>
> **修法**：
> 1. `ShowCountModeRow` 两条链按同一份判据（`EffectFieldPolicy.UsesCountMode`）；
> 2. 新增两栏只属于变化卡牌：`TransformPick`（范围：类型 / 自定义关键词组 / 卡牌组）与
>    `TransformCardId`（指定某一张卡，优先），三条挑牌路径（自己选 / 全部 / 随机）全套上；
>    两栏空 = 老行为逐字不变；写 `CET:ChangePick / CET:ChangeCard` 标记供回读；
>    空值用 `WhenWritingDefault` 不写进 JSON（锻造图节点里嵌的 EffectSpec 不因此变样）。
>
> **验收**：探针三条语句都对、生成工程 0 error、回读还原；老存档对拍（示例 113 / WhiteCloak 412）
> 0 差异；`AinodiMod.json` 只差 `.sln`；`--uicheck` **4027 PASS / 0 FAIL**（新增 13 条）。

## 第 99 批需求（V0.2.212：**「放置卡牌」怎么选「放在哪里」** —— 两处都能选 + 把这两栏钉死）

> 用户提问：「放置卡牌怎么选择放在哪里」。
>（详见 `docs/RELEASE_NOTES_V0.2.212.md`）
>
> **答案**：就在效果编辑器里 —— 「放到哪一摞」（手牌 / 抽牌堆（底）/ 弃牌堆 / 消耗牌堆 / 牌顶）
> + 「从哪里选牌」+ 「选牌方式」+ 数值（**0 = 这张牌自己**）。
> 卡牌 / 遗物 / 药水 / 自定义状态页都在效果栏里；锻造页在【效果】模块里（同一个编辑器，框小、
> 里面可滚动）。
>
> **这次的改动**：
> 1. 判据抽成 `EffectSpec.KindUsesPlaceTo` / `KindUsesSelectPile`（卡牌页与锻造页读同一份）；
> 2. 锻造页【效果】模块正式声明这两栏（候选复用 `EffectCatalog.PlacePiles` / `SelectPiles`），
>    模块框按栏数算高度 → 变高一点，少滚一截；
> 3. `ForgeEffectBridge` 双向同步：只有 `Fields` 的老图能读进 spec；图上改完写回节点字段
>    （只对用得到的种类写，别给积木摘要添噪声）。
>
> **验收**：锻造图探针 `PlaceTo=Top` → `CardPileCmd.Add(this, PileType.Draw, CardPilePosition.Top)`；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`AinodiMod.json` 重新生成只差 `.sln`；
> `--uicheck` **4014 PASS / 0 FAIL**（新增 5 条）。

## 第 98 批需求（V0.2.211：**两条「每累计几次触发」共用一份基准值 → 第一次只要 1 次**）

> 用户实测（自定义状态，两条计时时机：打出攻击牌 3 次 + 打出技能牌 3 次）：
> 「触发次数设置的 3+3 但是第一次触发只需要 1+1 就行，后续触发就是 3+3 了」。
>（详见 `docs/RELEASE_NOTES_V0.2.211.md`）
>
> **病因**：基准值字段名只有「时机 + 范围」（`__cntCardPlayedTurn`），两条筛选不同的计数时机共用一份。
> 两条的累计值不同，各自都会 `if (字段 < 0 || 累计 <= 字段) 字段 = 累计 - 1;` ——
> 后跑的那条用自己的「累计 - 1」把先跑那条的基准冲掉（通常是更小的值）→ 差值虚高 → 第一次就凑够。
>
> **修法**：
> 1. `CountBaseField` 改成按「时机 + 范围 + 筛选」起名，**只有「同一时机 + 同一范围下筛选不同」时才带后缀**
>    （单条 / 两条筛选相同 / 留空与 Any 的存档字段名一个字都不变 → 老工程逐字一致）；
> 2. 顺手修同类问题：同一个钩子上两条「每 N 个敌人死亡」以前各自 `BumpEnemyDeath()` 一次
>    （一次死亡被数成 2 个）→ 现在只 Bump 一次（`int __deathsNow = …`）。
>
> **验收**：探针（攻击 3 次 / 技能 3 次同状态）生成两个字段各用各的，`dotnet build` 0 error；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **4009 PASS / 0 FAIL**（新增 5 条）。

## 第 97 批需求（V0.2.210：**自定义状态里放「给伙伴施加状态」被拦住** —— 其余 6 个宠物效果进状态链）

> 用户截图：状态「新状态29」『打出一张牌时（这张牌还没生效）』第 1 条是「给伙伴施加状态」，
> 生成时被校验拦成错误（对话框把 `SupportedEffectKinds` 那份清单原样列了出来）。
>（详见 `docs/RELEASE_NOTES_V0.2.210.md`）
>
> **病因**：状态链白名单里只有 `SummonPet` / `PetSacrifice`，其余宠物效果（PetApplyPower / PetHeal /
> PetLoseHp / PetGainMaxHp / PetGuardOn / PetGuardOff）既没进白名单，`CustomPowerGen.EmitEffect` 也没实现。
>
> **修法**：
> 1. 六个种类补进 `PowerTriggers.SupportedEffectKinds` + `EffectText`（描述写中文，不印英文种类名）；
> 2. `CustomPowerGen.EmitEffect` 复用卡牌链那批发射器（`EmitPetApplyPower` / `EmitPetHeal` /
>    `EmitPetLoseHp` / `EmitPetGainMaxHp` / `EmitPetGuard`），只换上下文表达式：
>    查伙伴 = `XxxCmd.Get(base.Owner.Player)`、施加者 = `base.Owner`、「这张牌」= `null`；
>    「全部召唤物」逐只展开；「下回合生效」走 `EmitDelayedPowerApply`（挂延迟状态 + CET 标记）；
> 3. `NeedsChoiceContext(EffectSpec)` 补上这几条的判据（含「牺牲伙伴」按收益档位判 —— 以前一个都没列）；
> 4. 顺手修掉 4 个会生成出编译不过的代码的坑：状态里「牺牲伙伴」引用 `base.DynamicVars` / `cardPlay.Target`、
>    状态里「全部召唤物」空转、延迟状态类里宠物效果引用 `base.DynamicVars`（+ 给伙伴施加状态把自己再挂一遍的死循环）、
>    两只伙伴的延迟状态类撞名（类名现在带上伙伴）。
>
> **验收**：探针存档（2 只伙伴 + 6 个效果 + 下回合生效组合）生成工程 `dotnet build` **0 error**；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **4004 PASS / 0 FAIL**（新增 15 条）。


## 第 96 批需求（V0.2.209：**遗物描述里的 `{XxxPower:diff()}` 格式化报错 + 辅助 Power 缺 title 键**）

> 用户日志（`godot (23).log`）+ `AinodiMod.json`：
> `Localization formatting error … selector named "VulnerablePower"（table=relics key=AINODI_RELIC2.description）`；
> `GetRawText: Key 'AINODI_FORGE_TURN_END_PLAY_POWER.title' not found in table 'powers'`。
>（详见 `docs/RELEASE_NOTES_V0.2.209.md`）
>
> **病因**：
> 1. 「锻造图说了算」的遗物**效果全在图上**，源码里 `CanonicalVars => [ ]`（没有动态变量），
>    但遗物描述照旧写 `{变量名:diff()}` → SmartFormat 找不到那个变量就抛格式化错误（hover 时刷红字）。
> 2. `<角色>ForgeTurnEndPlayPower` 这类**辅助 Power** 以前没写进 `powers.json` → 游戏 / BaseLib 查 title 落空。
>
> **修法**：
> 1. `DescribeEffect` 新增 `literalAmount`：锻造图遗物（`ForgeGraph is { IsEmpty: false }`）的描述写数字字面量；
>    普通遗物照旧 `{…:diff()}`（源码里真有 `PowerVar<T>`）。
> 2. `PowersJson` 增补 7 个辅助 Power 的 title / description / smartDescription，**用到哪个才写哪个**
>    （TurnEndPlay / TempKeyword / TempUpgrade / NoHeal / DrawCost / StarCap / EnergyNextTurnDebt）。
>
> **验收**（拿用户这份存档真跑）：`AINODI_RELIC2.description` 变成字面量、辅助 Power 三条键都在；
> 生成工程 `dotnet build` **0 error**；老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；
> `--uicheck` **3989 PASS / 0 FAIL**（新增 4 条）。

## 第 95 批需求（V0.2.208：**「敌人回合结束时卡死」—— 三条攻击类效果在非卡牌布上仍拼 `.FromCard(null, null)`**）

> 用户日志（`godot (23).log`）：
> `turn loop died … NullReferenceException at AttackCommand.FromCard ← AinodiPower11.BeforeSideTurnEnd`。
>（详见 `docs/RELEASE_NOTES_V0.2.208.md`）
>
> **病因**：本体 `AttackCommand.FromCard` 第一句 `Player owner = card.Owner;` → 传 null 必炸，
> 而且在钩子里炸会把整个回合循环打断。V0.2.200 只修了锻造图【造成伤害】那一处，
> 同样的写法还漏在：**大限已至**、**伙伴攻击 / 按生命值伙伴攻击**（三条分支）、
> **牺牲伙伴 → 收益=伤害**（单体 / 群体两条分支）—— 状态 / 遗物 / 药水的布上 `cardExpr` 是字面量 `null`。
>
> **修法**：新增 `CSharpCodeGen.FromCardClause(cardExpr, cardPlayExpr)`（`cardExpr == "null"` → 返回空串），
> 上面三处发射器全部改用它；卡牌链 `cardExpr = "this"` → 文本逐字节不变。
>
> **验收**：探针（状态锻造图「敌人回合结束时 → 大限已至」）生成的 `BeforeSideTurnEnd` 里没有 `.FromCard(`，
> `dotnet build` **0 error**；老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；
> `--uicheck` **3985 PASS / 0 FAIL**（新增 2 条）。
>
> **记两条待办（同日志）**：① 描述里 `{VulnerablePower}` 这种「状态名当变量」导致
> `Localization formatting error`；② `AINODI_FORGE_TURN_END_PLAY_POWER.title` 没写进 `powers.json`
> （「回合结束时自动打出」辅助 Power 的标题键缺失）。

## 第 94 批需求（V0.2.207：**「打出一张带某关键词的牌」触发时机认不出战斗中刚给的关键词**）

> 用户原话：「在战斗中给予卡牌关键词后打出这张卡不能触发打出某关键词的卡这个时机」（日志无报错，是「没反应」）。
>（详见 `docs/RELEASE_NOTES_V0.2.207.md`）
>
> **病因**：触发时机的「只对哪类牌生效 = 关键词：XXX」走的是**生成期白名单**
> （`ScopeCondForFilter` → `cardPlay.Card.Id == ModelDb.Card<X>().Id || …`），只覆盖设计期就带这个关键词的牌；
> 战斗中「给予卡牌关键词」写的是**运行时**标记（`ForgeGivenKeywords` / `AddGivenCustomKeyword`），
> 被临时给上关键词的牌不在白名单里 → 时机不响。
>
> **修法**：新增 `CSharpCodeGen.TriggerCardFilterCond` —— 关键词那一档生成
> `白名单 || ForgeGivenKeywords.Get(cardPlay.Card).Contains("KEY")`；遗物链（`RelicTriggerGuardLines`）与
> 状态链（`CustomPowerGen.EmitCardPlayedItems`，锻造图的触发时机也走它）都改用它。
> 另外 `UsesGivenCustomKeyword` 把「触发时机的关键词筛选」也算进去 —— 否则只配筛选、没配给予关键词时
> 不会生成 `GivenKeywords.cs`，工程 CS0103。
>
> **验收**：探针（状态 + 遗物各一条 Keyword 筛选触发）生成工程 `dotnet build` **0 error**；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **3983 PASS / 0 FAIL**（新增 4 条）。

## 第 93 批需求（V0.2.206：**「从指定组抽 N 张选 1 张」既列整组又崩 `CanonicalModelException`**）

> 用户原话：「打出后，从指定组中抽10张选1张。这一效果依旧展示了所有牌且无法正常生成牌。」
> 日志里那条：`CanonicalModelException: Canonical model of type WhiteCloakMod.WhiteCloakExtort used in incorrect place.`
>（详见 `docs/RELEASE_NOTES_V0.2.206.md`）
>
> **两个真问题**：
> 1. `EmitGenerateCard` 里先判 `SpawnChosen`（候选 = 整组 Take(60)），把「多选1 = M」静默盖掉 →
>    界面把整组牌全列出来。现在 `自己挑 + M ≥ 2` 走「先随机抽 M 张候选」那条，`M = 1` 才是整组。
> 2. 「自己挑」的候选是**规范模型**（`ModelDb.Card<X>()`），却直接喂给
>    `CardPileCmd.AddGeneratedCardToCombat`（内部 `AssertMutable()`）→ 运行时抛 CanonicalModelException；
>    附加处理（升级 / 免费 / 关键词）也套在规范模型上，同样会炸。现在先
>    `CombatState.CreateCard(__picked, player)` 建实例、附加处理作用在实例上、再入堆。
>
> **验收**：探针（两种配法各一张卡）生成工程 `dotnet build` **0 error**；对拍 示例 113 个文件 0 差异、
> WhiteCloak 只有 `WhiteColakHeaven.cs` 变（正是出事的这张）；`--uicheck` **3979 PASS / 0 FAIL**（新增 3 条）。

## 第 92 批需求（V0.2.205：**锻造区「删除牌」填 0 = 删掉这张牌自己**）

> 用户原话：「锻造区，删除牌，数值填 0 不会删除自己」+ `godot (20).log`（日志里无相关报错，纯行为问题）。
>（详见 `docs/RELEASE_NOTES_V0.2.205.md`）
>
> **病因**：`RemoveCardGlobal / UpgradeCardGlobal / TransformCardGlobal` 属于「数值 = 力量」类
> （`AmountIsStrength`）→ 数值 0 落进 `IsInertZero` 整条被丢掉；就算不丢，生成端也是
> `Math.Max(1, amount)` → 0 被夹成 1 张（变成「随机 / 自己选 1 张」）。
>
> **修法**：
> 1. `AmountZeroIsSelf` 收进这三个种类（和「丢弃 / 消耗卡牌 0 = 自己」同一口径）。
> 2. `EmitGlobalCardEffect` 新增 `cardSelfExpr` 参数；三档各加「0 = 这张牌自己」分支，
>    按 `c.Id == this.Id` 认牌组里那张（同名副本都算）。卡牌链传 `"this"`，**卡牌的锻造图**传
>    `ctx.CardSelfExpr`；遗物 / 药水 / 状态没有「这张牌」→ 退回 1 张并在生成代码里写注释（不静默）。
> 3. 卡面（LocalizationGen）与列表行尾（Profile.Display）都写明「这张牌自己」。
>
> **⚠ 老存档影响**：以前「删除 / 升级 / 变化牌组 = 0」是「选 1 张」，这一版起变成「这张牌自己」；
> 想保持原行为请把数值填 ≥ 1。对拍发现 WhiteCloak 的 `WhiteCloakTensionMax` 正属此类（唯一变化）。
>
> **验收**：探针（删除 / 升级 / 变化 × 卡牌链 + 卡牌锻造图）生成工程 `dotnet build` **0 error**；
> 对拍 示例 113 个文件 0 差异、WhiteCloak 只差上面那 1 个；`--uicheck` **3976 PASS / 0 FAIL**（新增 7 条）。

## 第 91 批需求（V0.2.204：**范围界定补上「任务牌」**，穷举「非攻击牌」不再漏 Quest）

> 用户原话：「当前消耗指定牌的范围界定中不包含任务牌，使得穷举达成『消耗非攻击牌』一类效果时，任务牌无法被消耗。」
>（详见 `docs/RELEASE_NOTES_V0.2.204.md`）
>
> **病因**：本体 `CardType` 有 7 档（None / Attack / Skill / Power / Status / Curse / **Quest**），
> 我们那张「范围限定 / 范围界定」下拉只有前 6 档里的 5 个类型 + 不限 → 任务牌永远匹配不上。
>
> **修法**：`SpawnFilters` 加 `("Quest", 任务牌, 任务)`；四处「类型 → 生成期判据」全部补
> `c.Type == CardType.Quest`（`SpawnPoolCond` / `SpawnPoolExpr` / `ScopeCondForFilter` / `CardFilterCond`）；
> `CardFilterZh` 补「任务」；`IsKnownScrapeCond` 改成**从 SpawnFilters 推导**（以前手写死一份，漏了 Quest →
> 界面上能选、校验器却报「认不出来」）。
>
> **验收**：探针（消耗 / 丢弃 / 生成 / 刮削 + 任务牌）生成工程 `dotnet build` **0 error**，
> 三处判据都是 `c.Type == CardType.Quest`；老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；
> `--uicheck` **3969 PASS / 0 FAIL**（新增 7 条）。

## 第 90 批需求（V0.2.203：**锻造图支持「本回合这张牌自己费用变 0」**——「处决」那条报错）

> 用户截图：`卡牌「处决」（升级前 / 升级后）：【效果】选的「本回合这张牌自己费用变 0」节点图里还没接上…`
>（详见 `docs/RELEASE_NOTES_V0.2.203.md`）
>
> **为什么以前拦**：这一档在本体里不是「一条效果」，而是**这张牌自己的行为**
> （`CardModel.SetToFreeThisTurn()` = 能量费 + 额外资源量费本回合都变 0，打出后恢复，本体「子弹时间」那套）。
> 节点图的【效果】模块早先没有「这张牌」，所以被单列进「卡牌专属、做不了」并报错拦住。
>
> **修法**：
> 1. `ForgeGraphGen.SupportedEffectKinds` 收进 `CheapenSelf`；
>    `EmitEffect` 里 `ctx.Scope == "Card"` → 生成 `this.SetToFreeThisTurn();`（和卡牌页那条链同一句）。
> 2. 状态 / 遗物 / 药水的布仍然不支持（没有「这张牌」），但报错改成说得清的一句
>    「只有「卡牌」的锻造图里有「这张牌」…」，生成的注释也改成「这一档是卡牌自己的行为」。
> 3. `CheapenSelfOnDraw`（抽到就减费）仍然不支持（要挂卡自己的 `AfterCardDrawn` 钩子）。
> 4. 自检里那条「选到节点图没接的种类 → 报错」的断言改为用 `CheapenSelfOnDraw` 当反例，
>    并新增三条：卡牌图不报错 / 生成同一句 / 别的布上仍报错。
>
> **验收**：探针生成一张「打出时 → 本回合这张牌自己费用变 0」的锻造卡 → `dotnet build` **0 error**；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **3962 PASS / 0 FAIL**。

## 第 89 批需求（V0.2.202：**「全部友方」能勾「包括自己」+ 修掉「全部友方」在单人局里一个人都打不到**）

> 用户问：「对象的全体友方包括自己吗」→ 不包括；「那我要怎么选择全部友方包括自己」。
>
> **病根**（详见 `docs/RELEASE_NOTES_V0.2.202.md`）：生成端遍历的是
> `CombatState.PlayerCreatures`（= `Creatures.Where(c => c.IsPlayer)`），而召唤物的 `Player` 是 `null`
> （只有 `PetOwner`）→ 那个列表里**没有召唤物**；再 `c != 自己` 之后**单人局里一个都不剩**，
> 「全部友方」等于空转，「不包括召唤物」那个勾也一直无效。
>
> **修法**：
> 1. 循环换成 `CombatState.Allies`（玩家这一边：玩家本体 + 召唤物），过滤条件由新助手
>    `CSharpCodeGen.AllyLoopWhere(e)` 出：默认 `c.IsAlive && c != 自己`（文本逐字节不变）、
>    勾「不包括召唤物」补 `&& c.PetOwner is null`、勾「包括自己」去掉 `c != 自己`。
> 2. 新字段 `EffectSpec.AllAlliesIncludeSelf`（默认 false，**不写进 JSON**：
>    `JsonIgnoreCondition.WhenWritingDefault`，免得老存档 JSON 与 `CET:ForgeGraph=` 标记段变字节）+
>    界面新勾选框（`ShowAllAlliesIncludeSelfRow`，只在「全部友方」时显示）。
> 3. 标记 `// CET:TargetSide=AllAllies CET:AllySelf=1`（卡牌 / 遗物 / 药水三处）+ `ProjectRecovery` 回读。
> 4. 措辞：`LocalizationGen.AllAlliesZh(e)` 一处出（你、队友和所有召唤物 / 队友和所有召唤物 / 你和所有队友 / 所有队友）；
>    列表行尾「→ 全体友方（含自己）」。
>
> **验收**：探针（`sacprobe gen`，含 `SacAllySelf*` 三张 + 五种牺牲伙伴作用对象）生成工程 `dotnet build` **0 error**；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **3960 PASS / 0 FAIL**（新增 11 条）。
>
> **和 88 批的版本关系**：88 批先把 V0.2.201 发掉了（12:53），这批落在 **V0.2.202**。

## 第 88 批需求（V0.2.201：**锻造图同一个【数值 / 能量】模块被两个分支各用一次 → 生成 `CS0103 当前上下文中不存在名称 __fgXXXXXX`**；已发版）

> 用户原话：一张截图 —— `D:\自定义角色存档\MimikyuMod\cs\Cards\MimikyuCard52.cs(80,122):
> error CS0103: 当前上下文中不存在名称"__fgf73650"`。
>
> **复现**（探针 `_probe/fgport`，两条都真复现出来了）：
> 1. `ForgeGraphGen.ValueVar` 的缓存 `ValueEmitted` 是**按整个执行点**去重的，可它写出去的
>    `decimal __fgXXXXXX = …;` 是**就地写进当前语句流**的 —— 落在 `if` 真支的大括号里就出不了那个块。
>    假支再取同一个模块时只看缓存 → 只拿到**引用**，生成
>    `else { … Attack(__fgf73650) … }` → CS0103。探针实测：真支一份声明、假支裸引用。
> 2. 另一个同族 bug：缓存键带出口序号（`模块Id#1`），回收时却 `return node.VarName`（**不带 `p1`**）——
>    【能量】的「额外资源量」出口（第 1 口）被用两处时，第二处拿到 `__fgXXXXXX` 而声明的是
>    `__fgXXXXXXp1` → 同样 CS0103（生成期不报、编译时才炸）。
>
> **修法**（`ForgeGraphGen.cs`）：
> * `GenState.ValueEmitted`（HashSet）换成 `ValueScope`（键 → **声明时所在的作用域**）+
>   `ScopeStack` / `ScopeKey` / `ScopeCovers`；进 `if` 真支 / 假支、【且】/【或】、【条件】的大括号时压一层、
>   出来弹掉（新助手 `EmitInScope`）。
> * `ValueVar` 回收缓存时加一句「声明处罩不罩得住当前作用域」：罩得住 → 直接引用（**这条路上生成文本
>   一个字符都不变**）；罩不住 → 在当前这一段里**重新声明一份**（同名落在兄弟块 / 外层是合法 C#）。
> * 名字统一按 `outPort` 拼（`__fgXXXXXXp1`），不再返回裸 `node.VarName`。
>
> **验收**：探针 `_probe/fgport` 6/6 PASS（含「判定器自检」：喂它一段旧版坏样本，必须能抓到
> `__fg8131bb`；③ 红线：一个模块只用一次仍只声明一次）；`dotnet build` 全解决方案 **0 error**；
> `--uicheck` **3953 PASS / 0 FAIL**（新增 4 条：
> `V0.2.201：同一个【数值】模块在 if 真支 / 假支各用一次…`、`…两个分支各声明一份`、
> `V0.2.201：同一个【能量】「额外资源量」出口用两处 → 两处都带 p1 后缀`、
> `V0.2.201（红线）：一个模块只用一次 → 仍然只声明一次`）。
>
> **给那位作者的话**：这不是他配置写错 —— 用修好的版本**重新生成一次**（不用改存档），
> `MimikyuCard52.cs` 里那条引用就会有对应声明了。
>
> **发版**：`V0.2.201`（部署 `程序文件\Sts2CharForge.exe` 12:52，更新包
> `D:\ds\s\Sts2CharForge_更新包_V0.2.201.zip` 60.7 MB；旧 V0.2.200 包按规矩删了；
> 发版说明 `docs/RELEASE_NOTES_V0.2.201.md`）。

## 第 87 批需求（V0.2.200：**「牺牲伙伴」收益=造成伤害时能选作用对象**）

> 用户原话：「召唤物即死：收益的造成伤害能不能加个选择对象」+ `godot2026-10-17T11.19.53.log`（日志无报错，纯需求）。
>
> **病因**（详见 `docs/RELEASE_NOTES_V0.2.200.md`）：
> 生成端把收益伤害写死 `.Targeting(cardPlay.Target)`；界面上「作用对象」那一栏的判据只看**效果种类**
> （`EffectFieldPolicy.UsesTargetSide`），`PetSacrifice` 不在名单里 → 整行藏起来，配不出「全打」。
>
> **修法**：
> 1. 新判据 `EffectSpec.UsesTargetSideEffective` = 种类清单 ∪「牺牲伙伴 + 收益=伤害」；
>    `ShowTargetSideRow` / 行尾「→ 谁」/ 状态描述 / 生成端注释共用它。
>    （`PetSacrificeGain` 的 setter 补 `Raise`，切档时那一行才会出来。）
> 2. `CSharpCodeGen.EmitPetSacrifice` 的伤害分支：默认档（单体敌人 / 指定队友）走**老路**逐字节不变；
>    其它档走 `EmitPerCreature` 一只一只打（全体敌人 / 随机 N / 全体单位 / 全体友方），外面带
>    `CombatState is not null` 守卫（`AllUnits` / `AllAllies` 那两档 `EmitPerCreature` 自己就有守卫，不叠）。
> 3. 卡面描述（`LocalizationGen.PetSacrificeText`）按作用对象说「指定敌人 / 所有敌人 / 随机 N 个敌人 /
>    场上所有单位 / 指定队友 / 所有友方」；列表行尾的 `side` 映射补 `AllUnits / AllAllies / Ally`。
> 4. 校验器：`SupportsAllUnits` / 队友目标那两条白名单对「牺牲伙伴 + 收益=伤害」放行
>    （队友那两档只在卡牌链放行，状态链会 CS1061）；收益不是伤害时照旧拦住。
>
> **验收**：探针五种作用对象各生成一张卡 → 生成工程 `dotnet build` **0 error**；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；`--uicheck` **3949 PASS / 0 FAIL**（新增 12 条）。
>
> ⚠ 踩坑记一笔：批量改文件时**别用**「读全文 → `Replace("`r`n`r`n","`r`n")` → 写回」这种写法
> （会把整个文件的空行全删掉，`ProfileValidator.cs` 这次就这么毁过一次，已从
> `_备份\源码\源码_20261017_113009` 还原后重做）。要改就用 `edit` 工具按字面替换。

## 第 86 批需求（V0.2.200：**锻造图「造成伤害」在状态 / 遗物 / 药水档把 null 当卡牌传给 `AttackCommand.FromCard` → 敌人回合开始卡死**；已修，随第 87 批的 V0.2.200 一起发）

> 用户原话：`godot - 副本(1).log`（rukayukiking 的 ReyMod）+「还是会卡死」（第二次贴同一份日志）。
>
> **真因**（日志最顶上那条不属于引擎的帧）：
> `Combat #1 turn loop died … NullReferenceException at AttackCommand.FromCard(CardModel card, CardPlay cardPlay)
> ← Rukayukiking.Gokuen.AfterSideTurnStart ← Hook.AfterSideTurnStart ← CombatManager.StartTurn`，
> 「一回合一次、然后 `stuck until the room is restarted`」。生成端 `ForgeGraphGen.EmitEffect` 的 `"Damage"`
> 两支**无条件**拼 `.FromCard({ctx.CardSelfExpr}, {ctx.CardPlayExpr})`，而**状态 / 遗物 / 药水**作用域里
> 没有「自己这张牌」（`CardSelfExpr` / `CardPlayExpr` 就是字面量 `"null"`）→ 生成
> `DamageCmd.Attack(x).FromCard(null, null)`；本体 `AttackCommand.FromCard` 第一句 `Player owner = card.Owner;`
> → NRE → `StartTurn` 整条钩子链断掉。卡牌作用域一直是对的（有 `this` / `cardPlay`），所以只有
> 「状态 / 遗物触发的造成伤害」会炸——用户那张是敌人挂在身上的状态，玩家回合开始/结束各触发一次。
>
> **修法**（`ForgeGraphGen.cs` `EmitEffect`，约 2944 行）：拼 `fromCard` 之前先看 `ctx.CardSelfExpr` 是不是
> 字面量 `"null"`，是就**整段不拼**（状态 / 遗物 / 药水走 `DamageCmd.Attack(x).TargetingAllOpponents(...)` /
> `.Targeting(__fgFoe)`，伤害来源按本体「无来源」处理）；卡牌作用域逐字不变（守「默认档逐字不变」红线）。
>
> **验收**：探针 `_probe/forgecard` 4/4 PASS（卡牌档仍带 `.FromCard(this, cardPlay)`；状态 / 遗物档
> `DamageCmd.Attack` 在、`.FromCard(` 不在）；`dotnet build` 全解决方案 0 error；
> `--uicheck` **3949 PASS / 0 FAIL**（作者 V0.2.199 那版基线 3937；新增 2 条：
> `V0.2.200（红线）：卡牌档的锻造图「造成伤害」仍然 .FromCard(this, cardPlay)`、
> `V0.2.200：状态档的锻造图「造成伤害」**不再调 FromCard**`，两条都 PASS。行名里的 `V0.2.200`
> 就是实际发版的版本号（作者第 87 批同版发）。）
>
> **注意时序**：`程序文件` 里部署的 V0.2.199（11:31 发布）**不含**这条修复（改代码是 12:06），
> 所以那位用户用 V0.2.199 重测一定还炸——V0.2.200 才有。
>
> **记一笔**：`--uicheck` 结果文件是**共享的临时目录**（`%TEMP%\forge_uicheck_*` 固定名），
> 两个实例同时跑会互相 `Directory.Delete` → 其中一个中途抛
> `DirectoryNotFoundException … forge_uicheck_artpersist\美术存档.json`。跑自检前先确认没有残留
> `Sts2CharForge.exe … --uicheck` 进程，只跑一个。

## 第 85 批需求（V0.2.199：**药水图标「怎么在每一个药水都会显示」**）

> 用户原话：「药水上传的图标怎么在每一个药水都会显示」。
>
> **查清两件事**（详见 `docs/RELEASE_NOTES_V0.2.199.md`）：
> 1. 生成端是干净的：探针给 7 瓶里的前 2 瓶各传一张**不同**的图 → 落地
>    `images/potions/large/<entry>.png` 一张一瓶（8894 / 6294 / 其余 404 占位），
>    `potion_atlas.sprites/<entry>.tres` 各自指向自己那张；本体 `PotionModel.PackedImagePath` /
>    `LargeImagePath` 都按 `Id.Entry` 取，本来就不会共用。
> 2. 真凶在工具里：药水页那张预览缩略图绑的是 `Window.PotionIconPreview`
>    （`RelativeSource=AncestorType=Window`），换选中项时 **没有** `PropertyChanged` 就不会重读。
>    `SyncDetail()` 末尾当时只有 `Raise("RelicIconPreview")`，**漏了药水那一条** →
>    给第 1 瓶传了图、再点第 2 瓶，预览还停在上一瓶的图上，看起来就像「每瓶都显示」。
>    修法：补齐 `Raise("PotionIconPreview")`。
>
> **顺手加的**：预览下面一行 `当前这瓶：xxx.png（换一瓶药水这里会跟着变）`（`PotionIconHint`）；
> 构建日志逐瓶写 `· 药水「X」→ images/potions/large/<entry>.png ← 文件名 / 占位图`。
>
> **验收**：探针实测 7 瓶 → 只前 2 瓶是自己的图，其余占位（各文件哈希不同）；
> 老存档逐文件对拍（示例 113 / WhiteCloak 412）0 内容差异（只差 `_files.txt` 里的输出目录名）；
> `--uicheck` **3937 PASS / 0 FAIL**（新增 4 条）。

## 第 84 批需求（V0.2.198：**召唤物：再召唤不再回满血 + 没上传图片也生成占位场景**）

> 用户原话：「触发召唤时好像会直接把召唤物回满血」+ `godot - 副本.log`。
>
> **查到两个原因**（详见 `docs/RELEASE_NOTES_V0.2.198.md`）：
> 1. `PetGen.EmitSummonCmd` 里「已经在场」那条路多补了一句
>    `Heal(alive, MaxHp − CurrentHp)` —— 本体 `GainMaxHp` 自己就会把**新加的那部分**补满
>    （`SetMaxHp` + `Heal(实际加上的上限)`），多补这一句等于**直接奶满**。已删。
> 2. 日志真因：`Cannot open file 'res://scenes/creature_visuals/nuist_pet.tscn'` +
>    `MonsterModel.CreateVisuals` 在回退时抛 `NullReferenceException`（补丁包改过这个方法），
>    `PlayerCmd.AddPet` 半路挂掉 → 宠物没登记 → 每次触发召唤又新建一只**满血**的新宠物。
>    修法：`PetGen.GenerateVisuals` **没上传图片也写占位场景**（贴本体 `images/monsters/error.png`，
>    节点结构照 fallback.tscn），宠物类**永远**覆写 `VisualsPath` 指过去，不再依赖本体回退。
>
> **验收**：真生成一只没图的召唤物 → 场景文件在、贴 error.png、四个唯一名节点齐；生成工程 `dotnet build` **0 error**；
> `Summon` 里再召唤只剩 `GainMaxHp`；老存档逐文件对拍（示例 113 / WhiteCloak 412）0 差异；
> `--uicheck` **3933 PASS / 0 FAIL**（新增 4 条 + 改掉一条过期断言）。

## 第 83 批需求（V0.2.197：**数值公式补齐 .NET 的 Math（常量 / 幂根对数 / 三角 / 双曲 / 取整 / 判断）**）

> 用户原话：贴了一张 .NET `Math` 成员表 +「这些能用吗」。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.197.md`）：
> * `ForgeFormula` 加了 `Fns` 表（名字 → C# 名 + 参数个数 + 生成方式），三种生成方式：
>   **decimal 直调**（min/max/abs/sign/clamp/floor/ceiling/round/truncate —— 实测这几个有 decimal 重载）、
>   **double 转手**（`(decimal)System.Math.Xxx((double)(…))`：pow/sqrt/cbrt/exp/log/log10/log2/三角/双曲/IEEERemainder）、
>   **自己拼**（roundaway / maxmagnitude / minmagnitude / copysign / 角度↔弧度 —— .NET 9 的 Math 上没有后者两个静态方法）；
> * 常量：`Math.PI` / `Math.E` / `Math.Tau`（`MathF.*` 也认同值）；
> * 判断族 `double.IsNaN/IsInfinity/IsFinite`：decimal 不可能 NaN/无穷 → 生成确定常数（恒假 / 恒真）；
> * 解析器支持「类型.名字」前缀（Math / MathF / Convert / double / decimal）、`round(x, AwayFromZero)` 这个枚举写法；
> * 常量折叠整张表都覆盖（要编译期常量的地方，例如卡牌费用，也能用）；
> * 三处语法提示文案同步（App.xaml 两条 + 锻造【数值】模块 hint）。
> * 不支持的（都给中文原因）：`Math.DivRem`（out 参数）、`Math.BigMul`、`Math.ScaleB`、`Math.FusedMultiplyAdd`、
>   `double.NaN / PositiveInfinity` 字面量。
>
> **踩的坑**：`roundaway` 生成的第一版少一层括号（`x>=0 ? floor(...) : ceiling(...) / p` 除号只作用于 else 那支），
> 药水那条链真编译报 CS1003 —— 探针里加了「括号余额」检查抓到的（现在 25 条公式全部余额 0）。
>
> **验收**：探针逐条解析/生成/折叠全过；五处（卡牌/遗物/药水/状态/锻造图）各写一条 `Math.` 公式 → `dotnet build`
> **0 error**；老存档逐文件对拍（示例 113 / WhiteCloak 412）只差 `.sln`；`--uicheck` **3929 PASS / 0 FAIL**（新增 9 条）。

## 第 82 批需求（V0.2.196：**「数值公式」支持条件（比较 / 逻辑 / 三元，布尔当数值用）**）

> 用户原话：贴了一整段 C# 例子（`((2 > 1) ? 1 : 0) * 2`、三个数里正数个数、及格加权、符号函数、
> `(a > 0 && b > 0) ? 1 : 0`、`Convert.ToInt32(2 > 1) * 2` …）+「能不能让生成器使用上述」。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.196.md`）：
> * `ForgeFormula` 的解析器加了优先级链：三元 → `||` → `&&` → `== !=` → `> < >= <=` → `+ -` → `* / %` → 一元 `! - +`；
>   新增 `CompareNode / LogicNode / NotNode / TernaryNode`；
> * **布尔当数值 = 真 1 / 假 0**（`EmitNode` 里统一转换）；**数字当条件 = 不等于 0**（`EmitBool`）；
> * 认 C# 写法：`Math.xxx(…)` 前缀剥离、`Convert.ToInt32(…)`（→ `System.Math.Truncate`）、`true` / `false` 字面量；
> * 常量折叠同步（比较 / 逻辑 / 三元都能折成常数，所以「卡牌费用」这类要编译期常量的地方也能写条件式）；
> * 三处语法提示文案（卡牌页工具提示 / 效果数值框 / 锻造【数值】模块）一起更新（App.xaml 里 XML 要把
>   `<` `>` `&` 转义 —— 踩过一次 MC3000）。
>
> **验收**：用户 15 个例子逐条解析 + 生成 + 折叠全过（探针 `_tmp\fmprobe`，去掉数组下标 / 位移）；
> 四条链 + 锻造图数值栏各写一条条件公式 → 整份工程 `dotnet build` **0 error**；
> 老公式生成文本一个字没变（`2*3` → `((2m) * (3m))`）；老存档逐文件对拍（示例 113 / WhiteCloak 412）只有 `.sln`
> 的随机 GUID 不同；`--uicheck` **3920 PASS / 0 FAIL**（新增 9 条）。

## 第 81 批需求（V0.2.195：**条件选项新增「上一张打出的牌是 攻击/技能/能力/状态/诅咒 牌」**）

> 用户原话：「在条件选项增加『上一张打出的卡是否为技能/攻击/状态/诅咒/能力牌』」。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.195.md`）：
> * `EffectCatalog.Conditions` 加 5 条（`LastCardAttack` / `LastCardSkill` / `LastCardPower` /
>   `LastCardStatus` / `LastCardCurse`；四条链 `ForCard/ForRelic/ForPower` 全开、不要数值）；
> * 生成端 `CSharpCodeGen.ConditionExpr` 加 5 个 case，判据 = 战斗历史
>   `CombatManager.Instance.History.CardPlaysFinished.LastOrDefault(e => e.CardPlay.Player == 玩家)?.CardPlay.Card.Type`
>   （口径：最后一张**结算完**的牌；卡牌上用 = 你打出这张牌之前最后那张，遗物 / 状态用在「打出一张牌后」= 刚才那张）；
> * `ProjectRecovery` 认这段表达式（回读不丢）；卡面 / 悬停文案走原有 `ConditionZh`（`Display` 本身就是整句）；
>   锻造页【条件】候选自动带上（`ForgeGraphCatalog.ConditionPicks` = `Conditions` 里 `ForCard` 那批）。
>
> **验收**：四条链 + 锻造图各配一条 → 整份工程 `dotnet build` **0 error**；
> 从工程恢复把 5 条条件原样认回（`card.whole=LastCardAttack / card.effect=LastCardSkill / relic=LastCardCurse /
> potion=LastCardPower / power=LastCardStatus`）；老存档逐文件对拍（示例 113 / WhiteCloak 412）只有 `.sln`
> 的随机 GUID 不同；`--uicheck` **3911 PASS / 0 FAIL**（新增 11 条，含真回读那条）。

## 第 80 批需求（V0.2.194 追加：**「生成器怎么做荒疫打击」→ 新增内置读数 `ThisCardDamage`（本张牌这一次打出造成的伤害）**）

> 用户原话：「生成器怎么做荒疫打击」→「没有这张卡造成的伤害的参数吗」→「那做 B 这种，把做出来的参数放在参数弹窗里」。
> —— 本体 `BlightStrike.cs`（荒疫打击）的层数是
> `attackCommand.Results.SelectMany(r => r).Sum(r => r.TotalDamage)`（**含被格挡的部分**），
> 而生成器以前只有「本回合 / 本场累计」那几条伤害读数（`DamageDealtThisTurn` 等口径是 **UnblockedDamage**、
> 还会把别的牌 / 中毒 / 遗物打的伤害一起算进来），**没有**「这一张牌这一次打出的伤害」。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.194.md` 的追加章节）：
> * `FormulaBuiltins` 加一条 `ThisCardDamage`（`CardOnly: true`，表达式 `((decimal)this.ForgeThisCardDamage)`，
>   显示名 = 本张牌这一次打出造成的伤害）；`ParamSources.All` 把 `CardOnly` 的读数**排除**在
>   「取值来源」下拉外（参数是打出那一刻先算一次的，选它会恒 0）。
> * 生成端（卡牌页 + 锻造图两条链）：卡片类上生成 `public decimal ForgeThisCardDamage`，
>   `OnPlay` 开头清零、每条「造成伤害」之后
>   `+= (decimal)__atk{n}.Results.SelectMany(hits => hits).Sum(r => r.TotalDamage)`（图上用按模块唯一的 `__fgAtk*`）。
>   用**每条效果自己的**临时变量而不是 `__lastAttack`（随机敌人那档「命中数被夹成 0 时根本没打」不能把上一次再算一遍）。
>   只用「斩杀」时生成的文本**逐字不变**（老存档红线）。
> * 卡面：`LocalizationGen` 对「施加状态 + 公式正好是 ThisCardDamage」特判成
>   「施加等量于所造成伤害的 X。」（不印数字 —— 这条读数**不声明现算预览变量**：那个静态 lambda 只拿到
>   `CardModel`，读不到派生卡片类上的属性）。
> * 校验器（6 条）：非卡牌链（含图上非卡牌）→ 错误；卡牌时机钩子 → 错误；勾「下回合生效」→ 错误；
>   整张牌条件 → 错误；和别的量混写 → 错误（编不过）；前面没有造成伤害 → 警告。
> * 回读：`ProjectRecovery` 认 `…AttackCommand? __atk1 = await DamageCmd.Attack(` 这个新前缀、
>   跳过清零 / 累加两行辅助语句（公式本体走既有的 `CET:AmountFormula=` 标记）。
>
> **验收**：① `--uicheck` **3900 PASS / 0 FAIL**（新增 21 条断言）；② 两份探针存档（手填 + 锻造图）
> `--build` **0 错误** + 导出 PCK OK；③ `--recover` 认回来、`Unparsed` 空；
> ④ 红线：没用这条读数的牌生成文本**一个字符都不多**。
> **踩过的坑**（都写进注释）：`base.ForgeThisCardDamage` 会 CS0117（属性在派生类上）；
> 生成的注释里出现「OnPlay」这个词会让回读 `BodyOf` 把属性那对花括号当成方法体、整张牌效果全丢。

## 第 79 批需求（V0.2.194：**锻造页「要锻造哪一类」加第四档「状态列表」—— 用那块布给自定义状态编效果**）

> 用户原话（第 78 批追问里选的那一项）：「我要的不是模块，是**锻造页左边『要锻造哪一类』里多一个
> 『状态列表』**（像卡牌列表 / 药水列表 / 遗物列表那样，用那块布给自定义状态编效果）」。
> —— 第 77/78 批那个【状态列表】**取值模块**不动（它管「按状态数 / 层数算数值」），这一版管「状态本身」。
>
> **做法**（详见 `docs/RELEASE_NOTES_V0.2.194.md`）：
> * Core：`ForgeTimings` 多一档 Power（**由 `PowerTriggers.All` 生成**，菜单逐条对齐）；
>   `CustomPowerGen` 把「按触发时机发方法」抽成可传**正文**的形式（签名 / 归属守卫 / choiceContext /
>   数值闸门 / 衰减移除全部复用老实现 —— 老路 `bodyText == null` 时逐字不变）；
>   `ForgeGraphGen` 补 Power 档的序幕（`base.Owner.Player` / `base.Owner` / `base.Owner.CombatState`）、
>   `CondCtx.Power`、目标解析（默认只给自己，只有「打谁有意义」的种类才看「作用对象」）、
>   `CardFilter`（打牌类时机）与 `AmountIsStack / TimesIsStack / RepeatIsStack`（= 本状态层数）；
>   回读标记 `// CET:ForgeGraph=` + 从工程恢复 + 校验器三条 + 自动描述。
> * App：锻造页第四档单选 + 左边列表（`Profile.CustomPowers`）+「添加 / 删除」+
>   「⤓ 把现在的触发时机 / 效果搬到这块布上」按钮 + 「自定义状态」页在被布接管时提示去锻造页改。
> * **安全红线**：布上一有模块就算「图说了算」（`ForgeGraphSpec.IsEmpty` 只看模块数）——
>   所以**绝不**在「有老内容」时自动建布（`ForgePowerGraph.CanAutoCreate`），只能用户点按钮搬。
>
> **验收**：① 老存档逐文件对拍（WhiteCloak 31 只状态 / 412 个文件，只有 `.sln` 的随机 GUID 不同）；
> ② 搬迁后整份存档生成 + `dotnet build` **0 error**；③ 「疲劳」「黑暗圣所（减益）」逐行对比老版语义一致
> （失去 6 点格挡打自己、`for t < (int)base.Amount` 循环、组筛选 `CET:CardFilter=` 都保住了）；
> ④ `--uicheck` PASS/FAIL 见发版说明。


## 第 78 批需求（V0.2.193：**【状态列表】里「指定的那个状态」被默认档藏起来了 → 用户以为没有状态可选**；已修，待发版）

> 用户原话：「我怎么在锻造页没看到自定义状态列表」。
>
> **两种情况都处理了**：
> 1. **没装新版**：【状态列表】是 V0.2.192 才加的 —— 发版说明第 0 条写清「先确认程序版本」+
>    模块在**「加模块」下拉**与**积木调色板**两处。
> 2. **装了但看不到状态可挑**（真正的问题）：V0.2.192 里我把「**指定的那个状态**」那一栏做成了
>    `only when 看哪些状态 == 只看指定状态` → 默认档打开这个模块，**整块里一个状态都看不到**，
>    看着就像「这个模块没有状态可选」。这一版改成**四种档位下都摆着**（生成端照样只在 One 档读它），
>    和工具里那条老规矩对齐：**「在哪选状态」这种事不能藏**（当初【效果】模块的「增益 / 减益」
>    就是照这条规矩一直摆着 —— 见 `ForgeCanvasView.EffectFieldApplies` 里的注释）。
>    下拉里的自定义状态依旧**排最前 + ⭐**（`ForgeCanvasView.RefreshPowerItems`）。
>
> **验收**：新增自检 `ForgeCanvas.PowerListFieldVisibleForCheck` + 一条断言「四种档位下这一栏都显示」；
> `--uicheck` **3838 PASS / 0 FAIL**；生成结果一个字没变（`--samplegen` 仍是 5 处既有差异）。
> 详见 `docs/RELEASE_NOTES_V0.2.193.md`。

## 第 77 批需求（V0.2.192：**在锻造页增加「状态列表」模块**；已做，已发版）

> 用户原话：「能不能在锻造增加状态列表」。追问后确认要的是**新模块**（能判断 / 能算），不是参考窗口。
>
> **做法**：新增一种取值模块 **【状态列表】（`PowerList`）**：
> * 入口「归属」接【对象】（不接 = 打出牌时选中的那个目标），出口「数值」出 `Number`；
> * 三栏：**看哪些状态**（全部 / 只看增益 / 只看减益 / 只看指定状态 —— 指定那个走可搜索下拉）、
>   **算哪一个**（状态数 / 层数合计）；
> * 生成端：改走新运行时助手 `ForgeFormulaRuntime.PowerCount/PowerLayers(单位, filter, powerId)`
>   （筛选用 `power.TypeForCurrentAmount` 分增益/减益、`power.GetType().Name` 比指定状态 ——
>   和已有 `PowerAmount` 同一套口径）。于是「身上有任意减益」「状态数 ≥ N」这类判断
>   用【状态列表】+【关系符】就能写出来（本体自己的写法也是这样）。
> * 界面：目录（`ForgeGraphCatalog`）+ 积木调色板（`ForgeBlockView.Palette`）+ 画布字段
>   （`ListFilter` 改了要重建，好让「指定的那个状态」那一栏冒出来）+ 连线规则（能接数值的几处
>   都收它）+ 卡面文案（`LocalizationGen.RelateOperandText`）。
> * 校验器：选了「只看指定状态」却没选状态 → 错误；数值出口没接线 → 警告（搭了没人用）。
>
> **验收**：三种用法真生成核对（接效果数值 / 接关系符+if / 只看指定状态+层数合计）；
> 探针工程 `dotnet build` **0 错误**（新助手都写进了生成的 `ForgeFormulaRuntime.cs`）；
> 卡面文案「自己身上的状态数量 大于 3」✓；`--uicheck` **3837 PASS / 0 FAIL**（新增 6 条）；
> `--samplegen` 仍是 113 → 114 的 5 处既有差异（样例没有这个模块 ⇒ 默认档零影响）。
> 详见 `docs/RELEASE_NOTES_V0.2.192.md`。

## 第 76 批需求（V0.2.191：**节点图上「负数 = 反向」只剩格挡做了，其余五种静默无效**；已修，已发版）

> 用户追问：「所以现在失去金币能生效了吗」——查了一遍五条链，答案是：
> 卡牌 / 遗物 / 药水**一直就能**（`GainGoldLine`）、状态链 V0.2.190 修好、
> **锻造节点图还没做**（V0.2.174 只补了 `Block` 那一档）。这一版补齐节点图：
>
> | 节点图上填负数 | 旧版生成 | 本体行为 | 现在 |
> |---|---|---|---|
> | Gold | `PlayerCmd.GainGold(-N, p)` | 非正数直接 return | `PlayerCmd.LoseGold(N, p)` |
> | Energy | `PlayerCmd.GainEnergy(-N, p)` | 非正数直接 return | `PlayerCmd.LoseEnergy(N, p)` |
> | ExtraResource | `PlayerCmd.GainStars(-N, p)` | **抛 ArgumentException** | `PlayerCmd.LoseStars(N, p)` |
> | Heal | `CreatureCmd.Heal(foe, -N)` | 非正数什么都不做 | 改走 `CreatureCmd.Damage(…, Unblockable\|Unpowered\|Move, card, cardPlay)` |
> | MaxHp | `CreatureCmd.GainMaxHp(foe, -N)` | 非正数什么都不做 | `CreatureCmd.LoseMaxHp(choiceContext, foe, N, isFromCard: true)` |
>
> 判据与 V0.2.174 的 Block 那一档完全一致（`!amountN.Runtime && amountN.Value < 0m`），
> 都是**只在常数负数时**分档；运行时公式 / X 的正负生成期不知道 → 照旧按「获得」生成。
> 「下回合生效」那几档不受影响（它们在更上面就被延迟状态/EnergyNextTurnDebt 那条路接走了）。
>
> **验收**：6 种负数（Gold/Energy/ExtraResource/Heal/MaxHp/Block）逐条生成核对 + 生成物 `dotnet build` **0 错误**；
> **从工程恢复**六张探针卡原样认回来（-6 / -6 / -6 / -6 / -6 / -6，无「没认出来」）；
> `--uicheck` **3830 PASS / 0 FAIL**（新增 5 条钉住节点图这五种）；
> `--samplegen` 仍是 113 → 114 的 5 处既有差异。
> 详见 `docs/RELEASE_NOTES_V0.2.191.md`（里面有一张「负数 = 反向 × 五条链」的总表）。

## 第 75 批需求（V0.2.190：**状态链上「数值填负数 = 反向」有五种没做（失去格挡静默无效）**；已修，已发版）

> 用户原话：「状态失去格挡还是没有正常生效」＋ `WhiteCloakMod.json` ＋ `godot (16).log`。
> 「还是」是因为 V0.2.174 只修了**锻造节点图**那条链（用户当时报的是「锻造图里格挡填负数失去不了格挡」）。
>
> **真因**：`CustomPowerGen` 的 `case "Block"` 从来不看正负，一律
> `await CreatureCmd.GainBlock(base.Owner, {amt}, …)` —— 而本体 `CreatureCmd.GainBlock` 对**非正数直接返回**。
> 用户存档里的「疲劳」（玩家回合结束时 格挡 -6）生成出来就是 `GainBlock(base.Owner, -6m, …)` ⇒ 一点格挡都不掉。
>
> **顺带查出同一族另外四处**（卡牌 / 遗物 / 药水 / 节点图四条链早就有负数分支，只有状态链漏）：
>
> | 种类 | 填负数时旧版生成 | 本体行为 | 现在 |
> |---|---|---|---|
> | Block | `GainBlock(-N)` | 非正数直接返回（静默） | `CreatureCmd.LoseBlock(choiceContext, base.Owner, Abs, base.Owner)` |
> | Heal | `Heal(-N)` | 非正数什么都不做 | 改走 `Damage(…, Unblockable \| Unpowered, …)`（和卡牌链同口径） |
> | Energy | `GainEnergy(-N)` | 非正数直接返回 | `PlayerCmd.LoseEnergy(Abs, player)` |
> | Gold | `GainGold(-N)` | 非正数直接返回 | `PlayerCmd.LoseGold(Abs, player)` |
> | ExtraResource | `GainStars(-N)` | **直接抛 ArgumentException** | `PlayerCmd.LoseStars(Abs, player)` |
>
> ⚠ 第五条是**用户存档里真实存在**的一条（状态「黑暗圣所（减益）」：打出一张牌时 额外资源量 -1）——
> 旧版每打一张牌就抛一次异常（不是静默），这也解释了「怎么老是怪怪的」。
>
> **回读**：`ProjectRecovery` 补了 `await PlayerCmd.LoseStars(` 这一行（以前会当成「没认出来」丢掉那条效果）。
>
> **验收**：用用户那份存档真生成 + 真 `dotnet build`（**0 错误** 29 警告）；
> 从工程恢复：「疲劳」格挡 = -6、「黑暗圣所（减益）」额外资源量 = -1 都原样认回来（没认出来 40 → 38 处）；
> `--uicheck` **3825 PASS / 0 FAIL**（新增 5 条钉住这五种）；`--samplegen` 仍是 113 → 114 的 5 处既有差异。
> 详见 `docs/RELEASE_NOTES_V0.2.190.md`。

## 第 74 批需求（V0.2.189：**「取几张 = 最多 N 张」在五条链上没生效（当成恰好 N）**；已修，已发版）

> 用户原话：「最多N张和恰好N张都是恰好，最多怎么不能少选」＋ 两份日志。
>
> **真因**：选牌那句 `CardSelectorPrefs` 有的走公用写法 `SelectPrefsExpr`、有的**硬写两参构造**
> （本体两参 = 恰好 N）。逐条生成核对（探针程序 `_tmp\cmprobe`）：
>
> | 种类 | 修前 | 修后 |
> |---|---|---|
> | 变化卡牌（选哪些牌变） | `new CardSelectorPrefs(TransformSelectionPrompt, 3)` ✗ | `(…, 0, 3)` ✓ |
> | 复制卡牌 | `(…, 3)` ✗ | `(…, 0, 3)` ✓ |
> | 重放卡牌 | `(…, __replayWant)` ✗ | `(…, 0, __replayWant)` ✓ |
> | 回合结束时自动打出 | `(…, 3)` ✗ | `(…, 0, 3)` ✓ |
> | 给予关键词 | `(…, 3)` ✗ | `(…, 0, 3)` ✓ |
> | 删牌 / 全局变化 / 全局升级 | `(…, 3)` ✗ | `(…, 0, 3)` ✓ |
> | 消耗 / 丢弃 / 升级 / 三种拿牌 / 节点图 | 本来就对（`minCount: 0, maxCount: N`） | 不变 |
>
> 游戏里的表现：本体的选牌界面「选满 MaxSelect 自动结算」，只有 `Min != Max` 才会出现**确认按钮**
> （`NPlayerHand.RefreshSelectModeConfirmButton`：`count >= MinSelect && count <= MaxSelect` → Enable）。
> 我们把 Min 也写成 N ⇒ 确认按钮永不亮 ⇒ 少选就卡在选牌界面。
>
> **修法**：上面 9 处（`CSharpCodeGen.cs` 的 6472/6474、6267/6269、6337/6339、6759/6761、6794/6796、
> 11691/11693、1334、1363、1397、1427）统一改走 `SelectPrefsExpr(e, prompt, n)` ——
> 恰好档它原样产出两参构造（**默认档逐字不变**），最多/任意档产出 `0, N` / `0, 999999999`。
>
> **验收**：探针把 13 个种类 × {UpTo, Any} 真生成一个工程 → `dotnet build` **0 错误**；
> 逐种类断言（uicheck 新增 2 条）：「每个种类都生成 min=0」+「恰好档逐字一致」；
> `--samplegen` 仍是 113 → 114 的 **5 处既有差异**（默认档零影响）。
> 详见 `docs/RELEASE_NOTES_V0.2.189.md`。
>
> ⚠ 顺带发现：自检里那条「V0.2.150：卡牌链『取几张』——显示的都被生成端用到」用的是**机械测量**
> （生成两版对比、`StripMarkers` 已经会剥掉 `CET:` 标记），但它当时**没抓到**这个问题 ——
> 因为 `EffectFieldPolicy.UsesCountMode` 是**硬编码名单**，而那条断言比的是名单 vs 界面可见性。
> 这一版补的两条是**直接钉生成代码**的（`min=0` 有没有出现），以后这类「名单说读了、其实没读」
> 就藏不住了。

## 第 73 批需求（V0.2.188：**节点图的「目标卡」只认自己的卡 → 选本体卡时静默退化**；已修，已发版）

> 用户指认了出问题的那张卡：「挖肉补疮，英文 ID BugkillerCard56」＋ `godot (14).log`。
> 把那份存档真生成出来看，**两条效果都是坏的**，坏在同一处：
>
> ```
> 效果①：变化卡牌 3 张 · 目标卡 MinionSacrifice（本体「昆虫防御」）· 自己选 · 放手上
> 效果②：生成卡牌 3 张 · 目标卡 Infection（本体「感染」）· 放弃牌堆
> ```
>
> 旧版生成出来是：① `CardCmd.TransformToRandom`（**随机变化**，目标卡被无视）
> ② 整条丢掉、只留注释「缺必填的东西（状态 / 目标卡…）」。
>
> **真因**（`ForgeGraphGen.cs` 生成【效果】模块前那段）：
> `spawnCardClass` 只在 `p.AllCards`（**自己存档的卡**）里 `FirstOrDefault` —— 用户选的是**本体卡** →
> 找不到 → `spawnCardClass` 留 null → ①走「没有目标卡」那条分支退化成 `TransformToRandom`；
> ②在 switch 里 `spawnCardClass is null ? null : …` 整条不生成（还配一句会把人带偏的「缺必填」注释）。
>
> **修法**：目标卡认两种 —— 自己的卡用它的（当前）类名，**本体卡按用户选的类名原样用**，
> 交给 `CSharpCodeGen.CardTypeExpr` 写全名（本体卡 → `MegaCrit.Sts2.Core.Models.Cards.<X>`，
> 已经是全名时原样返回）。顺带把节点图里两处 `CreateCard<{spawnCardClass}>` 也套上 `CardTypeExpr`
> （以前直接写短名，同名类会撞），并把卡牌页 `EmitTransformBody` 的 `CreateCard<目标卡>` 也套上（同因）。
>
> **顺带**：节点图的【变化卡牌】/【生成卡牌】以前**没有**「目标卡认不认得出」这条检查
> （卡牌页早就有 `ValidateCardTargetExists`）；这一版抽出 `ProfileValidator.CardTargetProblem`
> 并接到节点图校验里 —— 名字写错 → 生成前就报 `错误`（不然现在会写成全名，编译期才 CS0246）。
>
> **验收**：用户那份存档真生成 + 真 `dotnet build`（0 错误 33 警告），`BugkillerCard56.cs` 两条效果都正确；
> `--uicheck` **3818 PASS / 0 FAIL**（新增 3 条：变化目标=本体卡 / 生成目标=本体卡 / 校验器不误报）；
> `--samplegen` 113 → 114（5 处差异，全部既有 + V0.2.187 的新文件；样例无锻造卡 ⇒ 本版零影响）。
> 详见 `docs/RELEASE_NOTES_V0.2.188.md`。
>
> ⚠ 「会卡死」这一条在两份日志里**没有直接证据**（打出那张牌的下一行就是正常的选牌 + 后续照常战斗）。
> 发版说明里请他重做「生成 + 一键构建 + 替换 mod」再测，并把卡死时的日志/卡在哪一步发回来。
> 已排除的一个可能来源：旧版那条「随机变化」会去本体卡池随机抓卡，而 `CardCmd.Transform` 在
> 「候选为空 / 拿不到手牌节点」时**直接抛异常**（抛在卡牌打出中间 = 一轮卡死）—— 这版把这条随机路径拿掉了。

## 第 72 批需求（V0.2.187：**mod 角色一点「卡牌图鉴」就 KeyNotFoundException（卡死）**；已修，已发版）

> 用户原话：「变出来的牌抽不上来，会卡死」＋ 两份日志（`godot2026-10-15T08.23.42.log` / `08.25.44.log`，
> 都是猎虫人 Bugkiller 的局）。两份日志里唯一一条真报错：
>
> ```
> ERROR: KeyNotFoundException: The given key 'CHARACTER.BUGKILLER (28743066)' was not present in the dictionary.
>    at ...NCardLibrary.OnSubmenuOpened_Patch1(NCardLibrary this)
>    at ...NSubmenuStack.Push(NSubmenu screen)      ← 点「卡牌图鉴」时
> ```
>
> **真因（本体源码）**：`NCardLibrary._Ready()` 那张 `Dictionary<CharacterModel, NCardPoolFilter> _cardPoolFilters`
> 只 Add 了 **5 个官方角色**（`NCardLibrary.cs:290-294`），而 `OnSubmenuOpened()` 第 470 行直接
> `_cardPoolFilters[characterModel]` → **mod 角色必抛**，异常抛在 `NSubmenuStack.Push()` 里 ⇒ 图鉴卡在打开到一半。
> 顺带：这张表里没有我们的卡池 ⇒ **图鉴里本来就看不到自己角色的卡**（以前没察觉，因为先崩了）。
>
> **修法**（新 `CardLibraryGen.cs` → 每个存档都写 `cs/ForgeCardLibrary.cs`）：
> `[HarmonyPrefix]` 打在本体 `NCardLibrary.OnSubmenuOpened`（public + `nameof`，编译期可查 ⇒ 走 PatchAll 安全）：
> 1. 只在这一局玩的是我方角色时动手（`RunManager.IsInProgress` → `LocalContext.GetMe`）；
> 2. 现造一个筛选器按钮：实例化本体那 8 个同款场景
>    `res://scenes/screens/card_library/library_pool_toggle.tscn`（自带的 `%SelectionReticle` 在**它自己的场景**里，
>    所以克隆节点的 `_Ready` 能正常跑 —— 这也是不直接 `Duplicate()` 本体那 8 个节点的原因）；
> 3. 头像 = `images/ui/top_panel/character_icon_<CharSlug>.png`（我们自己那张，缺失时退回本体铁甲图）；
> 4. `Loc` = 我们自己的 `characters.<CharEntry>.title`（悬停提示显示角色名）；
> 5. 谓词 `c.Pool is <我们的卡池>` 进 `_poolFilters`，并把按钮的 `Toggled` 接到本体私有的
>    `UpdateCardPoolFilter`（radio 互斥 + 刷新手感与本体那 8 个完全一致）；
> 6. **退路**：造按钮/接事件任何一步失败 → 挂到本体 `%MiscPool` 上（仍然不崩）；整体 try/catch + `Log.Error`。
>
> **顺带**（日志里 `[WARN] Unknown ModelId 'CARD.BUGKILLER_CARD18' during serialization, writing NONE`）：
> 存档（进行中的那一局 / 旧存档）里存的是**类名字符串**，用户**改过某个模型的英文类名**
> （`BugkillerCard18` → `Fulan`，卡片列表里 17 后面就是 `Fulan`、再是 19）→ 那一局里那张牌变成「不存在的模型」，
> **抽到它就卡住**。这条我们**改不了存档**，只能在 `NameStabilizer.WarnRenamedClasses` 里报出来：
> 「X 的英文类名从 A 变成了 B —— 旧存档里引用的还是 A，抽到可能卡住；想保住旧存档就把类名改回 A」
> （用旧类名表里「类名消失但同名模型还在别的类名下」判定）。
>
> **红线实测**：图鉴补丁生成物 `dotnet build` 0 错误（探针 + ExampleMod 样例各一份）；
> 改名警告用探针类名表验过（真的打出来了）；`--uicheck` **3815 PASS / 0 FAIL**（新增 4 条）；
> `--samplegen` 113 → 114（新增 `cs/ForgeCardLibrary.cs`，其余 4 处为既有差异）。
> 详见 `docs/RELEASE_NOTES_V0.2.187.md`。
>
> ⚠ 还没 100% 确认用户说的「变出来的牌抽不上来」是**图鉴**还是**战斗里生成/变化的牌**；
> 发版说明里已请他指认（哪张卡/哪个状态、卡在哪一步），若是战斗那条链下一批再查。

## 第 71 批需求（V0.2.186：**「状态在血条上的覆盖色没显示出来」**；已修，已发版）

> 用户原话：「状态在血条上的显示好像有问题，没显示出来」＋ `godot (13).log`。
> 追问后确认是**自定义状态那条「覆盖血条」**（像中毒那样盖一截颜色）**没出现**。
>
> **先说结论**：日志里**查不出原因** —— 这条链以前**每一处失败都不出声**。
> `.log` 里跟这个功能相关的**一个字都没有**，于是「存档没填颜色 / 补丁没打上 / 打上了没触发 /
> 触发了没画」这四种（修法完全不同）在日志里长得一样。所以这一版**先让它会说话**，
> 再把两处**确实会导致看不见**的地方修掉。
>
> **核对过的事实**（用反射读本机 `sts2.dll`，v0.111.0，与用户日志同版本）：
> `NHealthBar` 确实有私有字段 `_creature`（`FieldRefAccess` 真调过一次，拿到委托）；
> `RefreshForeground` 是私有的、**由 public 的 `RefreshValues` 调用**；
> `%PoisonForeground` / `%HpForeground` / `%DoomForeground` 三个节点名都还在。
> 另外用一个**同构的 Godot 场景**（Godot 4.5.1）跑了一遍 `Postfix` 的动作：从 Mask 上查
> `%PoisonForeground` 拿得到、`Duplicate()` + `AddChild` 正常、算出来的那一段落在 Mask 里。
> ⇒ 补丁的假设全都成立，「静默 return」之外没有别的解释。
>
> **修法**：
> 1. **诊断**：`ModEntry` 加载时报一行「带了哪几条」（`HpBarOverlayGen.LoadSummary`）；
>    两个入口各一条补丁失败警告；`Postfix` 里**每种「没画」的理由各记一条**
>    （`WarnOnce(bit, why)`，带当时的 Mask 宽 / MaxHp / Hp）、**第一次真画上去**记一条
>    （`NoteDrawn`：层数 + 那一段的坐标 + 血条宽）、**钩子进来了但身上没有配色状态**记一条
>    （`NoteRanOnce`）—— 四种情况在日志里彻底分开。
> 2. **12px 下限**：本体 `GetFgWidth` 是 `Math.Max(val, 12f)`，以前我们是纯线性
>    ⇒ 1~3 层只有 3~10px，而那截条是九宫格（左右边距各 6px）→ **画不出来**。照抄本体下限。
> 3. **两个入口**：除 `RefreshForeground` 外，把它的调用方 `RefreshValues` 也补一份
>    （画是幂等的：先 `HideOurs` 再摆）。
> 4. **少一处可能失败的唯一名查找**：`Ensure` 不再从 Mask 上重查 `%PoisonForeground`，
>    模板由 `__instance` 传进来。
> 5. **顺带**（日志里一次刷 8 条的 `Missing sprite ... in power_atlas`）：没上传图标的自定义状态
>    以前落到本体 `missing_power.png`（紫色问号）→ 现在给它写一张**中性占位图**
>    `images/powers/<状态>.png` + `power_atlas.sprites/<状态>.tres`（上传过图标的照旧优先，不覆盖）。
>
> **红线实测**：带 3 条覆盖色（含「从左」档）的探针存档 → 生成工程 `dotnet build` **0 错误**
> （第一次跑真抓出我改出来的一个参数错误，已修）；同存档 8 个自定义状态各生成占位图 + 图集 .tres；
> `dotnet build Sts2CharForge.sln` 0 错误；`--uicheck` **3811 PASS / 0 FAIL**（覆盖血条新增 4 条断言）；
> `--samplegen` 对拍 113 个文件**只有 4 个预期差异**（样例存档没有自定义状态 ⇒ 本批对样例零影响）。
> 详见 `docs/RELEASE_NOTES_V0.2.186.md`。
>
> ⚠ 下次再遇到「某个功能静默不生效」的报障：**先看这条链上有没有 `return` 不带日志** ——
> 这是第二次栽在同一个坑里（第 69 批的「消耗卡牌填 0」也是静默丢弃）。

## 第 70 批需求（V0.2.185：**生成代码引用本体类型时写的是短类名 → `CS0104 Void` / `CS8121 Defect`**；已修，已发版）

用户实测（Shadow 那个 mod 的构建失败弹窗，两个错误一起出）：

```
cs\Cards\TheShadowCard12.cs(36,53): error CS0104: "Void" 是 "MegaCrit.Sts2.Core.Models.Cards.Void"
                                    和 "void" 之间的不明确的引用
cs\Patches.cs(121,54):             error CS8121: "Defect" 类型的模式无法处理 "CharacterModel" 类型的表达式
```

**真因**：生成端引用**本体类型**时一律写**短类名**，一旦作用域里有同名类型就被抢走：

* 本体**真有一张卡叫 `Void`**（`src\Core\Models\Cards\Void.cs`）→ 短名 `Void` 和 `System.Void` 撞 ⇒ CS0104
  （那张牌的效果指定了本体卡「虚空」，生成的是 `AddToCombatAndPreview<Void>(…)` / `ModelDb.Card<Void>()`）；
* `cs\Patches.cs` 里那句「是不是本体那 6 个官方角色」的
  `character is Ironclad or Silent or Regent or Defect or Necrobinder or Deprived`
  → 本 mod 自己有个类叫 `Defect` 时，编译器把 `Defect` 当**模式类型**（不是本体角色类）⇒ CS8121。

**修法**：
1. 新增 `CSharpCodeGen.CardTypeExpr(p, className)`：**我们自己的卡**仍写短名（同一个命名空间）；
   **本体卡一律写全名** `MegaCrit.Sts2.Core.Models.Cards.<X>`（留空 = `Shiv`，也走全名）。
   所有「目标卡」发射点改用它：`ModelDb.Card<…>()`（初始卡组里的本体卡 / 生成到牌组 / 变化 / 先古卡池 /
   生成卡牌效果）＋ `CardPileCmd.AddToCombatAndPreview<…>()` ＋ 锻造图「变化成指定卡」那两处。
2. `Patches.cs` 的角色判断改成 6 个**全名**；同文件里两处 `ModelDb.Character<Ironclad>()` 也写全名。
3. **回读侧跟着放宽**（否则新产物回读会把全名当类名）：`ProjectRecovery` 里
   `AddToCombatAndPreview<(\w+)>` / `ModelDb\.Card<(\w+)>` → `([\w\.]+)`，并新增 `ShortCardName()` 去掉命名空间前缀
   （存档里 `SpawnCardId` 一直是短名，界面下拉 / 校验器 / 老存档都按短名走）。

**红线实测**：
* 探针 `_probe\nqcheck`（新）：造一份「目标卡 = 本体 `Void`」＋「本 mod 自己有个类叫 `Defect`」的存档 →
  **修前**生成出来的工程 `dotnet build` 报的正是 `CS0104 Void`；**修后** 6/6 断言 PASS，且**生成出来的工程 `dotnet build` 0 错误**。
* `dotnet build Sts2CharForge.sln` 0 错误；`--uicheck` **3807 PASS / 0 FAIL**
  （新增 3 条：本体卡全名 / 自己的卡仍短名 / 角色判断全名；另有 4 条钉住旧短名文本的断言同步更新）。
* **输出影响面**：样例工程逐文件对拍 **110 个一致 / 2 个不同** —— `cs\Patches.cs`（本批）＋ `cs\ModEntry.cs`（第 67 批的异常日志）；
  **卡牌文件一个都没变**（样例里没有「生成指定本体卡」的牌）。
  顺手改的 `ModelDb.Character<Ironclad>()`（角色文件的 CharacterSelectSfx）与 `CurseCardPool`/`StatusCardPool`
  已**回退** —— 那几处不是本次必需，避免无谓的输出变更。
* **已打包发版**：`tools\release_all.ps1 -Version V0.2.185 -NoRelease -NoCommit -Yes`；
  更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.185.zip`（60.7 MB），程序部署到 `D:\ds\s\4\程序文件`（V0.2.185，旧启动器 / 旧包自动清掉）。
  部署后那份 exe 的 `--uicheck` = **3806 PASS / 0 FAIL**（含本批 3 条），Core.dll 里能搜到 `MegaCrit.Sts2.Core.Models.Cards.`。

## 第 69 批需求（V0.2.180（与第 66~68 批同版发出）：**「消耗卡牌」填 0 = 消耗这张牌自己**；已修，本地已发版）
> 用户原话：「卡牌时机『被保留』时，状态牌应当触发的『消耗自身』没有触发。疑似因为消耗卡牌，数值填充 0
> 不会消耗自身而是跳过。」——**用户猜得完全对。**
>
> **真因**：`CSharpCodeGen.AmountIsStrength`（「数值 = 力度」的种类名单）里有 `ExhaustCard` ⇒ 0 = 不生效；
> `IsInertZero` 据此把整条效果**从生成里丢掉**（卡面也不写）⇒ 「消耗卡牌 数值 0」连一行代码都没生成。
> 而「数值 0 = 这张牌自己」那一档（`AmountZeroIsSelf`）以前只认 `DiscardCard`。
>
> **修法**：`AmountZeroIsSelf` 同时认 `DiscardCard` + `ExhaustCard`（`Amount <= 0 && !AmountIsX`）；
> `EmitExhaustCard` 加 `selfExpr` 参数（照 `EmitPlaceCard` 那套），**只有卡牌链传 `"this"`**，命中时生成
> `await CardCmd.Exhaust(choiceContext, this);`（走本体 API ⇒ `AfterCardExhausted` 照常派发）；
> 卡面新增「消耗这张牌自己。」（照「丢弃这张牌自己。」同款）；遗物 / 药水 / 状态上填 0 保持「不生效」，
> 但校验器**新增警告**说清（以前是静默什么都不做 —— 这次踩的就是这种静默）。
> 填 ≥ 1 的一切照旧（老存档逐字不变）。
>
> **验证**：探针三档（被保留+0 → `Exhaust(choiceContext, this)`；同档+2 → 仍是随机消耗 2 张；
> 遗物+0 → 无 Exhaust 语句 + 警告）→ 生成工程 `dotnet build` **0 错误**；
> `--uicheck` **3799 PASS / 0 FAIL**（新增 3 条 + 把老断言「消耗卡牌填 0 仍然是不生效」改成新口径）；
> `--samplegen` 仍是那 3 个已知差异（`.sln` / `ModEntry.cs`（V0.2.179 有意改的）/ `SevenGuard.cs`），**没扩大**。
> 详见 `docs/RELEASE_NOTES_V0.2.180.md` 第四节。

## 第 68 批需求（V0.2.180（与第 66 / 67 批同版发出）：**锻造卡两块布都设「这张牌被抽到时」→ 两个 `AfterCardDrawn` → CS0111**；已修，本地已发版）
> 用户原话：「锻造的升级前升级后触发时机均有这张卡被抽到时，会报错拦住」+ 一份构建日志。
>
> **证据（日志最后几行）**：`WhiteCloakTensionMax.cs(99,32): error CS0111: 类型"WhiteCloakTensionMax"
> 已定义了一个名为"AfterCardDrawn"的具有相同参数类型的成员`。日志里那两个 42 MB 的
> `// CET:ForgeGraph=` base64 注释挤在一起像是「被校验器拦住」，其实真因就是这一行 CS0111。
>
> **真因**：「升级前 / 升级后」是**两块布**、各自独立生成；两块都设同一个卡牌时刻时**各写了一个 override**
> → 一个类里两个同名同参方法。V0.2.169 处理过同一类坑（图的时刻撞**卡自己的**钩子 → 改私有方法），
> 但那条只在「卡自己也有这个钩子」时生效 —— **「两块布互相撞」这一种漏了**。
> 已复现：探针造一张两块布都设 `CardDrawn` 的卡 → `AfterCardDrawn` 定义 2 个。
>
> **修法**：新增 `ForgeGraphGen.CardGraphOverlapHooks(card)`（两块布都用的卡牌钩子；**只有
> `UpgradedForgeGraph` 真存在时才算**）；`CardHooks` 把这些钩子也变成**私有方法**；
> `CSharpCodeGen.EmitGraphOnlyHookContainers` 补**唯一一个**壳 override，按 `base.IsUpgraded` 二选一调用。
> 守卫照「卡牌时机」那份逐字同款（`card != this` + 战斗中；Retained 那档走 `player != base.Owner` +
> `retainedCards.Contains(this)`）。覆盖五档：被抽到 / 被消耗 / 被丢弃 / 被保留 / 被自动打出。
> **只有一块布时一个字符都不变**（图自己写 override 就够了）。
> ⚠ 第一版把「升级后没单独配 → 退回升级前那块布」也判成「两块都撞上」→ 凭空多出一个 `AfterFlush` 壳 override，
> 自检那条「不许生成 `AfterFlush(PlayerChoiceContext)` 这种不存在的签名」当场 FAIL；已改对并补了反例断言。
>
> **验证**：探针 5 张碰撞卡（五档两块布同设）+ 5 张「卡自己也有那个钩子」对照卡 → 真生成
> （每个钩子 override 都是 0 或 1 个，修前「被抽到」是 2 个）→ 生成工程 `dotnet build` **0 错误**；
> `--uicheck` **3797 PASS / 0 FAIL**（新增 4 条，含上面那条反例）；
> `--samplegen` 对 V0.2.143 基线：113 个文件里 3 个不同 —— `.sln` 随机 GUID、`SevenGuard.cs`（V0.2.170 起既知）、
> `ModEntry.cs`（**V0.2.179 那次有意改的**：初始化失败打全量 InnerException）；本轮**没有新增差异**。
> 详见 `docs/RELEASE_NOTES_V0.2.180.md` 第三节。
> 已发版：`D:\ds\s\4\程序文件` = V0.2.180、更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.180.zip`（60.7 MB，397 个文件）。
> **GitHub Release 还没发**（用户在修完这一条之后才会需要，等他一句话再传）。

## 第 67 批需求（V0.2.180（与第 66 批同版发出）：**本体给 `CardModel.GetDescriptionForPile` 加了重载 → 特性补丁歧义 → PatchAll 抛异常 → 整个模组初始化中断**（游戏里「找不到这个角色 / 存档」）；已修，已发版）

用户实测（本机 `%APPDATA%\SlayTheSpire2\logs\godot.log`）：进游戏**找不到 Ainodi 那个角色**。
日志里先只有一句 `[ERROR] AinodiMod - 加载失败` + 引擎栈（**没有异常类型/消息**），往下 50 行才露出真因：

```
[ERROR] Ambiguous match for HarmonyMethod[(class=MegaCrit.Sts2.Core.Models.CardModel,
        methodname=GetDescriptionForPile, type=Normal, args=undefined)]
   at AinodiMod.AinodiModInitializer.Initialize()
```

**真因**：本机 `sts2.dll` 里 `CardModel.GetDescriptionForPile` 现在有 **2 个重载**
（`(pileType,target)` 2 参 / `(pileType,previewType,target)` 3 参，`_probe\sigprobe` 实测），
而「被给予自定义关键词」那两个补丁是**带 `[HarmonyPatch(typeof(CardModel), "GetDescriptionForPile")]` 特性**、
由 `harmony.PatchAll(...)` 打的 —— 只写方法名 ⇒ Harmony `Ambiguous match` 抛异常；
**PatchAll 一抛异常，整个初始化器（一个大 try/catch）就中断** ⇒ 角色 / 卡池 / 遗物全都注册不上
⇒ 游戏里表现为「找不到这个角色（连带存档里的进度看着也没了）」。
这个坑工具里另外三处补丁**早就**改成手动打并留了注释（`PatchesGen.cs:151`、`HpBarOverlayGen.cs:122`、
`CSharpCodeGen.cs:926`：「PatchAll 抛异常的话角色注册也一起没了」），只有这一处还是特性补丁，
本体一加重载就踩中。

**修法**：
1. `GivenKeywordTextPatch` / `GivenKeywordHoverPatch` **去掉 `[HarmonyPatch]` 特性**，Postfix 改 `public static`（入口要 `nameof` 它）。
2. `ModEntry`（`PatchesGen.ModEntrySource`）里**逐个重载手动打、各自一个 try/catch**：
   `GetMethods(Public|NonPublic|Instance)` 里名字是 `GetDescriptionForPile` 的**全挂上**（2 参 / 3 参都挂，本体以后再加也不怕）；
   悬停提示走 `GetProperty("ExtraHoverTips").GetGetMethod(true)`；失败只 `Log.Warn`，绝不再连坐。
3. 顺手把初始化器的失败日志改成**打全量**：解包 `TargetInvocationException.InnerException` 并 `Log.Error(inner.ToString())` ——
   这次日志里只有「加载失败」四个字，真因得往下翻 50 行才看到（盘上那份旧产物连 `e.Message` 那行都没有）。
   ⚠ 这一条会**改动所有存档**生成的 `cs/ModEntry.cs` 文本（多几行日志），属于有意的输出变更、不是「默认档逐字不变」。

**红线实测**：
* 探针 `_probe\gkcheck`（新）：造一个用「给自定义关键词」的存档 → 生成 → 8 条断言全 PASS
  （两个补丁不带特性、Postfix 公开、入口逐重载手动打、两份文件花括号配平、失败打全量）。
* **生成出来的工程直接 `dotnet build`：0 错误**（手写 `foreach`/`if` 的花括号经真编译器验过）。
* `dotnet build Sts2CharForge.sln` 0 错误；`--uicheck` **3792 PASS / 0 FAIL**
  （两条断言改成「不再带 `[HarmonyPatch]`」+「入口逐重载手动打」）。
* 影响面：只有「用了『被给予自定义关键词』」的存档的 `GivenKeywords.cs` / `ModEntry.cs` 会变（那类本来就崩），
  其余存档除了第 3 条那几行日志外逐字不变。
* **已打包发版（与第 66 批同一版）**：`tools\release_all.ps1 -Version V0.2.180 -NoRelease -NoCommit -Yes`；
  更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.180.zip`（60.7 MB），程序部署到 `D:\ds\s\4\程序文件`（V0.2.180，旧启动器/旧包自动清掉）。
  **部署后那份 exe 的 `--uicheck` = 3792 PASS / 0 FAIL**（含本批 2 条 + 第 66 批 6 条），Core.dll 里能搜到本批新加的日志文案。

## 第 66 批需求（V0.2.180：**类名全大写 → 本地化键对不上 → 点角色抛 LocException → 选完变成别的角色**；已修，待发版）

用户实测（他自己那个 EVE 角色，英文类名 `EVE` 全大写）：选人界面点这个角色，进去却变成铁甲战士。日志给出精确位置：

```
[INFO] [StartRunLobby (1)] CHARACTER.IRONCLAD ascension set to preferred: 10
### Exception ###
MegaCrit.Sts2.Core.Localization.LocException: Key=E_VE_RELIC1.title not found in table=relics
  at LocManager.SmartFormat(…)
  at NCharacterSelectScreen.SelectCharacter(NCharacterSelectButton, CharacterModel)   ← 点角色那一下
  at NCharacterSelectButton.Select()
```

（同一条出现 6 次；它后面那 6 组 `Exception thrown while trying to log another exception` +
`NullReferenceException at Godot.NativeInterop.ExceptionUtils.LogException` 是游戏的**日志器**在处理它时自己又炸了，
不是独立问题 —— 别去查日志器。）

**真因**：`Naming.Slug()`（类名 → 本体 Entry，本地化键 / 资源文件名都靠它）以前自己写成「**每个大写字母前都插一个下划线**」，
而本体是 `StringHelper.CamelCaseRegex = ([A-Za-z0-9]|\G(?!^))([A-Z])` → `Replace("$1_$2")`：它**成对消费**，
连续大写里**每隔一个**才插：

| 类名 | 本体（= 游戏真正查的键） | 旧实现 |
|---|---|---|
| `EVERelic1` | `E_VE_RELIC1` | `E_V_E_RELIC1` ❌ |
| `ABCRelic1` | `A_BC_RELIC1` | `A_B_C_RELIC1` ❌ |
| `AIPet` | `A_I_PET` | 同 ✅ |
| `AnonCard10` / `VemiuRelic1` / `SevenCrush` / `TachiShrimpCard3` / `WhiteCloakHeaven` / `UIButton` / `HPBar` | — | 与本体**逐字相同** ✅ |

⇒ 只有「类名里出现 **3 个以上连续大写**」的存档才会坏（`EVE` / `ABC`）。他那个角色类名是 `EVE`：
角色自己的 Entry 两边都算 `E_V_E`（所以**选人页一直是正常的**），但遗物 `EVERelic1` 两边不一致
⇒ 工具把标题写成 `E_V_E_RELIC1.title`、游戏查 `E_VE_RELIC1.title`
⇒ 选人界面右侧要显示起始遗物标题时抛异常 ⇒ **选择流程中断、大厅里还是上一个角色**
= 用户看到的「选人页是对的，选完变成其他角色」。卡 / 状态类名同样是 3 连大写，键一样错，
只是**第一个被读到的是起始遗物**，所以日志里只有遗物那一条。

**修法**：`Naming.Slug()` 改成**逐字复刻本体那对正则**（`([A-Za-z0-9]|\G(?!^))([A-Z])` → `"$1_$2"`，再大写、再滤掉 `[^0-9A-Z_]`），
并把注释里那句「连续大写时每个大写前都插」的**错解释**一并改掉（就是它把人带偏的）。

**第二例（同一条 bug，症状更重：战斗卡死）** —— 另一个人的 mod（`Dddddddog` / 角色 `daoge`），卡「灵思泉涌」类名 `LingSiQuanYONG`：

```
917: [INFO] Player 1 playing card SUI_SHOU_CHUANG_ZUO (no target)                 ← 打出「随手创作」
918: [WARN] AtlasResourceLoader: Missing sprite 'daoge/ling_si_quan_yo_n_g' in card_atlas
919: [ERROR] LocException: Key=LING_SI_QUAN_YO_N_G.title not found in table=cards
985: [ERROR] GameAction PlayCardAction card: CARD.SUI_SHOU_CHUANG_ZUO completed with exception: AggregateException
1049:[ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck until the room is restarted
```

对拍（`_probe\slugcheck` 已加这三个名字）：`LingSiQuanYONG` → 本体/游戏 **`LING_SI_QUAN_YO_N_G`**（正是日志里查的键）、
旧实现 **`LING_SI_QUAN_Y_O_N_G`** ❌、修复后 `LING_SI_QUAN_YO_N_G` ✅。
同一原因还让**卡面图集条目名**也错（第 918 行那句 `Missing sprite`），所以卡面也显示不出来。
⇒ 结论：**不止角色/遗物，卡牌的类名同样中招**；「3 个以上连续大写」（`EVE` / `ABC` / `…YONG`）都会。
症状从「选完变铁甲战士」到「打出这张牌 → 战斗回合循环死掉、必须重开房间」不等。

**顺带发现（待定，未改）**：`AinodiMod` 那份日志里模组初始化直接失败，但日志只有
`[ERROR] AinodiMod - 加载失败` + 引擎栈，**没有异常类型也没有异常消息** —— 因为生成的初始化器只写
`Log.Error("<ModId> - 加载失败"); Log.Error(e.Message);`（`PatchesGen.cs:209-210`），
而 `MethodBase.Invoke` 抛出来的往往是 `TargetInvocationException`（真正的原因在 `InnerException` 里），
`e.Message` 可能只是一句「Exception has been thrown by the target of an invocation.」，盘上那份甚至没有这一行（旧版产物）。
**建议**：把 catch 改成「解包内层异常 + 打全量 `inner.ToString()`」：
```csharp
catch (Exception e) {
    var inner = (e as System.Reflection.TargetInvocationException)?.InnerException ?? e;
    Log.Error($"<ModId> - 加载失败：{inner.GetType().Name}: {inner.Message}\n{inner}");
}
```
⚠ 这会**改动所有存档生成的初始化器文本**（不是「默认档逐字不变」），所以先记在这里等确认，
不然下次再出这种问题还是查不出原因（这次就只能看到「加载失败」四个字）。

**红线实测**：
* 探针 `_probe\slugcheck`：22 个类名（含 `EVE` / `ABC` / `LingSiQuanYONG` / 带数字 / 带下划线）**工具 == 本体**全一致（修前 3 个 DIFF）。
* **只隔离这一处改动**做对拍：把 `Slug()` 临时还原成旧实现再生成一遍样例工程 → 与修复版**逐文件 112/112 逐字节一致**
  （常规驼峰命名的输出一个字节没动）。`--samplegen` 里唯一不同的那个文件（`SevenGuard.cs` 多出的 `GainsBlock`）
  是 65 批之前作者新版加的，与本改动无关。
* `dotnet build` Core / App / CLI **0 错误**；`--uicheck` **3792 PASS / 0 FAIL**（新增 6 条：4 个类名的期望 Entry +
  常规驼峰红线 + 端到端「`CharClass=EVE` 的存档，`relics.json` 里写的是 `E_VE_RELIC1.title`」）。
* 用户侧临时绕开（不用等发版）：把**角色英文类名**从 `EVE` 改成 `Eve`（不是 ModId）—— 两边算法对 `Eve`/`EveRelic1` 结果一致，
  不再抛 LocException；角色的 `CHARACTER.EVE` 不变，只有遗物 Entry 从 `E_VE_RELIC1` 变成 `EVE_RELIC1`（旧进度里那条变「未知 ID」，无害）。
* **已打包发版**：`tools\release_all.ps1 -Version V0.2.180 -NoRelease -NoCommit -Yes`（与第 67 批同一版）；
  更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.180.zip`，程序部署到 `D:\ds\s\4\程序文件`。
  部署后那份 exe 的 `--uicheck` = **3792 PASS / 0 FAIL**（本批 6 条断言全 PASS）。

## 第 65 批需求（V0.2.179：**额外资源量的「跨战斗保存」跨局 + 多人不同步** —— 已修，已发版）
> 用户原话：「额外资源，跨战斗保存，怎么会跨局保存，导致多人会不同步」。
>
> **真因：上一版把跨战斗余量放在 `private static int _carried;` 里。** static 属于**整个进程**，
> 跟「哪一局 / 哪个玩家」没关系，于是两个症状都出来了：
> * **跨局残留**：退出到主菜单 static 还在 → 新开一局（哪怕又是同一个角色）开场拿到上一局剩下的数；
> * **多人不同步**：本地多人两个玩家**共用**同一个 static；联机时房主与客机**各自跑一遍钩子**，
>   而 static 的值来自**自己这台机器上**上一次战斗结束时的余量（客机没跑过那场 → 0）→ 两边算出来不一样。
>   而 `SerializableRelic.Props` 本来就是本体联机包的一部分 —— 用 static 等于把这笔状态放在同步机制**之外**。
>
> **修法：改成本体自己的做法**（全项目唯一一份「模组状态存档 + 联机同步」扩展点，见本体
> `GalacticDust` 的 `[SavedProperty] public int StarsSpent`）：
> ```csharp
> [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
> public int CarriedAmount { get; set; }     // 替代 static int _carried
> ```
> 为什么这样就对（都核过源码）：属性 → **每个玩家一份**（起始遗物是每玩家各一份的模型实例）、
> **进存档**、**随 SerializableRelic.Props 走联机同步**（`ModelIdSerializationCache.Init` 会把
> `ModelDb.AllRelics` 里的模型——含模组遗物——上带 `[SavedProperty]` 的属性登记进同步表）、
> 新的一局重新造遗物时**天然归零**。
> ⚠ **反面教训（写进注释了）**：**跨战斗 = 存「跨战斗的东西」**（遗物上的存档属性），
> 不能存「每场战斗的东西」（`PlayerCombatState.Stars`——它是每场新造的对象，本体连存档都不存它），
> 更不能存「整个程序的东西」（`static`）。
>
> **顺手做的**：① 「额外资源量」页那个勾选框加了悬停说明 + 底下小字，写明「只在**这一局**内继承，
> 换一局归零」+ 为什么联机不会串；② `tools\使用说明.template.txt` 同一处说明；
> ③ 自检新增 2 条断言钉死形状 —— 「余量是 `[SavedProperty]` 实例属性（不是 static 字段）」+
> 「这份生成代码里**一个 static 成员都没有**」，以后再写 static 会直接 FAIL。
>
> **验证**：真生成一份「额外资源量 + 跨战斗继承」的存档 → `dotnet build` **0 错误**（属性编译通过，
> 本体那个命名空间不用加 using，全限定名可解析）；`--uicheck` **3786 PASS / 0 FAIL**；
> `--samplegen` 对 V0.2.143 基线仍只有 `.sln` + `SevenGuard.cs` 两处不同，**没有扩大**
>（示例存档没开额外资源量 → 连 ExtraResource.cs 都不生成）。
>
> 同时带上第 64 批的「触发时机加回合开始后」（两条一起发的版）。详见 `docs/RELEASE_NOTES_V0.2.179.md`。
> 已发版：`D:\ds\s\4\程序文件` = V0.2.179、更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.179.zip`（60.7 MB，397 个文件）。

## 第 64 批需求（V0.2.179：**触发时机加「回合开始后」** —— 已修，已发版；版本号落在 V0.2.179）
> 用户原话：「触发时机加一个回合开始后」。
>
> **做法**：本体 `Hook.AfterPlayerTurnStart` 一个函数里连着派发**三次**
>（`Hook.cs:894-917`：Early → 主 → Late），而它是 `CombatManager.SetupPlayerTurn` 的**最后一步**
>（回能量 → 抽牌 → 才调它，`CombatManager.cs:880-926`）。所以三个时刻的真实含义是
> 「抽牌前 / 抽牌后 / 最晚」——用户要的那一条 = 中间那个（原来那一档「玩家回合开始时」，
> id 仍是 `PlayerTurnStart`、钩子仍是 `AfterPlayerTurnStart`，旧存档零改动）。
> 新加两条 id：`PlayerTurnStartEarly` / `PlayerTurnStartLate`（**三个不同方法名** → 同一只遗物上可以各挂一块）。
>
> **三个页面一起接**：遗物页（`EffectCatalog.RelicTriggers`）、锻造节点图
>（`ForgeTimings` + `ForgeGraphCatalog`）、自定义状态页（`PowerTriggers`），id / 显示名两侧逐字对齐
>（`PlayerTurnStart` 的老中文名两边各自保留 —— 自检里列成显式例外）。
> 守卫三条一样：`if (player != base.Owner) return;`（遗物/图）/ `!= base.Owner.Player`（状态）。
>
> **顺手抓到一个真 bug 并修掉**：新 id 一开始没登记进 `CSharpCodeGen.HasContext` → 锻造图上
> 「回合开始后（抽牌前）」配**抽牌**效果时，生成的是 `await CardPileCmd.Draw(choiceContext, …)`
> 却**没有** `choiceContext` 参数（会落到「这个钩子没给 context 也造不出来 → 传 null!」那条注释上，
> 运行时会炸）。那个钩子**是带** choiceContext 的，登记进 `HasContext` 就正常了。
> **教训：加触发时机时，「它的钩子带不带 choiceContext / 要怎么造」必须和 ①②签名 一起改三处**
>（`HasContext` / `BootstrapInCombat` / `CanBootstrapContext`）。
>
> **验证**：真存档（4 只遗物 = 1 条遗物页链 + 3 只锻造各一时刻；3 只自定义状态各一时刻）
> → 真生成 + 真 `dotnet build` **0 错误**；逐条核对三个钩子名 + 守卫；
> `--uicheck` **3784 PASS / 0 FAIL**（V0.2.177 是 3778，新增 6 条，含一条
> 「自检清单和 `ForgeTimings.For("Relic")` 一一对应」的兜底 —— 以后往目录加时机忘加进自检清单会直接 FAIL）；
> `--samplegen` 对 V0.2.143 基线仍只有 `.sln` + `SevenGuard.cs` 两处不同，**没有扩大**。
> 详见 `docs/RELEASE_NOTES_V0.2.178.md`。已发版：`D:\ds\s\4\程序文件` = V0.2.178、
> 更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.178.zip`（60.7 MB，397 个文件）。

## 第 63 批需求（V0.2.177：**「升级后失去关键词」不生效** —— 已修，已发版）
> 用户原话：「升级后失去关键词不生效」+ 卡牌页关键词那两栏的截图。
>
> **真因：`upDel` 是死代码。** `CSharpCodeGen.HoverTipsOverride` 里「升级后失去」的那组提示算出来了
> （`upDel`），但生成代码时只把 `upAdd` 并进 `base.IsUpgraded` 那个条件分支，`upDel` **一个字都没用**
>（从 V0.2.136 加进去那天起就是这样 —— 查了 `源码_20261011_234717` 起的 **30 份备份快照，全都是 2 处 `upDel`**）。
> 现场表现：「自定义关键词」是显示等价物，分卡面那行字和 `ExtraHoverTips` 两块 ——
> **升级后卡面那行字确实消失了**（描述那半截的 `{IfUpgraded:show:…|…}` 一直是对的），
> 但**鼠标一悬停说明还在**，所以看起来就是「失去没生效」。附带：失去的那个词在升级后那段里**被列了两遍**
>（一次 `upDel` 那组、一次「一直在」那组）。
>
> **修法**（两半一起对齐，都在同一个「按 `base.IsUpgraded` 分两组」的写法里）：
> * 升级后那段 = `tips` 里**剔掉** `upDel` 的那些，再并上 `upAdd`；
> * 没升级那段 = `tips` 并上 `upDel`。
>   第②条是顺手补齐的：以前「失去」的那个词**连没升级时都没有悬停说明**（`upDel` 谁都没用），
>   等于这两栏的「失去」方向整个不起作用。现在「没升级时有、升级后没有」才是完整的「失去」。
> * 判据用**提示的完整 C# 表达式逐字相等**（`new LocString("card_keywords", "<键>.title")`），
>   和描述那半截的去重口径一致，不会漂移。
> * **顺手对齐一处自相矛盾**：同一个词既勾在「一直在」又填进「升级后失去」时，描述以「失去」为准、
>   悬停说明以前以「一直在」为准 → 现在 `KeywordGen.CardTextBoth` 也统一以「失去」为准，
>   校验器加一条**提示**（不是警告、不拦生成）把实际行为说清楚。
>
> **验证**：探针真生成 + 真 `dotnet build` **0 错误**（含边界档「升级后那段一条说明都不剩 → `Array.Empty<…>`」）；
> `--uicheck` **3778 PASS / 0 FAIL**（V0.2.176 是 3774，新增 4 条）。自检夹具也一起改了 ——
> 以前 VOID 只写在「失去」栏，**升级后要不要丢掉它根本没有断言盯着**，这正是它能漏到现在的原因。
> `--samplegen` 对 V0.2.143 基线：113 个文件里仍只有 `.sln` + `SevenGuard.cs` 两处不同（V0.2.170 起的既知差异），**没有扩大**。
> 详见 `docs/RELEASE_NOTES_V0.2.177.md`。已发版：`D:\ds\s\4\程序文件` = V0.2.177、
> 更新包 `D:\ds\s\Sts2CharForge_更新包_V0.2.177.zip`（60.7 MB，397 个文件）。

## 第 62 批需求（V0.2.176：**节点图效果种类 41 → 62 种**（用户「全做」）—— 已修，未发版）
> 用户原话：「锻造的效果种类支持以下吗」+ 一张 30 多条的清单 + 追加「全做」。
> 我先把「种类存在」和「节点图能生成」分开实测了一遍（逐个种类造成一个节点真生成），
> 得出缺口 **26 种**，然后分四批做完 **23 种**（节点图 41 → 64 种）：
>
> * **批次 A（6）**：放置卡牌 / 给予卡牌关键词 / 刮削 / 填满手牌 / 禁止回复生命 / 暴露
> * **批次 B（4）**：额外获得一回合 / 抽到就减费 / 增加额外资源量上限值 / 透支能量
> * **批次 C（12，用户点的最高优先级）**：召唤伙伴 + 伙伴攻击 ×4 + 治疗 / 失去生命 / 最大生命 /
>   牺牲 / 给伙伴施加状态 / 替主人承伤开·关
>   · 图上形状：按伙伴类名查局部变量 + `if (… is not null) { … }` 包住（**不能**写
>     `if (… is null) return;` —— 输出是内联在打出方法里的，那个 return 会把后面效果整段吞掉）；
>   · 伤害走 `.FromPetAttacker(变量)`，按生命值算的三种在图上**内联**算（图上没有 CalculatedDamage）；
>   · 「伙伴攻击」的目标 = 图上「指向对象」解析出的那一个。
> * **批次 D（2）**：大限已至（伤害 = 目标灾厄层数 × 数值，图上内联）；转化为先古卡（改生成方法的头尾做预览缓冲）
>
> **顺手修掉的结构性坑**（比补种类更要紧）：
> ① `CSharpCodeGen.AllEffects`（所有「用到才生成」的判据）**以前一条节点图效果都不收** →
>    图上用到辅助类就 CS0246；V0.2.168 是给 BoostCard 单独打补丁（逐个补，加种类就漏）。
>    改成一处收全（`ForgeEffectBridge.GraphsOf` 四类布全收）。
> ② `PetGen.UsesAttackExtension` 只看卡牌 / 状态列表 → 图上伙伴攻击让 `PetAttackExtensions.cs`
>    不生成（CS1061）；改成走统一 `UsesKind`。
> ③ 「资源上限」Power 只在「开了额外资源量 + 填了上限」时生成 → 只在图上用这一条时 CS0246；单独判。
>
> **剩下 3 种没做**（不是漏，已分类说清 + 校验器拦住）：
> 「本回合这张牌自己费用变 0」「抽到这张牌时费用 -N」= **卡牌自己的行为**（`SetToFreeThisTurn` /
> 卡自己的 `AfterCardDrawn` 钩子），节点图的【效果】模块没有「牌自己」可挂；
> 「转化为先古遗物」= 要选「换成哪只遗物」，而节点的效果模块按卡牌页字段存（没有遗物下拉）→ 选不到正确的值（CS0311）；它仍在「遗物 / 药水」的效果列表里可用；
> 「生成卡牌」那种其实**早就支持**（探针误报）。
> 新增 `ForgeGraphGen.CardOnlyEffectKinds` 让生成代码与校验器**分类说清**（卡牌专属 vs 还没接）。
>
> **验证**：全部 64 种各做一张卡 → 真生成 + **真编译 0 错误**（抓出 2 处 CS0246 + 6 处 CS1513，
> 都已修）；伙伴组单独再验 12 张 → 0 错误；`--uicheck` 3774 PASS / 0 FAIL。
> 详见 `docs/RELEASE_NOTES_V0.2.176.md`。
>
> ⚠ **事故记录**（本批发生）：我用 PowerShell 的 `(Get-Content -Raw).Replace(...) | Set-Content -Encoding UTF8`
> 批量改 `CSharpCodeGen.cs`，PowerShell 5.1 按本机 ANSI 重写 → **整个文件中文乱码、引号被打断**
> （12,325 → 12,450 行）。处置：从 `_备份\源码\源码_20261014_190310` 恢复 + **逐条重放**本次会话在该文件的
> 全部改动（计数族修复 / AllEffects / 6 个发射器参数化 / 宠物发射器 ×8），并用 `--uicheck` 3774 PASS 对齐验证。
> **教训：代码文件只准用 edit / write 工具改，不准用 shell 做文本替换。**

## 第 61 批需求（V0.2.175：**节点图上「格挡填负数」失去不了格挡** —— 已修，未发版）
> 用户原话：「格挡怎么不能失去」→ 追问确认：**锻造节点图**上【效果】=格挡、**数值填负数**、目标=**自己**。
>
> **真因**：卡牌页那条链一直是对的（`CSharpCodeGen` 的 `case "Block"`：`IsNegativeAmount` → `CreatureCmd.LoseBlock`），
> 但**节点图**那条链（`ForgeGraphGen.EmitEffect` 的 `"Block"` 分支）**没有负数处理**，只写
> `CreatureCmd.GainBlock(__fgFoe, (-5m), …)` —— 而本体 `GainBlock` 对**非正数直接返回**（不报错、不扣），
> 所以图上填负数「格挡一点没少、也没有任何提示」。
>
> **修法**：节点图加一条 `"Block" when !amountN.Runtime && amountN.Value < 0m` → 生成
> `CreatureCmd.LoseBlock(choiceContext, __fgFoe, Nm, __fgSelf)`（口径和卡牌页 `IsNegativeAmount` 一致：
> 常数负数才走；运行时公式算出来的正负生成期不知道，照旧 GainBlock；「下回合生效」+ 负数按立即生效，
> 本体没有「下回合失去格挡」的落点）。正数 / 下回合正数**一个字符都没变**。自检新增一条钉死。
> 探针实测：卡牌页 −5 / 节点图 −5 都生成 LoseBlock，+5 仍 GainBlock。`--uicheck` 3774 PASS / 0 FAIL。
> 详见 `docs/RELEASE_NOTES_V0.2.175.md`。
>
> **还没做的**：同一份存档里**别的「图上负数」效果**没一起查（用户只报了格挡）——
> 「获得能量」那条图上有专门处理（`EnergyNextTurnDebtPower`），但**「回复生命 / 最大生命」等填负数**
> 还没逐个确认，等用户点名再按同一口径过一遍。

## 第 60 批需求（V0.2.174：**锻造卡「变化其他牌」打出后卡住** —— 已发版）
> 用户原话：「变化其他牌的卡会一直挂在这，什么效果都不会触发」（游戏日志：
> `System.InvalidOperationException: No selection screen prompt for CARD.BUGKILLER_CARD35.`）。
> **V0.2.173 没修好** —— 用户发来 `BugkillerMod.json` 才把真因钉死。
>
> **真因**：那张卡（「你去！」`BugkillerCard35`）的节点图上只有一条效果
> `Effect=TransformCard, Pick=Chosen, SpawnCardId=BugkillerCard20, SpawnTo=Draw`。生成出来的
> `ForgeGraphOnPlay` 里有
> `CardSelectCmd.FromCombatPile(… new CardSelectorPrefs(base.SelectionScreenPrompt, …))` —— **会读提示语**；
> 而决定「写不写那条本地化键」的 `LocalizationGen.NeedsSelectPrompt` 那张**手写名单**里，
> 四档那条要求 `e.IsSpawnRandom && (SpawnChoice > 1 || SpawnChosen)`，而这一档 **`IsSpawnRandom` 是 false**
> （变成哪张卡是指定的，只有「挑哪张来变」由玩家选）→ 判据说「不用写键」→ 键没写 → 本体抛异常 →
> **异常抛在效果链中间 ⇒ 这张牌后面的效果一条都不跑**（「一直挂在那儿」）。
> ⚠ 节点图发射器 `ForgeGraphGen.EmitEffect` 判的是**图上另一个字段**（`node.Get("Pick") != "Random"`），
> 两张名单不同步 —— 同一类坑**第三次**（V0.2.145 自定义状态 / V0.2.170 四档 / 这次图上 Pick）。
>
> **修法**：①`NeedsSelectPrompt` 补 `(e.Kind is "TransformCard" or "CopyCard" && e.CardPick == "Chosen")`
> ②`SelectPromptText` 加「选择要变化的牌」③自检钉两条（生成代码出现 `base.SelectionScreenPrompt` 就必须有键 /
> 判据必须认这一档）。
>
> **验证**（用用户那份存档真跑）：54 张卡扫一遍 —— 会读提示语的 3 张（卡片 13/23/35）**缺键 0**；
> 卡片 35 现在有 `"BUGKILLER_CARD35.selectionScreenPrompt": "选择要变化的牌"`；
> 真生成 + 真编译 **0 错误**；生成工程全量校验（读提示语的源码文件 3 个，缺键 0）；
> `--uicheck` 3773 PASS / 0 FAIL。详见 `docs/RELEASE_NOTES_V0.2.174.md`。
>
> **还没做的**：这张「两张手写名单必须同步」的结构性隐患还在（我建议过改成「生成源码里读到提示语就补键」的保底，
> 用户还没定）；`Keyword:` / `Group:` 牌型筛选仍未拼进「每累计 N 次」的计数表达式。

## 第 59 批需求（V0.2.173：**「每打出 N 张技能/攻击牌」永远不触发** ＋ **「多人催促文本」绑错了数据** —— 已发版）
> 用户原话 ①：「每打出 3 张技能 3 张攻击获得增益，但实际没生效」（附遗物页截图：两条「打出一张牌时」，
> 其中一条 = 技能牌 + 每累计 3 次 + 效果【增益/减益：BugkillerPower 1 → 自己 / ThornsPower 1 → 自己】）。
> 用户原话 ②（澄清）：「**多人催促文本不是宝箱房的，是多人联机时右下角的 ping**」。
>
> **① 的真因**（不是用户配置的问题，生成出来的判据本身错了）：本体 `CardModel.OnPlayWrapper` 的顺序是
> `Hook.BeforeCardPlayed` → `History.CardPlayStarted` → `OnPlay` → `History.CardPlayFinished`
> → `Hook.AfterCardPlayed(Late)` —— 而「打出一张牌时」挂的是 **BeforeCardPlayed**，那一刻这一张牌
> **还没进 `CardPlaysFinished`**，所以 `CardPlaysFinished.Count % N != 0` 永远差一张：
> 打第 3 张时读到 2、第 4 张读到 3（而第 4 张往往过不了牌型守卫）→ **填 ≥ 2 之后一次都不触发**
> （填 1 时 `0 % 1 == 0` 恒成立，所以一直没暴露）。另外牌型筛选只写在守卫里（只筛当前这一张），
> 计数器仍在数**所有**打出的牌 →「每 3 张技能牌」实际是「第 3 张牌且它是技能牌时」。
>
> 修法：①「打出一张牌时」改用 `CardPlaysStarted`（含正在打出的这一张，和内置读数 `PlayedThisTurn` 同口径）
> ②「只对哪类牌生效」拼进计数表达式 ③`CountExpr` 补 `CardResolved`（「一张牌结算完毕时」填了次数
> **一个字符都不生成**，静默失效）④状态侧 `__cnt > 基准`（换成 Started 后施加状态的那一张牌会被算进基准
> → 一上状态就当场响一次）⑤`ShowCardFilterField` 也认 `CardResolved`（生成端一直写那句守卫，
> 界面却不显示这一栏）⑥`--recover` 的「至少打出 N 张牌」正则两个写法都认（不然老工程回读静默丢条件）。
>
> **② 的真因**：V0.2.172 加的那一栏**绑错了属性**（绑到了 `Profile.Art.TreasureFightText` = 宝箱房
> `gameplay_ui.TREASURE_FIGHT_TEXT`「开抢！！！」）。真正的「催促」是本体 `NPingButton` →
> `FlavorSynchronizer.SendEndTurnPing()` → `EndTurnPingMessage` → 在每个玩家头上冒 1.5 秒对话气泡，
> 文案取 `characters` 表的 `<角色>.banter.alive.endTurnPing`（阵亡取 `.dead.`）。
> 修法：新增 `Profile.EndTurnPingText` + 角色页那一栏改绑它（标题/说明都改成「Ping 按钮那句」）+
> `characters.json` 那一行不再写死（留空仍写「……继续。」→ 老存档 / `--samplegen` 逐字不变）+
> 宝箱房那句照旧留在美术页（两栏从此是两份数据）+ `--recover` 把默认值当作「没填」。
> 这一项**不需要补丁**，存档格式不变（老存档没有这个字段 → 空 → 默认那句）。
>
> 详细见 `docs/RELEASE_NOTES_V0.2.173.md`；`--uicheck` 3771 PASS / 0 FAIL；生成→回读往返一致。

**还没做的**（下一批候选）：--recover 对「状态链里从某一摞拿牌到手牌」的还原、「升级后变 X 费」实验档、
自定义状态自动图标兜底；节点图「将此牌放回手牌」的时机限制、「斩杀」只在卡牌档（校验器都会说清）；
`Keyword:` / `Group:` 牌型筛选**还没**拼进「每累计 N 次」的计数表达式（老四档攻击/技能/能力/诅咒/状态
已经拼了；自定义关键词要靠角色配置展开，`CountExpr` 拿不到 profile —— 现在的行为 = 与 V0.2.172 相同）。

## 第 58 批需求（V0.2.172：**角色页底部加多人催促文本** ＋ **角色最大能量** —— **已发版**）
> 用户原话：「在角色页底下增加多人催促文本，增加角色最大能量」。
> ①催促文本本来就在（美术页槽位列表下面），这次**在角色页底部再摆一个框**，绑**同一个**
> Profile.Art.TreasureFightText（两边同步、存档不变、美术页那个不删）。
> ②新增 Profile.MaxEnergy（默认 3 = 本体默认）：本体 CharacterModel.MaxEnergy 是虚属性，
> 生成的角色类里一个 public override int MaxEnergy => N; 就够，**不需要补丁**；
> ⚠ 等于 3 时**一个字都不写**（老存档 / --samplegen 基线逐字不变）；README 摘要里「能量 N」也只在不是 3 时印。

**还没做的**（下一批候选）：--recover 对「状态链里从某一摞拿牌到手牌」的还原、「升级后变 X 费」实验档、
自定义状态自动图标兜底；节点图「将此牌放回手牌」的时机限制、「斩杀」只在卡牌档（校验器都会说清）。

## 第 57 批需求（V0.2.171：**"我并没有填 Y 怎么还这样"** ＋ 「播放音频文件」CS1513 —— **已发版**）
> 用户截图报「生成被拦住了」：卡牌「新卡」第 1 条「播放音频文件」被校验器拦住，理由写着填了
> 「命中次数 / 对群数」的公式（Y），可他说**没填过**。查存档证实：RepeatFormula='Y' 是**换效果种类
> 之前**的残留，而那一栏按种类已收起 —— 用户看不见、也清不掉，却被它挡住生成。
> 改法：①有残留就把那一行**露出来**（可清）②换种类**自动清**用不到的它 ③这条降成**警告**，不挡生成。
>
> 上面这条不再拦之后，立刻露出**真的编译不过**：SparkleCard43.cs CS1513 应输入 } ——
> EmitPlaySoundFile 里 w.Open("{") 生成成 { {（CodeWriter.Open 自己还会补 " {"）= 开两个块只关一个。
> 从 V0.2.155 就存在、一直被别的报错挡着。已修（w.Line(注释) + w.Open("")），用户存档 --build OK。

**还没做的**（下一批候选）：--recover 对「状态链里从某一摞拿牌到手牌」的还原、「升级后变 X 费」实验档、
自定义状态自动图标兜底；节点图「将此牌放回手牌」的时机限制、「斩杀」只在卡牌档（校验器都会说清）。

## 第 56 批需求（V0.2.169：**版本号终于往前挪了一格** ＋ 节点图补 4 类栏位 / 7 档效果 —— **已发版**）
> **V0.2.170 已发版**：本批里「没做完的那 4 条栏位级缺口」在 V0.2.170 全部做完（见下面那一节）。
> 用户原话：「把这些也做了，**并且版本号怎么不加了**」。
> 坦白：V0.2.168 一个号被反复覆盖了七八次，每次都改了东西却还叫 V0.2.168 —— 是我偷懒，
> 以后**每批必须换号**。这一版号叫 V0.2.169，第 55 批（卡牌链 CS0111 + 斩杀 CS0103）的修复
> 也在同一棵树里，所以**这一版已经把它一起带上**了，不需要再单独发一次 169。

**这一版实际装进包里的**（详细逐条见 `docs/RELEASE_NOTES_V0.2.169.md`）：
* 第 55 批：锻图【触发时机】和卡牌自己的钩子撞方法名 CS0111；斩杀条件挂钩子上 CS0103；
* 状态页盯敌人不触发 / 卡牌档缺归属守卫 / 调试日志编译不过 / 次数填 X Y 只生效一次 /
  描述栏回车报错 / 每累计 N 次算到上状态之前 / 透镜不认格挡牌 / 无限升级不加数值 /
  遗物两条时机「全部满足」要触发两次；
* 新增：状态里能召唤伙伴；节点图「从哪里选牌」四摞；节点图补 7 档卡牌页效果
  （删除卡牌（全局）/ 加牌入组 / 变化卡牌（全局）/ 升级卡牌（全局）/ 毒性爆发 / 播放音效 /
  播放上传音频）；节点图能用「强化指定卡牌」。

**V0.2.170 把上面这 4 条栏位级缺口全做完了**（详细见 `docs/RELEASE_NOTES_V0.2.170.md`）：
* ✅「取卡方式」四档：按范围随机 / 范围限定 / 多选 1 / 从范围里自己挑一张 —— **生成卡牌 / 变化卡牌 /
  加牌进牌组（全局）/ 变化卡牌（全局）** 四种都能用（复用卡牌页那几份发射器，接上节点图的
  `__fgPlayer` / `__fgSelf` / `ctx.ChoiceVar` 上下文表达式）；
* ✅「取几张」四档（恰好 / 最多 N / 任意张 / 全部）—— 上一批写的，本版一起发；
* ✅「下回合生效」铺到**全部做得了的种类**（除本体没有延迟落点的 8 种：播放音效 / 结束回合 /
  自己变便宜之类）—— 节点图现在和卡牌页共用同一套延迟状态（生成卡牌 / 施加状态 / 造成伤害 /
  重放 / 回合结束自动打出…都能下回合生效）；
* ✅ 要「打出这张牌」那一刻上下文的：**重放卡牌 / 回合结束时自动打出 / 将此牌放回手牌 / 斩杀条件**。
  · 「将此牌放回手牌」在节点图里是**内联**的 → 只有挂在「这张牌被消耗时 / 被丢弃时」这类
    「牌已经不在手牌里」的时机上才生效（挂错了校验器会警告说清，不静默）；
  · 「斩杀」条件只有**卡牌**档 + 拿得到 `cardPlay` 的时机才成立（遗物 / 药水档报错拦住；
    挂在拿不到 cardPlay 的卡牌时机上给警告）。
* 这一批真编译抓出来并修掉的 4 个坑（都写进发版说明了）：
  ① 两个同类效果在同一方法里重复声明固定变量名（`__transformPool`）→ CS0128 ⇒ 每段效果包一层块作用域；
  ② 延迟状态的 Apply 实参传了 Player（要 Creature）→ CS1503；
  ③ 节点图「获得能量 + 下回合生效」去引用一个根本不会生成的延迟类 → CS0246 ⇒ 走本体现成的
     `EnergyNextTurnPower` / 星币同理；
  ④ 节点图里的「回合结束时自动打出」漏在「用到才生成」名单外 → `TurnEndPlayPower` CS0246。

**仍然没做的（下一批候选）**：`--recover` 对「状态链里从某一摞拿牌到手牌」的还原；
「升级后变 X 费」这种实验性档；自定义状态的自动图标兜底。

## 第 55 批需求（V0.2.169：**卡牌链 CS0111**（锻造图的【触发时机】和「这张牌自己的钩子」撞同一个方法名）
＋ **「斩杀」条件挂在钩子效果上会 CS0103（`__lastAttack` / `__fatalOk` 引用不到）** —— 已修，待发版）
> 版本标签 V0.2.169：V0.2.168 是第 54 批（多块【触发时机】+ 状态页盯敌人不生效 + 卡牌档归属守卫）已经发掉了。

**现象**：用户那张 `AinardiCard8` 反复重建十几次都卡在同一条构建错误上 —— 一个类里**两个签名一样的
`AfterCardExhausted` override** ⇒ `CS0111`，整个模组编译不过。他确认了原因：那张牌上**同时**有
①锻造图上的一块【触发时机 =「这张牌被消耗时」】和 ②一条普通效果的「卡牌时机 = 被消耗」。

**真因**：遗物那条链早就用「把图**嵌进**遗物自己的触发钩子」绕开了这个坑
（`ForgeGraphGen.RelicGraphNestsInTrigger` / `RelicGraphNestedMethod`），**卡牌这条链一直没做** ——
`CardHooks` 只把「打出时」那一支改成私有方法（`ForgeGraphOnPlay`），别的时机照样自己
`public override async Task …`；而「卡牌时机」那几条效果也会生成同一个 override
（`CSharpCodeGen.EmitCardTimingHooks`）→ 两边各写一个。

**修法（照遗物那套，一个字符都不多写）**：
1. `ForgeGraphGen`：新增 `NestedCardHooks(card)` / `CardGraphNestsInHook(card, hook)` / `GraphHasHook` /
   `CardNestedMethod(hook, upgraded)`；`CardHooks` 对**撞上的**那一块改成生成私有方法
   `ForgeGraphOn<钩子名>[Upgraded](PlayerChoiceContext choiceContext)`。判据里的「这张牌自己有哪些钩子」
   **从生成端那一份推**（`CSharpCodeGen.CardTimingMethodName` + 新增的 `HasSelfDrawHook`），不另抄一张表。
   嵌进去的方法拿不到目标钩子那套参数（`card` / `player` / `retainedCards`…）→ `EmitCtx` 新增
   `NoHookParams`：进门守卫交给外层钩子、`cardPlay` 走既有的 `CardPlay? cardPlay = null;` 占位。
2. `CSharpCodeGen`：`EmitCardTimingHooks` 收 `CardSpec`，被消耗 / 被丢弃 / 被保留三档在效果体之后调用
   `EmitNestedForgeGraphCall`；`EmitSelfDrawHook`（卡自带 `AfterCardDrawn`）同理，只有真撞上时才把
   `public override Task` 改成 `async Task`。两块布都撞上同一个钩子时按 `base.IsUpgraded` 二选一
   （`…Upgraded` 后缀，和 `ForgeGraphOnPlay` 同一个规矩）。
3. 那句调用是胶水代码 → 行尾写 `CET:ForgeNested`，`ProjectRecovery` 整行跳过（图本身是从
   `// CET:ForgeGraph=<Base64>` 整份恢复的）。**顺手修**：`CardTimingMethodName` 以前漏了 `HpLost`
   → 会走兜底变成 `AfterCardExhausted`，和 `EmitCardTimingHooks` 里那个 switch 口径不一致，已补成
   `AfterCurrentHpChanged`。

**红线实测**：
* 探针 `_probe\cardnest`（新）：27 条断言全 PASS；**修前**同一个探针 12 条 FAIL（`实际 2` 个
  `AfterCardExhausted`）。四种撞法（被消耗 / 被丢弃 / 被保留 / 被抽到）+ 两块布都撞 + 只有图 + 打出时各一组。
* **逐字节红线**：`out` 与修前逐文件对拍（忽略随机节点 GUID）—— `00_sample_all_cards` / `01_graph_only_exhausted` /
  `07_play_timing` **SAME**，只有那 5 个撞车用例 DIFF（本来就是编译不过的档，改动是修它）。
* `dotnet build` Core / App / CLI **0 错误**；`--uicheck` **3720 PASS / 0 FAIL**（新增 14 条：三张撞车卡各
  「同名 override 只有一个」+ 回读 `HasUnparsed == false` + 卡牌时机条件认得回来 + 「失去生命时」回读
  + 斩杀那三条）。探针：`_probe\cardnest` 27 条、`_probe\fatalcheck` 14 条，全 PASS。
* 自检里也钉了「回读」那一条：撞车卡生成完 → `ProjectRecovery.FromProject` → 没认出来的语句 0 条。
* **顺手修掉一条假红**：`--uicheck` 里 V0.2.166 那条「盯『任意敌人』+ 盯的就是这个状态自己 → **警告**说清
  自己涨到 N 不会触发」在 V0.2.168 把行为改成「按这份状态自己算」（校验器那条也从**警告**改成**提示**）之后
  就永远 FAIL 了（老文案的断言没跟着改；V0.2.168 自己那 6 条断言在 26118~26151 行、是 PASS 的）。
  现在这条断言按新口径写：`i.Level == "提示" && i.Message.Contains("这份状态自己")`。

**同族，这一批里也一起修了**：
* `AinardiCard13.cs(28,13) CS0103 __lastAttack / __fatalOk`（**已修**）：那两个变量只在卡牌 OnPlay 开头声明
  （`CSharpCodeGen.cs:4341-4343`），而「斩杀」那个条件的表达式在 `CSharpCodeGen.cs:1123-1124`。
  新探针 `_probe\fatalcheck` 把**真正会炸的两条路**钉出来了 —— 有两条效果**不在 OnPlay 里发射**、各自
  写进别的钩子：**「抽到这张牌时费用 -N」→ `AfterCardDrawn`**、**「将此牌放回手牌」→ `AfterCardPlayedLate`**；
  那两处既没有声明、也没有「上一条攻击」（`captureFatal: false`）⇒ 带「斩杀」条件时生成出来就是
  `if (__lastAttack is not null && __fatalOk && …)` 引用两个不存在的名字。
  ⚠ 关键点：**前面先有一条「造成伤害」**时，既有的「斩杀前面没伤害」那条校验**不会**报 —— 正好漏过去；
  探针里专挑 `伤害 + 斩杀挂在钩子效果上` 这个形状复现（B2 / C2）。
  修法：校验器新增 `ProfileValidator.ValidateFatalPlacement` —— 报**错误**并点明「实现在 AfterCardDrawn /
  AfterCardPlayedLate 钩子里、那里没有上一条攻击」，让他把「斩杀」挪到一条打出时的造成伤害后面。
  顺带实测**排除**了另外两条猜想：诅咒的 `OnTurnEndInHand`、以及「斩杀 + 下回合生效」**都不会**引用那两个
  变量（不是 CS0103，探针里已注明，别再按猜想改）。
* `AinardiCard4.cs(39,2) CS1513 应输入 }`（**未修**）：一次性的花括号不平（多半是手改生成物）。生成端这边
  `_probe\cardnest` 已加了「花括号配平 + 同类里同名 override 不重复」两条通用断言，可以当体检用。

## 第 54 批需求（V0.2.168：**一块布上多块【触发时机】** + 「某个状态被移除时」盯敌人不生效 + 卡牌档时机缺归属守卫 + 开着调试日志锻造图编译不过 + **「次数填 X/Y 只生效一次」** + **「描述栏打回车换行会报错」** + **「状态把上状态之前的计数也算进去」** + **「无限升级不加数值」** + **「状态里放召唤伙伴被拦住」** + **「遗物全部满足要触发两次」** + **「节点图『从哪里选牌』被拦住」** + **「节点图补 7 档效果」** + **「节点图强化指定卡牌」**；**已本地发版**）

用户两句话，各是一件事（后来又追加了第三件）：

* 「多个触发时机能用了吗」→「要，现在就加」：**锻造节点图支持一块布上多块【触发时机】**，每块配自己右后那一条链。
* 「某个状态被移除时，好像无法正常生效」（追问后确认：**状态页**上配的、盯**敌人**身上的状态）：
  生成端的守卫写着 `power.Owner != base.Owner`，等于要求「发生事的那份状态在**别的**生物身上」——
  而「盯哪个状态」填的正是**这只状态自己**（击晕值就贴在敌人身上）时，用户要的是
  「这份状态挂在谁身上，它自己那份出事就在谁那里触发」，被这条挡得死死的：生成、编译、进游戏全不报错，就是什么都不做。
* 「次数填写 Y 或者 X 次还是只生效一次」（他让我看游戏日志 → `godot.log` 里
  `卡牌「新卡」(SparkleCard43) → 造成伤害`，那张牌是「额外资源量费用 = X」）：
  本体**先扣费再跑效果** ⇒ 效果跑的那一刻「当前额外资源量」= **0** ⇒ `Math.Clamp(0,1,20)` 夹成 **1 下**。
  修法：X 费 / Y 费的牌里 `X` / `Y` **直接按「这张牌花掉的那部分」生成**
  （`ResolveEnergyXValue()` / `ResolveStarXValue()`，和「= X」那一条效果同一个值）；
  普通牌里 X / Y 照旧是「当前值」（不回归）。校验器那条「Y 读到 0」的警告改成提示。
* 「自定义状态的描述栏中 如果用回车换行会有报错」：WPF 输入框打回车存的是 **CRLF**，
  生成端把描述塞进 `/// <summary>…</summary>` 时只把 `\n` 换成空格、**留下了 `\r`** ——
  C# 把单独 `\r` 也当换行 ⇒ 注释被截断、后半截变成代码 ⇒ **整个工程编译不过**。
  修法：新增 `CustomPowerGen.OneLine`（CRLF/CR/LF → 空格）用在文档注释处；
  `CodeWriter.Line` 兜底（CRLF→LF、单独 `\r`→空格）、`CodeWriter.Str` 把单独 `\r` 也转义成 `\n`。
  多行描述本身照旧支持（本地化表是 JSON，换行正确转义）。新增探针 `--descprobe` 钉住。
* 「状态会直接统计整个回合（战斗）的计数，然后哪怕那时候还没上状态，也会计数……要打 4 张牌触发，但在上状态之前就打了 3 张，上状态之后只需要打 1 张就行了」：
  真因 = 状态页「每累计 N 次触发一次」用的是本体战斗历史里**整个回合 / 整场战斗**的累计数（直接判 `累计 % N == 0`），
  把上状态之前的事也数进去了。修法：每个状态**实例**自己记一个基准值（`__cnt<时机><范围> = -1` = 还没记过），
  只数基准之后的事件；「本回合」范围靠「换回合后累计值变小」自动重新记基准，不需要额外重置钩子。
  遗物那条链没动（要改成「从拿到遗物那一刻起数」再说）。

### 改了什么

1. **多块【触发时机】**（`ForgeGraphSpec.TriggerNodes` / `ForgeGraphGen.Plans`）：
   * 数据：`TriggerNode`（第一块，老代码 / 老图语义不变）之外新增 `TriggerNodes`（全部，按模块顺序）；
   * 生成：卡牌（`CardHooks`）、药水（`PotionHooks`）、遗物（`RelicHooks`）都按**每一块**各生成一个钩子 ——
     「卡牌被打出时」那一块照旧走私有方法 `ForgeGraphOnPlay`（由 OnPlay 末尾调用），别的时机各写自己的 override；
     遗物档嵌进遗物自己钩子的那几块按顺序编号 `ForgeGraphOnTrigger` / `ForgeGraphOnTrigger2`（都叫同名会 CS0111）；
   * **同一个本体钩子只写第一块**（`SplitByHook`）：两块都落在 `OnPlay` / `AfterCardExhausted` 上时，
     生成端只写第一块 + 一段注释；校验器（`ForgeGraphGen.Issues`）对这种配置**报错**说清怎么办
     （否则是同一个类里两个同名 override，整个模组编译不过）；
   * 别的配套：`TimingFieldsCore` 每块各一个「生效次数」计数器（`__fgUses` / `__fgUsesP2`…，
     复位钩子按「本回合 / 本场战斗」**合起来写一个**）、`LocalizationGen.GraphCardText` 每块各写一段卡面文字、
     `RelicsWithPowerRemoved` 扫**所有**触发模块、`HasPlayTimingIn` / `HasUseTimingIn` 扫全部；
   * 界面：模块下拉里**不再**加过一块就把【触发时机】删掉；**第一块**仍是固定模块（删不掉、不被全选带走），
     第二块起是普通模块；新加的那块默认挑「这块布上还没用过」的时机（不会一加就撞车）。
2. **「某个状态被移除时 / 层数变化后 / 到达 N 层时」的「盯谁」守卫抽成一份 `CustomPowerGen.PowerWatchGuard`**：
   四个调用点（合并钩子的变化 / 到达、单条变化、单条到达、单条移除）共用；
   **盯的就是它自己这个状态**（「盯哪个状态」= 这只状态自己的类名）时，不管「盯谁」选自己还是任意敌人，
   一律按「**这份状态自己**」生成（`power.Owner == base.Owner`）；盯**别的**状态时行为一个字节都不变。
   校验器那条「自己涨到 N 那一下不会触发」的**警告**随之改成**提示**（说清按「这份状态自己」生成）。
3. **卡牌档那几条「本体广播给全场」的时机的归属守卫**（顺带修的老坑）：
   锻造图上「这张牌被消耗时 / 被丢弃时 / 被抽到时」以前**没有** `card != this` 守卫 ——
   任何一张牌被消耗都会跑这张图的链；现在补上（判据和卡牌页「卡牌时机」那一份逐字相同），
   「被保留时」也补上本体那两句（`player != base.Owner` + `retainedCards.Contains(this)`）。
4. **开着「生成调试日志」时锻造图那张卡编译不过**（用户第二次实测报的：
   `SparkleCard41.cs(45,10) CS8641` + `SparkleCard42.cs(54,6) CS1513`，整个工程编译不过）：
   真因 = 日志那一行以行尾注释 `// CET:DebugLog` 收尾，而锻造图那几档生成的是
   「`foreach (…) { <日志行><语句> }`」**拼在一行**的形状 —— 那行注释把**后面整条语句 + `foreach` 收尾的 `}`**
   一起吃成注释 ⇒ 花括号不平、`else` 掉到语句开头。
   修法：`DebugLogGen.TargetInline` 返回的文本**自带换行**（日志自己占一行）、`PowerAfter` 也补换行；
   顺带把日志里的目标名从「转义后的字面量代码」改成**运行时求值**（以前日志里打出来是
   `目标=[' + ((x)?.Name ?? \"?\") + ']`，看不到真正打到谁）。
   新增探针 `--dbgprobe`（调试日志开着的锻造图卡：造成伤害 / 获得格挡 / 失去生命 / 施加状态 / 抽牌）钉住这条。

* 「能不能加一个格挡的本体卡标签，不然透镜触发不了」：先查本体怎么筛的 ——
  `Nimble.CanEnchant(card) = base.CanEnchant(card) && card.GainsBlock`（透镜 FresnelLens 给牌附魔 Nimble，
  本体注释里专门写了 **Don't convert this into a CardTag**：带 `CardTag.Defend` 的只有初始那几张防御）。
  修法：生成的卡**按效果自动**声明 `public override bool GainsBlock => true;`（判据 = 打出时立刻获得格挡：
  正数格挡 ✅；负数 / 下回合生效 / 只挂在「卡牌时机」钩子上的 ❌；锻造图上的【效果】也算 ✅；
  老行为「勾了 Defend 标签」照旧 ✅）。顺带这张牌会自动带上「格挡」的悬停提示。
  新增探针 `--gainprobe` 钉住这四档。**红线变化**：`--samplegen` 里 `cs/Cards/SevenGuard.cs` 多了一行
  `GainsBlock => true`（其余 111 个文件逐字节相同）—— 这是这次故意要改的东西。

* 「无限升级怎么升级后不加数值了」：打了「数值公式」的牌打出时用的是公式算出来的值，而升级增量并进表达式的写法是
  「升没升过」两档 `(base.IsUpgraded ? 9m : 6m)` —— 「可被无限升级」的牌第 2、3、4… 次升级全落进「升过了」那一支，
  数值再也不涨（`OnUpgrade` 里的 `UpgradeValueBy` 加的是变量，打出时不读变量）。
  修法：这种牌（`InfinitelyUpgradable`，生成期用 `_cardOwner` 认）改成 `(公式 + 增量 * base.CurrentUpgradeLevel)`；
  卡面预览变量（`FormulaVars.cs`）也按 `card.MaxUpgradeLevel > 1 ? _upgrade * card.CurrentUpgradeLevel : (IsUpgraded ? …)`。
  普通牌逐字不变；`ProjectRecovery.FillExpr` 也认新写法（回读不丢增量）。新增探针 `--infupgradeprobe` 钉住。

* 「自定义状态『生成卡牌时』里想放『召唤伙伴』，生成被拦住了」（截图报的）：状态那条链以前没实现「召唤伙伴」。
  修法：复用卡牌那条链**同一份** `EmitSummonPet`，召唤者传 `base.Owner.Player`（状态钩子里 base.Owner 是 Creature）；
  钩子本体不给 choiceContext 时照本体做法造一个（不然 CS0103 —— 探针真编译抓到过）；
  那只召唤物没启用照样报错拦住；状态描述也补上「召唤伙伴（N 点生命）」/「牺牲伙伴，换取…」
  （以前会把英文种类名原样印在状态说明里）；「从工程反推存档」两种召唤者写法都认。
  新增探针 `--petprobe` 钉住（生成 + 真编译 0 错误）。

* 「遗物的两个触发时机同时满足需要两次触发」（别人实测报的）：All（全部满足）模式以前写成「**只有最后那条**时机检查 `__andHit` 标记」，
  于是「最后那条先触发、别的时机后触发」时，凑齐的那一刻没人检查 → 必须等最后那条**再触发一次**才跑。
  修法：All 模式改成**每一段都检查**（`__andHit` 全为真就跑 + 立刻清标记），谁凑齐谁当场跑、与顺序无关，一个窗口仍只跑一次。
  Any 模式逐字不变（`--samplegen` 112 个文件里仍只有 `SevenGuard.cs` 那一行 GainsBlock 是故意改的）。

* 「节点图里『变化卡牌』的『从哪里选牌』不生效，生成被拦住」（截图报的）：节点图的选牌类效果以前**只从手牌里挑**。
  修法：四摞都支持（手牌照旧 `FromHand`，老文本逐字不变；别的三摞走 `CardSelectCmd.FromCombatPile(… PileType.X.GetPile(__fgPlayer) …)`，
  随机档也换成对应的那一摞，「牌顶」= 拿抽牌堆最上面那张），覆盖 变化 / 复制 / 消耗 / 丢弃 / 升级卡牌；
  校验器里那条错误随之删掉。探针 `--petprobe` 里加了「变化卡牌（抽牌堆）+ 复制卡牌（弃牌堆）」两张，生成 + 真编译 **0 错误**。

* 「是不是还有一些效果只能在卡牌页做、锻造节点图做不了」→ 用户截图点名「删除卡牌（全局：从牌组删牌）」被拦住。
  修法：节点图补上 RemoveCardGlobal / AddCardGlobal / TransformCardGlobal / UpgradeCardGlobal /
  Outbreak / PlaySfx / PlaySoundFile 七档（都只要玩家 + 可选 choiceContext），实现上加了一个
  ViaCodeWriter(...) 桥，**复用**卡牌页 / 状态页那几份现成发射器（不再抄第二遍）。
  仍然没接、会照旧报错说清：BoostCard（强化指定卡牌，要额外生成强化 Power，节点图这条链还没验）、
  要「打出这张牌」上下文的（复制 / 重放 / 回合结束自动打出…）等。探针 --petprobe 里加了这几种，生成 + 真编译 **0 错误**。

* 「强化指定卡牌怎么不能做」：它要额外生成一张强化 Power，而那个「用到才生成」的判断（CollectBoostEffects）
  只扫卡牌 / 遗物 / 药水 / 状态、**不扫锻造图** —— 真在节点图里放开就会 CS0246，所以以前是故意拦着。
  修法：CollectBoostEffects 也扫锻造图上的【效果】模块，节点图里这一档正式接上
  （生成写法与状态页逐字一致，只是「自己」= __fgSelf；强化 Power 仍按「目标卡 + 强化什么」去重生成一个）。
  探针 --petprobe 里加了它（目标卡 SevenSlash + 强化伤害 2），生成 + 真编译 **0 错误**。

### 验收（红线）

> **红线实测数字（第 54 批）**：① `dotnet build`（App / Cli）**0 错误**；
> ② `--uicheck` Debug **3749 PASS / 0 FAIL**（新增：多块【触发时机】界面 + 生成端 + 撞钩子报错 + 盯谁守卫四档 + 调试日志两条 + X/Y 按「花掉的那部分」三条 + 多行描述三条 + 计数基准四条）；
> ③ `--samplegen` **112 个文件逐字节相同**（只差 `ExampleMod.sln` 里的随机 GUID）；
> ④ 真实编译：`--multiprobe`（双时机卡 + 双时机遗物）、`--remprobe`（4 个「盯谁」探针 + 遗物）、
> **`--dbgprobe`（调试日志开着的锻造图卡）**、**`--descprobe`（多行描述：状态描述 + 卡牌自定义描述都用 CRLF）**、
> **`--remprobe` 里新增的三个计数探针**、**`--petprobe`（状态里放召唤伙伴）** 各 `forge --profile … --build` **0 错误**；
> ⑤ **用户存档 `SparkleMod.json` 原样重跑** `forge --profile … --out <临时目录> --build`：**编译 DLL OK / 0 错误**，
> `SparkleCard43.cs` 里 `Y` = `(decimal)base.ResolveStarXValue()`（只读，未动他的存档）
> （PCK 导出那一步照旧因为没有 Godot 路径而失败，与生成无关）。

## 第 52 批需求（V0.2.167：**锻造节点图支持「生成 / 变化出来的卡的附加处理 / 加关键词 / 复制份数」**，用户实测截图 + 提问「是不是好多效果在锻造中都用不了」；**已本地发版**）

> `--uicheck` 基线 = **3670 PASS / 0 FAIL**（Debug，V0.2.166）。
> 用户的卡（Da Capo / 第一~四乐章，升级后）用了「生成卡牌」的附加处理，构建时被
> 「「生成 / 变化出来的卡的附加处理…」在锻造节点图里不生效」**报错拦住**（生成被拦住 = 没法构建）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **看清为什么节点图读不到** | 这几栏**只存在新编辑器那份 `EffectSpec` 上**，而 `ForgeEffectBridge.MirrorToFields` 只回写老字段（种类 / 数值 / 次数 / 概率 / 目标卡 / 生成到 / 药水 / 反向 / 下回合），**不回写**附加处理 / 关键词 / 复制份数 ⇒ 老的发射器读 `node.Get(...)` 读不到，只能报错拦住（`UnsupportedEffectSettings`） | ✅ 已发 |
| ② | **生成端改成直接读 spec + 复用卡牌那条链** | `ForgeGraphGen.GraphSpec(node)`（`ForgeEffectBridge.Ensure`）+ `EmitGraphSpawnModifiers` / `EmitGraphTempUpgradeApply`（把 `CSharpCodeGen.EmitSpawnModifiers` / `EmitTempUpgradePowerApply` 吐出来的行按缩进追加 —— 那两份早就按「链」参数化，传 `__fgPlayer` / `__fgSelf`）。生成卡牌 / 变化卡牌（接住本体返回的 `cardAdded`）/ 复制卡牌（按份数循环）三条路都接上 | ✅ 已发 |
| ③ | **「用到才生成」的文件** | 图里用「仅本回合升级」也要生成 `TempUpgradePower.cs`：`CSharpCodeGen.UsesTempUpgrade` 扩到**锻造图上的 EffectSpec**（新增 `GraphEffectSpecs(p)`，用 `LocalizationGen.GraphEffects`），漏了就是 CS0246 | ✅ 已发 |
| ④ | **不再报错 + 自检 6 条** | `UnsupportedEffectSettings` 去掉这三项（其余缺口照旧报错拦住）；自检：三项都生成出来 / 不再被拦 / 仅本回合升级挂 Track / TempUpgradePower 会生成 / 复制份数按份数循环 / **对照**（没勾的老图逐字还是那一句 `CreateCard + AddGeneratedCardToCombat`）。另加 CLI 探针 `--graphmodprobe`（造这样一份存档）用于真编译 | ✅ 已发 |
| ⑤ | **同批带上的另一批修复**（工作区里已做好、未发版） | `PowerRemoval.cs` 花括号配平（CS1022）；同一钩子多个时机只生成一个 override（CS0111：PowerChanged+PowerReached / DamageReceived+EnemyDamaged / DamageDealt+EnemyDealtDamage / BlockGained+EnemyBlockGained）；BoostCard / 从弃牌堆拿牌 在敌人回合开始时自己造 choiceContext（CS0103）；生成卡牌目标卡不存在提前报错（CS0246）；「获得时」+打敌人 报错拦住（开局 NRE）；纯小写类名 CS8981 提示；输出目录在游戏 mods\ 里警告；sts2.dll 版本漂移检测；锻造图「逐个敌人」开调试日志时 `__fgFoe` 作用域 | ✅ 已发 |

> **红线实测数字（第 52 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3692 PASS / 0 FAIL**（新增 6 条）；
> ③ **真编译**：`--graphmodprobe` 造的存档（锻造卡：生成卡牌 + 直接升级/本回合免费/加关键词；复制卡牌 + 份数 3 + 附加处理）
> `forge --profile … --out … --build` → **生成的模组工程 0 错误**；
> ④ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（没勾这几项的老图逐字不变）；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.167 -NoRelease -NoCommit -Yes`。

## 第 51 批需求（V0.2.166：**「晕眩值到 100 却不击晕敌人」**，用户实测 + 我读了他的存档；**已本地发版**）

> `--uicheck` 基线 = **3665 PASS / 0 FAIL**（Debug，V0.2.165）。
> 用户原话：「看看我的游戏日志，为什么晕眩值达到100后不会击晕敌人」。
> 没有日志可查（最近那份只是启停日志）→ 直接读了他的存档 `D:\ds\s\4\自定义角色存档\WendaoMod.json`
> 与生成工程 `…\WendaoMod\cs\Powers\WendaoPower1.cs`，一眼定位。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **真因（配置，不是引擎）** | 他造了个贴在敌人身上的减益「击晕值」，触发时机 =「某个状态层数到达 N 层时」(N=100)、**盯「任意敌人」+ 盯的就是它自己**、效果 = 击晕「指定敌人」。生成出来的守卫是 `power.Owner != base.Owner && …HittableEnemies.Contains(power.Owner) && power is WendaoPower1` —— 状态里的「自己」就是挂着它的那个单位，「击晕值」自己涨到 100 时 `power.Owner == base.Owner` ⇒ **这个条件永远不成立**，整条 if 一次都进不去（只有别的敌人身上那份同名状态跨过 100 才会） | ✅ 已发 |
| ② | **顺带一条口径** | 状态里的「敌人」= **玩家视角的敌方那一侧** ⇒「作用对象 = 指定敌人」取的是那一侧里**第一个可打的敌人**，不一定是身上带着这个状态的那只（多只敌人时会晕错）。正解：盯 = 自己、作用对象 = 自己（状态挂在敌人身上时「自己」就是它） | ✅ 已发 |
| ③ | **修 CS1061：状态链上「击晕自己」生成 `base.Owner.Creature`** | `EmitStun` 的 `default`（对自己）分支以前写死 `CreatureCmd.Stun(base.Owner.Creature)` —— 卡牌 / 遗物 / 药水的 `base.Owner` 是 Player 所以对，**自定义状态的 `base.Owner` 本身就是 Creature**（`Creature` 上没有 `Creature` 属性）⇒ 用户按 ② 改成「自己」就会**编译不过**。给 `EmitStun` 加 `selfExpr` 参数（三条老链照旧传 `base.Owner.Creature`，生成结果逐字不变；状态链传 `base.Owner`） | ✅ 已发 |
| ④ | **配置检查新增两条（防复发）** | **警告**：盯「任意敌人」而盯的那个状态**就是这条时机所在的状态自己** → 说清「自己涨到 N 那一下不会触发」+ 指路「盯：自己」；**提示**：**减益**状态里的「击晕」作用对象 = 指定 / 全体 / 随机敌人 → 说清取的是「玩家视角敌方那一侧第一个可打的敌人」 | ✅ 已发 |

> **红线实测数字（第 51 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3670 PASS / 0 FAIL**（新增 5 条）；
> ③ **真存档验证**：把 `WendaoMod.json` 复制一份、只把那两处改成「自己」→ `forge --profile … --out … --build` **0 错误**，
> 守卫变成 `power.Owner == base.Owner && power is WendaoPower1 && power.Amount >= 100m …`、击晕变成 `CreatureCmd.Stun(base.Owner)`；
> ④ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.166 -NoRelease -NoCommit -Yes`。

## 第 50 批需求（V0.2.165：**切到先古页后卡面预览还是卡牌页那张**，用户实测；**已本地发版**）

> `--uicheck` 基线 = **3661 PASS / 0 FAIL**（Debug，V0.2.164）。
> 用户原话：「点卡牌页查看卡牌，然后再点先古页查看先古牌，先古牌的卡面会消失，并且比例变成卡牌页的比例而不是竖图的比例」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **先量出来再改** | 新增 4 条自检复现用户那两步（卡牌页选普通卡 → 切「诅咒 / 先古 / 状态」页选先古卡），**修之前实测**：`当前预览卡=自检比例卡 / 1000×760 / Uniform`（= 预览还停在卡牌页那张）⇒ 框是横图比例、图按普通卡的 key 去找（先古卡面在另一个 key 上）⇒ 看起来「卡面消失」 | ✅ 已发 |
| ② | **真因** | 三页共用同一套卡面预览（判据 `CardUiSelection ?? SpecialCardOf()`），而卡牌页那个列表镜像的语义是「点空槽行时**保持上一张**」（`SlotRowMirror.UiSelection`）；三栏互斥对诅咒 / 先古用的是 `ClearSelection()`（列表+镜像一起清），对**卡牌页**只写了 `CardList.SelectedItem = null` ⇒ 镜像不松手 | ✅ 已发 |
| ③ | **两个方向都连镜像一起清** | `ClearCardPageSelectionForPortrait()`（选中诅咒/先古/状态卡时：清列表选中 + `_cardMirror.ClearSelection()` + 通知 `CardUiSelection`）、`ClearSpecialCardSelections()`（在卡牌页选中真卡时清那三栏 —— 反方向以前也没清）。修完：普通卡 1000×760/Uniform → 先古卡 **606×852/Fill** 且预览取的是先古卡 → 切回又变 1000×760 | ✅ 已发 |

> **红线实测数字（第 50 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3665 PASS / 0 FAIL**；
> ③ **反向验证**：修之前那 2 条断言 FAIL（明细就是 `自检比例卡 / 1000×760 / Uniform`），修完全绿；
> ④ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只动界面选中逻辑）；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.165 -NoRelease -NoCommit -Yes`。

## 第 49 批需求（V0.2.164：**「数值公式」铺到遗物 / 药水 / 自定义状态**，用户问「状态和遗物不能使用公式吗」；**已本地发版**）

> `--uicheck` 基线 = **3647 PASS / 0 FAIL**（Debug，V0.2.163）。
> 同批还补记了随 V0.2.162 / V0.2.163 一起发出去、没单独记的那条修复（见 ④）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **为什么以前不能** | 公式求值要「自己 / 玩家 / 目标」：卡牌链靠「打出前先算一批局部变量」（`__pM` / `__X` / `__fgPlayer`…），遗物 / 药水 / 自定义状态的执行点（触发器钩子 / `OnUse`）没有这套前置 ⇒ 校验器一概拦住。而**锻造节点图**一直能用公式 —— 它走的是**全内联求值**（`ForgeFormula.ConditionResolver`，参数就地展开、不要局部变量） | ✅ 已发 |
| ② | **三条链接上内联求值** | `ForgeFormula.InlineResolver(p, playerExpr, selfExpr, targetExpr)`（`ConditionResolver` 改成它的薄包装，老输出逐字不变）；`CSharpCodeGen.InlineAmountFormula` + 三处取数点（遗物 `EmitRelicEffectOnce` / 药水 `EmitPotionEffectOnce` / 状态 `CustomPowerGen.EmitEffect`），格挡那条单独走 `blockAmt`。映射：遗物 / 药水 `__fgPlayer=base.Owner`、`__fgSelf=base.Owner.Creature`；状态 `__fgPlayer=base.Owner.Player`、`__fgSelf=base.Owner`；「指定敌人」来源的参数三条链都取**可打的第一个敌人** | ✅ 已发 |
| ③ | **显示：照卡牌那套给数字** | 遗物 / 药水的描述是 `{Heal:diff()}`，读的是变量的 `PreviewValue`，而它们**没有**卡牌那种「显示前回调」⇒ 生成一个 `__RefreshFormulaPreview()`，挂在 `ExtraHoverTips`（本体取悬停说明必读）上现算（`HoverTipsOverride` 加 `refreshFormulaPreview` 参数，老路径仍是表达式体、逐字不变）。自定义状态是生成期写死的纯文本 ⇒ 描述里写**公式本身**。另外这两条链也要 `FormulaVars.cs`（`UsesFormulaVars` 扩到遗物 / 药水）与 `ForgeFormulaRuntime.cs`（新增 `AnyNonCardFormula`），不然 CS0246 | ✅ 已发 |
| ④ | **补记：随 162 / 163 发出去的那条修复** | 「状态里监听某状态被移除时无法触发」：`PowerModel.RemoveInternal` 的通知补丁以前只通知**被移除那个状态所在的那一只生物** ⇒「挂自己身上、盯**敌人**身上的状态被移除」永远收不到通知（守卫里那个 `HittableEnemies.Contains` 分支是死代码）。改成通知**这一场战斗里的所有生物**（`CombatState.Creatures`），并加一行 `[模组调试] 状态被移除：…（通知了 N 个…）`（开调试日志时）。另外用真引擎 dll 实测过 Harmony **能**打上这个补丁（`GetPatchInfo` 非空）⇒ 问题确实在通知范围 | ✅ 已发（162/163 随包） |
| ⑤ | **校验器改口径 + 界面** | 那 5 种在三条链上放行；不支持的种类照旧报错（文案改成不再说「只做在卡牌上」）；新增两条**提示**：用了 `X`/`Y`（= 当前能量 / 额外资源量）、用了「指定敌人」来源的参数（= 可打的第一个敌人）。界面：这三条链的效果编辑器也摆出「可用量」小框（判据同一个 `ShowAmountFormula`），说明文字按链分口径 | ✅ 已发 |

> **红线实测数字（第 49 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；
> ② `--uicheck` Debug **3661 PASS / 0 FAIL**（新增 14 条）；③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**；
> ④ **真编译**：探针存档（遗物 `回复 M/2` + `造成 A+2`、药水 `格挡 M/4`、状态 `失去 M/10`）
> `forge --profile … --out … --build` → 生成的模组工程 **0 错误**（抓出两个真问题：属性不能直接写方法体 CS8124；
> 缺 `FormulaVars.cs` / `ForgeFormulaRuntime.cs` 会 CS0246）；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.164 -NoRelease -NoCommit -Yes`。

## 第 48 批需求（V0.2.163：**「取几张」第四档「全部」（不弹选牌界面）**，用户要求；**已本地发版**）

> `--uicheck` 基线 = **3600 PASS / 0 FAIL**（Debug，V0.2.162）。
> 用户原话：「生成器是不是还不能选择所有手牌」→ 我答「能选任意张（弹界面），但还不能『不弹界面、全动』，
> 要的话加第四档」→ 用户：「**要**」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **新增 `CountModes.All` 第四档** | 「全部（不弹选牌界面，直接对所有符合条件的牌动手）」；界面「取几张」下拉四档；`EffectSpec.CountIsAll` / `CountHasNoNumber`（原来只看 `CountIsAny` 的两处判据改成「任意张或全部」→ 不声明 `Cards` 变量、升级增量无落点） | ✅ 已发 |
| ② | **15 种选牌类效果逐条实现 All 分支** | 消耗 / 丢弃 / 升级 / 复制 / 重放 / 变化 / 回合结束自动打出 / 放置 / 给予关键词 / 三处拿牌 / 三种全局牌组。写法照本体习惯：消耗 / 升级 / 删牌逐张调用，丢弃 / 放置 / 拿牌**整批**（`CardCmd.Discard(ctx, list)` / `CardPileCmd.Add(list, …)`），变化 / 复制 / 重放 / 给关键词先 `.ToList()` 拍快照（遍历中改集合会炸 —— 本体恶魔之焰也是这么写的） | ✅ 已发 |
| ③ | **卡面文案 + 列表摘要** | 卡面「消耗 所有手牌。」（不带量词）；「全部」档把不生效的「自己选 / 随机 / 牌顶」从卡面与列表摘要里去掉（沿用 V0.2.161 那条规矩）；消耗 / 丢弃 / 升级 / 变化 / 复制 / 重放 / 回合结束 / 放置 / 给关键词 / 三处拿牌 / 全局三种，逐条写 All 说法 | ✅ 已发 |
| ④ | **回读：新增 `CET:CountMode=` 标记** | 「全部」档代码里**根本没有选牌语句**（是 foreach 直接动手），逐行猜不出来 ⇒ 非默认档（最多 / 任意 / 全部）写一行 `// CET:CountMode=…`，回读按 pending 标记配给紧跟那条效果。顺手修掉老缺口：**「最多 / 任意」以前回读会退回「恰好 N 张」** | ✅ 已发 |
| ⑤ | **回读跳段名单（踩了两个坑）** | All 档的实现行要进「自包含效果整段跳过」名单（只在 `skipSelfContainedBody` 里加、且写成 All 档**特有**的精确形状）：① 一开始写成宽条件 `foreach (CardModel c in ` → 把别的链上同形状的行也吞了（那条效果整条丢失）；② All 档沿用 `var toDiscard` / `var __taken` → 与「自己选」那支的识别行同名 ⇒ 多回读一条 ⇒ 改成 `__fgAllDiscard` / `__fgAllTaken`。**15 种逐条「生成→回读」自检**把这两个坑都钉住了 | ✅ 已发 |
| ⑥ | **校验器（不静默）** | 「全部」档：「选牌方式不生效」+「升级增量对这条没有作用」两条提示；「随机 + 最多 / 任意」那条警告的文案补上「全部」 | ✅ 已发 |
| ⑦ | **自检 47 条** | 四档在册 / 15 种都有这一栏 / 15 种逐条生成出 All 分支 / 15 种逐条带 `CET:CountMode=All` / 恶魔之焰原样写法（`foreach … await CardCmd.Exhaust` 且**不出现任何 CardSelectCmd**）/ 不声明 Cards 变量 / 卡面「消耗 所有手牌。」/ 校验器两条 / 三档（最多·任意·全部）**生成→回读**原样 + 0 条没认出来 | ✅ 已发 |

> **红线实测数字（第 48 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；
> ② `--uicheck` Debug **3647 PASS / 0 FAIL**；
> ③ `--samplegen` 与 V0.2.143 基线 **113 个文件逐字节一致**（只差 `.sln` 随机 GUID；新档是可选选项，默认档文本零改动）；
> ④ 探针：生成「全部档 15 种 + 80 条读数 + 参数页三种来源」的模组 → 真 Godot SDK 编译 **0 错误**；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.163 -NoRelease -NoCommit -Yes`。

## 第 47 批需求（V0.2.162：**「挖掘游戏本体的所有参数」**，用户要求；**已本地发版**）

> `--uicheck` 基线 = **3502 PASS / 0 FAIL**（Debug，V0.2.161）。
> 用户原话：「挖掘一下游戏本体的所有参数，如本回合消耗的手牌，拥有的状态牌数之类」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **把本体读得到的运行时状态全过一遍** | 解包工程 `src/Core/**` 逐文件读（Combat / History / Entities / Models / Runs / Commands）+ 真 `sts2.dll` 元数据（9761 类型）双向核对：**19 种战斗历史条目**、`Creature` / `Player` / `PlayerCombatState` / `CardModel` / `CardPlay` / `ICombatState` / `IRunState` / `PlayerMapPointHistoryEntry` / `ScoreUtility` / `ExtraPlayerFields`。产出 **`docs/VANILLA_STATE_PARAMS.md`**（每条带 C# 表达式 + 文件:行号 + 粒度 + 是否已内置） | ✅ 已发 |
| ② | **内置只读量 23 → 80 条** | 新增 57 条：牌组张数 / 牌组可升级数 / 手牌与牌组五类牌数（**`DeckStatusCount` = 用户点名的「拥有的状态牌数」**）/ 自己生命格挡资源 / 增益减益 / 伙伴球 / 活敌数与总生命 / 跑图层数幕数升华 / 本局累计伤害与减益 / 本回合与本场战斗的**打出·抽到·伤害·格挡·击杀·花掉的资源·召唤**（**`PlayedThisTurn` / `ExhaustedThisTurn` = 用户点名的「本回合消耗的手牌」**）。全部用本体现成 public API，不写补丁 | ✅ 已发 |
| ③ | **新探针：拿游戏自己的 dll 编译每一条表达式** | `_probe\formulaprobe`：把 `FormulaBuiltins` 每条表达式吐成一个真 C# 工程 → 用真 `sts2.dll` + `GodotSharp.dll` 编译；再**真生成一次模组**（用到全部 80 条 + 参数页 3 种来源）→ 用真 Godot SDK 编译。**当场抓出两个「一用就编不过」的老 bug**（见 ④⑤） | ✅ 已发 |
| ④ | **修 bug：`PowersOnMe` 一直编不过** | 写成 `.Powers.Count(谓词)`，而 `Creature.Powers` 是 `IReadOnlyList`（**自带 `Count` 属性**）→ `CS1955 不可调用的成员 Count`（V0.2.95 起就在，自检只看文本看不见）。改成 `.Where(谓词).Count()`，并加「所有内置量都不许出现 `集合.Count(谓词)`」的守卫断言 | ✅ 已发 |
| ⑤ | **修 bug：参数页「能量数」生成的是编不过的表达式** | `ParamSources.Energy` 生成 `__fgPlayer?.Energy`，而**本体 `Player` 上没有 `Energy`**（在 `PlayerCombatState` 上）→ 用户一用就整个模组编不过。改 `((decimal)(__fgPlayer?.PlayerCombatState?.Energy ?? 0))`；`ParamExpr` 同时改成**先认 7 个固定来源再看实时读数** | ✅ 已发 |
| ⑥ | **修 bug：`XSpent` / `YSpent` 用在伤害数值上编不过** | 卡面预览变量是 `static (card, target) => …` 的**静态匿名函数**，里面不许有 `base.`（CS8821）；预览表达式把剩下的 `base.` 换成 `(card).`（打出时那条照旧 `base.`） | ✅ 已发 |
| ⑦ | **战斗外不再抛异常** | 四条老牌堆读数改成 `{p}?.PlayerCombatState?.Hand?.Cards.Count ?? 0`（本体 `PileType.GetPile` 战斗外直接抛）；老表达式进 `LegacyCSharp`，老工程回读照样认 | ✅ 已发 |
| ⑧ | **自读量加 `My` 前缀 + 下拉改可搜索** | `MaxHp`/`Energy`/`Gold` 和参数页「取值来源」撞名（会出两个同名项、且按 Id 永远只命中前面那个 → 「指定敌人的最大生命」被换成自己的）⇒ 自读量叫 `MyHp`/`MyMaxHp`/`MyEnergy`/`MyGold`…；「取值来源」下拉换成 `SearchComboBox`（87 项，打字筛） | ✅ 已发 |
| ⑨ | **自检 98 条** | 新增：80 条逐条在册（带中文说明）/ 集合谓词守卫 / 战斗外保护 / 老表达式兼容 / 参数页两条修好 / 命名不撞车 / 用户点名那两条的表达式与文案 / 生成端落码与「预览变量无 base.」/ 生成→回读三条公式原样 + 0 条没认出来；存量 4 条按新表达式更新 | ✅ 已发 |

> **红线实测数字（第 47 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；
> ② `--uicheck` Debug **3600 PASS / 0 FAIL**（发布版同）；
> ③ `--samplegen` 与 V0.2.143 基线 **113 个文件逐字节一致**（只差 `ExampleMod.sln` 随机 GUID）；
> ④ 探针：80 条表达式拿真 `sts2.dll` 编译 **0 错误**；生成的「全量读数探针模组」拿真 Godot SDK 编译 **0 错误**；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.162 -NoRelease -NoCommit -Yes`。
> ⚠ 探针留下：`_probe\formulaprobe`（改任何读数表达式后必须跑一遍 —— 它抓的是「生成文本对、但编译不过」这类自检盲区）。

## 第 46 批需求（V0.2.161：**「抽卡 / 获得能量的作用对象没了，却显示对敌人」**，用户实测；**已本地发版**）

> `--uicheck` 基线 = **3496 PASS / 0 FAIL**（Debug，V0.2.160）。
> 用户原话：「为什么抽卡，获得能量之类的效果种类的对象没了，但是显示的是对敌人」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **看清是「文案在说假话」** | 生成端确实**不读**这些种类的作用对象（V0.2.150 的机械测量早就证明了，所以那一行才被藏起来）；问题是 `EffectSpec._targetSide` 的**默认值就是 `"Enemy"`**，而**两处文案**照旧把它写出来：① 四页共用的列表行 `EffectSpec.Display` →「抽牌 1 **→ 单体敌人**」；② 自定义状态描述 `PowerTriggers.EffectText` →「抽 1 张牌**（对指定敌人）**」（那里写的是「`TargetSide == "Self"` 才省略」，默认值一漏就中） | ✅ 已发 |
| ② | **同一份判据用两处** | 规矩补上另一半：**藏起来的栏位，文案里也不许写**。`Display`：`PetAction \|\| !EffectFieldPolicy.UsesTargetSide(Kind)` 才不写尾巴；`PowerTriggers.EffectText`：同一份判据（顺带替掉原来的 `PlaySfx/PlaySoundFile` 特例，那两个本来就不在允许清单里）。该显示的种类一个字没少（伤害 / 格挡 / 施加状态… 照旧） | ✅ 已发 |
| ③ | **自检 6 条（含全量双条件）** | 67 种效果逐个建 `TargetSide="Enemy"` 探针，断言「行尾有 `→ 单体敌人`」**当且仅当**「作用对象那一行会显示」（宠物效果除外）→「67 种逐条对」；抽牌 / 获得能量的行不带、状态描述不带；两条**对照**（造成伤害照旧带）。**反向验证**：判据退回原样 → 3 条 FAIL / PASS 3499（失败明细直接印出「抽牌 1 → 单体敌人」），改回即 3502 / 0 | ✅ 已发 |

> **红线实测数字（第 46 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3502 PASS / 0 FAIL**；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只改文案，生成端一字未动）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.161 -NoRelease -NoCommit -Yes`。

## 第 45 批需求（V0.2.160：**角色页「角色显示名」被挤到窗口外面**，用户实测；**已本地发版**）

> `--uicheck` 基线 = **3493 PASS / 0 FAIL**（Debug，V0.2.159）。用户原话：「角色页右侧的角色显示名怎么这么右边都看不到了」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **量尺寸找真因** | 先加断言量布局（不猜）：角色页那张 4 列表单修之前实测 **列宽 = 227 / 1366 / 147 / 1366**、表单右边 **3125**（可视区右边 1395）、「角色显示名」输入框右边 **3117** ⇒ 在窗口外面。真因：列宽写的是 `Auto / 320 / Auto / 320`，而同一张 Grid 里有 4 块**跨越 3~4 列且自动换行**的长说明（调试日志那一大段 / 初始卡组那段 / 召唤物那段）—— WPF 把跨列子元素「想要的宽度」摊到被跨越的列上，两个固定 320 被撑成各 1366；外层 `ScrollViewer` 横向滚动**默认 Disabled** ⇒ 看不到也点不到 | ✅ 已发 |
| ② | **两条一起修** | ① 输入列 `320` → `*` + `MinWidth="220"`（按窗口伸缩）；② 长说明**搬出网格**（放到网格下面单独的 StackPanel，仍在同一个 ScrollViewer 里）。修好实测 **227 / 488 / 147 / 488**、表单右边 **1368** ≤ 1395、显示名输入框宽 **480** | ✅ 已发 |
| ③ | **自检 3 条 + 顺手一个截图模式** | 断言：4 列表单找得到（打印列宽）/ 表单右边 ≤ 视口右边 / 「角色显示名」输入框右边 ≤ 视口右边且宽 ≥ 140（**修之前后两条 FAIL**，等于反向验证）。另加 `--roleshot`（按默认 1420×880 把角色页整窗渲染成 PNG，和 `--uishot` / `--forgeshot` 同一套，只给看、不写存档） | ✅ 已发 |
| ④ | **给后续版式问题留一句铁律（文档）** | README：**Grid 里「自动换行的长文字」不能跨列**（跨列 = 把列宽摊出去），输入列一律 `*` + MinWidth，长说明一律放在网格外面 | ✅ 已发 |

> **红线实测数字（第 45 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3496 PASS / 0 FAIL**；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（这次只动 XAML 版式，生成端一字未改）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.160 -NoRelease -NoCommit -Yes`。

## 第 44 批需求（V0.2.159：**`XSpent` / `YSpent` 在非 X 费牌上会抛异常**，第 43 批的收尾自检发现；**已本地发版**）

> `--uicheck` 基线 = **3490 PASS / 0 FAIL**（Debug，V0.2.158）。第 43 批刚打包完，复查生成代码时发现这个坑：
> 本体 `CardModel.ResolveEnergyXValue()` 第一句就是 `if (!EnergyCost.CostsX) throw new InvalidOperationException(...)`、
> `ResolveStarXValue()` 是 `if (!HasStarCostX) throw ...` —— 第 43 批发出去的是**直接调用**，
> 所以普通牌上写一次 `YSpent` 一打出去就抛异常。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **先判再调** | 两个内置量的表达式改成三元：`(base.EnergyCost.CostsX ? (decimal)base.ResolveEnergyXValue() : 0m)` / `(base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m)`（`EnergyCost.CostsX` 与 `HasStarCostX` 都是本体 `public` 成员）。同一个坑 V0.2.131 在「费用填 X」那条路上踩过，照同一条路修（自检里本来就有那条断言「费用不是 X 时不会生成会抛异常的 ResolveEnergyXValue()」） | ✅ 已发 |
| ② | **不静默（补两条警告）** | 公式里用了 `XSpent` 但「费用」不是 `X` → 警告「它不是 X 费牌，读数**恒为 0**，想有意义就把费用填成 X」；`YSpent` 与「额外资源量费用 = Y」同理 | ✅ 已发 |
| ③ | **自检 3 条** | 两个读数生成的是「先判再调」的三元（并断言卡牌源码里真的带 `base.HasStarCostX ? (decimal)base.ResolveStarXValue() : 0m`）；`XSpent` / `YSpent` 各自用在非 X 费牌上确实报警告（含「恒为 0」） | ✅ 已发 |

> **红线实测数字（第 44 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3493 PASS / 0 FAIL**；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（示例配置没用到这两个读数，生成端一字未动）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.159 -NoRelease -NoCommit -Yes`。

## 第 43 批需求（V0.2.158：**「次数」填 `Y`（额外资源量）只会触发一次**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~43 批都还没提交）。`--uicheck` 基线 = **3486 PASS / 0 FAIL**（Debug，V0.2.157）。
> 用户原话：「在次数填 Y（额外资源量）时，只会触发一次」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **查本体顺序（真因）** | 本体 `PlayCardAction` 是**先扣费、再跑效果**：`await _card.SpendResources()` → `OnPlayWrapper(...)`。所以效果跑的那一刻公式里的 `X` / `Y` 读到的是**扣完剩下的** —— 「Y 费」的牌读 `Y` = 0，生成端 `Math.Clamp(__Y, 1m, 20m)` 把它夹成 **1**，就是用户看到的「只触发一次」（`X` 费同理）。**不是生成端算错，是本体时序**，所以「让 `Y` 自己变成花掉的那个数」做不到 | ✅ 已发 |
| ② | **给出能用的读数（修法）** | `FormulaBuiltins` 新增两个内置量：**`XSpent`**「这张牌花掉的能量」= `(decimal)base.ResolveEnergyXValue()`、**`YSpent`**「这张牌花掉的额外资源量」= `(decimal)base.ResolveStarXValue()`（本体自己缓存的「这张牌花掉的那个数」，扣费之后再读也不会变 0）。「可用量」蓝框里直接能看到，三个公式框都能用 | ✅ 已发 |
| ③ | **写错时拦住（不要静默）** | `ProfileValidator`：卡牌是「X 费 / Y 费」（`CostIsXEffective` / `StarCostIsXEffective`）**且**效果公式里真的用了 `X` / `Y` → 给**警告**，写清「本体先扣费再跑效果 ⇒ 那一刻读到 0（次数被夹到 1）」并指向 `XSpent` / `YSpent`。判据是新写的 `ContainsIdentifier(formula, id)`（正则边界 `(?<![A-Za-z0-9_])Y(?![A-Za-z0-9_])`）—— 用 `ForgeFormula.Identifiers` 会把 `YSpent` 里的 `Y` 也算成用到 Y（踩过，改完自带正例仍报） | ✅ 已发 |
| ④ | **自检 4 条** | ① 两个读数都在 `FormulaBuiltins` 里且表达式真的指向 `ResolveEnergyXValue` / `ResolveStarXValue`；② 次数公式填 `YSpent` → 生成出来的就是 `(decimal)base.ResolveStarXValue()`；③ 「Y 费 + 公式用 Y」确实报出那条警告（含「先扣费再跑效果」+「YSpent」）；④ 只用 `YSpent` 的正例**不再**报那条警告（探针卡名不能带 `YSpent` 三个字 —— 断言按消息文本匹配，卡名会误命中，踩过） | ✅ 已发 |

> **红线实测数字（第 43 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；② `--uicheck` Debug **3490 PASS / 0 FAIL**；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `ExampleMod.sln` 的随机 GUID）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.158 -NoRelease -NoCommit -Yes`。
> ⚠ `--uicheck` 要用 `Start-Process <exe> -ArgumentList '--uicheck' -Wait` 跑：WPF 是 WinExe，`& exe` 立刻返回，
> 读到的会是**上一次**的 `uicheck_result.txt`（踩过，白读一轮）。

## 第 42 批需求（V0.2.157：**「打出一张牌时」实际是「打出一张牌后」**，用户实测 + godot 日志；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~42 批都还没提交）。`--uicheck` 基线 = **3468 PASS / 0 FAIL**（Debug，V0.2.156）。
> 用户原话：「状态监听『打出一张牌时』这个时机，会监听施加这个状态的牌，即『打出一张牌时』的时机实际为
> 『打出一张牌后』，触发时机靠后，与『一张牌结算完毕时』时机接近」。
> 本批还一起带上了工作区里先前做好、尚未发版的 4 条修复（见 ⑤，自检里早就有断言，只是没打包）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **查本体顺序 + 改钩子** | 本体 `CardModel`：`Hook.BeforeCardPlayed(cardPlay)` → `OnPlay`（卡的效果，上状态在这步）→ … → `Hook.AfterCardPlayed`（内部先 `AfterCardPlayed` 再 `AfterCardPlayedLate`）。工具那一档原来生成的是 `AfterCardPlayed` ⇒ 同一张牌施加的状态立刻被自己触发。**改法**：`CardPlayed` → **`BeforeCardPlayed`**（结算前，不会自己触发自己）；老语义单独留成新时机 **`CardResolved`** → `AfterCardPlayedLate` | ✅ 已发 |
| ② | **没有 choiceContext 怎么办** | `BeforeCardPlayed(CardPlay)` 本体**不给** choiceContext → 生成端按本体做法自己造 `HookPlayerChoiceContext`（复用已有的 `BootstrapInCombat` / `TriggerHasChoiceContext` 机制，和「花掉能量 / 自己失去生命」同一套）；只在该触发时机的效果**真的需要** context 时才造（自检探针一开始用了不需要 context 的效果，验不到那一行，换成「消耗卡牌（自己选）」才对） | ✅ 已发 |
| ③ | **三处菜单 / 签名 / 守卫 / 回读一起同步** | 状态侧（`PowerTriggerCatalog`）、遗物侧（`EffectCatalog.RelicTriggers`）、锻造页遗物档（`ForgeTimings.For("Relic")` + `ForgeTriggerGuardLines`）三处加 `CardResolved`、改 `CardPlayed` 的显示名（「打出一张牌时（这张牌还没生效）」/「一张牌结算完毕时」）；`RelicTriggerSignature` / `HasContext` / `BootstrapInCombat` 跟着调；卡牌链两条时机**共用**抽出来的 `EmitCardPlayedItems`；`ProjectRecovery`：`BeforeCardPlayed→CardPlayed`、`AfterCardPlayed`/`AfterCardPlayedLate→CardResolved`，并把它加进 `RelicHookIsTrigger`、把 `CardResolved` 纳入 CardFilter / 分段回读 | ✅ 已发 |
| ④ | **自检 5 条 + 存量断言更新** | 新增：状态里生成的是 `BeforeCardPlayed` 且**没有** `AfterCardPlayed` / 需要 context 时造了 `HookPlayerChoiceContext` / `CardResolved` 生成 `AfterCardPlayedLate` / 三处菜单显示名一致 / 遗物侧签名 + 归属守卫 + `HasContext`/`BootstrapInCombat` 口径。存量断言里 8 处写死了 `AfterCardPlayed` 或旧显示名 → 一并更新；`hookBootstrapIds` 加 `CardPlayed`、锻造守卫表加 `CardResolved`（27 条） | ✅ 已发 |
| ⑤ | **一起带上的 4 条旧修复**（工作区里已做好、未发版） | ① 自动类名撞名避让（事故：校验器报「卡牌类名重复：`GunnerRainyCard37`」）；② 「强化指定卡牌」的目标卡不存在时**生成前**报错（不再 CS0103）；③ 同一钩子合并多条触发时机时各条各自包裸块（消 `CS0128`）；④「回合结束时」的效果真的生成出来（`TurnEnd` / `PlayerTurnEnd` 都算） | ✅ 已发 |

> **红线实测数字（第 42 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3486 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.157 -NoRelease -NoCommit -Yes`。

## 第 41 批需求（V0.2.156：**拖动卡牌后卡面变成其他卡**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~41 批都还没提交）。`--uicheck` 基线 = **3465 PASS / 0 FAIL**（Debug，V0.2.155）。
> 用户原话：「在卡牌列表拖动卡牌后，卡面会变成其他卡」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **把三层 bug 全挖出来** | 卡面按**类名**存（`Art.CardPortraits`），自动类名按**行号**算 ⇒ 名字没跟卡走 = 图跟错人。三层：**(1) 时机**：拖动落点是 `Cards.Move → RefreshCardOrdinals() → SyncDetail()`，而 `RefreshCardOrdinals` 会把 `LastAutoOrdinal`（上次的号）就地改成新行号，可「旧名→新名」映射正是靠它算的 → 先覆盖就永远算不出映射（V0.2.153 我接的同步点在 `RefreshCardChoices`，**拖动这条路不走它**）；**(2) 重入**：搬迁改 `ClassName`/`LastAutoOrdinal`/`SpawnCardId` → PropertyChanged/CollectionChanged → `OnCardChanged`/`OnCardsChanged` 重入刷新 → 又搬一次；重入时号只改了一半 → 算出**部分映射**又搬一遍（实测一次拖动搬了 **4 遍、每次一张**，卡面互相覆盖）；**(3) 环**：往上拖会让名字成**环**（12→13、13→14、14→1），「顺序搬 + 目标已有就跳过」在环上会把先搬进去的图**删掉** | ✅ 已查证 |
| ② | **修时机** | `RefreshCardOrdinals()` 的**第一件事**改成 `SyncCardClassReferences()`（在写 `LastAutoOrdinal` 之前）——拖动、增删、复制、撤回这些走它的路径全部覆盖 | ✅ 已发 |
| ③ | **修重入** | `SyncCardClassReferences()` 加 `_syncingCardRefs` 重入保护（搬迁期间重入直接返回，由最外层一次性搬完）+ `Naming.RenumberCardClassNames` 改成**两趟**（先算完整张「旧名→新名」表、再动手改卡）——即使有重入也不会算出半张表。计数口径保持和以前完全一致（改名 +1；空类名那条只在 LAO 变化时 +1） | ✅ 已发 |
| ④ | **修环** | `RepairCardClassReferences` 搬卡面改成**从零重建新表**（`key → renamed[key] ?? key`），不再「在拷贝上 Remove(旧键)/赋值(新键)」——后者在环上会把刚放进去的图删掉 | ✅ 已发 |
| ⑤ | **自检（3 条，走真实路径）** | 用**真实**的 `RefreshCardOrdinals` + `Cards.Move`（不是另写模拟）：① 拖动后三张卡面各归各卡（不错位不丢）；② **反向说明**断言「旧名字现在属于另一张卡」（把你看到的现象钉死）；③ 往上拖形成**名字环**时也搬得对。**反向验证**：关掉重入保护 → 2 条 FAIL（`拖动探针1 拿到的是（没有卡面）`），打开即 0 FAIL。踩到的坑：探针卡要加在**真实 `_profile`** 上（拖动的入口是界面方法），所以自检里必须 try/finally 把卡和 `CardPortraits` 原样还回去 | ✅ 已发 |

> **红线实测数字（第 41 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3468 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**；④ 反向验证 2 条 FAIL → 0 FAIL；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.156 -NoRelease -NoCommit -Yes`。

## 第 40 批需求（V0.2.155：**上传自己的音频文件 + 「播放音频文件（我上传的）」效果**；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~40 批都还没提交）。`--uicheck` 基线 = **3447 PASS / 0 FAIL**（Debug，V0.2.154）。
> 用户原话：「那做自己上传音频文件」（承接上一版我说明的「放自己的音频要另做一条链路」）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **为什么不能走 FMOD** | 本体声音全是 FMOD 事件，而**模组注册不了新事件**（bank 里只有事件 GUID，42MB 的 bank 全文扫过一个 `event:/` 都没有）⇒ 只能绕开 FMOD：Godot `AudioStreamPlayer` + `GD.Load<AudioStream>("res://audio/…")`，音频作为**普通资源**打进 PCK（导出预设本来就是 `export_filter="all_resources"`） | ✅ 已查证 |
| ② | **素材链路（照卡面 PNG 那套）** | `AudioAssetSpec`（`Name` = 显示名兼生成的 Key、`File` = 用户本机绝对路径、`Key` = 名字去非法字符 + 原扩展名）+ `CharacterProfile.AudioAssets`；`ArtGenerator.Generate` 里把每条复制到 `<工程>/audio/<Key>`（文件不在本机就日志警告并跳过，扩展名不认也警告） | ✅ 已发 |
| ③ | **新效果种类 + 生成** | `EffectKinds` 新增 `PlaySoundFile`「播放音频文件（我上传的）」（数值 = 音量%）；`CSharpCodeGen.EmitPlaySoundFile`：整段包在 `{ }` 里（同一方法里出现两次不撞名）→ `new AudioStreamPlayer()` + `GD.Load<AudioStream>("res://audio/<Key>")` + `VolumeDb = Mathf.LinearToDb(pct/100)` + `((SceneTree)Engine.GetMainLoop()).Root.AddChild(...)` + `Finished += QueueFree` + `Play()`；四条链（卡牌 / 遗物 / 药水 / 状态）各加一个 `case`；`NoDynamicVarKind` / `NextTurnMeaningless` / `UsesUpgradeAmount` 三份清单同步 | ✅ 已发 |
| ④ | **文案 / 校验 / 回读** | 三处描述写「播放音频：<你起的名字>（音量 N%）」；校验：没选 = 错误、条目被删 = 错误、文件不在本机 = 错误、扩展名不认 = 错误（Key 会退回 .ogg）、同名两条互相覆盖 = 警告、>200% = 警告；回读认 `CET:SfxFile=` 标记（`__sfx` 那几行进「整段跳过」白名单），**并且从工程 `audio/` 目录把音频清单也认回来**（否则恢复出来的存档下次生成会丢文件） | ✅ 已发 |
| ⑤ | **界面** | 「美术资源」页底部新增「音效 / 音频」区：列表（显示「名字 · 本机文件名 → audio/xxx」）+「上传音频…」（OpenFileDialog 过滤 ogg/oga/mp3/wav，重名自动加序号）+「删除」（只从存档去掉，不动本机文件）+ 底下小字说明；效果栏新增「音频文件」下拉（绑 `AudioAssetChoices`，`IsPlaySoundFile` 控制显隐）+ 音量说明 | ✅ 已发 |
| ⑥ | **顺手修一条 V0.2.154 的老问题** | `PowerTriggers.SupportedEffectKinds`（状态触发器能用哪些种类的白名单）漏了 `PlaySfx` → 上一版「状态里放本体音效」**被校验器拦着用不了**（生成端其实实现了）。这一版把 `PlaySfx` 和 `PlaySoundFile` 都补进白名单 | ✅ 已发 |
| ⑦ | **自检 18 条** | 种类与音量口径 / Key 规则 / 四条链都生成播放器那一段（含 `0.8f`）/ 没选音频只写注释 / 卡面与状态描述 / **真跑一次 `ModGenerator.Generate` 并断言 `audio/<Key>` 被复制进工程** / **`ProjectRecovery` 把效果与音频清单都认回来且 `HasUnparsed == 0`** / 校验器两条 / 扩展名口径（`.flac`/`.txt` 不认）/ 界面两处。踩到的坑：探针 profile **不能清空卡牌**（奖励池 < 3 张会生成失败 → 准备那步红，且 `ProjectRoot` 为空会让后续 `Path.Combine` 抛异常把自检整段中断）；`IsSupportedAudio` 必须看**原始**扩展名（按归一化后的名字判断会把 `.txt` 也当成支持） | ✅ 已发 |

> **红线实测数字（第 40 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3465 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（没上传音频时生成结果零变化）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.155 -NoRelease -NoCommit -Yes`。

## 第 39 批需求（V0.2.154：**新增效果种类「播放音效」**，用户要求；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~39 批都还没提交）。`--uicheck` 基线 = **3434 PASS / 0 FAIL**（Debug，V0.2.153）。
> 用户原话：「能不能加一个播放音频的效果在效果种类里」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **查引擎怎么放声音** | 本体所有声音走 **FMOD**：`SfxCmd.Play(string sfx, float volume = 1f)`（`CreatureCmd` 里 block_gain / heal / PlayDamage… 都是这么放的）。⇒ 生成一句 `SfxCmd.Play("event:/…", 音量)` 就够，**不需要任何新素材** | ✅ 已查证 |
| ② | **新种类 + 字段** | `EffectKinds` 新增 `PlaySfx`「播放音效（本体音效库）」（Unit = %音量，0~200）；`EffectSpec` 新增 `Sfx`（事件路径）+ `IsPlaySfx` / `SfxZh` / `SfxVolumePercent`（**留空 / 0 = 100%**）；`Kind` setter 补 `Raise`。⚠ 它是**唯一一个「0 有意义」**的种类，所以**不列进** `AmountIsStrength`（列进去「数值 0 = 不生效」会把整条丢掉），但要列进 `NoDynamicVarKind`（不声明动态变量，否则兜底的 `new DynamicVar("Value", …)` 会和别的效果撞名 → 开局崩）+ `NextTurnMeaningless`（没有「延迟放声音」这回事） | ✅ 已发 |
| ③ | **候选表（55 条，带中文名）** | `EffectCatalog.SfxChoices`：本体源码里**真实调用过**的 362 个非模板事件里挑出常用的一批（攻击 / 格挡 / 治疗 / 增益 / 减益 / 能量 / 金币 / 卡牌打出与变化 / 遗物 / 附魔 / 宝箱 / 地图 / 界面 / 伙伴 / 各角色招牌音 / 4 首音乐）。**不列全 362 个**的原因：本体 `banks/desktop/*.bank` 里存的是事件 **GUID**（把 42MB 的 bank 全文扫过，一个 `event:/` 都没有），全清单只能从源码拿。界面用 SearchComboBox：**候选外可直接手填任意 `event:/…`** | ✅ 已发 |
| ④ | **四条链都能放** | `CSharpCodeGen.EmitPlaySfx`（一份实现）：写标记 `// CET:Sfx=… CET:SfxVolume=…` + `SfxCmd.Play(路径, 音量f)`（默认 1f / 150% → 1.5f）。卡牌 / 遗物 / 药水三条链各加一个 `case "PlaySfx"`，自定义状态链（`CustomPowerGen`）复用同一个发射器 | ✅ 已发 |
| ⑤ | **文案 + 校验 + 回读** | 三处描述（卡面 `LocalizationGen` / 状态 `PowerTriggers.EffectText` / 列表 `EffectSpec.Display`）写「播放音效：中文名（音量 N%）」；顺手修掉状态描述里跟着印的「（对指定敌人）」。校验器：没选 = 错误、不像 `event:/…` = 错误、不在候选表 = **提示**（不拦）、>200% = 警告。回读：`ProjectRecovery` 认 `CET:Sfx=` 标记（+ 把 `SfxCmd.Play(` 那一句归到「整段跳过」白名单） | ✅ 已发 |
| ⑥ | **自检 13 条 + 全量重跑** | 种类 / 候选表（全 `event:/`、含 sfx+music）/ 0=100% 且 `IsInertZero` == false / 卡牌生成 `1f` 与 `1.5f` / 没选音效只写注释 / 卡面描述 / 状态触发 + 状态描述 / 遗物 / 药水 / 校验器三条 / 界面行显隐 / **完整「生成 → 回读」认出 `PlaySfx=event:/sfx/heal@150`**（挂在已有的复制探针卡上，不额外跑一次生成）。自检还逼出两处必须同步的清单：`EffectFieldPolicy.UsesUpgradeAmount`（播放音效没有可升级数值 → 第 24 种）和「下回合生效」那条从 8 种变 9 种 | ✅ 已发 |
| ⑦ | **没做：放「你自己的音频文件」** | 自建 FMOD 事件需要 bank，而 bank 里只有 GUID、模组没法凭路径注册新事件 ⇒ 只能绕开 FMOD 用 Godot `AudioStreamPlayer` 加载 `res://` 音频，那要新加「上传音频素材 → 打进模组 → Godot 导入」一整套。**等用户拍板再做**（说清工程量） | ⏸ 待拍板 |

> **红线实测数字（第 39 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3447 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `.sln` 随机 GUID）—— 新种类不掺进任何现存效果的生成路径；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.154 -NoRelease -NoCommit -Yes`。

## 第 38 批需求（V0.2.153：**点构建后「目标卡」里的东西会变**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~38 批都还没提交）。`--uicheck` 基线 = **3431 PASS / 0 FAIL**（Debug，V0.2.152）。
> 用户原话：「为什么我点击构建后，目标卡里的东西会变」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **查真因** | 自动英文类名是**按列表行号**算的（`<角色>Card3` = 普通卡列表第 3 张；诅咒 / 先古各自一套前缀各自数），而效果里「目标卡」`SpawnCardId`、「转化为先古卡」类名清单、上传卡面 `Art.CardPortraits`、锻造图【效果】字段存的都是**类名字符串**。用户先选目标卡 → 之后增删 / 拖动过卡牌 → 行号变、自动名集体改号 → **改号那一刻引用没搬** → 字符串落到同名的另一张卡身上（自检里复现：老名字 `SevenCard3` 的新主人是插进来的那张「挪卡2」）。构建时 `Freeze` 才把类名写实，那时旧名已经丢了 → 用户看到「目标卡变了」 | ✅ 已查证 |
| ② | **界面里同步搬** | `RefreshCardChoices()`（每次重算候选表 = 增删 / 拖动 / 复制 / 撤回 / 改名之后都会走）开头新增 `SyncCardClassReferences()` → 调生成端那一步 `NameStabilizer.RenumberCardNames(_profile)`（幂等），搬过什么写进「构建 · 日志」（只有真搬了才写） | ✅ 已发 |
| ③ | **构建时「先搬再冻」** | `NameStabilizer.Stabilize`：`RepairReferences`（按类名表认名字）→ **`RenumberCardNames`（先搬引用）** → `Freeze`（再写实类名）。顺序是 `RenumberCardNames` 自己的注释里就写着的「必须排在 Freeze 之前」，以前漏了这一步 | ✅ 已发 |
| ④ | **补上「留空类名」那一支的旧名映射（关键）** | `Naming.RenumberCardClassNames`：以前对**没填类名**的卡只更新 `LastAutoOrdinal`（第几号），**不产出 `旧名 → 新名` 映射** → 最常见的这种卡一改号就搬不动。现在按 `LastAutoOrdinal`（上一次的号）拼出旧名，写进 `renamedOut`；`LastAutoOrdinal <= 0`（新建的卡）时跳过，不产生 `…Card0` 这种假映射。字段是 `[JsonIgnore]`，界面每次刷新候选表（含载入存档）都会被写实 → 开着存档改列表也准 | ✅ 已发 |
| ⑤ | **自检（3 条，含反向）** | ① 3 张卡、目标卡指向第 3 张 → 中间插一张 → 引用必须搬到**同一张卡的新编号**上（`SevenCard3 → SevenCard4`）；② **反向**断言老名字现在属于另一张卡（把用户看到的现象钉死）；③ 构建那条路（`Stabilize` → `Freeze`）后引用与冻出来的类名一致。**反向验证**：临时关掉 ④ → 3 条 FAIL（引用停在 `SevenCard3`，主人变成「挪卡2」），打开即 0 FAIL | ✅ 已发 |

> **红线实测数字（第 38 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3434 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `.sln` 随机 GUID）；
> ④ **反向验证** 3 条 FAIL → 0 FAIL；⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.153 -NoRelease -NoCommit -Yes`。

## 第 37 批需求（V0.2.152：**「某个状态层数到达 N 层时」的 N 界面上根本填不了**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~37 批都还没提交）。`--uicheck` 基线 = **3422 PASS / 0 FAIL**（Debug，V0.2.151）。
> 用户原话：「为什么我状态层数达到了，却没生成卡牌 / 自定义状态的某个层数到达N时，在游戏中怎么也是N，
> 而不是填的数」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **复现（读生成源码）** | 新增开发探针 `forge --powerprobe <out.cs>`：造「层数到达 N → 生成卡牌 + 造成伤害」的自定义状态，把生成出来的状态类源码 + 自动描述写出来。实测真因：`CSharpCodeGen.TriggerSupportsAmount` **漏了 `PowerReached`** → `ShowTriggerAmountField`/`ShowAmountField` 全 false → **界面没有 N 输入框** → `TriggerAmount` 永远是默认值 **1** → 生成的是「跨过 **1** 层那一下触发」（`power.Amount >= 1 && power.Amount - amount < 1`），用户想让它在 N 层触发就永远等不到（①）；描述里印的是下拉菜单那个**字母 N**（②）。另外生成守卫无条件带 `power != this` → 「盯自己这只状态」永远不成立 | ✅ 已查证 |
| ② | **补上 N 输入框 + 说清语义** | `TriggerSupportsAmount` 加 `"PowerReached"`（**不算**计数族：`TriggerAmountNeedsScope` 用的是 `CountExpr`，所以不会误显示「本回合 / 本场战斗」）；两侧 `TriggerAmountLabel`/`AmountLabel` 都补「到达 N 层（层数跨过这一层那一下触发）」（遗物那边本来就有这条 case，是死代码）；`TriggerSpendHint`/`SpendHint` 补「填 N = 层数**跨过** N 那一下才触发 + 现在填的是几层」 | ✅ 已发 |
| ③ | **描述里写出数字** | `PowerTriggers.TriggerLine`：`PowerReached` 不再用 `Find(kind).Display`（那个写死了字母 N），改成 `某个状态层数到达 {N} 层时`；并把「盯：自己的××状态」也一起写出来（和「层数变化后 / 被移除时」一个口径）。界面那一行与游戏里的状态描述共用它 | ✅ 已发 |
| ④ | **「盯自己这只状态」放行** | `CustomPowerGen` 的 `PowerReached` 守卫：新增 `SelfClassName(p)`（按生成期的 `_powerOwner` 反查类名），**显式盯到自己**时去掉 `power != this`；留空（任意状态）/ 盯别人时照旧排除自己。死循环风险由「跨过 N 那一下」判据本身挡住（4 层时 4-1=3 不 < 3） | ✅ 已发 |
| ⑤ | **顺手把「数值怎么变成层数了」讲清** | 自定义状态页那三个勾选框下面那行灰字补一句：勾了「数值 = 本状态的层数」之后，效果里的「数值」框填什么都不用（用触发那一刻的当前层数） | ✅ 已发 |
| ⑥ | **自检（9 条）+ 修回归** | 断言：这一档有 N 框（两侧）/ 标题与说明写清「跨过」/ 描述里是「5 层」而不是「N 层」+ 带「盯：」/ 生成守卫 `power.Amount >= 5m && - amount < 5m` / 盯自己时无 `power != this` / 留空时仍有 / 守卫与回读正则对得上。**踩到的回归**：`TriggerSupportsAmount` 一改，遗物那条路会为 `PowerReached` 多写一行 `CET:TriggerAmount` → 多时机（全部满足 / 任一满足）的段落切分多切一条 → 自检「回读 All 那只 2 条时机」变 3 条。两处标记条件都改成 `… && id != "PowerReached"`（它的 N 由 `CET:PowerReach=` 记着）后恢复绿 | ✅ 已发 |

> **红线实测数字（第 37 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3431 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `.sln` 随机 GUID）；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.152 -NoRelease -NoCommit -Yes`。

## 第 36 批需求（V0.2.151：**效果栏第二列被挤成一条缝 / 次数公式不见了 / 放着卡牌没反应 / 先古卡选不了范围**，用户实测 + 截图；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~36 批都还没提交）。`--uicheck` 基线 = **3417 PASS / 0 FAIL**（Debug，V0.2.150）。
> 用户原话：「次数公式怎么不见了 / 转变为先古卡和先古遗物怎么选不了范围 / 放置卡牌效果怎么没反应 /
> 有一部分的UI有问题，会变得如图所示」（附截图：第二列只剩一条缝）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **先复现（不许猜）** | 新增开发用观感截图开关 `--uishot <dir>`（`MainWindow.ShotEffectEditors` + `App.xaml.cs`）：把 6 种代表性效果栏 + 两个先古编辑器渲染成 PNG（不参与生成、不写存档）。截图 + 自检实测版式确认：效果栏第一列是 `Width="Auto"` 的标签列、第二列是 `*` 的输入列；`SelectPileLabel` 那句 34 字的标签把**标签列撑到 570px、输入列剩 0px** → 整列输入框变一条缝 | ✅ 已查证 |
| ② | **修版式（= 用户 ①③④ 三条的真因）** | `SelectPileLabel` / 「选牌方式」标签收短（「从哪里选牌」/「从哪里丢弃」/「从哪里升级」/「选牌方式」）；长说明**本来就**在那一行 ComboBox 的 ToolTip 里，一个字没丢。修后实测：标签列 108px / 输入列 275px | ✅ 已发 |
| ③ | **修「转化为先古卡」的范围（用户 ②）** | 配对表左边的 `AncientGroupChoices` 以前是 `GroupOnlyFiltersFor`（**只列自定义「组」**）→ 没建组就是空下拉。生成端 `EmitAncientTranscend` 用的 `SpawnPoolCond` 本来就认「范围限定」那一整套 → 界面改成 `SpawnFiltersFor` 去掉「不限」（会全换掉，危险），于是内置 攻击 / 技能 / 能力 / 诅咒 / 状态 直接可选；`AncientRowHint` 文案同步 | ✅ 已发 |
| ④ | **自检守卫（不许再回来）** | 新增 5 条：① 所有种类的 `SelectPileLabel` ≤ 8 字；②③ 卡牌页真的挂上效果栏后**实测**那个两列 Grid（找 `DataContext==效果 && 两列 && 17 行`）：标签列 ≤ 360、输入列 ≥ 140；④⑤ 先古卡范围下拉必须含内置五种类型、且不含「不限」。反向验证：长标签改回 → **2 条 FAIL（标签列=570 / 输入列=0）** | ✅ 已发 |

> **红线实测数字（第 36 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3422 PASS / 0 FAIL**（发布版 / 部署版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `.sln` 随机 GUID）—— 本批只改界面文案 / 候选表，不动生成端；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.151 -NoRelease -NoCommit -Yes`。

> **待用户确认**：「转化为先古遗物」的「要换掉哪个遗物」下拉在用户机器上是不是也是空的（候选来自本体遗物类名表，
> 只有读不到解包工程 / 游戏目录时才会为空）—— 是的话把截图发来。

## 第 35 批需求（V0.2.150：**卡牌页效果栏按种类裁剪**，用户点名「其他每一条效果都要问过我」；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~35 批都还没提交）。`--uicheck` 基线 = **3374 PASS / 0 FAIL**（Debug）。
> 用户原话：「卡牌页右边的效果栏做的太差了，能不能优化一下，像造成伤害这个效果，只需要数值（公式），
> 升级增量，作用对象，命中次数（公式），生效次数（公式），概率（公式），下回合生效，随机目标允许攻击同个敌人，
> 其他的都可以隐藏掉。其他每一条效果你都需要询问我每一个需要保留什么」。
> 分组问答：8 组（造成伤害 / 数值-状态 / 资源-牌堆 / 生成-变化 / 选牌处理 / 药水-奖励-卡牌独有 / 伙伴 / 刮削）
> 用户**全部选「按推荐」**；实现时以**机械测量**为准（措辞是概略的，实测更细）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **把「用得上」量出来（不许猜）** | 新增 `Core/Effects/EffectFieldUsage.cs`：65 种效果 × 3 条链 × 47 个栏位，每栏「改成特征值 → 重新生成这条效果的代码 → 与基线逐字符比对」（`// CET:` 标记行整行删掉，否则每个栏位都算「读了」）。命令行入口 `forge --fieldmatrix <out.tsv>`。三个坑写进类注释：① 基线必须进得了正常分支（「给予关键词」不选关键词、「获得药水」不选药水会走早退分支 → 别的栏位全测不出来）；② 测「作用对象」要用 `Self` 而不是 `AllEnemies`（遗物链上这两档生成同一句，会误判成「没读」）；③ 附加处理那几栏的特征值不能顺手带上 `SpawnFilter`（差异会来自范围而不是那一栏） | ✅ 已发 |
| ② | **允许清单（唯一一份）** | 新增 `Core/Effects/EffectFieldPolicy.cs`：作用对象 19 种 / 增益减益 6 种 / 目标卡 6 种（遗物链多一个「转化为先古遗物」的目标遗物，按链传 `plainChain`）/ 生成到 1 种 / 选牌方式 12 种 / 升级增量（23 种不读）/ 下回合生效（= `HonorsNextTurn`，不再写第二份）/ 生效次数（2 种不读）/ 取几张 15 种 / 取卡方式+范围限定+多选1+从范围里自己挑 4 种 / 随机允许重复（只看作用对象）。顺带把 `UseCardScopeFilter` 也收进这份清单（原来是写死在 Profile 里） | ✅ 已发 |
| ③ | **接到界面上** | `EffectSpec` 新增 `ShowTargetSideRow` / `ShowPowerRow` / `ShowTargetCardRow` / `ShowSpawnToRow` / `ShowCardPickRow` / `ShowUpgradeAmountRow` / `ShowCountModeRow` / `ShowAllowDuplicatesRow`，并把 `ShowSpawnFilter` / `ShowSpawnPickHint` / `ShowSpawnChosen` / `ShowSpawnChoice` 从 `UsesSpawnOptions` 改挂到新的 `ShowSpawnPickRow`（复制卡牌支持附加处理、但不读取卡方式）；`Kind` / `TargetSide` setter 全部补 `Raise`（换种类时那一行要当场出现 / 收起）。App.xaml 给那 5 行以前**无条件显示**的行补 `Visibility` 绑定。**锻造链一律 `IsForgeChain \|\| 老判据`，一个字都不改** | ✅ 已发 |
| ④ | **顺手修好「有用却摆不出来」** | 测量顺带发现三处「生成端在读、界面藏起来」：`取几张` 少摆 9 种（变化 / 复制 / 重放 / 回合结束自动打出 / 放置 / 给予关键词 / 三种全局牌组）、`多选1` 少摆 3 种（变化 / 全局加牌 / 全局变化）、`选牌方式` 少摆 3 种（三种全局牌组）→ 全部按测量摆出来；`CountIsAny` 跟着改成用 `ShowCountModeRow`（「任意张」的语义和界面一致） | ✅ 已发 |
| ⑤ | **自检（把「显示 = 有用」钉死）** | 新增 46 条：① 「造成伤害」显示的**正好**是用户列的那几行 + 其余全收起 + 随机允许重复只看作用对象；② 卡牌链 65 种 × 13 栏「清单 == 测量」；③ 遗物 / 药水链「不许藏起来但生成端在读」；④ 界面那几行的 `Visibility` 真的绑在那些属性上。踩过的坑：探针效果**必须**挂在一张卡上并置 `IsCardChain = true`，否则「命中 / 生效 / 概率 / 下回合」全测成 false；`范围限定` 与 `范围界定` 共用 `SpawnFilter` 字段（两个入口要并起来算）；收集绑定时要收**所有** `FrameworkElement`（作用对象 / 生成到 / 选牌方式是 TextBlock+ComboBox，没有外层 Grid） | ✅ 已发 |
| ⑥ | **文案 / 文档** | README / 使用说明 / RELEASE_NOTES_V0.2.150 写清「哪些栏位对哪些效果出现」与 `--fieldmatrix` 怎么复现 | ✅ 已发 |

> **红线实测数字（第 35 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3417 PASS / 0 FAIL**（发布版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **111 个文件逐字节一致**（只差 `.sln` 随机 GUID）—— 本批只改界面显隐与 Core 判据，不动生成端；
> ④ **反向验证**：临时把 `Damage` 塞进「增益 / 减益」允许清单 → **2 条 FAIL**（「造成伤害其余栏位全收起」+
> 「卡牌链『增益 / 减益』逐条对 → Damage(摆了但没用)」），改回即 0 FAIL（自检不是空转）；
> ⑤ 已打包发版：`tools\release_all.ps1 -Version V0.2.150 -NoRelease -NoCommit -Yes`，
> 部署版 `--uicheck` **3417 PASS / 0 FAIL**、`Sts2CharForge.Core.dll` SHA256 与本地 Release 一致。

## 第 34 批需求（V0.2.149：**「原本 1 费、升级后 X 费」没效果**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~34 批都还没提交）。`--uicheck` 基线 = **3374 PASS / 0 FAIL**（Debug）。
> 用户原话：「但是升级后的费用怎么没效果了，比如原本1费，升级后X费，实际上升级还是1费」。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **查证：本体到底支持不支持** | 读本体源码确认：X 费不是数字，而是 `CardEnergyCost.CostsX`（打出去花光能量、效果按花掉的量算），它在**第一次读 `EnergyCost` 时**由 `HasEnergyCostX` 捕获**定死**（`_energyCost ??= new CardEnergyCost(this, CanonicalEnergyCost, HasEnergyCostX)`，且 `Canonical = CostsX ? 0 : canonicalCost`）；之后打出 / 扣费 / `ResolveEnergyXValue()` 全看缓存的布尔值。升级能改的只有数字（`EnergyCost.UpgradeBy(差值)`）。⇒ **「数字费用 ↔ X 费」在升级时改不了（两个方向都不行）**；能做的只有「数字 → 数字」与「一开始就 X + 升级后 X+N」。**不是工具这一版改坏的**（该分支一直是「不生成 + 警告」，只是措辞含糊） | ✅ 已查证 |
| ② | **把警告改成说清原因 + 给出能做法的写法** | 「升级后费用」写 `X`（或 `X+N`）而当前费用不是 X 时：警告里点明「**本体做不到「升级后把数字费用变成 X 费」**：X 是卡牌构造时定死的一档（`CardEnergyCost.CostsX` 第一次读 `EnergyCost` 时捕获…），升级只能改数字（`EnergyCost.UpgradeBy`）」+ 两种能做到的写法（① 一开始就 X 费 + 升级后 `X+1`；② 这一栏填数字，例 1 费 → 0 费）。「升级后额外资源量费用」写 `Y` 而当前不是 Y 时同理（`HasStarCostX` 同一套） | ✅ 已发 |
| ③ | **文案** | `CostBoxHint` 补一句「升级改不了『数字费用 ↔ X 费』这一档」 | ✅ 已发 |
| ④ | **自检** | 新增 1 条：`1 费 + 升级后费用 X` → 警告必须含「本体做不到」+「EnergyCost.UpgradeBy」+「X+1」 | ✅ 已发 |

> **红线实测数字（第 34 批）**：① `dotnet build` **0 错误**；② `--uicheck` Debug **3374 PASS / 0 FAIL**（发布版同样）；
> ③ `--samplegen` 与 V0.2.143 基线 **112 个文件逐字节一致**（只差 `.sln` 随机 GUID）—— 本批只改文案 + 文档；
> ④ 已打包发版：`tools\release_all.ps1 -Version V0.2.149 -NoRelease -NoCommit -Yes`。

> **待用户拍板（本批没做）**：「升级后变 X 费」的**实验性**支持（条件覆写 `HasEnergyCostX => base.IsUpgraded`）。
> 两个已知风险：① X 与否在**第一次读 `EnergyCost`** 时捕获 → 牌组 / 奖励界面先读过就定死成非 X（界面上可能仍显示原费用，
> 战斗里的新实例有可能拿到 X）；② 效果里的 X 取值（`ResolveEnergyXValue()`）也要改成「升级后才读」，否则按 0 算。
> 现在的默认行为是「不生成 + 警告」（宁可不生效，也不静默生成半截）。用户说做再做。

## 第 33 批需求（V0.2.148：**「升级后费用」填不了 Y/X、清空后错误还在**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~33 批都还没提交）。`--uicheck` 基线 = **3373 PASS / 0 FAIL**（Debug）。
> 用户原话：「升级后费用里面填不了 Y 和 X，但是填过之后什么都没有也会报错」（附截图：卡牌「新卡」的
> 「升级后额外资源量费用」看不懂：X）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **「填不进去」的真因：框子不认原文** | `CardSpec.UpgradeStarCostText` 的 getter 以前只返回绝对数（`_upgradeStarCost`），填 `Y` / 填错时绝对数是 null → **框子显示成空**（看着像填不进去）。改成和「升级后费用」那一栏同款：原文非空就显示原文 | ✅ 已发 |
| ② | **「清空后错误还在」的真因：清空不清原文** | 那个 setter 的 `text.Length == 0` 分支只清 `UpgradeStarCost`、**没清 `UpgradeStarCostFormula`** → 校验器那句「看不懂」永远消不掉；填数字的分支同样没清原文。两处都补上 | ✅ 已发 |
| ③ | **`Y` 从「报错」改成「合法」** | 「升级后 Y」= 升级后还是 Y，而本体**改不了 X 那一档**（`UpgradeStarCostBy` 碰到 `HasStarCostX` 的牌**直接抛异常**）→ 等价于「升级不改」，生成端本来就不写任何东西。现在填 `Y`：状态是 Y 费牌 → 一句**提示**；不是 Y 费牌 → **警告**（Y 被当 0、不生效） | ✅ 已发 |
| ④ | **`Y+1` 报错要说清原因** | 以前只报「看不懂」；现在报「**填不了 Y+N**：本体 UpgradeStarCostBy 对『额外资源量费用 = X』的牌会直接抛异常（UpgradeStarCostBy called on … which has star cost X.）」并给出替代做法（先把费用从 Y 改成数字） | ✅ 已发 |
| ⑤ | **`Y` 填错栏的指路** | 把 `Y` 填进「升级后费用」（**能量**那一栏）时，报错末尾补一句「这一栏是能量费用，如果你想改的是『升级后额外资源量费用』，请填在右边那一栏」 | ✅ 已发 |
| ⑥ | **文案** | 两个框的 ToolTip 重写（每栏分别能填什么）；`CostBoxHint` 补上「升级后费用：X = 不改 / X+N；升级后资源量费用：Y = 不改、Y+N 本体做不到」 | ✅ 已发 |
| ⑦ | **自检** | 新增 9 条 `V0.2.148` 断言：填 `Y` 框里看得见 / 填 `Y` 不报错且不生成 `UpgradeStarCostBy` / **非法 → 清空后错误必须消失**（用户的 bug 本体）/ 非法 → 改数字后原文一起清掉 / `Y+1` 报错含本体原因 / X 费牌 `X` 与 `X+1` 都过且 X+1 生成里带 `IsUpgraded ? 1 : 0`（X 不带）/ 固定资源量费用的牌填数字照样生成 `UpgradeStarCostBy(2)` / `Y` 填错栏的提示 | ✅ 已发 |

> **红线实测数字（第 33 批）**：① `dotnet build` **0 错误**；
> ② `--uicheck` Debug **3373 PASS / 0 FAIL**（发布版同样 3373 / 0）；
> ③ **红线①默认输出不变**：`--samplegen` 与 V0.2.143 基线 **112 个文件逐字节一致**（只差 `.sln` 随机 GUID）——
> 本批只改「配置检查文案」+「那一栏的显示 / 清空」，没动任何老写法的生成路径；
> ④ 探针实测（`_tmp\v02148`）：Y 费牌 + 升级后 Y → 生成通过、源码无 `UpgradeStarCostBy`；
> 升级后 Y+1 → 报错（含本体异常原文）；`X` 费牌 + 升级后 `X+1` → 生成
> `decimal __X = ForgeFormulaRuntime.Energy(base.Owner) + (base.IsUpgraded ? 1 : 0);`；
> 固定资源量费用 1 → 升级后 3 → 生成 `UpgradeStarCostBy(2);`；
> ⑤ 已按正式流程打包发版：`tools\release_all.ps1 -Version V0.2.148 -NoRelease -NoCommit -Yes`
> （`4\程序文件` 更新、更新包 `Sts2CharForge_更新包_V0.2.148.zip`、没发 GitHub、没有 git 写操作）。

## 第 32 批需求（V0.2.147：**「状态挂在敌人身上时，『自己』是不是敌人自己」**，用户提问；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~32 批都还没提交）。`--uicheck` 基线 = **3363 PASS / 0 FAIL**（Debug）。
> 用户原话：「当状态挂在敌人身上时，状态的自己是不是敌人自己」。
> 答案：**是** —— 状态效果里的「自己」= 挂着它的那个单位（`base.Owner`）；顺带查出一个**会在游戏里抛异常**的缺口。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **回答清楚「自己 / 敌人」是谁** | 本体证据：`PowerModel.Owner` 是 `Creature`（挂着它的单位）；`Creature.Player` **只由玩家单位的构造函数赋值**（怪物 = null）；`CombatState.HittableEnemies` = `Enemies.Where(IsHittable)`（**玩家视角的敌方那一侧**，不随持有者变）。所以贴在敌人身上时：「自己」=那个敌人、「全体敌人」=它自己和它的同伴；玩家侧时机永不触发（既有警告）。README + 使用说明各加一节表格 | ✅ 已发 |
| ② | **新缺口：要拿玩家对象的效果在敌人身上会抛异常** | 状态里凡读 `base.Owner.Player` 的效果（抽牌 / 能量 / 金币 / 额外资源量 / 生成卡牌 / 药水 / 全局改牌组×4 / 升级·消耗·丢弃卡牌 / 给予关键词 / 三处拿牌 / 卡牌奖励 = **18 种**）以及**勾了「概率生效」的任何效果**（掷骰读 `base.Owner.Player.RunState`），贴到敌人身上时 null 传进本体接口 → 运行时异常 → **战斗卡住**（和 V0.2.145 同一类）。新增校验器**警告**（不是错误：状态可以只为自己用），逐条点名 + 给可替代做法 | ✅ 已发 |
| ③ | **清单必须机械可证** | `ProfileValidator.PlayerSidePowerEffectKinds` 是**扫生成源码扫出来的**（每种效果按「最想弹选牌界面」的参数生成一个状态、找没有空值保护的 `base.Owner.Player`；`!=` 比较 / `?.` / `??` 都不算）。自检 7 条：清单内三种真的读 / 清单外三种不读 / 概率档会读 / **清单与生成端逐条一致**（写这条时当场抓出漏掉的 `GiveKeyword`）/ 不多写 / 警告会出现 / 不误报 | ✅ 已发 |

> **红线实测数字（第 32 批）**：① `dotnet build` **0 错误**；
> ② `--uicheck` Debug **3363 PASS / 0 FAIL**（发布版同样 3363 / 0）；
> ③ **红线①默认输出不变**：本批只动「配置检查」+ 文档，生成端一行未改 ——
> `--samplegen` 与 V0.2.143 基线 **112 个文件逐字节一致**（只差 `.sln` 随机 GUID）；
> ④ 已按正式流程打包发版：`tools\release_all.ps1 -Version V0.2.147 -NoRelease -NoCommit -Yes`
> （`4\程序文件` 更新、更新包 `Sts2CharForge_更新包_V0.2.147.zip`、没发 GitHub、没有 git 写操作）。

## 第 31 批需求（V0.2.146：**「数值公式」框里填纯数字被拦住**，用户实测；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（第 29~31 批都还没提交）。`--uicheck` 基线 = **3356 PASS / 0 FAIL**（Debug）。
> 用户原话：「数值公式就不能填纯数值吗」+ 截图：卡牌「新卡」的「生成卡牌」在「数值公式」里填了 `1`，
> 被配置检查报错拦住 → **没法生成 / 构建**（正好卡住第 30 批那个卡死修复的验证）。
> 用户侧不改一行配置也能走：把 `1` 清掉填进左边的「数值」框 —— 但显然该由工具来兜。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **纯常数自动折进固定数值** | 新增 `ProfileFactory.FoldPlainConstantAmountFormulas(p)`：非那 5 种效果（抽 / 生成 / 消耗…）的「数值公式」框里写**纯常数**（`1` / `2+3` / `=1` / `0.5`）→ `Amount = 折出来的值`、`AmountFormula` 清空。三个入口都折：**打开存档**（`Load` → 紧跟在那条「固定数值 → 公式」的反向迁移后面）、**配置检查**（`Validate` 开头，和 `ForgeEffectBridge.SyncGraph` 并排）、**生成**（`ModGenerator.Generate` 再兜一次）。幂等 | ✅ 已发 |
| ② | **判据不另写一套** | 新增 `ForgeNumbers.TryFormulaConst(p, text, out value)`（不看种类，能不能折成常数）；`TryEffectFormulaConst` 改成调它，口径不变 | ✅ 已发 |
| ③ | **不许动的两处** | ① 那 5 种（伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态）上的常数公式**一个字符都不动** —— 它们本来就把公式栏当正牌编辑器（`"6"` 是常态），折了会和 `MigrateAmountsToFormulas` 来回打架；② 「数值 = X」那套（`AmountIsX` / 光秃秃 `X`）也不动（X 是运行时语义，不是常数） | ✅ 已发 |
| ④ | **文案** | 报错那句改成「三种情况」并把「纯数字 / 能折成常数的式子（例：1、2+3）—— 工具会自动折好」写进去；`EffectSpec.AmountFormulaHint`（编辑区底下那行灰字）与 App.xaml 的悬停提示同步 | ✅ 已发 |
| ⑤ | **自检** | 新增 11 条 `V0.2.146` 断言：折成 1 / 不再报那句拦路错误 / **生成源码与「直接填固定数值」逐字节一致**（最硬的判据）/ 幂等 / `2+3` / `=1` / 运行时公式（`M*3`、`HandCount`、`X`）不折 / 那 5 种不动 / `X` 老写法不动 / **存档存读一轮之后已经折好且校验无错** | ✅ 已发 |

> **红线实测数字（第 31 批）**：① `dotnet build` **0 错误**；
> ② `--uicheck` Debug **3356 PASS / 0 FAIL**（发布版同样 3356 / 0）；
> ③ **红线①默认输出不变**：`--samplegen` 与 V0.2.143 基线 **112 个文件逐字节一致**（只差 `.sln` 的随机 GUID）；
> ④ 探针（一张卡写着「生成卡牌 + 数值公式 1」）：`--build` 配置检查无错、生成工程 `dotnet build` **0 错误**、
> 生成出来的那一行是 `CardPileCmd.AddToCombatAndPreview<SevenSlash>(base.Owner.Creature, PileType.Hand, 1, base.Owner);`；
> ⑤ 已按正式流程打包发版：`tools\release_all.ps1 -Version V0.2.146 -NoRelease -NoCommit -Yes`
> （`4\程序文件` 更新、更新包 `Sts2CharForge_更新包_V0.2.146.zip`、没发 GitHub、没有 git 写操作）。

> **顺带发现（本批没改）**：探针回读（`--recover`）时，**自定义状态链的「从某一摞拿牌到手牌」生成代码没被认出来**
> （`CardSelectCmd.FromCombatPile(… new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, …))` ——
> `ProjectRecovery` 那条规则只认 `base.SelectionScreenPrompt` 形态，静态提示形态认不出来）。
> 这是**既有**缺口（不是这两批引入的），影响「从工程恢复存档」时那几个效果会被丢。要不要单独修一版？

## 第 30 批需求（V0.2.145：**自定义状态上「生成卡牌」会让战斗卡死**，用户实测 BUG；**已本地发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`（本批在第 29 批之后，未提交）。`--uicheck` 基线 = **3344 PASS / 0 FAIL**（Debug，含第 29 批）。
> 用户原话：「自定义状态选择生成卡牌会卡死，看看日志和调试日志」。
> **本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**。
> 红线①照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；
> 一切验证只用 `ProfileFactory.Sample()` + `_tmp` 自造探针（探针只用 `--build`，**绝不 `--install`**）。
> 本批**只读了游戏日志**（`%APPDATA%\SlayTheSpire2\logs\godot.log`，用户明确要求「看看日志」），
> 没有碰用户的存档 JSON 与游戏安装目录（安装目录只是用 `forge --detect` 拿到路径喂给探针）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **诊断（日志实锤）** | 游戏日志尾部：`[ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck until the room is restarted: System.InvalidOperationException: No selection screen prompt for POWER.SPARKLE_POWER6.` → 调用栈 `PowerModel.get_SelectionScreenPrompt()` ← `SparklePower6.AfterPlayerTurnStart(...)`；`[模组调试]` 那几行显示这张状态是被卡牌 `SparkleCard43` 施加的（触发时机 = 玩家回合开始时）。真因：状态的「生成卡牌 + 按范围随机 + 多选1(>3) / 从范围里自己挑一张」生成出来的代码读 `base.SelectionScreenPrompt`，而本体 `PowerModel.SelectionScreenPrompt` **键不存在直接抛异常**（和 `CardModel` 同一份实现），工具只给卡牌 / 遗物 / 药水写了这条本地化、**自定义状态这条链从来没写** → 抛在战斗回合钩子里 → 本轮 turn loop 死掉、战斗卡住（只有重开房间能恢复） | ✅ 已定位 |
| ② | **生成端修** | `LocalizationGen.PowersJson`：自定义状态的效果里只要有「要弹选牌界面」的就补一条 `<状态>.selectionScreenPrompt` | ✅ 待发 |
| ③ | **补上漏掉的第二半** | `NeedsSelectPrompt` 加「生成卡牌 + **从范围里自己挑一张**」（候选 > 3 时走的也是那条网格界面，以前连名单都没进）；`SelectPromptText` 加对应提示语「选择要生成的牌」 | ✅ 待发 |
| ④ | **顺带补锻造图** | 锻造卡 / 锻造药水 / 锻造遗物的效果在**节点图上**（`XxxSpec.Effects` 是空的）→ 新增 `LocalizationGen.GraphEffects(graph)`，三处判断都把图上的效果算进来（图上「从某一摞拿牌到手牌 / 预见」同样会卡死） | ✅ 待发 |
| ⑤ | **自检** | 新增 10 条 `V0.2.145` 断言：状态两种档位（多选1=4 / 自己挑）都写出 `<状态>.selectionScreenPrompt` + 生成代码确实读 `base.SelectionScreenPrompt` + 提示语正确 + **对照**（不带选牌界面的状态一个字都不多写）+ 锻造卡图上「从弃牌堆拿牌」也写出 `<卡>.selectionScreenPrompt` | ✅ 待发 |
| ⑥ | **第二遍「机械扫描」→ 又抓出一个漏网之鱼** | 用户第二次说「还是卡死」时复查：先把「会读 `base.SelectionScreenPrompt` 的效果种类」**按种类机械扫一遍**（每种效果都按「最想弹选牌界面」的参数配，分别生成**卡牌链 + 自定义状态链**源码，扫关键字）→ 断言「扫到的每一种 `NeedsSelectPrompt` 都必须认得」。这条断言当场抓出漏网的 **`PlaceCard`（放置卡牌 + 取牌方式 = 自己选）**——它在**卡牌链**上也会读 `base.SelectionScreenPrompt`（`SelectPrefsExpr`），漏了就是「打出这张牌 = 游戏卡死」。已补进 `NeedsSelectPrompt` + `SelectPromptText`（「选择要移动的牌」）。扫描共 12 处：`GenerateCard`×2 链、`CopyCard`、`ReplayCard`、`TurnEndPlay`、`Scry`、`TakeFromDraw/Discard/Exhaust`、`PlaceCard`、`GiveKeyword`×2 链 | ✅ 待发 |
| ⑦ | **「还是卡死」的真因 = 上一轮没打包** | 复查游戏日志时间线 + 程序时间戳：用户跑的是 `D:\ds\s\4\程序文件\Sts2CharForge.exe`，时间戳 **10/10 23:31**（V0.2.143 那版），而我上一轮只改了仓库、**没有打包发版** → 他用旧程序重新生成的模组依旧缺那条键。本轮按正式流程打包：`tools\release_all.ps1 -Version V0.2.145 -NoRelease -NoCommit -Yes` → `4\程序文件` 更新成 **V0.2.145**（用户数据 1020 个文件原样放回、旧启动器删除）、更新包 `Sts2CharForge_更新包_V0.2.145.zip`（60.6 MB）、旧包按策略清理；**没发 GitHub、没有 git 写操作**（`-NoRelease -NoCommit`）。核对：部署进去的 `Sts2CharForge.Core.dll` 与本机 Release 构建 **SHA256 完全相同**，且 dll 里能搜到这次的修复字符串 | ✅ 已发（本地） |

> **红线实测数字（第 30 批）**：① `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；
> ② `--uicheck` Debug **3345 PASS / 0 FAIL**（含那条「机械扫描」断言）、发布版（正式 `dotnet publish -c Release -r win-x64`）**3345 PASS / 0 FAIL**；
> ③ **红线①（默认输出不变）**：`--samplegen` 与 V0.2.143 的基线（`D:\ds\s\_tmp\v02143main\gen\ExampleMod`）对拍
> **112 个文件逐文件 SHA256 一致，只差 `ExampleMod.sln` 的随机 GUID**；
> ④ **反向验证**：把 `PowersJson` 那两行临时注释掉重跑 `--uicheck` → **3 条 FAIL**（正是新加的那三条），改回即 0 FAIL；
> ⑤ **探针真编译**（`_tmp\v02145\probe.json`，三个状态：多选1=4 / 从范围自己挑 / 从某一摞拿牌）：
> `forge --profile … --build` → 生成工程 `dotnet build` **0 错误**（PCK 那一步因为探针没填 Godot 路径而跳过，**没有安装任何东西**），
> 生成的 `localization/zhs/powers.json` 三条 `selectionScreenPrompt` 都在、状态源码里读的正是 `base.SelectionScreenPrompt`；
> ⑥ 文档同步：`README.md`（新增 V0.2.145 一节，含日志原文与真因；「生成卡牌」那一节改成「四条链都写提示语」）、
> `tools\使用说明.template.txt`（多选1 那一节补上「状态链漏了会卡死、现已修」）。

> **顺带发现（本批没改，等用户拍板）**：同一份日志里还有
> `Missing sprite 'sparkle_power6' in power_atlas` + `Asset not cached: res://images/powers/missing_power.png` ——
> 那个自定义状态**没上传图标**，所以状态栏里是本体那张 `missing_power` 紫色占位。
> 现在生成器只在「上传了图标」时才写图集 `.tres` + `images/powers/<状态>.png` 回退图；
> 要不要在没上传时也写一份回退（像「额外资源量」那只载体遗物用本体星星图标那样）？—— 等用户定。

## 第 29 批需求（V0.2.144：**遗物 / 药水 / 额外资源量右侧效果栏改成卡牌页版式**；**未发版**）

> 派活 HEAD = `13416d8 发版 V0.2.143`；`--uicheck` 基线 = **3339 PASS / 0 FAIL**（Debug，V0.2.143）。
> 用户原话：「遗物药水右侧的效果栏能不能同步卡牌的」。我先列出四档让他拍板
> （**A** 照卡牌页版式重做 / B 只把条件搬进每条效果 / C 反过来让卡牌页也折叠 / D 编辑器里的栏目也同步），
> 用户选 **A**，并且选「**额外资源量页一起改**」。
> **本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**。
> 红线①照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；
> 一切验证只用 `ProfileFactory.Sample()` + `_tmp` 自造探针（探针只用 `--build`，**绝不 `--install`**）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **三页右侧 = 卡牌页版式** | 上面一行**摘要列表**（固定 140px，`DisplayMemberPath="DisplayPlain"` —— 这三页不能升级，摘要里不显示「（升级 +N）」）+ 中间 8px + 下面**一整块共享编辑区**（`DataContext="{Binding SelectedItem, ElementName=XXXEffectList}"`，模板还是 `EffectEditorPlain` / `EffectEditorPotion`，一个字没重写）；**单条效果自己的「条件选项」跟着效果长在编辑区里**（`EffectConditionPlain`，候选表仍是 `PlainEffectConditions`），右下角那块独立条件框撤掉；没选中效果时编辑区内容用 `NotEmptyToVisibility` 收起、位置换成一句指路文字 | ✅ 待发 |
| ② | **删掉折叠行那一套** | `EffectEditorCollapsible` / `EffectRowEditorTemplate` / `OnToggleEffectRow` / `SelectEffectRowOf` / `ListBoxOwningEffect` / `CollectListBoxes` / `ToggleEffectRow` / `EffectSpec.IsExpanded` 全部删除 —— V0.2.143 那个「条件选完存不住」的结构性原因（整行是 Button 吃掉 ListBoxItem 的点击）**从根上没有了** | ✅ 待发 |
| ③ | **顺手修掉改版暴露的两个老毛病** | (a) 编辑器里「多选1 / 复制的份数 / 重放次数 / 回合结束时 / 缓慢」五段提示原本**一行不换行**（实测宽 788~1027px、横向跑出面板被裁掉，卡牌页一直如此，只是没人量过）→ `MaxWidth="320"` + `TextWrapping="Wrap"` 折行；(b) `ContentTemplate` 遇上 `Content=null` **也会把模板实例化**，那些行内 `Visibility` 绑定解不出来就停在默认值 `Visible` → 界面上凭空冒出几行开关（自检抓到「遗物页看见了概率独立」，DataContext 是 null）→ 编辑区内容按选中项收起 | ✅ 待发 |
| ④ | **自检** | 新增 / 改写 **44 条** `V0.2.144` 断言：三页各一组「点行（走 `ListBoxItem.IsSelected`，和鼠标同一条路）→ 选中 → 编辑区 DataContext 跟着换 → 在编辑区改「数值」真的写回模型 → 在编辑区选条件写回 `Condition.Kind` → 切走再切回还在」+「效果行里没有 Button」+「`EffectSpec.IsExpanded` 已删」+ 卡牌页回归（卡牌页本来就是这形状，不该被带到） | ✅ 待发 |

> **红线实测数字（第 29 批）**：① 工具本体 `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；
> ② `--uicheck` Debug **3334 PASS / 0 FAIL**、发布版（正式 `dotnet publish -c Release -r win-x64 --self-contained true` 的全新 exe）**3334 PASS / 0 FAIL**；
> ③ 生成端 / 回读 / 校验器**一行都没改** —— `git diff --stat` 只有 `README.md`、`App.xaml`、`MainWindow.xaml`、
> `MainWindow.xaml.cs`、`Profile.cs`（`Profile.cs` 只删了一个 `[JsonIgnore]` 的纯界面属性 `EffectSpec.IsExpanded`，
> 全仓库 grep 确认没有任何生成端 / 回读代码读它）；
> ④ 文档同步：`README.md`（「效果栏」那一节重写 + 新增 V0.2.144 一节 + V0.2.143 那一节标注「已被换代修法取代」）、
> `tools\使用说明.template.txt`（效果栏 / 条件入口 / 「效果栏太长了」那三处措辞）。

## 第 28 批需求（V0.2.142：**生成调试日志开关**，用户明确要过：「用来排查其它运行期问题」；**未发版**）

> 派活 HEAD = `f99d582 发版 V0.2.141`；`--uicheck` 基线 = **3297 PASS / 0 FAIL**（Debug）。
> 任务书：`_tmp\待发任务书_V0.2.142_生成调试日志开关.md`（**第 0 节的历史背景已过时**：
> 那句「用户报的负值力量在游戏里不生效」已在 V0.2.141 定位并修好 —— 真因是回读把升级增量加了两次，
> 见 `docs\RELEASE_NOTES_V0.2.141.md`；本批**只做调试日志开关**，不再重开那个调查）。
> **本轮不发版、不跑 `release_all.ps1`、没有任何 git 写操作**（连 `stash` / `worktree` 都没有；
> 「改动前逐字节不变」这条用**同一个 `--samplegen` 目录**在改动前后各跑一遍、逐文件 SHA256 对拍，见下面红线①）。
> 红线⑧ 照旧：不读 / 不复制 / 不使用用户存档；不写、也不盘点用户游戏安装目录；一切验证只用 `ProfileFactory.Sample()` +
> `_tmp` 自造探针（探针只用 `--build`，**绝不 `--install`**）。

> **要这个开关的原因**：游戏日志里既没有异常、也**不记「数值前后」、不记你打了哪张牌** ——
> 「填了 -2 层力量在游戏里没反应」这类**运行期**问题根本查不下去。所以要一个**默认关闭**的调试日志开关：
> 打开后每条效果执行时把「谁在跑 / 目标是谁 / 数值前后」写进 Godot 日志（`Godot.GD.Print`）。
> **⚠ 需求编号说明**：任务书原话是「追加『第 26 批』」，但 `docs\QUEUE.md` 里第 26 批已经存在、
> 发版前最新一批是**第 27 批（V0.2.141）** —— 所以本批按顺序记成**第 28 批**（内容与任务书一致）。

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | **存档开关（默认 false，必须进存档）** | `CharacterProfile.DebugLog`（**不加** `[JsonIgnore]`，所以写进存档）。老存档没有这个字段 → 读进来就是 `false` → 行为与以前完全一致。 | ✅ 待发 |
| ② | **界面勾选框 + 说清只影响日志** | 「角色」页倒数第二行：`CheckBox IsChecked="{Binding Profile.DebugLog}"`，文案 **「打开『生成调试日志』（排查用：只影响日志，不影响游戏行为；排查完请关掉）」** + 一行灰字说明（把「执行前 / 执行后层数」「本来没有 / 已经有了」写清楚）。 | ✅ 待发 |
| ③ | **生成端：五条链都插日志（默认关着时一个字都不写）** | `DebugLogGen`（新文件）是**唯一实现处**：`Begin(...)` 用 `using` 把「这一段属于哪张牌 / 遗物 / 药水 / 状态」放进生成期的当前作用域（深处的发射器不用再多收一串参数）；`Target` / `TargetInline` 打「谁 + 种类 + 目标 + 数值」；`PowerBefore` / `PowerAfter` 打「执行前 / 执行后层数」。**开关关着时这些方法一律直接 return**，生成文本与 V0.2.141 逐字节一致。 | ✅ 待发 |
| ④ | **ApplyPower 的「本来没有 / 已经有了」两种情况都要打出来** | `PowerBefore`：`PowerModel? __dbgFixN = (目标).GetPower<T>();` → `null` 时层数记 `-` 并标「本来没有这个状态」，非 null 记 `Amount` 并标「已经有了这个状态」，还打出这次实际施加的层数（生成出来的表达式先赋给临时变量再 `ToString`）；`PowerAfter` 打完再取一次，标「现在有了 / 现在还是没有」。两行**各自单独成行**（回读按行跳过，见 ⑤）。 | ✅ 待发 |
| ⑤ | **回读不能被日志行带坏** | 每条生成的日志行都带 `// CET:DebugLog`；`ProjectRecovery.ParseEffects` 里加一条「**整行**含 `CET:DebugLog` 就跳过」。为什么必须「整行跳」而不是「按标记删片段」：`ApplyPower` 的日志是**拼在原来那条语句前后**的，删片段会把效果本身一起删掉。开了开关才有的产物才受影响；**开关默认关着 ⇒ 老工程一行都没有 ⇒ 反推结果逐字节不变**。 | ✅ 待发 |
| ⑥ | **负值施加状态要说清是哪一档** | 日志里负数会补一句「实际按『施加负层数』执行（这个状态本体允许负层数，本体「尖啸」就是这么写的）」或「实际按『扣除已有层数』执行（这个状态本体不允许负层数，身上没有时什么都不做）」—— 判据与生成端共用 `EffectCatalog.AllowsNegativeLayers`。 | ✅ 待发 |
| ⑦ | **自检新增断言** | 默认 false / 界面上有那个勾选框且文案含「只影响日志」「不影响游戏行为」「排查完请关掉」/ 开关进存档（存一遍读回来还是 true）/ **关 → 文本里没有 `GD.Print`、没有 `CET:DebugLog`** / **开 → 有 `Godot.GD.Print` + `CET:DebugLog`** / 有「执行前层数」「执行后层数」「本来没有这个状态」「已经有了这个状态」/ 写明来源与「（不是自己）」/ 负数说清是哪一档 / 生成 → 真 `dotnet build` 0 错误。 | ✅ 待发 |
| ⑧ | **文档** | `README.md` 新增「生成调试日志（V0.2.142）」一节（含游戏日志里的**原文示例**）；`tools\使用说明.template.txt` 的「环境与构建」FAQ 加一条「游戏里填了数值却没反应怎么查」；本文件即第 28 批。**不**建 `docs\RELEASE_NOTES_V0.2.142.md`、**不**改版本号。 | ✅ 待发 |

> **红线实测数字（第 28 批）** 见实现报告：① `--samplegen` 改动前后 **112 个文件 / 112 行 SHA256，唯一差异 = `ExampleMod.sln` 的随机 GUID**；
> ② 工具本体 `dotnet build Sts2CharForge.sln -c Debug --no-incremental` **0 错误**；
> ③ `--uicheck` Debug / 发布版 0 FAIL（详见报告）；
> ④ 自造探针（一张卡 -2 力量 + 一张卡造成伤害 + 一条自定义状态「受到伤害时回复生命」）开关打开 → `--build` 真 `dotnet build` **0 错误** + 导入 / 导出 PCK 全 OK，**生成的日志行原文贴在报告里**。

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

- V0.2.143 修「遗物 / 药水 / 额外资源量页右下角『单条效果的触发条件』选不中目标效果、改了存不住」
  （用户原话「遗物的条件选项怎么用不了了」，验收口径「做的和卡牌一样就行了」）。
  **真因在 XAML / 事件层**：四页共用的折叠效果行模板 `EffectEditorCollapsible`（`MainWindow.xaml`）
  **整行就是一颗 `Button`**（`Click="OnToggleEffectRow"`）—— WPF 里 Button 在**冒泡阶段**先处理
  `MouseLeftButtonDown` 并置 `e.Handled = true` → 外层 `ListBoxItem` 收不到这次点击 →
  **那一行永远不会被选中**；而右下角那块 `RelicEffectConditionBox` / `PotionEffectConditionBox` /
  `ExtraResourceEffectConditionBox` 的 DataContext 正是 `{Binding SelectedItem, ElementName=…EffectList}`
  → **一直是 null** → 在里面选条件 = 写进空对象、**静默丢失**（切走再回来就变回「无条件」）。
  卡牌页没有这块（条件编辑器在展开区里、绑那一行自己的效果），所以一直正常。
  **修法**：`OnToggleEffectRow` 翻展开状态之后新增一句 `SelectEffectRowOf(sender, effect)`（四页共用）——
  从被点的元素往上找 `ListBoxItem` → `ItemsControlFromItemContainer` 反查它属于哪个 `ListBox` →
  设 `SelectedItem`；找不到 `ListBoxItem` 时退化成「在窗口可视树里找那个 `ItemsSource` 正好装着这条效果的
  列表」（找到多个 / 找不到都安静返回，宁可不选中也不许改错列表）；全都不命中就 `item.IsSelected = true`。
  **自检补上这次漏掉的方向**（以前只测「模型 → 界面」，从没测过「在下列表里选 → 写回存档」）：
  真的点那一行那颗按钮（`RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b))`，和用户点下去落进同一个处理器）
  → 断言 `SelectedItem` **就是这条效果**（修好之前是 null）+ 行容器 `IsSelected` + 右下角那块 DataContext 立刻跟上；
  再在下拉里真的选一个条件 → 断言写回 `EffectSpec.Condition.Kind` → 切走再切回**条件还在**（遗物 / 药水 / 额外资源量各一组）；
  外加「展开区那份与右下角那块绑的是**同一个对象**」+ 卡牌页回归（卡牌页效果列表不是折叠行模板、不误改）。新增 25 条断言。
  **反向验证**：把新加的那句「选中」临时注释掉 → `--uicheck` **11 条 FAIL**（PASS 3328 / FAIL 11）；改回即 **PASS 3339 / FAIL 0**。
  ⚠ 自检里**不要**在设 `SelectedItem` 前后调 `SyncDetail()`：它会重绑 `ItemsSource`，把「右下角绑 SelectedItem」
  那条 Binding 一起换掉（踩过，会假红 / 假绿）。⚠ `ButtonAutomationPeer.Invoke()` 在这条**没有消息循环**的自检路径上
  不触发 Click（实测），要真的走 Click 就用 `RaiseEvent`。
  红线：`--samplegen` 113 个文件逐字节不变（只差 `ExampleMod.sln` 的随机 GUID；`README.md` 只差 CRLF/LF）／
  `dotnet build` 0 错误／`--uicheck` Debug 与发布版都 0 FAIL／探针 `--build` 0 错误 + 导出 PCK OK + `--recover` 0 条「没认出来」。



