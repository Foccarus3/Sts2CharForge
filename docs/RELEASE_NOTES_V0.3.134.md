# V0.3.134 — 修复自定义 ComboBox 模板显示对象 ToString

## 问题

V0.3.133 把 `ComboBox` 换成主题模板后，选中项显示成了：

```text
EffectKindOption { Kind = RaiseResourceCap, Display = 增加额… }
ConditionOption { Id = NotPlayedThisTurn, Display = 本回合还没… }
```

原因是自定义模板只放了 `ContentPresenter`，没有像 WPF 默认模板那样处理 `DisplayMemberPath`；`ContentPresenter` 直接对对象调用了 `ToString()`。

## 修复

新增 `ComboDisplayConverter`：

- 优先按 `ComboBox.DisplayMemberPath` 取属性；
- 没写 DisplayMemberPath 时优先取 `Display`；
- 再退回 `ToString()`。

模板选中项改成：

```xml
<MultiBinding Converter="{StaticResource ComboDisplay}">
    <Binding Path="SelectedItem" RelativeSource="{RelativeSource TemplatedParent}"/>
    <Binding Path="DisplayMemberPath" RelativeSource="{RelativeSource TemplatedParent}"/>
</MultiBinding>
```

所以现在会正常显示：

```text
效果种类：增加额外资源量上限
条件选项：本回合还没打出过牌
```

## 说明

这次按要求没有跑其他自检，只做编译和打包。

## 版本

```text
V0.3.134
```
