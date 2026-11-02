# V0.2.395 — 资源能力（第二期第一步）：文件选择 / 图片预览 / 资源复制

需求书 4.9 与第二期。插件现在能**让用户选资源**、**预览图片**、**把资源复制进自己的目录**。

## 新增契约

```csharp
// IEditorPanel
IEditorFilePicker  AddFilePicker(string label, string filter, string value, Action<string>? onChange);
IEditorImagePreview AddImagePreview(string label, int height = 160);

// IEditorHost
string ImportAsset(string sourcePath, string fileName);   // → "assets/xxx.png"（名字非法 / 源不存在则返回 ""）
```

## 行为

* **文件选择栏**：文本框 + 「浏览…」按钮（`OpenFileDialog`，过滤器由插件给：`图片|*.png;*.jpg`）。
  插件只拿得到**用户自己选的路径**，不能自己遍历文件系统。
* **图片预览**：真实 `BitmapImage`（`OnLoad` + `Freeze`），文件不存在 → 灰字「（文件不存在：名字）」，
  读不出来 → 「（读不出这张图）」—— **任何情况都不抛异常**。
* **`ImportAsset`**：复制到 `<插件目录>/assets/<名字>`，返回相对路径 `assets/<名字>`；
  目标名带 `..` / 路径分隔符 / 非法字符、或源文件不存在 → 返回空串（拒绝），并记一条「最近一次异常」。

## 自检

**4768 PASS / 0 FAIL**（+5 条）：
选择栏存取路径 / 预览能加载真造的 PNG / 坏路径不抛 / `ImportAsset` 复制并返回 `assets/hero.png` /
`ImportAsset("../escape.png")` 与 `"a/b.png"` 都被拒。

## 第二期还差

* **音频预览**（`MediaPlayer` 播放 / 停止）—— 这一版没做，如实说明；
* 动画帧序列预览（逐帧 PNG）；
* 资源随 Mod 输出时自动拷进生成工程（现在只是复制进插件目录，生成清单里由插件自己决定写不写）。

## 说明

「路径锁死」这条原则在资源这里同样成立：插件不能指定绝对目标路径，只能给**文件名**，
落点永远是它自己目录下的 `assets/`。
