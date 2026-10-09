# V0.2.114 更新说明

> 这批需求里的第 ⑦ 条：**卡牌种类用「无色牌」时，卡面会丢失**。

## 一、根因：卡面素材写错了目录

本体找卡面的路径是**按「这张卡所在那个池」**拼的：

```csharp
// CardModel.cs
public virtual string PortraitPath    => ...$"atlases/card_atlas.sprites/{Pool.Title.ToLowerInvariant()}/{Id.Entry.ToLowerInvariant()}.tres";
protected virtual string PortraitPngPath => ...$"packed/card_portraits/{Pool.Title.ToLowerInvariant()}/{Id.Entry.ToLowerInvariant()}.png";
```

而**无色卡**的模型是靠 `ModHelper.AddModelToPool(ColorlessCardPool, T)` 登记进**本体的无色卡池**的
（工具也特意没把它放进你自己的池），所以 `Pool.Title` = **`"colorless"`**。

生成器以前却一律把卡面写到**你自己的池目录**下（`card_portraits/<你的池>/…`）——
游戏去 `card_portraits/colorless/…` 找，找不到 → **这张牌在游戏里没有卡面**（`HasPortrait = false`，
显示成缺图占位）。用户报的「无色牌卡面丢失」就是这个。

## 二、修法

`ArtGenerator` 写卡面时按**卡所在池**决定目录：

```csharp
string poolTitle = c.IsColorless ? "colorless" : n.PoolTitle;   // 无色卡 = 本体无色卡池的 Title
string pngDst  = .../images/packed/card_portraits/{poolTitle}/{卡ID小写}.png
string tresDst = .../images/atlases/card_atlas.sprites/{poolTitle}/{卡ID小写}.tres
```

- 那份 `.tres` 里的 `[ext_resource]` 路径也一起跟着改（本体是加载 `.tres` 再指向 png 的）；
- **普通卡 / 诅咒 / 先古卡照旧**写在你自己的池目录里（它们的 `Pool` 就是你角色的卡池）；
- 生成出来的工程 README 里也把这条路径写清了。

## 三、验证

- `--uicheck`：开发版与发布版都是 **2620 PASS / 0 FAIL**（新增 4 条：无色卡卡面在 `colorless/` 下、
  **不再**写进本角色池目录、`.tres` 里指向的也是 `colorless/` 的 png、对照「普通卡照旧在自己池目录」）；
- 探针 `RunColorless` 追加输出：`卡面(colorless/) = True`、`卡面tres(colorless/) = True`、
  `tres 指向 = res://images/packed/card_portraits/colorless/probe_colorless.png`、
  `普通卡照旧在自己池目录 = True`；整轮探针 **27 处 `HasUnparsed = False` / 0 处 True**；
- 打包：`Sts2CharForge_更新包_V0.2.114.zip`。

## 四、这批剩余的

| # | 需求 | 状态 |
|---|---|---|
| ① ② ④ ⑤ ⑥ ③ | 见前几版 | ✅V0.2.108~113 |
| ⑦ | 无色牌卡面丢失 | ✅ 本版 |
| ⑧ | 选牌 / 取牌方式增加「牌顶」 | 下一版 |
| ⑨ | 新效果「放置卡牌」（数值 0 = 自己；否则从五处挑一张放到五处） | 待做（做法已确认） |
| ⑩ | 同一状态的几条触发时机可勾「要和其他时机同时成立」+ 重置范围 | 待做（做法已确认） |
| ⑪ | 「只对哪类牌生效」增加选择范围 | 待做 |
