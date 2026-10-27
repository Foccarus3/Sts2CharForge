# V0.2.299 — 「指定敌人的意图为（）」选不到意图

## 原因

V0.2.288 加这条条件时，我只把「意图」下拉接在了**遗物页**那个条件模板上；
**卡牌 / 自定义状态的效果条件**用的是另一份模板（`App.xaml` 里那个共用的 `DataTemplate`），那一份漏了 ——
所以你在效果条件里选到「指定敌人的意图为（）」之后，**下面根本不出现意图下拉**，等于选不了。

条件本身、生成端、下拉数据（15 个 `IntentType`）都是好的，缺的是那一行控件。

## 修法

在那份共用模板里补上一行（和遗物页同一套绑定）：

```xml
<TextBlock Grid.Row="5" Grid.Column="0" Text="意图"
           Visibility="{Binding Condition.NeedsIntent, Converter={StaticResource BoolToVis}}"/>
<ComboBox Grid.Row="5" Grid.Column="1" Grid.ColumnSpan="3"
          ItemsSource="{Binding IntentOptions, RelativeSource={RelativeSource AncestorType=Window}}"
          DisplayMemberPath="Display" SelectedValuePath="Id"
          Visibility="{Binding Condition.NeedsIntent, Converter={StaticResource BoolToVis}}"
          SelectedValue="{Binding Condition.Intent}"/>
```

（同时给那个 Grid 补了第 6 个 `RowDefinition`，不然新行会被压到同一格里。）

## 现在三处都能选意图

| 位置 | V0.2.288 | V0.2.299 |
|---|---|---|
| 遗物页「整只遗物」条件 | ✅ | ✅ |
| 卡牌效果的条件 | ❌ | ✅ |
| 自定义状态效果的条件 | ❌ | ✅ |

## 验收 / 发布

* 自检：**4454 PASS / 0 FAIL**（4682 行，无异常）
* 只出更新包（`-NoBundle`）
