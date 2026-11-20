# V0.3.95 — 逆乱乾坤：公式里的 ThisCard 读数不再让卡面显示 0

## 问题

`逆乱乾坤`（AntiheroCard26）的伤害公式：

```text
8 + DiscardCount + ThisCardExhaust * 8
升级增量：4
```

实际打出时按公式计算是正确的，但卡面显示 `{Damage:diff()}` 只有 `0` / `4`。

## 根因

生成 `CanonicalVars` 时，只要公式里出现 `ThisCard*` 读数：

- 预览 lambda 的参数是 `CardModel`，不是派生卡类；
- 生成 `(card).ForgeThisCardExhaust` 会 `CS1061`；
- 旧代码干脆退回普通 `new DamageVar(Amount)` —— `Amount` 是 0，所以卡面显示 0。

## 修复

仍然声明 `ForgeFormulaDamageVar`，但在**卡面预览**里把 `ThisCard*` 读数按 0 代入：

```csharp
new ForgeFormulaDamageVar(
    static (card, target) =>
        (8m + discardCount + 0m * 8m),
    4m)
```

- 打出时：`ThisCardExhaust` 用真实值；
- 卡面：显示确定部分（基础 8 + 弃牌堆张数，升级 +4）；
- 不再生成 `(card).ForgeThisCardExhaust`，也不会退回 `DamageVar(0)`。

## 说明

“每张消耗的阴阳牌额外加多少”取决于打出时实际消耗几张牌，卡面无法提前知道，因此卡面显示的是**基础部分 + 弃牌堆部分**；这部分数字和描述里的固定部分一致。

## 验证

| 项 | 结果 |
|---|---|
| 生成 AntiheroCard26 源码 | 通过 |
| 卡面声明 ForgeFormulaDamageVar | 通过 |
| 预览 lambda 不再出现 `(card).ForgeThisCard` | 通过 |
| 开发版 `--uicheck` | **4937 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4936 PASS / 0 FAIL** |
