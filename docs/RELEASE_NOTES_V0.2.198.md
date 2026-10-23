# V0.2.198 —— 召唤物两处修好：再召唤不再把宠物奶满 / 没上传图片也会生成占位场景

> 用户原话：「触发召唤时好像会直接把召唤物回满血」+ `godot - 副本.log`。

## 1. 再召唤（宠物已经在场）不再把它回满血

生成的 `XxxCmd.Summon(...)` 以前是这样：

```csharp
if (alive is not null) {                       // 已经在场
    await CreatureCmd.GainMaxHp(alive, hp);    // 加最大生命（本体实现里已经会把「新加的那部分」补满）
    await CreatureCmd.Heal(alive, Math.Max(0m, alive.MaxHp - alive.CurrentHp));   // ← 多补这一句 = 直接奶满
    ...
}
```

本体 `CreatureCmd.GainMaxHp` 的实现是 `SetMaxHp(MaxHp + amount)` **再 `Heal(实际加上的上限)`** ——
也就是「加多少上限就补多少血」，身上原来的伤是留着的。所以那句 `Heal(MaxHp − CurrentHp)` 等于
**把召唤物直接奶满**，之前挨的打全没了。**已删掉**：现在再召唤一次只加最大生命（更结实），
血量按「加多少补多少」走，不会回满。

## 2. 没上传图片 → 现在也会生成占位场景（这才是「每次召唤都是满血新宠物」的根因）

日志里其实是这两行：

```
ERROR: Cannot open file 'res://scenes/creature_visuals/nuist_pet.tscn'.
[ERROR] Encountered exception loading the creature visuals for 信兵.
        Falling back to error scene. Exception: System.NullReferenceException
        at ... MonsterModel.CreateVisuals_Patch2 ...
        at ... PlayerCmd.AddPet[T](Player player)
        at Nuist.NuistPetCmd.Summon(...)
        at Nuist.SevenCharm.BeforeSideTurnStart(...)
```

以前「召唤物」页没上传图片时，工具**不写** `scenes/creature_visuals/<entry>.tscn`，也不覆写
`VisualsPath`，指望本体按约定回退 `creature_visuals/fallback` —— 但那条回退路在真机上会抛
`NullReferenceException`（`MonsterModel.CreateVisuals` 被补丁包（RitsuLib 那套）改过之后尤其容易踩），
**于是 `PlayerCmd.AddPet` 半路挂掉、这只宠物根本没登记** → 下一次触发召唤时 `Get(player)` 还是 null
→ 又新建一只**满血的新宠物**。看起来就是「每次召唤都回满血」。

现在：

* 不管有没有上传图片，**都会写** `scenes/creature_visuals/<entry>.tscn`（没图就贴本体的
  `images/monsters/error.png` 当占位，节点结构和 fallback.tscn 一样：`%Visuals` / `%Bounds` /
  `%CenterPos` / `%IntentPos`）；
* 宠物类**永远**覆写 `VisualsPath` 指向这个场景 —— 不再依赖本体「按约定去猜 + 回退」那条会抛异常的路径。

## 3. 验过的

* 真生成一遍（带一只「没上传图片」的召唤物）：场景文件确实生成、里面贴的是 `images/monsters/error.png`、
  四个唯一名节点齐全；整份工程 `dotnet build` **0 error**；
* 生成的 `Summon` 里再召唤那条只剩 `GainMaxHp(alive, hp)`（`alive.MaxHp - alive.CurrentHp` 那句没了），
  新召一只是 `SetMaxHp` + `Heal(hp − 当前)`（新宠物满血，和以前一样）；
* **老存档生成结果一个字节都没变**（示例配置 113 个文件、WhiteCloak 存档 412 个文件 —— 这两份配置里都没有召唤物）；
* 自检：新增 **4 条**断言（再召唤不加满 / 新召仍满血 / 没图也生成占位场景 / 宠物类永远指场景），
  并把一条**过期断言**（原先要求「没图时**不**覆写 VisualsPath」）按新行为改掉；
  整轮 `Sts2CharForge.exe --uicheck` **3933 PASS / 0 FAIL**。

## 4. 升级后你要做的一件事

**重新生成一次模组**（「构建」页 → 生成 + 编译），再进游戏。旧包里宠物场景是缺的，
不重新生成的话这只宠物的视觉还是加载不出来。
