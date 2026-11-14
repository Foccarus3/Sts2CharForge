# V0.3.62 — 卡牌 / 自定义状态跨类别类名撞车

## 问题

生成工程里 `cs/Cards/RenoWarlock.cs` 和 `cs/Powers/RenoWarlock.cs` 都定义：

```csharp
public sealed class RenoWarlock
```

所以 dotnet 报：

```text
CS0101 命名空间“MyMod”已经包含“RenoWarlock”的定义
CS0263 “RenoWarlock”的分部声明一定不能指定不同的基类
```

这两个类分别来自：

```text
卡牌 RenoWarlock
自定义状态 RenoWarlock
```

它们用了同一个英文类名。

## 处理

V0.3.62 在生成前增加跨类别类名检查：

```text
卡牌 / 遗物 / 药水 / 召唤物 / 自定义状态
```

任意两类出现同一个英文类名，直接报：

```text
模型类名重复：RenoWarlock（卡牌「…」和自定义状态「…」都用了这个英文类名）。
生成出来会是两个同名 class（CS0101 / CS0263），整个工程编译不过；
请到对应页给其中一个换一个英文类名。
```

同类内部的重名仍由原来的卡牌 / 遗物 / 药水 / 状态检查分别处理，不会重复刷屏。

## 用户侧修法

到对应页面把其中一个英文类名改掉，例如：

```text
卡牌：RenoWarlock
自定义状态：RenoWarlockPower
```

然后重新生成工程。

## 验证

| 项 | 结果 |
|---|---|
| 复现卡牌 + 自定义状态同名 | 生成前报「模型类名重复」 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |
