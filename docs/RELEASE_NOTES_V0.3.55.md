# V0.3.55 — 尘封魔典指定卡：优先级 + Postfix 兜底

## 现象

生成器已经生成了正确的：

```csharp
PigDustyTomePatch
{
    Prefix: __instance.AncientCard = ModelDb.Card<PigDukePigDragonFish>().Id;
}
```

但游戏里尘封魔典仍然随机。

## 原因

`DustyTome.SetupForPlayer` 被多个模组同时补丁：

- 本模组的 `PigDustyTomePatch`
- **BaseLib** 的 `DustyTomePatch`
- **RitsuLib** 的 `DustyTomeSetupForPlayerPatch`

RitsuLib 的 Prefix 会从角色卡池里随机选一张先古卡并返回 `false`。
如果它比我们后执行 / 或在我们之后写 `AncientCard`，最终结果就被它改回随机。

## 修法

生成的尘封魔典补丁改成两层：

```csharp
[HarmonyPriority(HarmonyLib.Priority.First)]
private static bool Prefix(DustyTome __instance, Player player)
{
    ...
    __instance.AncientCard = ModelDb.Card<PigDukePigDragonFish>().Id;
    return false;
}

[HarmonyPostfix]
[HarmonyPriority(HarmonyLib.Priority.Last)]
private static void Postfix(DustyTome __instance, Player player)
{
    ...
    __instance.AncientCard = ModelDb.Card<PigDukePigDragonFish>().Id;
}
```

- Prefix 尽量先执行并跳过本体；
- **Postfix 在所有其他 Prefix 之后强制写回指定卡**，即使 RitsuLib 已经随机选过，也会被覆盖回固定卡。

只对勾了「作为尘封魔典给的先古卡」的那张先古卡生成；
没勾的存档仍然走原来的随机排除补丁，输出不变。

## 验证

| 项 | 结果 |
|---|---|
| Prefix 带 `Priority.First` | PASS |
| 生成 `Priority.Last` 的 Postfix 强制写回 | PASS |
| 未勾选时仍走旧的随机排除补丁 | PASS |
| 开发版 `--uicheck` | **4909 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4908 PASS / 0 FAIL** |

⚠ 需要重新生成「EdrsGooglePig」工程并重新构建；旧工程里只有普通 Prefix。
