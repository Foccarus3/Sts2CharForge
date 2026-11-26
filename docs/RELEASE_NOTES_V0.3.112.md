# V0.3.112 — 新增内置触发时机「自己回复生命时」

## 新增

触发时机下拉新增：

```text
自己回复生命时（每一下）  HpHealed
```

挂在和「失去生命时」同一个本体钩子：

```csharp
public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
```

但守卫反过来：

```csharp
if (creature != base.Owner) return;
if (delta <= 0m) return;   // 只认回血
```

数值 N 表示「一次恢复至少 N 点」；填 0 / 留空 = 任何回血都触发。

## 覆盖

- 自定义状态触发时机；
- 遗物触发时机；
- 锻造图【触发时机】（遗物档 / 状态档）；
- 卡牌条件里的「卡牌时机」子时机。

## 和「失去生命时」的区别

| 时机 | 钩子 | 方向 |
|---|---|---|
| 自己失去生命时 | AfterCurrentHpChanged | `delta < 0` |
| 自己回复生命时 | AfterCurrentHpChanged | `delta > 0` |

两者不会互相触发。

## 生效方式

更新到 V0.3.112，重新生成对应模组并重装 DLL。

## 验证

| 项 | 结果 |
|---|---|
| 状态 / 遗物 / 锻造 / 卡牌时机都出现 HpHealed | 通过 |
| 生成守卫 `delta <= 0m return` | 通过 |
| 开发版 `--uicheck` | **4955 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4954 PASS / 0 FAIL** |
