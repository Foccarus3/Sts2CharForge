# V0.3.115 — 卡牌自定义描述支持 `{=表达式}`

## 新增写法

卡牌「自定义描述」/「升级后的自定义描述」现在可以直接写：

```text
{=表达式}
```

例：

```text
造成 {=A} 点伤害。
造成 {=A*2} 点伤害。
获得 {=Block} 点格挡。
造成 {=ThisCardDamage} 点伤害。
当前手牌 {=HandCount} 张。
```

## 实现方式

对每个 `{=表达式}`：

1. 用现有 `ForgeFormula` 解析；
2. 生成一个只用于显示的变量：

```csharp
new ForgeCardDescVar("ForgeCardDesc0", static (card, target) => (<表达式>))
```

3. 把描述里的 `{=表达式}` 替换成 `{ForgeCardDesc0}`；
4. `ForgeCardDescVar.UpdateCardPreview` 在卡牌预览时求值，写进 `PreviewValue`。

同一个表达式在「升级前 / 升级后」两段里出现，只生成一个变量。

## 旧存档

- 没写 `{=...}` 的卡：生成结果一个字符都不变；
- 写了 `{=...}` 的卡：多一个 `ForgeCardDescVar` 声明 + `FormulaVars.cs` 里的变量类；
- 诅咒卡也走同一套（`CurseSource` 单独补了声明）。

## 限制

`{=表达式}` 是**显示值**，不是实际结算值：

- 自身固定值 / 自身状态 / 自身资源 / 公式组合：可用；
- `ThisCardDamage` / `ThisCardBlock`：按这张牌的伤害 / 格挡效果做**预计值**；
- `ThisCardExhaust` / `Discard` / `Transform` / `Generate`：打出前按 0；
- 目标身上的易伤 / 虚弱 / 敌人减伤：手牌里没有目标，不能保证准确。

写错公式会由校验器报「不是合法公式」。

## 验证

| 项 | 结果 |
|---|---|
| `{=表达式}` 生成 `ForgeCardDescVar` | 通过 |
| 描述里替换成 `{ForgeCardDescN}` | 通过 |
| 同一表达式去重 | 通过 |
| 没写时输出不变 | 通过 |
| 开发版 `--uicheck` | **4963 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4962 PASS / 0 FAIL** |
