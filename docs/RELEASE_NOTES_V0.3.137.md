# V0.3.137 — 修「变化卡牌数值 0（变化自己）」生成的 cs 少一个 `}`

## 一、问题（用户报）

锻造卡上放了【效果】变化卡牌，**数值填 0**（= 把这张牌自己变掉）+ **取卡方式 = 按范围随机** 时，
生成出来的卡牌 `.cs` 少一个右括号，整个工程编译不过：

```text
PigAssessValue.cs(127,6): error CS1513: 应输入 }
PigAssessValue.cs(211,2): error CS1513: 应输入 }
```

用户给的两处（`PigAssessValue.cs` 第 122 / 205 行附近）形状完全一样：

```csharp
            if (__newSelf is not null) {
                CardModel __madeSelf = base.CombatState.CreateCard(__newSelf, __fgPlayer);
                if (this.Pile is null || this.Pile.Type != PileType.Discard)
                    await CardPileCmd.Add(this, PileType.Discard);
                await CardCmd.Transform(this, __madeSelf, ...);
        }        // ← 只关了一层：if 的 } 从来没写出去，整份文件少一个
    }
```

## 二、真因

`CSharpCodeGen.EmitTransformCard` 的「数值 0 = 变化自己」那一支：

```csharp
w.Open("if (__newSelf is not null)");   // 写 "if (...) {" 并把缩进 +1
...
EmitSelfTransformLines(...);
// ← 这里少了 w.Close()
```

`Open` 只负责写 `{` + 进缩进，**不会自动补 `}`**；这一支在 V0.2.320 / V0.2.327 加「变化自己」时
忘了配对的 `Close`，于是：

- 「变化自己 + 指定目标卡」那条（不 Open）没事；
- 「变化自己 + **按范围随机**」那条少一个 `}`。

而且受影响的不只是锻造卡：**卡牌页的普通卡**走的是同一个 `EmitTransformCard`，
同样条件（变化卡牌 / 数值 0 / 按范围随机）也会生成编译不过的代码。已用探针实测：

```text
修之前：① 锻造图那条 括号差 = 1    ② 普通卡那条 括号差 = 1
修之后：① 锻造图那条 括号差 = 0    ② 普通卡那条 括号差 = 0
```

（`{` 与 `}` 的计数差；生成文件的其它部分逐行 diff 只多出一行 `}`。）

## 三、修法

在 `if (__newSelf is not null)` 那一支的末尾补上 `w.Close()`：

```csharp
if (countInto is not null) w.Line($"{countInto} += 1;");
// V0.3.137：这一档上面 Open 了 if (__newSelf is not null)，以前忘了 Close
if (selfRandom) w.Close();
return;
```

关在计数那一句之后，所以「本张牌变化了几张」的记账也跟着留在 `if` 里：
池子空、`__newSelf` 为 null 时什么都没发生，不该 +1。

**只影响「变化卡牌 / 数值 0 / 按范围随机」这一种组合**；别的效果、别的档位生成文本一个字符都不变
（这次的探针是在同一份代码上前后对比的）。

## 四、验证

`--uicheck` 新增 3 条断言（都用**新建的**卡 / 节点，避免 `ForgeEffectBridge` 迁移出来的
`EffectSpec` 缓存住旧字段导致测不到这条分支）：

| 断言 | 结果 |
|---|---|
| 锻造图「变化卡牌数值 0 + 按范围随机」→ 整份 cs 括号配平 | 通过（差 0） |
| 那一块真的把 `if (__newSelf is not null)` 关上了（同缩进的 `}`） | 通过 |
| （对照）卡牌页普通卡同一条效果也配平（两条链共用同一份实现） | 通过（差 0） |

```text
V0.3.137 开发版 --uicheck：4993 PASS / 0 FAIL
V0.3.137 部署版 --uicheck：4992 PASS / 0 FAIL
```

## 五、用户这边怎么恢复

**已经生成过的那份工程必须重新生成**（旧的 `cs/Cards/PigAssessValue.cs` 还是坏的）：
更新到 V0.3.137 → 打开存档 → 重新「生成」一次（再编译 / 导出 PCK）。
存档本身不用改：数值 0 + 按范围随机的配置本来就是对的，是生成器少写了一个括号。

## 版本

```text
V0.3.137
```
