# V0.3.76 — 模组 ID / 角色类名不合法时指出具体字符

## 问题

用户把存档名从：

```text
antihero‌.json
```

改成：

```text
Antihero‌.json
```

仍然报：

```text
模组 ID 必须是纯英文/数字且以字母开头
角色英文类名必须是纯英文/数字且以字母开头
```

原因是文件名里有一个**看不见的零宽字符**，只把首字母改成大写并不会删掉它。

## 改动

`Naming.InvalidIdentifierReason` 会指出具体是第几个字符、Unicode 码位：

```text
第 9 个字符是不可见格式字符 U+200C「‌」
```

`ProfileValidator` 的模组 ID / 角色英文类名错误现在会附上这句，不再只说「必须纯英文」。

## 用户修法

1. 在「角色」页把「模组 ID」和「角色英文类名」清空；
2. **手动输入** `Antihero`，不要从文件名复制；
3. 或者用 PowerShell 检查：

```powershell
$s = 'Antihero‌'
$s.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }
```

看到 `U+200C` / `U+200B` / `U+FEFF` 就是隐形字符。

## 另外

`F:\1\1.png` 图标不存在是另一个独立错误：需要重新选择「宇宙秘法」的图标，或清空该路径。

## 验证

| 项 | 结果 |
|---|---|
| 非法字符定位 | 显示字符序号 + U+ 码位 |
| 开发版 `--uicheck` | **4916 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4915 PASS / 0 FAIL** |
