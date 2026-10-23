# V0.2.231 —— 「播放音频文件」底下新增一栏：**这段音频会不会被下一个自定义音频顶替**

> 来源：你要求「音频效果底下增加音频是否被下一个自定义音频顶替」。

## 1. 以前的行为（= 现在的默认）

每次「播放音频文件」都是一次**新开一个 `AudioStreamPlayer`**（挂到场景根、放完自己 `QueueFree`），
所以几段音频**各放各的、会叠在一起响** —— 上一段不会因为下一段开始而停。这也意味着：
一条长音效（比如开场音乐）被反复触发时，会一层一层叠上去。

## 2. 新加的那个勾

「播放音频文件（我上传的）」这条效果底下多一栏：

```
☐ 会被下一个自定义音频顶替（不勾 = 允许几段音频叠着响）
```

- **不勾（默认，老行为）**：和以前完全一样，几段叠着响；生成出来的代码**逐字不变**
  （还是那几句内联的 `var __sfx = new AudioStreamPlayer(); …`）。
- **勾上**：这段音频还在放的时候，只要**又播放了另一个「我上传的音频」**
  （卡牌 / 遗物 / 药水 / 自定义状态 / 锻造节点图里都算），**这一段立刻被停掉**、由新的接手。
  适合「开场音乐」「长音效」「BGM」这类不希望叠音的东西。

## 3. 实现（为什么需要一个生成文件）

勾了任意一条之后，工具会额外生成一个很小的 `cs/ForgeSfx.cs`：

```csharp
public static class <角色>ForgeSfx {
    private static readonly List<AudioStreamPlayer> Replaceable = new();
    public static void Play(string resourcePath, float volumeDb, bool replaceable) {
        // 新的自定义音频开始了 —— 先把「允许被顶替」的那些停掉
        for (int i = Replaceable.Count - 1; i >= 0; i--) { … old.Stop(); old.QueueFree(); }
        var sfx = new AudioStreamPlayer(); … sfx.Play();
        if (replaceable) Replaceable.Add(sfx);      // 记着它，下一段开始时先停掉
    }
}
```

并且**所有**「播放音频文件」都改成走它：

```csharp
<角色>ForgeSfx.Play("res://audio/xxx.ogg", Mathf.LinearToDb(0.8f), replaceable: true);   // 勾了
<角色>ForgeSfx.Play("res://audio/xxx.ogg", Mathf.LinearToDb(0.6f), replaceable: false);  // 没勾
```

- 为什么全走它：不勾的那段开始时也要能把前面「勾过、还在放」的段停掉，否则口径就错了；
- 不勾的时候**一个字都不多写**（不生成 `cs/ForgeSfx.cs`、播放代码和以前逐字一致）；
- 回读标记多一项：`// CET:SfxFile=… CET:SfxVolume=… CET:SfxReplace=1`（没勾就不写这一项），
  `--recover` 能把它认回来；那几行走小助手的语句也算「已识别的实现」，不会报「没认出来」。

## 4. 验收

- 探针 `sacprobe sfxreplace`：生成工程里有 `cs/ForgeSfx.cs`，两条音频分别是
  `replaceable: true` / `replaceable: false`，整份工程 `dotnet build` **0 error**；
  回读得到 `PlaySoundFile:80:replace=True | PlaySoundFile:60:replace=False`，没有认不出来的语句；
- 自检新增 14 条（`--uicheck` **4200 PASS / 0 FAIL**）：显隐只在「播放音频文件」上、
  **红线**（没勾时播放代码逐字不变、不生成文件）、勾了生成 `ForgeSfx.Play(…, replaceable: true)`、
  同一条存档里不勾的那条是 `false`、标记行、小助手源码、整份生成里「勾了才有 `cs/ForgeSfx.cs`」、
  回读两个方向、以及**机械测量**「这一栏只有「播放音频文件」读它」；
- 老存档对拍（新 Core ↔ 已发布 V0.2.230 的 Core）：示例 **113** / WhiteCloak **412** 个文件**只差 `.sln` 的随机 GUID**。
  （为了守住这条红线，新字段 `SfxReplace` 默认 `false` 时**不写进 JSON** —— 锻造图节点的快照里内嵌着
  `EffectSpec`，写进去会让老存档的 `CET:ForgeGraph` 那一行变样。）
