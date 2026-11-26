# V0.3.107 — 额外资源量图标过大撑爆悬停说明

## 现象

额外资源量计数器 / 悬停说明里出现一张巨大的图标，文字被挤到很小：

```text
[大图]
想象力：开场不发放，靠卡牌 / 遗物 / 药水获得……
```

## 根因

生成器会把上传的资源图标缩成 **24×24** 的内联小图：

```text
images/packed/sprite_fonts/<角色>_extra_resource_icon.png
```

但 `PngUtil.Decode` 只支持 **8bit、非隔行、RGB/RGBA** 的 PNG。
如果上传的是 16bit / 隔行 / 调色板 PNG，缩放会失败，旧代码就 fallback 成：

```csharp
FsSafe.Copy(src, inlineDst, overwrite: true);
```

于是 `[img]` 里放的是**原图尺寸**（512×512 等），游戏按原尺寸画，整段说明被撑爆。

## 修复

缩放失败时不再拷贝原图，改为写一张 **24×24 透明占位图**：

```csharp
PngUtil.WriteRgba(inlineDst, 24, 24, new byte[24 * 24 * 4]);
```

同时生成日志会写：

```text
[警告] 额外资源量图标不是 8bit 非隔行 RGB/RGBA，
文字内联图标已用 24×24 透明占位（计数器大图仍用原图）
```

计数器 / 遗物栏那张大图仍然使用原图，不受影响。

## 长期建议

上传资源图标时优先用 **8bit RGBA PNG**，这样内联小图能正常缩成 24×24，图标也会显示出来而不是透明占位。

## 验证

| 项 | 结果 |
|---|---|
| 非 8bit PNG 不再把原图塞进 inline | 通过 |
| inline 固定 24×24 | 通过 |
| 开发版 `--uicheck` | **4951 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4950 PASS / 0 FAIL** |
