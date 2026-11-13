# V0.3.56 — 适配游戏 v0.113（新 API + 新 CardTag）

## 目标

只适配新版 **v0.113**，暂不再兼容 v0.111。

## API 适配

| 旧版 | v0.113 | 修改 |
|---|---|---|
| `MonsterModel.IsHealthBarVisible` | `MonsterModel.IsInteractable` | 宠物生成的 override 改名 |
| `NCombatRoom.SetCreatureIsInteractable(pet, on)` | `NCreature.SetIsInteractable(on, changeVisibilityImmediately)` | 宠物重新开血条改用节点方法 |
| `AfterDeath(..., bool, float)` | `AfterDeath(..., CardModel? source, bool, float)` | 卡牌 / 自定义状态的「敌人死亡后」补 `source` 参数 |
| `CardModel.AfterTransformedFrom()` | `CardModel.AfterThisWasTransformedIntoInternal(CardModel card)` | 「被变化」时机改用新虚方法 |

`Sts2Metadata.SupportedGameVersion` 改成 **v0.113.0**；旧 v0.111 会提示版本不匹配。

## 新 CardTag

v0.113 的 `CardTag` 新增：

```text
Wah / Cho / Ki / Shoot
```

已加入：

- `EffectCatalog.CardTags`（卡牌属性页的标签候选）
- `CardTagZh`（界面中文说明）
- 工程回读 / 校验 / 卡牌属性 `Tags` 的识别

属性页里现在可以直接勾选这些新标签。

## 验证

| 项 | 结果 |
|---|---|
| v0.113 API 生成工程编译 | **0 错误** |
| 宠物新 API 编译 | PASS |
| 新 AfterDeath / Transform 签名编译 | PASS |
| 新 CardTag 进入候选表 | PASS |
| 开发版 `--uicheck` | **4910 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4909 PASS / 0 FAIL** |

⚠ 必须重新生成工程；旧工程里的 `Pet.cs` / `AfterDeath` / `AfterTransformedFrom` 仍是旧 API。
