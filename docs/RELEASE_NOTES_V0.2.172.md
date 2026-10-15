# V0.2.172 更新说明

> 用户要求：「**在角色页底下增加多人催促文本，增加角色最大能量**」。

## 一、多人催促文本：角色页底部也能填（**同一份数据**，美术页那个框还在）

* 「联机进宝箱房时屏幕中间那句催促文本」这个功能本来就有，只是摆在**「美术」页**槽位列表下面
  （Profile.Art.TreasureFightText）。现在**在「角色」页最底下**再摆一个输入框，绑的**就是同一个属性**
  —— 两边同步、**存档格式一个字都不变**，美术页那个框**没有删**（老自检 / 文档都指着它）。
* 文案：「多人催促文本（联机进宝箱房时屏幕中间那句）」+ 说明「留空 = 本体『开抢！！！』；
  和「美术」页那个框是同一份数据（改哪边都一样）；只改这一局、不动本体本地化」。
* 生成端、回读都没动（还是那份 TreasureFightTextPatch 与 CET:TreasureFightText= 标记）。

## 二、角色最大能量（新字段 Profile.MaxEnergy，默认 3 = 本体默认）

**引擎依据**（不需要任何补丁）：本体 Core/Models/CharacterModel.cs:97 是
`public virtual int MaxEnergy => 3;`；Player 建号时取 character.MaxEnergy，
PlayerCombatState.MaxEnergy => Hook.ModifyMaxEnergy(..., _player.MaxEnergy)，每回合 ResetEnergy()
按它回满。所以生成的角色类里写一句 public override int MaxEnergy => N; 就是「这个角色每回合的能量上限 = N」。

* **硬红线**：MaxEnergy == 3（本体默认）时生成端**一个字都不写** —— 老存档 / --samplegen 基线逐字不变。
  实测：默认存档生成的角色类里 MaxEnergy 出现 **0 次**；MaxEnergy = 5 时才写
  public override int MaxEnergy => 5;。
* 界面：「角色」页表单**新加第 10 行**「角色最大能量」（0~8 行一个都没动），旁边有灰色说明
  「留 3 = 不覆盖（生成结果和以前逐字一样）」。
* 校验器：1~99 之外报**错误**；不是 3 时给一条**提示**「改了会明显改难度」（3 时一条都不出，免得刷屏）。
* 回读：MaxEnergy => (\d+)，认不到就**回退 3**；恢复摘要里写「能量 N」。
* README 摘要：生命 … · 金币 … 后面补「· 能量 N」——**只在不是 3 时印**
  （默认角色那份 README 与老基线逐字一致，见 ModGenerator.EnergySummary）。

## 三、验收数字（全部真跑）

| 项目 | 结果 |
|---|---|
| dotnet build（Core / App / Cli） | **0 错误** |
| --energyprobe（新探针：MaxEnergy=5 + 催促文本） | 校验只出「提示」不出错误；生成里 public override int MaxEnergy => 5;；默认 3 的角色类里没有 MaxEnergy；TreasureFightTextPatch 会生成 |
| **用户存档副本**（MaxEnergy=5 + 催促文本）--profile … --build | **编译 DLL OK**，角色类 Sparkle.cs 里就是那一句 public override int MaxEnergy => 5; |
| 默认 3 的用户存档（未改） | 角色类里 MaxEnergy **0 处**（红线），照旧生成 |
| --recover（MaxEnergy=5 那个工程） | 「角色：花火（Sparkle）HP 72 / 金币 99 / **能量 5**」；催促文本也认回来了；恢复出的 json MaxEnergy = 5 |
| --recover（默认 3 的工程） | 「… **能量 3**」（回退值对） |
| --uicheck | Debug **3762 PASS / 0 FAIL**（比 V0.2.171 的 3757 多 5 条：角色页催促文本框 / 角色页最大能量框 / 默认 3 不写 override / 5 才写 / 校验器 1~99 之外报错） |
| --samplegen 对比基线 | 113 个文件里**只有 2 个不同**（ExampleMod.sln 随机 GUID、SevenGuard.cs 的 GainsBlock）—— **没有扩大** |

## 四、还没做（老账，不含糊）

--recover 对「状态链里从某一摞拿牌到手牌」的还原；「升级后变 X 费」实验档；自定义状态自动图标兜底；
节点图「将此牌放回手牌」只做在「牌已不在手牌」的时机、「斩杀」只做在卡牌档（校验器都会说清）。
