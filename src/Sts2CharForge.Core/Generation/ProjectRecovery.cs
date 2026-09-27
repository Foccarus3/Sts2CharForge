using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>恢复结果：配置 + 明细 + 没认出来的地方（要人工核对）。</summary>
public sealed class RecoveryResult
{
    public CharacterProfile Profile { get; init; } = new();

    /// <summary>恢复到了什么（给用户看的摘要）。</summary>
    public List<string> Notes { get; } = new();

    /// <summary>没认出来的生成代码语句（逐条列出来，方便人工补）。</summary>
    public List<string> Unparsed { get; } = new();

    public bool HasUnparsed => Unparsed.Count > 0;
}

/// <summary>
/// 【灾难恢复】从「生成出来的模组工程」反推回配置存档。
///
/// 为什么要这个：实测踩过「存档被误覆盖」——用户 20+ 张卡的存档变成了另一个存档的内容，
/// 而工具当时没有任何历史版本。好在生成出来的工程里带着全部信息（卡牌 / 遗物 / 药水 /
/// 自定义状态 / 角色属性 / 本地化名字），所以可以从工程反推出一个能直接用的存档。
///
/// 反推依据（都是生成器的输出格式，和 <see cref="CSharpCodeGen"/> 一一对应）：
///   · 卡牌：CanonicalVars（数值）+ 构造函数（费用 / 类型 / 稀有度）+ CanonicalKeywords（关键字）
///     + OnPlay（每条效果）+ OnUpgrade（升级增量 / 升级后费用）+ IsPlayable（条件）
///   · 名字：本地化表 &lt;ModId&gt;/localization/zhs/&lt;表&gt;.json
///   · 角色：&lt;角色&gt;.cs（血量 / 金币 / 起始卡组 / 起始遗物）+ 卡池（配色）
/// </summary>
public static class ProjectRecovery
{
    // ==================== 入口 ====================

    /// <summary>从工程目录恢复配置。<paramref name="template"/> 用来补「环境路径」这类没法从工程反推的字段。</summary>
    /// <param name="assetDir">给了就把工程里用到的图（额外资源量图标 / 状态图标）复制到
    /// <c>&lt;assetDir&gt;\_恢复素材</c>，并把配置里的路径指过去 —— 这样工程目录删了也不会缺图。</param>
    public static RecoveryResult FromProject(string projectDir, CharacterProfile? template = null, string? assetDir = null)
    {
        var result = new RecoveryResult { Profile = template is null ? new CharacterProfile() : CloneForTemplate(template) };
        var p = result.Profile;
        p.Cards.Clear();
        p.Relics.Clear();
        p.Potions.Clear();
        p.CustomPowers.Clear();
        p.VanillaPowerOverrides.Clear();
        p.Ancients.Clear();
        p.CustomKeywords.Clear();
        p.KeywordRenames.Clear();

        string cs = Path.Combine(projectDir, "cs");
        if (!Directory.Exists(cs)) throw new DirectoryNotFoundException("工程里没有 cs 目录，可能选错目录了：" + projectDir);

        // ---- 清单 + 本地化 ----
        string modId = Path.GetFileName(projectDir.TrimEnd('\\', '/'));
        string modName = "", author = "", version = "", description = "";
        string manifestPath = Path.Combine(projectDir, "mod_manifest.json");
        if (File.Exists(manifestPath))
        {
            var mf = Json.ReadDict(manifestPath);
            modId = mf.GetValueOrDefault("id", modId);
            modName = mf.GetValueOrDefault("name", "");
            author = mf.GetValueOrDefault("author", "");
            version = mf.GetValueOrDefault("version", "");
            description = mf.GetValueOrDefault("description", "");
        }
        p.ModId = modId;
        p.ModDisplayName = modName.Length > 0 ? modName : modId;

        var loc = LocTables.Load(projectDir, modId);

        // ---- 角色 ----
        string charFile = FindCharacterFile(cs);
        if (charFile.Length == 0) throw new FileNotFoundException("在 cs 里找不到角色类（没有继承 CharacterModel 的文件）");
        string charClass = Path.GetFileNameWithoutExtension(charFile);
        string charText = File.ReadAllText(charFile, Encoding.UTF8);
        p.CharacterClass = charClass;
        p.Gender = Match(charText, @"CharacterGender\.(\w+)") ?? "Masculine";
        p.StartingHp = Int(text: charText, pattern: @"StartingHp => (\d+)", fallback: 80);
        p.StartingGold = Int(text: charText, pattern: @"StartingGold => (\d+)", fallback: 99);
        p.AttackAnimDelay = (double)Dec(charText, @"AttackAnimDelay => ([\d.]+)f", 0.15m);
        p.CastAnimDelay = (double)Dec(charText, @"CastAnimDelay => ([\d.]+)f", 0.25m);
        string charEntry = EffectCatalog.SlugFor(charClass);
        p.DisplayName = loc.Get($"{charEntry}.title", p.ModDisplayName);
        p.Description = loc.Get($"{charEntry}.description", description);
        p.DeathText = loc.Get($"{charEntry}.eventDeathPrevention", "……还不到时候。");
        p.DeadBanterText = loc.Get($"{charEntry}.banter.dead.endTurnPing", "……");
        result.Notes.Add($"角色：{p.DisplayName}（{charClass}）HP {p.StartingHp} / 金币 {p.StartingGold}");

        // ---- 卡池：配色 + 是否入池 ----
        var removedFromPool = new HashSet<string>(StringComparer.Ordinal);
        string poolFile = Directory.GetFiles(cs, "*CardPool.cs").FirstOrDefault() ?? "";
        if (poolFile.Length > 0)
        {
            string poolText = File.ReadAllText(poolFile, Encoding.UTF8);
            p.Colors.DeckEntryColor = Hex(Match(poolText, @"DeckEntryCardColor => new Color\(""([0-9A-Fa-f]{6,8})""\)")) ?? "D62000";
            p.Colors.EnergyOutlineColor = Hex(Match(poolText, @"EnergyOutlineColor => new Color\(""([0-9A-Fa-f]{6,8})""\)")) ?? "802020";
            string frame = Match(poolText, @"CardFrameMaterialPath => ""([^""]+)""") ?? "";
            if (frame.EndsWith("_frame", StringComparison.Ordinal)) p.Colors.CardFrameColor = FrameColorFromMaterial(projectDir, frame) ?? "D62000";
            else p.Colors.CardFrame = frame;
            foreach (Match m in Regex.Matches(poolText, @"ModelDb\.Card<(\w+)>\(\)\.Id"))
                removedFromPool.Add(m.Groups[1].Value);
        }

        // ---- 起始卡组 / 起始遗物 ----
        var starting = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(charText, @"ModelDb\.Card<(\w+)>\(\)"))
            starting[m.Groups[1].Value] = starting.GetValueOrDefault(m.Groups[1].Value) + 1;
        var startingRelics = Regex.Matches(charText, @"ModelDb\.Relic<(\w+)>\(\)").Select(m => m.Groups[1].Value).ToList();

        // ---- 自定义状态（先解析：卡牌效果里要按类名引用它们） ----
        string powerDir = Path.Combine(cs, "Powers");
        if (Directory.Exists(powerDir))
            foreach (string file in Sorted(Directory.GetFiles(powerDir, "*.cs")))
                p.CustomPowers.Add(ParseCustomPower(file, p, loc, result));

        // ---- 本体状态改写（要在「状态名 → 类名」的映射之前解析：改名会用到它） ----
        ParseVanillaOverrides(projectDir, cs, p, loc, result);

        // ---- 状态名 → 类名（条件文案里写的是中文名，得反查回去） ----
        var nameToPowerId = BuildPowerNameMap(p);

        // ---- 自定义关键词（要在卡牌之前：卡牌会引用它们） ----
        ParseCustomKeywords(projectDir, modId, p);

        // ---- 召唤伙伴（也要在卡牌之前：这里只是把配置拿回来，卡牌的效果回读不依赖它） ----
        ParseSummon(projectDir, cs, p, result);

        // ---- 卡牌 ----
        foreach (string file in Sorted(Directory.GetFiles(Path.Combine(cs, "Cards"), "*.cs")))
        {
            var card = ParseCard(file, p, loc, nameToPowerId, result);
            card.InCardPool = !removedFromPool.Contains(card.ClassName);
            if (starting.TryGetValue(card.ClassName, out int copies))
            {
                card.InStartingDeck = true;
                card.StartingCopies = copies;
            }
            p.Cards.Add(card);
        }

        // ---- 遗物 ----
        string relicDir = Path.Combine(cs, "Relics");
        if (Directory.Exists(relicDir))
        {
            foreach (string file in Sorted(Directory.GetFiles(relicDir, "*.cs")))
            {
                if (Path.GetFileName(file) == "ExtraResourceRelic.cs") continue;
                var relic = ParseRelic(file, p, loc, nameToPowerId, result);
                relic.IsStartingRelic = startingRelics.Contains(relic.ClassName);
                p.Relics.Add(relic);
            }
        }

        // ---- 药水 ----
        string potionDir = Path.Combine(cs, "Potions");
        if (Directory.Exists(potionDir))
            foreach (string file in Sorted(Directory.GetFiles(potionDir, "*.cs")))
                p.Potions.Add(ParsePotion(file, p, loc, nameToPowerId, result));

        // ---- 额外资源量 ----
        ParseExtraResource(projectDir, cs, p, loc, result);

        // ---- 初始卡组里的**本体卡**（打击 ×5 / 防御 ×5）----
        // 生成的代码里它们是 ModelDb.Card<StrikeIronclad>()，没有对应的 cs/Cards/*.cs，
        // 所以上面那轮扫不到 —— 这里补成「本体卡引用」条目（只在初始卡组里用本体的英文类名，不生成自己的类）。
        foreach (var kv in starting)
        {
            if (p.Cards.Any(c => string.Equals(c.ClassName, kv.Key, StringComparison.Ordinal))) continue;
            p.Cards.Add(new CardSpec
            {
                Name = EffectCatalog.Cards.FirstOrDefault(x => x.Id == kv.Key)?.Zh ?? kv.Key,
                ClassName = kv.Key,
                IsVanillaCard = true,
                InStartingDeck = true,
                StartingCopies = kv.Value,
            });
            result.Notes.Add($"初始卡组里的本体卡：{kv.Key} ×{kv.Value}（按「本体卡引用」恢复，不会生成自己的卡类）");
        }

        // ---- 顺序：以「池子」里的顺序为准（= 存档里的顺序）----
        ReorderByPool(p, projectDir, cs, result);

        // ---- 先古之民对话 ----
        ParseAncients(cs, p, loc, result);

        // ---- 把用到的图复制到存档旁边（工程目录以后删了也不缺图） ----
        if (!string.IsNullOrWhiteSpace(assetDir)) CopyAssets(p, assetDir!, result);

        result.Notes.Add($"卡牌 {p.Cards.Count(c => !c.IsVanillaCard)} 张（初始牌 {p.Cards.Count(c => c.InStartingDeck && !c.IsVanillaCard)} 张 / 不入池 {p.Cards.Count(c => !c.IsVanillaCard && !c.InCardPool)} 张）"
            + (p.Cards.Any(c => c.IsVanillaCard) ? $" + 本体卡引用 {p.Cards.Count(c => c.IsVanillaCard)} 条" : "")
            + $"、遗物 {p.Relics.Count} 个、药水 {p.Potions.Count} 个、自定义状态 {p.CustomPowers.Count} 个");
        return result;
    }

    /// <summary>
    /// 把卡牌 / 遗物 / 药水按「池子里的顺序」重排。
    /// 为什么需要：显式写了英文类名的卡（SevenCrush 这种）文件名里没有序号，
    /// 只按文件名排会变成字母序，和存档里的原始顺序对不上（界面上的牌序就乱了）。
    /// 池子的 GenerateAllCards/GenerateAllRelics/GenerateAllPotions 是按存档顺序生成的，用它当准。
    /// </summary>
    private static void ReorderByPool(CharacterProfile p, string projectDir, string cs, RecoveryResult result)
    {
        string? PoolOrder(string suffix, string marker, string call)
        {
            string? file = Directory.GetFiles(cs, "*" + suffix + ".cs").FirstOrDefault();
            if (file is null) return null;
            string block = BlockAfter(File.ReadAllText(file, Encoding.UTF8), marker);
            var ids = Regex.Matches(block, @"ModelDb\." + call + @"<(\w+)>\(\)").Select(m => m.Groups[1].Value).ToList();
            return ids.Count == 0 ? null : string.Join("|", ids);
        }
        void Apply<T>(IList<T> items, string? order, Func<T, string> name)
        {
            if (order is null) return;
            var ids = order.Split('|').ToList();
            var sorted = items.OrderBy(x => ids.IndexOf(name(x)) is int i && i >= 0 ? i : int.MaxValue).ToList();
            items.Clear();
            foreach (var item in sorted) items.Add(item);
        }
        Apply(p.Cards, PoolOrder("CardPool", "GenerateAllCards()", "Card"), c => c.ClassName);
        // 初始卡组的打击 / 防御（类名 Strike / Defend）固定排在列表最前面，另外旧工程里的
        // 「本体卡引用」行（IsVanillaCard）也一起挪到前面 —— 和界面上的默认顺序一致。
        var basicsFirst = p.Cards.Where(c => c.IsVanillaCard
                || string.Equals(c.ClassName, "Strike", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ClassName, "Defend", StringComparison.OrdinalIgnoreCase)).ToList();
        if (basicsFirst.Count > 0)
        {
            bool alreadyFirst = string.Equals(p.Cards[0].ClassName, "Strike", StringComparison.OrdinalIgnoreCase)
                                || p.Cards[0].IsVanillaCard;
            if (!alreadyFirst)
            {
                basicsFirst = basicsFirst
                    .OrderBy(c => string.Equals(c.ClassName, "Strike", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
                foreach (var b in basicsFirst) p.Cards.Remove(b);
                for (int i = basicsFirst.Count - 1; i >= 0; i--) p.Cards.Insert(0, basicsFirst[i]);
            }
        }
        Apply(p.Relics, PoolOrder("RelicPool", "GenerateAllRelics()", "Relic"), r => r.ClassName);
        Apply(p.Potions, PoolOrder("PotionPool", "GenerateAllPotions()", "Potion"), s => s.ClassName);
    }

    /// <summary>把配置里引用到的图片复制到 <c>&lt;assetDir&gt;\_恢复素材</c> 并改写路径。</summary>
    private static void CopyAssets(CharacterProfile p, string assetDir, RecoveryResult result)
    {
        string dir = Path.Combine(assetDir, "_恢复素材");
        int copied = 0;
        string? Move(string? src)
        {
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return src;
            try
            {
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir, Path.GetFileName(src));
                File.Copy(src, dst, true);
                copied++;
                return dst;
            }
            catch { return src; }
        }
        p.ExtraResource.Icon = Move(p.ExtraResource.Icon);
        p.Art.CharacterStatic = Move(p.Art.CharacterStatic);
        p.Art.Icon = Move(p.Art.Icon);
        p.Art.IconOutline = Move(p.Art.IconOutline);
        p.Art.SelectIcon = Move(p.Art.SelectIcon);
        p.Art.SelectBackground = Move(p.Art.SelectBackground);
        p.Art.SelectIconLocked = Move(p.Art.SelectIconLocked);
        p.Art.MapMarker = Move(p.Art.MapMarker);
        p.Art.EnergyIcon = Move(p.Art.EnergyIcon);
        p.Art.Transition = Move(p.Art.Transition);
        foreach (var key in p.Art.CardPortraits.Keys.ToList()) p.Art.CardPortraits[key] = Move(p.Art.CardPortraits[key]) ?? "";
        foreach (var ov in p.VanillaPowerOverrides) ov.Icon = Move(ov.Icon);
        foreach (var cp in p.CustomPowers) cp.Icon = Move(cp.Icon);
        foreach (var r in p.Relics) r.Icon = Move(r.Icon);
        foreach (var s in p.Potions) s.Icon = Move(s.Icon);
        // 召唤伙伴的宠物图也是「工程目录删了就没了」的素材，一起搬到存档旁边
        if (p.Summon is not null) p.Summon.Image = Move(p.Summon.Image);
        if (copied > 0) result.Notes.Add($"素材 {copied} 个已复制到「{dir}」（配置里已指向这里）");
    }

    private static CharacterProfile CloneForTemplate(CharacterProfile t)
    {
        // 只借用「环境路径 / 素材路径」这些工程里反推不出来的东西
        return new CharacterProfile
        {
            // 召唤伙伴的宠物图路径借过来（工程里只能反推出「有没有」，反推不出用户原来选的是哪张图）
            Summon = new SummonSpec { Image = t.Summon?.Image },
            Paths = new PathsSpec
            {
                VanillaProject = t.Paths.VanillaProject,
                GameDataDir = t.Paths.GameDataDir,
                GodotExe = t.Paths.GodotExe,
                DotnetExe = t.Paths.DotnetExe,
                OutputDir = t.Paths.OutputDir,
                InstallDir = t.Paths.InstallDir,
            },
            Art = new ArtSpec
            {
                CharacterStatic = t.Art.CharacterStatic,
                Icon = t.Art.Icon,
                IconOutline = t.Art.IconOutline,
                IconOutlineColor = t.Art.IconOutlineColor,
                SelectIcon = t.Art.SelectIcon,
                SelectBackground = t.Art.SelectBackground,
                SelectIconLocked = t.Art.SelectIconLocked,
                MapMarker = t.Art.MapMarker,
                EnergyIcon = t.Art.EnergyIcon,
                Transition = t.Art.Transition,
                UseVanillaPlaceholders = t.Art.UseVanillaPlaceholders,
                CardPortraits = new Dictionary<string, string>(t.Art.CardPortraits),
            },
        };
    }

    // ==================== 卡牌 ====================

    private static CardSpec ParseCard(string file, CharacterProfile profile, LocTables loc,
        Dictionary<string, string> nameToPowerId, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = File.ReadAllText(file, Encoding.UTF8);
        string entry = EffectCatalog.SlugFor(cls);
        var card = new CardSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
        };

        var ctor = Regex.Match(text, @": base\((\d+), CardType\.(\w+), CardRarity\.(\w+), TargetType\.(\w+)\)");
        if (ctor.Success)
        {
            card.Cost = int.Parse(ctor.Groups[1].Value, CultureInfo.InvariantCulture);
            card.CardType = ctor.Groups[2].Value;
            card.Rarity = ctor.Groups[3].Value;
        }
        if (text.Contains("HasEnergyCostX => true")) card.CostIsX = true;
        if (text.Contains("HasStarCostX => true")) card.StarCostIsX = true;
        if (text.Contains("ResolveEnergyXValue() + (base.IsUpgraded ? 1 : 0)")) p_xPlus(card);

        var vars = ParseVars(text);
        ParseEffects(card.Effects, BodyOf(text, "OnPlay"), vars, nameToPowerId, EffectCtx.Card, result, cls);
        ParseUpgrade(card, vars, text, result, cls);
        ParseKeywords(card, text);
        // 自定义关键词：生成的 ExtraHoverTips 里写的是 new LocString("card_keywords", "<KEY>.title")
        var keywordRefs = Regex.Matches(text, @"LocString\(""card_keywords"", ""([^""]+)\.title""\)")
            .Select(m => m.Groups[1].Value)
            .Where(x => !KeywordGen.IsReservedKey(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keywordRefs.Count > 0) card.KeywordIds = keywordRefs;

        string starCost = Match(text, @"CanonicalStarCost => (\d+)");
        if (starCost is not null)
            card.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -int.Parse(starCost, CultureInfo.InvariantCulture), TargetSide = "Self" });
        else if (card.StarCostIsX && !card.Effects.Any(e => e.Kind == "ExtraResource" && e.Amount < 0))
        {
            // 「额外资源量费用为 X」：老工程里这笔开销不生成代码（走本体 HasStarCostX），
            // 但配置里得有一条负数效果，卡面才会写「需要 X 点…」—— 补一条 -1（X 费时数额不影响玩法）
            card.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -1, TargetSide = "Self" });
        }

        ParseCardCondition(card, text, nameToPowerId, result, cls);
        return card;

        static void p_xPlus(CardSpec c) => c.XPlusOnUpgrade = true;
    }

    /// <summary>CanonicalVars 里声明的数值变量（顺序 = 效果顺序）。</summary>
    private sealed record Var(string Kind, string? PowerId, decimal Amount, bool IsCards, bool IsEnergy);

    private static List<Var> ParseVars(string text)
    {
        var list = new List<Var>();
        string block = BlockAfter(text, "CanonicalVars =>");
        // 召唤伙伴 / 伙伴攻击：生成的是**带名字的普通 DynamicVar**（new DynamicVar("PetDamage", 6m)），
        // 名字是我们自己起的，所以先把它们捞出来（否则会被下面的数字正则当成「名字不是数字」而漏掉）
        foreach (Match m in Regex.Matches(block, @"new DynamicVar\(""(\w+)"",\s*(-?[\d.]+)m?\)"))
            list.Add(new Var("DynamicVar", m.Groups[1].Value,
                decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), false, false));
        foreach (Match m in Regex.Matches(block, @"new (\w+)Var(<(\w+)>)?\((-?[\d.]+)m?[^)]*\)"))
        {
            string kind = m.Groups[1].Value;
            if (kind == "Dynamic") continue;   // 上面那条已经处理过带名字的 DynamicVar
            string? power = m.Groups[3].Success ? m.Groups[3].Value : null;
            decimal amount = decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            list.Add(new Var(kind, power, amount, kind == "Cards", kind == "Energy"));
        }
        return list;
    }

    /// <summary>生成代码里的一层块（循环 / 条件）。</summary>
    private sealed record Block(bool IsLoop, string Count, ConditionSpec? Cond);

    /// <summary>把效果语句反推成 EffectSpec（本文件的核心）。</summary>
    private static void ParseEffects(IList<EffectSpec> into, string body, List<Var> vars,
        Dictionary<string, string> nameToPowerId, EffectCtx ctx, RecoveryResult result, string where)
    {
        var lines = body.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var frames = new List<Block>();          // 当前所在的块（循环 / 条件），按嵌套顺序
        int varIdx = 0;
        int hitsLiteral = 1;                     // 前面出现过的 int hits = N;
        bool randomFoes = false;                 // 出现了 List<Creature> foes（随机挑敌人的写法）
        bool foesRemoved = false;                // 随机挑敌人且不允许重复
        int i = 0;

        string? LoopTop() => frames.LastOrDefault(f => f.IsLoop)?.Count;
        ConditionSpec? CondTop() => frames.LastOrDefault(f => f.Cond is not null)?.Cond;

        // 收尾：把「每条效果自己的条件」带上（生成器是用一层 if 包的）
        void Done(EffectSpec e)
        {
            e.Condition = CondTop() ?? new ConditionSpec();
            into.Add(e);
        }

        while (i < lines.Count)
        {
            string raw = lines[i];
            string line = raw.Trim();
            i++;
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line == "{") continue;
            if (line.EndsWith("{"))
            {
                string head = line[..^1].Trim();
                if (head.StartsWith("for (", StringComparison.Ordinal) || head.StartsWith("foreach (", StringComparison.Ordinal))
                {
                    frames.Add(new Block(true, LoopCount(head), null));
                }
                else if (head.StartsWith("if (", StringComparison.Ordinal))
                {
                    // 每条效果自己的条件：生成器就是这么包一层 if 的
                    string expr = head.Length > 4 ? head[4..].Trim() : "";
                    if (expr.EndsWith(")")) expr = expr[..^1];
                    frames.Add(new Block(false, "", ConditionFromExpr(expr, nameToPowerId)));
                }
                continue;
            }
            if (line == "}")
            {
                if (frames.Count > 0) frames.RemoveAt(frames.Count - 1);
                continue;
            }
            if (line.StartsWith("int hits = "))
            {
                hitsLiteral = (int)Dec(line, @"int hits = ([\d.]+)", 1);
                continue;
            }
            if (line.Contains("foes.Remove(foe)")) { foesRemoved = true; continue; }
            if (line.StartsWith("List<Creature> foes")) { randomFoes = true; foesRemoved = false; continue; }
            if (line.StartsWith("ArgumentNullException")) continue;
            if (line.StartsWith("int x = ")) continue;
            // 选牌 / 变化的前置语句（真正的动作在后面的 foreach + await 里）
            if (line.StartsWith("var toTransform") || line.StartsWith("var toExhaust") || line.StartsWith("var pick")) continue;

            // 召唤伙伴：生成的是 `<X>Cmd.Summon(choiceContext, base.Owner, <血量>); // CET:PetHp=…`
            //
            // 两种情况：① 数值留空 → 直接写「角色」页配置的血量常量，标记是 CET:PetHp=configured，
            //             恢复成数值 0（保真：用户在界面上看到的就是留空）；
            //           ② 填了数值 → 走动态变量（base.DynamicVars["PetHp"].BaseValue），
            //             这样升级增量也能跟着回来。
            // 为什么要那个注释标记：两种情况的调用长得一模一样（配置血量是多少就写多少 m），
            // 不标记就分不清「留空用配置」和「填了同样的数字」。
            if (line.Contains("Cmd.Summon(choiceContext, base.Owner,"))
            {
                var e = new EffectSpec { Kind = "SummonPet", TargetSide = "Self" };
                if (!line.Contains("CET:PetHp=configured"))
                {
                    string arg = ArgAt(line, 2);
                    // 填了数值时生成的是 base.DynamicVars["PetHp"].BaseValue → 按**名字**去 CanonicalVars
                    // 里取那个值（这样升级增量也能跟着回来）。按名字查而不是按顺序捞：
                    // 顺序捞会被别的效果把变量吃掉（PetHp 只有一个，名字是唯一的）。
                    string? varName = Match(arg, @"base\.DynamicVars\[""(\w+)""\]");
                    Var? hit = varName is null
                        ? null
                        : vars.FirstOrDefault(v => v.Kind == "DynamicVar"
                            && string.Equals(v.PowerId, varName, StringComparison.Ordinal));
                    if (hit is not null) e.Amount = hit.Amount;
                    else FillExpr(e, arg);
                }
                ApplyLoop(e, frames);
                into.Add(e);   // 没有「效果级条件」（宠物守卫不是条件）
                continue;
            }

            // 攻击链：多行
            if (line.StartsWith("await DamageCmd.Attack(", StringComparison.Ordinal))
            {
                var chain = new StringBuilder(line);
                while (!line.EndsWith(";") && i < lines.Count)
                {
                    line = lines[i].Trim();
                    i++;
                    chain.Append(' ').Append(line);
                }
                string ch = chain.ToString();
                // 「伙伴攻击」也是 DamageCmd.Attack 链，区别只在 attacker 是宠物（.FromMonster(...)）。
                // 目标解析和下面普通伤害完全一样，所以这里分一次流就行。
                bool fromMonster = ch.Contains(".FromMonster(");
                var e = new EffectSpec { Kind = fromMonster ? "PetAttack" : "Damage", TargetSide = "Enemy" };
                if (ch.Contains(".TargetingAllOpponents")) e.TargetSide = "AllEnemies";
                else if (ch.Contains(".TargetingRandomOpponents"))
                {
                    e.TargetSide = "RandomEnemies";
                    e.AllowDuplicates = ch.Contains("allowDuplicates: true");
                }
                string hits = Match(ch, @"\.WithHitCount\(([^)]*)\)") ?? "1";
                if (e.TargetSide == "RandomEnemies")
                {
                    if (hits == "x") e.RepeatIsX = true;
                    else if (hits == "hits") e.RepeatCount = hitsLiteral;
                    else e.RepeatCount = (int)Dec(hits, @"([\d.]+)", 1);
                }
                else
                {
                    if (hits == "x") e.RepeatIsX = true;
                    else e.RepeatCount = Math.Max(1, (int)Dec(hits, @"([\d.]+)", 1));
                }
                // 变量：普通伤害是 DamageVar，伙伴攻击是我们自己起的 PetDamage（都在 CanonicalVars 里）
                FillAmount(e, NextVar(vars, ref varIdx, fromMonster ? "PetDamage" : "Damage"), nameToPowerId);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("CreatureCmd.GainBlock(", StringComparison.Ordinal) || line.StartsWith("await CreatureCmd.GainBlock(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Block" };
                FillAmount(e, NextVar(vars, ref varIdx, "Block"), nameToPowerId);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await CardPileCmd.Draw(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Draw" };
                FillAmount(e, NextVar(vars, ref varIdx, "Cards"), nameToPowerId, fallbackExpr: ArgAt(line, 1));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.GainEnergy(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Energy" };
                FillAmount(e, NextVar(vars, ref varIdx, "Energy"), nameToPowerId, fallbackExpr: ArgAt(line, 0));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.GainGold(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Gold" };
                FillExpr(e, ArgAt(line, 0));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.GainStars(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "ExtraResource" };
                string starArg = ArgAt(line, 0);
                // 有 StarsVar 就取变量值（升级增量在 OnUpgrade 里）；否则解析表达式（X / 字面量 / 三目）
                if (starArg.Contains("base.DynamicVars")) FillAmount(e, NextVar(vars, ref varIdx, "Stars"), nameToPowerId);
                else FillExpr(e, starArg);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.SetStars(", StringComparison.Ordinal))
            {
                // SetStars(Max(0, stars - N), owner) → 扣 N 点
                var e = new EffectSpec { Kind = "ExtraResource" };
                string arg = ArgAt(line, 0);
                string? n = Match(arg, @"-\s*([\d.]+)m");
                e.Amount = n is null ? 0 : -decimal.Parse(n, CultureInfo.InvariantCulture);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.EndTurn(", StringComparison.Ordinal) || line.StartsWith("PlayerCmd.EndTurn(", StringComparison.Ordinal))
            {
                into.Add(new EffectSpec { Kind = "EndTurn" });
                continue;
            }

            if (line.StartsWith("await CreatureCmd.Heal(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Heal" };
                FillExpr(e, ArgAt(line, 1));
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.GainMaxHp(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "MaxHp" };
                FillExpr(e, ArgAt(line, 1));
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.LoseMaxHp(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "MaxHp" };
                FillExpr(e, ArgAt(line, 2));
                e.Amount = -e.Amount;
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, base.Owner", StringComparison.Ordinal))
            {
                // 自己吃伤害：本体里 Heal(负数) 和 HpLoss 生成的是同一段代码，这里统一按「失去生命」
                var e = new EffectSpec { Kind = "HpLoss" };
                FillExpr(e, ArgAt(line, 2));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PowerCmd.Apply<", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"await PowerCmd\.Apply<(\w+)>\(([^;]*)\);");
                if (!m.Success) { result.Unparsed.Add($"{where}: {line}"); continue; }
                string power = m.Groups[1].Value;
                string args = m.Groups[2].Value;
                var parts = SplitArgs(args);
                string target = parts.Count > 1 ? parts[1].Trim() : "";
                var e = new EffectSpec { Kind = "ApplyPower", PowerId = power };
                if (power == "ForgeExtraTurnPower")
                {
                    e.Kind = "ExtraTurn";
                    e.PowerId = null;
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                if (power == "BlockNextTurnPower") { e.Kind = "Block"; e.NextTurn = true; }
                else if (power == "DrawCardsNextTurnPower" || power.StartsWith("ForgeDelayedDraw", StringComparison.Ordinal)) { e.Kind = "Draw"; e.NextTurn = true; }
                else if (power == "EnergyNextTurnPower" || power.StartsWith("ForgeDelayedEnergy", StringComparison.Ordinal)) { e.Kind = "Energy"; e.NextTurn = true; }
                else if (power.StartsWith("ForgeDelayed", StringComparison.Ordinal)) { e.NextTurn = true; }

                if (e.Kind == "ApplyPower")
                {
                    // 目标：cardPlay.Target = 单体敌人；HittableEnemies 整串 = 全体；
                    //      foe 出现在「随机挑敌人」的循环里 = 随机敌人，出现在 foreach 里 = 全体
                    if (target.Contains("cardPlay.Target")) e.TargetSide = "Enemy";
                    else if (target.Contains("HittableEnemies")) e.TargetSide = "AllEnemies";
                    else if (target == "foe") e.TargetSide = randomFoes ? "RandomEnemies" : "AllEnemies";
                    else e.TargetSide = "Self";
                    if (e.TargetSide == "RandomEnemies")
                    {
                        e.AllowDuplicates = !foesRemoved;
                        ApplyLoop(e, frames, randomHits: true);
                    }
                    else ApplyLoop(e, frames);
                }
                else
                {
                    if (e.NextTurn) e.TargetSide = "Self";
                    ApplyLoop(e, frames);
                }

                string amountExpr = parts.Count > 2 ? parts[2].Trim() : "0m";
                var v = NextVar(vars, ref varIdx, e.Kind switch
                {
                    "Block" => "Block",
                    "Draw" => "Cards",
                    "Energy" => "Energy",
                    _ => "Power:" + power,
                });
                FillAmountOrExpr(e, amountExpr, v, nameToPowerId, power);
                Done(e);
                continue;
            }

            if (line.StartsWith("await CardPileCmd.AddToCombatAndPreview<", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"AddToCombatAndPreview<(\w+)>\(([^;]*)\);");
                var parts = m.Success ? SplitArgs(m.Groups[2].Value) : new List<string>();
                var e = new EffectSpec
                {
                    Kind = "GenerateCard",
                    SpawnCardId = m.Success ? m.Groups[1].Value : null,
                    SpawnTo = parts.Count > 1 ? parts[1].Trim().Replace("PileType.", "") : "Hand",
                };
                if (parts.Count > 2) FillExpr(e, parts[2]);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await CardCmd.Transform(", StringComparison.Ordinal))
            {
                // 「自己选」走 foreach (… in toTransform)；「随机」走循环 + TransformToRandom
                bool chosenTransform = body.Contains("in toTransform");
                var e = new EffectSpec { Kind = "TransformCard", CardPick = chosenTransform ? "Chosen" : "Random" };
                string? target = Match(line, @"CreateCard<(\w+)>");
                e.SpawnCardId = target;
                string? count = chosenTransform ? Match(body, @"TransformSelectionPrompt, (\d+)") : null;
                if (count is not null) e.Amount = decimal.Parse(count, CultureInfo.InvariantCulture);
                else e.Amount = LoopValue(LoopTop() ?? "1");
                Done(e);
                continue;
            }

            if (line.StartsWith("await CardCmd.Exhaust(", StringComparison.Ordinal))
            {
                // 「自己选」走 foreach (… in toExhaust)；「随机」走循环 + NextItem(手牌)
                bool chosenExhaust = body.Contains("in toExhaust");
                var e = new EffectSpec
                {
                    Kind = "ExhaustCard",
                    CardPick = chosenExhaust ? "Chosen" : "Random",
                };
                string? count = chosenExhaust ? Match(body, @"ExhaustSelectionPrompt, (\d+)") : null;
                e.Amount = count is not null
                    ? decimal.Parse(count, CultureInfo.InvariantCulture)
                    : (LoopValue(LoopTop() ?? "1"));
                Done(e);
                continue;
            }

            if (line.StartsWith("await CardPileCmd.Draw(choiceContext, x, ") && ctx == EffectCtx.Card)
            {
                into.Add(new EffectSpec { Kind = "Draw", AmountIsX = true });
                continue;
            }

            // 「战斗胜利后」的卡牌奖励：生成的是本体那套战斗奖励（room.AddExtraReward + new CardReward(选项, N, 玩家)）
            if (line.Contains("AddExtraReward(") && line.Contains("CardReward("))
            {
                var e = new EffectSpec { Kind = "CardReward", TargetSide = "Self" };
                string? n = Match(line, @"CardReward\(__rewardOptions, (\d+),");
                e.Amount = n is not null ? decimal.Parse(n, CultureInfo.InvariantCulture) : LoopValue(LoopTop() ?? "1");
                // 勾了「生效次数 = 层数」时外面包着 for (int i = 0; i < (int)base.Amount; i++)
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 认不出来的语句 → 记下来让用户核对（不静默丢）
            if (line.Contains("await ") || line.Contains("PlayerCmd.") || line.Contains("CardCmd.") || line.Contains("CreatureCmd.")
                || line.Contains("AddExtraReward(") || line.Contains("RunState.CreateCard("))
                result.Unparsed.Add($"{where}: {line}");
        }

        // 兜底：按「召唤命令」再扫一遍（注释里带 <c>&lt;T&gt;</c> 之类的泛型括号时，
        // 逐行匹配可能整行都没落进上面的分支，那样就会静默丢掉一条「召唤伙伴」效果）
        if (ctx != EffectCtx.Potion)
        {
            foreach (var raw in lines)
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("//")) continue;
                if (!l.Contains("Cmd.Summon(choiceContext, base.Owner,")) continue;
                if (into.Any(x => x.Kind == "SummonPet")) continue;   // 上面已经认出来了，别重复加
                var e = new EffectSpec { Kind = "SummonPet", TargetSide = "Self" };
                if (!l.Contains("CET:PetHp=configured")) FillExpr(e, ArgAt(l, 2));
                into.Add(e);
            }
        }

        static bool Chosen(string text) => text.Contains("CardSelectCmd.FromHand");
    }

    private static void ApplyLoop(EffectSpec e, List<Block> frames, bool randomHits = false)
    {
        string? top = frames.LastOrDefault(f => f.IsLoop)?.Count;
        if (top is null) return;
        if (top == "x")
        {
            if (randomHits) e.RepeatIsX = true;
            else e.TimesIsX = true;
            return;
        }
        if (int.TryParse(top, out int n) && n > 1)
        {
            if (randomHits) e.RepeatCount = n;
            else e.Times = n;
        }
    }

    private static string LoopCount(string head)
    {
        string? m = Match(head, @"i < (\d+)");
        if (m is not null) return m;
        if (head.Contains("i < x")) return "x";
        if (head.Contains("(int)base.Amount")) return "stack";
        return "?";
    }

    private static int LoopValue(string s) => int.TryParse(s, out int n) ? n : 1;

    private static Var? NextVar(List<Var> vars, ref int idx, string kind)
    {
        for (int i = idx; i < vars.Count; i++)
        {
            var v = vars[i];
            bool match = kind switch
            {
                "Damage" => v.Kind == "Damage",
                "Block" => v.Kind == "Block",
                "Cards" => v.IsCards,
                "Energy" => v.IsEnergy,
                "Stars" => v.Kind == "Stars",
                // 伙伴攻击：生成时用的是我们自己起名的普通 DynamicVar "PetDamage"（不是 DamageVar）
                "PetDamage" => v.Kind == "DynamicVar" && string.Equals(v.PowerId, "PetDamage", StringComparison.Ordinal),
                _ when kind.StartsWith("Power:") => v.Kind == "Power" && string.Equals(v.PowerId, kind[6..], StringComparison.Ordinal),
                _ => false,
            };
            if (match)
            {
                idx = i + 1;
                return v;
            }
        }
        return null;
    }

    private static void FillAmount(EffectSpec e, Var? v, Dictionary<string, string> nameToPowerId, string? fallbackExpr = null)
    {
        if (v is not null) e.Amount = v.Amount;
        else if (fallbackExpr is not null) FillExpr(e, fallbackExpr);
    }

    private static void FillAmountOrExpr(EffectSpec e, string expr, Var? v, Dictionary<string, string> nameToPowerId, string power)
    {
        if (expr.Contains("base.DynamicVars")) { if (v is not null) e.Amount = v.Amount; return; }
        FillExpr(e, expr);
    }

    private static void FillExpr(EffectSpec e, string expr)
    {
        expr = expr.Trim();
        var up = Regex.Match(expr, @"\(base\.IsUpgraded \? ([\d.]+)m? : ([\d.]+)m?\)");
        if (up.Success)
        {
            e.Amount = decimal.Parse(up.Groups[2].Value, CultureInfo.InvariantCulture);
            e.UpgradeAmount = decimal.Parse(up.Groups[1].Value, CultureInfo.InvariantCulture) - e.Amount;
            return;
        }
        if (expr == "x") { e.AmountIsX = true; return; }
        if (expr == "base.Amount") { e.AmountIsStack = true; return; }
        if (expr.Contains("(int)base.Amount")) { e.TimesIsStack = true; return; }
        var m = Regex.Match(expr, @"^(-?[\d.]+)m?$");
        if (m.Success) e.Amount = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>OnUpgrade 里的升级增量 / 升级后费用 / 升级后关键字。</summary>
    private static void ParseUpgrade(CardSpec card, List<Var> vars, string text, RecoveryResult result, string where)
    {
        string body = BodyOf(text, "OnUpgrade");
        if (body.Length == 0) return;
        foreach (Match m in Regex.Matches(body, @"base\.DynamicVars\[""(\w+)""\]\.UpgradeValueBy\((-?[\d.]+)m\)"))
        {
            string name = m.Groups[1].Value;
            decimal delta = decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            // 变量名 → 效果（按 CanonicalVars 里的顺序对应效果顺序）
            var e = EffectByVarName(card, vars, name);
            if (e is not null) e.UpgradeAmount += delta;
            else result.Unparsed.Add($"{where}: 升级增量找不到对应效果（变量 {name}）");
        }
        string? costDelta = Match(body, @"EnergyCost\.UpgradeBy\((-?\d+)\)");
        if (costDelta is not null) card.UpgradeCost = card.Cost + int.Parse(costDelta, CultureInfo.InvariantCulture);
        foreach (Match m in Regex.Matches(body, @"AddKeyword\(CardKeyword\.(\w+)\)"))
        {
            string field = m.Groups[1].Value;
            if (KeywordUpgradeSpec.All.Any(k => k.Keyword == field)) card.UpgradeKeywords.Set(field, KeywordUpgradeSpec.Add);
        }
        foreach (Match m in Regex.Matches(body, @"RemoveKeyword\(CardKeyword\.(\w+)\)"))
        {
            string field = m.Groups[1].Value;
            if (KeywordUpgradeSpec.All.Any(k => k.Keyword == field)) card.UpgradeKeywords.Set(field, KeywordUpgradeSpec.Remove);
        }
    }

    private static EffectSpec? EffectByVarName(CardSpec card, List<Var> vars, string name)
    {
        // 变量名和效果一一对应：按 CanonicalVars 里的顺序数第几个变量，就是第几个「带变量的效果」
        int index = 0;
        foreach (var v in vars)
        {
            string varName = VarNameOf(v);
            if (varName == name)
            {
                var withVars = card.Effects.Where(HasVar).ToList();
                return index < withVars.Count ? withVars[index] : null;
            }
            index++;
        }
        return null;
    }

    /// <summary>
    /// 这个效果在生成代码里有没有「动态变量」（决定 OnUpgrade 的升级增量能不能按变量名对回效果）。
    /// PetAttack 用 PetDamage（新 DynamicVar）；SummonPet 数值 &gt; 0 时也有 PetHp，数值 0 时没有
    /// （所以和生成侧的 <c>CSharpCodeGen.HasNoDynamicVar</c> 保持一致）。
    /// </summary>
    private static bool HasVar(EffectSpec e) => e.Kind switch
    {
        "Damage" or "Block" or "Draw" or "Energy" => e.AmountIsStack == false,
        "ApplyPower" => true,
        "PetAttack" => true,
        "SummonPet" => e.Amount > 0,
        _ => false,
    };

    private static string VarNameOf(Var v) => v.Kind switch
    {
        "Damage" => "Damage",
        "Block" => "Block",
        "Cards" => "Cards",
        "Energy" => "Energy",
        "Power" => v.PowerId ?? "Power",     // PowerVar<T> 的变量名就是 T（和生成器一致）
        // 带名字的普通 DynamicVar：变量名就是我们起的那个（召唤伙伴的 PetHp / PetDamage）
        "DynamicVar" => v.PowerId ?? "Value",
        _ => v.Kind,
    };

    private static void ParseKeywords(CardSpec card, string text)
    {
        // 本体卡标签 CardTag（Strike / Defend / …）：别的模型按它查牌（升级初始打击 / 防御的遗物）
        string? tags = Match(text, @"CanonicalTags =>[^;]*?\{([^}]*)\}");
        if (tags is not null)
        {
            foreach (string t in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string tag = t.Replace("CardTag.", "").Replace("new HashSet<CardTag>", "").Trim();
                if (EffectCatalog.CardTags.Contains(tag) && !card.Tags.Contains(tag)) card.Tags.Add(tag);
            }
        }

        string? list = Match(text, @"CanonicalKeywords => \[([^\]]*)\]");
        if (list is null) return;
        foreach (string kw in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string k = kw.Replace("CardKeyword.", "").Trim();
            switch (k)
            {
                case "Exhaust": card.Exhausts = true; break;
                case "Ethereal": card.Ethereal = true; break;
                case "Innate": card.Innate = true; break;
                case "Retain": card.Retain = true; break;
                case "Unplayable": card.Unplayable = true; break;
                case "Sly": card.Sly = true; break;
            }
        }
    }

    /// <summary>卡牌条件：从「// 条件：…」注释 + IsPlayable 反推。</summary>
    private static void ParseCardCondition(CardSpec card, string text, Dictionary<string, string> nameToPowerId,
        RecoveryResult result, string where)
    {
        string? comment = Match(text, @"// 条件：(.+)");
        string? conditionExpr = Match(text, @"IsPlayable => (.+);") ?? Match(text, @"ShouldGlowGoldInternal => (.+);");
        if (comment is null && conditionExpr is null) return;

        var cond = ConditionFrom(comment, conditionExpr, nameToPowerId)
                   ?? ConditionFromExpr(conditionExpr, nameToPowerId);
        if (cond is null)
        {
            if (comment is not null || conditionExpr is not null)
                result.Unparsed.Add($"{where}: 条件没认出来（{comment ?? conditionExpr}）");
            return;
        }
        cond.WhenUnmet = conditionExpr is not null && text.Contains("IsPlayable =>") ? "Unplayable" : "NoEffect";
        card.Condition = cond;
    }

    /// <summary>把条件说明文字反推成 ConditionSpec。</summary>
    internal static ConditionSpec? ConditionFrom(string? comment, string? expr, Dictionary<string, string> nameToPowerId)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        string text = comment.Trim();
        // 去掉「（不满足时…）」尾巴
        int cut = text.IndexOf('（');
        if (cut > 0) text = text[..cut];
        foreach (var opt in EffectCatalog.Conditions)
        {
            if (opt.Id == "None") continue;
            string pattern = "^" + Regex.Escape(opt.Display)
                .Replace("N", @"(?<n>[\d.]+)")
                .Replace("某状态", @"(?<p>.+?)") + "$";
            var m = Regex.Match(text, pattern);
            if (!m.Success) continue;
            var cond = new ConditionSpec { Kind = opt.Id };
            if (opt.NeedsAmount) cond.Amount = decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (opt.NeedsPower)
            {
                string zh = m.Groups["p"].Value;
                // 模板没被替换过（原文里就是「某状态」）说明老配置里根本没选状态；
                // 生成器当时按 VulnerablePower 兜底编译的，这里照它的做法补上，并记一条提示。
                if (zh == "某状态") cond.PowerId = "VulnerablePower";
                else if (nameToPowerId.TryGetValue(zh, out string? id)) cond.PowerId = id;
                else cond.PowerId = zh;
            }
            return cond;
        }
        return null;
    }

    /// <summary>条件表达式 → ConditionSpec（认不出来返回 null）。</summary>
    internal static ConditionSpec? ConditionFromExpr(string? expr, Dictionary<string, string> nameToPowerId)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        string e = expr.Trim();
        var power = Regex.Match(e, @"GetPowerAmount<(\w+)>\(\) >= ([\d.]+)");
        if (power.Success) return new ConditionSpec { Kind = "HasPowerAtLeast", PowerId = power.Groups[1].Value, Amount = decimal.Parse(power.Groups[2].Value, CultureInfo.InvariantCulture) };
        var cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) <= ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtMost", Amount = decimal.Parse(cards.Groups[1].Value, CultureInfo.InvariantCulture) };
        cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) >= ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtLeast", Amount = decimal.Parse(cards.Groups[1].Value, CultureInfo.InvariantCulture) };
        if (e.Contains("CardType.Attack") && e.Contains("PileType.Hand")) return new ConditionSpec { Kind = "HandOnlyAttack" };
        if (e.Contains("CardType.Skill") && e.Contains("PileType.Hand")) return new ConditionSpec { Kind = "HandOnlySkill" };
        if (e.Contains("PileType.Draw")) return new ConditionSpec { Kind = "DrawPileEmpty" };
        if (e.Contains("PileType.Discard") && e.Contains("Any()")) return new ConditionSpec { Kind = "DiscardPileEmpty" };
        var hp = Regex.Match(e, @"CurrentHp \* 100 <= .*MaxHp \* ([\d.]+)");
        if (hp.Success) return new ConditionSpec { Kind = "HpBelowPercent", Amount = decimal.Parse(hp.Groups[1].Value, CultureInfo.InvariantCulture) };
        var played = Regex.Match(e, @"CardPlaysFinished\.Count\([^)]*\) >= ([\d.]+)");
        if (played.Success) return new ConditionSpec { Kind = "PlayedAtLeast", Amount = decimal.Parse(played.Groups[1].Value, CultureInfo.InvariantCulture) };
        if (e.Contains("CardPlaysFinished.Any") && e.Contains("== this")) return new ConditionSpec { Kind = "NotPlayedThisCombat" };
        var turn = Regex.Match(e, @"TurnNumber % ([\d.]+) == 0");
        if (turn.Success) return new ConditionSpec { Kind = "EveryNTurns", Amount = decimal.Parse(turn.Groups[1].Value, CultureInfo.InvariantCulture) };
        if (e.Contains("_condUsedThisCombat")) return new ConditionSpec { Kind = "OncePerCombat" };
        return null;
    }

    // ==================== 遗物 / 药水 / 自定义状态 ====================

    private static RelicSpec ParseRelic(string file, CharacterProfile profile, LocTables loc,
        Dictionary<string, string> nameToPowerId, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = File.ReadAllText(file, Encoding.UTF8);
        string entry = EffectCatalog.SlugFor(cls);
        var relic = new RelicSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
            Rarity = Match(text, @"=>\s*RelicRarity\.(\w+)") ?? "Common",
            Trigger = TriggerOfHook(RelicHookOf(text)),
        };
        var vars = ParseVars(text);
        ParseEffects(relic.Effects, BodyOfHook(text), vars, nameToPowerId, EffectCtx.Relic, result, cls);
        string? comment = Match(text, @"// 条件：(.+)");
        string? expr = Match(text, @"if \(!\((.+)\)\) return;") ?? Match(text, @"if \((_condUsedThisCombat)\)");
        var cond = ConditionFrom(comment, expr, nameToPowerId) ?? ConditionFromExpr(expr, nameToPowerId);
        if (cond is not null) relic.Condition = cond;
        return relic;
    }

    private static PotionSpec ParsePotion(string file, CharacterProfile profile, LocTables loc,
        Dictionary<string, string> nameToPowerId, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = File.ReadAllText(file, Encoding.UTF8);
        string entry = EffectCatalog.SlugFor(cls);
        var potion = new PotionSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
            Rarity = Match(text, @"=>\s*PotionRarity\.(\w+)") ?? "Common",
            Usage = Match(text, @"=>\s*PotionUsage\.(\w+)") ?? "CombatOnly",
            TargetType = Match(text, @"=>\s*TargetType\.(\w+)") ?? "Self",
        };
        var vars = ParseVars(text);
        ParseEffects(potion.Effects, BodyOf(text, "OnUse"), vars, nameToPowerId, EffectCtx.Potion, result, cls);
        return potion;
    }

    /// <summary>
    /// 回读自定义关键词 + 本体关键词改名：文案都来自模组工程里那份 <c>card_keywords.json</c>，
    /// 卡牌引用了哪些关键词由 <see cref="ParseCard"/> 按生成的
    /// <c>LocString("card_keywords","&lt;KEY&gt;.title")</c> 反查。
    ///
    /// 本体那 7 个关键词的键不是自定义关键词，却是**改名的落点**：
    /// 以前这里只扫 <c>.title</c> 键、而且把本体键直接 continue 丢掉，于是
    /// ① 只改了说明（名字留空）的条目在表里只有 <c>.description</c> 一个键 → 整条丢失；
    /// ② 改了名字的条目从工程恢复后静默消失，再生成一次卡面就变回本体的「消耗」。
    /// 现在按「键前缀」归并两种文案一起读，并把和本体原文相同的当没改（不然每恢复一次就多一堆空行）。
    /// </summary>
    private static void ParseCustomKeywords(string projectDir, string modId, CharacterProfile p)
    {
        string locDir = Path.Combine(projectDir, modId, "localization");
        if (!Directory.Exists(locDir)) return;
        foreach (string file in Directory.GetFiles(locDir, "card_keywords.json", SearchOption.AllDirectories))
        {
            // 先把这份表里「某个键」的两种文案收成 <键前缀> → (标题, 说明)。
            // 为什么要按前缀先归并、而不是只扫 .title：**只改了说明**（名字留空）的改名条目
            // 生成的表里只有 `<ID>.description` 一个键 —— 只扫 .title 会把它整条丢掉
            // （用户改了关键词说明，从工程恢复后说明消失了，再生成卡面又变回本体那句）。
            var byKey = new Dictionary<string, (string Title, string Desc)>(StringComparer.Ordinal);
            foreach (var kv in Json.ReadDict(file))
            {
                int dot = kv.Key.LastIndexOf('.');
                if (dot <= 0) continue;
                string prefix = kv.Key[..dot];
                string field = kv.Key[(dot + 1)..];
                if (field is not ("title" or "description")) continue;
                byKey.TryGetValue(prefix, out var cur);
                byKey[prefix] = field == "title" ? (kv.Value ?? "", cur.Desc ?? "") : (cur.Title ?? "", kv.Value ?? "");
            }

            foreach (var (key, text) in byKey)
            {
                string title = (text.Title ?? "").Trim();
                string desc = (text.Desc ?? "").Trim();

                // 本体关键词（那 7 个）：不是自定义关键词，而是「本体关键词改名」
                var vanilla = VanillaKeywordCatalog.ById(key);
                if (vanilla is not null)
                {
                    bool renamed = title.Length > 0
                                   && !string.Equals(title, VanillaKeywordGen.VanillaTitleOf(vanilla.Id), StringComparison.Ordinal);
                    bool descChanged = desc.Length > 0
                                       && !string.Equals(desc, VanillaKeywordGen.VanillaDescriptionOf(vanilla.Id), StringComparison.Ordinal);
                    if (!renamed && !descChanged) continue;              // 和本体一样 = 等于没改

                    var row = p.KeywordRenames.FirstOrDefault(x =>
                        string.Equals((x.KeywordId ?? "").Trim(), vanilla.Id, StringComparison.OrdinalIgnoreCase));
                    if (row is null)
                    {
                        row = new VanillaKeywordRenameSpec { KeywordId = vanilla.Id };
                        p.KeywordRenames.Add(row);
                    }
                    if (renamed) row.Name = title;
                    if (descChanged) row.Description = desc;
                    continue;
                }

                if (KeywordGen.IsReservedKey(key)) continue;             // 剩下的保留键（NONE / PERIOD）不是关键词
                if (p.CustomKeywords.Any(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
                p.CustomKeywords.Add(new CustomKeywordSpec
                {
                    Key = key,
                    Name = text.Title ?? "",
                    Description = text.Desc ?? "",
                });
            }
        }
    }

    /// <summary>
    /// 回读召唤伙伴：配置来自 <c>cs/Pet.cs</c>（类名 / 血量常量 / VisualsPath）+ 模组工程里那份
    /// <c>monsters.json</c>（中文名）。
    ///
    /// 为什么必须回读：**不同步改这里就会静默丢配置** —— 用户从工程恢复存档时，
    /// 召唤物的名字 / 血量 / 图片全没了，而卡牌上的「召唤伙伴」效果却还在，一生成就报错。
    /// </summary>
    private static void ParseSummon(string projectDir, string cs, CharacterProfile p, RecoveryResult result)
    {
        string file = Path.Combine(cs, "Pet.cs");
        if (!File.Exists(file)) return;
        string text = File.ReadAllText(file, Encoding.UTF8);

        string cls = Match(text, @"public sealed class (\w+) : MonsterModel") ?? "";
        if (cls.Length == 0)
        {
            result.Unparsed.Add("cs/Pet.cs 里找不到 `public sealed class X : MonsterModel`（宠物类名没恢复）");
            return;
        }
        p.Summon ??= new SummonSpec();
        p.Summon.Enabled = true;
        p.Summon.ClassName = cls;
        p.Summon.Hp = Int(text: text, pattern: @"private const int BaseHp = (\d+);", fallback: 8);

        // 中文名在本体的 monsters 表里（我们只写自己那一个键）
        string entry = EffectCatalog.SlugFor(cls);
        string locDir = Path.Combine(projectDir, p.ModId, "localization");
        if (Directory.Exists(locDir))
        {
            foreach (string f in Directory.GetFiles(locDir, "monsters.json", SearchOption.AllDirectories))
            {
                var dict = Json.ReadDict(f);
                if (dict.TryGetValue(entry + ".name", out string? name) && !string.IsNullOrWhiteSpace(name))
                {
                    p.Summon.Name = name;
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(p.Summon.Name)) p.Summon.Name = cls;

        // 宠物图片：生成时放在 images/monsters/<entry 小写>.png
        string png = Path.Combine(projectDir, "images", "monsters", entry.ToLowerInvariant() + ".png");
        if (File.Exists(png)) p.Summon.Image = png;

        bool hasScene = File.Exists(Path.Combine(projectDir, "scenes", "creature_visuals",
            entry.ToLowerInvariant() + ".tscn"));
        result.Notes.Add($"召唤伙伴：{p.Summon.Name}（{cls}，生命 {p.Summon.Hp}）"
            + (hasScene ? "，有自定义视觉场景" : "，视觉用本体占位图"));
    }

    private static CustomPowerSpec ParseCustomPower(string file, CharacterProfile profile, LocTables loc, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = File.ReadAllText(file, Encoding.UTF8);
        string entry = EffectCatalog.SlugFor(cls);
        var power = new CustomPowerSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
            Description = loc.Get($"{entry}.description", ""),
            Type = (Match(text, @"=>\s*PowerType\.(\w+)") ?? "Buff"),
            Single = (Match(text, @"=>\s*PowerStackType\.(\w+)") ?? "Counter") == "Single",
            Enabled = true,
        };
        var vars = ParseVars(text);
        // 每个钩子方法 = 一条触发时机
        foreach (Match m in Regex.Matches(text, @"public override async Task (\w+)\(([^)]*)\)\s*\{"))
        {
            string hook = m.Groups[1].Value;
            string kind = TriggerOfHook(hook);
            if (kind.Length == 0) continue;
            // 自定义状态用的触发时机 id 和遗物不一样（PowerTriggers 那一套）
            kind = kind switch
            {
                "PlayerTurnStart" => "TurnStart",
                "TurnEnd" => "TurnEnd",
                "CombatStart" => "CombatStart",
                _ => kind,
            };
            if (PowerTriggers.Find(kind) is null) continue;
            if (kind == "TurnEnd" && power.Triggers.Any(t => t.Kind == "TurnEnd")) continue;
            var trigger = new PowerTriggerSpec { Kind = kind };
            if (kind == "CardPlayed" && text.Contains("cardPlay.Card.Type == CardType."))
            {
                string? filter = Match(text, @"cardPlay\.Card\.Type == CardType\.(\w+)");
                if (filter is not null) trigger.CardFilter = filter;
            }
            // 「某个状态层数变化后」盯的是哪个状态（生成时是 if (power is StrengthPower)）
            if (kind == "PowerChanged")
            {
                string? watched = Match(text, @"if \(power is (\w+)\)");
                if (watched is not null) trigger.PowerId = watched;
                if (text.Contains("HittableEnemies.Contains(power.Owner)")) trigger.PowerTarget = "AnyEnemy";
            }
            string body = MethodBody(text, hook);
            ParseEffects(trigger.Effects, body, vars, new Dictionary<string, string>(StringComparer.Ordinal), EffectCtx.Power, result, cls);
            power.Triggers.Add(trigger);
        }
        if (text.Contains("PowerCmd.Remove(this)")) power.RemoveAtTurnEnd = true;
        string? decay = Match(text, @"SetAmount\(base\.Amount - (\d+)\)");
        if (decay is not null) power.DecayPerTurn = int.Parse(decay, CultureInfo.InvariantCulture);
        string? color = Match(text, @"""AmountColor""\s*=\s*""([0-9A-Fa-f]{6})""");
        if (color is not null) power.AmountColor = color;
        if (power.Triggers.Count == 0 && !power.RemoveAtTurnEnd && power.DecayPerTurn == 0)
            result.Notes.Add($"自定义状态「{power.Name}」没配触发时机（工程里就是这么生成的）");
        return power;
    }

    // ==================== 额外资源量 / 本体状态改写 / 先古之民 ====================

    private static void ParseExtraResource(string projectDir, string cs, CharacterProfile p, LocTables loc, RecoveryResult result)
    {
        string file = Path.Combine(cs, "ExtraResource.cs");
        if (!File.Exists(file)) { p.ExtraResource.Enabled = false; return; }
        string text = File.ReadAllText(file, Encoding.UTF8);
        p.ExtraResource.Enabled = true;
        p.ExtraResource.Initial = Int(text, @"InitialAmount = (\d+)", 0);
        p.ExtraResource.CarryOver = text.Contains("CarryOver = true");
        string ui = Path.Combine(cs, "ExtraResourceUi.cs");
        if (File.Exists(ui))
        {
            string uiText = File.ReadAllText(ui, Encoding.UTF8);
            string? name = Match(uiText, @"DisplayName = ""([^""]*)""");
            p.ExtraResource.ShowName = !string.IsNullOrEmpty(name);
            p.ExtraResource.Name = name ?? "";
            string? icon = Match(uiText, @"IconPath = ""res://([^""]+)""");
            if (!string.IsNullOrEmpty(icon))
            {
                string src = Path.Combine(projectDir, icon.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(src)) p.ExtraResource.Icon = src;
            }
        }
        result.Notes.Add($"额外资源量：名字「{p.ExtraResource.Name}」开场 {p.ExtraResource.Initial} 点"
            + (p.ExtraResource.CarryOver ? "（跨战斗继承）" : "（每场重置）")
            + (string.IsNullOrWhiteSpace(p.ExtraResource.Icon) ? "" : "，图标已找回"));
    }

    private static void ParseVanillaOverrides(string projectDir, string cs, CharacterProfile p, LocTables loc, RecoveryResult result)
    {
        // 名字/描述写在本地化表里（键 = 本体状态键），颜色/图标在补丁表里
        string file = Path.Combine(cs, "VanillaPowerOverride.cs");
        var colors = new Dictionary<string, (string Amount, string BarColor, string BarNode, string Icon)>(StringComparer.Ordinal);
        if (File.Exists(file))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file, Encoding.UTF8),
                         @"\[""(\w+)""\] = new Entry \{ IconRes = ""([^""]*)"", AmountColor = ""([^""]*)"", BarNode = ""([^""]*)"", BarColor = ""([^""]*)"" \}"))
            {
                colors[m.Groups[1].Value] = (m.Groups[3].Value, m.Groups[5].Value, m.Groups[4].Value, m.Groups[2].Value);
            }
        }
        foreach (var (key, zh) in loc.PowerTitles)
        {
            // 键是本体本地化表的键（POISON_POWER），配置里要的是**类名**（PoisonPower）
            var vanilla = EffectCatalog.Powers.FirstOrDefault(x => x.Slug == key);
            if (vanilla is null) continue;                   // 不是本体状态的键（自定义状态另有处理）
            colors.TryGetValue(key, out var c);
            var ov = new VanillaPowerOverride
            {
                PowerId = vanilla.Id,
                Name = zh,
                Description = loc.Get(key + ".description", ""),
                AmountColor = c.Amount ?? "",
                BarColor = c.BarColor ?? "",
            };
            if (!string.IsNullOrEmpty(c.Icon)) ov.Icon = Path.Combine(projectDir, c.Icon.Replace('/', Path.DirectorySeparatorChar));
            if (zh != vanilla.Zh || ov.Description.Length > 0 || ov.AmountColor.Length > 0 || ov.BarColor.Length > 0)
                p.VanillaPowerOverrides.Add(ov);
        }
        if (p.VanillaPowerOverrides.Count > 0)
            result.Notes.Add($"本体状态改写 {p.VanillaPowerOverrides.Count} 条（{string.Join("、", p.VanillaPowerOverrides.Select(o => o.Name))}）");
    }

    private static void ParseAncients(string cs, CharacterProfile p, LocTables loc, RecoveryResult result)
    {
        string file = Path.Combine(cs, "AncientDialoguePatch.cs");
        if (!File.Exists(file)) return;
        string text = File.ReadAllText(file, Encoding.UTF8);
        string charEntry = EffectCatalog.SlugFor(p.CharacterClass);
        foreach (Match row in Regex.Matches(text, @"\[""(\w+)""\] = new \(int VisitIndex, bool Repeating, int Lines\)\[\] \{ ([^}]*)\}"))
        {
            string ancientId = row.Groups[1].Value;
            var talk = new AncientTalkSpec { AncientId = ancientId };
            int index = 0;
            foreach (Match d in Regex.Matches(row.Groups[2].Value, @"\((-?\d+), (true|false), (\d+)\)"))
            {
                int visitIndex = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                int lines = int.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
                var dlg = new AncientDialogueSpec
                {
                    Times = visitIndex < 0 ? -1 : visitIndex + 1,
                    Repeating = d.Groups[2].Value == "true",
                };
                for (int k = 0; k < lines; k++)
                {
                    string key = $"{ancientId}.talk.{charEntry}.{index}-{k}{(dlg.Repeating ? "r" : "")}";
                    string who = loc.Get(key + ".ancient", "");
                    string me = loc.Get(key + ".char", "");
                    bool speakerIsAncient = who.Length > 0 || me.Length == 0;
                    dlg.Lines.Add(new AncientLineSpec
                    {
                        AncientSpeaks = speakerIsAncient,
                        Text = speakerIsAncient ? who : me,
                        NextText = loc.Get(key + ".next", ""),
                    });
                }
                if (dlg.Lines.Any(l => l.Text.Length > 0)) talk.Dialogues.Add(dlg);
                index++;
            }
            if (talk.HasAnyText) p.Ancients.Add(talk);
        }
        if (p.Ancients.Count > 0)
            result.Notes.Add($"先古之民对话 {p.Ancients.Count} 位（{string.Join("、", p.Ancients.Select(a => a.AncientId))}）");
    }

    // ==================== 小工具 ====================

    private enum EffectCtx { Card, Relic, Potion, Power }

    private static string FindCharacterFile(string cs)
    {
        foreach (string f in Directory.GetFiles(cs, "*.cs"))
            if (File.ReadAllText(f, Encoding.UTF8).Contains(": CharacterModel"))
                return f;
        return "";
    }

    private static string[] Sorted(string[] files) =>
        files.OrderBy(f => Regex.Match(Path.GetFileNameWithoutExtension(f), @"(\d+)$") is { Success: true } m
            ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
            : 0).ThenBy(f => f, StringComparer.Ordinal).ToArray();

    private static string FrameColorFromMaterial(string projectDir, string frameName)
    {
        string file = Path.Combine(projectDir, "materials", frameName + "_mat.tres");
        if (!File.Exists(file)) return null!;
        string text = File.ReadAllText(file, Encoding.UTF8);
        string? hex = Match(text, @"shader_parameter/color = Color\(([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\)");
        if (hex is null) return null!;
        var m = Regex.Match(text, @"shader_parameter/color = Color\(([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\)");
        int r = (int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 255);
        int g = (int)Math.Round(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 255);
        int b = (int)Math.Round(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * 255);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    private static Dictionary<string, string> BuildPowerNameMap(CharacterProfile p)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in EffectCatalog.Powers)
            if (!string.IsNullOrWhiteSpace(e.Zh)) map[e.Zh] = e.Id;
        // 本体状态改写会把名字换掉（例：中毒 → 感电）
        foreach (var ov in p.VanillaPowerOverrides)
        {
            foreach (var e in EffectCatalog.Powers)
                if (e.Slug == ov.PowerId && !string.IsNullOrWhiteSpace(e.Zh)) map.Remove(e.Zh);
            if (!string.IsNullOrWhiteSpace(ov.Name)) map[ov.Name] = ov.PowerId;
        }
        foreach (var cp in p.CustomPowers)
            if (CustomPowerGen.IsActive(cp)) map[cp.Name] = cp.ClassName;
        return map;
    }

    /// <summary>把文件里某个方法/属性的方法体抠出来（按大括号配对）。</summary>
    private static string BodyOf(string text, string marker)
    {
        int at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return "";
        int open = text.IndexOf('{', at);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[(open + 1)..i];
            }
        }
        return "";
    }

    /// <summary>取 <c>Marker =></c> 后面的那个表达式体（<c>[ ... ]</c> 或圆括号），用于 CanonicalVars。</summary>
    private static string BlockAfter(string text, string marker)
    {
        int at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return "";
        int open = text.IndexOfAny(new[] { '[', '(' }, at + marker.Length);
        if (open < 0) return "";
        char close = text[open] == '[' ? ']' : ')';
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == text[open]) depth++;
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0) return text[(open + 1)..i];
            }
        }
        return text[(open + 1)..];
    }

    private static string MethodBody(string text, string hook) => BodyOf(text, "public override async Task " + hook + "(");

    private static string BodyOfHook(string text)
    {
        foreach (Match m in Regex.Matches(text, @"public override async Task (\w+)\("))
        {
            string body = MethodBody(text, m.Groups[1].Value);
            if (body.Length > 0) return body;
        }
        return "";
    }

    private static string RelicHookOf(string text) =>
        // 「每场战斗只触发一次」的遗物会多写一个 AfterCombatEnd（用来清标记），它不是触发时机，别认成 CombatVictory
        Match(text, @"public override async Task (?!AfterCombatEnd)(\w+)\(") ?? "";

    private static string TriggerOfHook(string hook) => hook switch
    {
        "BeforeSideTurnStart" => "CombatStart",
        "AfterPlayerTurnStart" => "PlayerTurnStart",
        "BeforeSideTurnEnd" => "TurnEnd",
        "AfterCombatVictory" => "CombatVictory",
        // 自定义状态的「战斗胜利后」现在生成的是 AfterCombatEnd（本体发战斗奖励的钩子就是它）
        "AfterCombatEnd" => "CombatVictory",
        "AfterDamageReceived" => "DamageReceived",
        "AfterGoldGained" => "GoldGained",
        "AfterCardPlayed" => "CardPlayed",
        "AfterCardExhausted" => "CardExhausted",
        "AfterCardDrawn" => "CardDrawn",
        "AfterCardDiscarded" => "CardDiscarded",
        "AfterDamageGiven" => "DamageDealt",
        "AfterBlockGained" => "BlockGained",
        "AfterDeath" => "EnemyDeath",
        "BeforeCombatStart" => "CombatStart",
        "AfterEnergySpent" => "EnergySpent",
        "AfterStarsSpent" => "StarsSpent",
        "AfterPowerAmountChanged" => "PowerChanged",
        _ => "",
    };

    private static string? Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int Int(string text, string pattern, int fallback)
    {
        string? v = Match(text, pattern);
        return v is null ? fallback : int.Parse(v, CultureInfo.InvariantCulture);
    }

    private static decimal Dec(string text, string pattern, decimal fallback)
    {
        string? v = Match(text, pattern);
        return v is null ? fallback : decimal.Parse(v, CultureInfo.InvariantCulture);
    }

    private static string? Hex(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.TrimStart('#').ToUpperInvariant();

    /// <summary>把 <c>Foo(a, b, c)</c> 里的第 n 个参数抠出来（忽略括号里的逗号）。</summary>
    private static string ArgAt(string line, int index)
    {
        int open = line.IndexOf('(');
        int close = line.LastIndexOf(')');
        if (open < 0 || close <= open) return "";
        var parts = SplitArgs(line[(open + 1)..close]);
        return index < parts.Count ? parts[index].Trim() : "";
    }

    private static List<string> SplitArgs(string args)
    {
        var list = new List<string>();
        int depth = 0;
        var sb = new StringBuilder();
        foreach (char c in args)
        {
            if (c is '(' or '[' or '<') depth++;
            else if (c is ')' or ']' or '>') depth--;
            if (c == ',' && depth == 0) { list.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }

    /// <summary>本地化表（zhs 优先，缺了退回 eng）。</summary>
    private sealed class LocTables
    {
        private readonly Dictionary<string, string> _zh = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _en = new(StringComparer.Ordinal);

        public IEnumerable<(string Key, string Zh)> PowerTitles
        {
            get
            {
                foreach (var kv in _zh)
                {
                    if (!kv.Key.EndsWith(".title", StringComparison.Ordinal)) continue;
                    yield return (kv.Key[..^6], kv.Value);
                }
            }
        }

        public string Get(string key, string fallback) =>
            _zh.TryGetValue(key, out string? v) && v.Length > 0 ? v
            : _en.TryGetValue(key, out string? e) && e.Length > 0 ? e : fallback;

        public static LocTables Load(string projectDir, string modId)
        {
            var t = new LocTables();
            foreach (string lang in new[] { "zhs", "eng" })
            {
                string dir = Path.Combine(projectDir, modId, "localization", lang);
                if (!Directory.Exists(dir))
                {
                    // 有的工程把本地化放在别的子目录下，兜底扫一遍
                    var found = Directory.GetDirectories(projectDir, lang, SearchOption.AllDirectories).FirstOrDefault();
                    if (found is null) continue;
                    dir = found;
                }
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    var dict = Json.ReadDict(file);
                    var target = lang == "zhs" ? t._zh : t._en;
                    foreach (var kv in dict) target[kv.Key] = kv.Value;
                }
            }
            return t;
        }
    }

    /// <summary>极简 JSON 读字符串字典（不引第三方库；值都是字符串）。</summary>
    private static class Json
    {
        public static Dictionary<string, string> ReadDict(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return dict;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    dict[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
            }
            catch { }
            return dict;
        }
    }
}