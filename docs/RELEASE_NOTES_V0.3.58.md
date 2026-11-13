# V0.3.58 — 修「减少最大生命值」编译失败（v0.113 API）

## 问题

卡牌效果「最大生命值」填负数（减少最大生命）生成：

```csharp
await CreatureCmd.LoseMaxHp(choiceContext, target, 5m, isFromCard: false);
```

v0.113 把 `LoseMaxHp` 的第四个参数从 `bool isFromCard` 改成了 `CardModel? cardSource`，
于是编译报：

```text
error CS1739: "LoseMaxHp"的最佳重载没有名为"isFromCard"的参数
```

`EdrsGooglePig` / `ShiJiuMod` 里的两张卡都是这个原因。

## 修改

所有生成端统一改成传 `cardSource`：

| 位置 | 传值 |
|---|---|
| 卡牌效果（负数最大生命） | `this` |
| 状态 / 遗物 / 药水 | `null` |
| 锻造节点图 | `ctx.CardSelfExpr`（卡牌图 = `this`，其它布 = `null`） |

涉及：

```text
CSharpCodeGen.EmitCardEffectSwitch
CSharpCodeGen（遗物 / 药水两条链）
CustomPowerGen.EmitEffect
ForgeGraphGen
```

## 验证

- 带「最大生命值 -5」的探针工程用 v0.113 `sts2.dll` 真编译：**0 错误**；
- 自检 `--uicheck` 更新旧断言后 **4912 PASS / 0 FAIL**；
- 部署版 **4911 PASS / 0 FAIL**。

⚠ 旧工程必须重新生成；只覆盖 dll 不会改掉已经生成的 `LoseMaxHp(..., isFromCard: ...)` 那一行。
