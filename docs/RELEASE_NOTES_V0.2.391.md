# V0.2.391 — `IPluginStore<T>`：插件自有对象模型的保存 / 读取 / 版本迁移（需求书 4.4）

到这一步，「敌人列表 → 选中 → 右侧详情 → 编辑 → **保存**」的链路齐了。

## 契约

```csharp
IPluginStore<T>? GetStore<T>(string name) where T : class, new();   // ← IEditorHost

public interface IPluginStore<T> where T : class, new()
{
    T Value { get; set; }
    int SchemaVersion { get; set; }
    bool Exists { get; }
    string FilePath { get; }
    void Save();
    void Reload();
    void SetMigration(Func<int, string, T?> migrate);
}
```

## 关键性质

| 性质 | 说明 |
|---|---|
| **路径锁死** | 固定落在 `<插件目录>/data/<名字>.json`；`name` 里带 `..` / 路径分隔符 / 非法字符 → 直接返回 `null`（出不了插件目录） |
| **带版本的信封** | 文件是 `{ "schemaVersion": N, "data": { … } }`；也兼容没有信封的裸对象（早期 / 手写文件） |
| **迁移不猜测** | 读到 `schemaVersion < CurrentSchemaHint` 时**调插件注册的钩子** `(oldVersion, rawJson) → T?`；返回 null 就保留旧对象，绝不覆盖用户数据 |
| **原子写** | 先写 `.tmp` 再替换，避免写一半把存档写坏 |
| **坏文件不崩** | 解析失败 → 给一个空对象继续工作，**原文件原样保留**（用户能自己修） |

## 自检

**4755 PASS / 0 FAIL**（+5 条）：
路径确实在 `…/data/project.json` / 存了再读能拿回（含 `schemaVersion`）/ 旧版本触发迁移（`Name=迁移过 Count=1`）/
坏文件给空对象且原文件仍在 / `GetStore("../escape")` 与 `GetStore("a/b")` 都被拒。

## 踩到的坑（自检抓到）

第一版 `Reload()` 在成功解析信封后 `return;` —— **把后面的迁移段整段跳过了**，自检直接报「迁移没跑、读到的还是老结构」。
现在改成不 return，走 `else` 分支。

## 下一步

* 多文件生成：文件清单 → 预览 → 确认 → 写入（需求书 6.4「预览生成文件清单」）；
* 资源选择与预览（第二期）：图片 / 音频选择、复制进插件目录、路径校验；
* 插件详情页显示「最近错误 / 最近生成输出」（审核信息的剩下两项）。
