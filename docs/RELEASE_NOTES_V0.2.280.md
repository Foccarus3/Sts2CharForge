# V0.2.280 — 两个本地化 bug（卡面文案炸 / 中文花括号）+ 古老牙齿不显示的真因

## 你日志里炸的两条（已修）

### ① 「Format string has too many closing braces」——卡面描述整段渲染失败

```
In: "{IfUpgraded:show:打出一张牌时，生成{ColorXingtai:diff()}张升级过的牌。|…}"
ERROR: Format string has too many closing braces
table=cards key=COLOR_X_T.description
```

生成端为了「升级前后两版文案」把它包进 `{IfUpgraded:show:升级后|没升级}`，**但两版文案里本来就带
`{ColorXingtai:diff()}` 这种占位符** —— 内层的 `}` 被 SmartFormat 当成外层结构的结束，整段就崩了。

修法：**两版文案里只要含 `{` 占位符，就不套 `{IfUpgraded:…}`**，改用「没升级」那一版
（至少描述能正常显示；代价是升级前后文案相同时只显示一版）。

### ② 「Invalid character in the selector」——中文花括号被当占位符

```
In: "每回合生成{刺你！}"   →   '0x523A': Invalid character in the selector
```

校验器新增一条**警告**：自定义描述（卡 / 遗物 / 药水 / 状态说明）里出现 `{中文…}` 这种
「花括号里不是已知变量」的写法 → 明确告诉你「花括号会被本体当占位符，中文名字会让整段描述渲染失败，
请改用「」或去掉花括号，想插数值用『填入参数…』」。

## 古老牙齿不显示 —— 找到真因了（你说得对，它本来就在先古遗物池里）

不是「不在池里」，是**我们的补丁把它判成了「对当前角色不可用」**：

本体的 `ArchaicTooth.SetupForPlayer(Player)` **返回 bool**，先古之民房间用它决定「这件遗物能不能给这个角色」。
我们的补丁是 Harmony **Prefix**，原来写的是：

```csharp
__instance.SetupForTests(starter.ToSerializable(), ancient.ToSerializable());
return false;   // 跳过本体
```

`return false` = 不执行原方法，于是 `SetupForPlayer` 的返回值是**默认值 false** —— 房间看到 false
就当「这角色不能用这件遗物」，**直接不显示它**。

修法：三个 SetupForPlayer 补丁（古老牙齿 / 尘封魔典 / 欧洛巴斯之触）都在跳过本体前补一句：

```csharp
__result = true;   // 我们真的把它配好了 —— 返回 false 会被当成「不可用」，房间里就不显示
```

（`AfterObtained` 那两个补丁返回的是 `Task`，不加这句 —— 加了会 CS0029，这一点探针真编译踩到并已修正。）

## 验收

* 自检：**4453 PASS / 0 FAIL**（含「多目标档生成出来的工程真的编译得过」这条真编译探针 ——
  它一开始抓到了 `__result = true` 误插进 Task 版补丁的 CS0029，已修）。
* 对拍：不含 `{…}` 占位符的存档，卡面文案输出**逐字不变**。
