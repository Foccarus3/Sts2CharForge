# V0.3.26 — 自定义状态页有两个「效果种类」下拉，下面那份能选到状态不支持的效果

用户截图：自定义状态页里，上面「效果种类」只能选状态支持的 42 种，下面还有一个全量下拉（74 种），
在下拉里选「伙伴攻击」这类状态链不支持的效果，生成时才被校验器报错。

## 真因

自定义状态的效果编辑器是两份模板叠出来的：

- `EffectEditorPower` 自己在最上面摆了一个**收窄过**的下拉，候选是 `PowerKinds`（只含状态钩子里能用的种类）；
- 下面又复用了所有编辑页共用的 `EffectCommon`，它自带一个**全量**的「效果种类」下拉（`Kinds`）。

两个下拉绑的是同一个 `Kind`：

- 从上面选 → 只能选 `PowerKinds`；
- 从下面选 → 能选到 `Kinds` 里有、`PowerKinds` 里没有的种类（伙伴攻击 / 抽牌 / 选牌…）；
- 选完上面那个下拉会空着，生成时校验器再报「状态里不能用这个效果」。

## 修法

保留上面那个 `PowerKinds` 下拉作为唯一入口；在 `EffectEditorPower` 复用的
`EffectCommon` 上打 `Tag="Power"`，`EffectCommon` 里的全量下拉用 `DataTrigger` 判断
最近的 `ContentControl.Tag == "Power"` → `Visibility=Collapsed`。

- 自定义状态页：只剩 `PowerKinds` 下拉，选不到不支持的效果；
- 卡牌 / 遗物 / 药水 / 锻造页：`EffectCommon` 的 `Tag` 不是 `Power`，全量下拉照旧可见（输出不变）。

## 验证

自检新增 4 条（V0.3.26），其中两条是**真的把 `EffectCommon` 实例化**出来看视觉树：

| 断言 | 结果 |
|---|---|
| `PowerKinds` 比 `Kinds` 少（42 vs 74） | PASS |
| 「伙伴攻击」在 `Kinds`、不在 `PowerKinds` | PASS |
| `Tag="Power"` 时 `EffectCommon` 的全量下拉 `Collapsed` | PASS（找到 1 个，可见 0） |
| 不带 `Tag` 的普通编辑器里同一份下拉仍 `Visible` | PASS（找到 1 个，可见 1） |

| 项 | 结果 |
|---|---|
| `dotnet build Sts2CharForge.sln -c Debug` | **0 错误** |
| `--uicheck`（开发版） | **4865 PASS / 0 FAIL** |
| 部署版 `--uicheck` | **4864 PASS / 0 FAIL** |

⚠ 旧存档里已经选了不支持效果的状态：打开后上面下拉会是空的，改成 `PowerKinds` 里的种类即可；
生成端 / 校验器的拦截逻辑没有改。
