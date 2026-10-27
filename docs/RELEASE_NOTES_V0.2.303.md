# V0.2.303 — 「这张卡造成的伤害」等六条读数可以参与四则运算了

用户原话：

> 自定义参数的「这张卡造成的伤害」不能进行 +-*/ 的计算吗

以前写 `ThisCardDamage * 0.5` 会被校验器**报错挡住**（生成直接失败），文案是「只能光秃秃地单独用…生成的代码会编译不过」。

## 先查了一件事：那句「会编译不过」其实不成立

探针（`t3\_tmp\sacprobe` 的 `tcdmul` 模式）现场生成了一张「造成 8 点伤害 → 施加等同于一半伤害的灾厄」的卡，生成出来是：

```csharp
public decimal ForgeThisCardDamage { get; private set; }        // 卡片自己的属性
ForgeThisCardDamage = 0m;                                       // OnPlay 开头清零
if (__atk1 is not null) ForgeThisCardDamage += (decimal)__atk1.Results.SelectMany(hits => hits).Sum(r => r.TotalDamage);
await PowerCmd.Apply<DoomPower>(choiceContext, cardPlay.Target, (((decimal)this.ForgeThisCardDamage) * (0.5m)), base.Owner.Creature, this);
```

* 公式里那条读数是**打出这张牌、跑到这一句时才现算**的（`this.ForgeThisCardDamage`），**不是**方法开头取好的快照局部变量 —— 所以「先造成伤害、再读伤害」的顺序本来就是对的；
* 卡面那个「现算预览值」的静态 lambda 拿不到 `this`，生成端早就退回了普通变量（`new PowerVar<DoomPower>(0m)`），**不会** CS1061；
* `dotnet build` 真编译：**0 错误**（同一个探针跑出来的）。

结论：这条读数**本来就能参与四则运算**，只是校验器按「卡面预览会算错」的理由把它当成了错误、顺手把生成挡住。

## 改动（一处）：校验器从「错误」降为「提示」

`ProfileValidator.ValidateCardReadings`：六条「这一次打出」的读数（`ThisCardDamage` / `ThisCardBlock` / `ThisCardExhaust` / `Discard` / `Transform` / `Generate`）和别的量混着写时，**不再挡住生成**，改成一条提示，把两件真事说清：

* ① **顺序**：请把这条效果排在对应的效果（造成伤害 / 获得格挡 / 消耗 / 丢弃 / 变化 / 生成卡牌）**之后**，否则当场取到的是 0（每次对应效果之后才记账）；
* ② **卡面**：卡面那一栏会印成 0（这条读数没有「现算预览值」）—— 想让人看懂就用这张牌的「自定义描述」自己写句子。

「排在使用它的效果之前」「挂在卡牌时机钩子上」「勾了下回合生效」「写在整张牌条件里」「遗物 / 药水 / 状态的图上用」这几种**真的会静默失效或编不过**的写法，照旧报错拦住，一点没放宽。

## 验收

* 探针（`sacprobe tcdmul`）：`ThisCardDamage * 0.5` → 生成 `(((decimal)this.ForgeThisCardDamage) * (0.5m))`，且**没有**快照变量；`dotnet build` 0 错误。
* 自检：**4463 PASS / 0 FAIL**（比上一版 +7）：
  * 「不再挡住生成」
  * 「生成的是当场取属性的表达式，不是快照局部变量」
  * 「公式里那条读数没被取成方法开头的快照变量」
  * 「卡面预览退回普通变量（没有 ForgeFormulaPowerVar）」
  * 「生成出来的工程真编译得过（dotnet build 0 错误）」
  * 「回读把 `ThisCardDamage * 0.5` 原样认回来」+「回读没认出来的语句为 0」
* 另外三条旧断言口径跟着改（混着写 → 提示，不是错误）：`ThisCardDamage` / `ThisCardBlock` / `ThisCardGenerate`。
* 只出更新包（`-NoBundle`）。

## 说明

生成端**一行代码都没改**（只改了注释），所以老存档生成出来的文本逐字节不变。
