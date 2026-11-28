# V0.3.142 —— 召唤伙伴 +「次数 = 全部」时漏声明 `PetHp`，生成被自检拦住

> 用户转来的弹窗：
> ```
> 生成被攔住了…：引用了**没有声明**的动态变量：Patient → PetHp
> ```

## 复现（用他提供的存档 `ExampleMod (1).json`）

卡「布阵」`ClassName=Patient`，两条效果：

| # | 效果 | 数值 | 升级增量 | 次数 / 取几张 |
|---|---|---|---|---|
| 1 | 召唤伙伴 `SummonPet` | 2 | 1 | **全部**（`CountMode=All`） |
| 2 | 获得格挡 `Block` | 公式 8 | 5 | — |

生成出来的 `CanonicalVars` 只有格挡那一个变量：

```csharp
protected override IEnumerable<DynamicVar> CanonicalVars =>
[
    new ForgeFormulaBlockVar(8m, static (card, target) => (8m), 5m),
];
```

但召唤那一行仍然按变量取值：

```csharp
await SevenPetCmd.Summon(choiceContext, base.Owner, base.DynamicVars["PetHp"].BaseValue); // CET:PetHp=var
```

→ `PetHp` 没声明却被引用，生成后自检拦住（不拦的话，游戏里一打出这张牌就抛 `KeyNotFoundException`，
表现是牌组界面打不开 / 卡面空白）。

## 真因

`CSharpCodeGen.HasNoDynamicVar` 里那条：

```csharp
// 「取任意张 / 全部」：没有张数这个数字可显示，所以不声明变量
|| e.CountHasNoNumber
```

它是给**数量型**效果（复制几张、重放几次……）用的，但**没有排除「召唤伙伴」这类「数值 = 血量 / 伤害」的效果**。
于是卡上把「次数 / 取几张」选成「全部」时，整条效果的变量声明被跳过 —— 召唤的 `PetHp` 就这样消失了。

## 修法

新增 `HasPetValueVar(kind)`（召唤伙伴 / 伙伴攻击 / 按最大·当前·已损生命算的三种攻击 / 治疗 / 失去生命 /
最大生命 / 牺牲伙伴 / 给伙伴施加状态），把那条判断改成：

```csharp
|| (e.CountHasNoNumber && !HasPetValueVar(e.Kind))
```

## 验证

- **用他的存档全量扫描**（18 张卡逐个生成 + 自检）：
  - 修复前：`Patient「布阵」→ 漏声明：PetHp`（1 张）
  - 修复后：**0 张有问题**；`Patient.cs` 的 `CanonicalVars` 里已经出现 `new DynamicVar("PetHp", 2m)`
- `--uicheck` **5020 PASS / 0 FAIL**，新增 3 条：
  - 召唤伙伴 +「次数 = 全部」→ `PetHp` 仍然声明、自检为空
  - 伙伴攻击 +「次数 = 全部」→ `PetDamage` 同样正常
  - 红线：真正的数量型效果（复制卡牌 +「取全部」）**仍然不声明**数字变量（不会多出一个鬼变量）

## 用户侧

不需要改配置：升级到本版后重新「生成工程 / 一键构建」即可。
（如果急着用旧版：把那条召唤伙伴的「次数 / 取几张」从「全部」改成固定数字，或把召唤数值填 0 用召唤物页的血量。）

## 版本

- `V0.3.142`
