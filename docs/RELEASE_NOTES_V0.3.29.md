# V0.3.29 — 古老牙齿「换成先古卡」时空引用

日志里能看到的现场（Seren / Loadout）：

```text
[WARN] Property AncientCard on RELIC.ARCHAIC_TOOTH is null, which is not a valid SavedProperty
[WARN] Property StarterCard on RELIC.ARCHAIC_TOOTH is null, which is not a valid SavedProperty
WARNING: LoadoutImmediateMutation: stopped adding relic 'RELIC.ARCHAIC_TOOTH' ... Object reference not set ...
```

## 真因

本体 `ArchaicTooth.AfterObtained` 的逻辑是：

```csharp
CardModel starter = GetTranscendenceStarterCard(base.Owner); // 只认本体那 5 张
await CardCmd.Transform(starter, GetTranscendenceTransformedCard(starter));
```

`GetTranscendenceStarterCard` 用的是本体写死的「打击 / 中和 / 释放…」表，**自定义角色的初始卡不在表里** →
返回 `null`；`GetTranscendenceTransformedCard(null)` 里直接 `starterCard.Id` → `NullReferenceException`。

CharForge 生成的 `AfterObtained` 前缀本来接管了换牌，但找不到初始卡时写的是：

```csharp
if (starter is null) return true;   // 交回本体
```

正好把控制权交回给会 NRE 的本体。Loadout 直接往牌组里塞「古老牙齿」时（不经过 `SetupForPlayer`），
`StarterCard / AncientCard` 还是 null，就更容易走到这条路。

## 修法

找不到初始卡时**不再交回本体**：

```csharp
if (starter is null)
{
    Log.Warn("[ModId] 古老牙齿：牌组里没有初始卡「…」，跳过换牌（不交回本体，避免空引用）");
    __result = Task.CompletedTask;
    return false;
}
```

有初始卡时照旧 `CardCmd.Transform(starter, ancient)`，升级 / 附魔逻辑不变。

## 验证

| 项 | 结果 |
|---|---|
| 生成端出现 `if (starter is null)` + `Task.CompletedTask`，不再有「交给本体」 | PASS |
| 开发版 `--uicheck` | **4873 PASS / 0 FAIL** |
| 真编译（含先古补丁的那份探针） | **0 错误** |
| 部署版 `--uicheck` | **4872 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里的 `AncientItemPatches.cs` 仍是 `return true`。
