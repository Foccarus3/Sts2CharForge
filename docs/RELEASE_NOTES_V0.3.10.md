# V0.3.10 — 构建时不再清掉工程里的插件图片目录（`plugin_assets/`）

（按你的场景确认：**点构建后，工程里放图片的文件夹直接消失**）

## 原因

「一键构建」的第一步是**清掉上一次生成的工程**（防止旧 cs 残留导致编译失败），实现是整棵目录删除：

```csharp
FsSafe.DeleteTree(root);   // ← plugin_assets/ 也被一起删了
```

插件的图片放在工程里的 `plugin_assets/<插件id>/`，于是被这次清理连带删掉。

## 改法

清理时**跳过 `plugin_assets/`**：

```csharp
public static void ClearGeneratedProjectKeepingPluginAssets(string root)
{
    foreach (string child in Directory.GetFileSystemEntries(root))
    {
        if (名字是 plugin_assets) continue;      // ← 插件资产保留
        if (是目录) FsSafe.DeleteTree(child); else FsSafe.DeleteFile(child);
    }
}
```

旧 cs / project.godot / 缓存资源照常清掉（防旧文件残留），**只有插件的资产目录被保留**。

## 约定（插件作者请按这个写路径）

| 位置 | 用途 | 会不会被清 |
|---|---|---|
| `plugins/<插件id>/assets/<文件名>` | **原始图**（插件自己的数据，永久的） | 不会被清 |
| `<工程>/plugin_assets/<插件id>/<文件名>` | 工程内副本（每次生成从上面拷） | **不清**（V0.3.10 起） |

插件里请存**相对路径**（`assets/enemy.png`），不要存 `C:\Users\...\Downloads\xxx.png` 这种绝对路径。

## 自检

**4804 PASS / 0 FAIL**（+1 条，真实跑一遍清理）：

把 `plugin_assets/enemygen/enemy.png`、`cs/old.cs`、`project.godot` 一起造进临时目录 → 调清理 → 断言
**资产还在**、旧 cs 和 project.godot 都清掉了（`资产=True 旧cs=False`）。

## 你需要做的

更新到 **V0.3.10** → 把图片重新放进工程（或让插件重新导入一次）→ 再点构建，`plugin_assets/` 应该还在。

如果插件的图片目录**不叫** `plugin_assets`（比如它自己叫 `assets` 或 `images` 放在工程根下），告我那个目录名，我把它也加进保留名单——
目前保留的是**固定名字** `plugin_assets`（工具拷插件资产就用这个目录）。
