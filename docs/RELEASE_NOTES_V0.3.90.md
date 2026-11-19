# V0.3.90 — 修复描述里的 `{RegenPower}` 导致整段格式化失败

## 日志

```text
Localization formatting error! message=Error parsing format string:
No source extension could handle the selector named "RegenPower" at 1

{RegenPower}{__fgPowerDesc0}自己造成伤害后：施加 1 层再生（对指定敌人）。
table=powers key=SPARKLE_POWER8.description
variables={Amount:1,__fgPowerDesc0:5,...}
```

## 原因

描述里同时有：

```text
{RegenPower}
{=Amount*5}
```

- `{=Amount*5}` 已经正确换算成 `__fgPowerDesc0 = 5`；
- 但前面的 `{RegenPower}` 不是 SmartFormat 变量，格式化整段时直接抛异常；
- 异常发生后整段描述都不显示，所以看起来像 `=Amount*5` 没生效。

## 修复

生成描述时会识别 `{状态类名}`：

```text
{RegenPower} → 再生
```

能认出状态类名就换成游戏里的中文名；认不出的才保留。

Sparkle 状态 8 生成后：

```json
"SPARKLE_POWER8.description":
  "再生{__fgPowerDesc0}自己造成伤害后：施加 1 层再生（对指定敌人）。"
```

1 层时 `__fgPowerDesc0 = 5`，会正常显示 5。

## 正确写法

```text
{RegenPower}{=Amount*5}自己造成伤害后：施加 1 层再生。
```

注意公式要带花括号和等号：

```text
{=Amount*5}
```

不是裸写 `=Amount*5`。

## 验证

| 项 | 结果 |
|---|---|
| `{RegenPower}` 换中文名 | 通过 |
| `{=Amount*5}` 保留并显示 | 通过 |
| SparkleMod 真工程编译 | **通过** |
| 开发版 `--uicheck` | **4934 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4933 PASS / 0 FAIL** |
