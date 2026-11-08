# V0.3.39 — 额外资源量图标首次加载失败会重试

## 现象

额外资源量（星辉 / 自定义资源）的卡面费用图标有时是自定义图，有时还是本体星星；
没有固定存档 / 日志能复现，属于「有时不生效」。

## 真因

`ExtraResourceUi.LoadIcon()` 以前是：

```csharp
if (_iconTried) return _icon;
_iconTried = true;
_icon = GD.Load<Texture2D>(IconPath);
```

只要第一次 `GD.Load` 返回 `null`（模组 pck 是运行时挂载的，加载顺序 / 缓存状态不同，
第一次可能读不到），`_iconTried = true` 会**永久缓存失败**：
之后即使资源已经可用，也永远返回 null，计数器保持本体星星，而日志只有最开始一条。

## 修法

- 不再永久缓存失败：失败后每秒重试一次；
- 重试成功前只打一条日志，避免刷屏；
- `GD.Load` 失败时再用 **PNG 原始字节**兜底：

```csharp
byte[] bytes = FileAccess.GetFileAsBytes(IconPath);
var img = new Image();
img.LoadPngFromBuffer(bytes);
_icon = ImageTexture.CreateFromImage(img);
```

- 成功后 `_iconWarned` 复位，计数器 / 卡面费用图标都会用同一张图。

## 验证

| 项 | 结果 |
|---|---|
| 生成代码包含 `GetTicksMsec` 重试 | PASS |
| 生成代码包含 `LoadPngFromBuffer` 兜底 | PASS |
| 开发版 `--uicheck` | **4890 PASS / 0 FAIL**（+1 条 V0.3.39 断言） |
| 部署版 `--uicheck` | **4889 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里 `ExtraResourceUi` 还是「失败永久缓存」的旧代码。
