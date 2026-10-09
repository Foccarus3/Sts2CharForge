# 待办队列（用户要求「全做完，不用一步步报备」）

## 第 11 批需求（用户 2026-10-XX 提「遗物页现在有问题，先把遗物页优化了给我一版」，一条一版）

| # | 需求 | 做法要点 | 状态 |
|---|---|---|---|
| ① | 遗物页：**先修「现在的问题」**，再做「去掉左上角额外资源量那块 + 左侧序号 + 拖动换位置 + 自动类名跟序号走 + 稀有度中文 + 中间底下触发时机列表/自定义描述 + 右侧效果栏 + 右下角单条效果条件」 | **根因**：V0.2.122 把「整块效果编辑器」塞进了 `RelicEffectList` 的 `ItemTemplate`，而 ListBox 默认允许横向滚动 → 虚拟化面板按**无限宽度**量每一行 → 行里那个 `EffectEditorPlain` 的 ScrollViewer 把内容排成自然宽度（实测 **8260px**）→ 每行 507px 高、视口只有 160px + 一条横向滚动条，效果编辑器（含底下的「条件」）被切掉。**修**：`RelicEffectList` / `RelicTriggerList` 加 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`（+ `HorizontalContentAlignment="Stretch"`），行宽夹回 414。**布局重排**：中间 = 属性 + 触发时机列表/该条编辑器 + 自定义描述（两栏）+ 整只遗物条件；右侧 = 效果栏 + 「单条效果的触发条件」；额外资源量那块整块搬回「额外资源量/状态」页底部。**新模型**：`RelicSpec.Ordinal`（`[JsonIgnore]`，位置序号，拖动/增删后重算）、`RelicSpec.LastAutoOrdinal`（`[JsonIgnore]`，自动名对应第几号）、`Naming.RenumberRelicClassNames`（生成前跑：手填的不动、自动名跟序号走、没填的**类名保持留空**只记序号 —— 一写实就再也分不出手打的和工具生成的）、`EffectCatalog.RelicRarityOptions`（Id 英文 / Display 中文）。**自检**：序号 1..N 跟位置变、拖动后顺序真变、自动名跟序号走 + 手填不动 + 幂等、稀有度中文、中间底下触发时机列表/自定义描述、右侧效果栏 + 右下角条件、行宽 <= 列表宽、无 0 尺寸/跑出面板 | ✅ 本轮（`--uicheck` Debug 2806/0、发布版 2806/0 FAIL；`dotnet build` 0 错误；`ProfileFactory.Sample()` 生成 114 个文件只有 `.sln` 的随机 GUID 不同、其余逐文件 SHA256 一致）。药水页照抄 + 额外资源量页那条留到下一版 |

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
