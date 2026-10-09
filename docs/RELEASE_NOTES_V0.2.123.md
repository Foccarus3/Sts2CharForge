# V0.2.123 更新说明

> 你要求的：「**自己受到伤害时**不该被**失去生命**触发」（触发时机里已经有「自己失去生命时」了）。

## 一、改了什么

给「自己受到伤害后（每一下）」和「自己造成伤害后」加了同一句守卫，把**失去生命那一类掉血**排除掉：

```csharp
// 「失去生命」不算「受到伤害」：本体 DamageProps.cardHpLoss = Unblockable | Unpowered
//（血祭 / 献祭 / 中毒这类穿盾且没有来源加成的掉血）—— 它由「自己失去生命时」那条时机负责。
if (props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered)) return;
```

**三处守卫逐字同款**（遗物页 / 状态页 / 锻造页），位置都在原来那条「只认自己」的守卫之后、其它判据之前。

**没改的地方（明确说明）**：

- **敌人侧**的「敌人受到伤害后 / 敌人造成伤害后」**故意不排** —— 敌人那边没有「敌人失去生命」这条时机可替代，排掉就等于功能丢了（三份守卫里都写了注释）；
- **「对自己造成伤害」照旧算伤害**：它生成的是 `ValueProp.Unpowered | Move`（**可格挡**，不同时满足 `Unblockable`），所以不会被这句守卫误伤；
- 本体口径不变：血祭 / 献祭 / 中毒在游戏里**仍然是伤害**（`Hook.AfterDamageReceived` 在 `CreatureCmd.Damage` 里无条件广播），只是我们这条「受到伤害后」的时机不再收它 —— 想要那种触发请用「**自己失去生命时**」。

## 二、验证

- `dotnet build Sts2CharForge.sln -c Debug` **0 错误**；发布版 `--uicheck` **2781 PASS / 0 FAIL**（V0.2.122 是 2776，新增 5 条断言：守卫位置、三份守卫一致、敌人两条不带、自伤两种链的 props）；
- **「只有这两处守卫变化」对照**：拿 `ProfileFactory.Sample()` + 有 `DamageReceived`/`DamageDealt` 的遗物 + 自定义状态 + 一条「对自己造成伤害」+ 敌人两条，完整生成 138 个文件，再逐行减掉新守卫得到「改动前」——
  **内容有差异的文件 3 个、新增守卫 4 处、意外差异 0 处**；`CmpSelfDamageRelic` / 敌人两个文件里新守卫 0 处；
- 生成的整份工程真 `dotnet build` **0 错误**；既有探针回归 `_tmp\relicmulti` **55/0**、`_tmp\selfdmgprobe` 四条自伤路均不含 `Unblockable`；
- 文档同步：`README.md` 与 `tools\使用说明.template.txt` 里那条旧说法（「血祭也算受到伤害」）已改成现在的口径。

## 三、连带说明

- 自检要用到三份守卫函数，`CSharpCodeGen.RelicTriggerGuardLines` / `ForgeTriggerGuardLines` 的可见性从 `internal` 改成 `public static`（生成逻辑一字未动）；
- 打包：`Sts2CharForge_更新包_V0.2.123.zip`。
