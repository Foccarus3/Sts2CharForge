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
            if (!string.IsNullOrWhiteSpace(cp.Icon) && !File.Exists(cp.Icon))
                issues.Add(new("错误", $"{who} 的图标文件不存在：{cp.Icon}"));
            if (cp.RemoveAtTurnEnd && cp.DecayPerTurn > 0)
                issues.Add(new("警告", $"{who} 同时勾了「回合结束移除」和「每回合衰减」，衰减不会生效（直接移除）。"));
            if (cp.Triggers.Count == 0)
                issues.Add(new("警告", $"{who} 没配任何触发时机：这个状态挂上去也不会做事。"));

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
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条效果是「{e.Kind}」，"
                            + "这种效果需要卡牌上下文（选牌 / 结束回合），状态触发器里用不了。"
                            + $"能用的是：{string.Join(" / ", PowerTriggers.SupportedEffectKinds)}。"));
                    // 复制 / 重放 / 回合结束时自动打出，以及「斩杀」条件：都要「打出某张牌」的上下文，
                    // 状态触发器里没有 —— 生成时会留一行「已忽略」，这里也说清楚（不静默丢）。
                    if (e.Kind is "CopyCard" or "ReplayCard" or "TurnEndPlay")
                        issues.Add(new("警告", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」"
                            + "在**自定义状态**里用不了（它要「打出这张牌时选牌」的上下文）—— 生成时会被忽略，"
                            + "请把它放到卡牌 / 遗物 / 药水上。"));
                    if (e.Condition is { Kind: "Fatal" })
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条用了「斩杀」条件，但它只能用在**卡牌**上 —— "
                            + "请把这条效果放到攻击牌上（判的是「这一次攻击有没有把目标打死」）。"));
                    // 「按范围随机 / 生成出来的卡怎么处理」：状态触发器里走的是另一套生成链（base.Owner 是 Creature），
                    // 只支持「指定卡」那种写法 —— 说清楚，别让用户以为配了没生效（用户在自定义状态里配了会被静默忽略）
                    if (e.UsesSpawnOptions && (e.IsSpawnRandom || e.HasSpawnModifier))
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」"
                            + "用了「按范围随机」或「生成出来的卡怎么处理」，但**自定义状态的触发器里不支持**这两类设置 —— "
                            + "请改用卡牌 / 遗物 / 药水，或者把这两项关掉、用「指定卡」。"));
                    if (e.Kind is "ApplyPower" or "TempPower" && string.IsNullOrWhiteSpace(e.PowerId))
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条「{EffectCatalog.FindKind(e.Kind).Display}」还没选状态。"));
                    else if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.Powers.Count > 0
                             && !EffectCatalog.IsCustomPower(e.PowerId) && EffectCatalog.FindPower(e.PowerId) is null)
                        issues.Add(new("错误", $"{who}「{when}」的第 {j + 1} 条要施加的状态找不到：{e.PowerId}。"));
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
                // 诅咒的费用 / 类型是生成时写死的（-1 / Curse），界面上改不了，这里只提醒
                if (c.Cost != -1)
                    issues.Add(new("提示", $"诅咒「{c.Name}」的费用会被生成成 -1（打不出去），这里填的 {c.Cost} 不起作用。"));
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
            if (!EffectCatalog.RelicTriggers.Any(t => t.Id == r.Trigger))
                issues.Add(new("错误", $"遗物「{r.Name}」触发时机非法：{r.Trigger}"));
            if (r.Trigger == "Obtained")
                issues.Add(new("提示", $"遗物「{r.Name}」的触发时机是「获得时」：拿到这只遗物的那一刻触发一次"
                    + "（本体钩子 AfterObtained —— 本体的「好吃饼干 YummyCookie / 磨刀石 Whetstone / 爪子 Claws」"
                    + "这些「拾取时生效」的遗物都是它）。这个钩子本体没给选牌上下文，需要的那几条效果"
                    + "（生成卡牌 / 抽牌 / 卡牌奖励…）生成时会自己造一个阻塞式上下文，能用。"));
            // 「受到伤害时」是**每一下**都触发的钩子：本体的敌人攻击很多是「1 点 × N 下」的连击，
            // 所以一次「5 点伤害」的攻击会触发 5 次（本体原体黏土 SelfFormingClay 也是这样）。
            // 用户报过「5 点伤害触发了五次」——这里直接说清楚，并指路「受到攻击后（连击只算一次）」。
            if (r.Trigger == "DamageReceived")
                issues.Add(new("提示", $"遗物「{r.Name}」的触发时机是「受到伤害时（每一下）」：**每一下伤害都会触发一次** —— "
                    + "敌人的多次连击（本体很多攻击是「1 点 × 5 下」，意图上也写着次数）会触发多次。"
                    + "想让「一次攻击只触发一次」请把触发时机改成「受到攻击后（连击只算一次）」（本体钩子 AfterAttack，"
                    + "在所有命中都结束之后才跑一次）。全被格挡 / 0 伤害的那一下不算（生成时会判 UnblockedDamage > 0）。"));
            if (r.Trigger == "Attacked")
                issues.Add(new("提示", $"遗物「{r.Name}」的触发时机是「受到攻击后（连击只算一次）」："
                    + "一次攻击的**所有命中都结束之后**只触发一次（本体钩子 AfterAttack），"
                    + "所以「1 点 × 5 下」的连击也只算一次。判据是这一次攻击真的打到了你身上"
                    + "（全被格挡 / 打的是别人时不触发）。注意它只认「攻击」：中毒、事件掉血这类不是攻击的伤害不会触发它。"));
            ValidateEffects(issues, $"遗物「{r.Name}」", r.Effects, ctx: "Relic", p: p);
            AddDuplicateVarNotice(issues, $"遗物「{r.Name}」", r.Effects);
            ValidateCondition(issues, $"遗物「{r.Name}」（整只遗物的触发条件）", r.Condition, "Relic");

            if (!CSharpCodeGen.HasContext(r.Trigger) && !CSharpCodeGen.CanBootstrapContext(r.Trigger))
            {
                foreach (var e in r.Effects.Where(x => x.Kind is "Draw" or "Damage" or "HpLoss" or "ApplyPower" or "TempPower"
                                                       || (x.Kind == "MaxHp" && x.Amount < 0)))
                {
                    issues.Add(new("警告",
                        $"遗物「{r.Name}」的「{EffectCatalog.FindKind(e.Kind).Display}」在「{EffectCatalog.RelicTriggers.First(t => t.Id == r.Trigger).Display}」缺少 choiceContext，生成时会被忽略。"));
                }
            }
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
            ValidateEffects(issues, $"药水「{s.Name}」", s.Effects, s.TargetType, ctx: "Potion", p: p);
            AddDuplicateVarNotice(issues, $"药水「{s.Name}」", s.Effects);

            if (s.Usage == "AnyTime" && s.Effects.Any(e => e.Kind is "Damage" or "Block" or "Draw" or "ApplyPower" or "TempPower"))
                issues.Add(new("警告", $"药水「{s.Name}」是「任意时机」，但含战斗内效果（伤害/格挡/抽牌/挂增益），战斗外会缺少战斗上下文；建议改「仅战斗中」。"));
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

        return issues;
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
    private static void ValidateVanillaKeywordRenames(List<ValidationIssue> issues, CharacterProfile p)
    {
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
            Scan($"遗物「{r.Name}」", r.Effects, forPotion: false);
            // 「伙伴攻击」必须挂在卡牌上：遗物没有「玩家选中的目标」
            // 新增的那批宠物效果同理（这一档只做在卡牌上，见 CSharpCodeGen.EmitRelicEffectOnce）。
            if (r.Effects.Any(e => e.Kind == "PetAttack"))
                issues.Add(new("错误", $"遗物「{r.Name}」里放了「伙伴攻击」—— 遗物没有「玩家选中的目标」，"
                    + "宠物该打谁说不清。请把它放到卡牌上（遗物只支持「召唤伙伴」）。"));
            var relicPetFx = r.Effects.Where(e => EffectCatalog.IsPetKind(e.Kind) && e.Kind != "SummonPet").ToList();
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
        bool anyCardOrRelicSummons = p.AllCards.Any(c => c.Effects.Any(e => e.Kind == "SummonPet"))
            || p.Relics.Any(r => r.Effects.Any(e => e.Kind == "SummonPet"));
        if (!anyCardOrRelicSummons)
            issues.Add(new("提示", $"召唤物已启用（{enabled.Count} 只），但没有任何卡牌 / 遗物在「召唤」它们"
                + "（卡牌效果里选「召唤伙伴」，或给遗物加一条「召唤伙伴」+ 触发时机「战斗开始时」）—— 游戏里永远不会上场。"));
        if (p.Relics.Any(r => r.Effects.Any(e => e.Kind == "SummonPet") && RelicTriggerLacksContext(r.Trigger)))
            issues.Add(new("警告", "有遗物在「" + string.Join(" / ", p.Relics
                    .Where(r => r.Effects.Any(e => e.Kind == "SummonPet") && RelicTriggerLacksContext(r.Trigger))
                    .Select(r => r.Name)) + "」上配了「召唤伙伴」，但那个触发时机的本体钩子拿不到 choiceContext"
                + "（只有「战斗开始时 / 每回合开始时 / 每回合结束时 / 受到伤害时」有）→ 生成时会被忽略。"
                + "想要「战斗开始时召唤伙伴」，请把触发时机改成「战斗开始时」。"));
    }

    /// <summary>这个遗物触发时机的本体钩子有没有 choiceContext（没有的话召唤伙伴实现不了）。</summary>
    private static bool RelicTriggerLacksContext(string? trigger) =>
        trigger is "CombatVictory" or "GoldGained";

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
            if (e.Amount < kind.Min || e.Amount > kind.Max)
            {
                // 有些效果种类**根本没有数值**（击晕 / 结束回合 / 额外回合 / 伙伴承伤开关…：
                // 允许范围就是个 0~0 的空区间）。这种填什么都不影响生成，别拿「超出范围」把用户拦住 ——
                // 用户实测踩过：卡牌里的「伙伴替主人承伤（开）」数值是 1，被这条错误挡住，得自己想到要填 0。
                if (kind.Min == kind.Max)
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」没有数值这一说：填的 {e.Amount} 会被忽略（生成时不看它）。"));
                else
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」数值 {e.Amount} 超出允许范围 [{kind.Min} ~ {kind.Max}]。"));
            }
            // 数值 0 的效果（用户要求）：游戏里**不显示、也不执行**。
            // 为什么不做成「写个 0 摆着」：本体的伤害 / 格挡会被力量 / 敏捷加成
            //（0 点伤害 + 3 点力量 = 3 点伤害），所以 0 必须真的「不存在」。
            if (CSharpCodeGen.IsInertZero(e))
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
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.Powers.Count == 0) { /* 效果库整体为空时由上面统一报错 */ }
            else if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.FindPower(e.PowerId) is null && !EffectCatalog.IsCustomPower(e.PowerId))
                issues.Add(new("错误", $"{owner} 的增益/减益未选择有效的 Power（可以选本体的状态，也可以选「自定义状态」页里自己造的那个）。"));
            // 本体里有些状态显示的数字不是层数（自己 override 了 PowerModel.DisplayAmount）：
            // 填多少层状态栏都不会显示你填的值（用户报过「施加 30 层缓慢，游戏里只看到缓慢」）
            if (e.Kind is "ApplyPower" or "TempPower" && EffectCatalog.PowerAmountNote(e.PowerId) is string amountNote)
                issues.Add(new("提示", $"{owner} 施加的是「{EffectCatalog.PowerName(e.PowerId)}」："
                    + $"本体这个状态显示的数字不是层数 —— {amountNote}"
                    + "（层数照旧记着，只是状态栏那个数字由它自己算。）"));
            // 给予卡牌关键词：关键词必须选、而且必须存在（本体枚举名 或 已定义的自定义关键词）
            if (e.Kind == "GiveKeyword")
                ValidateGiveKeyword(issues, owner, e, p, ctx);
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
            if (e.NextTurn && !kind.SupportsNextTurn)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」不支持「下回合生效」。"));
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
            // 「从哪里选牌」只有消耗 / 变化卡牌用得到；别的效果上填了会被忽略（界面里那一行也不显示）
            if (!e.UsesSelectPile && e.SelectPile != "Hand")
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」填了「从哪里选牌 = {e.SelectPileZh}」，"
                    + "但这个选项只有「消耗卡牌 / 变化卡牌 / 升级卡牌 / 丢弃卡牌 / 给予卡牌关键词 / 复制卡牌 / 重放卡牌 / 回合结束时自动打出」用得到 —— 这条会被忽略。"));
            // 「从牌堆拿牌到手牌」：本体「搜寻 / 全息影像 / 挖掘」那种
            if (e.Kind is "TakeFromDraw" or "TakeFromDiscard")
                issues.Add(new("提示", $"{owner} 的「{kind.Display}」会弹一个选牌界面，"
                    + $"从{(e.Kind == "TakeFromDraw" ? "抽牌堆" : "弃牌堆")}里自己挑 {Math.Max(1, (int)e.Amount)} 张拿到手牌"
                    + "（本体「搜寻 / 全息影像 / 挖掘」的做法，界面提示语会一起生成）；那一摞里没牌时什么都不做。"));
            if (!kind.NeedsTarget && e.TargetSide != "Self" && e.Kind != "Outbreak")
                issues.Add(new("警告", $"{owner} 的「{kind.Display}」作用对象固定为自己，选项将被忽略。"));
            if (e.RepeatCount is < 1 or > 20)
                issues.Add(new("错误", $"{owner} 的重复次数 {e.RepeatCount} 超出范围（1~20）。"));
            if (e.ChanceEnabled && e.ChancePercent is < 1 or > 100)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」概率 {e.ChancePercent} 超出范围（1~100，单位是 %）。"));
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

            // 生成 / 消耗 / 变化卡牌
            if (e.Kind == "GenerateCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                issues.Add(new("警告", $"{owner} 的「生成卡牌」没填目标卡，将默认生成 Shiv（静默猎手的小刀）。"));
            // 生成到「消耗牌堆」：说明一下它和另外三摞的区别（不参与抽牌，本场战斗结束也不会回来）
            if (e.Kind == "GenerateCard" && e.SpawnToPile == "Exhaust")
                issues.Add(new("提示", $"{owner} 的「生成卡牌」生成出来的牌会**直接进消耗牌堆**"
                    + "（不经过手牌 / 抽牌堆，所以抽不到它；用来触发「消耗时」「消耗牌堆里的牌」这类效果，"
                    + "或者只是做个计数）。"));
            // 张数范围：0 也允许（用户要求：所有效果种类都能填 0，0 = 不生效，由 IsInertZero 整条丢掉）
            if (e.Kind is "ExhaustCard" or "TransformCard" or "DiscardCard" && e.Amount is < 0 or > 9)
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」张数 {e.Amount} 超出范围（0~9，0 = 这条效果不生效）。"));
            // 从消耗牌堆「消耗」：那摞里的牌本来就已经消耗掉了，再消耗一次没有意义（不拦，只提示）
            if (e.Kind == "ExhaustCard" && e.SelectPile == "Exhaust")
                issues.Add(new("提示", $"{owner} 的「消耗卡牌」选的是**消耗牌堆**：那摞里的牌本来就已经被消耗了，"
                    + "再「消耗」一次没有任何变化（想要「把消耗牌堆里的牌拿回来」请用「从消耗牌堆拿牌到手牌」）。"));
            if (e.Kind == "TransformCard" && string.IsNullOrWhiteSpace(e.SpawnCardId))
                issues.Add(new("提示", $"{owner} 的「变化卡牌」没填目标卡 → 会变化成随机卡牌。"));

            // ===== 范围限定里的「自定义关键词那一组」（用户要求：把自定义关键词当卡的组用）=====
            if (e.IsSpawnRandom && EffectCatalog.IsKeywordFilter(e.SpawnFilter))
            {
                string groupName = EffectCatalog.KeywordFilterName(e.SpawnFilter);
                var group = EffectCatalog.KeywordGroupCards(p, e.SpawnFilter);
                if (group.Count == 0)
                    issues.Add(new("错误", $"{owner} 的「{kind.Display}」范围限定选了自定义关键词「{groupName}」，"
                        + "但**没有任何一张你自己的牌带这个关键词** —— 这一组是空的，效果会什么都不做。"
                        + "请到卡牌页给这一组牌勾上这个关键词，或者换一个范围限定。"));
                else
                    issues.Add(new("提示", $"{owner} 的「{kind.Display}」范围限定 = 自定义关键词「{groupName}」那一组，"
                        + $"共 {group.Count} 张牌（{string.Join("、", group.Take(6).Select(c => c.Name))}"
                        + (group.Count > 6 ? " …" : "") + "）。"
                        + "生成时会把这些牌直接列进候选表（关键词是我们自己的标注，游戏里没有对应枚举，"
                        + "只能在生成时按配置查出来）。"
                        + "注意：本体的候选过滤会排除「基础 / 先古 / 事件」稀有度的牌，所以起始的打击 / 防御不会出现在这一组里。"));
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
                if (ctx == "Power")
                    issues.Add(new("警告", $"{owner} 的「复制卡牌」在**自定义状态**里用不了，生成时会被忽略 —— 请把它放到卡牌或药水上。"));
                if (e.SelectPile == "Deck" || e.SelectPile is not ("Hand" or "Draw" or "Discard" or "Exhaust"))
                    issues.Add(new("错误", $"{owner} 的「复制卡牌」只能从手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆里复制"
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

            // 「按范围随机 / 生成出来的卡的附加处理」：自定义状态的触发器里走的是另一套 emitter
            // （那里的 base.Owner 是 Creature），只支持「指定卡」那种写法 —— 说清楚，别让用户以为配了没生效
            if (ctx == "Power" && e.UsesSpawnOptions && (e.IsSpawnRandom || e.HasSpawnModifier))
                issues.Add(new("错误", $"{owner} 的「{kind.Display}」用了「按范围随机」或「生成出来的卡怎么处理」，"
                    + "但**自定义状态的触发器里不支持**这两类设置（那里的生成链拿不到玩家那一侧的对象）—— "
                    + "请改用卡牌 / 遗物 / 药水，或者把这两项关掉、用「指定卡」。"));

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
            _ => false,
        };
        if (!allowed)
        {
            issues.Add(new("错误", ctx == "Potion"
                ? $"{owner}：药水不支持条件选项（条件要用在卡牌 / 遗物 / 自定义状态上）。"
                : $"{owner} 的条件「{opt.Display}」不能用在这里，请换一个。"));
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
