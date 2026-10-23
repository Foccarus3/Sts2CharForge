# V0.2.221 —— 锻造图里的「只对这张卡 / 范围界定」现在真的生效了（消耗 / 丢弃 / 升级 等）

> 来源：用户实测报的「消耗丢弃升级指定卡没生效」。

## 1. 真因：锻造图那几档**自己拼**选牌语句，一个过滤谓词都没传

卡牌 / 遗物 / 药水 / 状态那四条链的「消耗 / 丢弃 / 升级 / 拿牌 / 变化 / 复制 / 预见」都调
`CSharpCodeGen` 里同一份发射器（`EmitExhaustCard` / `EmitDiscardCard` / `EmitUpgradeCard` /
`EmitTakeFromPile` / `EmitTransformCard` / `EmitScry`），那几份一直会读
「范围界定（`SpawnFilter`）+ 只对这张卡（`ScopeCardId`）」。

但**锻造节点图**这条链（`ForgeGraphGen.EmitEffect`）里这几档是**内联另写**的：

```csharp
// 以前（锻造图）：
var __fgPick = (await CardSelectCmd.FromHand(context: choiceContext, player: __fgPlayer,
    prefs: …, filter: null, source: this)).ToList();            // ← filter 写死 null
var __fgPick = (… CardPile.GetCards(__fgPlayer, PileType.Hand)
    .OrderBy(…).Take(N).ToList());                              // ← 随机那条路没有 .Where
foreach (CardModel __fgPick in (CardPile.GetCards(…)).ToList())  // ← 「全部」那条路也没有
```

【效果】模块用的是**和卡牌页同一份效果编辑器**，所以「范围界定 / 只对这张卡（指定）」两栏照常显示、
照常能填 —— 只是生成端压根没读 → **填了等于没填**（静默失效，日志里也没有任何报错）。

## 2. 修法：全部改走和卡牌页**同一份**判据

`CSharpCodeGen.CardScopePredicate`（`c => (范围界定) && (只对这张卡)`，两者是「与」关系）
现在也传给锻造图这六处：

| 图上的【效果】 | 现在生成 |
| --- | --- |
| 消耗卡牌 / 丢弃卡牌 / 升级卡牌 | 自己选 → `filter: c => …`；随机 / 牌顶 → 候选后面 `.Where(c => …)`；全部 → `(… .Where(c => …)).ToList()` |
| 从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌 | `FromCombatPile(…, c => …)` / `CardPile.GetCards(…).Where(…)` |
| 复制卡牌 | 同上（选「哪几张来复制」） |
| 变化卡牌 | 用**变化那一套**判据（`TransformSourcePredicate`：「只变化这张卡」优先，其次「变化哪些牌（范围）」） |
| 预见 | 抽牌堆顶那 N 张先 `.Where(c => …)`，只看得到符合条件的牌 |

- **没设范围时输出与以前逐字一致**（`GraphWhere(null)` / `GraphFilterArg(null)` 都返回空串，老图不变）；
- 升级那条路原本还自带「只能升级的牌」过滤，图上照旧（`c.IsUpgradable` 只在卡牌页那侧拼，图上维持原样）。

## 3. 顺带补上的两条「不许静默坏掉」

1. **校验器**：`只对这张卡（指定）` 存的是**卡牌类名**，生成出来是
   `c.Id == ModelDb.Card<类名>().Id` —— 类名认不出来（手改存档 / 那张卡被删了 / 换了机器类名表没跟过来）
   以前会一路生成到编译那一步（`CS0234/CS0246`，整个模组装不上）。现在生成前**直接报错**并指路
   「请到『只对这张卡（指定）』里重新选一张」（卡牌页 + 锻造图两条路都查）；
2. 探针里用了一个本作不存在的卡名（`Strike`）复现出了上面那个编译错误 —— 顺手也把自检里
   那两处旧用例换成真实存在的卡名（`SevenCurse`），免得误报。

## 4. 验收

- 自检新增 15 条：图上「消耗 / 丢弃 / 升级」三档（填了 → 真的筛；没填 → 老文本），
  随机 / 全部 / 拿牌 / 复制 / 变化 / 预见六条路各自真的筛；卡牌页三档仍筛（3 处，钉住不回退）；
  校验器「类名认不出来 → 报错」；
- 探针 `sacprobe onlycard`：卡牌页 + 锻造图（消耗 / 丢弃 / 升级 / 拿牌 / 复制 / 预见 + 只对这张卡）
  生成出来的工程 `dotnet build --no-incremental` **0 error**；
- 老存档逐文件对拍（新 Core ↔ 已发布 V0.2.220 的 Core，同一份输入各生成一遍）：
  示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的 GUID**（这两份存档的图上没设范围，所以不受影响）；
- `--uicheck`：**4116 PASS / 0 FAIL**。

## 5. 版本

- `V0.2.221`（用这一版重新生成一次工程即可；图上【效果】的「范围界定 / 只对这张卡」现在会真的作用到选牌上）
