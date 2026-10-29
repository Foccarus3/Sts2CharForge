# V0.2.324 — 修 V0.2.322 引入的编译错误：状态序幕里 `PetOwner?.Player`

> 先认账：这是我上一版（V0.2.322）**自己写出来的**编译错误，不是你的配置问题。

## 报错长这样

```
cs\Powers\Bleeding.cs(19,70): error CS1061: "Player" 未包含 "Player" 的定义
cs\Powers\SanguineDesire2.cs(20,70): error CS1061: "Player" 未包含 "Player" 的定义
…（凡是带节点图的自定义状态，每只一条）
```

## 真因

V0.2.322 为了让「挂在敌人身上的状态」也能算出图上的 X / Y，把序幕写成：

```csharp
Player __fgPlayer = base.Owner.Player ?? base.Owner.PetOwner?.Player ?? base.Owner.CombatState!.Players.First();
//                                    ^^^^^^^^^^^^^^^^^^^^ 这里错了
```

本体 `Creature.PetOwner` 的类型**本来就是 `Player?`**（不是 Creature），所以 `PetOwner?.Player` 是在
`Player` 上找 `Player` 属性 → CS1061。改对只要去掉那一段：

```csharp
Player __fgPlayer = base.Owner.Player ?? base.Owner.PetOwner ?? base.Owner.CombatState!.Players.First();
```

## 验收

* 探针 `sacprobe powergraph`（造一只和「流血」一样的图状态：触发时机 = 自己造成伤害后 → 失去生命）：
  生成上面那句，整份工程 **`dotnet build` 0 错误**（这条探针就是为了不再放跑这类错误加的）。
* 自检：**4543 PASS / 0 FAIL**（+1 条断言：序幕必须是 `Owner.Player ?? Owner.PetOwner ?? …`，且不得出现 `PetOwner?.Player`）。
* 只出更新包（`-NoBundle`）。

## 你要做的

重新生成 + `② 一键构建 + 安装`。这次状态那批文件（`cs\Powers\*.cs`）应该全部编过。
