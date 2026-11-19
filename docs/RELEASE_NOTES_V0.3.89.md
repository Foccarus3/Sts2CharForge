# V0.3.89 — 自定义状态描述直接填 `{C}` 自定义参数

## 问题

「填入参数…」里只有：

```text
{Amount}
{=Amount*2}
```

没有 Sparkle 存档里的自定义参数 `C`、变量 `a/b`。

手动写 `C` 也不会显示 C 的数值 —— 因为描述里只认 SmartFormat 占位符，`C` 没有被塞进 LocString。

## 修复

1. 「填入参数…」会列出：
   - `{Amount}`；
   - `{=Amount*2}` 公式模板；
   - 每个**自定义参数**：`{C}` / `{M}` …；
   - 每个**变量**：`{a}` / `{cnt}` …。

2. 自定义状态描述现在支持直接写：

```text
当前 C = {C}
```

生成时会自动改写成公式 `{=C}`，再塞进：

```csharp
__loc.Add("__fgPowerDesc1", ForgeFormulaRuntime.PowerAmount(base.Owner, "SparklePower8"));
```

4 层、C=4 时，描述显示 4。

## 用法

Sparkle 存档的状态 8：

```text
每层 2 点，当前共 {=Amount*2} 点；C = {C}；变量 b = {b}。
```

- `C` 是自定义参数（SparklePower8 的层数）；
- `b` 是变量（公式 `C*2`）；
- 都会按当前值显示。

## 注意

- `{C}` 必须与「自定义参数」页里的名字**完全一样**（大小写不敏感）；
- 变量同理；
- 旧工程需重新生成。

## 验证

| 项 | 结果 |
|---|---|
| 直接写 `{C}` 转运行时变量 | 通过 |
| 生成工程编译（SparkleMod） | **通过** |
| 开发版 `--uicheck` | **4934 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4933 PASS / 0 FAIL** |
