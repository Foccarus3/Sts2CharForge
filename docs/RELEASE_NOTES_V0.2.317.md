# V0.2.317 — 药水「指定敌人」打错人 + 遗物「获得药水」指向自己的药水

你报的两条，都查到了根因：

## ① 药水指向指定敌人，多目标时只给第一个敌人

药水**效果列表**里的「指定敌人」其实一直是好的（用的是 `OnUse` 的 `target`）。
出问题的是**药水的锻造节点图**那条路：图上的「打谁」默认值以前对药水是 `null`，
于是运行时退回「可打的第一个敌人」（`TargetOrFirst(__fgTarget, …)`）—— 场上有两个以上敌人时，
你选中右边那个，伤害/状态还是落在左边第一个身上。

改法：药水的图上默认目标 = `OnUse` 的 `target`：

```csharp
protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
{
    Creature? __fgTarget = target;   // 药水使用时选中的那个目标（OnUse 的参数）
    …
}
```

（卡牌 / 状态 / 遗物那几档不受影响：卡牌用 `cardPlay.Target`，状态用触发器的目标，遗物没有「选中的目标」。）

## ② 遗物「获得药水」会卡死

根因是**药水类名套错了命名空间**。「获得药水（指定一瓶）」生成的是：

```csharp
await PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.XXX>(base.Owner);
```

这是**本体**药水的位置；你自己做的药水在模组自己的命名空间里 —— 于是那句引用了一瓶「本体的 XXX」：
轻则编不过，重则运行时那句 `ModelDb.Potion<T>()` 抛异常、钩子里的 await 链断在中间，表现就是**卡住不动**。

改法：生成时先按存档里的药水表查类名，自己做的药水直接写 `TryToProcure<自己的类>`；本体药水照旧带本体命名空间。

## 验收

* 自检：**4529 PASS / 0 FAIL**（+4 条 V0.2.317 断言：药水效果用 `target` / 药水图上 `__fgTarget = target` /
  自己的药水不带本体命名空间 / 本体药水照旧）。
* 探针 `sacprobe potiondump`（药水 2 条「指定敌人」效果 + 药水带节点图 + 遗物「获得药水」指向自己的药水）：
  生成 `Apply<WeakPower>(choiceContext, target, …)`、`__fgTarget = target`、`TryToProcure<ProbePotion>`，
  整份工程 **`dotnet build` 0 错误**。
* 逐字节比对：Sample 113 个文件、WhiteCloak 412 个文件**全部相同**（只有 `.sln` 的 GUID 不同）。
* 只出更新包（`-NoBundle`）。

## 你要做的

重新生成 + `② 一键构建 + 安装`。若卡死还在，请把当时的 `godot…log` 发我 ——
正常应该能看到「药水栏满 / NotAllowed」之类的原因（本体 `TryToProcure` 返回 `success=false`）。
