# V0.3.25 — 两张布同用 OnTurnEndInHand：属性重复声明（V0.3.8 缺陷报告）

按 `Sts2CharForge_V0.3.8_锻造触发时机_两张布重复属性_缺陷报告.md` 复现并修复。

## 真因

`EmitGraphOnlyHookContainers` 在「升级前 / 升级后两张布都用同一个插件时机」时，
会在 `.Open(sig)` **之前**正确声明一次：

```csharp
public override bool HasTurnEndInHandEffect => true;
```

但在 `default` 分支（此时**方法体已经打开**）又补了一次：

```csharp
default:
    w.Line("if (base.CombatState is null) return;");
    if (!string.IsNullOrWhiteSpace(plugGuard)) w.Line(plugGuard!);
    if (hook == "OnTurnEndInHand")
        w.Line("public override bool HasTurnEndInHandEffect => true;");   // ← 落进方法体
    break;
```

于是生成的卡牌类里属性出现两次（一次类级、一次方法体内），
编译器报 CS1513 / CS1026 / CS1031 / CS1519，整个生成工程编译不过。

## 修法

删掉 `default` 分支里那段补充声明，**只保留 `.Open(sig)` 之前的那一次**。
`OnTurnEndInHand` 的 override 仍然正常生成，`base.IsUpgraded` 二选一逻辑不变。

## 回归

自检新增一条（V0.3.25）：

* 模拟一个带 `signature` 的插件时机 `OnTurnEndInHand`；
* 同一张卡「升级前 / 升级后」两张布都选它；
* 断言生成文本里 `HasTurnEndInHandEffect` **恰 1 处**、`OnTurnEndInHand` 的 override **恰 1 处**，
  且属性位置在 override 之前（类成员层，不在方法体里）。

## 验证

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4861 PASS / 0 FAIL**（+1 条 V0.3.25 断言） |
| 部署版 `--uicheck` | **4860 PASS / 0 FAIL** |

⚠ 之前已经生成的工程需要重新生成才会消掉那两行重复声明。
