# V0.3.140 — 「只变化这张卡」漏掉了初始份数的副本（只变一张）

## 一、问题（用户报）

另一位作者的角色「Antihero」：初始打击 / 防御各 **4 份**，做了一张「究极进化」——
「变化卡牌」勾了「只变化这张卡 = 打击（AntiheroStrike）」+「取几张 = 2 张」，想一次变掉几张打击，
结果**只变了一张**。

## 二、真因

初始份数 > 1 的卡会被生成器展开成**独立 CardModel**（`StarterCopyGen`：`AntiheroStrike` +
`AntiheroStrikeCopy2 / Copy3 / Copy4`，游戏日志里看到的就是 `ANTIHERO_STRIKE_COPY3/4`）。
它们是不同的 `ModelId`，而「只变化这张卡」生成的是：

```csharp
Where(c => c.Id == ModelDb.Card<AntiheroStrike>().Id)     // ← 只命中第一份
```

于是 2 张的循环第一次变掉原卡后，第二次已经**没有任何牌能匹配**（`NextItem` 返回 null → break）
—— 表现就是「只变了一张」。同理，凡是「按具体某一张初始卡」找牌的效果（变化 / 消耗 / 丢弃 /
升级 / 复制……）在有多份初始卡的牌组里都只会命中第一份。

（V0.3.123 只修过**初始打击 / 防御**这一类：按 `CardTag + Basic` 匹配所有份数。别的卡没覆盖到。）

## 三、改法

新增 `CSharpCodeGen.CardIdentityCond(p, 卡类名, var)`：把「这张卡 + 它按份数展开出来的所有副本」
一起列出来（副本靠 `CardSpec.StarterCopyOf` 认，那是展开时记下的原卡生成类名）。

「只变化这张卡」（`TransformSourcePredicate`）和「范围界定 / 只对这张卡」（`OnlyCardCond`）都改用它：

```csharp
// 有副本时（AntiheroStrike 4 份）
Where(c => (c.Id == ModelDb.Card<AntiheroStrike>().Id
         || c.Id == ModelDb.Card<AntiheroStrikeCopy2>().Id
         || c.Id == ModelDb.Card<AntiheroStrikeCopy3>().Id
         || c.Id == ModelDb.Card<AntiheroStrikeCopy4>().Id))
```

- **没有副本的卡**：生成的文本和以前**一模一样**（`c.Id == ModelDb.Card<X>().Id`，不套括号）；
- 初始打击 / 防御仍走 V0.3.123 那条 `CardTag + Basic` 的判据（本来就覆盖所有份数）；
- 用户明确选中某一份副本（比如 `AntiheroStrikeCopy2`）时仍是精确匹配那一份。

「取几张 = 全部」那条路本来就会 `foreach` 遍历命中的每一张 —— 配上这次的身份条件，
现在会把 4 份打击**全部**变掉（以前同样只变一张）。

## 四、验证

`--uicheck` 新增 3 条断言（用一张 ×3 初始份数的卡）：

| 断言 | 结果 |
|---|---|
| 「只变化这张卡」把原卡 + Copy2 + Copy3 一起列出来 | 通过 |
| 「取几张 = 全部」时 `foreach` 里是那 3 个 ModelId（每一份都变） | 通过 |
| （对照）没有副本的卡还是老写法（单个 ModelId、不套括号） | 通过 |

```text
V0.3.140 开发版 --uicheck：5011 PASS / 0 FAIL
```

另外用最小存档（初始打击 ×4 + 「只变化这张卡 = 那张打击」+ 取几张 = 全部）真生成了一份工程，
`dotnet build`：**0 错误**（验证 `ModelDb.Card<XCopy2…4>()` 这些副本类确实都在、真能编译）。

## 五、用户这边怎么用

更新到 V0.3.140 → 打开存档 → 重新「生成工程」。

- 想「变掉 2 张」：保持「取几张 = 恰好 N 张」、N = 2 —— 现在能正常变 2 张（以前只有 1 份能匹配，所以只变 1 张）；
- 想「把全部打击 / 防御都变掉」：把「取几张」改成**全部**（这条以前也会被同一个坑卡住，现在能全变）；
- 存档配置不用改。

## 版本

```text
V0.3.140
```
