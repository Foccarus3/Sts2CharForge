# V0.3.117 — 修复 `{A+2}` 这种没有 `=` 的写法导致整段描述渲染失败

## 日志证据

```text
Localization formatting error!
'0x2B': Invalid character in the selector
In: "... 获得 {Energy:diff()} 点能量。
{__fgCardDesc0}{A+2}"
variables={Energy:2,__fgCardDesc0:0,...}
```

`__fgCardDesc0` 其实**已经在变量表里**。真正让整段渲染失败的是后面的：

```text
{A+2}
```

SmartFormat 把 `+` 当成 selector 的非法字符，**整段描述格式化失败**，于是连前面的变量也原样打印出来。

## 修复

V0.3.117 同时支持两种写法：

```text
{=A+2}      ✅
{A+2}       ✅（没有 = 也认，只要内容是“带运算符的纯表达式”）
```

仍然不会误吃本体占位符：

```text
{Damage}          → 本体变量，不动
{Damage:diff()}   → 本体变量，不动
{Energy:diff()}   → 本体变量，不动
{IfUpgraded:...}  → 本体变量，不动
```

判据：

```text
裸 {...} 里必须带 + - * / % 之一，
且只含字母 / 数字 / 下划线 / 空格 / 括号 / 运算符
```

## 变量名

V0.3.116 把显示变量从 `__fgCardDescN` 改成了 `ForgeCardDescN`。  
改名本身不是这次问题的根因，但保留：不以双下划线开头，兼容性更稳。

## 验证

| 项 | 结果 |
|---|---|
| `{=A+2}` 和 `{A+2}` 都生成显示变量 | 通过 |
| 本体 `{Damage}` / `{Damage:diff()}` 不被误改 | 通过 |
| 非法公式仍被校验器拦住 | 通过 |
| 开发版 `--uicheck` | **4963 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4962 PASS / 0 FAIL** |
