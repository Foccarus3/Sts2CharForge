# V0.3.144 —— 多触发时机的遗物用「每累计花掉 N 点」时，两个计数基准字段没声明（CS0103）

> 用户转来的构建日志（V0.3.140）：生成、Godot 导入、导出 PCK、安装全过，**编译 DLL 失败**：
> ```
> cs\Relics\Bud.cs(32,38):            error CS0103 当前上下文中不存在名称"__spentBaseCombat"
> cs\Relics\BlossomofCalamity.cs(82,38): error CS0103 当前上下文中不存在名称"__spentBaseCombat"
> ```

## 复现

| 遗物的触发时机条数 | 生成结果 |
|---|---|
| 1 条（每累计花掉 3 点能量） | 引用 `__spentBaseTurn`、**声明了**字段 → OK |
| **2 条**（每累计花掉 3 点 + 战斗开始时） | 引用 `__spentBaseTurn`、**没有声明** → CS0103 |

复现出来的那一行：

```csharp
if ((__spentBefore - __spentBaseTurn + __EnergySpentIdx + 1) % 3 != 0) continue;   // 从这个遗物生效那一刻起数
```

## 真因

「每累计花掉 N 点能量 / 额外资源量」需要两个**实例字段**记基准：

```csharp
private int __spentBaseTurn = -1;
private int __spentBaseCombat = -1;
```

但遗物生成有**两条路径**：

- **单触发**（只有 1 条触发时机）→ 会声明这两个字段；
- **多触发**（≥2 条时机，按「钩子」分组、同一条钩子只生成一个 override）→ **只引用、不声明**。

他这两个遗物都是多条触发时机（其中一条是「每累计花掉 N 点」），正好踩在第二条路径上。
（V0.3.100 修过的是这两个字段在**单触发**路径里的位置 —— 以前被写进了 `CanonicalVars` 的数组里；
多触发这条路从来没声明过。）

## 修法

在多触发路径的类体里补一次声明（只要**任意一条**时机是 `EnergySpent` / `StarsSpent`）：

```csharp
if (triggers.Any(t => t is not null && PerPointTrigger(t.Kind)))
    w.Line("private int __spentBaseTurn = -1;   // 「每累计花掉 N 点」的基准（-1 = 还没记过）")
     .Line("private int __spentBaseCombat = -1;");
```

两个字段是**类级共享**的 —— 多个钩子共用同一份基准正是想要的语义（「从这个遗物生效那一刻起数」）。

## 验证

- 探针 `_probe/relmultispent`：单触发 / 双触发两种配置现在都是「引用 = True、声明 = True」
- `--uicheck` **5028 PASS / 0 FAIL**，新增 2 条：
  - 多触发遗物里的「每累计花掉 N 点」→ 基准字段**引用与声明成对**
  - 红线：没用到「每累计花掉 N 点」的遗物**不会**多出这两个字段
- 部署版自检：见下（发版后跑）

## 用户侧

不需要改配置：升级后重新「生成工程 / 一键构建 + 安装」即可，那 2 个 CS0103 会消失。

（旧版本上的临时绕法：把这两个遗物各自合并成**只有 1 条触发时机** —— 比如把「战斗开始时」那条效果并进「花掉能量后」，
或者把那条效果挪到另一个遗物上。）

## 版本

- `V0.3.144`
