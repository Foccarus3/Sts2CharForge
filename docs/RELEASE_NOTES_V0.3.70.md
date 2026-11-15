# V0.3.70 — 主菜单「百科大全」也显示模组角色的卡池筛选器

## 现象

游戏主菜单打开「百科大全 / 卡牌图鉴」时，看不到模组角色的筛选按钮 / 角色卡池。

## 原因

`CardLibraryGen` 的补丁只在**局内**加筛选器：

```csharp
if (me is not <角色>) return;
```

主菜单没有进行中的局，`me` 是 `null`，直接 return，所以主菜单图鉴永远不加模组角色。

## 修改

生成补丁里改成：

```csharp
if (me is null) me = ModelDb.Character<角色>();
else if (me is not 角色) return;
```

- 主菜单（没有局）→ 用模组角色补一个筛选器；
- 局内玩的是模组角色 → 照旧；
- 局内玩的是别的官方角色 → 仍然不碰，避免把模组卡混进别的角色图鉴。

## 验证

| 项 | 结果 |
|---|---|
| 主菜单百科补丁 | 不再因 `me == null` 提前返回 |
| 局内别的角色 | 仍然不添加 |
| 开发版 `--uicheck` | **4915 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4914 PASS / 0 FAIL** |

⚠ 必须重新生成工程；旧 `ForgeCardLibrary.cs` 不会自己变。
