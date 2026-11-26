# V0.3.127 — 静态立绘拆分 + 死亡倒地立绘 + 美味饼干图标

## 美术资源页

原来的「静态立绘（战斗 / 商店 / 篝火）」一个槽位，现在拆成：

| 槽位 | 用在哪 |
|---|---|
| 战斗静态立绘 | 普通战斗 |
| 商店静态立绘 | 商店 / 商人房 |
| 篝火静态立绘 | 篝火 / 休息处 |
| 死亡倒地立绘 | 战斗死亡 / 结算画面 |
| 通用静态立绘（旧 / 回退用） | 旧存档的 `CharacterStatic`，三个新槽位留空时回退到它 |

另外新增：

| 槽位 | 作用 |
|---|---|
| 替换遗物「美味饼干」图标 | 只替换本体遗物 `YummyCookie` 的小图标 / 大图标 |

## 生成结果

- 战斗场景用 `images/characters/<slug>_static_combat.png`
- 商店场景用 `_static_merchant.png`
- 篝火场景用 `_static_rest.png`
- 死亡倒地立绘拷成 `_static_death.png`，只写进战斗场景的 `DeadVisuals` 节点

没上传的槽位：

- 新三张留空 → 回退到「通用静态立绘」；
- 通用也空 → 工具自带占位图 / 中性占位图；
- 死亡槽位留空 → **不生成死亡替换补丁**，死亡时保持原站姿（不会把站姿当倒地图）。

## 死亡倒地

静态立绘没有本体的 Spine `die` 动画，所以生成两个补丁：

1. `NCreature.StartDeathAnim` 前缀：战斗里死亡时隐藏 `Visuals`，显示 `DeadVisuals`；
2. `NGameOverScreen.MoveCreaturesToDifferentLayerAndDisableUi` 后缀：结算画面把战斗视觉搬过去之后，同样切到倒地图。

只对**没有 Spine 动画**的本模组静态角色生效；有 Spine 的本体角色不受影响。

## 美味饼干图标

上传后：

- 拷贝到 `images/relics/<模组ID>_yummy_cookie.png`；
- 生成小图标 / 描边用的两个 `AtlasTexture`；
- 生成 `cs/YummyCookieIconPatch.cs`，只把 `YummyCookie.IconBaseName` 换成这个新名字。

不会改本体图集，也不会影响其他遗物。

## 生效方式

更新到 V0.3.127，重新生成工程并重装 DLL/PCK。

旧存档不需要改：原 `CharacterStatic` 仍保留为通用回退。

## 验证

| 项 | 结果 |
|---|---|
| 生成的四张静态图路径互相独立 | 通过 |
| 战斗场景包含 `DeadVisuals` + 死亡补丁 | 通过 |
| 生成的美味饼干 `.tres` / 大图完整 | 通过 |
| 生成工程对 v0.114 `sts2.dll` 编译 | **0 errors** |
| 开发版 `--uicheck` | **4973 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4972 PASS / 0 FAIL** |
