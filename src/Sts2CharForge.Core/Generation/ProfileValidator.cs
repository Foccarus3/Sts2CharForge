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
        // 自定义关键词的引用检查：普通卡 + 诅咒 + 先古卡都要查（三栏都可能勾关键词）
        foreach (var card in p.AllCards)
        {
            if (card is null) continue;
            foreach (string r in card.CustomKeywordList)
                if (KeywordGen.Find(p, r) is null)
                    issues.Add(new("错误", $"卡牌「{card.Name}」引用了不存在的自定义关键词：{r}"
                        + "（可能已经被删掉了，去「卡牌」页取消勾选，或到「自定义关键词」页把它加回来）。"));
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

        // 效果库整体为空（换机器后路径失效）→ 只报一条，说清怎么修
        if (EffectCatalog.Powers.Count == 0)
            issues.Add(new("错误", "效果库为空：增益/减益列表读不到。请到「路径」页指定你本机的「本体工程目录」"
                + "（解包后的原版工程，含 src/Core/Models/Powers）或「游戏 data 目录」（含 sts2.dll），"
                + "然后点「构建 / 日志」页的「重新扫描效果库」。"));

        // 自定义状态（能力牌用的「你自己的状态」）
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
            // 自定义状态上那块「节点图」生成端**还没接**（ForgeGraphGen 只处理卡牌 / 药水 / 遗物的布），
            // 所以布上画了什么都不会出现在生成的代码里 —— 这种「静默不生效」必须说出来。
            if (cp.ForgeGraph is { IsEmpty: false })
                issues.Add(new("警告", $"{who} 上用节点图画了东西，但**自定义状态的节点图生成端还没接**"
                    + "（这一版只有卡牌 / 药水 / 遗物的布会真的生成）—— 请在下面「触发时机」列表里配效果，"
                    + "那块布现在只是留着看的。"));

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
                    if (!CustomPowerGen.TriggerHasChoiceContext(t.Kind) && CustomPowerGen.NeedsChoiceContext(e.Kind) && !roomReward)
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
                    // 「数值公式」（需求：「另加一栏『数值公式』」）：自定义状态的触发器那条链没有
                    // 「打出这张牌」的上下文（拿不到打出的那张牌 / 玩家选中的目标），
                    // 所以这一版只做卡牌。填了就必须拦住 —— 不然生成端会静默用固定数值，用户看不出问题。
                    if (!string.IsNullOrWhiteSpace(e.AmountFormula))
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」"
                            + $"填了「数值公式」（{ForgeFormula.DisplayText(e.AmountFormula)}）："
                            + "这一版公式只做在**卡牌**上 —— 自定义状态的触发器没有「打出这张牌」那一刻的上下文。"
                            + "请清空公式，改用固定数值；或者把这条效果放到卡牌上。"));
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
                    // 「施加状态」填负数 = 扣除目标身上已有的层数。
                    // 为什么这一条以前没有、现在必须有：状态触发器那条链原来的「施加状态」是**手写**的
                    // PowerCmd.Apply（不是 CSharpCodeGen.ApplyPowerLine），负数会被本体拿去**新建一个 -N 层的状态**
                    //（ApplyInternal(target, -N, …)），和卡牌 / 遗物 / 药水（负数 = GetPower + ModifyAmount 扣层数）
                    // 完全是两回事。现在那条链也改成复用 ApplyPowerLine 了，这里给一条提示说清语义，
                    // 免得用户以为「-3 = 给目标挂一个 -3 层」。
                    // 放成 else 之外的独立 if：没选状态时上面已经报了错误，这条提示跟着说一句也不冲突。
                    if (e.Kind == "ApplyPower" && e.Amount < 0)
                        issues.Add(new("提示", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」数值是 {e.Amount}："
                            + $"会生成「{(e.TargetSide == "Self" ? "失去" : "扣除")} {-e.Amount} 层{EffectCatalog.PowerName(e.PowerId)}」"
                            + "（先找目标身上那份状态实例再扣；目标身上没有这个状态时什么都不做）"
                            + "—— 和卡牌 / 遗物 / 药水同一条实现，**不是**「给目标挂一个 " + e.Amount + " 层的新状态」。"));
                    // 每条效果自己的条件选项（能力/状态里的条件不支持「这张牌」类条件）
                    ValidateCondition(issues, $"{who}「{when}」第 {j + 1} 条", e.Condition, "Power");
                    if (e.Kind == "GenerateCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                        issues.Add(new("警告", $"{who}「{when}」的「生成卡牌」没选目标卡，会生成小刀（Shiv）。"));
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
                    issues.Add(new("警告", $"{who} 填了血条颜色，但本体血条上只有「中毒」那一截是按状态显示颜色的，"
                        + "这个状态没有血条段，颜色不会有显示效果。"));
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
            // 升级后费用：X 费牌改不了、负数费用会被夹到 0，提前说清楚
            if (c.UpgradeCost is { } upCost)
            {
                if (c.CostIsX)
                    issues.Add(new("警告", $"卡牌「{c.Name}」是 X 费用牌，「升级后费用」填的 {upCost} 不会生效"
                        + "（本体改费用的逻辑遇到 X 费牌会直接返回）。"));
                else if (c.Cost < 0)
                    issues.Add(new("警告", $"卡牌「{c.Name}」现在的费用是 {c.Cost}（负数 = 特殊牌），"
                        + "本体的改费逻辑会把结果夹到 0，可能让这张牌变成能打出。"));
                else if (upCost < 0)
                    issues.Add(new("错误", $"卡牌「{c.Name}」的「升级后费用」不能是负数：{upCost}。"));
            }
            if (usesX && !c.CostIsX && !c.StarCostIsX)
                issues.Add(new("警告", $"卡牌「{c.Name}」的效果里勾了「= X」，但这张牌的费用不是 X（也没勾资源量 X）："
                    + "X 会被当成 0，效果等于不生效。请到「费用」那一行勾上「X 费用」。"));
            if (c.StarCostIsX && !c.Effects.Any(e => e.Kind == "ExtraResource" && e.Amount < 0))
                issues.Add(new("警告", $"卡牌「{c.Name}」勾了「额外资源量费用为 X」，但效果里没有「花费额外资源量」（负数）这一条："
                    + "牌面会比本体多显示一个资源量费用图标。"));
            if (c.XPlusOnUpgrade && !c.CostIsX && !c.StarCostIsX)
                issues.Add(new("警告", $"卡牌「{c.Name}」勾了「升级后 X +1」，但它的费用不是 X —— 这个勾选会被忽略。"));
            if (c.Effects.Any(e => e.AmountIsX && e.UpgradeAmount != 0))
                issues.Add(new("警告", $"卡牌「{c.Name}」有一条「数值 = X」的效果填了「升级增量」：X 的数值不能直接升级（会被忽略），"
                    + "要升级请用卡牌上的「升级后 X +1」。"));
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
            // 多人联机：本体模型 ID 只看**类名**（不含命名空间），而且联机时所有人的模组都得装齐 ——
            // 两个模组里有同名类就会在加载模型库时抛 DuplicateModelException（游戏起不来）。
            // 自己起的类名带上角色类名前缀最省事（自动生成的类名已经带了，所以这里只提醒手填的那些）。
            if (!string.IsNullOrWhiteSpace(c.ClassName) && !Naming.IsBasicCardName(c.ClassName) && !cls.StartsWith(n.CharClass, StringComparison.Ordinal))
                issues.Add(new("提示", $"卡牌「{c.Name}」的英文类名「{cls}」没带角色前缀「{n.CharClass}」—— "
                    + "多人联机时所有人的模组会装在同一台机器上，**两份模组有同名类**会让游戏加载模型库直接抛异常"
                    + "（本体的模型 ID 只按类名算，不含命名空间）。建议改成 " + n.CharClass + cls + "。"));
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

            if (c.Cost is < 0 or > 5 && !c.IsCurseCard)
                issues.Add(new("警告", $"卡牌「{c.Name}」费用 {c.Cost} 超出常规范围（0~5）。"));
            if (c.InStartingDeck && c.StartingCopies is < 1 or > 10)
                issues.Add(new("警告", $"卡牌「{c.Name}」初始份数 {c.StartingCopies} 建议 1~10。"));
            ValidateEffects(issues, $"卡牌「{c.Name}」", c.Effects, ctx: c.IsCurseCard ? "Curse" : "Card", p: p);
            // 「数值公式」里引用了「指定敌人」的参数，但这张牌打出时**根本没有选中某个敌人**（全体敌人那种）
            // → 那个参数永远是 0，公式静默失效。用户实测报过：「设 A 为敌人的虚弱、伤害写 A+3、作用对象全体敌人」
            // 生成出来是 `PowerAmount(cardPlay?.Target, …)`，而全体敌人的牌 `cardPlay.Target` 是 null → 伤害恒为 3。
            if (!c.IsCurseCard) ValidateFormulaTargets(issues, c, p);
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

                if (!CSharpCodeGen.HasContext(trig.Kind) && !CSharpCodeGen.CanBootstrapContext(trig.Kind))
                {
                    foreach (var e in r.Effects.Where(x => x.Kind is "Draw" or "Damage" or "HpLoss" or "ApplyPower" or "TempPower"
                                                           || (x.Kind == "MaxHp" && x.Amount < 0)))
                    {
                        if (!relicAncientMsgs.Add("ctx|" + trig.Kind + "|" + e.Kind)) continue;   // 同上：同一句只报一次
                        issues.Add(new("警告",
                            $"遗物「{r.Name}」的{tPrefix}「{EffectCatalog.FindKind(e.Kind).Display}」在「{RelicTriggerName(trig.Kind)}」缺少 choiceContext，生成时会被忽略。"));
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

        // 多个模组装在一起会不会撞车（本体的模型 ID 只按类名算 —— 撞了游戏直接起不来）。
        // 这一步要读磁盘上别的存档工程，任何意外（路径怪、文件被占、读取失败）都绝不能影响正常校验。
        try { CheckMultiModConflicts(issues, p, n); } catch { /* 读不到就不报，宁可漏报 */ }

        // 逐帧动画（动图拆帧）：勾了总开关却没待机 / 素材不是 GIF / 数值越界
        ValidateAnimation(issues, p);

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
                        CheckPetRef(owner, e, i);
                    }
                }
                else if (EffectCatalog.IsPetKind(e.Kind))
                {
                    // ===== 新增的那批宠物效果（都要选「召唤物（哪一只）」，宠物不在场时安全跳过）=====
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
                                    + "—— 请把作用对象改成「单体敌人」或「全体敌人」。"));
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
        bool anyCardOrRelicSummons = p.AllCards.Any(c => c.Effects.Any(e => e.Kind == "SummonPet"))
            || p.Relics.Any(r => r.Effects.Any(e => e is not null && e.Kind == "SummonPet"));
        if (!anyCardOrRelicSummons)
            issues.Add(new("提示", $"召唤物已启用（{enabled.Count} 只），但没有任何卡牌 / 遗物在「召唤」它们"
                + "（卡牌效果里选「召唤伙伴」，或给遗物加一条「召唤伙伴」+ 触发时机「战斗开始时」）—— 游戏里永远不会上场。"));
        var summonRelicsWithoutCtx = p.Relics
            .Where(r => r.Effects.Any(e => e is not null && e.Kind == "SummonPet")
                        && r.Triggers.Any(t => t is not null && RelicTriggerLacksContext(t.Kind)))
            .ToList();
        if (summonRelicsWithoutCtx.Count > 0)
            issues.Add(new("警告", "有遗物在「" + string.Join(" / ", summonRelicsWithoutCtx.Select(r => r.Name)) + "」上配了「召唤伙伴」，"
                + "但那个触发时机的本体钩子拿不到 choiceContext"
                + "（只有「战斗开始时 / 每回合开始时 / 每回合结束时 / 受到伤害时」有）→ 生成时会被忽略。"
                + "想要「战斗开始时召唤伙伴」，请把触发时机改成「战斗开始时」。"));
    }

    /// <summary>触发时机的显示名（错误信息里用；认不出来就退回 id）。</summary>
    private static string RelicTriggerName(string kind) =>
        EffectCatalog.RelicTriggers.FirstOrDefault(t => t.Id == kind)?.Display ?? kind;

    /// <summary>这个遗物触发时机的本体钩子有没有 choiceContext（没有的话召唤伙伴实现不了）。</summary>
    private static bool RelicTriggerLacksContext(string? trigger) =>
        // AfterCombatVictory / AfterCombatEnd / AfterGoldGained 三个签名里都没有 choiceContext
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


    private static void ValidateEffects(List<ValidationIssue> issues, string owner, IEnumerable<EffectSpec> effects,
        string? potionTargetType = null, string ctx = "Card", CharacterProfile? p = null)
    {
        int index = 0;
        foreach (var e in effects)
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
            // 「命中次数 / 对群数」真的会被用到吗（用户报过「命中次数的公式不生效」）：
            // 生成端只有「造成伤害（多段）」「作用对象 = 随机敌人」「几种伙伴攻击」会读它，
            // 其它种类填了以前是**静默忽略**的 —— 现在说清（公式 → 错误；只填了数字 → 警告）。
            if (!e.UsesRepeatCount)
            {
                if (!string.IsNullOrWhiteSpace(e.RepeatFormula))
                    issues.Add(new("错误", $"{owner} 第 {index} 条「{kind.Display}」填了「命中次数 / 对群数」的**公式**"
                        + $"（{ForgeFormula.DisplayText(e.RepeatFormula)}），但这一条效果不会用到它 —— "
                        + "只有「造成伤害」「作用对象 = 随机敌人」和几种「伙伴攻击」会用到这个数字。"
                        + "想「整条效果重复几遍」请用「生效次数」的公式；否则请清空这一栏。"));
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
            if (CSharpCodeGen.AmountZeroIsSelf(e) || (e.Kind is "GiveKeyword" or "TurnEndPlay" && e.Amount <= 0m && !e.AmountIsX))
            {
                if (e.HasCardScope || !string.IsNullOrWhiteSpace(e.SpawnFilter))
                    issues.Add(new("提示", $"{owner} 第 {index} 条「{kind.Display}」的作用档位是「数值 0 = 这张牌自己」 —— "
                        + "这一档**不挑牌**，所以「范围界定」填了也不会生效（生成时整条按「这张牌自己」处理）。"
                        + "要让范围界定生效，请把数值改成 1 以上。"));
                else if (e.Kind == "DiscardCard" && e.SelectPile != "Hand")
                    issues.Add(new("提示", $"{owner} 第 {index} 条「丢弃卡牌」的作用档位是「数值 0 = 丢弃这张牌自己」 —— "
                        + "这一档不挑牌，「从哪里丢弃」填的是「{EffectCatalog.SelectPileZh(e.SelectPile)}」不会生效。"));
            }
            // 作用对象 = 队友（用户要求）：只有那些「真的按选中的那个队友 / 全体队友逐个动手」的            // 效果种类才支持，别的种类生成端会把它当成「自己」（那就变成给自己加东西了，很坑）——
            // 所以这里直接拦住并列出支持的名单，绝不静默。
            if (e.TargetSide is "Ally" or "AllAllies")
            {
                string sideZh = e.TargetSide == "Ally" ? "指定队友" : "全部队友";
                bool kindOk = e.Kind is "Damage" or "ApplyPower" or "TempPower" or "Block" or "Heal" or "HpLoss" or "MaxHp";
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
            if (e.Kind is "Energy" or "Gold" && e.Amount < 0 && !e.AmountIsX)
            {
                string unit = e.Kind == "Energy" ? "点能量" : "枚金币";
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 {e.Amount}："
                    + (e.NextTurn
                        ? $"会生成「下回合开始时失去 {-e.Amount} {unit}」（本体下回合加能量的状态只认正数，所以用生成的负债状态扣）"
                        : $"会生成「失去 {-e.Amount} {unit}」")
                    + $"，不够时只扣到 0（不会变成负数）。"));
            }
            if (e.Kind is "Energy" or "Gold" && e.Amount < 0 && e.AmountIsX)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值 = X 且填了负数：X 要到打出时才知道正负，"
                    + "所以这条会按「获得 X」生成（负数被忽略）。"));
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
                    + (EffectCatalog.AllowsNegativeLayers(e.PowerId)
                        // 力量 / 敏捷 / 专注这几个本体 AllowNegative => true：照本体「尖啸」那套，
                        // 目标身上 0 层也能直接扣成负层数
                        ? $"（{EffectCatalog.PowerName(e.PowerId)}本体允许负层数，所以**照本体「尖啸」那套**直接施加 {-e.Amount} 层，"
                          + "目标身上本来 0 层也能扣成负的。）"
                        : $"（{EffectCatalog.PowerName(e.PowerId)}本体不允许负层数，所以是从目标**身上已有的层数**里扣；"
                          + "目标身上没有这个状态时什么都扣不了。）")
                    + "这和「施加增益/减益」填负数的区别：那个是**永久**扣掉、不会还原。"));
            if (e.Kind == "ApplyPower" && e.Amount < 0)
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 {e.Amount}："
                    + $"会生成「{(e.TargetSide == "Self" ? "失去" : "扣除")} {-e.Amount} 层{EffectCatalog.PowerName(e.PowerId)}」"
                    + "（先找目标身上那份状态实例再扣；目标身上没有这个状态时什么都不做）。"));
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.Powers.Count == 0) { /* 效果库整体为空时由上面统一报错 */ }
            else if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.FindPower(e.PowerId) is null && !EffectCatalog.IsCustomPower(e.PowerId))
                issues.Add(new("错误", $"{owner} 的增益/减益还没选状态：未选择有效的 Power（可以选本体的状态，也可以选「自定义状态」页里自己造的那个）。"));
            // 本体里有些状态显示的数字不是层数（自己 override 了 PowerModel.DisplayAmount）：
            // 填多少层状态栏都不会显示你填的值（用户报过「施加 30 层缓慢，游戏里只看到缓慢」）
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerAmountNote(e.PowerId) is string amountNote)
                issues.Add(new("提示", $"{owner} 施加的是「{EffectCatalog.PowerName(e.PowerId)}」："
                    + $"本体这个状态显示的数字不是层数 —— {amountNote}"
                    + "（层数照旧记着，只是状态栏那个数字由它自己算。）"));
            // 给予卡牌关键词：关键词必须选、而且必须存在（本体枚举名 或 已定义的自定义关键词）
            if (e.Kind == "GiveKeyword")
                ValidateGiveKeyword(issues, owner, e, p, ctx);
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
            // 「下回合生效」：只有真正实现了延迟分支的几种才放行（见 EffectCatalog.HonorsNextTurn）——
            // 以前判的是 EffectKindOption.SupportsNextTurn，那一栏对 8 个**没实现**的种类也写着 true，
            // 于是勾了等于没勾（静默失效）。
            if (e.NextTurn && !EffectCatalog.HonorsNextTurn(e.Kind))
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」不支持「下回合生效」（勾了不会生效，生成端没有延迟分支）—— "
                    + "目前真正支持的是：获得格挡 / 抽牌 / 获得能量 / 获得额外资源量 / 施加或扣除状态 / "
                    + "生成卡牌 / 复制卡牌 / 变化卡牌 / 获得·变化·删除·升级牌组里的牌（全局）。"));
            // 生成类的「下回合生效」目前只做在**卡牌**上：延迟状态里只有 base.Owner.Player 那一套上下文，
            // 遗物 / 药水 / 自定义状态那三条链还没接（别静默按「立刻生成」处理）。
            else if (e.NextTurn && CSharpCodeGen.IsDelayedSpawnKind(e.Kind) && ctx is "Relic" or "Potion" or "Power")
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」勾了「下回合生效」：生成类的延迟状态目前只支持做在**卡牌**上"
                    + "（遗物 / 药水 / 自定义状态那三条链还没接），请去掉这个勾，或把这条效果挪到卡牌上。"));
            // 额外资源量 + 下回合生效：正数走本体的 StarNextTurnPower；「需要 N 点」（负数）是卡牌费用，跟下回合无关
            if (e.Kind == "ExtraResource" && e.NextTurn)
            {
                if (e.Amount > 0)
                    issues.Add(new("提示", $"{owner} 的「获得额外资源量」会在**下回合开始时**获得 {e.Amount:0.##} 点"
                        + "（本体 StarNextTurnPower：回合开始时发放并自毁）。"));
                else if (!e.AmountIsX)
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
                if (e.CardPick != "Chosen")
                    issues.Add(new("警告", $"{owner} 的「{kind.Display}」取牌方式是「随机」，但「取几张」选了"
                        + $"「{CountModes.Zh(e.CountMode)}」—— 随机那条路没有选牌界面，会按「恰好 {Math.Max(1, (int)e.Amount)} 张」处理。"
                        + "想要「可以少选 / 任意张」请把取牌方式改成「自己选」。"));
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
                    + (e.CountIsAny ? "任意张" : $"{(e.CountMode == CountModes.UpTo ? "最多 " : "")}{Math.Max(1, (int)e.Amount)} 张")
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
                if (e.Amount <= 0m && !e.AmountIsX)
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
                if (e.Amount is < 1m or > 5m)
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」张数 {e.Amount:0.##} 超出范围（1~5）。"));
                if (e.Copies is < 1 or > 20)
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」复制的份数 {e.Copies} 超出范围（1~20）。"));
                issues.Add(new("提示", $"{owner} 的「复制卡牌」会{EffectCatalog.CardPickZh(e.CardPick)}"
                    + $"从{EffectCatalog.SelectPileZh(e.SelectPile)}里拿 {e.Amount:0.##} 张牌，每张复制 {Math.Max(1, e.Copies)} 份放进手牌"
                    + "（本体「二刀流 DualWield」的官方做法：CreateClone + 加进手牌；复制出来的牌跟着原牌的升级 / 附魔走）。"));
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
                bool self = e.Amount <= 0m && !e.AmountIsX;
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
                if (string.IsNullOrWhiteSpace(e.SpawnCardId))
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」没选目标卡 —— 请在「目标卡」里选一张要强化的牌。"));
                if (e.Amount == 0m)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」数值是 0 → 挂上一个 0 层的强化状态，等于没有效果。"));
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
    /// 锻造节点图（那块布）的校验：把 <see cref="ForgeGraphGen.Issues"/> 逐条转成校验结果。
    ///
    /// 为什么要接进来：`ForgeGraphGen.Problems`（同一份逻辑）以前是**死代码**，没人调 ——
    /// 于是整块布的图问题**一个都不会告诉用户**。最要命的一条是「【效果】选了节点图没接上的种类」：
    /// 生成、编译、进游戏全都不报错，只是什么都不做 —— 别人实测报的「给召唤物 buff 不生效」就是这个。
    /// </summary>
    private static void ValidateForgeGraph(List<ValidationIssue> issues, string owner, ForgeGraphSpec? g,
        CharacterProfile p, string scope, string? cardTargetType = null)
    {
        foreach (var (level, message) in ForgeGraphGen.Issues(g, p, scope, cardTargetType))
            issues.Add(new(level, $"{owner}：{message}"));
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
            issues.Add(new("错误", $"{owner} 的「{kindZh}」不支持「数值公式」（现在填的是 {formula}）："
                + "这个效果的数值必须在**生成的时候**就定下来 —— 抽几张 / 生成几张 / 消耗几张这类要提前分配"
                + "动态变量、算好选牌上限，塞不进运行时表达式。"
                + "支持公式的只有这 5 种：造成伤害 / 获得格挡 / 失去生命 / 回复生命 / 施加增益减益。"
                + "请清空「数值公式」，改用左边的固定数值。"));
            return;
        }
        if (ctx is "Relic" or "Potion" or "Curse")
        {
            string where = ctx switch { "Relic" => "遗物", "Potion" => "药水", _ => "诅咒" };
            issues.Add(new("错误", $"{owner} 的「{kindZh}」填了「数值公式」（{formula}）："
                + $"这一版公式只做在**卡牌**上 —— {where}的代码链没有「打出这张牌」那一刻的上下文"
                + "（那一侧只有「使用 / 触发」的时刻，取不到「打出的那张牌 / 玩家选中的目标」）。"
                + "请清空公式；或者把这条效果放到卡牌上。"));
            return;
        }
        if (e.AmountIsX)
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
        foreach (string id in ForgeFormula.Identifiers(root))
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
    private static void ValidateCountFormulas(List<ValidationIssue> issues, string owner, EffectSpec e,
        string ctx, CharacterProfile? p)
    {
        if (!e.UsesAnyCountFormula) return;
        // 只做在卡牌上：公式求值要 __fgPlayer / __fgSelf，只有卡牌那条链会先算它们
        if (ctx is "Relic" or "Potion" or "Curse" or "Power")
        {
            string where = ctx switch { "Relic" => "遗物", "Potion" => "药水", "Power" => "自定义状态的触发时机", _ => "诅咒" };
            issues.Add(new("错误", $"{owner} 填了「次数公式」：这一版次数公式只做在**卡牌**上 —— "
                + $"{where}的代码链里没有那两个「自己 / 玩家」的局部变量（公式里的回合数 / 手牌数这类读不出来）。"
                + "请清空次数公式，改用左边的固定次数。"));
            return;
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
        if (e.AmountIsX)
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
        Check($"额外资源量「{p.ExtraResource.Name}」的名字", p.ExtraResource.Name);
        foreach (var c in p.AllCards)
        {
            if (c is null) continue;
            Check($"卡牌「{c.Name}」的自定义描述", c.CustomDescription);
            Check($"卡牌「{c.Name}」升级后的自定义描述", c.UpgradedCustomDescription);
            // 「升级后的自定义描述」填了，但这张牌**升不了级**（诅咒固定 MaxUpgradeLevel = 0）→ 那段永远不会显示
            if (c.HasUpgradedCustomDescription && c.IsCurseCard)
                issues.Add(new("警告", $"诅咒「{c.Name}」填了「升级后的自定义描述」—— 诅咒**不能升级**"
                    + "（本体所有诅咒都是 MaxUpgradeLevel = 0），所以那段文字游戏里永远不会显示。"));
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
}
