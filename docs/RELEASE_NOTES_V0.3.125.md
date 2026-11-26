# V0.3.125 — 抽牌变量残留 `Amount=0 + 公式=2` 时卡面显示 0

## 存档里的状态

`D2.json` 的「离子火花」：

```json
"Kind": "Draw",
"Amount": 0,
"UpgradeAmount": 1,
"AmountFormula": "2"
```

自定义描述：

```text
抽{Cards:diff()}张牌。
```

`Amount` 是 0，公式是 2。正常载入 / 生成会先做：

```text
FoldPlainConstantAmountFormulas：2 折进 Amount，公式清空
```

但如果这份工程是旧版本生成的，或 JSON 被手改过，声明动态变量时仍可能读到 `Amount=0`，于是：

```csharp
new CardsVar(0)
```

卡面和实际抽牌都变成 0。

## V0.3.125

`VarDeclarationCore` 声明 `Draw` 的变量时，增加兜底：

```csharp
if (Amount == 0 && 公式是纯常数)
    Amount = 折出来的常数
```

所以即使存档残留：

```json
"Amount": 0,
"AmountFormula": "2"
```

生成出来仍是：

```csharp
new CardsVar(2)
```

## 生效方式

更新到 V0.3.125，重新生成工程并重装 DLL。

如果重新生成后还是 0，检查生成工程的：

```text
cs/Cards/SparkOfIons.cs
```

里面必须是：

```csharp
new CardsVar(2)
```

而不是 `new CardsVar(0)`。

## 验证

| 项 | 结果 |
|---|---|
| `Draw Amount=0 + 公式 2` → `new CardsVar(2)` | 通过 |
| 正常折常数流程不变 | 通过 |
| 开发版 `--uicheck` | **4971 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4970 PASS / 0 FAIL** |
