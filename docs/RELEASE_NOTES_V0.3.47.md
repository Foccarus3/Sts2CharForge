# V0.3.47 — 真正修好 Y 费卡：`base.HasStarCostX` 是 virtual，必须走 override

## 日志证据

你的 V0.3.46 日志：

```text
[Y调试] spent=3/3 last=3 stars=0 Y=0
```

`StarsSpent`、`StarValue`、`LastStarsSpent` 都是 3，**但 `Y` 还是 0**。

## 真因

生成代码的条件是：

```csharp
decimal __Y = base.HasStarCostX ? ... : 0m;
```

`HasStarCostX` 在本体里是 **virtual** 属性，生成的卡写的是：

```csharp
public override bool HasStarCostX => true;
```

但 `base.HasStarCostX` **调用基类实现，绕过 override** → 运行时取到 `false` → 三元表达式直接走 `0m`。

所以之前 `Resources.StarsSpent` / `LastStarsSpent` 明明都是 3，Y 仍然是 0；命中次数 `Math.Max(1, Y)` = 1，生效次数 `Math.Max(0, Y)` = 0。

## 修法

所有生成代码里把 `base.HasStarCostX` 改成 `HasStarCostX`（走当前卡的 override）：

```csharp
decimal __Y = HasStarCostX ? (decimal)(...) : 0m;
```

同时修了公式内置量 `YSpent` 的同一处 `base.HasStarCostX`。

## 另外：卡面资源图标变回星辉

`ExtraResourceCardIconPatch` 以前只在 `NCard._Ready` / `ModelChanged` 时替换 `%StarIcon`；
本体在刷新费用 / 打出 / 升级后会重新设置贴图，于是又变回本体星辉。

V0.3.47 加了 `NCard._Process` 每帧兜底（`ExtraResourceCardIconKeepPatch`），
和计数器那边的 `NStarCounter._Process` 一个套路。

## 验证

| 项 | 结果 |
|---|---|
| 生成 Y 条件改用 `HasStarCostX`（走 override） | PASS |
| 卡面资源图标 `NCard._Process` 每帧兜底 | PASS |
| 生成工程真编译 | 0 错误 |
| 开发版 `--uicheck` | **4900 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4899 PASS / 0 FAIL** |

⚠ 需要重新生成工程；旧工程里还是 `base.HasStarCostX`。
