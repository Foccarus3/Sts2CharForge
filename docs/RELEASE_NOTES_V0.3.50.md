# V0.3.50 — 插件列表 pick 选反（选一次性存成永久）+ GIF 插件 1.3 支持多张卡

## 现象

「卡牌 GIF 替换」插件：

1. 播放方式选「一次性」，生成的 `CardGifSwapConfig.cs` 里却是 `"本场战斗永久"`；
   选「本场战斗永久」时又写成 `"一次性"`；
2. 一条规则只能写一张卡，写 `Defend,SparkleCard44` 不会命中。

## 真因 1：WPF ComboBox 的 SelectionChanged 里读了旧 Text

插件列表的 `pick` 子栏位原来：

```csharp
combo.Text = current;
combo.SelectionChanged += (_, _) => { row[key] = combo.Text; ... };
```

WPF 非可编辑 ComboBox 触发 `SelectionChanged` 时，`combo.Text` 可能还是**上一个选项**，
于是把旧值写进 rows.json → 生成文件反过来。

修法：初始化用 `SelectedItem`，事件里也只读 `SelectedItem`：

```csharp
if (sub.Options.Contains(current)) combo.SelectedItem = current;
combo.SelectionChanged += (_, _) => { row[key] = combo.SelectedItem as string ?? ""; ... };
```

页面级 `pick` 同样修了。

## 真因 2：CardFilter 只按整个字符串匹配

插件 `CardGifSwap.Matches` 原来把整个 `"Defend,SparkleCard44"` 当成一个类名比对，
所以永远不命中。

## 插件 1.3 修法

`CardFilter` 支持多个，逗号 / 顿号 / 分号 / `|` 分隔：

```text
Class:SparkleCard44,Class:SparkleCard45
Type:Attack,Class:SparkleCard44
Any
```

任意一个 token 命中即算命中；`Any` 仍然匹配全部。

播放方式判断也放宽为包含「永久」即永久（兼容旧配置）。

## 验证

| 项 | 结果 |
|---|---|
| 列表 pick 选第二项 → 行里存第二项 | **PASS**（新增 V0.3.50 断言） |
| 插件 1.3 生成工程真编译 | 0 错误 |
| 开发版 `--uicheck` | **4902 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4901 PASS / 0 FAIL** |

⚠ 插件本身也升级到 **1.3**：需要把 `插件_卡牌GIF替换` 重新放进生成器 `plugins\`，
并在页面里重新选一次播放方式（旧 rows.json 里已经存错的那行不会自动知道你的原意）。
