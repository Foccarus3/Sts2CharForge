# V0.2.396 — 音频预览 + 逐帧动画预览（第二期补齐）

V0.2.395 只有图片预览；这一版把「试听音效 / 看动画帧」补上。

## 新增契约

```csharp
IEditorAudioPreview AddAudioPreview(string label);              // IEditorPanel
IEditorFramePreview AddFramePreview(string label, int height);  // IEditorPanel

public interface IEditorAudioPreview { string Path { get; set; } }

public interface IEditorFramePreview
{
    string Folder { get; set; }   // 装着 PNG 序列的文件夹（绝对路径）
    int FrameCount { get; }       // 只读：这个文件夹里被当成帧的图片数
    int FrameIndex { get; set; }  // 0 起（越界自动夹）
}
```

## 界面

* **音频**：文件名 + 「▶ 播放」/「■ 停止」（`MediaPlayer`）。文件不存在 → 「（没有可播放的音频）」，
  播放失败 → 「（播不了：原因）」—— **不抛异常**。
* **逐帧**：图片 + 「◀ 上一帧 / 下一帧 ▶」+ 当前帧号 `2 / 3：frame2.png`。
  文件夹按文件名排序，认 `.png/.jpg/.jpeg/.bmp/.gif`；空目录 → 「（这个文件夹里没有图片）」。

## 自检

**4773 PASS / 0 FAIL**（+5 条）：
音频记住路径 / 坏音频路径不抛 / 逐帧数出 3 帧 / 帧号越界被夹到 2 / 坏目录不抛且帧数 0。

## 第二期剩余

* **资源随 Mod 输出**：现在 `ImportAsset` 只复制进插件目录（`assets/`），
  生成清单里要不要把这些文件一起写进 Mod 工程，由插件自己决定（下一步可以给一个
  `IEditorHost.ExportAssets(relativePaths)` 的便利方法 + 在确认窗里显示「将复制 N 个资源」）。
* 动画帧的**播放**（自动按 FPS 轮播）—— 现在只能手动翻帧。
