# V0.2.390 — 受控列表 `IEditorList<T>`（需求书 4.3 节）

「敌人列表 + 右侧详情」需要的最后一块界面能力。现在插件能建**带选中项的列表**了。

## 契约（Core `EditorWidgets.cs`）

```csharp
IEditorList<T> AddList<T>(string label, IReadOnlyList<T> items, Func<T,string> labelOf, Func<T,T>? cloner = null)

public interface IEditorList<T> where T : class
{
    IReadOnlyList<T> Items { get; }
    T? Selected { get; set; }
    event Action<T?>? SelectionChanged;
    void Add(T item);
    void Remove(T item);
    void MoveUp(T item);
    void MoveDown(T item);
    void Clone(T item);      // 用 cloner；没给就浅拷贝（MemberwiseClone）
    void Refresh();
}
```

## 界面

列表本体是 `ListBox`（宽 220、高 200），右边一列按钮：`+ 添加` / `✕ 删除` / `↑ 上移` / `↓ 下移` / `⧉ 复制`。
所有插件回调（包括 `SelectionChanged`）都走 `_guard`：抛异常只弹提示，不把异常抛回主程序。

## 一个行为决定

`Selected` 的 setter **也会触发** `SelectionChanged` ——
插件常常是代码里选（比如「新建一只敌人后选中它」），不只在用户点击时。这一条是自检逼出来的
（第一版 setter 静默，自检立刻红）。

## 自检

**4750 PASS / 0 FAIL**（+5 条）：列条目 + 默认选中第一项 / 改选中项触发事件（拿到「重击」）/
Add + MoveUp（顺序变成 横扫/格挡/重击）/ Clone（用插件 cloner，插在被复制项后面）/ Remove。

## 下一步

* `IPluginStore<T>`：插件自有对象模型（`EnemyProject` / `EnemySpec` …）的保存、读取、版本迁移；
* 多文件生成：文件清单 → 预览 → 确认 → 写入（需求书 6.4）；
* 资源选择与预览（第二期）：图片 / 音频选择、复制、路径校验。

到这一步，「敌人列表 → 选中 → 右侧详情 → 编辑 → 保存」的链路已经**能拼出来了**（列表 + 面板 + 输入控件 + 按钮事件都有了），
还差的只是**插件自己的对象模型怎么落盘**（`IPluginStore<T>`）。
