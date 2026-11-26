# V0.3.129 — 诅咒 / 先古 / 状态费用与升级描述；死亡动画定格修复

## 一、诅咒 / 先古 / 状态页

三页现在都补上了与「卡牌」页相同的费用栏：

```text
费用 ／ 额外资源量费用
升级后费用 ／ 升级后额外资源量费用
```

- 诅咒页：默认 -1 费（打不出去），仍然保留「能打出去」的写法；
- 先古卡：本来就能升级，升级后费用 / 额外资源量费用现在可以直接填；
- 状态卡：勾「可被无限升级」后，升级后费用才会生效。

### 诅咒升级

诅咒页增加：

```text
可被升级（诅咒默认不能升级；勾上后可被无限升级）
```

不勾 = 仍生成 `MaxUpgradeLevel => 0`，和以前完全一样；  
勾上后生成 `int.MaxValue`，并支持：

- 升级后能量费用；
- 升级后额外资源量费用；
- 升级后的自定义描述。

### 升级前 / 升级后自定义描述

- **诅咒卡**：新增「升级后的自定义描述」+「自动填入（升级后）」，生成
  `CET:UpgradedCustomDescription` 标记，回读时能还原；
- **状态卡**：和先古卡共用详情区，升级前 / 升级后描述现在明确按状态卡生效
  （需先勾「可被无限升级」）。

`LocalizationGen` 会用本体的 `{IfUpgraded:show:升级后|未升级}` 按升级状态显示。

## 二、动画页死亡动画

真因：生成注册行**没有把 `holdAtEnd` 传给 `FrameAnimRegistry.Register`**：

```csharp
// 以前
FrameAnimRegistry.Register(..., loop: false, ...);
```

死亡帧因此按普通单次动画处理，播完 `_Process` 自动 `Play("idle_loop")`。

修复：

```csharp
// 现在，死亡动作会多这一项
FrameAnimRegistry.Register(..., holdAtEnd: true);
```

并在播放器里增加保护：

```csharp
if (_held && trigger is "Idle" or "Relaxed") return;
```

死亡帧播完定格后，本体后续补发的 `Idle` 不会再把倒地帧顶回待机；只有 `Revive` 能解除定格。

## 生效方式

更新到 V0.3.129，重新生成工程并重装 DLL/PCK。  
已经生成的旧工程必须重新生成，旧的 `FrameAnimRuntime.cs` / `ModEntry.cs` 不会自动改。

## 验证

| 项 | 结果 |
|---|---|
| 死亡注册行带 `holdAtEnd: true` | 通过 |
| 定格后 `Idle` / `Relaxed` 被拦截 | 通过 |
| 诅咒 / 先古 / 状态费用栏 XAML 正常加载 | 通过 |
| 诅咒升级 / 升级后描述标记生成 | 通过 |
| 开发版 `--uicheck` | **4976 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4975 PASS / 0 FAIL** |
