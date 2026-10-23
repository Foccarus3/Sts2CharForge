# V0.2.199 —— 药水图标：「怎么在每一个药水都会显示」的真凶是预览没跟着刷新

> 用户原话：「药水上传的图标怎么在每一个药水都会显示」

## 1. 生成端是干净的：每瓶药水本来就各有一份图

先把最容易被怀疑的地方钉死（新自检也在看着它）：

| 位置 | 内容 |
| --- | --- |
| 大图（弹窗） | `images/potions/large/<entry>.png`（`<entry>` = 这瓶药水的类名 ↓ 蛇形小写，例如 `sparkle_potion_trick.png`） |
| 物品栏小图标 | `images/atlases/potion_atlas.sprites/<entry>.tres`（AtlasTexture，指向它自己那张大图） |
| 选中高亮描边 | `images/atlases/potion_outline_atlas.sprites/<entry>.tres` |
| 运行时供给 | `AtlasSpritePatch` 的 `(potion_atlas, <entry>)` 一条一个，键不同 |

本体找图是 `PotionModel.PackedImagePath` / `LargeImagePath` —— 都按 **`Id.Entry`（这瓶药水自己的类名）** 拼路径，
所以「一瓶一张、互不共用」在生成端是成立的本体行为，不是我们另起一套。

实测（探针：7 瓶里只给第 1、2 瓶各传一张**不同**的图）落地文件：

```
sparkle_potion_trick.png   8894  ← 上传的图 A
sparkle_potion_daze.png    6294  ← 上传的图 B
sparkle_potion_mask.png     404  ← 占位图（另外 5 瓶都是这张）
```

## 2. 真凶：换选中项时，工具里的预览图**没有通知重读**

药水页那张预览缩略图绑的是 `Window.PotionIconPreview`（`RelativeSource=AncestorType=Window`），
它按「列表里当前选中的那瓶」取图。问题是它**不会**因为 DataContext 变了就自己重读 ——
必须有人发一次 `PropertyChanged`。

`SyncDetail()`（列表选中项一变就跑的那个方法）末尾以前只有：

```csharp
RaiseCardPortrait();
Raise("RelicIconPreview");      // 遗物有
                                // ← 药水漏了 PotionIconPreview
```

于是实际表现就是用户看到的那样：给第 1 瓶传了图标 → 点第 2 瓶 → 右下角**还停在**第 1 瓶那张图上，
看起来像「上传的图标出现在每一瓶药水上」。**已补上** `Raise("PotionIconPreview")`（还有下面那行文字）。

## 3. 顺手加了一行「当前这瓶：xxx.png」

缩略图小、几瓶药水长得又像，光看图很难确认「这张是给这瓶传的」。
所以在预览框下面加了一行小字，直接写当前这瓶的文件名，换一瓶就跟着变：

```
当前这瓶：闪光瓶.png（换一瓶药水这里会跟着变）
当前这瓶：未上传图标（生成时自动用占位图）
```

## 4. 构建日志也逐瓶写清楚

以前只有一句汇总「药水图标：7 个」。现在每瓶一行：

```
    · 药水「戏法药水」→ images/potions/large/sparkle_potion_trick.png ← 戏法药水.png
    · 药水「面具药水」→ images/potions/large/sparkle_potion_mask.png ← 占位图（没上传）
```

对得上 = 各自一份；对不上 = 那瓶的「上传药水图标…」真的没存进存档。

## 自检

新增 / 改动的断言（uicheck 全绿，0 FAIL）：

- 换选中药水会通知「药水图标预览」重读（否则上一瓶的图标会一直显示在每一瓶药水上）
- 药水图标那一行会写出当前这瓶的文件名
- 没传图标时那一行写「未上传」
- 每瓶药水的图集精灵名互不相同、各自指向自己那张图（不允许共用一张）

## 版本

- 版本号：`V0.2.199`（工具 exe 的「产品版本」里能看到）
