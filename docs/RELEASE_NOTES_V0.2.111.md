# V0.2.111 更新说明

> 这批需求里的第 ⑤ 条：**公式在卡牌上对数值的描述**。你选的是「卡面上直接印着 `M/2` 这种公式文本，
> 玩家看不懂（想要算出来的数字）」—— 这一版就是把它改成**算出来的数字**。

## 一、卡面现在显示「按当前属性算出来的数字」

以前填了「数值公式」的卡，卡面是把手写的公式**原样印出来**：

```
对指定敌人造成 M/2 点伤害。          ← 玩家看不懂 M/2 是什么
```

现在卡面用的是本体的动态变量占位符，游戏会填上**现算的数字**（力量 / 敏捷 / 易伤那些加成照旧叠上去）：

```
对指定敌人造成 24 点伤害。            ← 按「自己的最大生命 ÷ 2」算出来的
```

做法（用到才生成 `cs/FormulaVars.cs`）：

```csharp
internal sealed class ForgeFormulaDamageVar : DamageVar {
    private readonly Func<CardModel, Creature?, decimal> _formula;
    public ForgeFormulaDamageVar(Func<CardModel, Creature?, decimal> formula) : base(0m, ValueProp.Move) { … }
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) {
        if (card.Owner is not null) BaseValue = _formula(card, target);   // ① 公式值当基准
        base.UpdateCardPreview(card, previewMode, target, runGlobalHooks); // ② 本体再叠力量 / 敏捷…
    }
}
```

- 卡面 `{Damage:diff()}` 显示的就是 `PreviewValue`；普通 `DamageVar` 只会拿构造时的固定基准值，
  所以以前那张「公式卡」在卡面上不是没数字就是假数字 —— 现在基准值本身就是公式现算的；
- **5 种支持公式的效果各有一个**：伤害 / 格挡 / 失去生命 / 回复生命 / 施加状态
  （最后一个用泛型 `ForgeFormulaPowerVar<T>`，变量名就是状态类名）；
- **变量名和本体完全一致**（`Damage` / `Block` / `HpLoss` / `Heal` / 状态类名）→ 卡面占位符、
  「升级增量」、回读都不用改；
- 公式里的自定义参数是**内联展开**的（参数在 `OnPlay` 里是局部变量，变量子类拿不到），
  内置只读量（`Turn` / `HandCount`…）、`X` / `Y`、参数语法全都照旧；
- **没用公式的卡一个字节都不变**：`UsesFormulaVars` 为假时**不生成** `cs/FormulaVars.cs`，
  固定数值卡照旧 `new DamageVar(6m, …)`（红线照旧）。
- ⚠ 「次数类公式」（生效次数 / 命中次数 / 复制 / 重放）**还是印公式本身** ——
  次数是打出去那一瞬间才知道的，卡面没有预览机制（文档里写明了）。

## 二、验证

- `--uicheck`：开发版与发布版都是 **2596 PASS / 0 FAIL**（新增 11 条：判据 / 5 个变量子类 /
  名字一致 / 先算基准再走 base / 泛型施加状态 / 红线「没用公式不生成」/ 生成 → 工程里真有那个文件 →
  回读 0 没认出来；另外把两条旧断言改成新口径）；
- 探针新增 `RunFormulaText`（5 种公式效果各一条 + 打印卡面描述）→ 卡面变成
  `对指定敌人造成 {Damage:diff()} 点伤害。` 这类占位符；生成 → **真 `dotnet build` 0 错误**
  （含泛型变量与 `UpdateCardPreview` 覆写）→ 回读 5 条公式原样；整轮探针
  **27 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.111.zip`。

## 三、这批剩余的

| # | 需求 | 状态 |
|---|---|---|
| ① | 锻造页【能量】费用没反映到卡面 / 游戏 | ✅V0.2.108 |
| ② | 触发时机增加「移除某状态时」 | ✅V0.2.109 |
| ④ | 命中次数公式不生效（`2+A`） | ✅V0.2.110 |
| ⑤ | 公式在卡面上对数值的描述 | ✅ 本版 |
| ⑥ | 生成器效果去掉「数值」框、只留公式（四处编辑器；公式留空 = 0） | 下一版 |
| ③ | 额外资源量的隐藏遗物在遗物页列出来 + 「在游戏中隐藏」勾选 | 待做（做法已确认） |
| ⑦ | 无色牌卡面丢失 | 待做 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | 待做 |
| ⑨ | 新效果「放置卡牌」（数值 0 = 自己；否则从五处挑一张放到五处） | 待做（做法已确认） |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 待做（做法已确认） |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
