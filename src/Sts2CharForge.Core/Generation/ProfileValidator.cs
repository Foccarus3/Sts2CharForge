using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

public sealed record ValidationIssue(string Level, string Message)
{
    public bool IsError => Level == "错误";
    public override string ToString() => $"[{Level}] {Message}";
}

/// <summary>生成前的配置校验：把"编译不过/进游戏会崩"的问题挡住。</summary>
public static class ProfileValidator
{
    public static List<ValidationIssue> Validate(CharacterProfile p)
    {
        var issues = new List<ValidationIssue>();
        var n = Naming.From(p);

        // 锻造【效果】模块：老字段 → EffectSpec 幂等迁移；「新编辑器改过」的再回写成老字段。
        // 校验器读的是老字段（和生成端同一份），所以必须先同步，否则会「界面填了、校验还说没填」。
        ForgeEffectBridge.Normalize(p);
        foreach (var fg in ForgeEffectBridge.GraphsOf(p)) ForgeEffectBridge.SyncGraph(fg);
        // V0.2.146：「数值公式」框里填了**纯常数**（例：1 / 2+3）的，先折进固定数值再校验 ——
        // 用户会顺手把数字填进那个框，以前会被下面那条「不支持数值公式」直接拦住（用户实测报的）。
        // 折完之后这里看到的只剩「真·运行时公式」（X / M*3 / HandCount…），照旧报错拦住。
        ProfileFactory.FoldPlainConstantAmountFormulas(p);

        if (!Naming.IsValidIdentifier(p.ModId))
            issues.Add(new("错误", "模组 ID 必须是纯英文/数字且以字母开头（同时作为 pck/dll/json 文件名）。"));

        // 自定义关键词：名字 / 英文标识（本地化键）合法性、撞本体、重名、被卡牌引用却不存在
        var keywordMap = KeywordGen.All(p);
        var seenKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int ki = 0; ki < keywordMap.Count; ki++)
        {
            var (spec, key) = keywordMap[ki];
            string who = $"自定义关键词 #{ki + 1}";
            if (string.IsNullOrWhiteSpace(spec.Name))
                issues.Add(new("错误", $"{who} 还没填名字（名字就是卡面上显示的那个词）。"));
            else
                who = $"自定义关键词「{spec.Name.Trim()}」";

            if (string.IsNullOrWhiteSpace(spec.Key))
                issues.Add(new("警告", $"{who} 没填英文标识，生成时会自动用 {key} 当本地化键（不影响游戏内显示）。"));
            else if (KeywordGen.IsReservedKey(spec.Key))
                issues.Add(new("错误", $"{who} 的英文标识「{spec.Key.Trim()}」和本体关键词撞名"
                    + "（NONE / EXHAUST / ETHEREAL / INNATE / UNPLAYABLE / RETAIN / SLY / ETERNAL 是本体占用的），换一个。"));
            else if (!string.Equals(key, KeywordGen.NormalizeKeyText(spec.Key), StringComparison.OrdinalIgnoreCase))
                issues.Add(new("警告", $"{who} 的英文标识会规范成 {key}（只能用英文 / 数字 / 下划线）。"));

            if (seenKeys.TryGetValue(key, out var first))
                issues.Add(new("错误", $"{who} 的英文标识和「{first}」重复（都是 {key}），本地化键会互相覆盖。"));
            else
                seenKeys[key] = string.IsNullOrWhiteSpace(spec.Name) ? key : spec.Name.Trim();

            if (string.IsNullOrWhiteSpace(spec.Description))
                issues.Add(new("警告", $"{who} 没写说明：鼠标悬停在用到它的卡上会弹出一个空面板。"));
        }
        // 自定义关键词的引用检查：普通卡 + 诅咒 + 先古卡都要查（三栏都可能勾关键词）。
        // V0.2.136 起还查「升级后获得 / 失去」两栏（引用不存在 → 错误；同一条同时获得又失去 → 警告）。
        foreach (var card in p.AllCards)
        {
            if (card is null) continue;
            foreach (string r in card.CustomKeywordList)
                if (KeywordGen.Find(p, r) is null)
                    issues.Add(new("错误", $"卡牌「{card.Name}」引用了不存在的自定义关键词：{r}"
                        + "（可能已经被删掉了，去「卡牌」页取消勾选，或到「自定义关键词」页把它加回来）。"));
            // V0.2.136「升级后获得 / 失去的自定义关键词」：引用不存在的关键词同样是**错误**
            // （生成端会跳过它 —— 卡面上什么都没有，用户会以为设置没生效，不许静默）。
            foreach (var (who, refs) in new[]
                     {
                         ("升级后获得", card.UpgradeAddedCustomKeywords),
                         ("升级后失去", card.UpgradeRemovedCustomKeywords),
                     })
            {
                foreach (string r in refs ?? new List<string>())
                    if (KeywordGen.Find(p, r) is null)
                        issues.Add(new("错误", $"卡牌「{card.Name}」的「自定义关键词 · {who}」引用了不存在的自定义关键词：{r}"
                            + "（可能已经被删掉了，去「卡牌」页把这一栏改掉，或到「自定义关键词」页把它加回来）。"));
            }
            // 同一张牌既「升级后获得」又「升级后失去」同一条关键词 → 自相矛盾（生成出来两段都不显示它）
            foreach (string r in card.UpgradeAddedCustomKeywords ?? new List<string>())
                if ((card.UpgradeRemovedCustomKeywords ?? new List<string>())
                    .Any(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(new("警告", $"卡牌「{card.Name}」把同一条自定义关键词「{r}」同时填进了「升级后获得」和"
                        + "「升级后失去」—— 两栏互相抵消，实际效果是升级前后都不显示它。"));
            // V0.2.177：同一条既在「一直在」（卡牌页那栏勾了它）又在「升级后失去」里 —— **不是矛盾**，
            // 而是一种「升级后拿掉」的写法：升级前显示、升级后不显示（生成端两处都以「失去」为准）。
            // 但生成端是**拿掉了**它（升级后不再显示），用户在卡牌页上会看到这个词既在「一直在」那栏、
            // 又在「失去」那栏，容易以为设置没生效 —— 所以这里把实际行为说清楚。
            foreach (string r in card.UpgradeRemovedCustomKeywords ?? new List<string>())
            {
                bool alsoAlwaysOn = (card.CustomKeywordList ?? new List<string>())
                    .Any(x => string.Equals(KeywordGen.KeyOfReference(p, x), KeywordGen.KeyOfReference(p, r),
                        StringComparison.OrdinalIgnoreCase));
                if (alsoAlwaysOn)
                    issues.Add(new("提示", $"卡牌「{card.Name}」的自定义关键词「{r}」既勾在「一直在」里、又填进了"
                        + "「升级后失去」—— 实际行为是**升级前显示、升级后不显示**（「失去」那栏优先）。"
                        + "如果你本来就想要这个效果，可以不去管它；想升级前后都显示就把它从「升级后失去」里去掉。"));
            }
        }

        // 本体关键词改名：id 必须是那 7 个之一、名字不能带富文本标记、不能和别的本体关键词撞名
        ValidateVanillaKeywordRenames(issues, p);

        // 自定义参数 / 组（「自定义参数 / 组」页）：名字必须是没被占用的字母、来源配全、组引用存在
        ValidateParamsAndGroups(issues, p);

        // 描述里的富文本标签体检（拼错 / 没闭合 / 交叉嵌套会让游戏里的方括号原样印出来）
        ValidateRichText(issues, p);

        // ===== 召唤物（列表；本体的通用宠物 API，不需要 Harmony 补丁）=====
        // 为什么要在这里拦：卡牌 / 遗物上的「召唤伙伴 / 伙伴攻击」生成出来的代码会引用宠物类，
        // 没启用召唤物的话那个类根本不存在 → dotnet 直接报 CS0103，而用户看不懂。
        ValidateSummon(issues, p);

        // 「在游戏中隐藏那只额外资源量载体遗物」：隐藏是**纯视觉**的（后缀补丁，原方法照跑），
        // 必须保住「遗物栏节点列表长度 = Player.Relics 长度」这条不变量 —— 一破就是越界卡死
        //（V0.2.113~V0.2.137 的真实事故）。这里把它的前提条件钉死（见方法注释）。
        ValidateHideCarrierRelic(issues, p, n);
        // 「获得时」+ 战斗内效果：开局那一刻还没有战斗 → 生成出来必 NRE（见方法注释）
        ValidateObtainedCombatEffects(issues, p);

        // 效果库整体为空（换机器后路径失效）→ 只报一条，说清怎么修
        if (EffectCatalog.Powers.Count == 0)
            issues.Add(new("错误", "效果库为空：增益/减益列表读不到。请到「路径」页指定你本机的「本体工程目录」"
                + "（解包后的原版工程，含 src/Core/Models/Powers）或「游戏 data 目录」（含 sts2.dll），"
                + "然后点「构建 / 日志」页的「重新扫描效果库」。"));

        // 自定义状态（能力牌用的「你自己的状态」）
        // 类名唯一：生成是「一个类名一个文件」（cs/Powers/&lt;类名&gt;.cs）—— 见下面那条检查。
        var powerClassNames = new HashSet<string>(StringComparer.Ordinal);
        for (int ci = 0; ci < p.CustomPowers.Count; ci++)
        {
            var cp = p.CustomPowers[ci];
            if (!cp.Enabled) continue;
            if (string.IsNullOrWhiteSpace(cp.Name))
            {
                issues.Add(new("错误", $"自定义状态 #{ci + 1} 还没填名字（名字就是游戏里显示的状态名）。"));
                continue;
            }
            string who = $"自定义状态「{cp.Name}」";

            if (!string.IsNullOrWhiteSpace(cp.ClassName) && !Naming.IsValidIdentifier(cp.ClassName))
                issues.Add(new("错误", $"{who} 的英文类名不合法：{cp.ClassName}（只能是英文/数字，且以字母开头）。"));
            // 类名唯一（V0.2.157 补）：卡牌 / 遗物 / 药水 / 召唤物早就有这条检查，自定义状态一直没有。
            // 为什么必须有：生成是**一个类名一个文件**（<c>cs/Powers/&lt;类名&gt;.cs</c>，见 ModGenerator），
            // 两个状态同名时**后一个写的那次会把前一个整个覆盖掉** —— 前者在游戏里静默消失
            //（触发时机 / 效果 / 描述全丢），而两个状态的本地化键也一模一样（状态栏名字分不开）。
            // ⚠ 真事故：作者自己的 ShadowverseMod 存档里有两个状态都叫 DreizehnPower9。
            string cpCls = CustomPowerGen.ClassNameOf(p, cp, ci);
            if (!powerClassNames.Add(cpCls))
                issues.Add(new("错误", $"{who} 的英文类名「{cpCls}」和前面另一个自定义状态**重复** —— "
                    + "生成出来的代码是「一个类名一个文件」（cs/Powers/" + cpCls + ".cs），"
                    + "同名时**后一个会把前一个覆盖掉**，前者在游戏里静默消失（触发时机 / 效果全丢）。"
                    + "请到「自定义状态」页把它们改成两个不同的英文类名。"));
            if (cp.Type is not ("Buff" or "Debuff"))
                issues.Add(new("错误", $"{who} 的类型只能是 Buff（增益）或 Debuff（减益）。"));
            if (!string.IsNullOrWhiteSpace(cp.AmountColor) && !CardColorSpec.IsHex(cp.AmountColor))
                issues.Add(new("错误", $"{who} 的层数颜色不是合法的 RRGGBB：{cp.AmountColor}"));
            // 「要和其他时机同时成立」（V0.2.117）：勾了它的时机少于 2 条时那条设置不生效（只有一条凑不齐）
            if (cp.AndTriggers.Count == 1)
                issues.Add(new("提示", $"{who} 只有 1 条触发时机勾了「要和其他时机同时成立」——"
                    + "没有别的时机和它凑，所以这条设置**不生效**（照旧每次都触发）。"
                    + "想用「同时成立」请在同一个状态上把**参与的那几条时机都勾上**。"));
            else if (cp.AndTriggers.Count >= 2 && !cp.ShowAndScope)
                issues.Add(new("提示", $"{who} 的「同时成立」设置异常（勾了 {cp.AndTriggers.Count} 条）——请重新勾一下。"));
            // 「覆盖血条」：颜色写坏会生成出一个空的 Color(...) → 游戏里一刷新血条就抛异常，
            // 所以和层数颜色一样必须**报错拦住**（不是静默不上色）。
            if (cp.HasHpBarOverlay && !CardColorSpec.IsHex(cp.HpBarColor))
                issues.Add(new("错误", $"{who} 的「覆盖血条颜色」不是合法的 RRGGBB：{cp.HpBarColor}"
                    + "（六位十六进制，例如 FF8800；留空 = 不覆盖血条）。"));
            // 注：「覆盖血条从哪端开始」只有两档，而且 HpBarSide 的 setter 已经把别的值**归一成「从右边」**了
            //（老存档 / 手写 JSON 里那种值同样在这一步被收掉），所以这里不需要再报一条永远不会出现的警告。
            if (!string.IsNullOrWhiteSpace(cp.Icon) && !File.Exists(cp.Icon))
                issues.Add(new("错误", $"{who} 的图标文件不存在：{cp.Icon}"));
            if (cp.RemoveAtTurnEnd && cp.DecayPerTurn > 0)
                issues.Add(new("警告", $"{who} 同时勾了「回合结束移除」和「每回合衰减」，衰减不会生效（直接移除）。"));
            if (cp.Triggers.Count == 0)
                issues.Add(new("警告", $"{who} 没配任何触发时机：这个状态挂上去也不会做事。"));

            // V0.2.248（用户要求「减少指定卡牌类别的费用（类似腐化，刺破帷幕这些）」）：
            // 费用修正那一栏的几个「配了也不生效」的情况
            string costScope = (cp.CostReduceScope ?? "").Trim();
            if (costScope.Length > 0)
            {
                if (EffectCatalog.IsCardGroupFilter(costScope)
                    && EffectCatalog.CardGroupCards(p, costScope).Count == 0)
                    issues.Add(new("警告", $"{who} 的「费用修正」选了一个**空组** —— 组里一张牌都没有，"
                        + "生成的判据永远不成立（这类牌的费用根本不会减）。到「组」页给它勾几张牌。"));
                if (cp.CostReduceAmount < 0m)
                    issues.Add(new("警告", $"{who} 的「费用修正」填了负数（{cp.CostReduceAmount:0.##}）—— "
                        + "按「变成 0 费」处理（减费只会往 0 走，不会加费）。"));
                else if (cp.CostReduceAmount == 0m && cp.CostReducePerStack)
                    issues.Add(new("提示", $"{who} 的「费用修正」填了 0（= 变成 0 费），"
                        + "这时「按层数」没有意义（0 乘几都是 0），已按「不变」处理。"));
            }

            // ===== V0.2.147：这个状态里用了「玩家侧」的东西 —— 只对**挂在玩家自己身上**有意义 =====
            //
            // 背景（用户问「状态挂在敌人身上时，状态的『自己』是不是敌人自己」）：
            //   · 状态效果里的「自己」= **挂着这个状态的那个单位**（生成代码里就是 base.Owner）；
            //   · 贴到敌人身上时「自己」就是那个敌人；而本体钩子传进来的 player 是**玩家** →
            //     玩家侧触发时机那句 `player != base.Owner.Player` 护栏永远成立 → 那些时机永远不触发；
            //   · 更要命的是「要拿玩家对象」的效果：本体 `Creature.Player` 对**怪物是 null**，
            //     生成代码却直接把 base.Owner.Player 当参数传（抽牌 / 能量 / 生成卡牌 / 药水 / 改牌组…）
            //     → null 传进本体接口 = 运行时异常 → **战斗卡住**（和「没有选牌提示语」同一类）。
            //
            // ⚠ <see cref="PlayerSidePowerEffectKinds"/> 这份清单是**机械扫出来的**，不是拍脑袋列的：
            //   把每一种效果都按「最想弹选牌界面」的参数生成一个自定义状态，看生成源码里有没有
            //   「没有空值保护」的 base.Owner.Player（`!= base.Owner.Player` 比较、`?.` / `??` 都不算）。
            //   自检里有断言钉着这份清单与生成端一致（改效果种类时重跑那条扫描即可）。
            var playerSideEffects = cp.Triggers.Where(t => t is not null)
                .SelectMany(t => t.Effects ?? new System.Collections.ObjectModel.ObservableCollection<EffectSpec>())
                .Where(e => e is not null && PlayerSidePowerEffectKinds.Contains(e.Kind, StringComparer.Ordinal))
                .Select(e => EffectCatalog.FindKind(e.Kind).Display)
                .Distinct().ToList();
            var chanceEffects = cp.Triggers.Where(t => t is not null)
                .SelectMany(t => t.Effects ?? new System.Collections.ObjectModel.ObservableCollection<EffectSpec>())
                .Where(e => e is not null && (e.ChanceEnabled || !string.IsNullOrWhiteSpace(e.ChanceFormula)))
                .Select(e => EffectCatalog.FindKind(e.Kind).Display)
                .Distinct().ToList();
            var playerSideTriggers = cp.Triggers.Where(t => t is not null).Select(t => t.Kind)
                .Where(k => SelfScopedPowerTriggers.Contains(k, StringComparer.Ordinal))
                .Select(SelfScopedPowerTriggerZh).Distinct().ToList();
            if (playerSideTriggers.Count > 0 || playerSideEffects.Count > 0 || chanceEffects.Count > 0)
                issues.Add(new("警告", $"{who} 里用了「玩家侧」的东西 —— 它们只对**挂在玩家自己身上**的状态有意义："
                    + (playerSideTriggers.Count > 0
                        ? $"触发时机（{string.Join(" / ", playerSideTriggers)}）盯的是「挂着它的那个单位」，"
                          + "挂到敌人身上时这些时机**永远不会触发**；" : "")
                    + (playerSideEffects.Count > 0
                        ? $"效果（{string.Join(" / ", playerSideEffects)}）要拿**玩家对象**"
                          + "（抽牌 / 能量 / 金币 / 生成卡牌 / 药水 / 改牌组…），"
                          + "而敌人身上没有玩家（本体 Creature.Player 对怪物就是 null）→ "
                          + "真挂到敌人身上会在运行时抛异常、**战斗卡住**；" : "")
                    + (chanceEffects.Count > 0
                        ? $"勾了「概率生效」的那些效果（{string.Join(" / ", chanceEffects)}）也一样："
                          + "掷骰子要读 base.Owner.Player，敌人身上同样是 null → 同样会抛异常；" : "")
                    + "要做「贴在敌人身上的减益 / 标记」，请只用不依赖玩家的效果（造成伤害 / 失去生命 / 获得格挡 / "
                    + "施加或清除状态 / 击晕 / 翻倍层数 / 暴露 / 毒性爆发 / 禁止格挡或回复…）；"
                    + "「为自己附魔」这类状态请把施加它的效果「作用对象」改成「自己」。"
                    + "⚠ 另外：状态里的「敌人」指的是**玩家视角的敌方那一侧**，所以贴在敌人身上时"
                    + "「单体 / 全体敌人」打的是**它自己和它的同伴**（不是「持有者的敌人」）。"));
            // V0.2.194：自定义状态那块布**生成端已经接了**（锻造页 →「状态列表」）——
            // 以前这里写的是「自定义状态的节点图生成端还没接」，现在是**图非空 = 图说了算**：
            // 老的 Triggers 不再参与生成（要说出来，不然用户在「自定义状态」页改了没反应会一头雾水）。
            if (ForgeGraphGen.UsesGraph(cp))
            {
                if (ForgePowerGraph.HasLegacyContent(cp))
                    issues.Add(new("警告", $"{who} 现在由锻造页那块布说了算（布上有图就盖过「自定义状态」页那套）—— "
                        + "页面上配的触发时机 / 效果**不再参与生成**。要接着改请去「锻造」页 →「状态列表」→ 那块布；"
                        + "想退回老路子就把那块布清空（一个模块都不留）。"));
                ValidateForgeGraph(issues, $"{who}（锻造节点图）", cp.ForgeGraph, p, ForgeTimings.PowerScope);
                ValidatePowerGraphIssues(issues, who, cp);
            }

            for (int k = 0; k < cp.Triggers.Count; k++)
            {
                var t = cp.Triggers[k];
                var opt = PowerTriggers.Find(t.Kind);
                if (opt is null)
                {
                    issues.Add(new("错误", $"{who} 的第 {k + 1} 个触发时机不认识：{t.Kind}。"));
                    continue;
                }
                string when = opt.Display;
                if (t.Effects.Count == 0)
                    issues.Add(new("警告", $"{who}「{when}」下面没有效果，这一条等于没写。"));
                // 「某个状态层数变化后」：选的状态要能找到（留空 = 任意状态，是合法的）
                if (t.Kind == "PowerChanged" && !string.IsNullOrWhiteSpace(t.PowerId)
                    && EffectCatalog.Powers.Count > 0 && !EffectCatalog.IsCustomPower(t.PowerId)
                    && EffectCatalog.FindPower(t.PowerId) is null)
                    issues.Add(new("警告", $"{who}「{when}」盯的状态找不到：{t.PowerId}"
                        + "（可以选本体的状态类名，或「自定义状态」页里自己造的那个；留空 = 除自己以外任意状态变层数都触发）。"));
                // ===== V0.2.166：用户实测报的「击晕值达到 100 却没击晕敌人」的真因 =====
                // 「某个状态层数到达 N 层时」这类**盯某个层数**的时机：如果「盯」选的是「任意敌人」、
                // 而盯的那个状态**就是这条时机所在的状态自己**，老代码生成出来的守卫写着
                // `power.Owner != base.Owner && …HittableEnemies.Contains(power.Owner)`
                // —— 它要求「被盯的那份状态在**别的**生物身上」，于是「这个状态自己涨到 N 那一下」
                // **永远不会触发**（用户就是这么配的：一个贴在敌人身上的减益「击晕值」，盯「任意敌人」的它自己，N=100）。
                //
                // V0.2.168：**改成自动按「这份状态自己」生成**（见 CustomPowerGen.PowerWatchGuard）——
                // 「盯的就是它自己这个状态」时，「盯自己 / 任意敌人」两种档都按「这份状态挂在谁身上就在谁那里触发」算。
                // 所以这里不再是「不会触发」的警告，只留一条说明，免得用户以为「任意敌人」＝「别人身上的才算」。
                if (t.Kind is "PowerReached" or "PowerChanged" or "PowerRemoved"
                    && string.Equals(t.PowerTarget, "AnyEnemy", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(t.PowerId)
                    && string.Equals(t.PowerId.Trim(), CustomPowerGen.ClassNameOf(p, cp, ci), StringComparison.Ordinal))
                {
                    issues.Add(new("提示", $"{who}「{when}」的「盯」选的是「任意敌人」，而盯的那个状态就是**这个状态自己**"
                        + $"（{t.PowerId.Trim()}）—— 按「**这份状态自己**」生成（这份状态挂在谁身上，"
                        + "它自己那件事就在谁那里触发；包括贴在敌人身上的那份自己）。"
                        + "要「只有别的生物身上那份同名状态出事才触发」，请把「盯哪个状态」换成一个**别的**状态。"));
                }
                // V0.2.166：「击晕」在**挂在敌人身上**的状态里：作用对象选「指定敌人」= 玩家视角敌方那一侧里
                // 第一个可打的敌人（本体 CombatState.HittableEnemies），**不一定是身上带着这个状态的那只**。
                // 用户要的「击晕值满了就晕那只怪」应该用「作用对象 = 自己」（状态挂在敌人身上时「自己」就是它）。
                if (cp.Type == "Debuff")
                {
                    var stunByEnemyTarget = t.Effects.Where((EffectSpec x) => x is not null && x.Kind == "Stun"
                        && x.TargetSide is "Enemy" or "AllEnemies" or "RandomEnemies").ToList();
                    if (stunByEnemyTarget.Count > 0)
                        issues.Add(new("提示", $"{who}「{when}」里的「击晕」作用对象是"
                            + $"「{EffectCatalog.TargetSideZh(stunByEnemyTarget[0].TargetSide)}」：这是个**减益**（一般贴在敌人身上），"
                            + "而状态里的「敌人」指的是**玩家视角的敌方那一侧** —— 「指定敌人」取的是那一侧里**第一个可打的敌人**，"
                            + "**不一定是身上带着这个状态的那只**（场上多只敌人时就会晕错）。"
                            + "想「击晕带着这个状态的那只」请把作用对象改成**自己**（状态挂在敌人身上时「自己」就是它）。"));
                }
                // 「只对哪类牌生效」（V0.2.118 增加选择范围）：关键词组 / 自定义组里一张牌都没有时，
                // 生成出来的判据是 `if (false)` —— 这条时机永远不会触发，必须**拦住并说清**（不静默）。
                if (t.Kind == "CardPlayed" && (EffectCatalog.IsKeywordFilter(t.CardFilter) || EffectCatalog.IsCardGroupFilter(t.CardFilter)))
                {
                    int groupCount = EffectCatalog.IsCardGroupFilter(t.CardFilter)
                        ? EffectCatalog.CardGroupCards(p, t.CardFilter).Count
                        : EffectCatalog.KeywordGroupCards(p, t.CardFilter).Count;
                    if (groupCount == 0)
                        issues.Add(new("错误", $"{who}「{when}」的「只对哪类牌生效」选的是"
                            + $"「{PowerTriggers.CardFilterZh(t.CardFilter)}」，但这一组里**一张牌都没有**"
                            + "（没勾任何牌 / 那个关键词没人用）—— 这条时机永远不会触发。"
                            + "请到「卡牌」页把牌勾进这个组，或者把筛选改回「任意 / 攻击 / 技能 / 能力」。"));
                }

                // 「自己受到伤害后」= 本体 AfterDamageReceived：**每一下**伤害都会触发一次。
                // 用户报过「敌人一次 5 点伤害触发了五次」—— 本体的敌人攻击很多是「1 点 × N 下」的连击，
                // 所以这里直接说清楚，并指路「自己受到攻击后（连击只算一次）」。这两句是**触发时机级**的说明，
                // 放在效果循环外面（不然一个触发时机配了三条效果就会重复三遍）。
                if (t.Kind == "DamageTaken")
                    issues.Add(new("提示", $"{who}「{when}」是**每一下伤害都会触发一次**（本体钩子 AfterDamageReceived）："
                        + "敌人的多次连击会触发多次（本体很多攻击是「1 点 × 5 下」，一次「5 点伤害」就是 5 次）。"
                        + "想让「一次攻击只触发一次」，请把触发时机改成「自己受到攻击后（连击只算一次）」"
                        + "（本体钩子 AfterAttack，在所有命中都结束之后才跑一次）。"
                        + "全被格挡 / 0 伤害的那一下不算（生成时会判 UnblockedDamage > 0，和本体原体黏土一致）。"));
                if (t.Kind == "Attacked")
                    issues.Add(new("提示", $"{who}「{when}」在**一次攻击的所有命中都结束之后只触发一次**"
                        + "（本体钩子 AfterAttack）—— 敌人的「1 点 × 5 下」连击也只算一次。"
                        + "判据是这一次攻击真的打到了你身上（全被格挡 / 打的是别人时不触发）；"
                        + "注意它只认「攻击」：中毒、事件掉血这类不是攻击的伤害不会触发它。"));
                // 「自己失去生命时」= 本体 AfterCurrentHpChanged（delta < 0）。和「受到伤害后」的关系说清楚：
                // 那条走伤害管线（AfterDamageReceived），这条认的是生命值真的少了这一件事 —— 是它的超集。
                if (t.Kind == "HpLost")
                    issues.Add(new("提示", $"{who}「{when}」用的是本体钩子 AfterCurrentHpChanged（delta < 0）："
                        + "只要**生命值真的少了**就触发 —— 伤害、自己「失去生命」、直接扣血（致死那套）都算，回血（delta > 0）不算。"
                        + "它比「自己受到伤害后（每一下）」更宽：那条只认伤害，这条连非伤害的掉血也会触发；"
                        + "同样是**每一下都会触发一次**（敌人「1 点 × 5 下」的连击就是 5 次）。"));

                for (int j = 0; j < t.Effects.Count; j++)
                {
                    var e = t.Effects[j];
                    // 「获得卡牌奖励」在「战斗胜利后」里走的是本体的战斗奖励（room.AddExtraReward），不需要 choiceContext
                    bool roomReward = e.Kind == "CardReward" && t.Kind == "CombatVictory";
                    // ⚠ 判据用 **EffectSpec 重载**（不是 e.Kind）：有些效果分档才算需要
                    //（「生成卡牌」只有「多选1 / 自己挑一张」会弹选牌界面 → 才写 choiceContext），
                    // 用 string 版会漏报这条「生成时会自己造一个，能用」的提示。
                    if (!CustomPowerGen.TriggerHasChoiceContext(t.Kind) && CustomPowerGen.NeedsChoiceContext(e) && !roomReward)
                        issues.Add(new("提示", $"{who}「{when}」的第 {j + 1} 条是「{EffectCatalog.FindKind(e.Kind).Display}」，"
                            + "而这个触发时机的本体钩子本身不给 choiceContext（例：敌人回合开始时 AfterSideTurnStart）——"
                            + "生成时会照本体的做法自己造一个再跑，能用；如果游戏里没反应，看日志里这一条。"));
                    // 「获得卡牌奖励」在「战斗胜利后」：说明它到底是怎么发的（挂进本场战斗的结算奖励里）
                    if (roomReward)
                        issues.Add(new("提示", $"{who}「{when}」里的「获得卡牌奖励」会挂进本场战斗的结算奖励里"
                            + "（本体「王国资产」RoyaltiesPower.AfterCombatEnd 的做法）：打赢这场之后，奖励界面会多一条「选一张卡」，"
                            + "N 选一，选中的直接进牌组；打输了不会给。"));
                    if (e.Kind == "CardReward" && t.Kind != "CombatVictory")
                        issues.Add(new("提示", $"{who}「{when}」里放了「获得卡牌奖励」：这个触发时机是在战斗中途，"
                            + "生成时会在那一刻直接弹「N 选一」的选牌界面、选中就进牌组（想要「打赢后奖励界面多一条」就挂「战斗胜利后」）。"));
                    if (!PowerTriggers.Supports(e.Kind))
                    {
                        // ⚠ 文案按**真实原因**分档：以前一律说「需要卡牌上下文（选牌 / 结束回合）」，
                        // 但那个清单里混了两种原因 —— ClearPowerOne / ClearPowerDebuffs / PetSacrifice
                        // 只是「状态那条链还没实现」，根本没用到那个上下文，用户被这句话误导过。
                        bool needsCardPlay = PowerTriggers.NeedsCardPlayContext.Contains(e.Kind, StringComparer.Ordinal);
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条效果是「{EffectCatalog.FindKind(e.Kind).Display}」，"
                            + (needsCardPlay
                                ? "它要的是「打出这张牌」那一刻的上下文（这张牌本身 / 此刻弹选牌界面），状态触发器里没有这个上下文。"
                                : "生成端还没给「自定义状态」那条链实现这个效果种类（会什么都不做），所以先拦下来。")
                            + $"状态触发器里能用的是：{string.Join(" / ", PowerTriggers.SupportedEffectKinds)}。"));
                    }
                    // 作用对象 = 「全体单位」（V0.2.132 第 7 档）：状态链也在生成端实现了
                    //（CustomPowerGen.EmitEffect / EmitDamage），但只对那几个种类 —— 其余一律报错拦住。
                    ValidateAllUnitsTarget(issues, $"{who}「{when}」", j + 1, EffectCatalog.FindKind(e.Kind), e, "Power");
                    // 复制 / 重放 / 回合结束时自动打出，以及「斩杀」条件：都要「打出某张牌」的上下文，
                    // 状态触发器里没有 —— 生成时会留一行「已忽略」，这里也说清楚（不静默丢）。
                    if (e.Kind is "CopyCard" or "ReplayCard" or "TurnEndPlay")
                        // 为什么这一条**仍然要拦**：这三个都要「打出这张牌、此刻弹选牌界面」的上下文
                        //（CopyCard 要原牌在战斗牌堆里 + 选牌界面、ReplayCard 要现在就把重放次数写上去、
                        // TurnEndPlay 要把牌挂到本回合结束那一刻的钩子上）—— 状态触发器里只有「模型自己的钩子」，
                        // 没有「打出的那张牌」，CustomPowerGen.EmitEffect 对这三个只写一行「已忽略」。
                        // 所以它拦的是「生成端真的什么都不做」的组合，和需求 ⑥ 放开的
                        //「按范围随机 / 生成出来的卡怎么处理 / 范围界定」是两回事。
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」"
                            + "在**自定义状态**里用不了（它要「打出这张牌时选牌」的上下文）—— 生成时会被忽略，"
                            + "请把它放到卡牌 / 遗物 / 药水上。"));
                    // 「数值公式」：V0.2.164 起自定义状态链也支持（那 5 种走**全内联**求值，见
                    // CSharpCodeGen.InlineAmountFormula）—— 这里改成统一走那一份校验：
                    // 支持的 5 种放行（并提示「指定敌人」在这条链上取的是可打的第一个敌人），
                    // 不支持的种类照旧报错拦住（不能静默用固定数值，用户看不出问题）。
                    if (!string.IsNullOrWhiteSpace(e.AmountFormula))
                        ValidateAmountFormula(issues, $"{who}「{when}」的第 {j + 1} 条", e,
                            EffectCatalog.FindKind(e.Kind), "Power", p);
                    // 两条「这一次打出」的卡牌专属读数：读的是**卡片自己的属性** ——
                    // 自定义状态这条链上没有这个东西（生成出来会 CS0103 / 编译不过）。
                    foreach (var cr in CSharpCodeGen.CardReadingsAll)
                        if (CSharpCodeGen.MentionsReading(e, cr.Id))
                            issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条用了 {CSharpCodeGen.ReadingZh(cr.Id)} ——"
                                + "这条读数**只能用在卡牌上**（它读的是这张牌自己的属性，自定义状态身上没有）。"
                                + (cr.Id == CSharpCodeGen.ThisCardDamageId
                                    ? "想用「本回合 / 本场战斗造成的伤害」请改内置读数 DamageDealtThisTurn / DamageDealtThisCombat。"
                                    : "想用「本回合 / 本场战斗获得的格挡」请改内置读数 BlockGainedThisTurn / BlockGainedThisCombat。")));
                    if (e.Condition is { Kind: "Fatal" })
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条用了「斩杀」条件，但它只能用在**卡牌**上 —— "
                            + "请把这条效果放到攻击牌上（判的是「这一次攻击有没有把目标打死」）。"));
                    // 用户要求「所有触发时机下拉菜单内容相同」之后，状态侧也列出了「获得时」——
                    // 但状态是被施加的、没有「拿到的那一刻」，这里明确拦住（不静默什么都不做）。
                    if (t.Kind == "Obtained")
                        issues.Add(new("错误", $"{who}「{when}」：状态是被**施加**的，没有「拿到的那一刻」——"
                            + "「获得时」只有遗物能用。请换成别的触发时机（例：「玩家回合开始时」），"
                            + "或者把这条效果做在遗物上。"));
                    // 「某个状态层数到达 N 层时」：N 必须 ≥ 1（层数从 0 往上跨才可能触发）
                    if (t.Kind == "PowerReached" && t.TriggerAmount <= 0m)
                        issues.Add(new("错误", $"{who}「{when}」的「到达 N 层」填的是 {t.TriggerAmount:0.##}："
                            + "层数要从 0 往上跨才可能触发，请填 ≥ 1 的数。"));
                    // 「层数刚好达到 N 层」：只有「某个状态层数变化后」那条钩子同时知道
                    // 「变成多少（power.Amount）」和「这次变了多少（amount）」—— 别的钩子上生成出来永远不成立。
                    if (e.Condition is { Kind: "PowerJustReached" })
                    {
                        if (t.Kind != "PowerChanged")
                            issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条用了条件"
                                + "「某状态层数刚好达到 N 层」：它只能挂在触发时机「某个状态层数变化后」上"
                                + "（只有那条钩子知道「这次变化跨过了 N」）；在别的触发时机上生成出来永远不成立。"
                                + "想判「现在有 N 层就生效」请改用条件「拥有某状态至少 N 层」。"));
                        else if (string.IsNullOrWhiteSpace(e.Condition.PowerId))
                            issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条用了条件"
                                + "「某状态层数刚好达到 N 层」，但没选「是哪个状态」—— 请到条件选项里选一个增益 / 减益。"));
                    }
                    // 需求 ⑥：这里原本还有两条**错误**级检查，拦的是「按范围随机 / 生成出来的卡怎么处理」与「范围界定」。
                    // 当时的理由是那条链（CustomPowerGen.EmitEffect）手写 AddToCombatAndPreview、base.Owner 是 Creature，
                    // 拿不到玩家那一侧的对象与角色配置。现在那条链改成复用 CSharpCodeGen.EmitGenerateCard
                    //（传 base.Owner.Player + profile），三样都能做了 —— 两条检查都撤掉，不在这里再报错。
                    // 也**不需要**补「自定义关键词那一组是空的」检查：上面通用效果校验里的
                    //「范围限定 / 范围界定里的自定义关键词那一组」那段是按效果本身判的（spawnScope || cardScope），
                    // 状态触发器同样会走到那一段，空组照样报错。
                    if (e.Kind is "ApplyPower" or "TempPower" && string.IsNullOrWhiteSpace(e.PowerId))
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」还没选状态。"));
                    else if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.Powers.Count > 0
                             && !EffectCatalog.IsCustomPower(e.PowerId) && EffectCatalog.FindPower(e.PowerId) is null)
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条要施加的状态找不到：{e.PowerId}。"));
                    // 「施加状态」填负数 = 扣层数。V0.2.139 起分两档（见 CSharpCodeGen.ApplyPowerLine）：
                    //   · 本体允许负层数的 5 个状态（力量/敏捷/专注/尖啸/萎缩）→ 照本体「尖啸」那套直接施加负值；
                    //   · 其余 → 先取目标身上那份实例再 ModifyAmount 扣（层数不能为负）。
                    // 之所以要给提示：免得用户以为「-3」永远是「扣目标身上已有的 3 层」。
                    if (e.Kind == "ApplyPower" && e.Amount < 0)
                        issues.Add(new("提示", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」数值是 {e.Amount}："
                            + $"会生成「{(e.TargetSide == "Self" ? "失去" : "扣除")} {-e.Amount} 层{EffectCatalog.PowerName(e.PowerId)}」"
                            + NegativeLayerNote(e.PowerId, e.Amount)
                            + "—— 和卡牌 / 遗物 / 药水同一条实现，**不是**「给目标挂一个 " + e.Amount + " 层的新状态」。"));
                    // 每条效果自己的条件选项（能力/状态里的条件不支持「这张牌」类条件）
                    ValidateCondition(issues, $"{who}「{when}」第 {j + 1} 条", e.Condition, "Power");
                    if (e.Kind == "GenerateCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                        issues.Add(new("警告", $"{who}「{when}」的「生成卡牌」没选目标卡，会生成小刀（Shiv）。"));
                    // 「强化指定卡牌」在**状态**那条链上同样会生成 BoostPowers.cs 里的
                    // `if (cardSource is not <目标卡类名>)` —— 目标卡认不出来一样是 CS0103，所以这里也要查。
                    if (e.Kind == "BoostCard")
                        ValidateBoostCardTarget(issues,
                            $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」", p, e);
                    if (e.AmountIsStack && e.Amount == 0 && e.Kind is "Damage" or "Block")
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条数值 = 层数，层数可能为 0，这条效果会打 0。"));
                    // 「数值 = 本状态的层数」：用户报过「写了 30 层、游戏里只给 1 层」——
                    // 勾上这个之后「数值」里填的数字是被忽略的（生成的是 base.Amount），不说清就会踩
                    if (e.AmountIsStack && e.Amount != 0)
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条勾了「数值 = 本状态的层数」，"
                            + $"「数值」里填的 {e.Amount:0.##} 不会生效（游戏里用的是这个状态的层数 base.Amount）。"
                            + "想要固定数值就把那个勾去掉。"));
                    // 本体里有些状态显示的数字根本不是层数（自己 override 了 DisplayAmount）：
                    // 填多少层，状态栏那个数字都不会是你填的值（用户报过「30 层缓慢」）

                    if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerAmountNote(e.PowerId) is string amountNote)
                        issues.Add(new("提示", $"{who}「{when}」的第 {j + 1} 条施加的是「{EffectCatalog.PowerName(e.PowerId)}」："
                            + $"本体这个状态显示的数字不是层数 —— {amountNote}"
                            + "（层数照旧记着，只是状态栏那个数字由它自己算，别按「显示 = 你填的层数」去读。）"));
                    if (e.TimesIsStack && e.Times != 1)
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条勾了「生效次数 = 层数」，"
                            + $"「生效次数」里填的 {e.Times} 会被忽略（层数说了算）。"));
                    if (e.RepeatIsStack && e.RepeatCount != 1)
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条勾了「命中/对群数 = 层数」，"
                            + $"「对群数」里填的 {e.RepeatCount} 会被忽略（层数说了算）。"));
                    if ((e.TimesIsStack || e.RepeatIsStack) && cp.DecayPerTurn == 0 && !cp.RemoveAtTurnEnd)
                        issues.Add(new("警告", $"{who}「{when}」按层数重复执行、而且这个状态不会衰减："
                            + "层数越高执行次数越多（可能卡顿），建议配一点「每回合衰减」，或让卡牌只给少量层数。"));
                    // 「每一下伤害都触发」× 「按层数重复执行」= 相乘：用户的「一次 5 点伤害触发了五次」
                    // 有可能是连击，也有可能是这里相乘 —— 直接算给他看。
                    if (t.Kind == "DamageTaken" && (e.TimesIsStack || e.RepeatIsStack))
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条同时勾了「按层数重复执行」："
                            + "「每一下伤害都触发」会和层数**相乘** —— 敌人的一次 5 连击 × 这个状态 5 层 = 一次攻击执行 25 次。"
                            + "想「一次攻击只算一次」请把触发时机改成「自己受到攻击后（连击只算一次）」并取消「按层数重复」。"));
                    // 「获得卡牌奖励」按层数重复：层数是几，结算界面就多几条奖励（每条都是「N 选一」）
                    if (roomReward && e.TimesIsStack)
                        issues.Add(new("提示", $"{who}「{when}」的「获得卡牌奖励」勾了「生效次数 = 本状态的层数」："
                            + "这个状态有几层，奖励界面就会多出几条「选一张卡」（每条都是 N 选一）。"
                            + "只想要一条奖励就别勾那个，或者把卡牌给的层数控制在 1。"));
                    // 「卡牌奖励」的 N 是「给几张让你选一张」，别和「生效次数」搞混
                    if (roomReward && e.Times > 1)
                        issues.Add(new("提示", $"{who}「{when}」的「获得卡牌奖励」生效次数是 {e.Times}："
                            + "会加 {e.Times} 条「选一张卡」的奖励（每条给 {EffectCatalog.FindKind(e.Kind).Display} 里填的数量选一）。"));
                }
            }
        }

        // 先古之民的遗物选项替换
        foreach (var talk in p.Ancients)
        {
            if (string.IsNullOrWhiteSpace(talk.AncientId)) continue;
            var ancient = EffectCatalog.FindAncient(talk.AncientId);
            string who = ancient?.Epithet ?? talk.AncientId;
            var nm = Naming.From(p);
            var usedFrom = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < talk.RelicReplacements.Count; k++)
            {
                var r = talk.RelicReplacements[k];
                string rw = $"先古之民「{who}」的第 {k + 1} 条遗物替换";
                string from = r.FromRelicId ?? "";
                if (from.Length > 0 && !usedFrom.Add(from))
                    issues.Add(new("警告", $"{rw}：「{from}」配了不止一次，生成时只留第一条。"));
                if (from.Length == 0 && r.Slot < 1)
                    issues.Add(new("警告", $"{rw}：还没选要替换掉哪个「原本的遗物」—— 不确定要换掉哪一个，这条不会生效。"));
                if (from.Length > 0 && ancient is not null && ancient.RelicCandidateIds.Count > 0
                    && !ancient.RelicCandidateIds.Contains(from, StringComparer.Ordinal))
                    issues.Add(new("警告", $"{rw}：「{from}」不是这位先古之民原本会给的遗物 —— "
                        + $"他每次只随机给 3 个选项，没抽到「{from}」的那一次不会替换（不会顶掉别的选项）。"));
                if (string.IsNullOrWhiteSpace(r.RelicId))
                {
                    issues.Add(new("警告", $"{rw}还没选要换成什么遗物，生成时会跳过。"));
                    continue;
                }
                if (from.Length > 0 && string.Equals(from, r.RelicId, StringComparison.Ordinal))
                    issues.Add(new("提示", $"{rw}：原本的遗物和要换成的遗物是同一个（等于没改）。"));

                bool isMine = p.Relics
                    .Select((rel, i) => nm.RelicClassName(rel, i))
                    .Any(cls => string.Equals(cls, r.RelicId, StringComparison.Ordinal));
                bool isVanilla = AncientCatalog.VanillaRelics.Any(v => string.Equals(v.Id, r.RelicId, StringComparison.Ordinal));
                if (!isMine && !isVanilla && AncientCatalog.VanillaRelics.Count > 0)
                    issues.Add(new("错误", $"{rw}里要换成的遗物类名找不到：{r.RelicId}"
                        + "（要么是你「遗物」页里自己做的，要么是本体的遗物类名）。"));
                else if (isVanilla && ancient is not null && ancient.RelicCandidateIds.Count > 0
                         && !ancient.RelicCandidateIds.Contains(r.RelicId, StringComparer.Ordinal))
                    issues.Add(new("提示", $"{rw}：{r.RelicId} 不在本体这位先古之民原本的候选表里 —— "
                        + "补丁仍然会把它塞进选项，只是风格上可能和这位先古之民不太搭。"));
            }
        }

        // 卡池太小 → 直接拦住（池子得自己够抽，不够就报错，不会替你凑）
        if (CSharpCodeGen.RewardPoolTooSmall(p))
        {
            issues.Add(new("错误", $"卡池里奖励能抽到的卡只有 {CSharpCodeGen.RewardPoolStatus(p)}，"
                + $"至少要 {CSharpCodeGen.MinRewardPool} 张（Common / Uncommon / Rare）。"
                + "本体发奖励时会「互不重复地抽 3 张」，抽不出来会直接抛异常 → 奖励界面不弹 → 死档。"
                + "解决办法：把「加入卡池」的卡加到至少 3 张（Basic 不参与奖励；上限不限，越多奖励越丰富）。"));
        }

        // 每种牌型都要有一张能上商店的卡，否则进商店直接黑屏（本体 CardFactory.CreateForMerchant 会抛异常）
        var missingTypes = CSharpCodeGen.MerchantMissingTypes(p);
        if (missingTypes.Count > 0)
        {
            issues.Add(new("错误", $"卡池里的「{string.Join(" / ", missingTypes)}」牌一张都上不了商店："
                + "本体会给「攻击 / 技能 / 能力」这三种牌型各摆一个货架，摆不出来会直接抛异常 → **进商店黑屏**"
                + "（战斗奖励不受影响，所以很容易漏掉）。"
                + "原因：那个牌型的卡稀有度全是「Basic（基础）」（其它 Ancient / Curse 也不进商店）。"
                + "解决办法：把该牌型的卡稀有度至少改成 Common（普通）/ Uncommon（罕见）/ Rare（稀有）各一张，再重新生成。"
                + "（状态 / 诅咒 / 任务这几种牌型本体根本不摆货架，不在这条检查里。）"));
        }

        // 本体状态改写：键要对得上、颜色要合法、图标文件要存在
        for (int i = 0; i < p.VanillaPowerOverrides.Count; i++)
        {
            var o = p.VanillaPowerOverrides[i];
            string who = $"本体状态改写 #{i + 1}";
            if (!o.Enabled) continue;
            if (string.IsNullOrWhiteSpace(o.PowerId))
            {
                issues.Add(new("错误", $"{who} 没有选要改的本体状态（在「本体状态改写」页里选一个，比如中毒）。"));
                continue;
            }
            who = $"本体状态改写「{(string.IsNullOrWhiteSpace(o.VanillaName) ? o.PowerId : o.VanillaName)}」";

            if (EffectCatalog.Powers.Count > 0 && EffectCatalog.FindPower(o.PowerId) is null)
                issues.Add(new("错误", $"{who} 在本体状态列表里找不到（类名要对上本体，比如 PoisonPower）。"));
            else
            {
                string slug = VanillaPowerGen.SlugOf(o);
                string? titleKey = EffectCatalog.ZhLocText(slug + ".title");
                bool renaming = !string.IsNullOrWhiteSpace(o.Name) && o.Name.Trim() != (o.VanillaName ?? "").Trim();
                if (renaming && EffectCatalog.ZhPowerLoc.Count > 0 && titleKey is null)
                    issues.Add(new("警告", $"{who} 要改名字，但本体的 powers 本地化表里没有 {slug}.title 这个键，"
                        + "改名可能不生效（键名要跟本体的 Id.Entry 对上）。"));
            }

            if (!string.IsNullOrWhiteSpace(o.BarColor))
            {
                if (!CardColorSpec.IsHex(o.BarColor))
                    issues.Add(new("错误", $"{who} 的血条颜色不是合法的 RRGGBB：{o.BarColor}（例：66BF3C）。"));
                else if (EffectCatalog.HealthBarNodeFor(o.PowerId) is null)
                    issues.Add(new("警告", $"{who} 填了血条颜色，但本体血条上只有「中毒」和「灾厄」两截是本体自己按状态显示的，"
                        + "这个本体状态**没有可上色的血条段**，颜色不会有显示效果。"
                        + "想让任意状态都在血条上盖一截颜色，用「自定义状态」页那条「覆盖血条颜色」"
                        + "（它是照本体中毒那截复制一份节点、想要什么颜色都行）。"));
            }

            if (!string.IsNullOrWhiteSpace(o.AmountColor) && !CardColorSpec.IsHex(o.AmountColor))
                issues.Add(new("错误", $"{who} 的层数颜色不是合法的 RRGGBB：{o.AmountColor}"));

            if (!string.IsNullOrWhiteSpace(o.Icon) && !File.Exists(o.Icon))
                issues.Add(new("错误", $"{who} 的图标文件不存在：{o.Icon}"));

            if (!o.ChangesAnything)
                issues.Add(new("警告", $"{who} 什么都没填（名字/描述/图标/颜色都空），生成时会跳过这一条。"));
        }

        // X 费用：本体 ResolveEnergyXValue() 在「不是 X 费用」的牌上会直接抛异常，
        // 所以「效果用了 X 但费用不是 X」必须在这里挡住（生成代码里也做了兜底：X 按 0）。
        for (int i = 0; i < p.AllCards.Count(); i++)
        {
            var c = p.AllCards.ElementAt(i);
            // 本体卡引用（打击 / 防御）：数值/效果/费用都由本体决定，不参与这些校验
            if (c.IsVanillaCard) continue;
            // 诅咒：费用和类型都是生成时写死的（-1 / Curse），X 费用那几条不适用
            if (c.IsCurseCard) continue;
            bool usesX = c.Effects.Any(e => e.UsesX);
            // ===== V0.2.131：费用 / 额外资源量费用 / 升级后那两栏 =====
            // 判据都用「老字段 + 新式子」合并后的有效值（CSharpCodeGen.IsCostX 等），界面上的勾选框已经
            // 撤掉、改成在这四个框里写 X / Y / X+N / 数字 / 生成期常量式子。
            bool costIsX = CSharpCodeGen.IsCostX(c);
            bool starIsX = CSharpCodeGen.IsStarCostX(c);
            int costNow = CSharpCodeGen.CardCostOf(p, c);
            int starNow = CSharpCodeGen.StarCostOf(p, c);
            // 费用框：解析得出来吗（运行时表达式必须在这里**说清原因**拦住 —— 本体 base(cost, …) 收不了）
            var costR = ForgeNumbers.ResolveCost(p, c.CostFormula, "X", allowPlus: true, "费用");
            if (!costR.Ok)
                issues.Add(new("错误", $"卡牌「{c.Name}」的{costR.Error}"));
            else if (costR.IsConstant && costR.Value < 0)
                issues.Add(new("警告", $"卡牌「{c.Name}」的费用填成 {costR.Value}（负数 = 特殊牌）："
                    + "本体的改费逻辑会把结果夹到 0，可能让这张牌变成能打出。"));
            // 额外资源量费用框
            var starR = ForgeNumbers.ResolveCost(p, c.StarCostFormula, "Y", allowPlus: false, "额外资源量费用");
            if (!starR.Ok)
                issues.Add(new("错误", $"卡牌「{c.Name}」的{starR.Error}"));
            else if (starR.IsConstant && starR.Value < 0)
                issues.Add(new("错误", $"卡牌「{c.Name}」的「额外资源量费用」不能是负数：{starR.Value}。"));
            // 框里写了费用、效果里又留着负数「花费额外资源量」：框优先决定实际费用，卡面那句还是按效果写
            //（数字可能对不上）—— 说清，别让人以为哪个生效了。
            if (!string.IsNullOrWhiteSpace(c.StarCostFormula)
                && c.Effects.Any(e => e is not null && e.Kind == "ExtraResource" && e.Amount < 0))
                issues.Add(new("提示", $"卡牌「{c.Name}」在「额外资源量费用」框里写了 {ForgeFormula.DisplayText(c.StarCostFormula)}，"
                    + "效果列表里又留着一条负数「花费额外资源量」：**以框里那个为准**（生成的是 CanonicalStarCost 那条路），"
                    + "卡面那句话仍然是按效果里那条写的 —— 数字可能对不上，建议只留一处（把效果里那条改成别的种类或删掉）。"));
            // 升级后费用框（常量式子 / X+N 两档；运行时的量同样收不了）
            if (!string.IsNullOrWhiteSpace(c.UpgradeCostFormula))
            {
                var upR = ForgeNumbers.ResolveCost(p, c.UpgradeCostFormula, "X", allowPlus: true, "升级后费用");
                if (!upR.Ok)
                    issues.Add(new("错误", $"卡牌「{c.Name}」的{upR.Error}"
                        // V0.2.148：用户会把 Y 填到「升级后费用」这一栏（那是**能量**费用栏）——
                        // 报错里顺手指一下右边的「升级后额外资源量费用」栏。
                        + (c.UpgradeCostFormula.Contains('Y')
                            ? "⚠ 这一栏是**能量费用**：如果你想改的是「升级后额外资源量费用」，请填在它右边那一栏（那边填 Y）。"
                            : "")));
                else if (upR.IsConstant && upR.Value < 0)
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后费用」不能是负数：{upR.Value}。"));
                else if (upR.IsX && !costIsX)
                {
                    // V0.2.149（用户实测报的「原本 1 费、升级后 X 费，实际升级还是 1 费」）：
                    // 本体**不支持**「升级时把数字费用变成 X 费」——
                    //   · X 是 `CardEnergyCost.CostsX`，在**第一次读 `EnergyCost` 时**就由 `HasEnergyCostX` 捕获定死
                    //     （`CardModel.EnergyCost` 里 `_energyCost ??= new CardEnergyCost(this, CanonicalEnergyCost, HasEnergyCostX)`），
                    //     之后只认缓存的那个布尔值（打出 / 扣费 / ResolveEnergyXValue 全看它）；
                    //   · 升级能改的只有**数字**：`EnergyCost.UpgradeBy(差值)`。
                    // 所以这一档做不了，而且必须说清楚「不是工具没生成，是本体做不到」，并给出能做到的两种写法。
                    string what = upR.Plus > 0 ? $"X+{upR.Plus}" : "X";
                    issues.Add(new("警告", $"卡牌「{c.Name}」的「升级后费用」写的是 {what}，但这张牌现在的费用不是 X"
                        + "（费用框里也没写 X）—— **本体做不到「升级后把数字费用变成 X 费」**："
                        + "X 是卡牌构造时定死的一档（`CardEnergyCost.CostsX` 在第一次读 `EnergyCost` 时就捕获了，"
                        + "之后打出 / 扣费 / 取值全看它），升级能改的只有数字（`EnergyCost.UpgradeBy`）。"
                        + "所以这一段不会生效，升级后还是原来的费用。能做到的两种写法："
                        + "① 想要「X 费」就把「费用」框一开始就填 X，再在「升级后费用」里写 X+1（= 升级后 X 多 1）；"
                        + "② 想「升级后更便宜」就在这一栏填数字（例：1 费 → 升级后 0 费）。"));
                }
            }
            // V0.2.158（用户实测报的「次数填 Y（额外资源量）时只会触发一次」）：
            // 本体是**先扣费、再跑效果**（PlayCardAction：`await _card.SpendResources()` → `OnPlayWrapper(...)`），
            // 所以公式里的 X / Y 在效果跑的那一刻读到的是**扣完剩下的**。
            //   · 「X 费」的牌 → X 读到 0；「Y 费」的牌 → Y 读到 0（次数会被夹到 1，就是用户看到的现象）。
            // 想用「这张牌花掉的那部分」有现成的读数：XSpent / YSpent（本体 ResolveEnergyXValue / ResolveStarXValue）。
            {
                bool costIsXNow = c.CostIsXEffective;
                bool starCostIsXNow = c.StarCostIsXEffective;
                if (costIsXNow || starCostIsXNow)
                {
                    foreach (EffectSpec fx in c.Effects.Where(x => x is not null))
                    {
                        string allFormulas = (fx.AmountFormula ?? "") + " "
                            + string.Join(" ", EffectSpec.CountFormulaKeys.Select(fx.CountFormula));
                        // V0.2.168：**不再是警告**了 —— 生成端现在对「X 费 / Y 费」的牌**直接按花掉的那部分**生成
                        //（__X / __Y 取 ResolveEnergyXValue() / ResolveStarXValue()，见 ForgeGraphGen.EmitCardFormulaPrologue），
                        // 所以「次数填 Y」在这种牌上本来就是对的，用户不用改成 YSpent。留一条提示说清口径，
                        // 免得他以为 Y 还是「当前剩下的」。
                        if (starCostIsXNow && ContainsIdentifier(allFormulas, "Y"))
                            issues.Add(new("提示", $"卡牌「{c.Name}」是「Y 费」（额外资源量费用 = X 那一档），"
                                + "效果里用的 Y **已按「这张牌花掉的额外资源量」生成**（本体先扣费再跑效果，"
                                + "读「当前值」会是 0、次数会被夹到 1）。要「当前还剩多少」请改用内置量里的"
                                + "「这张牌花掉 / 当前值」以外的写法，或者填 YSpent（两者在这种牌上现在等价）。"));
                        // 用户实测（「次数填 Y 或者 X 次都只生效一次」）：这一档界面上叫「额外资源量费用 = X」，
                        // 于是很容易以为公式里也该写 X —— 但内置量里 **X 永远是「能量」**、额外资源量是 **Y**。
                        // 他写 X 时读到的是当前能量（和费用无关），当然也不是「花掉的资源量」。说清这一条。
                        if (starCostIsXNow && ContainsIdentifier(allFormulas, "X"))
                            issues.Add(new("提示", $"卡牌「{c.Name}」的「额外资源量费用」是**「X」那一档**，"
                                + "但效果公式里写的 **X 是「当前能量」**（内置量：X = 能量、Y = 额外资源量）——"
                                + "想「花掉几点额外资源量就几次 / 就多少点」请写 **Y**（这张牌上 Y 已按「花掉的那部分」生成）。"));
                        if (costIsXNow && ContainsIdentifier(allFormulas, "X"))
                            issues.Add(new("提示", $"卡牌「{c.Name}」是「X 费」，效果里用的 X **已按「这张牌花掉的能量」生成**"
                                + "（本体先扣费再跑效果，读「当前能量」会是扣完剩下的）。"));
                    }
                }
                // V0.2.159：XSpent / YSpent 在**不是那种费用**的牌上是 0（生成端做了判空三元）——
                // 本体那两个方法是「不是 X 费就 throw InvalidOperationException」，不判就会在战斗中抛异常。
                // 不会崩了，但读数恒为 0 = 白写，所以这里明说一句（用户要求「不要静默」）。
                foreach (EffectSpec fx in c.Effects.Where(x => x is not null))
                {
                    string allFormulas = (fx.AmountFormula ?? "") + " "
                        + string.Join(" ", EffectSpec.CountFormulaKeys.Select(fx.CountFormula));
                    if (ContainsIdentifier(allFormulas, "XSpent") && !costIsXNow)
                        issues.Add(new("警告", $"卡牌「{c.Name}」的效果里用了 **XSpent**，但这张牌的「费用」不是 X "
                            + "（费用框里没写 X）—— 它不是「X 费」牌，所以 XSpent 读出来**恒为 0**（生成端不会崩，"
                            + "但这条公式等于没写）。想让它有意义：把「费用」填成 X（花光全部能量）。"));
                    if (ContainsIdentifier(allFormulas, "YSpent") && !starCostIsXNow)
                        issues.Add(new("警告", $"卡牌「{c.Name}」的效果里用了 **YSpent**，但这张牌的「额外资源量费用」不是 Y "
                            + "（那一栏没写 Y）—— 它不是「Y 费」牌，所以 YSpent 读出来**恒为 0**（生成端不会崩，"
                            + "但这条公式等于没写）。想让它有意义：把「额外资源量费用」填成 Y（花光全部额外资源量）。"));
                }
            }
            // 升级后额外资源量费用：写进去却解析不出来的原文留在 UpgradeStarCostFormula 里
            // V0.2.148：`Y`（= 升级后还是 Y）是**合法**写法 —— 本体改不了 X 那一档，语义上就是「升级不改」，
            // 生成端不会写任何 UpgradeStarCostBy，所以不当错误报（用户实测报「填不了 Y」）。
            // 「升级后 Y+1」本体做不到：`UpgradeStarCostBy` 碰到 HasStarCostX 的牌**直接抛异常**，
            // 所以那一档要报错，并把真正的原因写出来。
            if (!string.IsNullOrWhiteSpace(c.UpgradeStarCostFormula))
            {
                // 用 allowPlus: true 解一次 —— 只有这样才能分辨「`Y`（合法 = 不改）」和「`Y+1`（本体做不到）」
                var starRaw = ForgeNumbers.ResolveCost(p, c.UpgradeStarCostFormula, "Y", allowPlus: true, "升级后额外资源量费用");
                if (starRaw.Ok && starRaw.IsX && starRaw.Plus == 0)
                {
                    if (starIsX)
                        issues.Add(new("提示", $"卡牌「{c.Name}」的「升级后额外资源量费用」写的是 Y（= 升级后还是 Y）："
                            + "等于「升级不改资源量费用」，不会生成任何 UpgradeStarCostBy —— 想清空这一栏也可以。"));
                    else
                        issues.Add(new("警告", $"卡牌「{c.Name}」的「升级后额外资源量费用」写的是 Y，"
                            + "但这张牌现在的「额外资源量费用」不是 Y —— **本体做不到「升级后把数字资源量费用变成 Y」**"
                            + "（`HasStarCostX` 那一档不能靠升级改，本体只给了 `UpgradeStarCostBy(数字)`）。"
                            + "所以这一段不会生效。想要「Y 费」请把「额外资源量费用」框一开始就填 Y。"));
                }
                else if (starRaw.Ok && starRaw.IsX)
                {
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后额外资源量费用」填不了 Y+{starRaw.Plus}："
                        + "本体 UpgradeStarCostBy 对「额外资源量费用 = X」的牌会**直接抛异常**"
                        + "（UpgradeStarCostBy called on … which has star cost X.），所以「升级后资源量费用多 N 点」这一档做不了。"
                        + "想「升级不改」请留空、或写一个 Y；想「升级后固定花 N 点」请先把「额外资源量费用」从 Y 改成固定数字，再填这一栏。"));
                }
                else
                {
                    var starRawStrict = ForgeNumbers.ResolveCost(p, c.UpgradeStarCostFormula, "Y", allowPlus: false, "升级后额外资源量费用");
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后额外资源量费用」看不懂：{c.UpgradeStarCostFormula}。"
                        + (starRawStrict.Error is { Length: > 0 } ? starRawStrict.Error : "")
                        + "这一栏只支持：① 整数（或生成期就算得出来的式子，例：1+1）；② 一个 Y（= 升级后还是 Y，等于不改）。"
                        + "（本体升级资源量费用走的是 UpgradeStarCostBy(差值)，差值必须是生成时定下来的常量。）"));
                }
            }
            // 升级后费用：X 费牌改不了、负数费用会被夹到 0，提前说清楚
            if (c.UpgradeCost is { } upCost)
            {
                if (costIsX)
                    issues.Add(new("警告", $"卡牌「{c.Name}」是 X 费用牌，「升级后费用」填的 {upCost} 不会生效"
                        + "（本体改费用的逻辑遇到 X 费牌会直接返回）。想「升级后 X + 1」请在「升级后费用」框里写 X+1。"));
                else if (costNow < 0)
                    issues.Add(new("警告", $"卡牌「{c.Name}」现在的费用是 {costNow}（负数 = 特殊牌），"
                        + "本体的改费逻辑会把结果夹到 0，可能让这张牌变成能打出。"));
                else if (upCost < 0)
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后费用」不能是负数：{upCost}。"));
            }
            if (c.UpgradeStarCost is { } upStar)
            {
                if (starIsX)
                    issues.Add(new("警告", $"卡牌「{c.Name}」是「额外资源量费用 = X」的牌，「升级后额外资源量费用」"
                        + $"填的 {upStar} 不会生效（本体改不了 X 那一档的资源量费用）。"));
                else if (upStar < 0)
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后额外资源量费用」不能是负数：{upStar}。"));
                else if (starNow == 0)
                    issues.Add(new("提示", $"卡牌「{c.Name}」现在不花额外资源量，但填了「升级后额外资源量费用 {upStar}」——"
                        + "升级后这张牌会开始要额外资源量（本体 UpgradeStarCostBy 会把它加上去）。"));
            }
            if (usesX && !costIsX && !starIsX)
                issues.Add(new("警告", $"卡牌「{c.Name}」的效果里用了 X（「数值（公式）」/「次数公式」框里写了 X，"
                    + "或者老存档勾了「= X」），但这张牌的费用不是 X（也没勾资源量 X）："
                    + "X 会被当成 0，效果等于不生效。请在「费用」框里填 X（或到「额外资源量费用」框里填 Y）。"));
            if (starIsX && string.IsNullOrWhiteSpace(c.StarCostFormula)
                && !c.Effects.Any(e => e.Kind == "ExtraResource" && e.Amount < 0))
                issues.Add(new("警告", $"卡牌「{c.Name}」勾了「额外资源量费用为 X」，但效果里没有「花费额外资源量」（负数）这一条："
                    + "牌面会比本体多显示一个资源量费用图标。"));
            if (c.XPlusOnUpgrade && !costIsX && !starIsX)
                issues.Add(new("警告", $"卡牌「{c.Name}」勾了「升级后 X +1」，但它的费用不是 X —— 这个勾选会被忽略。"));
            if (c.Effects.Any(e => e.AmountIsXEffective && e.UpgradeAmount != 0))
                issues.Add(new("警告", $"卡牌「{c.Name}」有一条「数值 = X」的效果填了「升级增量」：X 的数值不能直接升级（会被忽略），"
                    + "要升级请用卡牌上的「升级后费用」框写 X+1。"));
            // 可被无限升级：勾了却没有升级增量 = 能反复花钱升级但数值不变（本体的升级增量全靠 OnUpgrade 累加）
            if (c.InfinitelyUpgradable
                && c.UpgradeCost is null
                && !c.Effects.Any(e => e.UpgradeAmount != 0)
                && (c.UpgradeKeywords?.Added?.Count ?? 0) == 0
                && (c.UpgradeKeywords?.Removed?.Count ?? 0) == 0)
                issues.Add(new("警告", $"卡牌「{c.Name}」勾了「可被无限升级」，但一处升级增量都没填"
                    + "（效果里的「升级增量」/「升级后费用」/「升级后关键词」都是空的）：反复升级不会让数值变强。"));
            // 斩杀条件：判的是「上一条攻击有没有把目标打死」，所以要写在造成伤害那一条**后面**，
            // 而且只能写在单条效果上（写在整张牌的条件上时，包裹的那段代码跑在伤害之前，永远不成立）。
            if (c.Condition is { Kind: "Fatal" })
                issues.Add(new("错误", $"卡牌「{c.Name}」把「斩杀」当成了**整张牌**的条件：整张牌的条件是在效果跑之前判的，"
                    + "那时伤害还没发生、「斩杀」永远不成立。请把「斩杀」写到**造成伤害那一条效果后面**的那条效果上"
                    + "（本体 Feed / HandOfGreed 就是「造成伤害 → 斩杀时…」）。"));
            bool fatalWithoutDamage = false;
            foreach (var e in c.Effects)
            {
                if (e.Condition is not { Kind: "Fatal" }) continue;
                // 这条效果**前面**有没有「造成伤害」（卡牌自己的攻击 / 对敌人失去生命都算）
                int idx = c.Effects.IndexOf(e);
                bool hasDamageBefore = c.Effects.Take(idx).Any(x => x.Kind == "Damage"
                    || (x.Kind == "HpLoss" && x.TargetSide is "Enemy" or "AllEnemies" or "RandomEnemies"));
                if (!hasDamageBefore)
                {
                    fatalWithoutDamage = true;
                    break;
                }
            }
            if (fatalWithoutDamage)
                issues.Add(new("错误", $"卡牌「{c.Name}」有一条效果勾了「斩杀」，但它**前面没有任何造成伤害的效果** —— "
                    + "「斩杀」判的是「上一条攻击有没有把目标打死」，前面没有伤害就永远不成立。"
                    + "请在这条效果前面加一条「造成伤害」，或者去掉这个条件。"));
        }

        // 卡牌配色
        if (!CardColorSpec.IsHex(p.Colors.DeckEntryColor))
            issues.Add(new("错误", $"卡牌「牌堆底色」不是合法的十六进制颜色：{p.Colors.DeckEntryColor}（应为 RRGGBB 或 RRGGBBAA）。"));
        if (!CardColorSpec.IsHex(p.Colors.EnergyOutlineColor))
            issues.Add(new("错误", $"卡牌「能量描边色」不是合法的十六进制颜色：{p.Colors.EnergyOutlineColor}（应为 RRGGBB 或 RRGGBBAA）。"));
        if (!string.IsNullOrWhiteSpace(p.Colors.CardFrameColor) && !CardColorSpec.IsHex(p.Colors.CardFrameColor))
            issues.Add(new("错误", $"卡牌「边框颜色」不是合法的十六进制颜色：{p.Colors.CardFrameColor}（应为 RRGGBB，留空表示用边框材质）。"));
        if (!string.IsNullOrWhiteSpace(p.Art.IconOutlineColor) && !CardColorSpec.IsHex(p.Art.IconOutlineColor))
            issues.Add(new("错误", $"「头像描边颜色」不是合法的十六进制颜色：{p.Art.IconOutlineColor}（应为 RRGGBB，留空表示不自动生成描边）。"));
        // V0.2.230：「地图蜡笔颜色」（NMapDrawings 用 Character.MapDrawingColor 给 Line2D 上色）
        if (!string.IsNullOrWhiteSpace(p.MapDrawingColor) && !CardColorSpec.IsHex(p.MapDrawingColor))
            issues.Add(new("错误", $"「地图画线颜色」不是合法的十六进制颜色：{p.MapDrawingColor}"
                + "（应为 RRGGBB 或 RRGGBBAA，留空 = 本体默认黑色）。"));
        else if (CardColorSpec.NormalizeHex(p.MapDrawingColor) is { Length: 8 } mapHex8
                 && mapHex8[6..] is "00")
            issues.Add(new("警告", "「地图画线颜色」的透明度是 00（全透明）—— 地图上画出来的线会看不见。"
                + "想用不透明的颜色就写 6 位（RRGGBB）。"));
        CheckTreasureGestures(p, issues);
        if (!string.IsNullOrWhiteSpace(p.Colors.CardFrame) && !CardColorSpec.Frames.Contains(p.Colors.CardFrame))
            issues.Add(new("警告", $"卡牌边框「{p.Colors.CardFrame}」不在本体自带素材里（{string.Join(" / ", CardColorSpec.Frames)}），游戏里可能显示不出边框。"));
        // 「诅咒 / 先古卡」两类牌各自的卡框颜色（生成的是「外观池」+ 一份 hsv 染色材质）
        foreach (var (style, what) in new[] { (p.CurseStyle, "诅咒"), (p.AncientStyle, "先古卡") })
        {
            if (style is null) continue;
            if (style.IsCustomFrame && !style.AllowsCustomColor)
            {
                // 诅咒：用户要求去掉「自定义颜色」这一项 —— 老存档里存过的一律忽略（退回本体诅咒卡池）
                issues.Add(new("提示", $"{what}不支持「自定义卡框颜色」（这个选项已经去掉）："
                    + $"存档里那句 Frame = custom（颜色 {style.FrameColor}）会被忽略，诅咒仍然按本体的诅咒卡框（灰色）显示。"));
                continue;
            }
            if (style.IsCustomFrame)
            {
                // 填了内容但不是合法颜色 → 报错（空着 = 没设样式，不报）
                if (style.FrameColor.Trim().Length > 0 && CardColorSpec.NormalizeHex(style.FrameColor).Length == 0)
                    issues.Add(new("错误", $"{what}的自定义卡框颜色「{style.FrameColor}」不是合法的颜色（要 6 位 RRGGBB，例如 8A5CF6）。"));
            }
            else if (style.Frame.Length > 0 && !CardColorSpec.Frames.Contains(style.Frame))
            {
                issues.Add(new("警告", $"{what}的卡框「{style.Frame}」不在本体自带素材里（{string.Join(" / ", CardColorSpec.Frames)}），游戏里可能显示不出边框。"));
            }
        }
        if (!Naming.IsValidIdentifier(p.CharacterClass))
            issues.Add(new("错误", "角色英文类名必须是纯英文/数字且以字母开头（决定模型 ID 与所有资源文件名）。"));
        if (string.IsNullOrWhiteSpace(p.DisplayName))
            issues.Add(new("警告", "角色显示名为空，游戏里会显示成键名。"));
        if (p.StartingHp <= 0 || p.StartingHp > 999)
            issues.Add(new("错误", $"初始生命 {p.StartingHp} 不合理（建议 1~999）。"));
        if (p.StartingGold < 0 || p.StartingGold > 9999)
            issues.Add(new("错误", $"初始金币 {p.StartingGold} 不合理（建议 0~9999）。"));
        // V0.2.172：角色最大能量（本体默认 3；生成端一个 override 就够，见 CSharpCodeGen.CharacterSource）
        if (p.MaxEnergy < 1 || p.MaxEnergy > 99)
            issues.Add(new("错误", $"角色最大能量 {p.MaxEnergy} 不合理（建议 1~99；本体默认 3）。"));
        else if (p.MaxEnergy != 3)
            issues.Add(new("提示", $"角色最大能量 = {p.MaxEnergy}（本体默认 3）：这是每回合的能量上限，"
                + "改大 / 改小都会明显改变难度。生成时只在不是 3 的时候覆盖 CharacterModel.MaxEnergy。"));
        if (!new[] { "Neutral", "Feminine", "Masculine" }.Contains(p.Gender))
            issues.Add(new("错误", $"性别 {p.Gender} 非法（Neutral / Feminine / Masculine）。"));
        if (!string.IsNullOrWhiteSpace(p.UnlockAfter) && !Naming.IsValidIdentifier(p.UnlockAfter))
            issues.Add(new("错误", "前置角色必须填本体的角色类名（如 Silent / Defect / Ironclad）。"));

        // 卡牌
        var cardNames = new HashSet<string>(StringComparer.Ordinal);
        // 普通卡 + 诅咒 + 先古卡一起校验（三者在同一个命名空间里、类名必须互不重复）
        foreach (var c in p.AllCards)
        {
            // 额外资源量遗物上的效果（用户要求：那只隐藏起始遗物放到「遗物」页里能加效果）：
            // 资源量没启用时那只遗物根本不会生成 → 这些效果一个都不会生效，必须说清楚（不静默）。
            if (!p.ExtraResource.Enabled && p.ExtraResource.Effects.Count > 0)
                issues.Add(new("警告", $"「额外资源量遗物」上配了 {p.ExtraResource.Effects.Count} 条效果，"
                    + "但「额外资源量 / 状态」页没勾「启用额外资源量」—— 那只隐藏起始遗物不会生成，"
                    + "这些效果一条都不会生效（它们挂在那只遗物上）。要么勾上启用，要么把这几条效果挪到别的遗物 / 卡牌上。"));
            // 无色卡（用户要求）：进本体无色卡池。三条要说清的：
            //  ① 稀有度不能是 Ancient / Event / Quest / Token —— 本体 CardFactory 对这几档有特殊处理
            //    （`CardFactory.cs:172` 会把它们当「无色」用在别的用途上），做进无色池会出怪事；
            //  ② 它不进**你角色**的卡池 → 不帮「奖励里至少 3 张」那条规矩；
            //  ③ 卡框/能量前缀用本体的无色那套，本工具的自定义卡框对这张牌不生效（这是设计如此）。
            if (c.IsColorless && !c.IsVanillaCard)
            {
                if (c.Rarity is "Ancient" or "Event" or "Quest" or "Token")
                    issues.Add(new("错误", $"无色卡「{c.Name}」的稀有度是「{c.Rarity}」：本体 CardFactory 对 Ancient / Event / Quest / Token"
                        + " 这几档有特殊处理（会把它们当成「无色」用在别的用途上），放进无色卡池会出怪事。"
                        + "无色卡请用 Common / Uncommon / Rare。"));
                if (c.InCardPool)
                    issues.Add(new("提示", $"无色卡「{c.Name}」勾着「加入卡池」：无色卡**不进你角色的卡池**（奖励抽的是你角色的卡池），"
                        + "所以它不会帮「奖励里至少 3 张」那条规矩 —— 那个勾对无色卡没作用，留着也不影响。"));
                issues.Add(new("提示", $"无色卡「{c.Name}」的卡框 / 能量图标用**本体的无色那套**（card_frame_colorless），"
                    + "「卡牌」页的卡框颜色对这张牌不生效；它也不会出现在你角色的战斗奖励 / 商店里"
                    + "（只有「无色卡池」相关的来源才会给到它）。"));
            }
            // 作用对象 = 队友（用户要求）：一张牌只有一个 TargetType，队友和敌人不能混在一张牌上
            // （生成端会按「指定敌人」处理，队友那几条会拿到错误的目标）。
            if (!c.IsVanillaCard)
            {
                bool anyAlly = c.Effects.Any(e => e.TargetSide == "Ally");
                if (anyAlly && c.Effects.Any(e => e.TargetSide is "Enemy" or "AllEnemies" or "RandomEnemies"))
                    issues.Add(new("错误", $"卡牌「{c.Name}」里既有「作用对象 = 指定队友」的效果、也有打敌人的效果："
                        + "本体一张牌只有一个目标类型（TargetType），生成时按「指定敌人」处理，队友那几条会拿到错误的目标 —— "
                        + "请把它们拆成两张牌。"));
                if (anyAlly)
                    issues.Add(new("提示", $"卡牌「{c.Name}」有「作用对象 = 指定队友」的效果："
                        + "本体 TargetType.AnyAlly 是**多人联机**里才会弹目标选择的那一档，单人游戏里场上活着的玩家单位只有你一个，"
                        + "按本体规则（CardModel.cs:1742）**这张牌直接打不出去** —— 单人测试时请临时把它改成「自己」。"));
            }
            // 本体卡引用：只校验「类名是不是本体的英文类名」和初始份数，别的都不适用
            if (c.IsVanillaCard)
            {
                string vcls = (c.ClassName ?? "").Trim();
                if (!Naming.IsValidIdentifier(vcls))
                    issues.Add(new("错误", $"本体卡「{c.Name}」没填本体英文类名（例如 StrikeIronclad / DefendIronclad）。"));
                else if (EffectCatalog.Cards.Count > 0 && !EffectCatalog.Cards.Any(x => x.Id == vcls))
                    issues.Add(new("警告", $"本体卡「{c.Name}」的类名「{vcls}」在本体卡牌表里找不到 —— 生成出来的初始卡组会引用一张不存在的卡"
                        + "（要先在「构建 / 日志」页选好解包工程，工具才读得到本体卡牌表）。"));
                if (c.InStartingDeck && c.StartingCopies is < 1 or > 10)
                    issues.Add(new("警告", $"本体卡「{c.Name}」初始份数 {c.StartingCopies} 建议 1~10。"));
                if (c.InCardPool)
                    issues.Add(new("提示", $"本体卡「{c.Name}」不会进你自己的卡池（它属于本体的卡池），奖励里不会出现它 —— 只有初始卡组那几份。"));
                continue;
            }
            string cls = n.CardClassName(p, c);
            if (!cardNames.Add(cls))
                issues.Add(new("错误", $"卡牌类名重复：{cls}（自定义卡牌的英文类名需唯一）。"));
            // 「自动类名跟序号走」（用户要求第 1 条《卡牌页优化.md》）不许**静默**生效：
            // 用户手打了一个正好长得像自动编号的名字（`<角色类名> + Card|Curse|Ancient + <数字>`）、
            // 而那个数字和它现在排的位置对不上时，下一次生成 / 构建会被按当前位置改名
            // （`Naming.RenumberCardClassNames`）—— 这种「名字会被改掉」的事必须在报告里明说，
            // 否则用户只会看到「我没改它，它自己变了」。
            // 名字数字 == 当前位置（幂等那条路）就不报，免得每次生成都刷一行没用的提示。
            // ⚠ 比的是「这个位置**应该**用的自动名」，不能用 n.CardClassName(p, c) ——
            // 那个函数在有类名时原样返回用户的类名，一比就永远相等、永远不报。
            // ⚠ V0.2.137 号的口径（用户拍板 A）和 Naming.AutoCardClassName **完全一致**：
            // **每页列表各自 1..N**（普通卡含本体卡 / 初始打击防御、诅咒、先古卡各自从 1 数），
            // 前缀按稀有度分三套 —— 所以这里必须直接调那个函数，别再自己拼
            // `CharClass + "Card" + 连续行号`（老口径的拼法在新口径下会误报 / 漏报）。
            // ⚠ 这里用**带角色类名**的判据（`n.IsAutoCardClassName`）：生成端
            // `RenumberCardClassNames` 用的就是它，两边必须一字不差 —— 用「只按形状认」那版
            // 会对手打的 `MyCard1`（本角色类名不是 My）报一条永远不会发生的「下次会改名」。
            if (n.IsAutoCardClassName(c.ClassName))
            {
                string wantAuto = n.AutoCardClassName(p, c);
                if (!string.Equals(c.ClassName!.Trim(), wantAuto, StringComparison.Ordinal))
                    issues.Add(new("提示", $"卡牌「{c.Name}」的英文类名 {c.ClassName.Trim()} 长得像**工具自动生成的编号**，"
                        + $"但它现在排在{(c.IsCurseCard ? "诅咒卡" : c.IsAncientCard ? "先古卡" : "普通卡")}列表第 "
                        + $"{Naming.CardRowOf(p, c)} 位 —— 下次生成 / 构建会按位置改名为 {wantAuto}。"
                        + "如果你想让它固定不变，请换一个不像自动编号的名字（例：SevenSlash）。"));
            }
            // 多人联机：本体模型 ID 只看**类名**（不含命名空间），而且联机时所有人的模组都得装齐 ——
            // 两个模组里有同名类就会在加载模型库时抛 DuplicateModelException（游戏起不来）。
            // 自己起的类名带上角色类名前缀最省事（自动生成的类名已经带了，所以这里只提醒手填的那些）。
            if (!string.IsNullOrWhiteSpace(c.ClassName) && !Naming.IsBasicCardName(c.ClassName) && !cls.StartsWith(n.CharClass, StringComparison.Ordinal))
                issues.Add(new("提示", $"卡牌「{c.Name}」的英文类名「{cls}」没带角色前缀「{n.CharClass}」—— "
                    + "多人联机时所有人的模组会装在同一台机器上，**两份模组有同名类**会让游戏加载模型库直接抛异常"
                    + "（本体的模型 ID 只按类名算，不含命名空间）。建议改成 " + n.CharClass + cls + "。"));
            // 纯小写类名 → C# 会报 CS8981（「类型名称仅包含小写 ascii 字符…可能会成为该语言的保留值」）。
            // 编译能过，只是刷警告；但以后真跟保留字撞上就是编译不过，所以提醒一句（用户实测报过一堆）。
            if (!string.IsNullOrWhiteSpace(c.ClassName) && Naming.IsValidIdentifier(c.ClassName)
                && cls.All(ch => !char.IsUpper(ch)) && cls.Any(char.IsLetter))
                issues.Add(new("提示", $"卡牌「{c.Name}」的英文类名「{cls}」全是小写：C# 会报 CS8981 警告"
                    + "（「类型名称仅包含小写 ascii 字符…可能会成为该语言的保留值」），编译能过但建议改成首字母大写"
                    + $"（例：{char.ToUpperInvariant(cls[0])}{cls.Substring(1)}）。"));
            // 类名不能和本体卡重名：本体的模型 ID 只按类名算（忽略命名空间），
            // 自己定义一个 StrikeIronclad 会和本体撞 ID，模组加载时抛 DuplicateModelException。
            if (EffectCatalog.Cards.Any(x => string.Equals(x.Id, cls, StringComparison.OrdinalIgnoreCase)))
                issues.Add(new("错误", $"卡牌「{c.Name}」的英文类名「{cls}」和本体卡重名 —— 本体的模型 ID 只按类名算"
                    + "（忽略命名空间），重名会让模组加载当场抛 DuplicateModelException。请改个自己的名字"
                    + $"（比如你自己的前缀：My{cls}）。初始的打击 / 防御已经用不会撞名的 Strike / Defend 了。"));
            // 初始打击 / 防御靠 CardTag 被本体的遗物认出来，漏标就等于那些遗物找不到这张牌。
            // 注意：这里比的是**配置里的类名**（Strike / Defend）—— 生成出来的类名带了角色类名前缀（<角色>Strike）。
            bool isBasicCard = Naming.IsBasicCardName(c.ClassName);
            if ((isBasicCard && string.Equals(c.ClassName!.Trim(), "Strike", StringComparison.OrdinalIgnoreCase) && !c.TagList.Contains("Strike"))
                || (isBasicCard && string.Equals(c.ClassName!.Trim(), "Defend", StringComparison.OrdinalIgnoreCase) && !c.TagList.Contains("Defend")))
                issues.Add(new("警告", $"卡牌「{c.Name}」（{c.ClassName}）没有标本体卡标签 —— 本体那些「升级你的初始打击 / 防御」的"
                    + "遗物 / 事件是按 CardTag 查牌的，漏标它们就找不到这张牌。到「卡牌」页的「本体卡标签」里勾上 Strike / Defend。"));
            if (!EffectCatalog.CardTypes.Contains(c.CardType)
                // 诅咒的类型就是本体的 CardType.Curse（这一档不给普通卡选，但诅咒固定是它）
                && !(c.IsCurseCard && string.Equals(c.CardType, "Curse", StringComparison.OrdinalIgnoreCase)))
                issues.Add(new("错误", $"卡牌「{c.Name}」类型非法：{c.CardType}"));
            if (!EffectCatalog.CardRarities.Contains(c.Rarity))
                issues.Add(new("错误", $"卡牌「{c.Name}」稀有度非法：{c.Rarity}"));

            // ===== 诅咒 =====
            if (c.IsCurseCard)
            {
                // 费用（用户要求：诅咒 / 先古 / 状态三类都能调费用）。
                //  · 负数（默认 -1）= 打不出去，生成时不写「不能被打出」以外的额外东西，效果在「回合结束还在手牌里」；
                //  · ≥ 0 = **能打出去**：生成时不再强制「不能被打出」，但效果仍然写在「回合结束还在手牌里」，
                //    所以打出去本身不做事（只是能把它从手里处理掉）—— 必须说清楚，不静默。
                if (c.Cost >= 0)
                    issues.Add(new("提示", $"诅咒「{c.Name}」的费用是 {c.Cost}（≥0）：生成出来的这张牌**能打出去**"
                        + "（不会再强制「不能被打出」）。但它的效果仍然写在「你的回合结束时，如果它还在你的手牌里」，"
                        + "所以打出去本身不做事（只是能把它从手里丢掉）。想让效果在打出时生效，请到「卡牌」页做一张普通牌"
                        + "（稀有度用 Common / Uncommon / Rare）。"));
                else if (c.Cost != -1)
                    issues.Add(new("提示", $"诅咒「{c.Name}」的费用是 {c.Cost}：负数都是「打不出去」（本体用的就是 -1），"
                        + $"卡面上的费用数字会显示 {c.Cost}。"));
                if (c.Effects.Count == 0 && !c.HasCustomDescription)
                    issues.Add(new("警告", $"诅咒「{c.Name}」没有任何效果、也没写自定义描述 —— 它只会是一张"
                        + "「不能被打出」的白板牌（本体「苦恼 Writhe」就是这样，确认这是你想要的）。"));
                if (c.InStartingDeck)
                    issues.Add(new("警告", $"诅咒「{c.Name}」被放进了初始卡组 —— 开局手里就有这张诅咒，确认这是你想要的。"));
                if (c.InCardPool)
                    issues.Add(new("提示", $"诅咒「{c.Name}」不会出现在战斗奖励 / 商店里（本体只从 Common / Uncommon / Rare 里抽奖励）"
                        + "—— 它要由你自己的效果（生成卡牌 / 获得卡牌（全局）/ 获得卡牌奖励）或遗物给出来。"));
                // 诅咒的效果是在「回合结束还在手牌里」触发的：随机目标 / 指定敌人都能算出来，
                // 但没有 CardPlay，所以「打出的目标」这类写法在这里没有意义
                foreach (var e in c.Effects.Where(x => x.Kind is "TakeFromDraw" or "TakeFromDiscard"))
                    issues.Add(new("警告", $"诅咒「{c.Name}」的「{EffectCatalog.FindKind(e.Kind).Display}」会在回合结束时弹选牌界面，"
                        + "每条效果都弹一次会打断战斗节奏 —— 确认这是你想要的。"));
            }
            // ===== 状态卡（用户要求：「诅咒 / 先古卡」页改成「诅咒 / 先古 / 状态」）=====
            else if (c.IsStatusCard)
            {
                // 本体状态牌（伤口 / 灼伤 / 眩晕 / 粘液）都是 **CardType.Status + CardRarity.Status**；
                // 类型选了别的也会生成得出来，但卡面分类 / 本体的「按类型查牌」会对不上，所以提示一句。
                if (!string.Equals(c.CardType, "Status", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("提示", $"状态卡「{c.Name}」的类型是「{c.CardType}」：本体那些状态牌（伤口 / 灼伤 / 眩晕 / 粘液）"
                        + "类型都是 Status。类型不影响能不能打出去，只影响卡面分类 / 本体按类型查牌，"
                        + "要跟本体一致就把「类型」也改成 Status。"));
                if (c.InfinitelyUpgradable)
                    issues.Add(new("提示", $"状态卡「{c.Name}」勾了「可被无限升级」：本体状态牌是不能升级的"
                        + "（粘液 Slimed 写着 MaxUpgradeLevel => 0），这里按你的勾生成「能升级的状态卡」。"));
                if (c.InCardPool)
                    issues.Add(new("提示", $"状态卡「{c.Name}」不会出现在战斗奖励 / 商店里（本体的奖励只从 Common / Uncommon / Rare 里抽）"
                        + "—— 它要由你自己的效果（生成卡牌 / 获得卡牌（全局））或敌人的效果给出来。"));
                if (c.InStartingDeck)
                    issues.Add(new("警告", $"状态卡「{c.Name}」被放进了初始卡组 —— 开局牌组里就有这张状态牌"
                        + "（本体只有「诅咒」才会这么干），确认这是你想要的。"));
            }
            // ===== 先古卡 =====
            else if (c.IsAncientCard)
            {
                if (c.InCardPool)
                    issues.Add(new("提示", $"先古卡「{c.Name}」不会出现在战斗奖励 / 商店里（本体 CardFactory 显式排除 Ancient）"
                        + "—— 拿到它的方式是先古遗物（本体「尘封的书」会从你的卡池里随机挑一张先古卡）或你自己的效果。"));
                if (c.Rarity is "Basic")
                    issues.Add(new("错误", $"卡牌「{c.Name}」的稀有度不能既是先古卡又是 Basic。"));
            }

            // V0.2.131：费用栏可以写生成期常量式子（1+1 / A+3）—— 该报的是**算完之后的费用**，
            // 而不是那个只当占位用的老字段 Cost（否则「费用框写 1+1、Cost 还是默认的 1」时看着没问题，
            // 「Cost 填 9、式子折成 2」时反而平白报一条）。
            int costForRange = CSharpCodeGen.CardCostOf(p, c);
            if (!CSharpCodeGen.IsCostX(c) && costForRange is < 0 or > 5 && !c.IsCurseCard)
                issues.Add(new("警告", $"卡牌「{c.Name}」费用 {costForRange} 超出常规范围（0~5）。"));
            if (c.InStartingDeck && c.StartingCopies is < 1 or > 10)
                issues.Add(new("警告", $"卡牌「{c.Name}」初始份数 {c.StartingCopies} 建议 1~10。"));
            ValidateEffects(issues, $"卡牌「{c.Name}」", c.Effects, ctx: c.IsCurseCard ? "Curse" : "Card", p: p);
            // 「数值公式」里引用了「指定敌人」的参数，但这张牌打出时**根本没有选中某个敌人**（全体敌人那种）
            // → 那个参数永远是 0，公式静默失效。用户实测报过：「设 A 为敌人的虚弱、伤害写 A+3、作用对象全体敌人」
            // 生成出来是 `PowerAmount(cardPlay?.Target, …)`，而全体敌人的牌 `cardPlay.Target` 是 null → 伤害恒为 3。
            if (!c.IsCurseCard) ValidateFormulaTargets(issues, c, p);
            // 「斩杀」条件只能用在**打出时**那一段效果上（V0.2.169，见方法注释）
            ValidateFatalPlacement(issues, c);
            // 锻造节点图（升级前 / 升级后两块布）：图里的问题以前**一个都没报给用户**
            //（ForgeGraphGen.Problems 是死代码，没人调）—— 最要命的是「【效果】选了节点图没接上的种类」：
            // 生成、编译、进游戏全都不报错，就是没反应（别人实测报的「给召唤物 buff 不生效」就是这个）。
            ValidateForgeGraph(issues, $"卡牌「{c.Name}」（升级前）", c.ForgeGraph, p, "Card", CSharpCodeGen.TargetTypeOf(c));
            ValidateForgeGraph(issues, $"卡牌「{c.Name}」（升级后）", c.UpgradedForgeGraph, p, "Card", CSharpCodeGen.TargetTypeOf(c));
            AddDuplicateVarNotice(issues, $"卡牌「{c.Name}」", c.Effects);
            // 老存档的「整张牌一个条件」也校验一下（打开后会自动搬到第一条效果上）
            if (c.Condition is not null && !c.Condition.IsNone && c.Effects.Count > 0)
                issues.Add(new("提示", $"卡牌「{c.Name}」用的是老版「整张牌一个条件」，已按老存档兼容处理；"
                    + "界面上现在改成「每条效果各自一个条件」，重新打开这张牌就能改。"));
            ValidateCondition(issues, $"卡牌「{c.Name}」", c.Condition, "Card");
            // 两条「这一次打出」的卡牌专属读数（ThisCardDamage / ThisCardBlock）不能写在**整张牌的条件**里：
            // 那个条件会落进本体的 IsPlayable / ShouldGlowGoldInternal —— 那两处是在**打出之前**求值的，
            // 那时这张牌一点伤害都还没造成、格挡也还没拿到（属性还是 0）。要判就判在**某一条效果自己的条件**上。
            foreach (var r in CSharpCodeGen.CardReadingsAll)
                if (CSharpCodeGen.MentionsReading(c.Condition, r.Id))
                    issues.Add(new("错误", $"卡牌「{c.Name}」的**整张牌条件**里用了 {CSharpCodeGen.ReadingZh(r.Id)} ——"
                        + "整张牌的条件是在**打出之前**求值的（本体的「能不能打出 / 描金边」），那时这张牌还没有造成任何伤害 / 获得任何格挡，"
                        + "读出来恒为 0。请把这条条件挪到**某一条效果**自己的「条件选项」上（那条在打出过程中求值），"
                        + "而且要排在对应的那条效果**后面**。"));
        }

            // 遗物
        var relicNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            string cls = n.RelicClassName(r, i);
            if (!relicNames.Add(cls)) issues.Add(new("错误", $"遗物类名重复：{cls}"));
            if (!EffectCatalog.RelicRarities.Contains(r.Rarity))
                issues.Add(new("错误", $"遗物「{r.Name}」稀有度非法：{r.Rarity}"));
            // 第 10 批：一只遗物能挂**多条触发时机**，每条各自校验一遍。
            // 错误信息前面带上「第 N 条时机」——只有一条时**一个字都不加**（老存档的消息逐字不变）。
            var relicTriggerList = r.Triggers.Where(t => t is not null).ToList();
            if (relicTriggerList.Count == 0) relicTriggerList.Add(r.First);
            bool multiTrigger = relicTriggerList.Count > 1;
            // V0.2.126：效果是**整只遗物唯一那一份** → **只校验一次**（不再按每条时机各校验一遍效果）。
            // 消息里的定位前缀按形状选，保证「只有一条时机」时和以前**逐字一致**（老存档的消息不变）：
            //   · 单条时机 → "遗物「X」"（V0.2.125 的文本）；
            //   · 多条时机 → "遗物「X」的效果（…）"（点明是共享那一份，但要能让用户分清是哪条时机的配置问题）。
            string fxWhere = multiTrigger ? $"遗物「{r.Name}」的效果（{string.Join("、", relicTriggerList.Select(t => RelicTriggerName(t.Kind))) } 共用同一份）" : $"遗物「{r.Name}」";
            ValidateEffects(issues, fxWhere, r.Effects, ctx: "Relic", p: p);
            AddDuplicateVarNotice(issues, fxWhere, r.Effects);
            // 关系选「全部满足才触发」但只配了 1 条时机 → 凑不齐，「不生效」（和「自定义状态」页同一句口径）
            if (r.IsAllTriggers && relicTriggerList.Count <= 1)
                issues.Add(new("提示", $"遗物「{r.Name}」的触发时机关系选的是「全部满足才触发」，"
                    + "但这只遗物上只有 1 条触发时机 —— 没有别的时机和它凑，所以这条设置**不生效**"
                    + "（照旧这条时机成立就触发）。想用「全部满足才触发」请再加一条触发时机。"));
            // 每只遗物一份：多条时机共用同一份效果时，同一条「只在获得时才有意义」的错误只报一次
            var relicAncientMsgs = new HashSet<string>(StringComparer.Ordinal);
            for (int ti = 0; ti < relicTriggerList.Count; ti++)
            {
                var trig = relicTriggerList[ti];
                // 只有一条时机时前缀为空串 → 下面所有消息文本和以前完全一致
                string tPrefix = multiTrigger ? $"第 {ti + 1} 条时机（{RelicTriggerName(trig.Kind)}）的" : "";
                string tWhere = multiTrigger ? $"第 {ti + 1} 条时机（{RelicTriggerName(trig.Kind)}）" : "";
                if (!EffectCatalog.RelicTriggers.Any(t => t.Id == trig.Kind))
                    issues.Add(new("错误", $"遗物「{r.Name}」触发时机非法：{trig.Kind}"));
                if (trig.Kind == "Obtained")
                    issues.Add(new("提示", $"遗物「{r.Name}」的{tPrefix}触发时机是「获得时」：拿到这只遗物的那一刻触发一次"
                        + "（本体钩子 AfterObtained —— 本体的「好吃饼干 YummyCookie / 磨刀石 Whetstone / 爪子 Claws」"
                        + "这些「拾取时生效」的遗物都是它）。这个钩子本体没给选牌上下文，需要的那几条效果"
                        + "（生成卡牌 / 抽牌 / 卡牌奖励…）生成时会自己造一个阻塞式上下文，能用。"));
                // 「受到伤害时」是**每一下**都触发的钩子：本体的敌人攻击很多是「1 点 × N 下」的连击，
                // 所以一次「5 点伤害」的攻击会触发 5 次（本体原体黏土 SelfFormingClay 也是这样）。
                // 用户报过「5 点伤害触发了五次」——这里直接说清楚，并指路「受到攻击后（连击只算一次）」。
                if (trig.Kind == "DamageReceived")
                    issues.Add(new("提示", $"遗物「{r.Name}」的{tPrefix}触发时机是「受到伤害时（每一下）」：**每一下伤害都会触发一次** —— "
                        + "敌人的多次连击（本体很多攻击是「1 点 × 5 下」，意图上也写着次数）会触发多次。"
                        + "想让「一次攻击只触发一次」请把触发时机改成「受到攻击后（连击只算一次）」（本体钩子 AfterAttack，"
                        + "在所有命中都结束之后才跑一次）。全被格挡 / 0 伤害的那一下不算（生成时会判 UnblockedDamage > 0）。"));
                if (trig.Kind == "Attacked")
                    issues.Add(new("提示", $"遗物「{r.Name}」的{tPrefix}触发时机是「受到攻击后（连击只算一次）」："
                        + "一次攻击的**所有命中都结束之后**只触发一次（本体钩子 AfterAttack），"
                        + "所以「1 点 × 5 下」的连击也只算一次。判据是这一次攻击真的打到了你身上"
                        + "（全被格挡 / 打的是别人时不触发）。注意它只认「攻击」：中毒、事件掉血这类不是攻击的伤害不会触发它。"));
                // 「只对哪类牌生效」（用户要求，第 9 批 ④：遗物侧和状态侧同步）：
                // 只有「打出一张牌后」用得到。关键词组 / 自定义组里**一张牌都没有**时，
                // 生成出来的判据是 `if (false)` —— 这条时机永远不会触发，必须拦住并说清（不静默）。
                if (trig.Kind == "CardPlayed"
                    && (EffectCatalog.IsKeywordFilter(trig.CardFilter) || EffectCatalog.IsCardGroupFilter(trig.CardFilter)))
                {
                    int relicGroupCount = EffectCatalog.IsCardGroupFilter(trig.CardFilter)
                        ? EffectCatalog.CardGroupCards(p, trig.CardFilter).Count
                        : EffectCatalog.KeywordGroupCards(p, trig.CardFilter).Count;
                    if (relicGroupCount == 0)
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「只对哪类牌生效」选的是"
                            + $"「{PowerTriggers.CardFilterZh(trig.CardFilter)}」，但这一组里**一张牌都没有**"
                            + "（没勾任何牌 / 那个关键词没人用）—— 这条时机永远不会触发。"
                            + "请到「卡牌」页把牌勾进这个组，或者把筛选改回「任意 / 攻击 / 技能 / 能力」。"));
                }
                else if (trig.Kind != "CardPlayed" && !string.Equals(trig.CardFilter, "Any", StringComparison.Ordinal))
                {
                    issues.Add(new("提示", $"遗物「{r.Name}」{tWhere}填了「只对哪类牌生效 = {PowerTriggers.CardFilterZh(trig.CardFilter)}」，"
                        + "但触发时机不是「打出一张牌后」—— 这个值**不生效**（留着不碍事，换成那个时机就会用上）。"));
                }
                // 「转化为先古卡」：只在「获得时」有意义（那时牌组是稳定的；战斗里改的是战斗牌堆，
                // 而且本体的先古卡替换遗物（古老牙齿 / 潘多拉魔盒）清一色挂在 AfterObtained）。
                // ⚠ 效果是整只遗物共享的：多条时机时同一条效果会被检查好几遍 —— 用 Set 去重，
                //   免得同一句话在问题列表里刷好几条。
                foreach (var e in r.Effects.Where(x => x.Kind == "AncientTranscend"))
                {
                    if (trig.Kind != "Obtained" && relicAncientMsgs.Add("transcend|" + trig.Kind))
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「转化为先古卡」的触发时机是"
                            + $"「{RelicTriggerName(trig.Kind)}」"
                            + "—— 这条效果只允许挂在「获得时」（它是改**牌组**的：本体的古老牙齿 / 潘多拉魔盒都在拾取那一刻做）。"));
                    ValidateAncientRows(issues, $"遗物「{r.Name}」", e, p);
                }
                // 「某个状态层数到达 N 层时」（用户要求）：两个参数只有选了这个时机才有意义
                if (trig.Kind == "PowerReached")
                {
                    if (trig.Amount <= 0m)
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}触发时机是「某个状态层数到达 N 层时」，但「到达 N 层」填的是 {trig.Amount:0.##}："
                            + "层数要从 0 往上跨才可能触发，请填 ≥ 1 的数（一般就是这个状态最有意义的那一档层数）。"));
                    if (string.IsNullOrWhiteSpace(trig.PowerId))
                        issues.Add(new("提示", $"遗物「{r.Name}」的{tPrefix}触发时机是「某个状态层数到达 N 层时」，但「盯哪个状态」留空 —— "
                            + "这样自己身上**任何**状态的层数跨过 N 层都会触发。想只盯某一个状态就在上面选一个。"));
                    if (r.Effects.Count == 0)
                        issues.Add(new("警告", $"遗物「{r.Name}」{tWhere}选了「某个状态层数到达 N 层时」但这只遗物一个效果都没有 —— 它不会做任何事。"));
                }
                else if (!string.IsNullOrWhiteSpace(trig.PowerId) || trig.Amount != 1m)
                {
                    issues.Add(new("提示", $"遗物「{r.Name}」{tWhere}填了「盯哪个状态 / 到达 N 层」（{trig.PowerId ?? "任意"} / {trig.Amount:0.##}），"
                        + "但触发时机不是「某个状态层数到达 N 层时」—— 这两个值**不生效**（留着不碍事，换成那个时机就会用上）。"));
                }
                // 「花掉 N 点」的数值（用户要求）：只对「花掉能量 / 花掉额外资源量」有意义
                if (trig.Kind is "EnergySpent" or "StarsSpent")
                {
                    if (trig.Amount < 0m)
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「花掉多少点触发」填的是 {trig.Amount:0.##}：不能是负数，请填 ≥ 1 的数（留空 / 0 = 每 1 点）。"));
                }
                else if (trig.Amount < 0m)
                {
                    issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}触发时机数值填的是 {trig.Amount:0.##}：不能是负数。"));
                }
                // 「转化为先古遗物」（用户要求）：同样只在「获得时」有意义（本体欧洛巴斯之触就是 AfterObtained）。
                // ⚠ 同上去重：效果是整只遗物共享的，多条时机时同一条只报一次。
                foreach (var e in r.Effects.Where(x => x.Kind == "AncientRelicSwap"))
                {
                    if (trig.Kind != "Obtained" && relicAncientMsgs.Add("swap|" + trig.Kind))
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「转化为先古遗物」的触发时机是"
                            + $"「{RelicTriggerName(trig.Kind)}」"
                            + "—— 这条效果只允许挂在「获得时」（本体「欧洛巴斯之触」就是拾取那一刻换的）。"));
                    if (string.IsNullOrWhiteSpace(e.SpawnCardId))
                        issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「转化为先古遗物」还没选「目标遗物」（效果下面那一栏）—— 这一条会被跳过。"));
                    else
                    {
                        string want = e.SpawnCardId!.Trim();
                        bool mine = p.Relics.Any(x => x is not null && string.Equals(Naming.From(p).RelicClassName(x, p.Relics.IndexOf(x)), want, StringComparison.Ordinal));
                        bool vanilla = AncientCatalog.VanillaRelics.Any(v => string.Equals(v.Id, want, StringComparison.Ordinal));
                        if (!mine && !vanilla && AncientCatalog.VanillaRelics.Count > 0)
                            issues.Add(new("错误", $"遗物「{r.Name}」的{tPrefix}「转化为先古遗物」要换成「{want}」，但这个类名在本存档和本体遗物表里都找不到 —— 请重新选一个。"));
                        // 换成自己做的「初始遗物」会有点怪：初始遗物是开局就带着的，换出来等于凭空多一只
                        var myRelic = p.Relics.FirstOrDefault(x => x is not null && string.Equals(x.ClassName, want, StringComparison.Ordinal));
                        if (myRelic is { IsStartingRelic: true })
                            issues.Add(new("提示", $"遗物「{r.Name}」的{tPrefix}「转化为先古遗物」要换成「{myRelic.Name}」，"
                                + "但那只勾了「作为初始遗物」—— 换出来的话它会**额外**出现在遗物栏（初始遗物本来是开局就带着的）。"));
                        if (string.Equals(e.SpawnFilter, EffectCatalog.RelicFilterStarter, StringComparison.Ordinal)
                            && !p.Relics.Any(x => x is not null && x.IsStartingRelic))
                            issues.Add(new("警告", $"遗物「{r.Name}」的{tPrefix}「转化为先古遗物」只换初始遗物，"
                                + "但你还没有任何遗物勾了「作为初始遗物」—— 生成出来的效果永远不会触发。"));
                    }
                }

                // V0.2.233：`BootstrapContextRuntime`（「获得金币时」）现在会按运行时造 context，
                // 所以它不再算「缺 choiceContext」；其余没有 context 的时机仍然会把这几种效果静默丢掉。
                if (!CSharpCodeGen.HasContext(trig.Kind) && !CSharpCodeGen.CanBootstrapContext(trig.Kind)
                    && !CSharpCodeGen.BootstrapContextRuntime(trig.Kind))
                {
                    foreach (var e in r.Effects.Where(x => x is not null && CSharpCodeGen.RelicNeedsChoiceContext(x)))
                    {
                        if (!relicAncientMsgs.Add("ctx|" + trig.Kind + "|" + e.Kind)) continue;   // 同上：同一句只报一次
                        issues.Add(new("警告",
                            $"遗物「{r.Name}」的{tPrefix}「{EffectCatalog.FindKind(e.Kind).Display}」在「{RelicTriggerName(trig.Kind)}」缺少 choiceContext，生成时会被忽略（请把这条效果挪到「战斗开始时 / 每回合开始时」这类时机上）。"));
                    }
                }
            }
            // 这两条是**整只遗物**的（不属于某一条时机），所以留在时机循环外面
            ValidateForgeGraph(issues, $"遗物「{r.Name}」", r.ForgeGraph, p, "Relic");
            ValidateCondition(issues, $"遗物「{r.Name}」（整只遗物的触发条件）", r.Condition, "Relic");
        }
        if (p.Relics.Count(r => r.IsStartingRelic) > 3)
            issues.Add(new("警告", "初始遗物超过 3 个，界面可能显示不下。"));

        // 药水
        var potionNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < p.Potions.Count; i++)
        {
            var s = p.Potions[i];
            string cls = n.PotionClassName(s, i);
            if (!potionNames.Add(cls)) issues.Add(new("错误", $"药水类名重复：{cls}"));
            if (!EffectCatalog.PotionRarities.Contains(s.Rarity))
                issues.Add(new("错误", $"药水「{s.Name}」稀有度非法：{s.Rarity}"));
            if (!EffectCatalog.PotionUsages.Contains(s.Usage))
                issues.Add(new("错误", $"药水「{s.Name}」使用时机非法：{s.Usage}"));
            if (!EffectCatalog.PotionTargets.Contains(s.TargetType))
                issues.Add(new("错误", $"药水「{s.Name}」目标非法：{s.TargetType}"));
            // 「自动类名跟序号走」（用户要求第 6 条「药水方面参考遗物的优化方案」）不许**静默**生效：
            // 用户手打了一个正好长得像自动编号的名字（`<角色类名>Potion<数字>`）、而那个数字和它现在排的
            // 位置对不上时，下一次生成 / 构建会被按当前位置改名（`Naming.RenumberPotionClassNames`）——
            // 这种「名字会被改掉」的事必须在生成日志里明说，否则用户只会看到「我没改它，它自己变了」。
            // 名字数字 == 当前位置（幂等那条路）就不报，免得每次生成都刷一行没用的提示。
            // ⚠ 比的是「这个位置**应该**用的自动名」，不能用 n.PotionClassName(s, i) ——
            // 那个函数在有类名时原样返回用户的类名，一比就永远相等、永远不报。
            string wantAuto = n.CharClass + "Potion" + (i + 1).ToString();
            if (Naming.IsAutoPotionClassNameFor(null, s.ClassName)
                && !string.Equals(s.ClassName.Trim(), wantAuto, StringComparison.Ordinal))
                issues.Add(new("提示", $"药水「{s.Name}」的英文类名 {s.ClassName.Trim()} 长得像**工具自动生成的编号**，"
                    + $"但它现在排第 {i + 1} 位 —— 下次生成 / 构建会按位置改名为 {wantAuto}。"
                    + "如果你想让它固定不变，请换一个不像自动编号的名字（例：SevenFlask）。"));
            ValidateEffects(issues, $"药水「{s.Name}」", s.Effects, s.TargetType, ctx: "Potion", p: p);
            AddDuplicateVarNotice(issues, $"药水「{s.Name}」", s.Effects);
            ValidateForgeGraph(issues, $"药水「{s.Name}」", s.ForgeGraph, p, "Potion");

            if (s.Usage == "AnyTime" && s.Effects.Any(e => e.Kind is "Damage" or "Block" or "Draw" or "ApplyPower" or "TempPower"))
                issues.Add(new("警告", $"药水「{s.Name}」是「任意时机」，但含战斗内效果（伤害/格挡/抽牌/挂增益），战斗外会缺少战斗上下文；建议改「仅战斗中」。"));

            // 目标是自己的药水 + 「造成伤害」：**生成出来一条都不会执行**（药水没有「自己挨打」这条路），
            // 以前只在生成的代码里留一行注释 —— 生成 / 编译 / 进游戏全都不报错，就是没反应。
            // 想「喝下去对敌人造成伤害」请把药水的目标改成单体敌人 / 全体敌人。
            if (s.TargetType == "Self" && s.Effects.Any(e => e.Kind == "Damage"))
                issues.Add(new("警告", $"药水「{s.Name}」的目标是「自己」，但里面有一条「造成伤害」—— "
                    + "这条**不会执行**（药水不会对自己造成伤害）。想喝下去打敌人，请把药水的「目标」改成「单体敌人 / 全体敌人」；"
                    + "真要「付出生命换效果」请用效果种类「失去生命」。"));
        }

        // 初始卡组：本体卡引用（打击 / 防御）也是牌，所以「空不空」要看有没有任何放进初始卡组的牌
        if (!p.AllCards.Any(c => c.InStartingDeck))
            issues.Add(new("警告", "初始卡组是空的：卡牌页里没有任何卡勾了「放进初始卡组」"
                + "（默认那两条本体「打击 / 防御」被删掉了吗？）—— 开局会没有牌可打。"));
        if (!p.Cards.Any(c => !c.IsVanillaCard) && !p.Curses.Any() && !p.AncientCards.Any())
            issues.Add(new("提示", "还没有自己的卡牌：现在初始卡组只有本体的打击 / 防御（本体卡只做引用，不生成自己的卡类）。"));

        // V0.2.225：「本回合预见过 / 本回合已预见至少 N 次」靠工具自己记的计数器 ——
        // 但**只有「预见」那条效果**会 +1（本体没有预见这个机制）。整份配置里一条「预见」都没有时
        // 这两条条件永远不成立，这里说清（不然又是「配了没反应」）。
        if (CSharpCodeGen.UsesScryCounter(p)
            && !CSharpCodeGen.AllEffects(p).Any(e => e is not null && string.Equals(e.Kind, "Scry", StringComparison.Ordinal)))
            issues.Add(new("警告", "这份配置用了条件「本回合预见过 / 本回合已预见至少 N 次」，"
                + "但整份配置里**一条「预见」效果都没有** —— 那个计数器永远是 0，这两条条件永远不会成立。"
                + "要么加一条「预见」效果，要么把条件换掉。"));

        // 安装目录：本体只认 <游戏目录>\mods（选成游戏目录 / data 目录时自动纠正，这里只提醒）
        bool? looksLikeMods = PathAutoDetect.LooksLikeModsDir(p.Paths.InstallDir);
        if (looksLikeMods == false)
        {
            string? mods = PathAutoDetect.FindModsDir(p.Paths.InstallDir) ?? PathAutoDetect.FindModsDir();
            issues.Add(new("警告", $"安装目录看起来不是游戏的 mods 目录：{p.Paths.InstallDir}"
                + (mods is null
                    ? "（一般是 <游戏目录>\\mods，例如 ...\\steamapps\\common\\Slay the Spire 2\\mods）"
                    : $"（已自动改成：{mods}）")));
        }

        // 路径
        if (!Directory.Exists(p.Paths.VanillaProject))
            issues.Add(new("错误", $"解包工程目录不存在：{p.Paths.VanillaProject}（占位美术与 spine 插件要从这里取）"));
        if (!File.Exists(Path.Combine(p.Paths.GameDataDir, "sts2.dll")))
            issues.Add(new("错误", $"游戏 data 目录里找不到 sts2.dll：{p.Paths.GameDataDir}"));
        if (!File.Exists(p.Paths.GodotExe))
            issues.Add(new("警告", $"Godot 可执行文件不存在：{p.Paths.GodotExe}（只影响导出 PCK）"));

        // 本体签名漂移核对（V0.2.167）：生成端有几处把本体的方法签名**写死**了，游戏更新后签名一变，
        // 生成出来的代码就在编译期炸（用户实测报过 CS1501「FromCard 方法没有采用 2 个参数的重载」，
        // 报错信息完全看不出是「游戏版本和工具假设的不一样」）。这里抽查最关键的一处，明确说出来。
        // ⚠ 只在「类型找得到、但那个重载没有」时报（读不出来 / 找不到类型都不报）。
        try
        {
            string sts2 = Path.Combine(p.Paths.GameDataDir ?? "", "sts2.dll");
            if (File.Exists(sts2))
            {
                string dataDir = (p.Paths.GameDataDir ?? "").TrimEnd('\\', '/');
                string? gameDir = dataDir.Length == 0 ? null : Directory.GetParent(dataDir)?.FullName;
                string? dllVer = Sts2Metadata.DllProductVersion(sts2);
                string? dllCommit = Sts2Metadata.CommitOf(dllVer);
                string? relVer = Sts2Metadata.GameVersion(gameDir);
                string? relCommit = Sts2Metadata.GameCommit(gameDir);
                // 「我读的是哪份 dll」也一起写出来（用户问过「会不会拿的是反编译/解包那份公开版」——
                // 这条报错只看「游戏 data 目录」，把路径 + dll 自己的版本串摆出来就能自查）
                string readInfo = $"读的是 {sts2}，dll 版本 {dllVer ?? "(无版本信息)"}"
                    + (relVer is null ? "" : $"，游戏目录 release_info = {relVer}");

                if (Sts2Metadata.HasMethod(sts2, "AttackCommand", "FromCard", 2) == false)
                {
                    // 把**他这份 dll 里真实的签名**打出来：本体改成了什么一眼就能看见，
                    // 不用再问用户「你的 dll 发我一下」（用户报过：只有这一条错，来回问一轮太慢）。
                    var overloads = Sts2Metadata.MethodOverloads(sts2, "AttackCommand", "FromCard");
                    string found = overloads.Count == 0
                        ? "连 FromCard 这个方法都没找到"
                        : "实际只有：" + string.Join(" / ", overloads);
                    issues.Add(new("错误", "本体没有 `AttackCommand.FromCard(CardModel, CardPlay?)` 这个重载"
                        + $"（{found}；{readInfo}）"
                        + " —— **你这份游戏的版本和本工具假设的不一样**："
                        + "生成出来的代码会在编译期报 `CS1501 FromCard 方法没有采用 2 个参数的重载`。"
                        + "请换用与本工具匹配的游戏版本，或把 `data_sts2_*\\sts2.dll` 发给工具作者适配。"));
                }

                // dll 和游戏目录的 release_info.json 不是同一次构建 → 多半是「游戏 data 目录」指错了：
                // 指到解包/反编译出来的那份、或者某份旧副本（用户问过：会不会拿的是公开版的文件）。
                // dll 的 ProductVersion 是 `0.1.0+<commit>`，release_info 里正好也写着 commit，一对就知道。
                if (!Sts2Metadata.SameCommit(dllCommit, relCommit))
                    issues.Add(new("警告", $"「游戏 data 目录」里这份 sts2.dll 是**另一次构建**（dll 里记的 commit {dllCommit}，"
                        + $"游戏目录 release_info.json 记的是 {relCommit}）—— 多半是这个目录指到了旧副本 / 解包（反编译）出来的那份。"
                        + $"换回游戏自己的 `{(gameDir ?? "<游戏目录>")}\\data_sts2_windows_x86_64` 再试。"));
            }
        }
        catch { /* 只是核对，读不出来就算了 */ }

        // 游戏版本核对（和上面那条签名抽查配套）：release_info.json 里写着本体版本，
        // 和工具支持的版本前两位对不上就直接说清楚 —— 现在只有「FromCard 签名不对」这一处能暴露版本问题，
        // 本体改别的地方时用户只会看到一堆莫名其妙的编译错。
        try
        {
            string dataDir = (p.Paths.GameDataDir ?? "").TrimEnd('\\', '/');
            string? gameDir = dataDir.Length == 0 ? null : Directory.GetParent(dataDir)?.FullName;
            string? gameVer = Sts2Metadata.GameVersion(gameDir);
            if (gameVer is not null && Sts2Metadata.VersionMismatch(gameVer))
                issues.Add(new("警告", $"你这份游戏的版本是 {gameVer}，本工具按 {Sts2Metadata.SupportedGameVersion} 写的"
                    + " —— 生成出来的代码可能出现编译错误（本体方法签名变了）。"
                    + "先在 Steam「属性 → 测试版」里切回匹配的版本，或把 `data_sts2_*\\sts2.dll` 发给工具作者适配。"));
        }
        catch { /* 只是核对 */ }

        // 生成 / 存档目录落在游戏的 mods\ 里面（V0.2.167）：本体是按目录扫 *.json 找模组清单的，
        // 存档（和 _备份 里的副本）会被当成模组去加载 → 日志里一串
        // 「Mod manifest for mod X declares that it should load a dll/pck, but no … was found!」+
        // 「Neither a DLL nor a PCK was loaded for mod X」（用户实测报过，还套了三层 _备份）。
        try
        {
            string modsDir = PathAutoDetect.FindModsDir(p.Paths.InstallDir) ?? "";
            string outDir = string.IsNullOrWhiteSpace(p.Paths.OutputDir) ? "" : Path.GetFullPath(p.Paths.OutputDir);
            if (modsDir.Length > 0 && outDir.Length > 0
                && outDir.StartsWith(Path.GetFullPath(modsDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("警告", $"生成 / 存档目录在游戏的 mods 目录**里面**（{p.Paths.OutputDir}）—— "
                    + "本体是按目录扫 *.json 当模组清单的，会把存档（以及 `_备份` 里的副本）也当成模组去加载，"
                    + "游戏日志里会出现「声明要加载 dll / pck，但那个路径下没有」那几条错误。"
                    + "建议把「输出目录」挪到 mods 外面（例如 我的文档\\Sts2CharForge）。"));
        }
        catch { /* 路径怪就算了：这条只是提醒 */ }

        // 多个模组装在一起会不会撞车（本体的模型 ID 只按类名算 —— 撞了游戏直接起不来）。
        // 这一步要读磁盘上别的存档工程，任何意外（路径怪、文件被占、读取失败）都绝不能影响正常校验。
        try { CheckMultiModConflicts(issues, p, n); } catch { /* 读不到就不报，宁可漏报 */ }

        // 逐帧动画（动图拆帧）：勾了总开关却没待机 / 素材不是 GIF / 数值越界
        ValidateAnimation(issues, p);

        // ===== V0.2.278（用户要求「效果加个即死」）=====
        // 「即死」目前在卡牌 / 遗物 / 药水三条链上都生成了；锻造页的【效果】模块走的是另一套字段，
        // 还没接这个种类 —— 与其静默不生效，不如在这里明确拦住（用户最烦静默失效）。
        foreach (var g in ForgeEffectBridge.GraphsOf(p))
            foreach (var nd in g.Nodes)
                if (ForgeEffectBridge.IsEffectNode(nd)
                    && string.Equals((nd.Get("Effect") ?? "").Trim(), "InstantKill", StringComparison.Ordinal))
                    issues.Add(new("警告", $"锻造页的【效果】模块里用了「即死」—— 这条链暂时还没有它，"
                        + "生成出来不会执行。想用即死请把它配在卡牌 / 遗物 / 药水的效果栏里。"));
        // ===== V0.2.276（用户要求「自定义描述里能填能量图标」）=====
        // 「填入参数…」里现在有 `{Energy:energyIcons()}`。它依赖本体卡面描述变量里那个 Energy ——
        // 卡牌一定有；遗物 / 药水 / 状态的描述变量集不一样，取不到值时会**原样印出** `{Energy:energyIcons()}`。
        // 所以这里只对非卡牌的自定义描述给一句提示（卡牌不打扰）。
        {
            foreach (var r in p.Relics)
            {
                if (r is null) continue;
                string d = r.CustomDescription ?? "";
                if (d.Contains("{Energy", StringComparison.Ordinal))
                    issues.Add(new("提示", $"遗物「{r.Name}」的自定义描述里用了 `{{Energy:energyIcons()}}`（能量图标）——"
                        + "遗物描述的变量集不一定有 Energy，取不到时卡面上会原样印出那段花括号。"
                        + "如果悬停时看到花括号，就把它改成普通文字。"));
            }
        }
        // ===== V0.2.275（用户报「概率填了百分比却没生效 / 又全被自动勾成 50%」）=====
        // 概率必须**显式勾选**才生效（V0.2.272 那版「自动开启」会把每一条都变成 50% 掷骰，已撤回）。
        // 填了 1~99 的百分比或写了概率公式却没勾 → 提示一句，免得又是静默不生效。
        foreach (var fx in CSharpCodeGen.AllEffects(p))
        {
            if (fx is null || fx.ChanceEnabled) continue;
            bool hasFormula2 = !string.IsNullOrWhiteSpace(fx.ChanceFormula);
            bool hasPercent2 = fx.ChancePercent is > 0m and < 100m;
            if (!hasFormula2 && !hasPercent2) continue;
            string what2 = hasFormula2
                ? $"概率公式「{fx.ChanceFormula}」"
                : $"{fx.ChancePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%";
            issues.Add(new("提示", $"有一条「{EffectCatalog.FindKind(fx.Kind)?.Display ?? fx.Kind}」写了{what2}，"
                + "但**没勾**「概率生效」—— 这一条不会掷骰子（等于每次都生效）。想真的按概率判定就把那个勾打上。"));
        }

        // ===== V0.2.265（用户要求）：音频进组 + 「仅能被下个同组音频顶替」 =====
        // ① 勾了「仅能被下个同组音频顶替」但没勾「被下一个自定义音频顶替」→ 它永远不会被顶替，说明白；
        // ② 勾了「仅能被下个同组音频顶替」但这个音频**没加入任何组** → 也没有同组音频能顶替它；
        // ③ 组里记的音频 Key 在「美术资源」页找不到 → 提一句（改名 / 删掉了）。
        {
            var audioKeys = p.AudioAssets.Where(a => a is not null)
                .Select(a => (a.Key ?? "").Trim()).Where(k => k.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groupedAudio = p.CardGroups.Where(g => g is not null)
                .SelectMany(g => g.AudioIdList).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var e in CSharpCodeGen.AllEffects(p))
            {
                if (e is not { Kind: "PlaySoundFile" }) continue;
                string key = (e.Sfx ?? "").Trim();
                if (!e.SfxSameGroupOnly) continue;
                if (!e.SfxReplace)
                    issues.Add(new("提示", $"有一条「播放音频文件（{key}）」勾了「仅能被下个同组音频顶替」，"
                        + "但**没勾**「被下一个自定义音频顶替」—— 不勾那一个的话它根本不允许被顶替，"
                        + "所以这个「同组」限制不会起作用。"));
                else if (key.Length > 0 && !groupedAudio.Contains(key))
                    issues.Add(new("警告", $"「播放音频文件（{key}）」勾了「仅能被下个同组音频顶替」，"
                        + "但这个音频**没有加入任何组** → 永远不会有同组音频来顶替它（等于不顶替）。"
                        + "到「组」页那个「这个组里有哪些音频」里勾上它。"));
            }
            foreach (var g in p.CardGroups)
            {
                if (g is null) continue;
                foreach (string k in g.AudioIdList)
                    if (!audioKeys.Contains(k))
                        issues.Add(new("警告", $"组「{g.Name}」里的音频 `{k}` 在「美术资源」页找不到"
                            + "（可能改名 / 删掉了）—— 这一条不会参与「同组顶替」判断。"));
            }
        }

        // ===== V0.2.260（用户要求）：先古道具专属勾选（古老牙齿 / 尘封魔典 / 欧洛巴斯之触）=====
        // ① 同一种勾选最多一个（本体那三件道具每次只给一个东西）；
        // ② 勾了古老牙齿但没有「初始卡组里的牌」→ 生成端写不出那个补丁，得说出来；
        // ③ 勾了欧洛巴斯的遗物稀有度不是「先古 / 初始」→ 提醒一句（效果照旧生效）。
        var toothFlags = p.AllCards.Where(c => c.ToothReplace).ToList();
        if (toothFlags.Count > 1)
            issues.Add(new("错误", "有 " + toothFlags.Count + " 张先古卡都勾了「替换古老牙齿所替换的初始卡牌」（"
                + string.Join("、", toothFlags.Select(c => "「" + c.Name + "」")) + "）—— 古老牙齿一次只能给一张，请只留一张。"));
        else if (toothFlags.Count == 1)
        {
            var onlyTooth = toothFlags[0];
            if (!onlyTooth.IsAncientCard)
                issues.Add(new("警告", $"「{onlyTooth.Name}」勾了「替换古老牙齿所替换的初始卡牌」，但它**不是先古卡**"
                    + "（稀有度不是「先古」）—— 生成端只对先古卡写这个补丁，这一勾不会生效。"));
            else if (AncientItemPatchGen.StarterCardFor(p) is null)
                issues.Add(new("警告", $"「{onlyTooth.Name}」勾了「替换古老牙齿所替换的初始卡牌」，但你的**初始卡组里一张牌都没有**"
                    + "（卡牌页没勾「初始牌」或张数为 0）—— 古老牙齿没有可替换的卡，这个补丁不会生成。"));
            // V0.2.261：指定了「被替换的初始卡」但那张牌已经不存在（改过类名 / 删了）→ 说清楚已退回自动
            string wantStarter = (onlyTooth.ToothStarterClass ?? "").Trim();
            if (wantStarter.Length > 0
                && !p.AllCards.Any(c => string.Equals(n.CardClassName(p, c), wantStarter, StringComparison.Ordinal)))
                issues.Add(new("警告", $"「{onlyTooth.Name}」指定的「被古老牙齿换掉的初始卡」是 `{wantStarter}`，"
                    + "但这张牌在当前存档里找不到（改过类名、或者那张牌被删了）—— 已自动退回「初始卡组里第一张」，"
                    + "请到先古卡页那个下拉里重新选一次。"));
        }
        var tomeFlags = p.AllCards.Where(c => c.DustyTomeGrant).ToList();
        if (tomeFlags.Count > 1)
            issues.Add(new("错误", "有 " + tomeFlags.Count + " 张先古卡都勾了「作为尘封魔典给的先古卡」（"
                + string.Join("、", tomeFlags.Select(c => "「" + c.Name + "」")) + "）—— 尘封魔典一次只给一张，请只留一张。"));
        var orobasFlags = p.Relics.Where(r => r.OrobasReplace).ToList();
        if (orobasFlags.Count > 1)
            issues.Add(new("错误", "有 " + orobasFlags.Count + " 只遗物都勾了「替换欧洛巴斯之触所替换的初始遗物」（"
                + string.Join("、", orobasFlags.Select(r => "「" + r.Name + "」")) + "）—— 欧洛巴斯之触一次只给一只，请只留一只。"));
        else if (orobasFlags.Count == 1 && orobasFlags[0].Rarity is not ("Ancient" or "Starter"))
            issues.Add(new("提示", $"「{orobasFlags[0].Name}」勾了「替换欧洛巴斯之触所替换的初始遗物」，"
                + "但它稀有度不是「先古 / 初始」—— 效果照旧生效（欧洛巴斯之触会把它换上），只是看起来容易懵。"));

        return issues;
    }

    /// <summary>
    /// 逐帧动画（动图拆帧）的校验。见 <c>docs\逐帧动画_吸收设计.md</c>。
    ///
    /// 这里只拦「生成之前就能一眼看出来的错」：整张动图**不在这里解**——校验器每改一次界面就跑一次
    /// （<c>RefreshIssues</c>），解一张几十帧的动图要几十毫秒，界面会卡；真解不开在生成时还会再报一次
    /// （<see cref="AnimFramesGen.BuildPlan"/> 会写 <c>[错误] 待机动图解不开</c> 并让整份计划作废）。
    /// </summary>
    private static void ValidateAnimation(List<ValidationIssue> issues, CharacterProfile p)
    {
        var a = p.Animation;

        // 槽位顺序 = AnimationSpec.Slots 的顺序（界面、存档、生成三处一致）
        var slots = new (string Label, string Path)[]
        {
            ("待机（循环）", a.Idle),
            ("攻击", a.Attack),
            ("施法", a.Cast),
            ("受伤", a.Hurt),
            ("死亡", a.Die),
            ("篝火", a.RestSite),
            ("商店", a.Merchant),
        };
        int filled = slots.Count(s => !string.IsNullOrWhiteSpace(s.Path));

        if (a.Enabled && string.IsNullOrWhiteSpace(a.Idle))
            issues.Add(new("错误", "勾了「启用逐帧动画」但没传「待机（循环）」动图：待机是回退目标，"
                + "其它动作没素材时都会退回它 —— 没有它整条动画都不生效。到「动画」页选一张待机 GIF，或把总开关关掉。"));

        if (a.Enabled && filled == 0)
            issues.Add(new("提示", "勾了「启用逐帧动画」但一个动图都没传：这次生成不会写任何帧图 / 运行动画代码（模组照常能编译）。"));

        foreach (var (label, path) in slots)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!File.Exists(path))
                issues.Add(new("错误", $"逐帧动画：{label}的动图文件不存在：{path}"
                    + "（到「动画」页重新选一个，或把这个槽位清除）"));
            else if (!LooksLikeGif(path))
                issues.Add(new("错误", $"逐帧动画：{label}的素材不是 GIF：{path}"
                    + "（逐帧动画只吃动图；静态 PNG 请放「美术资源」页）"));
        }

        if (!a.Enabled && filled > 0)
            issues.Add(new("提示", $"「动画」页里放了 {filled} 个动图，但「启用逐帧动画」没勾：这些素材这次不会生效"
                + "（生成结果和没放素材一样）。"));

        // 裁剪框格式不对给**警告**不给错误：生成侧会优雅降级（写一条 [警告] 然后改用自动裁剪），
        // 用户手打错一个字符不该拦住整次生成 —— 两边口径必须一致。
        if (!string.IsNullOrWhiteSpace(a.ManualCrop) && !IsManualCrop(a.ManualCrop))
            issues.Add(new("警告", $"逐帧动画：「手动裁剪框」格式不对，要 x0,y0,x1,y1 四个整数（x1/y1 是开区间，"
                + $"例如 0,0,20,24）：{a.ManualCrop} —— 生成时会忽略它、改用自动裁剪（所有动作所有帧的并集）。"));

        // 下面这几个直接进游戏里的缩放 / 播放速度 / 抠底参数，越界会算出看不见人的精灵或抠出窟窿
        if (a.Scale < 0 || a.Scale > 50)
            issues.Add(new("错误", $"逐帧动画：「缩放」只能是 0（按静态立绘高度自动算）或 0.05 ~ 50 之间的数，现在是 {Num(a.Scale)}。"));
        if (a.SpeedScale <= 0 || a.SpeedScale > 20)
            issues.Add(new("错误", $"逐帧动画：「播放速度系数」只能是大于 0 的数（1 = 动图原速，2 = 双倍速，上限 20），"
                + $"现在是 {Num(a.SpeedScale)}。0 不是原速，原速是 1。"));
        if (a.KeyTolerance < 0 || a.KeyTolerance > 255)
            issues.Add(new("错误", $"逐帧动画：「抠背景容差」是 RGB 每个通道允许的最大差，只能是 0 ~ 255，现在是 {Num(a.KeyTolerance)}。"));
        if (a.OutlineLuminance < 0 || a.OutlineLuminance > 255)
            issues.Add(new("错误", $"逐帧动画：「剥描边的亮度阈值」是 0 ~ 255 的亮度，现在是 {Num(a.OutlineLuminance)}。"));

        static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 是不是 GIF：只看文件头 6 字节（<c>GIF87a</c> / <c>GIF89a</c>）。
    /// 为什么不整个解一遍：见 <see cref="ValidateAnimation"/> 的说明 —— 校验器跑得很勤，读整个文件会卡界面。
    /// </summary>
    private static bool LooksLikeGif(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[6];
            int n = 0;
            while (n < head.Length)
            {
                int read = fs.Read(head, n, head.Length - n);
                if (read <= 0) break;
                n += read;
            }
            if (n < head.Length) return false;
            return head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F'
                && head[3] == (byte)'8' && (head[4] == (byte)'7' || head[4] == (byte)'9') && head[5] == (byte)'a';
        }
        catch
        {
            return false;   // 读不动（被占用 / 权限）按「不是 GIF」报，反正生成时也解不开
        }
    }

    /// <summary>
    /// 手动裁剪框是不是 <c>x0,y0,x1,y1</c>。判定写法**必须和 <see cref="AnimFramesGen.BuildPlan"/> 里那一段一模一样**，
    /// 否则会出现「校验器说没问题、生成时却按自动算」这种自相矛盾的行为（两边都允许前后空格、正负号）。
    /// </summary>
    private static bool IsManualCrop(string text)
    {
        string[] parts = text.Split(new[] { ',' }, 4, StringSplitOptions.None);
        return parts.Length == 4
            && parts.All(s => int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _));
    }

    /// <summary>
    /// 「mods 里放了多个存档生成的模组」时的撞车检查。
    ///
    /// 为什么必须有这一步：本体的 <c>ModelDb</c> 只用**类名**算模型 ID（**忽略命名空间**），
    /// 同一个类名注册两次就抛 <c>DuplicateModelException: conflict in mod content names</c>，
    /// 表现是**游戏直接起不来**（用户实测报过「mods 里有不同存档构建的角色模组时游戏打不开」）。
    ///
    /// 这里扫「同一个存档目录下别的存档生成的工程」，把和本配置**重名的模型类**找出来 ——
    /// 生成器自己起的固定名字（打击 / 防御、额外资源量遗物、Forge* 那几个 Power）已经带上角色类名前缀，
    /// 所以剩下的撞名基本都是「两个存档用了同一个角色类名」或「手填了同一个英文类名」。
    /// 只报**警告**（不拦生成）：用户可能只想装其中一个，或者正要删掉另一个。
    /// </summary>
    private static void CheckMultiModConflicts(List<ValidationIssue> issues, CharacterProfile p, Naming n)
    {
        string outDir = p.Paths.OutputDir;
        if (string.IsNullOrWhiteSpace(outDir) || !Directory.Exists(outDir)) return;

        // 本配置会注册的全部模型类名
        var mine = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in p.AllCards)
            if (c is not null && !c.IsVanillaCard) mine.Add(n.CardClassName(p, c));
        for (int i = 0; i < p.Relics.Count; i++)
            if (p.Relics[i] is not null) mine.Add(n.RelicClassName(p.Relics[i], i));
        for (int i = 0; i < p.Potions.Count; i++)
            if (p.Potions[i] is not null) mine.Add(n.PotionClassName(p.Potions[i], i));
        foreach (var cp in CustomPowerGen.Active(p))
            mine.Add(CustomPowerGen.ClassNameOf(p, cp, p.CustomPowers.IndexOf(cp)));
        mine.Add(n.CharClass);
        mine.Add(n.CardPoolClass);
        mine.Add(n.RelicPoolClass);
        mine.Add(n.PotionPoolClass);
        if (p.ExtraResource.Enabled) mine.Add(n.ExtraResourceRelicClass);
        if (CSharpCodeGen.UsesExtraTurn(p)) mine.Add(n.ExtraTurnPowerClass);
        if (CSharpCodeGen.UsesEnergyDebt(p)) mine.Add(n.EnergyDebtPowerClass);
        if (CSharpCodeGen.UsesEnergyNextTurnDebt(p)) mine.Add(n.EnergyNextTurnDebtPowerClass);
        if (CSharpCodeGen.UsesTempUpgrade(p)) mine.Add(n.TempUpgradePowerClass);
        if (CSharpCodeGen.UsesTempKeywordPower(p)) mine.Add(n.TempKeywordPowerClass);
        if (CSharpCodeGen.UsesTurnEndPlay(p)) mine.Add(n.TurnEndPlayPowerClass);
        foreach (var e in CSharpCodeGen.CollectDelayedEffects(p)) mine.Add(n.DelayedPowerClass(e));
        foreach (var e in CSharpCodeGen.CollectTempPowerEffects(p)) mine.Add(n.TempPowerClass(e));
        if (PetGen.IsActive(p))
        {
            foreach (var d in PetGen.All(p)) mine.Add(d.ClassName);
            if (PetGen.Enabled(p).Any(s => s.TakesDamageForOwner)) mine.Add(n.GuardianPowerClass);
        }

        string myRoot;
        try { myRoot = Path.GetFullPath(Path.Combine(outDir, n.ModId)); }
        catch { return; }

        foreach (string dir in Directory.GetDirectories(outDir))
        {
            string cs = Path.Combine(dir, "cs");
            if (!Directory.Exists(cs)) continue;
            string otherRoot;
            try { otherRoot = Path.GetFullPath(dir); }
            catch { continue; }
            if (string.Equals(otherRoot, myRoot, StringComparison.OrdinalIgnoreCase)) continue;  // 自己

            var hits = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string f in Directory.EnumerateFiles(cs, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                             text, @"(?m)^public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*:\s*[\w<>\.]*(?:Model|Power)\b"))
                {
                    string cls = m.Groups[1].Value;
                    if (mine.Contains(cls)) hits.Add(cls);
                }
            }
            if (hits.Count == 0) continue;

            string other = Path.GetFileName(dir);
            issues.Add(new("警告", $"「{other}」这个存档生成的模组和本配置**撞了 {hits.Count} 个类名**"
                + $"（{string.Join("、", hits.Take(6))}{(hits.Count > 6 ? " …" : "")}）—— "
                + "本体的模型 ID 只用类名算（忽略命名空间），两个模组同时装进 mods 会抛 "
                + "DuplicateModelException，**游戏直接起不来**。"
                + "要同时装两个模组：把其中一个存档的「角色类名」改掉（例：S1Mod 的角色类名从 Seven 改成 S1）、"
                + "别手填和别人一样的英文类名，然后重新生成两边；只想装一个的话忽略这条即可。"));
        }
    }

    /// <summary>
    /// 本体关键词改名的校验：id 合法、名字里没有富文本标记、不和其他本体关键词撞名。
    ///
    /// 为什么这几条必须拦住（不是洁癖）：
    ///   · 撞名：两个关键词显示成同一个词（本体「消耗」被改成「虚无」，而「虚无」本来就是另一个关键词），
    ///     玩家在卡面上根本分不出这两条 —— 生成出来的卡面自相矛盾，我们不如在生成前直接报错。
    ///   · 富文本标记：本体卡面文字是 <c>CardKeywordExtensions.GetCardText()</c> 拼的
    ///     <c>[gold]&lt;title&gt;[/gold]</c>，名字里再带一个 <c>[</c> 或 <c>]</c> 会把标签解析坏
    ///     （整张卡面文字错位 / 显示成原始标记）。
    ///   · id：只剩 7 个正式关键词（NONE / PERIOD 不是关键词，是占位键，改了没有意义）。
    ///
    /// 撞车判定用的是「最终显示名」：没改的关键词按本体原文算，改了按新名字算。
    /// </summary>
    /// <summary>
    /// 「自定义参数 / 组」页的校验：参数名必须是没被占用、来源要配全，卡牌不能引用不存在的组。
    /// 这些是「生成出来的模组里公式算不出来 / 组筛选筛不到」的根因，所以提前拦住。
    /// </summary>
    private static void ValidateParamsAndGroups(List<ValidationIssue> issues, CharacterProfile p)
    {
        // ===== 自定义参数 =====
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < p.CustomParams.Count; i++)
        {
            var param = p.CustomParams[i];
            if (param is null) continue;
            string name = ParamNames.Normalize(param.Name);
            string who = $"自定义参数 {name}=";

            if (ParamNames.Reserved.Contains(name, StringComparer.Ordinal))
                issues.Add(new("错误", $"{who} 用的是保留字母：X = 当前所有能量，Y = 当前所有额外资源量。"
                    + "公式里 X / Y 已经有固定含义，换个字母。"));
            else if (seen.TryGetValue(name, out string? first))
                issues.Add(new("错误", $"{who} 和「{first}」重名：同一个字母只能代表一个值（公式里会算出哪个数全看运气）。"));
            else
                seen[name] = string.IsNullOrWhiteSpace(param.Note) ? who : $"{who}（{param.Note.Trim()}）";

            if (param.IsPower && string.IsNullOrWhiteSpace(param.PowerId))
                issues.Add(new("警告", $"{who} 选了「增益/减益」但还没选具体是哪个状态：这个参数现在算不出值。"));
            if (!ParamNames.IsUsable(name))
                issues.Add(new("警告", $"{who} 的字母不合法（只能用 A~Z，且不能是 X / Y）。"));
        }
        // 只警告一次：有几个参数被指定成「给予量」就说明这一页在用了
        foreach (var param in p.CustomParams)
        {
            if (param is null) continue;
            string grant = ParamGrants.Normalize(param.EnergyGrant);
            if (grant.Length > 0 && !ParamNames.Reserved.Contains(grant, StringComparer.Ordinal)
                && !p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), grant, StringComparison.Ordinal)))
                issues.Add(new("警告", $"自定义参数 {ParamNames.Normalize(param.Name)}= 指定「给予能量 = {grant}」，"
                    + $"但没有一个参数叫 {grant}=（X / Y 是保留的「当前所有能量 / 当前所有额外资源量」，可以直接用）。"));
            string res = ParamGrants.Normalize(param.ResourceGrant);
            if (res.Length > 0 && !ParamNames.Reserved.Contains(res, StringComparer.Ordinal)
                && !p.CustomParams.Any(x => x is not null && string.Equals(ParamNames.Normalize(x.Name), res, StringComparison.Ordinal)))
                issues.Add(new("警告", $"自定义参数 {ParamNames.Normalize(param.Name)}= 指定「给予额外资源量 = {res}」，"
                    + $"但没有一个参数叫 {res}=。"));
        }

        // ===== 组 =====
        var groupIds = new HashSet<string>(p.CardGroups.Where(g => g is not null).Select(g => g.Id), StringComparer.Ordinal);
        var groupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < p.CardGroups.Count; i++)
        {
            var g = p.CardGroups[i];
            if (g is null) continue;
            string name = (g.Name ?? "").Trim();
            if (groupNames.TryGetValue(name, out string? firstGroup))
                issues.Add(new("警告", $"组「{name}」和「{firstGroup}」重名了：名字只影响界面上的查看，"
                    + "但两个同名组在「范围」下拉里会分不清谁是谁，建议改一个。"));
            else
                groupNames[name] = name;
        }
        foreach (var card in p.AllCards)
        {
            if (card is null) continue;
            foreach (string id in card.GroupIdList)
                if (!groupIds.Contains(id))
                    issues.Add(new("警告", $"卡牌「{card.Name}」还属于一个已经被删掉的组（内部标识 {id}）："
                        + "重新勾一次组就能清掉，或者到「自定义参数 / 组」页把它加回来。"));
            // 自定义状态**施加到敌人身上**时：本体那些钩子里的 target / player 都是 base.Owner，
            // 而「玩家回合开始时」「自己受到伤害后」这类钩子是**挂到谁身上就盯谁**的 ——
            // 挂到敌人身上时它永远不会触发（实测踩过：「染色：红 / 黄 / 蓝」贴给了敌人 → 一条都不生效）。
            foreach (var e in card.Effects)
            {
                if (e is null || e.Kind != "ApplyPower") continue;
                string pid = (e.PowerId ?? "").Trim();
                if (pid.Length == 0 || !EffectCatalog.IsCustomPower(pid)) continue;
                var cp = p.CustomPowers.FirstOrDefault(x => x is not null
                    && string.Equals(x.ClassName, pid, StringComparison.Ordinal));
                if (cp is null) continue;
                bool selfSide = string.Equals(e.TargetSide, "Self", StringComparison.Ordinal);
                var playerScoped = cp.Triggers.Where(t => t is not null).Select(t => t.Kind)
                    .Where(k => SelfScopedPowerTriggers.Contains(k, StringComparer.Ordinal)).ToList();
                if (!selfSide && playerScoped.Count > 0)
                    issues.Add(new("警告", $"卡牌「{card.Name}」把自定义状态「{cp.Name}」施加到**敌人**身上了，"
                        + $"而这个状态的触发时机（{string.Join(" / ", playerScoped.Select(SelfScopedPowerTriggerZh))}）"
                        + "盯的都是**挂着它的那个单位**（本体钩子里的 target / player 就是 base.Owner）——"
                        + "贴在敌人身上这些时机永远不会触发，这张牌等于没做事。"
                        + "「为自己附魔」这类状态请把效果的「作用对象」改成「自己」，让状态挂在自己身上。"));
            }
        }

        // ===== 锻造页做出来的条目 =====
        // 现阶段节点图还没接上：锻造卡的数值 / 效果还没有地方填，生成出来的会是空牌。
        // 不拦住（用户可能只想先把分类定下来），但要说清楚。
        int forgedCards = p.Cards.Count(c => c is not null && c.IsForged);
        int forgedPotions = p.Potions.Count(x => x is not null && x.IsForged);
        int forgedRelics = p.Relics.Count(x => x is not null && x.IsForged);
        if (forgedCards + forgedPotions + forgedRelics > 0)
            issues.Add(new("警告", $"「锻造」页里有 {forgedCards} 张卡、{forgedPotions} 瓶药水、{forgedRelics} 只遗物"
                + "还没接上节点图（那张布现在是空的）：它们生成出来会是「没有任何效果」的空条目。"
                + "先把名称 / 类型 / 稀有度填好，节点编辑器接上之后再补。"));
    }

    /// <summary>
    /// 这些触发时机在生成的代码里盯的是 <c>base.Owner</c>（= **挂着这个状态的那个单位**）：
    /// 「玩家回合开始时」判 <c>player != base.Owner.Player</c>、「自己受到伤害后」判 <c>target != base.Owner</c>…
    /// 所以状态一旦贴到敌人身上，这些时机永远不会触发（实测踩过）。
    /// </summary>
    private static readonly string[] SelfScopedPowerTriggers =
    {
        "TurnStart", "TurnEnd", "DamageTaken", "Attacked", "HpLost", "CardPlayed",
        "EnergySpent", "BlockGained", "PowerChanged", "EnemyDamaged", "EnemyKilled", "CombatStart",
    };

    /// <summary>
    /// 自定义状态链上**会去拿「玩家对象」**（<c>base.Owner.Player</c>）的效果种类（V0.2.147）。
    ///
    /// <para>为什么这张表存在：状态效果里的「自己」是**挂着它的那个单位**（<c>base.Owner</c>），
    /// 而下面这些效果要的是**玩家**（抽牌 / 加能量 / 生成卡牌 / 药水 / 改牌组…）——
    /// 状态一旦贴在敌人身上，<c>Creature.Player</c> 就是 <c>null</c>，生成代码把 null 传进本体接口
    /// 会在运行时抛异常 → 战斗卡住。校验器用这张表提前警告（见那个「用了玩家侧的东西」警告）。</para>
    ///
    /// <para><b>这份清单是机械扫出来的</b>：把每种效果都按「最想弹选牌界面」的参数配一个自定义状态、
    /// 生成源码，扫有没有「没有空值保护」的 <c>base.Owner.Player</c>
    /// （<c>!= base.Owner.Player</c> 那种比较、<c>?.</c> / <c>??</c> 都不算）。
    /// 另外「勾了概率生效」的任何效果也会读 <c>base.Owner.Player.RunState</c>（掷骰），
    /// 所以那一档单独判（见警告那段）。自检里有断言钉着这张表与生成端一致。</para>
    /// </summary>
    public static readonly string[] PlayerSidePowerEffectKinds =
    {
        "Draw", "Energy", "Gold", "ExtraResource", "GenerateCard", "GainPotion",
        "AddCardGlobal", "TransformCardGlobal", "RemoveCardGlobal", "UpgradeCardGlobal",
        "UpgradeCard", "ExhaustCard", "DiscardCard", "GiveKeyword",
        "TakeFromDraw", "TakeFromDiscard", "TakeFromExhaust", "CardReward",
    };

    private static string SelfScopedPowerTriggerZh(string kind)
        => PowerTriggers.Find(kind)?.Display ?? kind;

    private static void ValidateVanillaKeywordRenames(List<ValidationIssue> issues, CharacterProfile p)    {
        if (p.KeywordRenames is null || p.KeywordRenames.Count == 0) return;

        for (int i = 0; i < p.KeywordRenames.Count; i++)
        {
            var r = p.KeywordRenames[i];
            if (r is null) continue;
            string id = (r.KeywordId ?? "").Trim();
            if (id.Length == 0 && !r.ChangesAnything) continue;      // 空行（没有这一条）不算错

            string who = $"本体关键词改名「{id}」";
            var entry = VanillaKeywordCatalog.ById(id);
            if (entry is null)
            {
                issues.Add(new("错误", $"本体关键词改名的 #{i + 1} 条的枚举名「{id}」不是本体关键词"
                    + "（只能是 EXHAUST / ETHEREAL / INNATE / UNPLAYABLE / RETAIN / SLY / ETERNAL 这 7 个）。"));
                continue;
            }
            who = $"本体关键词「{entry.VanillaName}」的改名";

            string name = (r.Name ?? "").Trim();
            if (name.Length > 0 && (name.Contains('[') || name.Contains(']')))
                issues.Add(new("错误", $"{who} 的新名字里有 [ 或 ]：本体卡面是按 [gold]名字[/gold] 拼的，"
                    + "名字里再带方括号会把卡面文字解析坏。请去掉方括号。"));

            string desc = (r.Description ?? "").Trim();
            if (desc.Length > 0 && desc.Contains('[') && !desc.Contains("[/"))
                issues.Add(new("警告", $"{who} 的新说明里有个 [ 但没有配对的 [/…]："
                    + "说明支持 [gold]…[/gold] 这类富文本，写错了会原样显示出来。"));

            // 同一行重复出现（手写 JSON / 老存档）：以最后一条为准，这里只提示
            if (p.KeywordRenames.Take(i).Any(x => x is not null
                    && string.Equals((x.KeywordId ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase)
                    && x.ChangesAnything))
                issues.Add(new("警告", $"{who} 出现了多行，生成时以最后一行填的内容为准。"));

            // 改成和原名一模一样 = 白写一条（不报错，只提醒）
            if (name.Length > 0 && string.Equals(name, VanillaKeywordGen.VanillaTitleOf(entry.Id), StringComparison.Ordinal))
                issues.Add(new("警告", $"{who} 的新名字和本体原名一样，等于没改。"));
        }

        // 撞车判定必须**独立扫一遍全部 7 个关键词**（不是在上面那个循环里顺手做）：
        // 上面循环对「什么都没填」的关键词会跳过 —— 而撞车恰恰常常出在
        // 「有人把 A 改成了 B 的本体名（B 本身一个字没改）」这种组合上，
        // 跳过 B 就永远查不出来（自检里就是这么发现的）。
        var finalNames = new Dictionary<string, string>(StringComparer.Ordinal);   // 生效名 → 先出现的枚举名
        foreach (var entry in VanillaKeywordCatalog.All)
        {
            string finalName = VanillaKeywordGen.EffectiveNameOf(p, entry.Id);
            if (finalName.Length == 0) continue;
            if (finalNames.TryGetValue(finalName, out string? firstId))
            {
                issues.Add(new("错误", $"本体关键词改名的名字撞车了：「{firstId}」和「{entry.Id}」"
                    + $"在游戏里都会显示成「{finalName}」，玩家分不清这两条。给它们其中一个换一个名字。"));
                continue;
            }
            finalNames[finalName] = entry.Id;
        }
    }

    /// <summary>
    /// 召唤物的校验：启用了就把每只的类名 / 名字 / 血量 / 站位 / 图片查一遍；
    /// 没启用而卡牌 / 遗物 / 药水却用了「召唤伙伴 / 伙伴攻击」→ 报错拦住（否则生成出来的牌会引用不存在的宠物类）。
    /// 另外拦住两件**只有校验器能拦**的事：
    ///   · 列表里两只召唤物用了同一个类名（本体模型 ID 只按类名算 → DuplicateModelException）；
    ///   · 有两只同时勾了「替主人承伤」（本体 Hook.ModifyUnblockedDamageTarget 是链式遍历，两个重定向者
    ///     会让伤害最终归谁完全不可预期）；
    ///   · 效果上选的召唤物不存在 / 没启用（生成出来的代码会引用一个不存在的类 → CS0103）。
    ///
    /// 这一档**不需要**任何 Harmony 补丁：走的是本体的通用宠物 API（PlayerCmd.AddPet&lt;T&gt;），
    /// 所以这里校验的都是「游戏里能不能正常显示 / 能不能编过」的东西，不是补丁条件。
    /// </summary>
    /// <summary>
    /// 这张牌游戏里到底能不能升级（V0.2.136）。
    ///
    /// <para>判据和生成端 <c>CSharpCodeGen.CardSourceCore</c> 里写 <c>MaxUpgradeLevel</c> 的那两处**一一对应**：
    /// 诅咒固定 0；状态牌固定 0（除非勾了「可被无限升级」—— 生成端那条 <c>int.MaxValue</c> 优先）。</para>
    /// </summary>
    private static bool CardIsUpgradable(CardSpec c)
    {
        if (c is null) return false;
        if (c.IsCurseCard) return false;
        if (c.IsStatusCard && !c.InfinitelyUpgradable) return false;
        return true;
    }

    /// <summary>
    /// 「在游戏中隐藏那只额外资源量载体遗物」（<c>ExtraResource.HideInGame</c>）的校验。
    ///
    /// <para>
    /// 这一条**不是**可选的润色 —— V0.2.113~V0.2.137 的隐藏是给 <c>NRelicInventory.Add</c> 打的
    /// **前缀补丁**（遇到载体遗物就 <c>return false</c> 跳过原方法），于是遗物栏的节点列表
    /// <c>_relicNodes</c> 比模型 <c>Player.Relics</c> 少一项；之后再获得任何遗物时，
    /// 本体 <c>OnRelicObtained</c> 按模型算出来的 <c>index</c> 插进这个短列表就**越界**
    /// （<c>List.Insert</c> 抛 ArgumentOutOfRangeException，异步任务死掉 → 玩家眼里就是卡死）。
    /// </para>
    ///
    /// <para>
    /// V0.2.138 起生成端改成**后缀补丁 + 纯视觉隐藏**（原方法照跑、跑完只设
    /// <c>Visible = false</c>），列表长度与顺序永远和模型一致。这里只拦一件事：
    /// 隐藏的**前提** —— 必须启用额外资源量，否则那个类根本不存在、补丁编译不过。
    /// （隐藏之后「遗物还在角色身上、只是节点不可见」这句说明写在界面文案里，不在这里刷提示。）
    /// </para>
    /// </summary>
    private static void ValidateHideCarrierRelic(List<ValidationIssue> issues, CharacterProfile p, Naming n)
    {
        if (!p.ExtraResource.HideInGame) return;

        // 前提：隐藏补丁里引用的 <角色>ExtraResourceRelic 只有「启用额外资源量」才生成。
        // 不禁用这个勾，而是报错说清怎么修（不拦的话生成出来的补丁会引用不存在的类 → CS0246）。
        if (!p.ExtraResource.Enabled)
            issues.Add(new("错误", "勾了「在游戏中隐藏（不显示在遗物栏里）」，但「额外资源量 / 状态」页没勾"
                + "「启用额外资源量」—— 那只载体遗物（" + n.ExtraResourceRelicClass + "）根本不会生成，"
                + "隐藏它的那个补丁就编译不过（CS0246：找不到类型）。要么勾上「启用额外资源量」，"
                + "要么把这个勾去掉。"));
    }

    private static void ValidateSummon(List<ValidationIssue> issues, CharacterProfile p)
    {
        var enabled = PetGen.Enabled(p);
        bool hasSummonEffect = false;

        // 效果上的召唤物引用：必须能在「已启用的召唤物」里找到
        void CheckPetRef(string owner, EffectSpec e, int i)
        {
            hasSummonEffect = true;
            string kindZh = EffectCatalog.FindKind(e.Kind).Display;
            // 「全部召唤物」：对**每一只启用的**各来一遍 —— 只要有一只启用就是合法配置
            if (PetGen.IsAll(e.PetSummon))
            {
                if (enabled.Count == 0)
                    issues.Add(new("错误", $"{owner} 的第 {i} 条「{kindZh}」选了「全部召唤物」，"
                        + "但「召唤物」页里一只都没启用：到「召唤物」页添加一只并勾上「启用」。"));
                return;
            }
            if (string.IsNullOrWhiteSpace(e.PetSummon))
            {
                // 老存档（上一版只有一只召唤物）没有这个字段：生成时自动用第一只，行为和以前一致
                if (enabled.Count > 0)
                    issues.Add(new("提示", $"{owner} 的第 {i} 条「{kindZh}」"
                        + $"没选召唤物，生成时会用第一只启用的「{enabled[0].Name}」"
                        + "（老存档就是这样，重新在效果里选一次更清楚）。"));
                else
                    issues.Add(new("错误", $"{owner} 的第 {i} 条「{kindZh}」"
                        + "没有可用的召唤物：到「召唤物」页添加一只并勾上「启用」。"));
                return;
            }
            string want = e.PetSummon!.Trim();
            if (PetGen.Resolve(p, want) is null)
            {
                // 「填过类名」和「留空靠位置自动推」两种都要认：只比 ClassName 的话，
                // 类名留空的那种（界面上很常见）会被当成「这个召唤物不存在」，
                // 于是提示变成「找不到（可能已经被删掉了）」—— 明明是停用了，会误导用户去重加一只。
                bool existsButDisabled = p.Summons.Any(s => s is not null && !s.Enabled
                    && (string.Equals((s.ClassName ?? "").Trim(), want, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(PetGen.ClassNameOf(p, s), want, StringComparison.OrdinalIgnoreCase)));
                issues.Add(new("错误", $"{owner} 的第 {i} 条引用的召唤物「{want}」"
                    + (existsButDisabled
                        ? "已经被停用了（到「召唤物」页把它勾回「启用」，或在这条效果里换一只）。"
                        : "找不到（可能已经被删掉了，到「召唤物」页把它加回来，或在这条效果里换一只）。")));
            }
        }

        void Scan(string owner, IEnumerable<EffectSpec> effects, bool forPotion)
        {
            int i = 0;
            foreach (var e in effects)
            {
                i++;
                string kindZh = EffectCatalog.FindKind(e.Kind).Display;
                if (e.Kind == "SummonPet")
                {
                    if (forPotion)
                        issues.Add(new("错误", $"{owner} 的第 {i} 条是「召唤伙伴」，但**药水不支持**"
                            + "（用户要求这一档只支持卡牌 + 遗物触发）—— 请改用卡牌或遗物。"));
                    else
                    {
                        if (e.Amount is < 0 or > 999)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「召唤伙伴」生命 {e.Amount} 超出范围（0~999；0 = 用「召唤物」页配置的血量）。"));
                        // 「下回合生效」+「全部召唤物」：见下面 IsPetKind 那段同样的说明（延迟状态里只认一只）
                        if (e.NextTurn && PetGen.IsAll(e.PetSummon))
                            issues.Add(new("错误", $"{owner} 的第 {i} 条是「召唤伙伴」，同时勾了「下回合生效」和「全部召唤物」——"
                                + "这两样不能一起用。请改成具体一只伙伴，或者取消「下回合生效」。"));
                        CheckPetRef(owner, e, i);
                    }
                }
                else if (e.Kind == "PetAttack")
                {
                    if (forPotion)
                        issues.Add(new("错误", $"{owner} 的第 {i} 条是「伙伴攻击」，但**药水不支持**"
                            + "（药水没有「玩家选中的目标」，宠物该打谁说不清）—— 请改用卡牌。"));
                    else
                    {
                        if (e.Amount <= 0)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「伙伴攻击」伤害要大于 0（现在填的是 {e.Amount}）。"));
                        if (e.NextTurn && PetGen.IsAll(e.PetSummon))
                            issues.Add(new("错误", $"{owner} 的第 {i} 条是「伙伴攻击」，同时勾了「下回合生效」和「全部召唤物」——"
                                + "这两样不能一起用。请改成具体一只伙伴，或者取消「下回合生效」。"));
                        CheckPetRef(owner, e, i);
                    }
                }
                else if (EffectCatalog.IsPetKind(e.Kind))
                {
                    // ===== 新增的那批宠物效果（都要选「召唤物（哪一只）」，宠物不在场时安全跳过）=====
                    // V0.2.210：「下回合生效」+「全部召唤物」这一档生成不出来 —— 延迟状态类里是
                    // 「按 e.PetSummon 查出那**一只**伙伴再动手」（`PetGen.Resolve`，对 "*" 返回 null），
                    // 逐只展开需要每只一个延迟状态类，而类名 / 回读标记都只认一条。所以直接拦住说清楚，
                    // 不让它生成出一段什么都不做的注释（那就是静默失效）。
                    if (e.NextTurn && PetGen.IsAll(e.PetSummon))
                        issues.Add(new("错误", $"{owner} 的第 {i} 条是「{kindZh}」，同时勾了「下回合生效」和「全部召唤物」——"
                            + "这两样不能一起用：下回合生效那条要在生成期把这**一只**伙伴写进延迟状态里，"
                            + "「全部召唤物」做不到。请改成具体一只伙伴，或者取消「下回合生效」。"));
                    if (forPotion)
                    {
                        issues.Add(new("错误", $"{owner} 的第 {i} 条是「{kindZh}」，但**药水不支持**"
                            + "（这一档的宠物效果只做在卡牌上）—— 请改用卡牌。"));
                    }
                    else
                    {
                        CheckPetRef(owner, e, i);
                        // 「按生命值算的伙伴攻击」的数值是**升级加值**（基础是 0）：负数和超大值都拦掉。
                        if (e.Kind is "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp"
                            && e.Amount is < 0 or > 999)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「{kindZh}」的数值 {e.Amount} 超出范围"
                                + "（-999~999；这是升级后额外加的那点伤害，伤害本身按伙伴的生命值算）。"));
                        if (e.Kind == "PetHeal" && e.Amount <= 0)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「治疗伙伴」的治疗量要大于 0（现在填的是 {e.Amount}）。"));
                        if (e.Kind == "PetLoseHp" && e.Amount <= 0)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「伙伴失去生命」的失去量要大于 0（现在填的是 {e.Amount}）。"));
                        if (e.Kind == "PetGainMaxHp" && e.Amount <= 0)
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「伙伴最大生命 +N」的 N 要大于 0（现在填的是 {e.Amount}）。"));
                        if (e.Kind == "PetApplyPower" && string.IsNullOrWhiteSpace(e.PowerId))
                            issues.Add(new("错误", $"{owner} 的第 {i} 条「给伙伴施加状态」没有选「增益 / 减益」"
                                + "—— 到效果栏里选一个本体状态（例：力量 / 格挡 / 中毒）。"));
                        if (e.Kind == "PetSacrifice")
                        {
                            string formula = e.PetSacrificeFormula;
                            if (formula is not ("Fixed" or "MaxHp" or "CurHp"))
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」的收益公式不合法（{formula}）："
                                    + "只能是「固定 N / 最大生命 × 倍率 / 当前生命」。"));
                            else if (formula == "Fixed" && e.Amount <= 0)
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」选了「固定 N」，但 N 要大于 0（现在填的是 {e.Amount}）。"));
                            else if (formula == "MaxHp" && e.PetSacrificeMultiplier <= 0)
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」的倍率要大于 0（现在填的是 {e.PetSacrificeMultiplier}）。"));
                            if (e.PetSacrificeGain == "Damage" && e.TargetSide == "Self")
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」的收益是伤害，但「作用对象」选的是自己"
                                    + "—— 请把作用对象改成「单体敌人」「全体敌人」（或随机敌人 / 全体单位）。"));
                            // 收益选「获得增益 / 减益」时必须选一个状态：没选的话生成不出 PowerCmd.Apply<T>
                            //（泛型参数是空的 → CS0305），所以这里直接拦住，而不是生成一段编译不过的代码。
                            if (e.PetSacrificeGain == "Power" && string.IsNullOrWhiteSpace(e.PowerId))
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」的收益是「获得增益 / 减益」，"
                                    + "但没有选状态 —— 到效果栏的「状态（牺牲后获得）」里选一个"
                                    + "（例：力量 / 敏捷 / 中毒）。"));
                            // 按生命值算的收益（MaxHp / CurHp）没有可升级的变量：那个计算变量的名字是本体
                            // 固定死的，升级只能抬 CalculationBase，而「按生命值算」的基础值是 0 —— 所以这里的
                            // 升级增量生成时会**静默丢掉**（与其让用户以为加了，不如直接拦住）。
                            if ((formula is "MaxHp" or "CurHp") && e.UpgradeAmount != 0)
                                issues.Add(new("错误", $"{owner} 的第 {i} 条「牺牲伙伴」的收益公式是「{e.PetSacrificeFormulaZh}」"
                                    + "，它没有可以升级的数值（收益完全跟着伙伴的生命值走）—— 请把「升级增量」清成 0"
                                    + "，或者把公式改成「固定 N」。"));
                        }
                    }
                }
            }
        }
        // 宠物效果的自检：普通卡 + 诅咒 + 先古卡一起扫（诅咒里的「召唤伙伴」也会生成宠物代码）
        foreach (var c in p.AllCards)
        {
            if (c is null) continue;
            Scan($"卡牌「{c.Name}」", c.Effects, forPotion: false);
            // 一张牌最多一条「按生命值算」的宠物效果：本体的计算变量名是**固定**的
            // （DynamicVars.CalculatedDamage / CalculatedBlock / CalculationBase 都是按名字取的），
            // 两条会互相覆盖那个 CalculationBase → 数字对不上，而且回读也分不清哪条是哪条。
            // 「全部召唤物」那一档不算在内：它走的是内联计算（每只各自算），根本不碰这些固定名字的变量。
            var calcs = c.Effects.Where(x => CSharpCodeGen.CanonicalVarNeedsPetCmd(x) && !PetGen.IsAll(x.PetSummon)).ToList();
            if (calcs.Count > 1)
                issues.Add(new("错误", $"卡牌「{c.Name}」里有 {calcs.Count} 条「按生命值算」的宠物效果"
                    + $"（{string.Join("、", calcs.Select(x => EffectCatalog.FindKind(x.Kind).Display))}）"
                    + "—— 本体的计算变量名是固定的（CalculatedDamage / CalculatedBlock / CalculationBase 按名字取），"
                    + "一张牌只能有一条。请把其余的挪到另一张卡上。"));
        }
        foreach (var r in p.Relics)
        {
            if (r is null) continue;
            // 稀有度「先古」（用户要求加的）：本体所有先古遗物都是**先古之民给的**，不在普通奖励 / 商店里
            //（本体 RelicRarity.Ancient 就是为它们留的那一档）。
            if (r.Rarity == "Ancient" && r.InRelicPool)
                issues.Add(new("提示", $"遗物「{r.Name}」的稀有度是「先古」：本体这一档的遗物（古老牙齿 / 星盘 / 黑星…）"
                    + "**不会从普通奖励 / 商店里抽出来**，是先古之民给的。你现在还勾着「加入遗物池」——"
                    + "想让它在奖励池里出现就把稀有度改成 Common / Uncommon / Rare / Shop；"
                    + "确实要做先古遗物就取消那个勾，用「先古之民」页安排它（或自己的效果发放）。"));
            // V0.2.126：效果是**整只遗物唯一那一份**（触发时机只说明「什么时候触发」）→ 扫它一份
            var relicAllFx = r.Effects.Where(e => e is not null).ToList();
            Scan($"遗物「{r.Name}」", relicAllFx, forPotion: false);
            // 「伙伴攻击」必须挂在卡牌上：遗物没有「玩家选中的目标」
            // 新增的那批宠物效果同理（这一档只做在卡牌上，见 CSharpCodeGen.EmitRelicEffectOnce）。
            if (relicAllFx.Any(e => e.Kind == "PetAttack"))
                issues.Add(new("错误", $"遗物「{r.Name}」里放了「伙伴攻击」—— 遗物没有「玩家选中的目标」，"
                    + "宠物该打谁说不清。请把它放到卡牌上（遗物只支持「召唤伙伴」）。"));
            var relicPetFx = relicAllFx.Where(e => EffectCatalog.IsPetKind(e.Kind) && e.Kind != "SummonPet").ToList();
            if (relicPetFx.Count > 0)
                issues.Add(new("错误", $"遗物「{r.Name}」里放了宠物效果"
                    + $"（{string.Join("、", relicPetFx.Select(e => EffectCatalog.FindKind(e.Kind).Display).Distinct())}）"
                    + "—— 这一档的宠物效果只做在卡牌上（遗物只支持「召唤伙伴」）。"));
        }
        foreach (var s in p.Potions) if (s is not null) Scan($"药水「{s.Name}」", s.Effects, forPotion: true);
        // V0.2.168：自定义状态**触发时机**里的宠物效果也要扫 —— 用户报的用例「状态：生成卡牌时 → 召唤伙伴」。
        // 只扫**状态链支持**的那几档（目前是「召唤伙伴」和「牺牲伙伴」）：这样
        //   · 那只召唤物必须启用 / 类名找得到（不然生成出来会引用不存在的类）；
        //   · 「没启用任何召唤物」那条错误也靠它把 hasSummonEffect 置上。
        // 状态链**不支持**的宠物效果（伙伴攻击 / 按生命值算…）由 PowerTriggers.SupportedEffectKinds 那条报错，别在这里重复报。
        foreach (var cp in p.CustomPowers)
        {
            if (cp is null) continue;
            var cpPetFx = cp.Triggers.Where(t => t is not null).SelectMany(t => t.Effects)
                .Where(e => e is not null && EffectCatalog.IsPetKind(e.Kind) && PowerTriggers.Supports(e.Kind)).ToList();
            if (cpPetFx.Count == 0) continue;
            Scan($"自定义状态「{cp.Name}」", cpPetFx, forPotion: false);
        }

        if (enabled.Count == 0)
        {
            if (hasSummonEffect)
                issues.Add(new("错误", "有卡牌 / 遗物用了「召唤伙伴」「伙伴攻击」或其它宠物效果，但「召唤物」页里一只都没启用 —— "
                    + "生成出来的代码会引用一个不存在的宠物类（dotnet 直接报 CS0103）。"
                    + "去「召唤物」页添加一只并勾上「启用」、填好名字 / 血量，或者把这些效果删掉。"));
            // 「有停用的召唤物但没人用」也提醒一句：用户可能以为停用=不生成但效果还能用
            foreach (var off in p.Summons.Where(s => s is { Enabled: false }))
                issues.Add(new("提示", $"召唤物「{(string.IsNullOrWhiteSpace(off.Name) ? (string.IsNullOrWhiteSpace(off.ClassName) ? "(还没起名)" : off.ClassName.Trim()) : off.Name.Trim())}」"
                    + "没有勾「启用」，不会生成对应代码。"));
            return;
        }

        // ===== 每只逐条查 =====
        var seenClass = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in enabled)
        {
            string who = $"召唤物「{(string.IsNullOrWhiteSpace(s.Name) ? "(还没起名)" : s.Name.Trim())}」";
            string cls = PetGen.ClassNameOf(p, s);

            if (!string.IsNullOrWhiteSpace(s.ClassName) && !Naming.IsValidIdentifier(s.ClassName))
                issues.Add(new("错误", $"{who} 的英文类名不合法：{s.ClassName}（只能是英文/数字，且以字母开头）。"));
            else
            {
                // 本体的 ModelDb 只按**类名**算模型 ID（忽略命名空间），和本体怪物撞名会抛 DuplicateModelException
                if (EffectCatalog.VanillaMonsterNames.Contains(cls))
                    issues.Add(new("错误", $"{who} 的类名「{cls}」和本体怪物重名 —— 本体的模型 ID 只按类名算，"
                        + "撞名会让模组加载当场失败（DuplicateModelException）。换一个类名。"));
                // 列表内也不能重名：两只同名的话第二只在 ModelDb 里注册不上（同样抛 DuplicateModelException）
                if (seenClass.TryGetValue(cls, out string? firstCls))
                    issues.Add(new("错误", $"{who} 的类名「{cls}」和「{firstCls}」重复 —— 两只召唤物不能有同一个类名"
                        + "（本体按类名注册模型，重名会让模组加载当场失败）。"));
                else
                    seenClass[cls] = who;
            }

            if (string.IsNullOrWhiteSpace(s.Name))
                issues.Add(new("警告", $"有一只召唤物还没填中文名：生成时会用类名「{cls}」当宠物名牌（游戏里看着像英文变量名）。"));

            string display = PetGen.DisplayNameOf(s, cls);
            if (seenName.TryGetValue(display, out string? firstNm))
                issues.Add(new("警告", $"召唤物「{display}」和「{firstNm}」的中文名一样 —— 游戏里两只长得同名，"
                    + "分不清哪张卡召唤的是哪只（界面上的「召唤物」下拉能靠类名区分，但卡面描述只写名字）。"));
            else
                seenName[display] = display;

            if (s.Hp <= 0)
                issues.Add(new("错误", $"{who} 的生命要大于 0（现在填的是 {s.Hp}）—— 血量 ≤ 0 的宠物一上场就是死的，"
                    + "而且死的宠物打不出任何伤害（本体 AttackCommand.Execute 会静默早退）。"));
            else if (s.Hp > 999)
                issues.Add(new("警告", $"{who} 的生命 {s.Hp} 超出常规范围（建议 1~999）。"));

            if (s.StandDistance < SummonSpec.MinStandDistance || s.StandDistance > SummonSpec.MaxStandDistance)
                issues.Add(new("警告", $"{who} 的站位距离 {s.StandDistance} 超出建议范围（{SummonSpec.MinStandDistance}~{SummonSpec.MaxStandDistance}），"
                    + $"生成时会夹到范围内的值（现在会按 {PetGen.StandDistanceOf(s)} 生成）。"));

            if (!string.IsNullOrWhiteSpace(s.Image) && !File.Exists(s.Image))
                issues.Add(new("错误", $"{who} 的图片文件不存在：{s.Image}"));
            if (string.IsNullOrWhiteSpace(s.Image))
                issues.Add(new("提示", $"{who}没上传图片：会用本体的占位图（一张静态 error.png）显示，"
                    + "能正常上场 / 攻击 / 死亡，只是长得不好看。上传一张 PNG 就会自动生成宠物场景。"));
        }

        // ===== 「替主人承伤」：可以勾多只，由生成代码自己仲裁 =====
        // 本体的 Hook.ModifyUnblockedDamageTarget 是**链式遍历**（Hook.cs:2057-2065）：
        //     creature = item.ModifyUnblockedDamageTarget(creature, …)
        // 而本体 DieForYouPower 第一句是 `if (target != Owner.PetOwner?.Creature) return target;` ——
        // 两只都挂守卫时，链上第一只改完目标之后，第二只看到的已经不是主人、直接放行，
        // 于是「谁真正承伤」取决于本体不可控的监听顺序。我们的生成代码给所有勾选的宠物挂**同一个**守卫类，
        // 并在钩子里自己仲裁（只有宠物列表里第一只活着且挂着守卫的才承担，它死后下一只自动接手），
        // 所以这里**不再是错误**，只是把实际生效规则讲清楚。
        var guardians = enabled.Where(s => s.TakesDamageForOwner).ToList();
        if (guardians.Count > 1)
            issues.Add(new("提示", $"有 {guardians.Count} 只召唤物勾了「替主人承伤」（"
                + string.Join("、", guardians.Select(s => $"「{PetGen.DisplayNameOf(s, PetGen.ClassNameOf(p, s))}」"))
                + "）：承伤的是召唤物列表里第一只活着的，它死后会自动换下一只。"
                + "本体的伤害重定向是链式遍历，所以由生成代码自己仲裁，行为是确定的。"));
        // 用「伙伴替主人承伤（开 / 关）」效果、但那只召唤物没勾「替主人承伤」勾选框：
        // 不是错误（效果照样生效，生成时会一起把守卫 Power 类生成出来），但要说清差别，
        // 否则用户会以为「勾选框没勾 = 这个效果没用」。
        if (guardians.Count == 0 && PetGen.UsesGuardianEffect(p))
            issues.Add(new("提示", "你用了「伙伴替主人承伤（开 / 关）」效果，但没有任何召唤物勾「替主人承伤」勾选框："
                + "效果照样有效（打出那张牌 / 触发那只遗物时现场挂上守卫，它就开始替你承伤），"
                + "差别只在「开场不会自动承伤」—— 想让某只一上场就挡在你前面，就去「召唤物」页勾上它的「替主人承伤」。"));
        else if (guardians.Count == 1)
            issues.Add(new("提示", $"「{PetGen.DisplayNameOf(guardians[0], PetGen.ClassNameOf(p, guardians[0]))}」会在主人受可格挡攻击时替主人承伤"
                + "（中毒 / 失去生命这类穿盾伤害照旧打在主人身上）。"));

        // 召唤了但没地方召唤：不算错，只是提醒（有些人先配宠物、后加卡）
        // V0.2.126：效果挂在整只遗物上（时机只说明「什么时候触发」）→ 看遗物那份效果
        // V0.2.168：自定义状态**触发时机**里的「召唤伙伴」也算一处（用户报的用例）
        bool anyCardOrRelicSummons = p.AllCards.Any(c => c.Effects.Any(e => e.Kind == "SummonPet"))
            || p.Relics.Any(r => r.Effects.Any(e => e is not null && e.Kind == "SummonPet"))
            || p.CustomPowers.Any(cp => cp is not null && cp.Triggers.Any(t => t is not null
                && t.Effects.Any(e => e is not null && e.Kind == "SummonPet")));
        if (!anyCardOrRelicSummons)
            issues.Add(new("提示", $"召唤物已启用（{enabled.Count} 只），但没有任何卡牌 / 遗物 / 自定义状态在「召唤」它们"
                + "（卡牌效果里选「召唤伙伴」，或给遗物加一条「召唤伙伴」+ 触发时机「战斗开始时」，"
                + "或给自定义状态的某条触发时机加一条「召唤伙伴」）—— 游戏里永远不会上场。"));
        var summonRelicsWithoutCtx = p.Relics
            .Where(r => r.Effects.Any(e => e is not null && e.Kind == "SummonPet")
                        && r.Triggers.Any(t => t is not null && RelicTriggerLacksContext(t.Kind)))
            .ToList();
        if (summonRelicsWithoutCtx.Count > 0)
            issues.Add(new("警告", "有遗物在「" + string.Join(" / ", summonRelicsWithoutCtx.Select(r => r.Name)) + "」上配了「召唤伙伴」，"
                + "但那个触发时机**可能在战斗外**触发（战斗胜利 / 战斗结束 / 获得金币 —— 战斗外没有伙伴可召唤）"
                + "→ 生成时会被忽略。"
                + "想要「战斗开始时召唤伙伴」，请把触发时机改成「战斗开始时」。"));
    }

    /// <summary>触发时机的显示名（错误信息里用；认不出来就退回 id）。</summary>
    private static string RelicTriggerName(string kind) =>
        EffectCatalog.RelicTriggers.FirstOrDefault(t => t.Id == kind)?.Display ?? kind;

    /// <summary>
    /// 这个遗物触发时机**可能根本不在战斗里**（→ 召唤伙伴 / 战斗内的状态层数这类实现不了）。
    ///
    /// <para>V0.2.233：`GoldGained` 现在会按运行时造 choiceContext（见
    /// <see cref="CSharpCodeGen.BootstrapContextRuntime"/>），但「获得金币」也可能发生在**战斗外**
    /// （战斗奖励 / 事件）—— 外面没法召唤伙伴 / 上战斗状态，所以这条警告保留，只是理由改成「可能在战斗外」。</para>
    /// </summary>
    private static bool RelicTriggerLacksContext(string? trigger) =>
        trigger is "CombatVictory" or "CombatEnd" or "GoldGained";

    /// <summary>
    /// 「只做在卡牌上」的效果种类：卡牌那个大 switch（CSharpCodeGen.EmitCardEffectOnce）实现了，
    /// 遗物 / 药水那两条链（EmitRelicEffectOnce / EmitPotionEffectOnce）**连 case 都没有** ——
    /// 配到遗物 / 药水上会落到它们的 default，只留一行告警注释（CSharpCodeGen.WarnUnsupportedKind），
    /// 一行代码都不生成（用户看不到任何反馈、以为已经生效）。
    ///
    /// 这份清单不是猜的：把三个 switch 的顶层 case 用脚本抽出来做差集，
    ///   卡牌 −（遗物 ∪ 药水）= Expose / ClearPowerOne / ClearPowerDebuffs / DoublePower /
    ///                        ForbidBlock / ForbidHeal / TimesUp / OverdraftEnergy
    /// 恰好这 8 个（遗物 / 药水的 case 集合是卡牌的真子集，没有多出来的种类）。
    /// 以后任何一条链增删 case，都要重跑那个差集再改这里。
    ///
    /// 其中 TimesUp 在 ValidateEffects 里已经有自己的检查（连「作用对象要选指定敌人」一起管），
    /// 所以调用点把它排除了（同一个问题不报两遍）；OverdraftEnergy 是这 8 个里**唯一**以前没有任何检查的。
    /// </summary>
    private static bool IsCardOnlyKind(string? kind) =>
        kind is "Expose" or "ClearPowerOne" or "ClearPowerDebuffs" or "DoublePower"
            or "ForbidBlock" or "ForbidHeal" or "TimesUp" or "OverdraftEnergy"
            // 「本回合这张牌自己费用变 0」只对卡牌有意义（遗物 / 药水没有「自己这张牌」）
            or "CheapenSelf"
            // 「将此牌放回手牌」同理：遗物 / 药水没有「这张牌」，而且它要在打完牌之后才搬
            //（只有卡牌有 AfterCardPlayedLate 这个时机）—— 配到遗物 / 药水上会落到 default、一行代码都不生成
            or "ReturnSelfToHand"
            // 「抽到这张牌时它本场战斗费用 -N」：还是「这张牌」（卡自己的 AfterCardDrawn）
            or "CheapenSelfOnDraw";

    /// <summary>
    /// 同一个模型里同种效果出现多次时提醒一句：动态变量会自动起别名（Damage2 这种）。
    /// 为什么重要：本体 <c>DynamicVarSet</c> 用变量名当键，重名会直接抛异常，
    /// 而且是在**构造卡牌**的时候抛（战斗一开始创建卡组就炸）→ 表现就是「抽不了牌、结束不了回合、
    /// 战斗卡死在第一回合」。生成器已经自动处理了，这里只是让用户知道这件事。
    /// </summary>
    private static void AddDuplicateVarNotice(List<ValidationIssue> issues, string owner, IEnumerable<EffectSpec> effects)
    {
        var dups = CSharpCodeGen.VarKeysOf(effects)
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (dups.Count == 0) return;
        issues.Add(new("提示", $"{owner} 里有 {string.Join("、", dups.Select(g => $"「{g.Key}」×{g.Count()}"))} 条同种效果："
            + "本体要求同种动态变量各自起名字（否则构造卡牌时会抛 DynamicVarSet 重名异常、整场战斗卡死），"
            + "生成时已自动把第二个起成 " + string.Join("、", dups.Select(g => g.Key + "2")) + "，数值 / 描述都对得上，不用手动改。"));
    }

    /// <summary>
    /// 「转化为先古卡」的配对表检查（<see cref="EffectSpec.AncientRows"/>）。
    ///
    /// 四条：配对表非空 / 每行选了源组 / 每行至少勾一张先古卡 / 池子里引用的卡在本存档里存在。
    /// 外加一条提示：某一行的「组里张数 &gt; 池子张数」时告诉用户会少换几张（下策 b = 多出来的保持原样）。
    /// </summary>
    private static void ValidateAncientRows(List<ValidationIssue> issues, string owner, EffectSpec e, CharacterProfile p)
    {
        var rows = e.AncientRows;
        if (rows is null || rows.Count == 0)
        {
            issues.Add(new("错误", $"{owner} 的「转化为先古卡」还没有配对表：到效果下面点「加一行」，"
                + "左边选一个「组」（在「自定义参数 / 组」页里建），右边勾几张先古卡。"));
            return;
        }
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string who = $"{owner} 的配对表第 {r + 1} 行";
            if (row is null) { issues.Add(new("错误", $"{who} 是空的。")); continue; }
            if (string.IsNullOrWhiteSpace(row.Group))
            {
                issues.Add(new("错误", $"{who} 还没选「源组」—— 左边要选一个组（或自定义关键词那一组）。"));
            }
            else if (!EffectCatalog.IsCardGroupFilter(row.Group) && !EffectCatalog.IsKeywordFilter(row.Group))
            {
                issues.Add(new("错误", $"{who} 的源组「{row.Group}」不是有效的组（只支持「组」和「自定义关键词」两种）。"));
            }
            else
            {
                // 这一组眼下有没有牌：空组 = 生成出来谁都不匹配（不报错，但要说清楚）
                var inGroup = EffectCatalog.IsCardGroupFilter(row.Group)
                    ? EffectCatalog.CardGroupCards(p, row.Group)
                    : EffectCatalog.KeywordGroupCards(p, row.Group);
                if (inGroup.Count == 0)
                    issues.Add(new("警告", $"{who} 选的「{EffectCatalog.SpawnFilterZh(row.Group)}」这一组里**一张牌都没有**"
                        + "（到「卡牌」页给牌勾上这个组 / 关键词），生成出来这条效果什么都不会做。"));
                int inDeck = inGroup.Where(c => c.InStartingDeck || c.InCardPool).Sum(c => Math.Max(1, c.StartingCopies));
                var pool = (row.AncientCards ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (pool.Count == 0)
                {
                    issues.Add(new("错误", $"{who} 还没勾任何先古卡（右边那一栏）—— 这一行会被跳过。"));
                }
                else if (inDeck > pool.Count)
                {
                    issues.Add(new("提示", $"{who}：「{EffectCatalog.SpawnFilterZh(row.Group)}」在初始牌组里大约有 {inDeck} 张，"
                        + $"但只勾了 {pool.Count} 张先古卡 —— 按「各不相同」的规矩只会换前 {pool.Count} 张，"
                        + "多出来的保持原样（想全换就多勾几张先古卡）。"));
                }
            }
            // 池子里的卡必须存在：本体那 19 张先古卡是内置的（永远在），**你自己做的**才要查存档
            //（删了卡 / 改了类名 → 引用失效）。不能拿「存档里的卡」去查本体卡：真实存档里本体卡是
            // 「引用」条目（IsVanillaCard），自检里那份样例存档没有它们，会把 Break 误判成失效。
            var known = new HashSet<string>(p.AllCards.Where(c => c is not null).Select(c => Naming.From(p).CardClassName(p, c)), StringComparer.Ordinal);
            foreach (string cls in (row.AncientCards ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
            {
                if (CoreGenerationVanillaAncient.Contains(cls)) continue;
                if (!known.Contains(cls))
                    issues.Add(new("错误", $"{who} 里的先古卡「{cls}」在本存档里找不到（可能已经被删掉或改了类名）—— 请重新选一张。"));
            }
        }
    }

    /// <summary>
    /// 本体那 19 张「先古」稀有度的卡（类名）。用于「转化为先古卡」池子的存在性检查：
    /// 这些是内置的、永远存在，不该被当成「存档里找不到」。
    /// </summary>
    private static readonly HashSet<string> CoreGenerationVanillaAncient = new(StringComparer.Ordinal)
    {
        "Abundance", "Apotheosis", "Apparition", "BiasedCognition", "Break", "BrightestFlame", "Corruption",
        "ForbiddenGrimoire", "Maul", "MeteorShower", "NeowsFury", "Protector", "Quadcast", "Relax", "Suppress",
        "TheSealedThrone", "Whistle", "Wish", "WraithForm",
    };


    /// <summary>
    /// 「强化指定卡牌」的**目标卡**必须是认得出的卡牌类名（V0.2.157 修）。
    ///
    /// <para>为什么必须拦：生成出来的是 <c>BoostPowers.cs</c> 里的
    /// <c>if (cardSource is not &lt;目标卡类名&gt;) return 0m;</c> —— 这个类名不存在就是 **CS0103**
    /// 「当前上下文中不存在名称 X」，构建在编译那一步才炸，报错信息完全看不出是配置问题。
    /// 用户实测报过：存档是从别的机器拷过来的（<c>&lt;ModId&gt;.forge_classmap.json</c> 没跟着走），
    /// 卡牌在存档里已经改号 / 删掉，而「目标卡」这一栏还留着旧类名。</para>
    ///
    /// <para>⚠ 认不出时分两档报：本机**读不到**本体卡牌表（没解包工程 / 没 pck）就只报**提示**
    /// —— 这时候一张本体卡也会「认不出」，报错误全是误报（和「获得药水」那条同一个理由）。
    /// 只有「本体卡牌表读得到、本存档里也没有」才是**错误**。</para>
    /// </summary>
    private static void ValidateBoostCardTarget(List<ValidationIssue> issues, string where, CharacterProfile? p, EffectSpec e)
    {
        // 没有存档就没有「我们自己的卡」可查（只有自检那种调用会传 null）→ 什么都不报，免得全是误报。
        if (p is null) return;
        if (string.IsNullOrWhiteSpace(e.SpawnCardId))
        {
            issues.Add(new("错误", $"{where}没选目标卡 —— 请在「目标卡」里选一张要强化的牌。"));
            return;
        }
        string want = e.SpawnCardId!.Trim();
        var (known, rightCase) = RecognizeCardClass(p, want);
        if (known) return;
        if (rightCase is not null)
            issues.Add(new("错误", $"{where}的目标卡「{want}」和本存档里的卡**大小写对不上**："
                + $"生成出来的代码是按类名精确匹配的（写的是「{want}」），本存档里那张卡叫「{rightCase}」—— "
                + "请到「目标卡」里重新选一次（手打过这一栏才会这样）。"));
        else if (EffectCatalog.Cards.Count == 0)
            issues.Add(new("提示", $"{where}的目标卡「{want}」在本存档里找不到，而本机也读不到本体卡牌表"
                + "（没配本体工程目录 / 游戏 pck）—— 如果它其实是一张**本体卡**，这条提示可以忽略；"
                + "否则请重新选一张（那段按类名找牌的代码会编译不过）。"));
        else
            issues.Add(new("错误", $"{where}的目标卡「{want}」在本存档和本体卡牌表里都找不到"
                + "（多半是这张卡被删掉 / 改了类名，或者这份存档是从别的机器拷来的、卡牌清单没跟着走）—— "
                + "请到「目标卡」里重新选一张：生成出来的代码是按这个类名精确匹配的，名字不存在就是编译不过。"));
    }

    /// <summary>
    /// 「目标卡」这一类**按卡牌类名存的引用**必须认得出（V0.2.167 扩到生成 / 变化 / 全局加牌 / 填满手牌）。
    ///
    /// <para>为什么必须拦：生成出来的代码是按**类名**找牌的（<c>ModelDb.Card&lt;类名&gt;()</c> 那种），
    /// 类名不存在就是编译期 <c>CS0246 未能找到类型或命名空间名</c> —— 用户实测报过
    /// <c>StellePotion3.cs(24,91) StelleCard5</c>：药水里的「生成卡牌」还指着一张早就被删 / 改了手填类名的自动名。
    /// 生成前那次「按类名表修引用」（<see cref="NameStabilizer.RepairReferences"/>）只有在**类名表还在、
    /// 而且那张卡还在**时才修得回来（换机器只拷存档 = 类名表没跟过来），所以修不回来的必须在这里报出来。</para>
    ///
    /// <para>⚠ 分两档：本机读不到本体卡牌表（没解包工程 / 没 pck）时只报**提示**（这时候一张本体卡也会「认不出」）；
    /// 只差大小写时报错并给出正确写法（生成端按类名精确匹配）。</para>
    /// </summary>
    private static void ValidateCardTargetExists(List<ValidationIssue> issues, string where, CharacterProfile? p, EffectSpec e)
    {
        if (CardTargetProblem(p, where, e.SpawnCardId) is { } problem)
            issues.Add(new(problem.Level, problem.Message));
    }

    /// <summary>
    /// 同上，但返回「哪一档 + 那句话」（<c>null</c> = 没问题）—— 节点图那边的检查列表是
    /// <c>(级别, 说明)</c> 的元组、不是 <see cref="ValidationIssue"/>，所以共用这一份判断。
    /// </summary>
    internal static (string Level, string Message)? CardTargetProblem(CharacterProfile? p, string where, string? want,
        string fieldZh = "目标卡")
    {
        if (p is null || string.IsNullOrWhiteSpace(want)) return null;
        string name = want.Trim();
        var (known, rightCase) = RecognizeCardClass(p, name);
        if (known) return null;
        if (rightCase is not null)
            return ("错误", $"{where}的「{fieldZh}」填的是「{name}」，和本存档里那张卡的**大小写对不上**"
                + $"（本存档里叫「{rightCase}」）—— 生成出来的代码按类名精确匹配，请重新选一次。");
        if (EffectCatalog.Cards.Count == 0)
            return ("提示", $"{where}的「{fieldZh}」「{name}」在本存档里找不到，而本机也读不到本体卡牌表"
                + "（没配本体工程目录 / 游戏 pck）—— 如果它其实是一张**本体卡**这条可以忽略；否则请重新选一张。");
        return ("错误", $"{where}的「{fieldZh}」「{name}」在本存档和本体卡牌表里都找不到"
            + "（多半是那张卡被删了 / 改成了手填的类名 / 位置变了，或者这份存档是从别的机器拷来的、类名表没跟过来）—— "
            + $"请到「{fieldZh}」里重新选一张；名字不存在会让生成出来的代码编译不过（CS0246）。");
    }

    /// <summary>
    /// 「获得时」触发时机 + **要看战斗状态 / 敌人列表**的效果 = 开局必炸（V0.2.167）。
    ///
    /// <para>本体 <c>RunManager.FinalizeStartingRelics()</c>（<c>RunManager.cs:725-738</c>）在**开局那一刻**
    /// 对玩家身上每只遗物调 <c>await relic.AfterObtained()</c> —— 那时还没进任何房间 / 战斗，而
    /// <c>Creature.CombatState</c> 的注释写着「Never null for monsters. <b>Will be null for players when
    /// outside of combat.</b>」→ 生成出来的「对所有敌人造成伤害」这类效果直接 NRE，开局流程中断。</para>
    ///
    /// <para>用户实测报过：<c>Exception starting singleplayer run : NullReferenceException at
    /// ReyMod.ReyRelic1.AfterObtained()</c>，表现是**黑屏卡在「出发」那一步**（游戏照常起来，但那一局开不了）。</para>
    ///
    /// <para>判据只收「一定要读战斗状态 / 敌人列表」的那些种类（打敌人的伤害 / 击晕 / 翻倍·清除状态 /
    /// 禁止格挡·回复 / 召唤伙伴…）—— 其余（抽牌 / 能量 / 金币 / 生成卡牌 / 卡牌奖励 / 升级卡牌…）
    /// 在「获得时」是合法用法（本体「好吃饼干 / 爪子」就是拾取时选牌），不拦。</para>
    /// </summary>
    private static void ValidateObtainedCombatEffects(List<ValidationIssue> issues, CharacterProfile p)
    {
        static bool EnemySide(string? side) => side is "Enemy" or "AllEnemies" or "RandomEnemies" or "AllUnits";
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            if (r is null || r.IsForged) continue;      // 锻造图那条链自己管（普通方法，不走 AfterObtained）
            bool obtained = r.Triggers.Any(t => t is not null && t.Kind == "Obtained")
                            || string.Equals(r.Trigger, "Obtained", StringComparison.Ordinal);
            if (!obtained) continue;
            var bad = r.Effects.Where(e => e is not null && (
                e.Kind is "Stun" or "DoublePower" or "ClearPowerOne" or "ClearPowerDebuffs"
                        or "ForbidBlock" or "ForbidHeal" or "Outbreak" or "TimesUp"
                        or "SummonPet" or "SummonPetAll"
                || (e.Kind is "Damage" or "ApplyPower" or "TempPower" && EnemySide(e.TargetSide))
                || (e.Kind == "Damage" && string.IsNullOrWhiteSpace(e.TargetSide)))).ToList();
            if (bad.Count == 0) continue;
            string kinds = string.Join(" / ", bad.Select(e => EffectCatalog.FindKind(e.Kind).Display).Distinct());
            issues.Add(new("错误", $"遗物「{r.Name}」的触发时机是「获得时」，但效果里有**战斗内**的东西（{kinds}）—— "
                + "「获得时」发生在**开局那一刻**（本体 RunManager.FinalizeStartingRelics），那时场上还没有战斗，"
                + "玩家 Creature 的 CombatState 是 null → 生成出来的代码会在这里抛 NullReferenceException，"
                + "**开局直接失败（黑屏卡在「出发」）**。要「开局就对敌人动手」请把触发时机改成「战斗开始时」。"));
        }
    }

    /// <summary>
    /// 这个类名现在认不认得出来（「强化指定卡牌」的目标卡这类**按卡牌类名存的引用**）：
    /// ① 本存档里的卡（含「本体卡」引用条目，走 <see cref="Naming.CardClassName"/> 算出来的**生成名**）
    /// → 认得出来；② 本体卡牌表里查得到 → 认得出来。
    ///
    /// <para>顺带把「只差大小写」的那个正确写法带出来（<c>RightCase</c>）——
    /// 生成端是按类名**精确**匹配的，只差大小写照样编译不过，调用方可以照着这条改。</para>
    /// </summary>
    private static (bool Known, string? RightCase) RecognizeCardClass(CharacterProfile? p, string want)
    {
        string s = (want ?? "").Trim();
        if (s.Length == 0) return (false, null);
        string? rightCase = null;
        if (p is not null)
        {
            var n = Naming.From(p);
            foreach (var c in p.AllCards)
            {
                if (c is null) continue;
                string cls = n.CardClassName(p, c);
                if (string.Equals(cls, s, StringComparison.Ordinal)) return (true, null);
                if (rightCase is null && string.Equals(cls, s, StringComparison.OrdinalIgnoreCase)) rightCase = cls;
            }
        }
        foreach (var v in EffectCatalog.Cards)
        {
            if (string.Equals(v.Id, s, StringComparison.Ordinal)) return (true, null);
            if (rightCase is null && string.Equals(v.Id, s, StringComparison.OrdinalIgnoreCase)) rightCase = v.Id;
        }
        return (false, rightCase);
    }

    /// <summary>
    /// 两条「这一次打出」的卡牌专属读数（<c>ThisCardDamage</c> / <c>ThisCardBlock</c>，V0.2.194）的用法检查。
    ///
    /// <para>它们读的是**卡片身上那两个属性**：打出时清零、每条对应效果之后累加
    ///（见 <c>CSharpCodeGen.EmitAttack</c> / <c>case "Block"</c> / <c>ForgeGraphGen</c> 的 thisCardCap / thisBlockCap）。
    /// 于是这几种写法是**静默失效**的，必须报出来：</para>
    /// <list type="number">
    /// <item>不是卡牌链（遗物 / 药水 / 自定义状态的 Owner 不是 CardModel → 生成出来编不过）；</item>
    /// <item>卡牌上但挂在「卡牌时机」那几种钩子上（被消耗 / 被丢弃 / 被抽到 / 放回手牌 / 抽到减费…）——
    ///   那些钩子跑的时候「打出那一刻」的清零早就过去了，读到的是上一次打出留下的数；</item>
    /// <item>写它的那一条效果**前面没有任何对应的效果**（伤害那条要有「造成伤害」、格挡那条要有「获得格挡」）→ 恒 0；</item>
    /// <item>勾了「下回合生效」（延迟状态在下回合开始时才求值，同上）；</item>
    /// <item>和别的量混着写（复合式子编不过 —— 它没有卡面预览变量）。</item>
    /// </list>
    /// </summary>
    private static void ValidateCardReadings(List<ValidationIssue> issues, string owner, EffectSpec e,
        EffectKindOption kind, string ctx, int index, IReadOnlyList<EffectSpec> prior)
    {
        foreach (var r in CSharpCodeGen.CardReadingsAll)
        {
            if (!CSharpCodeGen.MentionsReading(e, r.Id)) continue;
            string where = $"{owner} 第 {index} 条「{kind.Display}」";
            string zh = CSharpCodeGen.ReadingZh(r.Id);
            // 卡牌链（含诅咒 / 状态卡那一页 —— 它们同样是 CardModel，属性照样有）
            if (ctx is not ("Card" or "Curse"))
            {
                issues.Add(new("错误", $"{where} 用了 {zh} —— 这条读数**只能用在卡牌上**"
                    + "（它读的是这张牌自己的属性，遗物 / 药水 / 自定义状态身上没有这个东西）。"
                    + (r.Id == CSharpCodeGen.ThisCardDamageId
                        ? "想用「本回合 / 本场战斗造成的伤害」请改内置读数 DamageDealtThisTurn / DamageDealtThisCombat。"
                        : "想用「本回合 / 本场战斗获得的格挡」请改内置读数 BlockGainedThisTurn / BlockGainedThisCombat。")));
                return;
            }
            bool inHook = CSharpCodeGen.IsCardTiming(e.Condition) || CSharpCodeGen.IsSelfReturn(e) || CSharpCodeGen.IsSelfDrawCost(e);
            if (inHook)
                issues.Add(new("错误", $"{where} 用了 {zh}，但这条效果挂在**卡牌时机**那几种钩子上"
                    + "（被消耗 / 被丢弃 / 被抽到 / 放回手牌 / 抽到减费那一类）—— 那些钩子跑的时候「打出这张牌」那一刻早就过去了，"
                    + "读到的会是上一次打出留下的数。想读「这一次打出」请把这条效果留在**打出时**（别给它选「卡牌时机」这个条件）。"));
            if (e.NextTurn)
                issues.Add(new("错误", $"{where} 用了 {zh}，同时勾了「下回合生效」——"
                    + "延迟状态是**下回合开始时**才求值的，那时候读到的是上一次打出留下的数。"
                    + "二选一：去掉「下回合生效」，或者别用它。"));
            // 这条读数**要前面先有哪一种效果**才有值（照打出顺序）
            string? needKind = r.Id switch
            {
                var x when x == CSharpCodeGen.ThisCardDamageId => "Damage",
                var x when x == CSharpCodeGen.ThisCardBlockId => "Block",
                var x when x == CSharpCodeGen.ThisCardExhaustId => "ExhaustCard",
                var x when x == CSharpCodeGen.ThisCardDiscardId => "DiscardCard",
                var x when x == CSharpCodeGen.ThisCardTransformId => "TransformCard",
                var x when x == CSharpCodeGen.ThisCardGenerateId => "GenerateCard",
                _ => null,
            };
            // 「格挡那条」要排除负数（负数走的是失去格挡，不记账）
            bool before = needKind is not null && prior.Any(x => x is not null && x.Kind == needKind
                && (needKind != "Block" || x.Amount >= 0m) && !CSharpCodeGen.IsInertZero(x));
            if (!before)
            {
                // 说人话的短名（不用 EffectKindOption.Display —— 那个带「（负数 = 失去）」这种括号）
                string what = needKind switch
                {
                    "Damage" => "造成伤害",
                    "Block" => "获得格挡",
                    "ExhaustCard" => "消耗卡牌",
                    "DiscardCard" => "丢弃卡牌",
                    "TransformCard" => "变化卡牌",
                    "GenerateCard" => "生成卡牌",
                    _ => "对应的效果",
                };
                issues.Add(new("警告", $"{where} 用了 {zh}，但它**前面没有任何「{what}」**——"
                    + $"这条读数记的是「这一次打出到目前为止{what}攒下来的量」，前面没有就是 0。"
                    + $"照打出顺序排：先「{what}」，后面那一条再读它（本体「荒疫打击」就是「先伤害、再读伤害」）。"));
            }
            if (e.Times > 1 || !string.IsNullOrWhiteSpace(e.TimesFormula)
                || e.RepeatCount > 1 || !string.IsNullOrWhiteSpace(e.RepeatFormula))
                issues.Add(new("警告", $"{where} 用了 {zh}，同时「生效次数 / 命中次数」大于 1 ——"
                    + "重复执行时后一次会把前一次刚记下的量一起读进去（越滚越多）。"
                    + "想「只算一条」就把重复次数填 1；想「每次重复各算各的」得拆成两条效果。"));
            if (e.Kind == "ApplyPower" && !CSharpCodeGen.IsExactReading(e.AmountFormula, r.Id))
                issues.Add(new("提示", $"{where} 的公式写的是「{ForgeFormula.DisplayText(e.AmountFormula)}」（不是光秃秃的 {r.Id}）："
                    + "卡面会照「现算预览值」印数字，而那个值只是**上一次打出**留下的数（卡面上会不准）。"
                    + $"想要本体「荒疫打击」那种观感，把公式改成正好是 {r.Id}，或者用这张牌的「自定义描述」自己写卡面句子。"));
            // 别的种类（伤害 / 格挡 / 生命…）**能生效**，但卡面印的是那个 0 值变量 —— 提醒一句，
            // 别让玩家以为「这张牌只造成 0 点伤害」。其中「伤害 = 本牌获得格挡」有专门的卡面文案（全身撞击那种）。
            if (EffectSpec.FormulaSupports(e.Kind) && e.Kind != "ApplyPower"
                && CSharpCodeGen.IsExactReading(e.AmountFormula, r.Id)
                && !(e.Kind == "Damage" && r.Id == CSharpCodeGen.ThisCardBlockId))
                issues.Add(new("提示", $"{where} 的「数值公式」是 {r.Id}：**能生效**，但卡面那一栏印的是变量占位（0）——"
                    + "这张牌进游戏后卡面会写成「0 点」。想让人看懂就给它配一段「自定义描述」"
                    + "（「卡牌」页 → 卡面预览下面的自定义描述），或者改用「施加状态」那一档（那一档有专门的卡面文案）。"));
            // 复合式子（ThisCardDamage + 2 / ThisCardBlock*2…）**编不过**：这两条读数读的是派生卡片类上的属性，
            // 而卡面那个「现算预览值」的 lambda 只拿到 CardModel（生成端已经在 VarDeclarationCore 里退回普通变量，
            // 没有预览变量可用）—— 与其生成一个编译不过的工程，不如在这里拦住并说清。
            if (EffectSpec.FormulaSupports(e.Kind)
                && ForgeFormula.MentionsIdentifier(e.AmountFormula, r.Id)
                && !CSharpCodeGen.IsExactReading(e.AmountFormula, r.Id))
                issues.Add(new("错误", $"{where} 的「数值公式」里把 {CSharpCodeGen.ReadingZh(r.Id)} 和别的量混在一起用了"
                    + $"（{ForgeFormula.DisplayText(e.AmountFormula)}）—— 这条读数**只能光秃秃地单独用**："
                    + "它读的是这张牌自己的属性，卡面那个「现算预览值」拿不到它（生成的代码会编译不过）。"
                    + "想加别的量请在卡面用「自定义描述」写，或者把这条效果拆成两条。"));
        }
    }

    private static void ValidateEffects(List<ValidationIssue> issues, string owner, IEnumerable<EffectSpec> effects,
        string? potionTargetType = null, string ctx = "Card", CharacterProfile? p = null)
    {
        int index = 0;
        // V0.2.194：要按「这条效果前面有没有造成伤害」判「本张牌这一次打出造成的伤害」用对了没，
        // 所以先落成表（原来直接 foreach 可枚举）。
        var effectList = effects.ToList();
        foreach (var e in effectList)
        {
            index++;
            if (!EffectCatalog.EffectKinds.Any(k => k.Kind == e.Kind))
            {
                issues.Add(new("错误", $"{owner} 的效果种类非法：{e.Kind}"));
                continue;
            }
            var kind = EffectCatalog.FindKind(e.Kind);
            // 「数值公式」（需求：「另加一栏『数值公式』」）：写了公式的话，左边的固定数值只是个**占位**
            //（生成出来的动态变量没人读，卡面写的也是公式本身）——
            // 所以「数值超出范围」那条不该再报，改成校验公式自己的合法性。
            if (e.UsesAmountFormula)
                ValidateAmountFormula(issues, owner, e, kind, ctx, p);
            else if (e.Amount < kind.Min || e.Amount > kind.Max)
            {
                // 有些效果种类**根本没有数值**（击晕 / 结束回合 / 额外回合 / 伙伴承伤开关…：
                // 允许范围就是个 0~0 的空区间）。这种填什么都不影响生成，别拿「超出范围」把用户拦住 ——
                // 用户实测踩过：卡牌里的「伙伴替主人承伤（开）」数值是 1，被这条错误挡住，得自己想到要填 0。
                if (kind.Min == kind.Max)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」没有数值这一说：填的 {e.Amount} 会被忽略（生成时不看它）。"));
                else
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」数值 {e.Amount} 超出允许范围 [{kind.Min} ~ {kind.Max}]。"));
            }
            // 写了公式、但效果种类根本不支持：不能静默忽略（用户会以为配好了）。
            // 非空但种类不支持时 UsesAmountFormula 是 false，所以这条要单独判 e.AmountFormula。
            if (!string.IsNullOrWhiteSpace(e.AmountFormula) && !EffectSpec.FormulaSupports(e.Kind))
                ValidateAmountFormula(issues, owner, e, kind, ctx, p);
            // 次数类公式（用户要求⑦）：只在卡牌上、和「= X / = 层数」互斥、名字要对得上
            ValidateCountFormulas(issues, owner, e, ctx, p);
            // 「这一次打出」那一组卡牌专属读数（V0.2.194 起的 6 条）：读的是卡片自己的属性，
            // 只能在卡牌链、只能在「打出时」那几条效果上，且前面要有一条对应的效果。
            ValidateCardReadings(issues, owner, e, kind, ctx, index, effectList.Take(index - 1).ToList());
            // 「命中次数 / 对群数」真的会被用到吗（用户报过「命中次数的公式不生效」）：
            // 生成端只有「造成伤害（多段）」「作用对象 = 随机敌人」「几种伙伴攻击」会读它，
            // 其它种类填了以前是**静默忽略**的 —— 现在说清（公式 → 错误；只填了数字 → 警告）。
            if (!e.UsesRepeatCount)
            {
                if (!string.IsNullOrWhiteSpace(e.RepeatFormula))
                    issues.Add(new("警告", $"{owner} 第 {index} 条「{kind.Display}」的「命中次数 / 对群数」里还留着公式"
                        + $"（{ForgeFormula.DisplayText(e.RepeatFormula)}），但这一条效果不会用到它 —— "
                        + "只有「造成伤害」「作用对象 = 随机敌人」和几种「伙伴攻击」会用到这个数字，所以它不生效。"
                        + "这一栏按种类本来是收起的，因为里面还有值才显示出来（多半是换效果种类之前填的）："
                        + "想「整条效果重复几遍」请用「生效次数」的公式，否则把它清空就行 —— **不挡住生成**。"));
                else if (e.RepeatCount > 1)
                    issues.Add(new("警告", $"{owner} 第 {index} 条「{kind.Display}」的「命中次数 / 对群数」填了 {e.RepeatCount}，"
                        + "但这一条效果不会用到它（只有「造成伤害」「作用对象 = 随机敌人」和几种「伙伴攻击」会用到）—— "
                        + "这个数字不生效。想「整条效果重复几遍」请改填「生效次数」。"));
            }
            // 药水链**根本不读**「命中次数」（V0.2.127 查明的老缺口）：上面那条 UsesRepeatCount 是按**种类**判的
            //（「造成伤害」在卡牌上确实会用到多段），而 `EmitPotionEffectOnce` 的「造成伤害」只有一句
            // `CreatureCmd.Damage(...)` —— 药水打谁由「作用目标」决定，没有多段 / 随机敌人那几条路。
            // 探针实测：药水上 RepeatCount=2 生成出来只有一次 Damage。以前**一句提示都没有**。
            // 和「概率生效」同样的处理：这一版**不补生成**（补了会改掉老存档的生成结果），改成明确报出来。
            if (ctx == "Potion" && e.UsesRepeatCount && (e.RepeatCount > 1 || !string.IsNullOrWhiteSpace(e.RepeatFormula)))
                issues.Add(new("警告", $"{owner} 第 {index} 条「{kind.Display}」填了「命中次数 / 对群数」"
                    + $"（{(string.IsNullOrWhiteSpace(e.RepeatFormula) ? e.RepeatCount.ToString() : ForgeFormula.DisplayText(e.RepeatFormula))}），"
                    + "但**药水不支持命中次数**：这一栏不会生成（在药水上一直是这样，打几下由「作用目标」决定）。"
                    + "想要「这条效果重复执行几遍」请改填「生效次数」。"));
            // 「升级增量」填了、但**这个种类没有可以升级的数值**：生成端在 OnUpgrade 里根本不会写那一行
            //（这些种类的数字是写死的循环次数，或者压根没有数字）→ 以前是**静默忽略**的。
            // 用户问过「升级卡牌的升级增量没用吗」：战斗内的「升级卡牌」是有用的（它声明了 Cards 变量），
            // 但「升级卡牌（全局）/ 获得卡牌（全局）/ 结束回合 / 额外回合 / 获得药水…」这些填了也不会生效。
            if (e.UpgradeAmount != 0 && CSharpCodeGen.KindIgnoresUpgradeIncrement(e))
                issues.Add(new("警告", $"{owner} 第 {index} 条「{kind.Display}」填了「升级增量」{e.UpgradeAmount:0.##}，"
                    + "但这个种类**没有可以升级的数值**（它的数字在生成时是写死的，或者根本没有数字）—— "
                    + "升级增量不会生效，请清成 0。要「升级后才变强」，请用有数值的效果（造成伤害 / 格挡 / 抽牌…），"
                    + "或者锻造卡那样给「升级后」单独画一张节点图。"));
            // 「数值 0 = 这张牌自己」那一档（给予关键词 / 回合结束自动打出 / 丢弃卡牌）**不挑牌**：
            // 界面现在不藏那几行了（藏了就没法填，用户实测报过「怎么不能填范围了」），
            // 所以「填了但这一档用不上」必须在这里说清楚，不能静默忽略。
            if (CSharpCodeGen.AmountZeroIsSelf(e) || (e.Kind is "GiveKeyword" or "TurnEndPlay" && e.Amount <= 0m && !e.AmountIsXEffective))
            {
                if (e.HasCardScope || !string.IsNullOrWhiteSpace(e.SpawnFilter))
                    issues.Add(new("提示", $"{owner} 第 {index} 条「{kind.Display}」的作用档位是「数值 0 = 这张牌自己」 —— "
                        + "这一档**不挑牌**，所以「范围界定」填了也不会生效（生成时整条按「这张牌自己」处理）。"
                        + "要让范围界定生效，请把数值改成 1 以上。"));
                else if (e.Kind == "DiscardCard" && e.SelectPile != "Hand")
                    issues.Add(new("提示", $"{owner} 第 {index} 条「丢弃卡牌」的作用档位是「数值 0 = 丢弃这张牌自己」 —— "
                        + "这一档不挑牌，「从哪里丢弃」填的是「{EffectCatalog.SelectPileZh(e.SelectPile)}」不会生效。"));
                else if (e.Kind == "ExhaustCard" && e.SelectPile != "Hand")
                    issues.Add(new("提示", $"{owner} 第 {index} 条「消耗卡牌」的作用档位是「数值 0 = 消耗这张牌自己」 —— "
                        + "这一档不挑牌，「从哪里消耗」填的是「{EffectCatalog.SelectPileZh(e.SelectPile)}」不会生效。"));
                // 遗物 / 药水 / 自定义状态上**没有「这张牌」**：填 0 只会整条不生效（生成端不会崩，
                // 但也绝不会去消耗什么）—— 用户实测报过「填 0 想消耗自身却什么都没发生」，所以这里必须说清。
                else if (e.Kind == "ExhaustCard" && ctx != "Card")
                    issues.Add(new("警告", $"{owner} 第 {index} 条「消耗卡牌」填了 0 —— 「数值 0 = 消耗这张牌自己」"
                        + "只有在**卡牌**上成立（遗物 / 药水 / 状态没有「这张牌」）—— 这条效果**不会做任何事**。"
                        + "想消耗某一摞的牌，请把数值填 ≥ 1（消耗几张）。"));
            }
            // 作用对象 = 队友（用户要求）：只有那些「真的按选中的那个队友 / 全体队友逐个动手」的            // 效果种类才支持，别的种类生成端会把它当成「自己」（那就变成给自己加东西了，很坑）——
            // 所以这里直接拦住并列出支持的名单，绝不静默。
            if (e.TargetSide is "Ally" or "AllAllies")
            {
                string sideZh = e.TargetSide == "Ally" ? "指定队友" : "全部队友";
                // V0.2.200：「牺牲伙伴 → 造成伤害」也真的按作用对象打（生成端走 EmitPerCreature），
                // 所以队友那两档对它也算支持 —— 判据挂在**收益类型**上，别的收益（格挡 / 状态）照旧不算。
                // ⚠ 只放行**卡牌链**：队友那一档要么要玩家选中的目标（Ally）、要么生成的是
                // `base.Owner.Creature` 那一套（AllAllies）—— 状态钩子里 base.Owner 本身是 Creature，
                // 放行会生成出 CS1061 的代码（宁可在校验器说清，也不给一个编译不过的工程）。
                bool kindOk = e.Kind is "Damage" or "ApplyPower" or "TempPower" or "Block" or "Heal" or "HpLoss" or "MaxHp"
                    || (e.Kind == "PetSacrifice" && e.PetSacrificeGain == "Damage" && ctx is "Card" or "Curse");
                if (!kindOk)
                {
                    issues.Add(new("错误", $"{owner} 第 {index} 条「{kind.Display}」的作用对象选了「{sideZh}」，"
                        + "但这个效果种类还没有实现队友目标（生成端会把它当成「自己」）—— 目前支持队友目标的只有："
                        + "造成伤害 / 施加增益减益 / 临时增益 / 获得格挡 / 回复生命 / 失去生命 / 最大生命。"));
                }
                else if (e.TargetSide == "Ally" && ctx is "Relic" or "Potion")
                {
                    issues.Add(new("错误", $"{owner} 第 {index} 条「{kind.Display}」的作用对象选了「指定队友」，"
                        + "但遗物 / 药水**没有「玩家选中的目标」**（本体只有卡牌会在打出时弹目标选择）—— "
                        + "「指定队友」只能做在卡牌上；想让遗物 / 药水影响全队，请改用「全部队友」。"));
                }
            }
            // 作用对象 = 「全体单位」（V0.2.132 新增的第 7 档）：生成端真的实现了的种类有限 ——
            // 其余种类落进各条目标分支的 default 会**静默变成「对自己」**，所以这里直接报错拦住。
            ValidateAllUnitsTarget(issues, owner, index, kind, e, ctx);
            // 数值 0 的效果（用户要求）：游戏里**不显示、也不执行**。
            // 为什么不做成「写个 0 摆着」：本体的伤害 / 格挡会被力量 / 敏捷加成
            //（0 点伤害 + 3 点力量 = 3 点伤害），所以 0 必须真的「不存在」。
            //
            // 「临时增益 / 临时减益」**单独拦成错误**：用户报的就是「临时增益不生效」——
            // 本体的 PowerCmd.Apply 收到 0 会**在第一行直接 return**（见 PowerCmd.Apply<T>），
            // 所以数值 0 的临时增益打出去一点变化都没有，而它在卡面上也不显示（描述里不写），
            // 用户只会看到「这张牌什么都没发生」。这里把原因和怎么填一次说清。
            if (CSharpCodeGen.IsInertZero(e) && e.Kind == "TempPower")
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」数值是 0：本体 PowerCmd.Apply 收到 0 会直接返回，"
                    + "打出去**一点变化都没有**（这就是「临时增益不生效」）。请在「数值」里填一个数"
                    + "（例：本回合 +5 点力量填 5；让目标失去 5 点填 -5），或者写「数值公式」；"
                    + "想「升级之后才生效」就填「升级增量」。"));
            else if (CSharpCodeGen.IsInertZero(e))
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 0、升级也不加数值："
                    + "按你的要求这条效果在游戏里**不显示、也不会执行**（描述里不写、代码也不生成）。"
                    + "想让它升级后才生效，就填「升级增量」；不需要它就删掉这条效果。"));
            else if (CSharpCodeGen.IsUpgradeOnlyZero(e))
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 0、升级增量 {e.UpgradeAmount:0.##}："
                    + "没升级时这条**不显示、也不执行**（不会被力量 / 敏捷加成），升级之后才出现并生效。"));
            // 「获得能量 / 获得金币」填了负数 = 扣除（本体 GainEnergy / GainGold 对非正数直接返回，
            // 所以生成的是 LoseEnergy / LoseGold，数值会夹到 0、不会扣成负数）。这里是让用户确认一下语义。
            if (e.Kind is "Energy" or "Gold" && e.Amount < 0 && !e.AmountIsXEffective)
            {
                string unit = e.Kind == "Energy" ? "点能量" : "枚金币";
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 {e.Amount}："
                    + (e.NextTurn
                        ? $"会生成「下回合开始时失去 {-e.Amount} {unit}」（本体下回合加能量的状态只认正数，所以用生成的负债状态扣）"
                        : $"会生成「失去 {-e.Amount} {unit}」")
                    + $"，不够时只扣到 0（不会变成负数）。"));
            }
            if (e.Kind is "Energy" or "Gold" && e.Amount < 0 && e.AmountIsXEffective)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值 = X 且填了负数：X 要到打出时才知道正负，"
                    + "所以这条会按「获得 X」生成（负数被忽略）。"));
            // ===== V0.2.141：「升级增量把数值抵成了 0」→ **警告**（不是错误）=====
            //
            // 用户实测报的 BUG：「在生成器填负值的力量不生效」。查下来是两件事叠在一起：
            //   ① 回读把**同一个**升级增量算了两遍（生成端写了三处：变量构造第二参 / 打出时三元式 /
            //      OnUpgrade 的 UpgradeValueBy —— 回读时前两处已经算出增量，第三处又 += 一遍），
            //      「力量 -2」的卡回读成 UpgradeAmount=2 → 再生成出来升级后正好是 0 层；
            //   ② 就算增量是对的（用户自己填「升级增量 2」配公式 -2），升级后那一档确实是 0 层，
            //      而**界面上一个字都不说** —— 用户只会看到「填了负值，升级后打出去什么都没发生」。
            // ①已在 ProjectRecovery 修掉（同一个增量不再累加两次）；②在这里变成看得见的一条警告。
            //
            // 级别为什么是**警告**而不是错误：这**不是**生成不出来的配置（老存档 / 用户就想这么配都得能继续生成），
            // 而且「升级后为 0」有时正是用户想要的（先把这条效果占个位）。所以只提醒、不拦。
            //
            // 判据只看**纯常数公式**那一支（真·现算公式在生成期折不出常数，升级后是不是 0 要打出时才知道）。
            // 非卡牌链（遗物 / 药水 / 状态 / 锻造）上「数值 + 升级增量 == 0」走同一个判据（那些用固定 Amount，
            // 折不出常数公式 → 落到 else 那一支）。判据本体在 CSharpCodeGen.UpgradeCancelsToZero，
            // 和界面摘要行 / 生成端共用同一份折叠，不另写一套。
            if (e.Amount != 0m && CSharpCodeGen.UpgradeCancelsToZero(p, e))
            {
                bool hasFormula = Generation.ForgeNumbers.TryEffectFormulaConst(p, e, out decimal constValue);
                decimal baseValue = hasFormula ? constValue : e.Amount;
                string unit = EffectUnitZh(e.Kind);
                string shown = hasFormula ? $"数值公式里的 {baseValue:0.##}" : $"数值 {baseValue:0.##}";
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」：升级增量 {(e.UpgradeAmount > 0 ? "+" : "")}{e.UpgradeAmount:0.##} "
                    + $"把{shown} 抵成了 **0**：**升级后的这张牌打出去不会有任何效果**"
                    + $"（没升级的那张才是 {baseValue:0.##}{unit}）。想让升级后照样有 {-baseValue:0.##}，"
                    + "请把「升级增量」清成 0；想让升级后更强，请把「升级增量」改成正数方向。"));
            }
            // 负数 = 反向（扣除层数 / 失去格挡）：生成时会改走 PowerCmd.ModifyAmount(-N) / CreatureCmd.LoseBlock。
            // 两个组合它做不到，直接拦住并在提示里说清：
            // 翻倍状态层数：只做在卡牌上（要「选中的目标」），而且必须选是哪个状态
            // 暴露 / 清除指定状态：必须选是哪个状态；三者都只做在卡牌上（需要「选中的目标」）
            if (e.Kind is "Expose" or "ClearPowerOne" && string.IsNullOrWhiteSpace(e.PowerId))
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」没有选「增益 / 减益」"
                    + (e.Kind == "Expose" ? "（要施加哪个状态）。" : "（要清除哪个状态）。")));
            if (e.Kind == "DoublePower" && string.IsNullOrWhiteSpace(e.PowerId))
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」没有选「增益 / 减益」（哪一栏填要翻倍的那个状态）。"));
            // 「只做在卡牌上」的那批种类：遗物 / 药水两条链（EmitRelicEffectOnce / EmitPotionEffectOnce）
            // 连 case 都没有 —— 配到遗物 / 药水上会落到它们的 default 上，只留一行告警注释、一行代码都不生成
            //（CSharpCodeGen.WarnUnsupportedKind）。这里在**生成前**拦住 / 提醒。
            //
            // 级别怎么定（照「静默跳过会不会让这只遗物 / 这瓶药水完全没作用」这条判据）：
            //   · 这只遗物 / 这瓶药水的**全部**效果都是这类种类 → **错误**：整只遗物 / 整瓶药水生成出来是个空壳，
            //     用户会以为「配好了」，实际游戏里拿到它什么都不发生；
            //   · 只是其中一条 → **警告**：其余效果照常工作，只是这一条静默消失，不该拦住整个生成
            //     （也避免给老存档冒出一堆新错误）。
            //
            // 「大限已至」要排除：它在下面（TimesUp 那一段）已经有一条更细的检查了 —— 不但拦住非卡牌上下文，
            // 还管「作用对象必须选指定敌人」。这里再报一条就是同一个问题报两遍。
            else if (ctx is "Relic" or "Potion" && IsCardOnlyKind(e.Kind) && e.Kind != "TimesUp")
            {
                bool wholeThingDead = effects.All(x => IsCardOnlyKind(x.Kind));
                // 原因按种类说清：绝大多数是「需要玩家选中的那个目标」，而「这张牌自己」那一类
                //（本回合费用变 0 / 放回手牌）要的是**这张牌本身** —— 遗物 / 药水压根没有「自己这张牌」。
                string why = e.Kind is "CheapenSelf" or "ReturnSelfToHand" or "CheapenSelfOnDraw"
                    ? "它要的是「这张牌自己」"
                    : "它需要「选中的目标」";
                issues.Add(new(wholeThingDead ? "错误" : "警告", $"{owner} 的「{kind.Display}」只做在卡牌上"
                    + $"（{why}，遗物 / 药水没有）—— 生成时会跳过、一行代码都不写"
                    + (wholeThingDead
                        ? "；这只遗物 / 这瓶药水的效果全是这一类，生成出来等于没有作用。"
                        : "（其它效果照常生效）。")
                    + "请把这一条放到卡牌的效果里。"));
            }
            if (e.Kind == "ApplyPower" && e.Amount < 0 && e.NextTurn)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」填了负数（扣除层数）又勾了「下回合生效」"
                    + "—— 下回合那套是给目标挂一个延迟状态（它的机制是加层数），扣层数没有对应的做法。"
                    + "请二选一：去掉「下回合生效」，或把数值改成正数。"));
            // 「临时增益」填负数 = 让目标先**失去** N 层、到点还原（用户要求：让敌人失去 5 点临时力量）。
            // 以前这里是一条**错误**拦着，理由写的是「负数会变成『本回合先扣、回合结束加回来』，和预期不一样」
            // —— 那个理由站不住：生成端本来就是「撤 -base.Amount」，负数档正好还原成正数。
            // 现在改成提示，说清它会做什么、以及和「施加增益/减益填负数」的区别（那个是永久扣，不还原）。
            if (e.Kind == "TempPower" && e.Amount < 0)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 {e.Amount}："
                    + $"会让目标先**失去 {-e.Amount} 层{EffectCatalog.PowerName(e.PowerId)}**，"
                    + (e.TempPowerReverse == TempPowerReverses.NextTurnStart
                        ? "到**下回合开始时**再加回来"
                        : "到**这一回合结束时**再加回来")
                    + NegativeLayerNote(e.PowerId, e.Amount)
                    + "这和「施加增益/减益」填负数的区别：那个是**永久**扣掉、不会还原。"));
            if (e.Kind == "ApplyPower" && e.Amount < 0)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 {e.Amount}："
                    + $"会生成「{(e.TargetSide == "Self" ? "失去" : "扣除")} {-e.Amount} 层{EffectCatalog.PowerName(e.PowerId)}」"
                    + NegativeLayerNote(e.PowerId, e.Amount)
                    + "—— 和卡牌 / 遗物 / 药水同一条实现。"));
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.Powers.Count == 0) { /* 效果库整体为空时由上面统一报错 */ }
            else if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.FindPower(e.PowerId) is null && !EffectCatalog.IsCustomPower(e.PowerId))
                issues.Add(new("错误", $"{owner} 的增益/减益还没选状态：未选择有效的 Power（可以选本体的状态，也可以选「自定义状态」页里自己造的那个）。"));
            // 本体里有些状态显示的数字不是层数（自己 override 了 PowerModel.DisplayAmount）：
            // 填多少层状态栏都不会显示你填的值（用户报过「施加 30 层缓慢，游戏里只看到缓慢」）
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerAmountNote(e.PowerId) is string amountNote)
                issues.Add(new("提示", $"{owner} 施加的是「{EffectCatalog.PowerName(e.PowerId)}」："
                    + $"本体这个状态显示的数字不是层数 —— {amountNote}"
                    + "（层数照旧记着，只是状态栏那个数字由它自己算。）"));
            // V0.2.244：这个状态本体**没有玩法效果**（只是视觉标记）—— 直接说清「施加了也不会发生任何事」
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerNoGameplayNote(e.PowerId) is string noFxNote)
                issues.Add(new("提示", $"{owner} 施加的是「{EffectCatalog.PowerName(e.PowerId)}」：{noFxNote}"));
            // V0.2.245：这个状态的效果**有它自己的触发条件**（例：翱翔只减攻击伤害）—— 说清，免得以为没生效
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerMechanicNote(e.PowerId) is string mechNote)
                issues.Add(new("提示", $"{owner} 施加的是「{EffectCatalog.PowerName(e.PowerId)}」：{mechNote}"));
            // 给予卡牌关键词：关键词必须选、而且必须存在（本体枚举名 或 已定义的自定义关键词）
            if (e.Kind == "GiveKeyword")
                ValidateGiveKeyword(issues, owner, e, p, ctx);
            // 播放音效（V0.2.154）：音效必须选，而且得是「像 FMOD 事件路径」的形状；不在候选表里给一句提示
            if (e.Kind == "PlaySfx")
                ValidatePlaySfx(issues, owner, e);
            // 播放音频文件（V0.2.155）：必须选一条上传的音频，而且那条的文件得真的在
            if (e.Kind == "PlaySoundFile")
                ValidatePlaySoundFile(issues, owner, e, p);
            // 需求 ⑦：生成 / 变化出来的卡带上的关键词也必须存在（同一个判据，两处共用一个取值口径）
            if (e.UsesSpawnOptions && e.HasSpawnKeyword)
                ValidateSpawnKeyword(issues, owner, e, p);
            // 「直接把「缓慢」设成 N%」那几个坑
            if (e.SlowPercent > 0)
            {
                if (e.Kind != "ApplyPower")
                    issues.Add(new("警告", $"{owner} 填了「直接把「缓慢」设成 {e.SlowPercent:0.##}%」，"
                        + $"但这条效果是「{EffectCatalog.FindKind(e.Kind).Display}」不是「施加增益/减益」—— 这个选项会被忽略。"));
                else if (!e.IsSlowPower)
                    issues.Add(new("警告", $"{owner} 填了「直接把「缓慢」设成 {e.SlowPercent:0.##}%」，"
                        + $"但选的状态不是「缓慢」（{EffectCatalog.PowerName(e.PowerId)}）—— 这个选项会被忽略。"));
                else
                {
                    if (e.SlowPercentEffective != e.SlowPercent)
                        issues.Add(new("提示", $"{owner} 填的「缓慢」百分比 {e.SlowPercent:0.##}% 按 10% 一档折算成 "
                            + $"{e.SlowPercentEffective}%（本体内部是整数档位：1 档 = 受到伤害 +10%）。"));
                    if (e.NextTurn)
                        issues.Add(new("警告", $"{owner} 勾了「下回合生效」+「直接把「缓慢」设成 {e.SlowPercentEffective}%」："
                            + "「下回合生效」走的是延迟状态，这个百分比选项不会生效（会在下回合按层数正常施加）。"));
                    issues.Add(new("提示", $"{owner} 施加「缓慢」时会按本体的做法只施加 1 层，然后把它的内部数值直接设成 "
                        + $"受到伤害 +{e.SlowPercentEffective}%（本体「缓慢」显示/生效的数字不是层数 —— 它按「本回合每打出一张牌 +10%」算，"
                        + "所以「施加 30 层」在游戏里看不到 30；另外它每次敌人回合开始会清零，这是本体机制）。"));
                    if (!e.AmountIsStack && e.Amount != 1)
                        issues.Add(new("提示", $"{owner} 那条「缓慢」的层数填的是 {e.Amount:0.##} —— 层数对「缓慢」没有作用，"
                            + "生成时会忽略它、按本体的做法施加 1 层（真正生效的是上面那个百分比）。"));
                }
            }
            // 「下回合生效」：只有真正实现了延迟分支的种类才放行（见 EffectCatalog.HonorsNextTurn）——
            // 以前判的是 EffectKindOption.SupportsNextTurn，那一栏对 8 个**没实现**的种类也写着 true，
            // 于是勾了等于没勾（静默失效）。
            // ⚠ V0.2.133 起 HonorsNextTurn = 「除 EffectCatalog.NextTurnMeaningless 之外的每一种」，
            //   而 EffectKindOption.SupportsNextTurn **就是** HonorsNextTurn（同一份判据）——
            //   界面上勾不到的种类，这里也就不会再报（不会有「勾了没反应」）。
            if (e.NextTurn && !EffectCatalog.HonorsNextTurn(e.Kind))
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」不支持「下回合生效」—— "
                    + "这一条效果的语义**绑死在打出的那一瞬间**（比如「本回合这张牌自己费用变 0」「把这张牌放回手牌」"
                    + "「结束回合」「额外获得一回合」「刮削」「回合结束时自动打出」「抽到就减费」），"
                    + "没有「下回合再做」这回事，所以界面上也不给这一栏显示勾选框。请去掉这个勾。"));
            // V0.2.220：「为抽到的牌加关键词」+「下回合生效」→ 抽牌发生在**下回合开始时**
            // （本体 DrawCardsNextTurnPower 自己抽），那一刻不在这一条效果的代码里 → 加不了关键词。
            // 与其静默少一半，不如提前说清（和别处「配了但不生效」一律给提示的规矩一致）。
            if (e.HasDrawnKeyword && e.NextTurn)
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」勾了「下回合生效」：牌是**下回合开始时**才抽的"
                    + "（本体 DrawCardsNextTurnPower 自己抽），那一刻已经不在这一条效果的代码里 → "
                    + "「为抽到的牌加关键词（" + e.DrawnKeywordZh + "）」这一项**不会生效**。"
                    + "要去掉这个勾，或者把关键词改成单独一条「给予卡牌关键词」。"));
            // V0.2.220：「为抽到的牌加关键词」只做在真的会「把牌拿进手」的那几档；
            // 别的地方（老存档手改 JSON / 换了效果种类没清干净）静默丢掉最难查，所以这里说一句。
            if (e.HasDrawnKeyword && !Sts2CharForge.Core.Effects.EffectFieldPolicy.UsesDrawnKeyword(e.Kind))
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」配了「为抽到的牌加关键词（" + e.DrawnKeywordZh + "）」，"
                    + "但这一种效果**不抽牌也不拿牌**，generate 时这一项会被忽略 —— 请把它配到抽牌 / 从三摞拿牌 / 填满手牌上，"
                    + "或者改成单独一条「给予卡牌关键词」。"));
            // 生成类的「下回合生效」以前只做在**卡牌**上；V0.2.133 起遗物 / 药水 / 自定义状态
            // 三条链也都接上了通用延迟分支（见 CSharpCodeGen.IsDelayedEffectKind），所以这里不再拦。
            // 额外资源量 + 下回合生效：正数走本体的 StarNextTurnPower；「需要 N 点」（负数）是卡牌费用，跟下回合无关
            if (e.Kind == "ExtraResource" && e.NextTurn)
            {
                if (e.Amount > 0)
                    issues.Add(new("提示", $"{owner} 的「获得额外资源量」会在**下回合开始时**获得 {e.Amount:0.##} 点"
                        + "（本体 StarNextTurnPower：回合开始时发放并自毁）。"));
                else if (!e.AmountIsXEffective)
                    issues.Add(new("警告", $"{owner} 的「获得额外资源量」填的是 {e.Amount:0.##}（负数 = 卡牌费用「需要 N 点」），"
                        + "费用和「下回合生效」没关系 —— 这一勾会被忽略（想扣资源请用正数 + 负的升级增量，或直接改基础数值）。"));
            }
            // 额外资源量上限：这一条改的是**上限**，上限本身是「额外资源量」页的东西
            if (e.Kind == "RaiseResourceCap" && p is not null)
            {
                if (!p.ExtraResource.Enabled)
                    issues.Add(new("错误", $"{owner} 用了「增加额外资源量上限值」，但这个存档**没启用额外资源量** —— "
                        + "上限机制整个不会生成，这一条等于白配（先去「额外资源量」页把开关打开）。"));
                else if (e.Amount <= 0m)
                    issues.Add(new("警告", $"{owner} 的「增加额外资源量上限值」填的是 {e.Amount:0.##}："
                        + "正数才是「把上限抬高多少」，0 / 负数不会让上限变小（想调低上限请改「额外资源量」页那个基础值）。"));
                else if (p.ExtraResource.Max <= 0)
                    issues.Add(new("提示", $"{owner} 的「增加额外资源量上限值」加 {e.Amount:0.##} 层，"
                        + "但「额外资源量」页的「数量上限」填的是 0（= 不限）——"
                        + "这种情况下实际上限就等于这个加成层数，想要一个固定顶请把基础值填上。"));
            }
            // 「取几张」（需求：「消耗任意张手牌」）：
            //   · 只有选牌类效果用得到 —— 别的效果上填了会被忽略；
            //   · 「随机」那条路**没有选牌界面**，「任意张」无从谈起 → 提示会被当成「恰好 N 张」；
            //   · 「任意张」时没有数字可显示，升级增量也没有落点 → 提示。
            if (!string.IsNullOrWhiteSpace(e.CountMode) && !e.UsesCountMode)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」填了「取几张 = {CountModes.Zh(e.CountMode)}」，"
                    + "但这个选项只有「消耗 / 丢弃 / 升级卡牌」和「从抽牌堆 / 弃牌堆 / 消耗牌堆拿牌」用得到 —— 这条会被忽略。"));
            if (e.UsesCountMode && e.CountMode != CountModes.Exact)
            {
                if (e.CountIsAll)
                {
                    // 「全部」（V0.2.163）：不弹选牌界面 —— 选牌方式（自己选 / 随机 / 牌顶）不生效，
                    // 升级增量也没有落点（没有张数可显示）。两件事都要说，不许静默。
                    if (e.SelectPile == "Top" || e.CardPick != "Chosen")
                        issues.Add(new("提示", $"{owner} 的「{kind.Display}」是「取全部」：**不弹选牌界面**，"
                            + "所以「选牌方式（自己选 / 随机 / 牌顶）」这一栏不生效（忽略）。"));
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」是「取全部」：卡面写「所有」、不声明张数变量，"
                        + "所以「升级增量」对这条没有作用（想升级后多拿几张就选「最多 N 张」）。"));
                }
                else if (e.CardPick != "Chosen")
                    issues.Add(new("警告", $"{owner} 的「{kind.Display}」取牌方式是「随机」，但「取几张」选了"
                        + $"「{CountModes.Zh(e.CountMode)}」—— 随机那条路没有选牌界面，会按「恰好 {Math.Max(1, (int)e.Amount)} 张」处理。"
                        + "想要「可以少选 / 任意张 / 全部」请把取牌方式改成「自己选」（「全部」不看这一栏）。"));
                else if (e.CountIsAny)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」是「取任意张」：卡面写「任意张」、不声明张数变量，"
                        + "所以「升级增量」对这条没有作用（想升级后能多选就选「最多 N 张」）。"));
            }

            // 「从哪里选牌」只有消耗 / 变化卡牌用得到；别的效果上填了会被忽略（界面里那一行也不显示）
            // 「转化为先古遗物」例外：它的「源遗物」筛选复用了 SpawnFilter 这个字段，和这两个都无关。
            // 「取几张」这一栏时别再顺手报一条无关的「从哪里选牌」（那是共用字段的默认值，不提就好）
            if (!e.UsesSelectPile && e.SelectPile != "Hand" && e.Kind != "AncientRelicSwap" && !e.HasCardScope
                && !(e.UsesCountMode && e.SelectPile == "Hand"))
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」填了「从哪里选牌 = {e.SelectPileZh}」，"
                    + "但这个选项只有「消耗卡牌 / 变化卡牌 / 升级卡牌 / 丢弃卡牌 / 给予卡牌关键词 / 复制卡牌 / 重放卡牌 / 回合结束时自动打出」用得到 —— 这条会被忽略。"));
            // 「从牌堆拿牌到手牌」：本体「搜寻 / 全息影像 / 挖掘」那种
            if (e.Kind is "TakeFromDraw" or "TakeFromDiscard")
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」会弹一个选牌界面，"
                    + $"从{(e.Kind == "TakeFromDraw" ? "抽牌堆" : "弃牌堆")}里自己挑 "
                    + (e.CountIsAll ? "**全部**（这一档不弹界面）" : e.CountIsAny ? "任意张" : $"{(e.CountMode == CountModes.UpTo ? "最多 " : "")}{Math.Max(1, (int)e.Amount)} 张")
                    + "拿到手牌"
                    + "（本体「搜寻 / 全息影像 / 挖掘」的做法，界面提示语会一起生成）；那一摞里没牌时什么都不做。"));
            if (!kind.NeedsTarget && e.TargetSide != "Self" && e.Kind != "Outbreak"
                // 「转化为先古卡 / 先古遗物」本来就是改自己的牌组 / 遗物（没有「作用对象」这一说），
                // 别报「选项将被忽略」
                && e.Kind != "AncientTranscend" && e.Kind != "AncientRelicSwap")
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」作用对象固定为自己，选项将被忽略。"));
            if (e.RepeatCount is < 1 or > 20)
                issues.Add(new("错误", $"{owner} 的重复次数 {e.RepeatCount} 超出范围（1~20）。"));
            if (e.ChanceEnabled && e.ChancePercent is < 1 or > 100)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」概率 {e.ChancePercent} 超出范围（1~100，单位是 %）。"));
            // 药水的「概率生效」是老缺口（V0.2.127 查明）：界面上药水效果一直有那个勾选框，
            // 但生成端 `EmitPotionEffect` 从来没有为药水写掷骰 —— 勾了等于没勾，而且**一句提示都没有**。
            // 这一版**不补生成**（补了会改掉老存档的生成结果：老存档里可能勾过、一直被忽略，
            // 而「老存档生成结果逐字不变」是红线），改成在这里明确报出来，不再静默。
            if (ctx == "Potion" && e.ChanceEnabled)
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」勾了「概率生效」，但药水**不支持概率**："
                    + "这一勾不会生成任何掷骰（在药水上一直是这样）。想去掉这条提示就把勾去掉；"
                    + "真要「有概率地生效」请把它做成卡牌 / 遗物上的效果。"));
            // 「不同效果的概率之间是否独立」（V0.2.132）：勾了就说清它和默认档的区别 ——
            // 只在**显式勾上**时才有这条（老存档是 false，一句都不会多）。
            if (e.ChanceEnabled && e.ChanceIndependent)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」勾了「不同效果的概率各自独立判定」："
                    + "这一条会**自己掷一次**（可能和同一张牌里别的概率效果同时中、也可能同时不中）；"
                    + "不勾（默认）时同一张牌里所有概率效果**共用一次掷骰、各占一段区间**（互斥：不可能同时生效）。"));
            // 全局（牌组）类效果：直接改玩家的牌组、跨战斗永久生效，这类改动值得先提醒一句
            if (CSharpCodeGen.IsGlobalCardEffect(e.Kind))
            {
                if (e.Kind is "AddCardGlobal" or "TransformCardGlobal" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                    issues.Add(new("警告", $"{owner} 的「{kind.Display}」没选目标卡，会按「小刀（Shiv）」处理"
                        + (e.Kind == "TransformCardGlobal" ? "（变化类留空 = 随机变化，忽略这条）" : "") + "。"));
                if (ctx == "Potion")
                    issues.Add(new("警告", $"{owner} 的「{kind.Display}」是**改牌组**的效果（永久），药水一般用完就没了，"
                        + "确认这是你想要的（建议用「任意时机」以外的药水也行，但请先备份存档试一次）。"));
            }
            if (e.Times is < 1 or > 20)
                issues.Add(new("错误", $"{owner} 的生效次数 {e.Times} 超出范围（1~20）。"));

            // 每条效果自己的条件选项
            ValidateCondition(issues, $"{owner} 第 {index} 条「{kind.Display}」", e.Condition, ctx);
            // 条件里的「数值公式」（用户要求⑦）：只做在卡牌上、公式能解析、名字对得上
            ValidateConditionFormulas(issues, $"{owner} 第 {index} 条「{kind.Display}」", e.Condition!, ctx, p);

            // 生成 / 消耗 / 变化卡牌
            if (e.Kind == "GenerateCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                issues.Add(new("警告", $"{owner} 的「生成卡牌」没填目标卡，将默认生成 Shiv（静默猎手的小刀）。"));
            // 用「目标卡」填满手牌：没选卡就退成小刀；顺便说清「张数由手牌上限决定」这件事
            if (e.Kind == "FillHand")
            {
                if (string.IsNullOrWhiteSpace(e.SpawnCardId))
                    issues.Add(new("警告", $"{owner} 的「用目标卡填满手牌」没填目标卡，将默认用小刀（Shiv）填满。"));
                issues.Add(new("提示", $"{owner} 的「用目标卡填满手牌」张数是**手牌上限 - 当前手牌数**（本体「迫降」的做法）："
                    + "手牌满了就什么都不发生；它只填空位，不会超过上限。"));
            }
            // 生成到「消耗牌堆」：说明一下它和另外三摞的区别（不参与抽牌，本场战斗结束也不会回来）
            // V0.2.249（用户要求「减少指定卡牌类别的费用（类似腐化，刺破帷幕），加在效果种类里」）：
            // 「范围」就是「哪一类牌」——没选的话生成的判据是空名单，一类牌都不会减
            if (e.Kind == "CostDown")
            {
                if (!e.HasCardScope || string.IsNullOrWhiteSpace(e.SpawnFilter))
                    issues.Add(new("警告", $"{owner} 的「减少卡牌费用」**没选范围** —— 上面那一行「范围界定」就是「哪一类牌」，"
                        + "留空 = 一张牌都不会减（这条效果等于没写）。"));
                else if (EffectCatalog.IsCardGroupFilter(e.SpawnFilter)
                    && EffectCatalog.CardGroupCards(p, e.SpawnFilter).Count == 0)
                    issues.Add(new("警告", $"{owner} 的「减少卡牌费用」选了一个**空组**（组里一张牌都没有）—— "
                        + "这一类牌一张都匹配不到，减费不会生效。到「组」页给它勾几张牌。"));
                else if (e.Amount <= 0m)
                    issues.Add(new("提示", $"{owner} 的「减少卡牌费用」数值填的是 0 → 这一类牌的**费用变成 0**"
                        + "（腐化那种）。想「减 1 费」请填 1。"));
            }
            if (e.Kind == "GenerateCard" && e.SpawnToPile == "Exhaust")
                issues.Add(new("提示", $"{owner} 的「生成卡牌」生成出来的牌会**直接进消耗牌堆**"
                    + "（不经过手牌 / 抽牌堆，所以抽不到它；用来触发「消耗时」「消耗牌堆里的牌」这类效果，"
                    + "或者只是做个计数）。"));
            // 需求 ⑨：获得药水 —— 指定档必须选一瓶，而且选的必须是**认得出类名**的药水
            //（生成的是 PotionCmd.TryToProcure<类名>，认不出来就会编译不过）。
            // 药水目录读不到时（没解包工程 / 没 pck）不报「认不出来」，只提示去配路径 —— 否则换台电脑全是误报。
            if (e.Kind == "GainPotion")
            {
                if (!e.IsPotionRandom && string.IsNullOrWhiteSpace(e.PotionId))
                    issues.Add(new("错误", $"{owner} 的「获得药水」选了「指定药水」但没选「目标药水」"
                        + "—— 请在那一栏里选一瓶（可以打字搜索中文名），或者把「取药方式」改成「随机药水」。"));
                else if (!e.IsPotionRandom && !EffectCatalog.HasPotion(e.PotionId))
                {
                    if (EffectCatalog.Potions.Count == 0)
                        issues.Add(new("提示", $"{owner} 的「获得药水」选了「{e.PotionId}」："
                            + "本机读不到本体药水目录（没配本体工程目录 / 游戏 pck），这一栏的候选是空的，"
                            + "生成前请在「路径」里指一下本体工程目录。"));
                    else
                        issues.Add(new("错误", $"{owner} 的「获得药水」选的「{e.PotionId}」不是一个本体药水类名"
                            + $"（候选共 {EffectCatalog.Potions.Count} 项）—— 请从「目标药水」那一栏里选。"));
                }
                if (e.IsPotionRandom)
                    issues.Add(new("提示", $"{owner} 的「获得药水」是**随机**一瓶："
                        + "走本体卡「炼制药水」那套（PotionFactory，按稀有度抽；战斗外自动换成非战斗池，"
                        + "战斗中治疗类药水不在随机范围内）。药水栏满时什么都不发生。"));
                else if (!string.IsNullOrWhiteSpace(e.PotionId))
                    issues.Add(new("提示", $"{owner} 的「获得药水」=「{EffectCatalog.FindPotionZh(e.PotionId)}」："
                        + "药水栏满时**不会发生任何事**（本体 TryToProcure 返回失败，不会丢掉 / 替换你已有的药水），"
                        + "卡面描述里已经写明了这一点。"));
            }
            // 张数范围：0 也允许（用户要求：所有效果种类都能填 0，0 = 不生效，由 IsInertZero 整条丢掉）
            if (e.Kind is "ExhaustCard" or "TransformCard" or "DiscardCard" && e.Amount is < 0 or > 9)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」张数 {e.Amount} 超出范围（0~9，0 = 这条效果不生效）。"));
            // 从消耗牌堆「消耗」：那摞里的牌本来就已经消耗掉了，再消耗一次没有意义（不拦，只提示）
            if (e.Kind == "ExhaustCard" && e.SelectPile == "Exhaust")
                issues.Add(new("提示", $"{owner} 的「消耗卡牌」选的是**消耗牌堆**：那摞里的牌本来就已经被消耗了，"
                    + "再「消耗」一次没有任何变化（想要「把消耗牌堆里的牌拿回来」请用「从消耗牌堆拿牌到手牌」）。"));
            if (e.Kind == "TransformCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                issues.Add(new("提示", $"{owner} 的「变化卡牌」没填目标卡 → 会变化成随机卡牌。"));
            // V0.2.259（用户报「临时护卫加了没用、还越来越多」「莫名 -2 层荆棘」）：
            // ① 本体 Single（层数恒为 1、不显示）+ Instanced 的状态（护卫 / 壁垒这类标记）：临时加减层没有意义，
            //    生成端现在按「挂上 / 摘下」处理 —— 这里说一句，免得以为「填 1 就是 1 层」；
            // ② 不允许负层数的状态做「临时减益」：目标 0 层时无事发生，还原也只会夹到 0（不会出现 -2 层）——
            //    真出现负层数只可能是别的来源（本体遗物 / 旧版 mod）。
            if (e.Kind == "TempPower" && !string.IsNullOrWhiteSpace(e.PowerId))
            {
                string pid = e.PowerId.Trim();
                bool single = EffectCatalog.FindPower(pid)?.IsSingle == true || EffectCatalog.IsSingleLayerCustomPower(pid);
                if (single)
                {
                    string zh = EffectCatalog.FindPower(pid)?.Zh ?? pid;
                    issues.Add(new("提示", $"{owner} 的「临时」状态选的是「{zh}」：这个状态本体是**单层标记**"
                        + "（层数恒为 1、界面不显示，本体每次施加还是新实例）—— 工具按「这回合挂上 / 到点整个摘掉」处理，"
                        + "填的层数只当开关用。"));
                }
                if (e.Amount < 0m && !EffectCatalog.AllowsNegativeLayers(pid))
                {
                    string zh = EffectCatalog.FindPower(pid)?.Zh ?? pid;
                    issues.Add(new("提示", $"{owner} 的「临时减益」是「{zh} -{System.Math.Abs(e.Amount):0.##}」："
                        + $"「{zh}」本体不允许负层数 —— 目标身上 0 层时这条什么都不会发生，"
                        + "到点还原也会被夹到 0（不会出现 -2 层那种负层数）。"));
                }
            }
            // V0.2.255（用户要求「让状态进组，然后在清空指定状态的效果里可以选组」）：
            // 「清除指定状态」选了一个组 = 把组里每个状态各清一次；组空 / 两边都没填都要说出来
            if (e.Kind == "ClearPowerOne")
            {
                string grpId = (e.PowerGroup ?? "").Trim();
                if (grpId.Length > 0)
                {
                    var members = CSharpCodeGen.ResolvePowerGroup(p, grpId);
                    string grpName = p.CardGroups.FirstOrDefault(g => g is not null && g.Id == grpId)?.Name ?? grpId;
                    if (members.Count == 0)
                        issues.Add(new("警告", $"{owner} 的「清除指定状态」选的组「{grpName}」里**一个状态都没有** —— "
                            + "这条效果什么都不会清（到「组」页勾「这个组里有哪些状态」）。"));
                    else
                        issues.Add(new("提示", $"{owner} 的「清除指定状态」用的是组「{grpName}」→ 会把组里这 {members.Count} 个状态**全部**清掉："
                            + string.Join("、", members.Select(m => EffectCatalog.PowerName(m, m))) + "。"
                            + (string.IsNullOrWhiteSpace(e.PowerId) ? "" : "（上面那个单独的状态栏会被忽略 —— 组优先）")));
                }
                else if (string.IsNullOrWhiteSpace(e.PowerId))
                    issues.Add(new("警告", $"{owner} 的「清除指定状态」既没选状态、也没选组 —— 这条效果什么都不做。"));
            }
            // V0.2.252（用户报「自定义状态为升级手上的状态牌不生效」）：
            // 生成端本来就会跳过不可升级的牌（`c.IsUpgradable`，和本体「武装」同一个口径），
            // 而**本体状态牌 / 诅咒牌天生不能升级**（生成时写死 `MaxUpgradeLevel => 0`，
            // 本体「粘液 / 伤口 / 灼伤 / 晕眩」全是这样）→ 这条效果对它们「什么都不做」。
            // 以前完全静默，用户只会看到「升级没生效」。这里说清楚，并指出让自己建的状态牌能升级的开关。
            if (e.Kind is "UpgradeCard" or "UpgradeCardGlobal")
            {
                string upScope = (e.SpawnFilter ?? "").Trim();
                bool statusScope = string.Equals(upScope, "Status", StringComparison.Ordinal);
                bool curseScope = string.Equals(upScope, "Curse", StringComparison.Ordinal);
                // 「只升级这张卡」直接点名了某张状态 / 诅咒牌时同样成立
                if (!statusScope && !curseScope && e.HasScopeCardId)
                {
                    var only = p.AllCards.FirstOrDefault(x => x is not null
                        && string.Equals((x.ClassName ?? "").Trim(), (e.ScopeCardId ?? "").Trim(), StringComparison.Ordinal));
                    if (only is not null) { statusScope = only.IsStatusCard; curseScope = only.IsCurseCard; }
                }
                if (statusScope || curseScope)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」范围是{(statusScope ? "状态牌" : "诅咒牌")}："
                        + "本体这一档**天生不能升级**（状态 / 诅咒牌生成时写死 `MaxUpgradeLevel => 0`，"
                        + "本体「粘液 / 伤口 / 灼伤 / 晕眩」全都一样），生成端会像本体「武装」那样跳过不可升级的牌 —— "
                        + "所以这条效果对它们**一件都不会做**（不会报错，只是没反应）。"
                        + "想让**你自己建的状态牌**能局内升级：到「诅咒 / 先古卡」页勾「可被无限升级」，"
                        + "并在它的效果里填好「升级增量」（不填的话即使升级了数值也不变）。"));
            }

            // ===== 范围限定 / 范围界定里的「自定义关键词那一组」（用户要求：把自定义关键词当卡的组用）=====
            // 「范围限定」= 生成 / 变化卡牌那一套；「范围界定」= 需求 ③ 给选牌类效果加的那一套。
            // 两套共用同一个 SpawnFilter 字段和同一份选项表，所以检查也只有一份（措辞按当前是哪一套换）。
            bool spawnScope = e.UsesSpawnOptions && e.IsSpawnRandom;
            bool cardScope = e.HasCardScope;
            if ((spawnScope || cardScope) && (EffectCatalog.IsKeywordFilter(e.SpawnFilter) || EffectCatalog.IsCardGroupFilter(e.SpawnFilter)))
            {
                bool byGroup = EffectCatalog.IsCardGroupFilter(e.SpawnFilter);
                string scopeWord = cardScope ? "范围界定" : "范围限定";
                string groupName = byGroup ? EffectCatalog.CardGroupFilterName(p, e.SpawnFilter) : EffectCatalog.KeywordFilterName(e.SpawnFilter);
                var group = byGroup ? EffectCatalog.CardGroupCards(p, e.SpawnFilter) : EffectCatalog.KeywordGroupCards(p, e.SpawnFilter);
                string what = byGroup ? $"组「{groupName}」" : $"自定义关键词「{groupName}」";
                if (group.Count == 0)
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」{scopeWord}选了{what}，"
                        + (byGroup
                            ? "但**这个组里一张牌都没有** —— 这一组是空的，效果会什么都不做。"
                              + "请到「卡牌」页（或「自定义参数 / 组」页）把牌勾进这个组，或者换一个范围限定。"
                            : "但**没有任何一张你自己的牌带这个关键词** —— 这一组是空的，效果会什么都不做。"
                              + "请到卡牌页给这一组牌勾上这个关键词，或者换一个范围限定。")));
                else
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」{scopeWord} = {what}那一组，"
                        + $"共 {group.Count} 张牌（{string.Join("、", group.Take(6).Select(c => c.Name))}"
                        + (group.Count > 6 ? " …" : "") + "）。"
                        + (byGroup
                            ? "生成时会把这些牌直接列进候选表（组是我们自己的标注，游戏里没有对应枚举，只能在生成时按配置查出来）。"
                            : "生成时会把这些牌直接列进候选表（关键词是我们自己的标注，游戏里没有对应枚举，"
                              + "只能在生成时按配置查出来）。")
                        + "注意：本体的候选过滤会排除「基础 / 先古 / 事件」稀有度的牌，所以起始的打击 / 防御不会出现在这一组里。"));
            }

            // ===== 范围界定（需求 ③：选牌类效果只对某一类牌 / 某一组牌动手）=====
            if (cardScope)
            {
                string scopeName = EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter);
                string pickWord = e.CardPick == "Chosen" ? "自己选" : "随机";
                // 「范围 + 取牌方式」：说清这一档到底会怎么挑牌（用户看着「范围界定」不一定知道随机那条路也受限）
                string how = e.Kind switch
                {
                    "ExhaustCard" => $"只会从{EffectCatalog.SelectPileZh(e.SelectPile)}里挑「{scopeName}」的牌来消耗",
                    "DiscardCard" => $"只会从{EffectCatalog.SelectPileZh(e.SelectPile)}里挑「{scopeName}」的牌丢掉",
                    "UpgradeCard" => $"只会从{EffectCatalog.SelectPileZh(e.SelectPile)}里挑「{scopeName}」的牌升级",
                    "GiveKeyword" => $"只会给{EffectCatalog.SelectPileZh(e.SelectPile)}里「{scopeName}」的牌加上这个关键词",
                    "CopyCard" => $"只会复制{EffectCatalog.SelectPileZh(e.SelectPile)}里「{scopeName}」的牌",
                    "ReplayCard" => $"只会让{EffectCatalog.SelectPileZh(e.SelectPile)}里「{scopeName}」的牌重放",
                    "TurnEndPlay" => $"只会自动打出{EffectCatalog.SelectPileZh(e.SelectPile)}里「{scopeName}」的牌",
                    "Scry" => $"只会把符合「{scopeName}」的牌列进可以丢掉的那一批（看牌堆顶 N 张之后再按范围筛）",
                    _ => $"只会从对应那摞牌里挑「{scopeName}」的牌",
                };
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」设了范围界定 = 「{scopeName}」，取牌方式 = {pickWord}："
                    + how + " —— 范围里一张都没有时这一条效果什么都不做（不会弹空选牌界面）。"));
            }
            // 填了范围界定、但这条效果种类**不支持**它（界面里那一行也不显示）→ 说一句，别让人以为配了会有用
            // （「生成 / 变化 / 全局加牌」那几种的范围限定不是这个功能，所以不在此列；
            //   「转化为先古遗物」也不是 —— 它的「源遗物」筛选复用了 SpawnFilter 这个字段）
            else if (!string.IsNullOrWhiteSpace(e.SpawnFilter) && !e.UsesSpawnOptions && e.Kind != "AncientRelicSwap")
            {
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」填了「范围界定 = {EffectCatalog.SpawnFilterZhFor(p, e.SpawnFilter)}」，"
                    + "但「范围界定」只有**选牌类**效果用得到（消耗 / 丢弃 / 升级 / 给关键词 / 复制 / 重放 / "
                    + "回合结束时自动打出 / 从三摞牌拿牌 / 预见）—— 这条会被忽略。"));
            }

            // ===== 「多选1」（候选张数 > 1）=====
            if (e.IsSpawnRandom && e.SpawnChoice > 1m)
            {
                if (e.SpawnChoice is < 2m or > 60m)
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」多选1 的候选张数 {e.SpawnChoice:0.##} 超出范围（2~60）。"));
                issues.Add(new("提示", $"{owner} 的「生成卡牌」勾了「多选1」：会先随机抽 {(int)e.SpawnChoice} 张候选"
                    + "（互不重复）弹选牌界面让你挑 1 张，**只生成选中那一张** —— 所以「数值」里的张数这时用不上"
                    + "（它就是候选张数）；候选池里的牌不够时有多少给多少。"));
                if (e.SpawnToPile == "Exhaust")
                    issues.Add(new("提示", $"{owner} 的「生成卡牌」生成到消耗牌堆 + 多选1：选中的那张会直接进消耗牌堆。"));
            }
            else if (e.Kind == "GenerateCard" && e.SpawnChoice > 1m && !e.IsSpawnRandom)
            {
                issues.Add(new("警告", $"{owner} 的「生成卡牌」取卡方式是「指定卡」，多选1 不会生效"
                    + "（指定卡的话几张候选都是同一张，没意义）—— 想用多选1 请把取卡方式改成「按范围随机」。"));
            }

            // ===== 重放卡牌（用户要求的新效果）=====
            if (e.Kind == "ReplayCard")
            {
                if (e.Amount is < 1m or > 5m)
                    issues.Add(new("错误", $"{owner} 的「重放卡牌」张数 {e.Amount:0.##} 超出范围（1~5）。"));
                if (e.ReplayTimes is < 1 or > 20)
                    issues.Add(new("错误", $"{owner} 的「重放卡牌」重放次数 {e.ReplayTimes} 超出范围（1~20）。"));
                issues.Add(new("提示", $"{owner} 的「重放卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}"
                    + $"从{EffectCatalog.SelectPileZh(e.SelectPile)}里拿 {e.Amount:0.##} 张牌，让它们本场战斗内额外打出 {Math.Max(1, e.ReplayTimes)} 次"
                    + "（本体 CardModel.BaseReplayCount：重放 N 次 = 打出去时连着打 N+1 次）。"
                    + "本体「转化 Transfigure / 隐藏宝石 HiddenGem / 剑圣 SwordSagePower」都是这一句。"
                    + "**只能给别的牌**加 —— 自己这张牌的重放次数在本体里是 OnPlay 之前就算好的，改它没有作用。"));
                // 下面这条 ctx == "Power" 分支**永远不生效**：ValidateEffects 只在卡牌 / 诅咒 / 遗物 / 药水
                // 四处被调用（ctx 传的是 Card / Curse / Relic / Potion），自定义状态触发器走的是上面那条
                // PowerTriggers 专用校验（ValidateCustomPowers → 按 kind 判）。留着是为了和另外两处同款写法一致，
                // 也让「假如以后有人把状态触发器接进这个函数」时不会漏掉这条拦住。
                // 为什么这条**仍然要拦**：重放卡牌要「现在就把 BaseReplayCount 写上去」的牌上下文，
                // CustomPowerGen.EmitEffect 对 ReplayCard 只写一行「已忽略」。
                if (ctx == "Power")
                    issues.Add(new("警告", $"{owner} 的「重放卡牌」在**自定义状态**里用不了，生成时会被忽略 —— 请把它放到卡牌 / 遗物 / 药水上。"));
            }

            // ===== 回合结束时自动打出（用户要求的新效果）=====
            if (e.Kind == "TurnEndPlay")
            {
                if (e.Amount is < 0m or > 5m)
                    issues.Add(new("错误", $"{owner} 的「回合结束时自动打出」数值 {e.Amount:0.##} 超出范围（0~5）。"));
                if (e.Amount <= 0m && !e.AmountIsXEffective)
                {
                    if (ctx is not ("Card" or "Curse"))
                        issues.Add(new("错误", $"{owner} 的「回合结束时自动打出」数值 0 = **这张牌自己**，只有卡牌才有「自己」—— "
                            + "遗物 / 药水 / 自定义状态请填 1 以上（从牌堆里选 N 张）。"));
                    else
                        issues.Add(new("提示", $"{owner} 的「回合结束时自动打出」数值 0 = **这张牌自己**："
                            + "打出它之后，本回合结束时再自动打出它一次（自动打出时目标由本体随机挑）。"));
                }
                else
                {
                    issues.Add(new("提示", $"{owner} 的「回合结束时自动打出」会先{EffectCatalog.CardPickZh(e.CardPick)}"
                        + $"从{EffectCatalog.SelectPileZh(e.SelectPile)}里选 {e.Amount:0.##} 张牌，"
                        + "**本回合结束时**逐张自动打出（先选好再打：回合结束那一刻不弹选牌界面）。"
                        + "生成的 <角色>ForgeTurnEndPlayPower 挂在 BeforeSideTurnEnd —— 那个时机在本体「结算手牌」之前，"
                        + "所以自动打出的牌会正常离开手牌。"));
                }
                // 同上：ctx == "Power" 在这条函数里到不了（状态触发器有自己的校验）。
                // 为什么这条**仍然要拦**：TurnEndPlay 要把选好的牌挂到「本回合结束那一刻」的钩子上，
                // 状态里没有「打出的那张牌」这个上下文，CustomPowerGen.EmitEffect 只写一行「已忽略」。
                if (ctx == "Power")
                    issues.Add(new("警告", $"{owner} 的「回合结束时自动打出」在**自定义状态**里用不了，生成时会被忽略 —— 请把它放到卡牌 / 遗物 / 药水上。"));
            }

            // ===== 斩杀条件（本体 Fatal）=====
            foreach (var ce in new[] { e }.Where(x => x.Condition is { Kind: "Fatal" }))
            {
                if (ctx is not ("Card" or "Curse"))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「斩杀」条件，但它只能用在**卡牌**上"
                        + "（本体 Feed / HandOfGreed / TheHunt 都是攻击牌：判的是「这一次攻击有没有把目标打死」）。"));
            }

            // ===== 卡牌时机（需求⑩）=====
            // 这一条不是「打出时算一个 bool」，而是把效果挂到本体对应时机的钩子上，所以：
            //   · 钩子里没有「打出的目标」（cardPlay.Target）→ 会解引用 null，直接拦住；
            //   · 钩子里没有打出时取的 X 值 → 拦住（不是「按 0 处理」那种能猜的事）；
            //   · 钩子里查不到「这只伙伴在不在场」的那个变量 → 伙伴类效果拦住（纯召唤仍可用，
            //     因为 EmitSummonPet 不需要已存在的伙伴）。
            if (CSharpCodeGen.IsCardTiming(e.Condition))
            {
                string timingZh = EffectCatalog.CardTimingZh(e.Condition!.Timing);
                if (ctx is not "Card")
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「卡牌时机（{timingZh}）」，但它只能用在**普通卡牌**上"
                        + "（诅咒的效果写在「回合结束时在手上」里，遗物 / 药水 / 自定义状态根本没有「这张牌被消耗」这一说）。"));
                if (CSharpCodeGen.TimingUsesPlayedTarget(e))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「卡牌时机（{timingZh}）」+「打出的目标」—— "
                        + "那个钩子里没有打出记录（cardPlay.Target 是空的）。请把这条效果改成「自己 / 全体敌人 / 随机敌人」，"
                        + "或者把它挪到不带「卡牌时机」的普通效果里。"));
                if (e.UsesX)
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「卡牌时机（{timingZh}）」而且数值填了 X —— "
                        + "X 是「打出时」消耗掉的那个量，这些钩子（被消耗 / 被丢弃 / …）里没有它。请填固定数值。"));
                if (CSharpCodeGen.NeedsExistingPet(e))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「卡牌时机（{timingZh}）」而且需要「伙伴已经在场」—— "
                        + "那个变量是在 OnPlay 里查的，钩子里拿不到。请改用「召唤伙伴」（不需要已有伙伴），"
                        + "或把这条效果挪到普通效果里。"));
                if (e.Condition.UnplayableWhenUnmet)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」的条件是「卡牌时机（{timingZh}）」："
                        + "它不是「打出的条件」，所以「不满足时这张牌打不出去」对它没有作用 —— "
                        + "效果只会在那个时机触发，牌本身照常能打出。"));
            }

            if (e.Kind == "CopyCard")
            {
                // V0.2.226（用户要求「复制效果能够复制自己」）：数值 0 = 复制**这张牌自己**（只有卡牌上有「这张牌」）
                bool copySelf = CSharpCodeGen.AmountZeroIsSelf(e);
                bool copySelfCardChain = ctx is "Card" or "Curse" or "Ancient" or "Status";
                if (!copySelf && e.Amount is < 1m or > 5m)
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」张数 {e.Amount:0.##} 超出范围（0 = 这张牌自己，1~5 = 从那一摞里挑几张）。"));
                if (e.Copies is < 1 or > 20)
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」复制的份数 {e.Copies} 超出范围（1~20）。"));
                if (copySelf && !copySelfCardChain)
                    issues.Add(new("警告", $"{owner} 的「复制卡牌」填了 0 —— 「0 = 复制这张牌自己」只有在**卡牌**上成立"
                        + "（遗物 / 药水 / 状态没有「这张牌」），所以这条效果**不会做任何事**；"
                        + "想复制某一摞里的牌，请把数值填 ≥ 1（从哪一摞挑几张）。"));
                else if (copySelf)
                    issues.Add(new("提示", $"{owner} 的「复制卡牌」：数值 0 = 把**这张牌自己**复制 {Math.Max(1, e.Copies)} 份"
                        + $"放到{e.PlaceToZh}（「从哪里选牌」那一栏在这条效果上不生效）。"));
                else
                    issues.Add(new("提示", $"{owner} 的「复制卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}"
                        + $"从{EffectCatalog.SelectPileZh(e.SelectPile)}里拿 {e.Amount:0.##} 张牌，每张复制 {Math.Max(1, e.Copies)} 份"
                        + $"放到{e.PlaceToZh}"
                        + "（本体「二刀流 DualWield」的官方做法：CreateClone + AddGeneratedCardToCombat；复制出来的牌跟着原牌的升级 / 附魔走）。"));
                if (ctx == "Relic")
                    issues.Add(new("警告", $"{owner} 的「复制卡牌」在**遗物**上用不了（遗物没有「战斗里选牌」的时刻），"
                        + "生成时会被忽略 —— 请把它放到卡牌或药水上。"));
                // 同上：ctx == "Power" 在这条函数里到不了（状态触发器有自己的校验）。
                // 为什么这条**仍然要拦**：复制卡牌要原牌在战斗牌堆里 + 一个「选哪张 / 复制几份」的选牌流程，
                // 状态钩子里没有，CustomPowerGen.EmitEffect 对 CopyCard 只写一行「已忽略」。
                if (ctx == "Power")
                    issues.Add(new("警告", $"{owner} 的「复制卡牌」在**自定义状态**里用不了，生成时会被忽略 —— 请把它放到卡牌或药水上。"));
                if (e.SelectPile == "Deck" || e.SelectPile is not ("Hand" or "Draw" or "Discard" or "Exhaust" or "Top"))
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」只能从手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶里复制"
                        + "（本体 CreateClone 要求原牌在战斗牌堆里，牌组里的牌不能直接克隆）。"));
            }
            // 丢弃卡牌：丢进弃牌堆（洗牌后会回来，不是「消耗」）；丢哪一摞行为差得挺多，说明清楚
            if (e.Kind == "DiscardCard")
            {
                issues.Add(new("提示", e.SelectPile == "Hand"
                    ? $"{owner} 的「丢弃卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}把 {(int)e.Amount} 张手牌丢进弃牌堆"
                        + "（走本体 CardCmd.Discard：奇巧这类「被丢弃时」的钩子照常触发；洗牌后会回到抽牌堆，不是消耗）。"
                    : $"{owner} 的「丢弃卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}把抽牌堆里的 {(int)e.Amount} 张牌丢进弃牌堆"
                        + "（不经手牌，所以「奇巧」这种「从手牌被丢弃时」的效果**不会**触发）。"));
            }

            // 放置卡牌（V0.2.116）：数值 0 = 这张牌自己；≥ 1 = 从某一摞挑 N 张放到「放到哪一摞」            if (e.Kind == "PlaceCard")
            {
                bool self = e.Amount <= 0m && !e.AmountIsXEffective;
                // 「卡牌」那几条链（普通卡 / 诅咒 / 先古 / 状态卡）都有「这张牌自己」；遗物 / 药水没有。
                bool cardChain = ctx is "Card" or "Curse" or "Ancient" or "Status";
                if (self && !cardChain)
                    issues.Add(new("警告", $"{owner} 的「放置卡牌」填了 0 —— 「0 = 这张牌自己」只有在**卡牌**上成立"
                        + "（遗物 / 药水 / 状态没有「这张牌」），所以这条效果**不会做任何事**；"
                        + "想把某一摞的牌挪个地方，请把数值填 ≥ 1（从哪一摞挑几张放过去）。"));
                else if (self)
                    issues.Add(new("提示", $"{owner} 的「放置卡牌」：数值 0 = 把**这张牌自己**放到{e.PlaceToZh}"
                        + "（走本体 CardPileCmd.Add(this, …, CardPilePosition)；「从哪里选牌」那一栏在这条效果上不生效）。"));
                else
                    issues.Add(new("提示", $"{owner} 的「放置卡牌」：{EffectCatalog.CardPickZh(e.CardPick)}从"
                        + $"{EffectCatalog.SelectPileZh(e.SelectPile)}拿 {e.Amount:0.##} 张牌放到{e.PlaceToZh}。"));
            }
            // 升级卡牌 / 预见：都要弹选牌界面，说明一下行为（数值 = 几张牌）
            if (e.Kind == "UpgradeCard")
            {
                issues.Add(new("提示", $"{owner} 的「升级卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}"
                    + $"把{EffectCatalog.SelectPileZh(e.SelectPile)}里的 {e.Amount:0.##} 张牌升级"
                    + "（走本体 CardCmd.Upgrade：只能升级的牌会出现在选择里；战斗里的升级只影响本场战斗，"
                    + "因为战斗牌是牌组牌的克隆）。"));
            }
            if (e.Kind == "Scry")
            {
                issues.Add(new("提示", $"{owner} 的「预见 {e.Amount:0.##}」会弹一个选牌界面，"
                    + $"让你看抽牌堆顶的 {e.Amount:0.##} 张牌，并把其中**任意张**丢进弃牌堆（可以一张都不丢）。"
                    + "本体没有「预见」这个机制，是工具按一代观者的效果自己拼的"
                    + "（抽牌堆顶 = Cards 的前 N 个：本体索引 0 就是顶；选牌走 FromSimpleGrid）。"));
            }

            // ===== 刮削（抽 N 张 → 对其中符合条件的牌做选定的操作）=====
            // 张数范围沿用效果种类表里的 0~10（上面那条通用检查已经管了，这里不重复报一次）。
            if (e.Kind == "ScrapeDraw")
            {
                // 条件 / 操作的认识与否：Id 是存档里存着的字符串，手改过 / 旧版本留下的都可能对不上，
                // 对不上就必须报错 —— 生成端对认不出来的 Id 只能退成「谁也不匹配」，效果会静静地什么都不做。
                if (!EffectCatalog.IsKnownScrapeCond(e.ScrapeCond))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」条件「{e.ScrapeCond}」认不出来："
                        + "只支持 非 0 费 / 不限 / 攻击 / 技能 / 能力 / 诅咒 / 状态 / 自定义关键词那一组 / 组"
                        + "（请在界面上重新选一次）。"));
                if (!EffectCatalog.IsKnownScrapeOp(e.ScrapeOp))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」操作「{e.ScrapeOp}」认不出来："
                        + "只支持 丢弃 / 消耗 / 升级（请在界面上重新选一次）。"));
                // 自定义关键词那一组：和「范围限定 / 范围界定」同一套检查（复用现成写法）——
                // 空组必须报错，否则这条效果会什么都不做，用户还以为配好了。
                if (EffectCatalog.IsKeywordFilter(e.ScrapeCond) || EffectCatalog.IsCardGroupFilter(e.ScrapeCond))
                {
                    bool byGroup = EffectCatalog.IsCardGroupFilter(e.ScrapeCond);
                    string groupName = byGroup ? EffectCatalog.CardGroupFilterName(p, e.ScrapeCond) : EffectCatalog.KeywordFilterName(e.ScrapeCond);
                    var group = byGroup ? EffectCatalog.CardGroupCards(p, e.ScrapeCond) : EffectCatalog.KeywordGroupCards(p, e.ScrapeCond);
                    if (group.Count == 0)
                        issues.Add(new("错误", $"{owner} 的「{kind.Display}」条件选了{(byGroup ? $"组「{groupName}」" : $"自定义关键词「{groupName}」")}，"
                            + (byGroup
                                ? "但**这个组里一张牌都没有** —— 抽完之后一张都不会动手。请把牌勾进这个组，或者换一个条件。"
                                : "但**没有任何一张你自己的牌带这个关键词** —— 这一组是空的，抽完之后一张都不会动手。"
                                  + "请到卡牌页给这一组牌勾上这个关键词，或者换一个条件。")));
                    else
                        issues.Add(new("提示", $"{owner} 的「{kind.Display}」条件 = {(byGroup ? "组" : "自定义关键词")}「{groupName}」那一组，"
                            + $"共 {group.Count} 张牌（{string.Join("、", group.Take(6).Select(c => c.Name))}"
                            + (group.Count > 6 ? " …" : "") + "）。生成时会把这些牌直接列进判据"
                            + "（这一层分组是我们自己的标注，游戏里没有对应枚举，只能在生成时按配置查出来）。"));
                }
                // 一句人话的总说明：抽 N 张 → 对其中 X 的牌执行 Y
                string condZh = EffectCatalog.ScrapeCondZhFor(p, e.ScrapeCond);
                string opZh = EffectCatalog.ScrapeOpZh(e.ScrapeOp);
                string tail = e.ScrapeOp switch
                {
                    "Exhaust" => "（本场战斗不再回来；走本体 CardCmd.Exhaust，和「消耗卡牌」同一个命令）",
                    "Upgrade" => "（走本体 CardCmd.Upgrade，和「升级卡牌」同一个命令；战斗里的升级只影响本场战斗）",
                    _ => "（丢进弃牌堆、洗牌后会回来；走本体 CardCmd.Discard，所以「奇巧」这类跟弃牌有关的钩子照常触发）",
                };
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」会抽 {e.Amount:0.##} 张牌，"
                    + $"然后对其中「{condZh}」的牌{opZh}{tail}。"
                    + "条件与操作都可以改：条件选「不限」就是抽到的每一张都动手；"
                    + "条件选自定义关键词那一组时，会在生成期把这一组牌查成显式判据。"));
            }
            // 升级卡牌（全局）：直接改牌组（永久、写进存档），和「获得 / 变化 / 删除卡牌（全局）」同一档
            if (e.Kind == "UpgradeCardGlobal")
            {
                issues.Add(new("提示", $"{owner} 的「升级卡牌（全局）」会{EffectCatalog.CardPickZh(e.CardPick)}"
                    + $"把**牌组**里的 {e.Amount:0.##} 张牌永久升级（本体「香盒 Pomander / 混沌之香」那种，"
                    + "写进存档、跨战斗生效）—— 和「升级卡牌」（只升级本场战斗的手牌 / 抽牌堆 / 弃牌堆）不是一回事。"));
            }

            if (e.Kind == "Outbreak")
            {
                issues.Add(new("提示", $"{owner} 的「毒性爆发」永远是**所有敌人**上 {e.Amount:0.##} 层中毒再立刻触发一次中毒"
                    + "（本体 Outbreak 就是全体，作用对象那一栏会被忽略）；那一下触发伤害在本体里不算「你造成的伤害」。"));
            }
            // 大限已至：只能用在卡牌上，而且必须选「指定敌人」
            if (e.Kind == "TimesUp")
            {
                if (ctx is not ("Card" or "Curse"))
                    issues.Add(new("错误", $"{owner} 的「大限已至」只能用在**卡牌**上"
                        + "（本体 Time's Up 是「指定敌人」的攻击牌，要的是「这张牌打出去时选中的那个敌人」）—— 请改用卡牌。"));
                else if (e.TargetSide != "Enemy")
                    issues.Add(new("警告", $"{owner} 的「大限已至」作用对象不是「指定敌人」："
                        + "它实际只会打**这张牌选中的那个敌人**（本体这张牌是 AnyEnemy 目标，没选到目标就不生效）—— "
                        + "建议把「作用对象」改成「指定敌人」。"));
            }

            // 需求 ⑥：这里原本有两条 ctx == "Power" 的**错误**级检查（「按范围随机 / 生成出来的卡怎么处理」与「范围界定」）。
            // 撤掉的原因：那张拦网当时是基于「状态触发器链拿不到玩家那一侧的对象 / 角色配置」，
            // 现在 CustomPowerGen.EmitEffect 的 GenerateCard 改成复用 CSharpCodeGen.EmitGenerateCard
            //（传 playerExpr: "base.Owner.Player" 与 profile），三样都能做了。
            // 顺带说明这两条本来也到不了：ValidateEffects 只被卡牌 / 诅咒 / 遗物 / 药水调用，ctx 永远不会是 "Power"
            // —— 用户真正看到的那两条报错在 ValidateCustomPowers 里（同一个文件，现已一起撤掉）。
            if (e.Kind == "BoostCard")
            {
                // 目标卡必须**认得出**（V0.2.157 修）：生成出来的 BoostPowers.cs 里写的是
                // `if (cardSource is not <目标卡类名>) return 0m;` —— 类名不存在就是 CS0103
                //「当前上下文中不存在名称 X」，构建在编译那一步才炸，报错信息完全看不出是配置问题
                //（用户实测报过：存档从别的机器拷来，卡牌清单没跟着走）。见 ValidateBoostCardTarget。
                ValidateBoostCardTarget(issues, $"{owner} 的「{kind.Display}」", p, e);
                if (e.Amount == 0m)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 0 → 挂上一个 0 层的强化状态，等于没有效果。"));
            }

            // 「目标卡」存的是**卡牌类名**，填了就必须认得出（V0.2.167 扩面）——
            // 生成出来的代码是按类名找牌的（`ModelDb.Card<类名>()` 那种），类名不存在 = 编译期 CS0246。
            // 用户实测报过：药水里的「生成卡牌」还指着一张早就改名 / 删掉的自动名（StellePotion3.cs(24,91) StelleCard5）。
            // ⚠ 只在**指定卡**那一档查：随机 / 范围限定那几档根本不看这一栏。
            if (!e.IsSpawnRandom && !string.IsNullOrWhiteSpace(e.SpawnCardId)
                && e.Kind is "GenerateCard" or "TransformCard" or "TransformCardGlobal" or "AddCardGlobal" or "FillHand")
                ValidateCardTargetExists(issues, $"{owner} 的「{kind.Display}」", p, e);

            // V0.2.221：「只对这张卡（指定）」存的也是**卡牌类名**（生成的是 `c.Id == ModelDb.Card<类名>().Id`）
            // —— 类名认不出来 = 编译期 CS0243/CS0234，整个模组装不上。
            // 以前没查这一栏：手改存档 / 那张卡被删了 / 从别的机器拷来（类名表没跟过来）都会踩到。
            if (e.HasScopeCardId)
            {
                string where = $"{owner} 的「{kind.Display}」";
                if (CardTargetProblem(p, where, e.ScopeCardId, fieldZh: "只对这张卡（指定）") is { } scopeProblem)
                    issues.Add(new(scopeProblem.Level, scopeProblem.Message));
            }

            // 药水：实际打谁由药水的「作用目标」决定，效果里的对象只影响描述，容易配出不一致
            if (potionTargetType is not null)
            {
                string? want = potionTargetType switch
                {
                    "AnyEnemy" => "Enemy",
                    "AllEnemies" => "AllEnemies",
                    _ => "Self",
                };
                bool matters = e.Kind is "Damage" or "ApplyPower" or "TempPower";
                if (matters && e.TargetSide != want)
                    issues.Add(new("警告", $"{owner} 的「{kind.Display}」效果对象是「{e.TargetSide}」，"
                        + $"但药水作用目标是「{potionTargetType}」→ 实际执行按药水目标，描述也已按药水目标生成。"));
            }
        }
    }

    /// <summary>
    /// 这条效果的数值带什么单位（V0.2.141「升级后为 0」那条警告用）。
    ///
    /// <para>为什么要它：警告的原文要写成「没升级的那张才是 -2 **层**」这种能直接读懂的句子。
    /// 只给**真的按层数算**的种类写「层」（施加状态 / 临时增益 / 翻倍层数 / 清除层数）；
    /// 其它种类（伤害 / 格挡 / 抽牌…）不硬编单位（写了「-2 点伤害」这种要额外一套映射，容易漂掉，
    /// 而那句话的主语已经由「{owner} 的「{kind.Display}」」交代清楚了）。</para>
    /// </summary>
    private static string EffectUnitZh(string kind) => kind switch
    {
        "ApplyPower" or "TempPower" or "DoublePower" or "Expose" or "ClearPowerOne" or "ClearDebuffs" => " 层",
        _ => "",
    };

    /// <summary>
    /// 锻造节点图（那块布）的校验：把 <see cref="ForgeGraphGen.Issues"/> 逐条转成校验结果。
    ///
    /// 为什么要接进来：`ForgeGraphGen.Problems`（同一份逻辑）以前是**死代码**，没人调 ——
    /// 于是整块布的图问题**一个都不会告诉用户**。最要命的一条是「【效果】选了节点图没接上的种类」：
    /// 生成、编译、进游戏全都不报错，只是什么都不做 —— 别人实测报的「给召唤物 buff 不生效」就是这个。
    /// </summary>
    /// <summary>
    /// 作用对象 = 「全体单位」（V0.2.132 新增的第 7 档）时的校验。
    ///
    /// <para><b>为什么必须有这一条</b>：<c>CSharpCodeGen.EmitPerCreature</c> / <c>EmitAttack</c> /
    /// <c>EmitStun</c> 以及各条目标分支的 <c>default</c> 都是「当成自己」—— 用没有实现这一档的
    /// 效果种类选了「全体单位」，生成出来的就是「对自己做这件事」，而生成 / 编译 / 进游戏全都不报错。
    /// 所以这里按 <see cref="EffectCatalog.SupportsAllUnits"/>（= 生成端真的写了分支的那几个种类）
    /// 直接报错拦住，并把支持名单写清楚（不静默）。</para>
    /// </summary>
    private static void ValidateAllUnitsTarget(List<ValidationIssue> issues, string owner, int index,
        EffectKindOption kind, EffectSpec e, string ctx)
    {
        if (!string.Equals(e.TargetSide, "AllUnits", StringComparison.Ordinal)) return;
        // 自定义状态那条链支持的更少（它的「格挡 / 回血 / 失去生命 / 最大生命」是手写的、只打自己）
        bool powerChain = ctx == "Power";
        if (powerChain ? EffectCatalog.SupportsAllUnitsOnPower(e.Kind) : EffectCatalog.SupportsAllUnits(e.Kind)) return;
        // V0.2.200：「牺牲伙伴 → 造成伤害」这几档目标都是 EmitPerCreature 展开的（卡牌 / 状态 / 锻造
        // 用的是同一段），所以「全体单位」对它算支持。收益是格挡 / 状态时不读作用对象，这里不放行。
        if (e.Kind == "PetSacrifice" && e.PetSacrificeGain == "Damage") return;
        string support = powerChain
            ? "造成伤害 / 施加增益减益 / 临时增益 / 击晕 / 清除指定状态 / 清除所有减益"
            : "造成伤害 / 获得格挡 / 回复生命 / 失去生命 / 最大生命 / 施加增益减益 / 临时增益 / 击晕";
        issues.Add(new("错误", $"{owner} 第 {index} 条「{kind.Display}」的作用对象选了「全体单位」，"
            + "但这个效果种类还没有实现「全体单位」这一档（生成端会把它当成「对自己」）—— "
            + $"目前支持「全体单位」的是：{support}。"
            + "请改回「自己 / 全部敌人」之类的老档位，或者换一个上面列的种类。"
            + (ctx == "Potion"
                ? "（另外：药水实际打谁由药水自己的「作用目标」决定，效果里的作用对象只影响卡面描述。）"
                : "")));
    }

    private static void ValidateForgeGraph(List<ValidationIssue> issues, string owner, ForgeGraphSpec? g,
        CharacterProfile p, string scope, string? cardTargetType = null)
    {
        foreach (var (level, message) in ForgeGraphGen.Issues(g, p, scope, cardTargetType))
            issues.Add(new(level, $"{owner}：{message}"));
        ForgeGraphConditionHints(issues, owner, g, p, scope);
    }

    /// <summary>
    /// V0.2.246（用户报「形态生效、效果没生效」：「多效卡识别不了形态一」）——
    /// 锻造图上会**静默判错**的写法，给「提示」说清（不拦生成）：
    ///
    /// <para>【状态】/【状态列表】节点**没接【对象】**时，卡牌 / 遗物 / 药水的默认归属是
    /// <c>__fgTarget</c>（打出时选中的**那个敌人**），不是自己（见 <c>ForgeGraphGen.ValueDefaultUnit</c>）。
    /// 用户要判「我身上有没有某个形态」时会一直判不到 —— 图上看不出差别（就是没连线的样子）。</para>
    ///
    /// <para>⚠ 「只叠一层的状态 + 等于 1 层」那个坑**不在这里报**：那种情况生成端已经按语义夹成 0/1
    /// （见 <c>ForgeGraphGen.IsSingleLayerCustomPower</c>），「等于 1」现在是对的，不必打扰用户。</para>
    /// </summary>
    private static void ForgeGraphConditionHints(List<ValidationIssue> issues, string owner, ForgeGraphSpec? g,
        CharacterProfile? p, string scope)
    {
        if (g is null || g.IsEmpty) return;
        bool isCardLike = scope is "Card" or "Relic" or "Potion";
        if (!isCardLike) return;
        foreach (var node in g.Nodes)
        {
            if (node is null) continue;
            if (node.Kind is not ("Power" or ForgeGraphCatalog.PowerListKind)) continue;
            bool wired = g.Links.Any(l => string.Equals(l.To, node.Id, StringComparison.Ordinal) && l.ToPort == 0);
            if (wired) continue;
            issues.Add(new("提示", $"{owner} 的【状态】条件「{PowerDisplayOf(p, node)}」"
                + "**左边没接【对象】**：这一档的默认归属是「打出时选中的那个敌人」身上的状态，**不是自己**。"
                + "想判「我身上有没有它」（形态 / 姿态 / 增益这类）请拖一块【对象】（选「自己」）接到它左边；"
                + "确实是判敌人身上的就不用管。"));
        }
    }

    /// <summary>用**存档里**的自定义状态表取显示名（不依赖 <see cref="EffectCatalog"/> 的登记顺序）。</summary>
    private static string PowerDisplayOf(CharacterProfile? p, ForgeNodeSpec? node)
    {
        string pid = (node?.Get("PowerId") ?? "").Trim();
        if (pid.Length == 0) return node?.Kind == ForgeGraphCatalog.PowerListKind ? "（状态列表）" : "（未选状态）";
        if (p is not null)
            for (int i = 0; i < p.CustomPowers.Count; i++)
            {
                var cp = p.CustomPowers[i];
                if (cp is null) continue;
                if (string.Equals(CustomPowerGen.ClassNameOf(p, cp, i), pid, StringComparison.Ordinal))
                    return string.IsNullOrWhiteSpace(cp.Name) ? pid : cp.Name;
            }
        return EffectCatalog.PowerName(pid, pid);
    }

    /// <summary>
    /// V0.2.194：**自定义状态那块布**上「做不了 / 会静默不生效」的几条（用户要求「不要静默」）。
    ///
    /// 状态这条链（<see cref="CustomPowerGen"/>）和卡牌那条链的能力边界不一样，这里把差异点报出来：
    ///   · 一块【触发时机】都没有 → 这块布一个钩子都不生成（状态挂上去什么都不做）；
    ///   · 【触发时机】= 「获得时」→ 那是遗物专属的（状态是被施加的，没有「拿到的那一刻」），
    ///     生成端那一档**不写方法**（只留一句注释）；
    ///   · 【效果】= 「获得卡牌奖励」→ 那几句要 <c>base.Owner.Character.CardPool</c>（状态上没有这个），
    ///     生成出来编译不过；
    ///   · 布上摆着【能量】模块 → 状态没有费用这一说，那一块不参与生成（状态值是「卡牌自己的费用」）。
    /// </summary>
    private static void ValidatePowerGraphIssues(List<ValidationIssue> issues, string who, CustomPowerSpec cp)
    {
        var g = cp.ForgeGraph;
        if (g is null || g.IsEmpty) return;
        var triggers = g.TriggerNodes;
        if (triggers.Count == 0)
            issues.Add(new("错误", $"{who} 的锻造节点图上**一块【触发时机】都没有** —— "
                + "触发时机是整张图的起点，没有它这一版不会生成任何钩子（状态挂上去什么都不会做）。"
                + "请在布上「添加」一块【触发时机】。"));
        foreach (var trigger in triggers)
        {
            var timing = ForgeTimings.Find(trigger.Get("Timing"), ForgeTimings.PowerScope);
            if (timing is null) continue;
            if (string.Equals(timing.Id, "Obtained", StringComparison.Ordinal))
                issues.Add(new("错误", $"{who} 的锻造节点图上用了【触发时机】=「获得时」——"
                    + "那是**遗物**专属的（状态是被施加的，没有「拿到的那一刻」），状态上不会生成任何东西。"
                    + "请换成别的触发时机（想「刚被施加时做事」用「玩家回合开始时」+【条件】判层数）。"));
            // 同一条链上有没有「获得卡牌奖励」（状态那块布上编译不过，见上面的说明）
            var (chain, _) = ForgeGraphGen.WalkChain(g, trigger);
            if (chain.Any(n => string.Equals(n.Get("Effect"), "CardReward", StringComparison.Ordinal)))
                issues.Add(new("错误", $"{who} 的锻造节点图上「{timing.Display}」那一条链里放了【获得卡牌奖励】——"
                    + "那个效果生成出来要用 <c>base.Owner.Character.CardPool</c>（本体挂奖励的写法），"
                    + "而**状态**（PowerModel）上没有这个属性 ⇒ 生成出来的工程编译不过。"
                    + "想给卡牌奖励请写在卡牌 / 遗物那条链上。"));
        }
        if (g.Nodes.Any(n => n is not null && string.Equals(n.Kind, "Energy", StringComparison.Ordinal)))
            issues.Add(new("警告", $"{who} 的锻造节点图上有一块【能量】模块 —— 状态没有「费用」这一说，"
                + "那一块不参与生成（费用只在卡牌上有效）。留着不影响生成，删掉可以让布干净一点。"));
    }

    /// <summary>
    /// 「斩杀」条件只能挂在**打出时**那一段效果上（V0.2.169）。
    ///
    /// 它判的是「上一条攻击有没有把目标打死」，生成出来引用的是卡牌 OnPlay 开头声明的两个局部变量
    /// <c>__lastAttack</c> / <c>__fatalOk</c>（见 CSharpCodeGen 里 usesFatal 那一段）。
    /// 有两条效果**不在 OnPlay 里发射**、而是各自写进别的钩子：
    ///   · 「抽到这张牌时它本场战斗的费用 -N」→ <c>AfterCardDrawn</c>；
    ///   · 「将此牌放回手牌」→ <c>AfterCardPlayedLate</c>。
    /// 那两处既没有声明、也没有「上一条攻击」（captureFatal 是 false），所以带「斩杀」条件时生成出来就是
    /// <c>if (__lastAttack is not null &amp;&amp; __fatalOk &amp;&amp; …)</c> 引用两个不存在的名字 ⇒
    /// <c>CS0103</c>，整个模组编译不过（用户实测报过 <c>AinardiCard13.cs(28,13) CS0103</c>）。
    ///
    /// 语义上也没救 —— 那两个钩子里根本不存在「这张牌刚打出的那一次攻击」，所以这里**报错**让他挪走，
    /// 而不是悄悄生成一个永远不触发的条件（那正是本工具最忌讳的「静默不生效」）。
    /// </summary>
    private static void ValidateFatalPlacement(List<ValidationIssue> issues, CardSpec c)
    {
        foreach (var e in c.Effects)
        {
            if (e.Condition is not { Kind: "Fatal" }) continue;
            string? where = CSharpCodeGen.IsSelfDrawCost(e)
                    ? "「抽到这张牌时它本场战斗的费用 -N」的效果（实现在 AfterCardDrawn 钩子里）"
                : CSharpCodeGen.IsSelfReturn(e)
                    ? "「将此牌放回手牌」的效果（实现在 AfterCardPlayedLate 钩子里）"
                : null;
            if (where is null) continue;
            issues.Add(new("错误", $"卡牌「{c.Name}」这条 {where} 用了「斩杀」条件 —— "
                + "那个条件判的是**上一条攻击**的结果（生成出来引用 __lastAttack / __fatalOk），"
                + "而那两个变量只在「打出时」那一段里有声明，这个钩子里既没有声明、也不会有上一条攻击 ⇒ "
                + "生成出来是 CS0103（整个模组编译不过）。请把「斩杀」挪到一条**打出时**的『造成伤害』效果"
                + "后面（本体「狂暴 / 贪婪之手」都是这么写的），或者把这条效果的条件换成别的。"));
        }
    }

    /// <summary>

    /// 为什么必须有这一条（用户实测报的）：「设 A 为敌人的虚弱，伤害写 <c>A+3</c>」，
    /// 但这条伤害的「作用对象」是**全体敌人** —— 全体敌人的牌打出时**不选目标**，
    /// <c>cardPlay.Target</c> 是 null，于是生成出来的
    /// <c>PowerAmount(cardPlay?.Target, "WeakPower")</c> 恒等于 0，实际伤害就是 3。
    /// 用户看到的是「公式填了却没用」，而且生成、编译、进游戏全都不报错 —— 最难查的一类问题。
    ///
    /// 判据：只有「指定敌人」这一档需要牌上真的有选中的目标
    /// （其余「随机敌人 / 全部敌人 / 全体友方 / 召唤物 / 自己」生成的都是一个**固定表达式**，
    ///  不依赖 cardPlay.Target，任何牌上都有值）。
    /// </summary>
    private static void ValidateFormulaTargets(List<ValidationIssue> issues, CardSpec c, CharacterProfile p)
    {
        string targetType = CSharpCodeGen.TargetTypeOf(c);
        if (targetType == "AnyEnemy") return;                 // 这张牌会选目标：一切正常

        string targetZh = targetType switch
        {
            "AllEnemies" => "对所有敌人",
            "RandomEnemy" => "对随机敌人",
            _ => "对自己",
        };
        foreach (var e in c.Effects)
        {
            if (e is null || !e.UsesAmountFormula) continue;
            var root = ForgeFormula.TryParse(e.AmountFormula, out _);
            var names = ForgeFormula.Identifiers(root);
            if (names.Count == 0) continue;
            foreach (var param in p.CustomParams)
            {
                if (param is null) continue;
                string name = ParamNames.Normalize(param.Name);
                if (!names.Contains(name, StringComparer.Ordinal)) continue;
                if (ParamTargets.Normalize(param.Target) != ParamTargets.ChosenEnemy) continue;
                issues.Add(new("错误", $"卡牌「{c.Name}」的「{EffectCatalog.FindKind(e.Kind).Display}」"
                    + $"数值公式（{e.AmountFormulaText}）里用了参数 {name}=，但它的「从谁身上取」是**指定敌人**，"
                    + $"而这张牌是「{targetZh}」—— 打出这张牌时**没有**「选中的那个敌人」，所以 {name} 永远是 0，"
                    + "公式等于白写。二选一："
                    + $"① 把这条效果的「作用对象」改成「指定敌人（单体）」；"
                    + $"② 到「自定义参数 / 组」页把 {name}= 的「从谁身上取」改成「全部敌人」（取第一个活着的敌人）"
                    + "或「随机敌人」—— 注意公式是**整张牌只算一次**的，所以那样所有敌人吃到的是同一个数字。"));
            }
        }
    }

    /// <summary>

    /// 三件事：种类支不支持、上下文（必须卡牌）、公式本身能不能解析 + 里面的参数名存不存在。
    /// 为什么种类和上下文都要拦：写成非空却没人读它 = **静默不生效**，
    /// 用户看到的是「公式填了，游戏里伤害还是那个固定数值」（最难查的一类问题）。
    /// </summary>
    private static void ValidateAmountFormula(List<ValidationIssue> issues, string owner, EffectSpec e,
        EffectKindOption kind, string ctx, CharacterProfile? p)
    {
        string kindZh = kind.Display;
        string formula = ForgeFormula.DisplayText(e.AmountFormula);
        if (!EffectSpec.FormulaSupports(e.Kind))
        {
            // V0.2.131：卡牌链上「数值（公式）」框**所有种类**都摆出来了 —— 非那 5 种只认光秃秃的 X
            //（= 原来的「数值 = X」勾选框，用户要求去掉那个勾选框、把 X 搬进公式框）。
            if (ctx == "Card" && Generation.ForgeNumbers.IsBareX(e.AmountFormula))
                return;
            issues.Add(new("错误", $"{owner} 的「{kindZh}」不支持「数值公式」（现在填的是 {formula}）："
                + "这个效果的数值必须在**生成的时候**就定下来 —— 抽几张 / 生成几张 / 消耗几张这类要提前分配"
                + "动态变量、算好选牌上限，塞不进运行时表达式。"
                + "支持完整公式的只有这 5 种：造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益"
                + "（**卡牌 / 遗物 / 药水 / 自定义状态的触发器上都能写**，V0.2.164 起）。"
                + "卡牌上这一栏还有另外两种情况能用：① 一个**纯数字 / 能折成常数的式子**（例：1、2+3）—— 工具会把它"
                + "当成左边的固定数值（打开存档 / 生成前自动折好，不用你手填两处）；② 光秃秃写一个 X"
                + "（= 老勾选框「数值 = X」，等于打出这张牌那一刻消耗掉的能量 —— 费用框里也要填 X）。"
                + "请把「数值公式」改成一个固定数字（或纯数字式子），或者直接把它填在左边的固定数值框里。"));
            return;
        }
        if (e.AmountIsXEffective)
            issues.Add(new("错误", $"{owner} 的「{kindZh}」同时填了「数值公式」（{formula}）又勾了「数值 = X」——"
                + "两个都是「改用算出来的值」，只能留一个（X 只能引用一个数，公式里也能用 X，所以留公式就够了）。"));
        var root = ForgeFormula.TryParse(e.AmountFormula, out string err);
        if (root is null)
        {
            issues.Add(new("错误", $"{owner} 的「{kindZh}」数值公式看不懂：{err}"));
            return;
        }
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (p is not null)
            foreach (var cp in p.CustomParams)
                if (cp is not null) known.Add(ParamNames.Normalize(cp.Name));
        var knownIds = ForgeFormula.Identifiers(root).ToList();
        foreach (string id in knownIds)
        {
            if (id is "X" or "Y") continue;
            // 内置只读量（用户要求：参数监听）：Turn / DiscardedThisTurn / PowersOnMe… 直接写就能用
            if (FormulaBuiltins.Find(id) is not null) continue;
            if (known.Contains(id)) continue;
            issues.Add(new("错误", $"{owner} 的「{kindZh}」数值公式里的「{id}」不是一个参数："
                + $"到「自定义参数 / 组」页加一个 {id}=（并选好它从哪来），或者把公式改掉。"
                + "X = 打出这张牌那一刻的当前能量，Y = 当前额外资源量，这两个不用定义；"
                + $"内置只读量（不用定义、每次求值现算）：{FormulaBuiltins.HintList}。"));
        }
        // V0.2.164：公式铺到遗物 / 药水 / 自定义状态之后，这两条**口径**要说清 ——
        // 不说的话，用户会以为 X / Y 还是「这张牌花掉的那点」、「指定敌人」还是玩家选中的那个。
        if (ctx is "Relic" or "Potion" or "Power")
        {
            string where = ctx switch { "Relic" => "遗物", "Potion" => "药水", _ => "自定义状态" };
            if (knownIds.Contains("X") || knownIds.Contains("Y"))
                issues.Add(new("提示", $"{owner} 的「{kindZh}」公式里用了 {(knownIds.Contains("X") ? "X" : "Y")}："
                    + $"{where}上没有「打出这张牌」那一刻，所以这里的 X / Y 读的是**当前**能量 / 额外资源量"
                    + "（不是「这张牌花掉的那点」；想避开歧义可以改用 MyEnergy / MyStars 这类实时读数）。"));
            if (p is not null)
            {
                var chosen = knownIds.Where(id => p.CustomParams.Any(cp => cp is not null
                        && string.Equals(ParamNames.Normalize(cp.Name), id, StringComparison.Ordinal)
                        && ParamTargets.Normalize(cp.Target) == ParamTargets.ChosenEnemy)).ToList();
                if (chosen.Count > 0)
                    issues.Add(new("提示", $"{owner} 的「{kindZh}」公式里用了「指定敌人」来源的参数"
                        + $"（{string.Join(" / ", chosen)}）：{where}上没有「玩家选中的目标」，"
                        + "这里取的是**可打的第一个敌人**（和这些链上「作用对象 = 单体敌人」的执行口径一致）。"));
            }
        }
        if (e.UpgradeAmount != 0m)
            issues.Add(new("提示", $"{owner} 的「{kindZh}」填了「数值公式」（{formula}）："
                + $"左边的固定数值（{e.Amount:0.##}）和「升级增量」（{e.UpgradeAmount:0.##}）都不再决定强度了 ——"
                + "卡面显示的是公式本身，升级也不会让它变大。想让升级也有用，就把升级增量去掉。"));
        if (e.Amount < 0m)
            issues.Add(new("提示", $"{owner} 的「{kindZh}」填了「数值公式」（{formula}），但左边的固定数值是 {e.Amount:0.##}："
                + "公式在求值之前生成端不知道正负，所以这条一律按「正面效果」生成"
                + "（伤害 / 格挡 / 回复 / 施加状态都不会反向）。想让公式算出负数时也照常扣，请改用「施加增益减益」"
                + "填负固定值那种写法。"));
    }

    /// <summary>
    /// 次数类公式（用户要求⑦：所有能填数值的地方都能写公式，次数类优先）的校验：
    /// 只能在卡牌上、必须和「= X / = 层数」互斥、公式本身要解析得出来、里面的名字要么是自定义参数、
    /// 要么是内置只读量。
    /// </summary>
    /// <summary>
    /// 「负数 = 直接施加负值」还是「负数 = 只扣已有层数」那一段说明（V0.2.239）。
    ///
    /// 为什么要单独一份：自定义状态也能勾「允许负层数」了，文案里再一律说「本体允许 / 本体不允许」就是错的
    /// （用户造的状态没有「本体」）。三处提示（状态链的施加 / 临时增益、以及那条通用对照）共用它。
    /// </summary>
    private static string NegativeLayerNote(string? powerId, decimal faceAmount)
    {
        string name = EffectCatalog.PowerName(powerId);
        bool custom = EffectCatalog.IsCustomPower(powerId);
        string n = Math.Abs(faceAmount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (EffectCatalog.AllowsNegativeLayers(powerId))
            return custom
                ? $"（你给这个自定义状态勾了「允许负层数」，所以**直接施加 -{n} 层**，目标身上本来 0 层也能扣成负的"
                  + " —— 和本体「尖啸」扣敌人力量同一套写法。）"
                : $"（{name}本体允许负层数，所以**照本体「尖啸」那套直接施加 -{n} 层**，目标身上本来 0 层也能扣成负的。）";
        return custom
            ? "（这个自定义状态没有勾「允许负层数」，所以是从目标**身上已有的层数**里扣；扣到 0 就把这个状态去掉，"
              + "目标身上没有它时什么都不做 —— 想让层数能到负值，请到「自定义状态」页勾上「允许负层数」。）"
            : $"（{name}本体不允许负层数，所以是从目标**身上已有的层数**里扣（先找那份状态实例再扣）；"
              + "目标身上没有这个状态时什么都不做 —— 层数不能凭空变成负的。）";
    }

    private static void ValidateCountFormulas(List<ValidationIssue> issues, string owner, EffectSpec e,
        string ctx, CharacterProfile? p)
    {
        if (!e.UsesAnyCountFormula) return;
        // 卡牌链之外：**只有「生效次数」**能写公式（V0.2.238，用户问「生效次数可以填写公式吗」）。
        // 遗物 / 药水走 EmitRepeated 的 inlineProfile 内联求值、自定义状态走 CustomPowerGen 的内联求值，
        // 都不再需要卡牌那套前置局部变量（__fgPlayer / __pM / __x …）。
        // 其余几栏（命中次数 / 复制份数 / 重放次数 / 概率）这几条链的生成端**根本不读** ——
        // 留着就是「填了不生效」，照旧拦住并说清（绝不静默忽略）。
        if (ctx is "Relic" or "Potion" or "Curse" or "Power")
        {
            string where = ctx switch { "Relic" => "遗物", "Potion" => "药水", "Power" => "自定义状态的触发时机", _ => "诅咒" };
            var blocked = EffectSpec.CountFormulaKeys
                .Where(k => k != "Times" && !string.IsNullOrWhiteSpace(e.CountFormula(k)))
                .ToList();
            if (blocked.Count > 0)
                issues.Add(new("错误", $"{owner} 的「{string.Join(" / ", blocked.Select(EffectSpec.CountFormulaZh))}」写了公式："
                    + $"{where}这条链只认「生效次数」的公式（V0.2.238 起）—— "
                    + "「命中次数 / 复制份数 / 重放次数 / 概率」的公式目前只做在卡牌和锻造【效果】里。"
                    + "请清空这几栏的公式，改用左边的固定值。"));
        }
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (p is not null)
            foreach (var cp in p.CustomParams)
                if (cp is not null) known.Add(ParamNames.Normalize(cp.Name));
        foreach (string key in EffectSpec.CountFormulaKeys)
        {
            string raw = e.CountFormula(key);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string zh = EffectSpec.CountFormulaZh(key);
            // 和「= X / = 层数」互斥（两者都是「改用算出来的值」）
            bool clash = key switch
            {
                "Times" => e.TimesIsX || e.TimesIsStack,
                "Repeat" => e.RepeatIsX || e.RepeatIsStack,
                _ => false,
            };
            if (clash)
                issues.Add(new("错误", $"{owner} 的「{zh}」同时写了公式（{ForgeFormula.DisplayText(raw)}）"
                    + "又勾了「= X / = 层数」—— 两个都是「改用算出来的值」，只能留一个。"));
            var root = ForgeFormula.TryParse(raw, out string err);
            if (root is null)
            {
                issues.Add(new("错误", $"{owner} 的「{zh}」次数公式看不懂：{err}"));
                continue;
            }
            foreach (string id in ForgeFormula.Identifiers(root))
            {
                if (id is "X" or "Y") continue;
                if (FormulaBuiltins.Find(id) is not null) continue;
                if (known.Contains(id)) continue;
                issues.Add(new("错误", $"{owner} 的「{zh}」次数公式里的「{id}」不是一个参数："
                    + $"到「自定义参数 / 组」页加一个 {id}=（并选好它从哪来），或者把公式改掉。"
                    + "X = 打出这张牌那一刻的当前能量，Y = 当前额外资源量，这两个不用定义；"
                    + $"内置只读量（不用定义、每次求值现算）：{FormulaBuiltins.HintList}。"));
            }
            // 概率公式：语义和固定值那条不同 —— 写完要说清（不然用户以为还是「同组共用一次掷骰、各占区间」）
            if (key == "Chance" && p is not null)
            {
                var sameOwner = p.AllCards.SelectMany(x => x.Effects)
                    .Where(x => !ReferenceEquals(x, e) && x.ChanceEnabled && x.ChancePercent > 0m).ToList();
                if (sameOwner.Count > 0)
                    issues.Add(new("提示", $"{owner} 的「概率」写了公式：这一条会**自己独立判一次**，"
                        + "不再和同一张牌里其它「概率生效」的效果共用一次掷骰、各占一段区间"
                        + "（那段区间是生成时按固定百分比算好的，公式算不出来）。"
                        + "想要原来那种「互斥」的观感，请把这几条都改成固定百分比。"));
            }
        }
    }

    /// <summary>
    /// 条件里的「数值公式」（用户要求⑦）：只能在卡牌上、公式要解析得出来、名字要么是自定义参数要么是内置只读量。
    /// 另外：写了公式时「填负数 = 反向」不生效（公式算出来的正负生成时不知道），要说清楚。
    /// </summary>
    private static void ValidateConditionFormulas(List<ValidationIssue> issues, string owner, ConditionSpec c,
        string ctx, CharacterProfile? p)
    {
        if (c is null || !c.UsesAnyFormula) return;
        if (ctx is "Relic" or "Potion" or "Curse" or "Power")
        {
            string where = ctx switch { "Relic" => "遗物", "Potion" => "药水", "Power" => "自定义状态的触发时机", _ => "诅咒" };
            issues.Add(new("错误", $"{owner} 的条件写了「数值公式」：这一版只做在**卡牌**上 —— "
                + $"{where}的代码链里没有那两个「自己 / 玩家」的局部变量。请清空公式，改用固定数值。"));
            return;
        }
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (p is not null)
            foreach (var cp in p.CustomParams)
                if (cp is not null) known.Add(ParamNames.Normalize(cp.Name));
        foreach (var (which, raw) in new[] { ("数值 N", c.AmountFormula), ("数值 M", c.Amount2Formula) })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var root = ForgeFormula.TryParse(raw, out string err);
            if (root is null)
            {
                issues.Add(new("错误", $"{owner} 的条件「{which}」的公式看不懂：{err}"));
                continue;
            }
            foreach (string id in ForgeFormula.Identifiers(root))
            {
                if (id is "X" or "Y") continue;
                if (FormulaBuiltins.Find(id) is not null) continue;
                if (known.Contains(id)) continue;
                issues.Add(new("错误", $"{owner} 的条件「{which}」公式里的「{id}」不是一个参数："
                    + $"到「自定义参数 / 组」页加一个 {id}=，或者把公式改掉。"
                    + $"内置只读量（不用定义）：{FormulaBuiltins.HintList}。"));
            }
        }
        if (c.IsInverted || c.IsOutsideRange)
            issues.Add(new("提示", $"{owner} 的条件既填了负数（「填负数 = 反向 / 区间外」）又写了「数值公式」："
                + "公式算出来的正负生成时不知道，所以**这一版一律按「正面」判断** —— 固定数值那个负号不生效。"
                + "要反向请把负号写进公式本身（例：`0 - Turn`），或者清空公式用固定负值。"));
    }

    /// <summary>
    /// 「播放音频文件（我上传的）」的校验（V0.2.155）：必须选一条上传的音频，
    /// 而且那条记录的文件要真的在本机、扩展名得是 Godot 认的（.ogg / .mp3 / .wav）。
    /// </summary>
    private static void ValidatePlaySoundFile(List<ValidationIssue> issues, string owner, EffectSpec e, CharacterProfile? p)
    {
        string key = (e.Sfx ?? "").Trim();
        if (key.Length == 0)
        {
            issues.Add(new("错误", $"{owner} 的「播放音频文件」还没选音频 ——"
                + "先到「美术资源」页的「音效 / 音频」区点「上传音频…」，再回到这里那一栏选中它。"));
            return;
        }
        AudioAssetSpec? hit = p?.AudioAssets?.FirstOrDefault(a => a is not null
            && string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
        {
            issues.Add(new("错误", $"{owner} 的「播放音频文件」用的是「{key}」，"
                + "但「美术资源」页里已经没有这一条了（被删了？）—— 重新选一个，或者把它重新上传。"));
            return;
        }
        if (string.IsNullOrWhiteSpace(hit.File) || !File.Exists(hit.File))
        {
            issues.Add(new("错误", $"{owner} 的「播放音频文件」用的「{hit.Name}」在本机找不到了："
                + $"{hit.File} —— 回到「美术资源」页重新选一下这个文件（生成时会跳过它）。"));
        }
        else if (!AudioAssetSpec.IsSupportedAudio(hit.File))
        {
            issues.Add(new("错误", $"{owner} 的「播放音频文件」用的「{hit.Name}」扩展名是 "
                + $"{Path.GetExtension(hit.File)}，Godot 不认 —— 请换成 .ogg / .mp3 / .wav。"));
        }
        if (e.Amount > 200m)
            issues.Add(new("警告", $"{owner} 的「播放音频文件」音量填的是 {e.Amount:0.##}%（超过 200%）——"
                + "会非常吵，而且可能爆音，建议 ≤ 200。"));
        // 同一个文件被多条目共用是允许的（会复制成同一个 audio/<Key>），但**显示名撞了**就会撞文件名 ——
        // 生成时后复制的那条会覆盖前一条（内容不同就播错声音了），所以这里说清楚。
        if (p?.AudioAssets is { } all)
        {
            int sameKey = all.Count(a => a is not null && string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
            if (sameKey > 1)
                issues.Add(new("警告", $"{owner} 的「播放音频文件」用的「{key}」在「美术资源」页里有 {sameKey} 条"
                    + "同名（生成出来的文件名一样，会互相覆盖）—— 把它们的名字改得不一样。"));
        }
    }

    /// <summary>
    /// 公式里是不是**真的用到了**这个单字母读数（X / Y，见 V0.2.158 那两条警告）。
    /// 按**标识符边界**匹配：<c>Y</c> 要单独出现才算 —— <c>YSpent</c> / <c>Y2</c> 里的 Y 不算
    /// （用正则的「前后不是字母数字下划线」做边界；<c>\b</c> 不行，它会把 <c>YSpent</c> 开头的 Y 也匹配上）。
    /// </summary>
    private static bool ContainsIdentifier(string? formula, string id)
    {
        if (string.IsNullOrWhiteSpace(formula)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(formula,
            $@"(?<![A-Za-z0-9_]){System.Text.RegularExpressions.Regex.Escape(id)}(?![A-Za-z0-9_])");
    }

    /// <summary>
    /// 「播放音效」的校验（V0.2.154）：音效必须选，而且得是 <c>event:/…</c> 这种形状。
    /// 不在候选表里只给**提示**（候选表只是「本体源码里真实用过的一批」，不是全集 ——
    /// 手填别的本体事件完全合法；路径打错时最多是游戏里不放声音，不会崩）。
    /// </summary>
    private static void ValidatePlaySfx(List<ValidationIssue> issues, string owner, EffectSpec e)
    {
        string path = (e.Sfx ?? "").Trim();
        if (path.Length == 0)
        {
            issues.Add(new("错误", $"{owner} 的「播放音效」还没选音效（编辑器里「音效」那一栏）——"
                + "生成出来会是一句注释，游戏里什么都不放。"));
            return;
        }
        if (!EffectCatalog.LooksLikeSfxPath(path))
        {
            issues.Add(new("错误", $"{owner} 的「播放音效」填的是「{path}」："
                + "本体音效是 FMOD 事件路径，必须形如 event:/sfx/xxx（例：event:/sfx/block_gain）。"));
            return;
        }
        if (!EffectCatalog.IsKnownSfx(path))
        {
            issues.Add(new("提示", $"{owner} 的「播放音效」用的是「{path}」——"
                + "它不在下拉候选里（候选只是本体源码里真实用过的一批）。确认路径没打错就行；"
                + "路径不存在时游戏里只是不放声音（不会崩）。"));
        }
        if (e.Amount > 200m)
            issues.Add(new("警告", $"{owner} 的「播放音效」音量填的是 {e.Amount:0.##}%（超过 200%）——"
                + "会非常吵，而且本体可能爆音，建议 ≤ 200。"));
    }

    /// <summary>
    /// 「给予卡牌关键词」的校验：关键词填了没、存不存在；数值 0（= 这张牌自己）只有卡牌上成立。
    /// 自定义关键词给的是「战斗里的牌」，所以卡牌 / 遗物 / 药水 / 状态触发器都能用（数值 ≥ 1 即可）。
    /// </summary>
    private static void ValidateGiveKeyword(List<ValidationIssue> issues, string owner, EffectSpec e, CharacterProfile p, string ctx)
    {
        string raw = (e.GivenKeyword ?? "").Trim();
        if (raw.Length == 0)
        {
            issues.Add(new("错误", $"{owner} 的「给予卡牌关键词」还没选要给哪个关键词"
                + "（本体关键词在「给予关键词」下拉里，自定义关键词要先到「自定义关键词」页添加）。"));
            return;
        }
        string? vanilla = EffectCatalog.NormalizeVanillaKeyword(raw);
        if (vanilla is null && p is not null && KeywordGen.Find(p, raw) is null)
        {
            issues.Add(new("错误", $"{owner} 的「给予卡牌关键词」要给的「{raw}」找不到："
                + "既不是本体关键词（消耗 / 虚无 / 固有 / 保留 / 不能被打出 / 奇巧 / 永恒），也不是已定义的自定义关键词。"));
        }
        if (e.AmountIsXEffective)
            issues.Add(new("提示", $"{owner} 的「给予卡牌关键词」数值 = X："
                + "选几张牌取决于这张牌结算时的 X（X 费牌才有意义）。"));
        else if (e.Amount <= 0 && ctx != "Card")
            issues.Add(new("错误", $"{owner} 的「给予卡牌关键词」数值是 0（= 这张牌自己）："
                + "只有卡牌上的这条效果才有「自己」这张牌 —— 遗物 / 药水 / 状态触发器请改成 ≥ 1 张。"));
        if (e.TempKeyword)
            issues.Add(new("提示", $"{owner} 的「给予卡牌关键词」勾了「临时关键词」："
                + "保留 / 奇巧走本体的单回合标记，其它关键词会在回合结束时被摘掉（卡面上那几个字也会跟着消失）。"));
    }

    /// <summary>
    /// 需求 ⑦：「生成 / 变化出来的卡：加上关键词」的校验。判据和「给予卡牌关键词」完全一样 ——
    /// 关键词必须是本体那 7 个之一，或者是「自定义关键词」页里已经定义好的那个（键 / 名字都认）。
    ///
    /// 找不到时报**错误**：生成出来的代码会调 <c>AddGivenCustomKeyword("KEY")</c>，
    /// 注册表里没有这个键的话游戏里只会把键名原样印在卡面上，用户看不出来哪里配错了。
    /// </summary>
    private static void ValidateSpawnKeyword(List<ValidationIssue> issues, string owner, EffectSpec e, CharacterProfile p)
    {
        string raw = (e.SpawnKeyword ?? "").Trim();
        string? vanilla = EffectCatalog.NormalizeVanillaKeyword(raw);
        string kindZh = EffectCatalog.FindKind(e.Kind).Display;
        if (vanilla is null && p is not null && KeywordGen.Find(p, raw) is null)
        {
            issues.Add(new("错误", $"{owner} 的「{kindZh}」选了「生成 / 变化出来的卡：加上关键词」=「{raw}」，"
                + "但这个关键词找不到：既不是本体关键词（消耗 / 虚无 / 固有 / 保留 / 不能被打出 / 奇巧 / 永恒），"
                + "也不是已定义的自定义关键词。请到「自定义关键词」页添加它，或者改成别的关键词。"));
            return;
        }
        // 给一句提示：说清游戏里那张生成出来的卡会带什么（自定义关键词还额外说明金字 / 悬停说明来自我们的补丁）
        issues.Add(new("提示", $"{owner} 的「{kindZh}」：生成 / 变化出来的卡会带上"
            + $"「{EffectCatalog.VanillaKeywordZh(raw)}」"
            + (vanilla is null ? "（自定义关键词，卡面上的金字与悬停说明由生成的关键词补丁加上）。" : "。")));
    }

    /// <summary>
    /// 条件能不能用在这个地方、需不需要选状态 / 填数值。
    /// </summary>
    internal static void ValidateCondition(List<ValidationIssue> issues, string owner, ConditionSpec? cond, string ctx)
    {
        if (cond is null || cond.IsNone) return;

        var opt = EffectCatalog.FindCondition(cond.Kind);
        if (opt is null)
        {
            issues.Add(new("错误", $"{owner} 的条件不认识：{cond.Kind}。"));
            return;
        }
        bool allowed = ctx switch
        {
            "Card" => opt.ForCard,
            // 诅咒也是卡（效果写在 OnTurnEndInHand 里），条件可用范围和卡牌一样
            "Curse" => opt.ForCard,
            "Relic" => opt.ForRelic,
            "Power" => opt.ForPower,
            // 药水（V0.2.127，用户要求第 6 条「药水方面参考遗物的优化方案」）：
            // 药水页右下角现在也有「单条效果的条件」，生成端 `EmitPotionEffect` 真的会包这层 if
            //（`CondCtx.Potion` 的表达式和遗物同形）。所以可用范围和遗物**同一档**。
            // ⚠ 以前这里对 Potion 一律报「药水不支持条件选项」—— 那条是**当时**生成端确实没做；
            // 老存档带药水条件本来就生成不出来，放宽不会改到任何老存档的生成结果。
            "Potion" => opt.ForRelic,
            _ => false,
        };
        if (!allowed)
        {
            issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」不能用在这里，请换一个。"));
            return;
        }
        // 卡牌时机（需求⑩）：子时机必须认得出来，而且这条条件只对**普通卡牌**有意义 ——
        // 遗物 / 药水 / 自定义状态根本没有「这张牌被消耗 / 被丢弃 / 被保留」这一说；
        // 诅咒虽然也是卡，但它的效果写在「回合结束时在手上」里，生成端没有为它挂这些卡牌钩子
        //（这就是要单独拦 Curse 的原因：ForCard 那一关它是过的）。
        // 它也没有数值 / 状态 / 指向对象可填，所以判完就返回。
        if (opt.NeedsTiming)
        {
            if (EffectCatalog.FindCardTiming(cond.Timing) is null)
            {
                issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」的子时机认不出来：{cond.Timing}。"
                    + "（可选：" + string.Join(" / ", EffectCatalog.CardTimings.Select(t => t.Display)) + "）"));
                return;
            }
            if (!string.Equals(ctx, "Card", StringComparison.Ordinal))
                issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」不能用在这里："
                    + "它只对**普通卡牌**有意义（遗物 / 药水 / 自定义状态没有「这张牌」；"
                    + "诅咒的效果写在「回合结束时在手上」，挂不上这些卡牌钩子）。"));
            return;
        }
        if (opt.NeedsPower && string.IsNullOrWhiteSpace(cond.PowerId))
            issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」还没选状态。"));
        else if (opt.NeedsPower && EffectCatalog.FindPower(cond.PowerId) is null && !EffectCatalog.IsCustomPower(cond.PowerId))
            issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」里的状态找不到：{cond.PowerId}。"));
        // V0.2.247（用户要求）：「上一张打出的牌带有某关键词 / 是某范围（可按组）」的两条漏填检查
        if (opt.NeedsKeyword)
        {
            if (EffectCatalog.NormalizeVanillaKeyword(cond.Keyword) is null)
                issues.Add(new("错误", $"{owner} 的条件「{opt.Display}」还没选关键词"
                    + "（可选：" + string.Join(" / ", EffectCatalog.VanillaKeywordChoices.Select(k => k.Display)) + "）。"));
        }
        if (opt.NeedsScope && string.IsNullOrWhiteSpace(cond.Scope))
            issues.Add(new("提示", $"{owner} 的条件「{opt.Display}」范围留空了：留空 = **任意牌**，"
                + "这条条件等于没判（一定会成立）。想判某个组 / 某类牌请在下拉里选一个。"));
        else if (opt.NeedsScope && EffectCatalog.IsCardGroupFilter(cond.Scope))
        {
            // 组是空的（组里一张牌都没勾）→ 生成出来的判据恒为 false，等于效果永远不生效
            var cards = EffectCatalog.CardGroupCards(EffectCatalog.DisplayProfile, cond.Scope);
            if (cards.Count == 0)
                issues.Add(new("警告", $"{owner} 的条件「{opt.Display}」选的组里**一张牌都没有** —— "
                    + "生成的判据永远不成立（这条效果等于永远不生效）。到「组」页给这个组勾几张牌。"));
        }
        // 「填负数 = 反向」（用户要求）：支持的条件给一句提示，不支持的说清「会被当成正数用」
        if (opt.SupportsNegative)
        {
            if (opt.NeedsAmount2 && cond.IsOutsideRange)
                issues.Add(new("提示", $"{owner} 的条件「{opt.Display}」填了负数 → 判断成**区间外**："
                    + $"生命不在 {Math.Abs(cond.Amount):0.##}%~{Math.Abs(cond.Amount2):0.##}% 之间时才成立。"));
            else if (cond.IsInverted)
                issues.Add(new("提示", $"{owner} 的条件「{opt.Display}」填了负数 → 判断**反过来**："
                    + $"实际生效的是「{EffectCatalog.ConditionZh(cond.Kind, cond.Amount, cond.Amount2, cond.PowerId, cond.TargetZh)}」。"));
        }
        else if (opt.NeedsAmount && cond.Amount < 0)
        {
            issues.Add(new("警告", $"{owner} 的条件「{opt.Display}」填了负数：这个条件不支持「填负数 = 反向」，"
                + $"生成时按绝对值 {Math.Abs(cond.Amount):0.##} 处理。"));
        }
        if (opt.NeedsAmount && cond.Amount <= 0 && !opt.SupportsNegative && !opt.NeedsAmount2)
            issues.Add(new("警告", $"{owner} 的条件「{opt.Display}」填的数值是 {cond.Amount}，条件会永远不成立。"));
        else if (opt.NeedsAmount && cond.Amount == 0 && opt.SupportsNegative)
            issues.Add(new("警告", $"{owner} 的条件「{opt.Display}」填的数值是 0（0 既不是正数也不是负数）——"
                + "生成出来的是「>= 0 / 低于 0」这种恒真条件，确认这是你想要的。"));
        if (opt.NeedsAmount2 && cond.Amount2 == 0 && cond.Amount != 0)
            issues.Add(new("提示", $"{owner} 的条件「{opt.Display}」第二个数值是 0："
                + $"按下界 0% / 上界 {Math.Abs(cond.Amount):0.##}% 处理（顺序写反了也没关系，工具会自己排好）。"));
        if (opt.NeedsTarget && !EffectCatalog.ConditionTargets.Any(t => t.Id == cond.Target))
            issues.Add(new("错误", $"{owner} 的条件的「指向对象」非法：{cond.Target}。"));
        // 「指定敌人」= 玩家给这张牌选的目标：只有写在效果里才知道结果
        if (opt.NeedsTarget && string.Equals(cond.Target, "Enemy", StringComparison.OrdinalIgnoreCase))
        {
            if (cond.UnplayableWhenUnmet)
                issues.Add(new("警告", $"{owner} 的条件指向对象是「指定敌人」，同时勾了「不满足时打不出去」——"
                    + "打出前玩家还没选目标，这项判定做不了，已按「只包住这条效果」处理（想拦住打出请改用「任意一个敌人」）。"));
            else
                issues.Add(new("提示", $"{owner} 的条件指向对象是「指定敌人」：按你给这张牌选的那个目标判断，效果条生效；"
                    + "（「不满足时打不出去」那种整张牌判定用不了这个对象。）"));
        }
    }

    /// <summary>
    /// 多人联机宝箱房的手势 + 催促文本（美术资源页最下面那几项）。
    ///
    /// 为什么要单独说一句：这四张手势是本体按**固定路径**读的一整套
    /// （<c>CharacterModel.ArmPointingTexture / ArmRock / ArmPaper / ArmScissors</c>），
    /// 只传一半的话，宝箱房那只手会一半是你的一半是占位 —— 而宝箱房只在联机出现，
    /// 单机永远看不到，所以在这里提前说清；「拿取」是本体没有的第 5 张（我们单独打补丁），
    /// 不传才是正常情况。
    /// </summary>
    private static void CheckTreasureGestures(CharacterProfile p, List<ValidationIssue> issues)
    {
        string?[] four = [p.Art.GesturePoint, p.Art.GestureScissors, p.Art.GestureRock, p.Art.GesturePaper];
        int filled = four.Count(x => !string.IsNullOrWhiteSpace(x));
        if (filled > 0 && filled < 4)
        {
            issues.Add(new("警告", $"多人联机宝箱房手势只传了 {filled} / 4 张（选择 / 剪刀 / 石头 / 布）："
                + "没传的那几张会用占位图，联机进宝箱房时那只手会一半是你的一半是占位，看着很怪。要么四张都传，要么都不传。"));
        }
        else if (filled == 0)
        {
            issues.Add(new("提示", "没传多人联机宝箱房手势（选择 / 剪刀 / 石头 / 布）：联机进宝箱房时那只手是"
                + (p.Art.UseVanillaPlaceholders
                    ? "铁甲战士的手（现在勾了「未上传的槽位用本体素材占位」）"
                    : "中性色块（勾上「未上传的槽位用本体素材占位」可以先用铁甲战士的手顶着）")
                + "。单机看不到这处美术，只在联机宝箱房出现。"));
        }
        else if (string.IsNullOrWhiteSpace(p.Art.GestureGrab))
        {
            issues.Add(new("提示", "四张手势都传了、没传「拿取」：这是正常的 —— 本体没有「拿取」这张图，"
                + "它是把「布（伸手）→ 石头（攥住）」两步糊出来的，所以抓着遗物的那只手就是你的「石头」。"
                + "想让「攥住」那一下单独用一张图，再传「拿取」。"));
        }
        string text = (p.Art.TreasureFightText ?? "").Trim();
        if (text.Length > 20)
            issues.Add(new("提示", $"宝箱房催促文本有 {text.Length} 个字：那行字会按屏幕自动缩字号（本体最小 8 号），太长会缩得很小。"));
    }

    /// <summary>
    /// 描述类文本的**富文本标签体检**。
    ///
    /// 为什么要单列一条：本体的 <c>MegaLabelHelper.ParseBbcode</c> 对标签是严格的，
    /// 拼错（写成 <c>[god]</c>）、没闭合（<c>[gold]文字</c>）、交叉嵌套都会**抛异常**
    /// （游戏日志里的 "Found end tag gold, expected god" 就是这么来的），
    /// 一旦抛了，本体的自动字号计算中断、卡面上那串方括号还会被原样印出来。
    /// 生成时已经用 <see cref="RichTextFix"/> 自动修好了，这里把「修了什么」报给用户看清楚
    /// （用户实测就是写成了 [god]，看到卡面上一堆方括号以为富文本坏了）。
    /// </summary>
    private static void ValidateRichText(List<ValidationIssue> issues, CharacterProfile p)
    {
        void Check(string who, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (string note in RichTextFix.Repair(text).Notes)
                issues.Add(new("警告", $"{who} 的富文本标签有问题：{note}"
                    + "（生成时会自动修好，但建议你回去把它改对）"));
        }

        Check("角色描述", p.Description);
        Check("死亡描述文本", p.DeathText);
        Check("阵亡后台词", p.DeadBanterText);
        Check("多人催促文本（联机 Ping 那句）", p.EndTurnPingText);
        Check($"额外资源量「{p.ExtraResource.Name}」的名字", p.ExtraResource.Name);
        foreach (var c in p.AllCards)
        {
            if (c is null) continue;
            Check($"卡牌「{c.Name}」的自定义描述", c.CustomDescription);
            Check($"卡牌「{c.Name}」升级后的自定义描述", c.UpgradedCustomDescription);
            ValidateHighlightVars(issues, c);
            // 「升级后的自定义描述」填了，但这张牌**升不了级**（诅咒固定 MaxUpgradeLevel = 0）→ 那段永远不会显示
            if (c.HasUpgradedCustomDescription && c.IsCurseCard)
                issues.Add(new("警告", $"诅咒「{c.Name}」填了「升级后的自定义描述」—— 诅咒**不能升级**"
                    + "（本体所有诅咒都是 MaxUpgradeLevel = 0），所以那段文字游戏里永远不会显示。"));
            // V0.2.136 同理：「自定义关键词 · 升级后获得 / 失去」填了但这张牌升不了级 → 那一栏永远不会生效
            // （照上面诅咒那条的写法）。状态牌（MaxUpgradeLevel = 0）也走这一条。
            if (c.UpgradeCustomKeywordsAny && !CardIsUpgradable(c))
                issues.Add(new("警告", $"「{c.Name}」填了「自定义关键词 · 升级后获得 / 失去」—— 这张牌**升不了级**"
                    + (c.IsCurseCard
                        ? "（诅咒固定 MaxUpgradeLevel = 0）"
                        : "（状态牌固定 MaxUpgradeLevel = 0）")
                    + "，所以这两栏游戏里永远不会生效（卡面描述与悬停说明都不会变）。"
                    + "另外提醒一句：自定义关键词本来就**只影响显示**（卡面那行 + 悬停说明），"
                    + "本体是封闭枚举、没有任何按它判定的逻辑，所以这两栏也不会带来机制层面的变化。"));
            // 升级前 / 升级后两段不一样时，生成出来是 `{IfUpgraded:show:升级后|没升级}`；
            // 而本体那套是按**字面量 |** 切分选项的 —— 描述里自己写一个 | 会把那句话截断（卡面直接读不出来）。
            // 生成端会把 | 换成全角 ｜（能显示、不影响阅读），这里提醒一声，免得你以为是自己写错了。
            if (c.HasCustomDescription || c.HasUpgradedCustomDescription)
            {
                bool twoVersions = c.HasUpgradedCustomDescription
                    || !string.Equals(LocalizationGen.CardAutoText(p, c, upgraded: false),
                                      LocalizationGen.CardAutoText(p, c, upgraded: true), StringComparison.Ordinal);
                if (twoVersions && ((c.CustomDescription ?? "").Contains('|') || (c.UpgradedCustomDescription ?? "").Contains('|')))
                    issues.Add(new("警告", $"卡牌「{c.Name}」的描述里写了 `|`（竖线），而这张牌的升级前 / 升级后描述不一样 —— "
                        + "生成时会用本体的 `{IfUpgraded:show:升级后|没升级}` 把它们包起来，其中那个 `|` 会把这句话**截断**。"
                        + "生成时已经帮你换成了全角「｜」（看起来几乎一样），想改回来就用全角那个字符。"));
            }
        }
        foreach (var r in p.Relics) Check($"遗物「{r.Name}」的自定义描述", r.CustomDescription);
        foreach (var s in p.Potions) Check($"药水「{s.Name}」的自定义描述", s.CustomDescription);
        foreach (var k in KeywordGen.All(p))
            Check($"自定义关键词「{k.Spec.Name}」的名字/说明", (k.Spec.Name ?? "") + "\n" + (k.Spec.Description ?? ""));
        foreach (var cp in p.CustomPowers)
        {
            if (!cp.Enabled) continue;
            Check($"自定义状态「{cp.Name}」的名字/描述", (cp.Name ?? "") + "\n" + (cp.Description ?? ""));
        }
        foreach (var o in VanillaPowerGen.Active(p))
            Check($"本体状态「{o.PowerId}」的新名字/新描述", (o.Name ?? "") + "\n" + (o.Description ?? ""));
        foreach (var s in p.Summons)
            Check($"召唤物「{s.Name}」的名字", s.Name);
    }

    /// <summary>
    /// **升级差异高亮**（<c>{X:diff()}</c>）的体检 —— 用户要求：「填了 :diff() 但那个变量在当前配置里不存在
    /// （比如效果删了）→ 给提示或错误并说清是哪张牌；不许静默」。
    ///
    /// 为什么必须拦：本体的占位符是**按动态变量的名字取**的（<c>DynamicVars["Damage"]</c>），
    /// 取不到就把 <c>{Damage:diff()}</c> **原样印在卡面上**（见 <see cref="CSharpCodeGen.DisplayVarNameOf"/>
    /// 那段注释里用户实测的截图）。这件事只有进了游戏才看得见，所以生成前必须报出来。
    ///
    /// 两条判据：
    /// 1. 正文里写的 <c>{X}</c> / <c>{X:diff()}</c>，X 不在「这张牌真的声明过的变量」里：
    ///    <c>:diff()</c> 那种 = **错误**（用户就是按高亮按钮写出来的，必然是本版这个功能踩的坑）；
    ///    普通 <c>{X}</c> 那种 = **警告**（也可能是用户手写的本体自己的键，口径放宽一档，别把人拦死）。
    /// 2. 「升级后的自定义描述」里勾了高亮（<c>{X:diff()}</c>）但**整段一个 {} 占位符都没有**：
    ///    提示去点「填入参数…」（用户现场就是纯中文 + 直接写死的数字，这条提示是最要紧的一句界面文案）。
    /// </summary>
    private static void ValidateHighlightVars(List<ValidationIssue> issues, CardSpec c)
    {
        // 这张牌**真的声明过**的动态变量（含重名别名 Cards2；口径见 CSharpCodeGen.DeclaredVarKeysOf）
        var declared = CSharpCodeGen.DeclaredVarKeysOf(c.Effects);

        // 两段用户文本里的占位符都要查（自定义描述也可能写 {Damage:diff()}）
        var undeclaredDiff = new List<string>();
        var undeclaredPlain = new List<string>();
        var seenDiff = new HashSet<string>(StringComparer.Ordinal);
        var seenPlain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (who, text) in new[]
                 {
                     ("自定义描述", c.CustomDescription),
                     ("升级后的自定义描述", c.UpgradedCustomDescription),
                 })
        {
            foreach (var ph in HighlightDiffText.Scan(text))
            {
                if (declared.Contains(ph.Key)) continue;
                if (ph.HasDiff)
                {
                    if (seenDiff.Add(who + "\u0000" + ph.Key)) undeclaredDiff.Add($"{who}里的 `{HighlightDiffText.DiffToken(ph.Key)}`");
                }
                else if (seenPlain.Add(who + "\u0000" + ph.Key))
                {
                    undeclaredPlain.Add($"{who}里的 `{HighlightDiffText.PlainToken(ph.Key)}`");
                }
            }
        }
        if (undeclaredDiff.Count > 0)
            issues.Add(new("错误", $"卡牌「{c.Name}」的描述里用了这张牌**没有声明过**的变量："
                + string.Join("、", undeclaredDiff)
                + "。本体的卡面是**按变量名取值**的，取不到就把 `{名字:diff()}` 原样印在卡面上 —— "
                + "多半是那条效果后来被删了 / 换了种类。请用「填入参数…」重新插一次（那个菜单只列真的声明过的变量），"
                + "或者把这几处占位符改回普通文字。"));
        if (undeclaredPlain.Count > 0)
            issues.Add(new("警告", $"卡牌「{c.Name}」的描述里用了这张牌**没有声明过**的变量："
                + string.Join("、", undeclaredPlain)
                + "。生成出来游戏里取不到值，会原样印出 `{{名字}}`（本体自己的占位符就无妨）。"
                + "要用数值请用「填入参数…」重新插一次。"));

        // 「勾了高亮但这整段里一个占位符都没有」 —— 用户现场就是这种（纯中文 + 写死的数字），必须明说
        string up = c.UpgradedCustomDescription ?? "";
        if (up.Contains(":diff()", StringComparison.Ordinal) && HighlightDiffText.Scan(up).Count == 0)
            issues.Add(new("警告", $"卡牌「{c.Name}」的「升级后的自定义描述」里写了 `:diff()`，"
                + "但这段文字里**一个 `{}` 占位符都没有**（写的是直接的数字 / 中文）—— 高亮不会生效。"
                + "要让数值按升级差异变绿，请点这一栏底下的「填入参数…」把数字换成占位符"
                + "（例：造成 `{Damage:diff()}` 点伤害），再用「高亮字段…」逐字段挑。"));
    }
}
