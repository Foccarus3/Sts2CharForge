# V0.3.77 — 修复「混沌芳香」升级分支缺本地化导致卡事件

## 问题

事件「混沌芳香」：

- 选「变化」正常；
- 选「保持控制 / 升级」后，选完要升级的牌，事件卡住。

日志：

```text
LocException: Key=YUXIANG.aromaPrinciple not found in table=characters
  at AromaOfChaos.MaintainControl()
  at CardSelectCmd.FromDeckForUpgrade(...)
```

## 原因

本体事件 `AromaOfChaos.MaintainControl()` 在升级分支会读取：

```text
characters.<角色 ID>.aromaPrinciple
```

生成器只给角色写了 `title / description / pronouns / ...`，**没有写这条**，所以模组角色一选升级就抛 `LocException`，事件停在半路。

## 修复

`LocalizationGen.CharactersJson` 现在给每个生成角色补：

```json
"<角色>.aromaPrinciple": "……我选择保留这份力量。"
```

所以所有模组角色都会带上这条，不再只有变化分支能用。

## 用户侧

必须重新生成工程，`localization/zhs/characters.json` 里才会出现：

```json
"YUXIANG.aromaPrinciple": "……我选择保留这份力量。"
```

旧工程不会自动更新。

## 验证

| 项 | 结果 |
|---|---|
| characters.json 含 `aromaPrinciple` | 生成端已补 |
| 开发版 `--uicheck` | **4916 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4915 PASS / 0 FAIL** |
