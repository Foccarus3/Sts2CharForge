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

    /// <summary>
    /// 工程里 <c>cs/Pet.cs</c> 里的宠物类名（按出现顺序 = 召唤物列表顺序）。
    /// 为什么放在结果里：卡牌效果回读时要靠它把 <c>.FromPetAttacker(__uiCheckPet)</c> 的局部变量名
    /// 反推回「哪一只召唤物」（<c>EffectSpec.PetSummon</c>）。
    /// </summary>
    public List<string> PetClassNames { get; } = new();

    /// <summary>
    /// V0.2.134：**从工程里认回来的锻造节点图**（每张图一行，给用户看的摘要）。
    /// 老工程（V0.2.133 及以前生成、没有图标记）这里是空的 —— 那不是错误，只是图恢复不回来。
    /// </summary>
    public List<string> IsForged { get; } = new();

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
        // 召唤物：模板里只借了「第一只的图片路径」当底，这里清掉重读（否则会和工程里读出来的叠成两份）
        p.Summons.Clear();

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
        // V0.2.134：清单里本来就写着作者 / 版本 / 模组说明，以前**没有回读**（不报错、静默变成空串）——
        // 后果是「从工程恢复 → 再生成一次」的 mod_manifest.json 会跟第一次不一样。
        // 这三样在工程里没有别的载体，只能从清单认回来；空串照旧不回写（别把模板带来的值冲掉）。
        if (author.Length > 0) p.Author = author;
        if (version.Length > 0) p.Version = version;
        if (description.Length > 0) p.Description = description;

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
        // V0.2.134：「说明」那一栏在工程里**没有自己的载体** —— 空说明生成出去就是本地化表里那句
        // 兜底文案（见 LocalizationGen.DefaultCharacterDescription）。不把这一档认出来的话，
        // 恢复出来的存档会「无中生有」多出一句说明，再生成一次 mod_manifest.json 就与第一次不同。
        // 判据两条一起看：本地化文本 == 兜底那句 **且** 清单里 description 是空的 → 当作用户没填。
        if (string.Equals(p.Description, LocalizationGen.DefaultCharacterDescription, StringComparison.Ordinal)
            && description.Length == 0)
            p.Description = "";
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

        // ---- 「诅咒 / 先古卡」两类牌各自的卡框颜色（外观池） ----
        ParseSpecialStyle(p, projectDir, cs, Naming.From(p).CurseStylePoolClass, p.CurseStyle, "curse");
        ParseSpecialStyle(p, projectDir, cs, Naming.From(p).AncientStylePoolClass, p.AncientStyle, "ancient");

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
            // 卡池 / 初始卡组里写的是**生成时的类名**（打击 / 防御带角色类名前缀：<角色>Strike），
            // 而配置里存的是去掉前缀的 Strike —— 这两处一律按生成时的类名去对。
            string emitted = Path.GetFileNameWithoutExtension(file);
            var card = ParseCard(file, p, loc, nameToPowerId, result);
            card.InCardPool = !removedFromPool.Contains(emitted);
            if (starting.TryGetValue(emitted, out int copies))
            {
                card.InStartingDeck = true;
                card.StartingCopies = copies;
            }
            // 诅咒 / 先古卡按**稀有度**回到各自的列表（页面上它们是分开的两栏；
            // 生成 / 卡池 / 素材都按稀有度判断，所以这里也必须按稀有度分回去，
            // 否则回读出来的诅咒会跑到普通卡列表里、界面上看着就不对了）
            if (card.IsCurseCard) p.Curses.Add(card);
            else if (card.IsAncientCard) p.AncientCards.Add(card);
            else p.Cards.Add(card);
        }

        // ---- 遗物 ----
        string relicDir = Path.Combine(cs, "Relics");
        if (Directory.Exists(relicDir))
        {
            foreach (string file in Sorted(Directory.GetFiles(relicDir, "*.cs")))
            {
                // 额外资源量的载体遗物（<角色类>ExtraResourceRelic）不是用户配置的遗物，跳过
                if (Path.GetFileName(file).EndsWith("ExtraResourceRelic.cs", StringComparison.Ordinal)) continue;
                var relic = ParseRelic(file, p, loc, nameToPowerId, result);
                relic.IsStartingRelic = startingRelics.Contains(relic.ClassName);
                // V0.2.134：锻造节点图（遗物那一档）—— 和卡牌同一个标记、同一套回读。
                if (ForgeGraphGen.AttachMarker(file, relic, result, $"遗物「{relic.Name}」的锻造节点图",
                        portVersionSink: () => p.ForgeGraphPortVersion = ForgeGraphGen.PortVersion))
                    result.IsForged.Add($"{relic.Name}（遗物 · {relic.ForgeGraph!.Nodes.Count} 个模块）");
                p.Relics.Add(relic);
            }
        }

        // ---- 药水 ----
        string potionDir = Path.Combine(cs, "Potions");
        if (Directory.Exists(potionDir))
            foreach (string file in Sorted(Directory.GetFiles(potionDir, "*.cs")))
            {
                var potion = ParsePotion(file, p, loc, nameToPowerId, result);
                // V0.2.134：锻造节点图（药水那一档）。
                if (ForgeGraphGen.AttachMarker(file, potion, result, $"药水「{potion.Name}」的锻造节点图",
                        portVersionSink: () => p.ForgeGraphPortVersion = ForgeGraphGen.PortVersion))
                    result.IsForged.Add($"{potion.Name}（药水 · {potion.ForgeGraph!.Nodes.Count} 个模块）");
                p.Potions.Add(potion);
            }

        // ---- 额外资源量 ----
        ParseExtraResource(projectDir, cs, p, loc, result, nameToPowerId);

        // ---- 多人联机宝箱房（自定义「拿取」手势 / 催促文本）----
        ParseTreasureGesture(projectDir, cs, p, result);

        // ---- 无色卡（用户要求）：登记的痕迹在 cs/ModEntry.cs 里 ----
        // 工程里看不出「这张卡属于哪个池」（卡类本身不声明池，成员表在池类里），
        // 但无色卡必须靠 ModHelper.AddModelToPool 登记，生成时写的是
        // `ModHelper.AddModelToPool(__colorlessPool, __t<卡类名>)` —— 按它认回来。
        {
            string entryFile = Path.Combine(cs, "ModEntry.cs");
            if (File.Exists(entryFile))
            {
                var colorless = new List<string>();
                foreach (Match m in Regex.Matches(File.ReadAllText(entryFile, Encoding.UTF8), @"AddModelToPool\(__colorlessPool,\s*__t(\w+)\)"))
                    colorless.Add(m.Groups[1].Value);
                if (colorless.Count > 0)
                {
                    foreach (var card in p.AllCards)
                        if (card is not null && colorless.Contains(card.ClassName, StringComparer.Ordinal)) card.IsColorless = true;
                    result.Notes.Add($"无色卡：{colorless.Count} 张（进本体无色卡池）");
                }
            }
        }

        // ---- 初始卡组里的**本体卡**（打击 ×5 / 防御 ×5）----
        // 生成的代码里它们是 ModelDb.Card<StrikeIronclad>()，没有对应的 cs/Cards/*.cs，
        // 所以上面那轮扫不到 —— 这里补成「本体卡引用」条目（只在初始卡组里用本体的英文类名，不生成自己的类）。
        foreach (var kv in starting)
        {
            // 已有同名条目的（自有卡或被别的路径加过）→ 不重复加
            if (p.Cards.Any(c => string.Equals(c.ClassName, kv.Key, StringComparison.Ordinal))) continue;
            // **必须是本体真的有的卡 id** 才算「本体卡引用」。
            // 为什么要这一条：用户自己的打击 / 防御生成出来是带角色类名前缀的（<角色>Strike / <角色>Defend），
            // 初始卡组里写的也是 <角色>Strike —— 不加判断的话这里会把它们又当成一条「本体卡引用」加一份，
            // 回读出来的牌数变多、界面里多出两条重复的初始卡（实测踩过）。
            if (EffectCatalog.Cards.All(x => !string.Equals(x.Id, kv.Key, StringComparison.Ordinal))) continue;
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

        // ---- 自定义参数（需求：数值公式要靠它们）----
        ParseCustomParams(cs, p, template, result);

        // ---- 先古之民对话 ----
        ParseAncients(cs, p, loc, result);

        // ---- 把用到的图复制到存档旁边（工程目录以后删了也不缺图） ----
        // 模组预览图先按固定路径认回来：本体模组界面读的是 res://<模组ID>/mod_image.png
        //（工程里就是 <工程>\<模组ID>\mod_image.png），认到之后再交给 CopyAssets 搬进 _恢复素材
        {
            string modImageInProject = Path.Combine(projectDir, modId, "mod_image.png");
            if (File.Exists(modImageInProject)) p.Art.ModImage = modImageInProject;
        }
        if (!string.IsNullOrWhiteSpace(assetDir)) CopyAssets(p, assetDir!, result);

        result.Notes.Add($"卡牌 {p.Cards.Count(c => !c.IsVanillaCard)} 张（初始牌 {p.Cards.Count(c => c.InStartingDeck && !c.IsVanillaCard)} 张 / 不入池 {p.Cards.Count(c => !c.IsVanillaCard && !c.InCardPool)} 张）"
            + (p.Cards.Any(c => c.IsVanillaCard) ? $" + 本体卡引用 {p.Cards.Count(c => c.IsVanillaCard)} 条" : "")
            + $"、遗物 {p.Relics.Count} 个、药水 {p.Potions.Count} 个、自定义状态 {p.CustomPowers.Count} 个");
        // V0.2.134：锻造节点图从工程恢复的结果（老工程 = 0 张，不是错误，界面 / 日志照旧说清）
        if (result.IsForged.Count > 0)
            result.Notes.Add($"锻造节点图：从工程里认回 {result.IsForged.Count} 张 —— " + string.Join("、", result.IsForged));
        else
            result.Notes.Add("锻造节点图：这个工程里一行图标记都没有（V0.2.133 及以前生成的工程就是这一档）"
                + "—— 图仍然为空，其余字段照旧恢复；要拿回图请用生成它的那一版重新生成一次。");
        return result;
    }

    /// <summary>
    /// 「自定义参数 / 组」页那些参数的恢复。
    ///
    /// 为什么要这一段：卡牌的「数值公式」和锻造页的【数值】模块都靠参数（<c>=M*3</c>）。
    /// 参数丢了的后果不是「少了个设置」，而是回读出来的公式全部报「不是一个参数」、
    /// 整个存档生成不过去 —— 所以这一段必须和公式一起做。
    ///
    /// 认法：生成出来的代码里，每个**用到的**参数都有一行
    /// <c>decimal __pM = __fgSelf?.MaxHp ?? 0;   // M= 自己的最大生命</c>
    /// （见 <see cref="ForgeGraphGen.EmitCardFormulaPrologue"/> 以及每个执行点的序幕），
    /// 取值来源完整地编码在那个表达式里，所以能原样认回来。
    ///
    /// 认不回来的两样（都不影响生成，只是界面上少一点信息）：
    ///   · 「备注」那一栏是纯给用户自己看的（不参与生成，代码里没有它的位置）；
    ///   · 「全部友方」和「自己」生成的是**同一个**表达式 <c>__fgSelf</c>，只能按默认的「自己」认回来。
    /// </summary>
    private static void ParseCustomParams(string cs, CharacterProfile p, CharacterProfile? template, RecoveryResult result)
    {
        var found = new List<ParamSpec>();
        foreach (string file in Directory.GetFiles(cs, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
            foreach (Match m in Regex.Matches(text, @"decimal\s+__p([A-Z])\s*=\s*([^;]+);"))
            {
                string letter = m.Groups[1].Value;
                if (found.Any(x => string.Equals(x.Name, letter, StringComparison.Ordinal))) continue;
                var spec = ParamFromExpr(letter, m.Groups[2].Value.Trim());
                if (spec is not null) found.Add(spec);
            }
        }
        // 一个都没找到：不要把模板里的那份清掉（用户可能只是还没写公式 / 没画图）
        if (found.Count == 0) return;
        // 旧存档里同名字母的那几项**借过来**：备注 / 给予能量 / 给予额外资源量这三样代码里没有
        //（备注是纯给自己看的；那两个「给予量」目前是纯配置项），丢了对用户来说就是白丢。
        var carried = new Dictionary<string, ParamSpec>(StringComparer.Ordinal);
        if (template is not null)
            foreach (var old in template.CustomParams)
                if (old is not null) carried[ParamNames.Normalize(old.Name)] = old;
        var ordered = found.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        p.CustomParams.Clear();
        foreach (var s in ordered)
        {
            if (carried.TryGetValue(s.Name, out ParamSpec? old))
            {
                s.Note = old.Note;
                s.EnergyGrant = old.EnergyGrant;
                s.ResourceGrant = old.ResourceGrant;
            }
            p.CustomParams.Add(s);
        }
        result.Notes.Add($"自定义参数 {ordered.Count} 个："
            + string.Join(" / ", ordered.Select(x => x.Name + "=" + x.Summary))
            + "（备注 / 给予量这几栏代码里没有，用旧存档当模板恢复时才带得过来）");
    }

    /// <summary>把一行 <c>decimal __pM = &lt;表达式&gt;;</c> 里的表达式还原成一条参数配置。</summary>
    private static ParamSpec? ParamFromExpr(string letter, string expr)
    {
        var spec = new ParamSpec { Name = letter };
        Match power = Regex.Match(expr, @"ForgeFormulaRuntime\.PowerAmount\((.*?),\s*""(.*?)""\)");
        if (power.Success)
        {
            spec.Source = ParamSources.Power;
            spec.PowerId = power.Groups[2].Value;
            spec.Target = ParamTargetOf(power.Groups[1].Value);
            return spec;
        }
        if (expr.Contains("?.CurrentHp", StringComparison.Ordinal)) spec.Source = ParamSources.CurrentHp;
        else if (expr.Contains("?.MaxHp", StringComparison.Ordinal)) spec.Source = ParamSources.MaxHp;
        else if (expr.Contains("?.Gold", StringComparison.Ordinal)) spec.Source = ParamSources.Gold;
        else if (expr.Contains("MaxEnergy", StringComparison.Ordinal)) spec.Source = ParamSources.MaxEnergy;
        else if (expr.Contains("?.Energy", StringComparison.Ordinal)) spec.Source = ParamSources.Energy;
        else if (FormulaBuiltins.All.FirstOrDefault(b =>
                     string.Equals(b.Expr("__fgPlayer", "__fgSelf"), expr.Trim(), StringComparison.Ordinal)) is { } live)
        {
            // 用户要求：参数页也能选那些「实时读数」（回合数 / 丢弃·消耗·生成·复制张数 / 资源量 / 状态数…）——
            // 它们的表达式就是内置量那张表里的原文，对得上就认回来（不用另加标记）。
            spec.Source = live.Id;
            spec.Target = ParamTargets.Self;   // 都是「自己」的读数
            return spec;
        }
        else if (decimal.TryParse(expr.TrimEnd('m', 'M'), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out decimal v))
        {
            spec.Source = ParamSources.Value;
            spec.Value = v;
            return spec;   // 固定数值没有「从谁身上取」这一说
        }
        else return null;   // 认不出来的写法：不乱猜，宁可少一条（生成时会报「不是一个参数」让用户补）
        spec.Target = ParamTargetOf(expr);
        return spec;
    }

    /// <summary>取值表达式里的「从谁身上取」（生成端见 <see cref="ForgeFormula.TargetExpr"/>）。</summary>
    private static string ParamTargetOf(string expr)
    {
        if (expr.Contains("ForgeFormulaRuntime.RandomEnemy", StringComparison.Ordinal)) return ParamTargets.RandomEnemy;
        if (expr.Contains("ForgeFormulaRuntime.FirstEnemy", StringComparison.Ordinal)) return ParamTargets.AllEnemies;
        if (expr.Contains("ForgeFormulaRuntime.FirstSummon", StringComparison.Ordinal)) return ParamTargets.Summon;
        if (expr.Contains("__fgTarget", StringComparison.Ordinal)) return ParamTargets.ChosenEnemy;
        return ParamTargets.Self;
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

        // 「加入药水池」这个勾（V0.2.127 起补的回读规则）：生成端把**所有**药水都列进
        // <角色>PotionPool.GenerateAllPotions()，没勾的那些是在 GetUnlockedPotions() 里
        // 用 `keep.RemoveAll(p => p.Id == ModelDb.Potion<X>().Id …)` 剔掉的
        //（掉落/商店走的就是 GetUnlockedPotions）。所以：类名出现在那个方法体里 = **没勾**。
        // ⚠ 以前完全没回读这一项 → 从工程恢复出来的「不入池」药水会静默变回入池（探针实测踩到）。
        {
            string poolFile = Path.Combine(cs, Naming.From(p).PotionPoolClass + ".cs");
            if (File.Exists(poolFile) && p.Potions.Count > 0)
            {
                string poolText = File.ReadAllText(poolFile, Encoding.UTF8);
                string unlocked = BodyOf(poolText, "GetUnlockedPotions(");
                if (unlocked.Length > 0)
                    foreach (var s in p.Potions)
                    {
                        if (string.IsNullOrWhiteSpace(s.ClassName)) continue;
                        s.InPotionPool = !unlocked.Contains($"ModelDb.Potion<{s.ClassName}>()");
                    }
            }
        }
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
                FsSafe.Copy(src, dst, true);
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
        p.Art.ModImage = Move(p.Art.ModImage);
        // 多人联机宝箱房的手势（四张本体手势 + 我们自己那张「拿取」）
        p.Art.GesturePoint = Move(p.Art.GesturePoint);
        p.Art.GestureScissors = Move(p.Art.GestureScissors);
        p.Art.GestureRock = Move(p.Art.GestureRock);
        p.Art.GesturePaper = Move(p.Art.GesturePaper);
        p.Art.GestureGrab = Move(p.Art.GestureGrab);
        foreach (var key in p.Art.CardPortraits.Keys.ToList()) p.Art.CardPortraits[key] = Move(p.Art.CardPortraits[key]) ?? "";
        foreach (var ov in p.VanillaPowerOverrides) ov.Icon = Move(ov.Icon);
        foreach (var cp in p.CustomPowers) cp.Icon = Move(cp.Icon);
        foreach (var r in p.Relics) r.Icon = Move(r.Icon);
        foreach (var s in p.Potions) s.Icon = Move(s.Icon);
        // 召唤物的宠物图也是「工程目录删了就没了」的素材，一起搬到存档旁边（每只一张）
        foreach (var s in p.Summons) if (s is not null) s.Image = Move(s.Image);
        if (copied > 0) result.Notes.Add($"素材 {copied} 个已复制到「{dir}」（配置里已指向这里）");
    }

    private static CharacterProfile CloneForTemplate(CharacterProfile t)
    {
        // 只借用「环境路径 / 素材路径」这些工程里反推不出来的东西
        return new CharacterProfile
        {
            // ⚠ 锻造图的端口版本要借过来（V0.2.134）：不借的话恢复出来的存档是 0，
            // 下次一 Load 就被 ForgeGraphGen.MigrateIfPorts 当成老存档**再迁一次**，
            // 【if】的「是 / 否」两支会挪位（实测：6 张图各丢一条线 / 换一条线）。
            ForgeGraphPortVersion = t.ForgeGraphPortVersion,
            // 召唤物的宠物图路径借过来（工程里只能反推出「有没有」，反推不出用户原来选的是哪张图）。
            // 这里只借**第一只**的图当模板：真正的图片路径在下面 ParseSummon 里按工程里的 PNG 重新指过去。
            Summons = new System.Collections.ObjectModel.ObservableCollection<SummonSpec>(
                t.Summons is { Count: > 0 } ? new[] { new SummonSpec { Image = t.Summons[0].Image } }
                                            : Array.Empty<SummonSpec>()),
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
                ModImage = t.Art.ModImage,
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
        string emitted = cls;      // 生成时的类名（本地化键、卡池、初始卡组都按它找）
        // 生成器给「初始打击 / 防御」加了角色类名前缀（<角色类>Strike / <角色类>Defend）——
        // 因为本体的 ModelDb 只按**类名**注册模型，两个模组各自一个 class Strike 会撞车、游戏起不来。
        // 回读时把前缀摘掉：配置里仍然是 Strike / Defend（界面上这两张牌固定排在列表最上面）。
        {
            string cc = Naming.From(profile).CharClass;
            if (cls.Equals(cc + "Strike", StringComparison.OrdinalIgnoreCase)) cls = "Strike";
            else if (cls.Equals(cc + "Defend", StringComparison.OrdinalIgnoreCase)) cls = "Defend";
        }
        string text = ReadForRecovery(file);      // V0.2.128：锻造节点图那几段方法体整段抠掉（见 ReadForRecovery）
        string entry = EffectCatalog.SlugFor(emitted);
        var card = new CardSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
        };

        // 卡牌自定义描述：生成时写的是 `// CET:CustomDescReplace=0/1 CET:CustomDescription=<换行转义过的文本>`
        // （见 CSharpCodeGen.CardSource）—— 从本地化表里认不出来（那张表里是「自动描述 + 你写的」拼起来的）
        if (ParseCustomDescription(text) is var (cdText, cdReplace))
        {
            card.CustomDescription = cdText;
            card.CustomDescriptionReplaces = cdReplace;
        }
        // 「升级后的自定义描述」：另一行 CET:UpgradedCustomDescription=（生成出来是 {IfUpgraded:show:…|…}，
        // 从描述表里拆不回来，所以也走标记）
        card.UpgradedCustomDescription = ParseUpgradedCustomDescription(text);

        // 费用可能是负数（诅咒固定 -1），所以数字部分要允许前导 -
        var ctor = Regex.Match(text, @": base\((-?\d+), CardType\.(\w+), CardRarity\.(\w+), TargetType\.(\w+)\)");
        if (ctor.Success)
        {
            card.Cost = int.Parse(ctor.Groups[1].Value, CultureInfo.InvariantCulture);
            card.CardType = ctor.Groups[2].Value;
            card.Rarity = ctor.Groups[3].Value;
        }
        if (text.Contains("HasEnergyCostX => true")) card.CostIsX = true;
        if (text.Contains("HasStarCostX => true")) card.StarCostIsX = true;
        // 可被无限升级：生成时写的是 `public override int MaxUpgradeLevel => int.MaxValue;`
        // （诅咒写的是 => 0，那不是这一项）—— 直接按这行认，不用另加标记。
        if (text.Contains("MaxUpgradeLevel => int.MaxValue")) card.InfinitelyUpgradable = true;
        // V0.2.131：「升级后费用」写 X+N 时生成 `ResolveEnergyXValue() + (base.IsUpgraded ? N : 0)`。
        // N = 1 落回老字段 XPlusOnUpgrade（生成文本与老存档完全一样）；
        // N > 1 落到 UpgradeCostFormula（新字段）—— 两者再生成出来都是同一行代码，所以「生成 → 回读 → 再生成」幂等。
        var xPlus = Regex.Match(text, @"ResolveEnergyXValue\(\) \+ \(base\.IsUpgraded \? (\d+) : 0\)");
        if (xPlus.Success && int.TryParse(xPlus.Groups[1].Value, out int xPlusN))
        {
            if (xPlusN == 1) p_xPlus(card);
            else card.UpgradeCostFormula = "X+" + xPlusN.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            // 「升级后 X + N」还有另一条载体：公式序幕那句注释
            // `// X = 打出这张牌那一刻的当前能量（升级后 X + 1）`（见 ForgeGraphGen.EmitCardFormulaPrologue）。
            // 牌上一条「= X」的老效果都没有、全靠公式里的 X 时，生成的代码里只有这句注释带得出 N
            //（没有它，「升级后费用填 X+1」回读后就会丢，再生成出来就少一个 +1 —— 幂等会破）。
            var xPlusNote = Regex.Match(text, @"// X = .*（升级后 X \+ (\d+)）");
            if (xPlusNote.Success && int.TryParse(xPlusNote.Groups[1].Value, out int noteN))
            {
                if (noteN == 1) p_xPlus(card);
                else card.UpgradeCostFormula = "X+" + noteN.ToString(CultureInfo.InvariantCulture);
            }
        }

        var vars = ParseVars(text);
        // ParseEffects 会**消耗** vars（按种类 / 名字取走变量值，避免被后面的效果按顺序捡走），
        // 所以给 ParseUpgrade 一份**独立的副本**：升级增量要靠「变量在 CanonicalVars 里的位置」
        // 反查效果，被消耗过就对不上号了（会变成「升级增量找不到对应效果」）。
        var varsForUpgrade = new List<Var>(vars);
        // 诅咒：效果写在 OnTurnEndInHand 里（不是 OnPlay），而且没有「打出的目标」这一说 ——
        // 语句是照遗物那套生成的，所以按**遗物**的上下文解析（ctx 用 Card 会去认 cardPlay.Target）
        bool curse = card.IsCurseCard;
        // 需求⑩「卡牌时机」：带这个条件的效果不在 OnPlay 里，而是各自一个钩子方法。这里把它们的**方法体**
        // 按生成顺序（= EffectCatalog.CardTimings 的顺序 = CanonicalVars 的声明顺序）拼在 OnPlay 后面。
        // 每个方法体里都有生成器写的 `// CET:CardTiming=X` 标记 —— ParseEffects 见到它就记住
        // 「接下来这些效果属于哪个子时机」（见那里的 timingCond），不用再包一层假 if：
        // 钩子里有 EmitRepeated 生成的裸花括号块，那个 `}` 会把假 if 的栈帧弹掉（试过，第二条效果就丢了条件）。
        // 为什么必须**拼成一个字符串**交给同一次 ParseEffects：变量是按顺序配对的（NextVar），
        // 分几次调用会各自从头配对，数值会串到别的效果上。
        string body = BodyOf(text, curse ? "OnTurnEndInHand" : "OnPlay");
        if (!curse)
        {
            foreach (var opt in EffectCatalog.CardTimings)
            {
                string hookBody = BodyOf(text, CSharpCodeGen.CardTimingMethodName(opt.Id) + "(");
                if (hookBody.Length == 0) continue;
                body += "\n" + hookBody + "\n";
            }
            // 「卡自己的钩子」那两类效果（「将此牌放回手牌」→ AfterCardPlayedLate、
            // 「抽到这张牌时它本场战斗费用 -N」→ AfterCardDrawn）：实现写在钩子里
            //（打完牌之后才能搬 / 抽到时才知道），但它们的识别行只有标记那一条 ——
            // 所以这里**只把标记行**拼进解析流（CET:Effect=ReturnSelfToHand / CheapenSelfOnDraw），
            // 实现语句整段丢掉。
            // 为什么不做成「拼整个方法体 + 跳过名单」：那个名单得逐行列出钩子里的实现语句，
            // 而条件 / 概率那层守卫的文本是任意表达式，列不全（漏一行就会多回读一条 / 报「没认出来」）。
            foreach (string cardHook in new[] { "AfterCardPlayedLate", "AfterCardDrawn" })
            {
                string cardHookBody = BodyOf(text, cardHook + "(");
                if (cardHookBody.Length == 0) continue;
                var cardHookMarkers = cardHookBody.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Contains("CET:Effect=ReturnSelfToHand", StringComparison.Ordinal)
                             || l.Contains("CET:Effect=CheapenSelfOnDraw", StringComparison.Ordinal));
                body += "\n" + string.Join("\n", cardHookMarkers) + "\n";
            }
        }
        ParseEffects(card.Effects, body, vars, nameToPowerId,
            curse ? EffectCtx.Relic : EffectCtx.Card, result, cls, result.PetClassNames);
        if (curse)
        {
            // 「战斗结束时如果它还在牌组里就删掉自己」（本体「罪恶 Guilty」那套）
            card.CurseRemoveAfterCombat = Regex.IsMatch(text, @"CardPileCmd\.RemoveFromDeck\(this\)");
        }
        else
        {
            ParseUpgrade(card, varsForUpgrade, text, result, cls);
        }
        ParseKeywords(card, text);
        // 自定义关键词：生成的 ExtraHoverTips 里写的是 new LocString("card_keywords", "<KEY>.title")
        var keywordRefs = Regex.Matches(text, @"LocString\(""card_keywords"", ""([^""]+)\.title""\)")
            .Select(m => m.Groups[1].Value)
            .Where(x => !KeywordGen.IsReservedKey(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // V0.2.136「升级后获得 / 失去的自定义关键词」：那一行标记里存的键是**升级后那两栏专有**的，
        // 而上面那句 ExtraHoverTips 扫描出来的是「升级前 + 升级后」的并集（条件分支里两条都在）——
        // 所以要把标记里出现的键从 KeywordIds 里摘出去，否则「升级后获得的」会被当成一开始就有的。
        var upKwAdd = MarkerList(text, "UpgradeKeywordAdd");
        var upKwDel = MarkerList(text, "UpgradeKeywordRemove");
        foreach (string k in upKwAdd.Concat(upKwDel))
            keywordRefs.RemoveAll(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
        if (keywordRefs.Count > 0) card.KeywordIds = keywordRefs;
        if (upKwAdd.Count > 0) card.UpgradeAddedCustomKeywords = upKwAdd;
        if (upKwDel.Count > 0) card.UpgradeRemovedCustomKeywords = upKwDel;

        string starCost = Match(text, @"CanonicalStarCost => (\d+)");
        int baseStar = starCost is null ? 0 : int.Parse(starCost, CultureInfo.InvariantCulture);
        // V0.2.131：「额外资源量费用」框里写过的原文（`// CET:StarCostFormula=`）优先 ——
        // 有它就不按「CanonicalStarCost → 凭空补一条负数效果」那条老口径走（那条会改动生成结果，幂等会破）。
        string? starFormulaRaw = Match(text, @"CET:StarCostFormula=([^\r\n]*)");
        if (starFormulaRaw is not null)
            card.StarCostFormula = CSharpCodeGen.UnescapeMarker(starFormulaRaw.Trim());
        else if (starCost is not null)
            card.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -baseStar, TargetSide = "Self" });
        else if (card.StarCostIsX && !card.Effects.Any(e => e.Kind == "ExtraResource" && e.Amount < 0))
        {
            // 「额外资源量费用为 X」：老工程里这笔开销不生成代码（走本体 HasStarCostX），
            // 但配置里得有一条负数效果，卡面才会写「需要 X 点…」—— 补一条 -1（X 费时数额不影响玩法）
            card.Effects.Add(new EffectSpec { Kind = "ExtraResource", Amount = -1, TargetSide = "Self" });
        }
        // V0.2.131「升级后额外资源量费用」：生成时是 OnUpgrade 里的 `UpgradeStarCostBy(差值)`
        //（本体 CardModel 自带的方法）—— 回读按「基础值 + 差值」还原成绝对值。
        var starUp = Regex.Match(text, @"UpgradeStarCostBy\((-?\d+)\)");
        if (starUp.Success && int.TryParse(starUp.Groups[1].Value, out int starDelta))
            card.UpgradeStarCost = baseStar + starDelta;

        ParseCardCondition(card, text, nameToPowerId, result, cls);
        // V0.2.134：锻造节点图（从工程恢复）。生成端把整张图写成一行 `// CET:ForgeGraph=<版本>:<Base64>`
        // （见 ForgeGraphGen.MarkerOf）—— 这里按标记把图整个装回来，并把 IsForged 置 true。
        // 老工程（V0.2.133 及以前，没有这行标记）→ ReadMarker 返回 null、**不报错**，
        // 图仍然是空的、IsForged 仍然是 false（和以前逐字一样，不产生「没认出来」）。
        if (ForgeGraphGen.AttachMarker(file, card, result, $"卡牌「{cls}」的锻造节点图",
                portVersionSink: () => profile.ForgeGraphPortVersion = ForgeGraphGen.PortVersion))
            result.IsForged.Add($"{card.Name}（{card.ForgeGraph!.Nodes.Count} 个模块 / {card.ForgeGraph.Links.Count} 条连线）");
        ForgeGraphGen.AttachMarker(file, card, result, $"卡牌「{cls}」的升级后锻造节点图", upgraded: true,
            portVersionSink: () => profile.ForgeGraphPortVersion = ForgeGraphGen.PortVersion);
        return card;

        static void p_xPlus(CardSpec c) => c.XPlusOnUpgrade = true;
    }

    /// <summary>CanonicalVars 里声明的数值变量（顺序 = 效果顺序）。</summary>
    private sealed record Var(string Kind, string? PowerId, decimal Amount, bool IsCards, bool IsEnergy, string? Name = null);

    /// <summary>
    /// 这个变量是「按生命值算」的宠物效果留下来的计算变量（CalculationBase / CalculationExtra / ExtraDamage / CalculatedBlock）。
    ///
    /// 为什么要单独挑出来：那几条效果在 CanonicalVars 里会多出 2~3 个条目，
    /// 而 <see cref="ParseEffects"/> 是按顺序（<see cref="NextVar"/>）把变量配给效果的 ——
    /// 不挑出来的话，后面所有效果的变量都会错位一格（数值全串到别的效果上）。
    /// </summary>
    private static bool IsCalcVar(Var v) => v.Kind is
        "CalculationBase" or "CalculationExtra" or "ExtraDamage" or "CalculatedBlock" or "CalculatedDamage";

    /// <summary>按名字找计算变量的值（取出后就从 <paramref name="vars"/> 里删掉，免得被别的效果按顺序捡走）。</summary>
    private static decimal? TakeCalcVar(List<Var> vars, string kind)
    {
        int at = vars.FindIndex(v => string.Equals(v.Kind, kind, StringComparison.Ordinal));
        if (at < 0) return null;
        decimal value = vars[at].Amount;
        vars.RemoveAt(at);
        return value;
    }

    private static List<Var> ParseVars(string text)
    {
        string block = BlockAfter(text, "CanonicalVars =>");
        // **一次按声明顺序扫完** —— 顺序必须和生成代码里一致：升级增量是按「变量在 CanonicalVars 里
        // 的第几个」反查效果的，顺序错了增量就配到别的效果上（自检测出来过）。
        //   · 带名字的普通 DynamicVar（召唤伙伴的 PetHp / PetDamage、强化指定卡牌的 Boost）名字在第一个参数；
        //   · 其它变量（DamageVar / BlockVar / PowerVar<T> …）既可能带名字（同种变量第二次的别名、
        //     牺牲伙伴的收益），也可能不带名字。
        // 以前 DynamicVar 是**先单独扫一遍**、其余再扫一遍，等于把 DynamicVar 全排到最前面 ——
        // 一张牌上同时有「伤害」和「强化指定卡牌」时，两边的升级增量会互换。
        // 两种声明都要收进**同一个列表**、而且必须保持生成代码里的先后顺序：
        //   ① 普通变量：new DamageVar(3m, ValueProp.Move) / new PowerVar<VulnerablePower>(2m) …
        //   ② 「数值公式」的现算变量（V0.2.112 起卡牌的固定数值会被迁成公式，声明变成
        //      `new ForgeFormulaDamageVar(static (card, target) => (8m))` /
        //      `new ForgeFormulaPowerVar<VulnerablePower>(static (card, target) => (1m))`）。
        //
        // ②以前**完全没被认出来**（下面这个正则要求 `(` 后面紧跟数字字面量，公式那支是 `static …`）→
        // 变量表里整条缺失，后果有两层（第 9 批验证时实测）：
        //   · OnUpgrade 的升级增量对不上效果 → 回读报「升级增量找不到对应效果（变量 Damage）」，用户以为配置丢了；
        //   · 这张牌**后面**那几个变量在表里的次序全部前移 → 按顺序配的数值会串到别的效果上。
        // 所以这里单独扫一遍公式变量，按出现位置和普通变量合并（顺序 = 声明顺序，红线）。
        var found = new List<(int At, Var V)>();
        foreach (Match m in Regex.Matches(block, @"new (\w+)Var(?:<(\w+)>)?\((?:""(\w+)"",\s*)?(-?[\d.]+)m?"))
        {
            string kind = m.Groups[1].Value;
            string? power = m.Groups[2].Success ? m.Groups[2].Value : null;
            string? alias = m.Groups[3].Success ? m.Groups[3].Value : null;
            decimal amount = decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            found.Add((m.Index, kind == "Dynamic"
                // 带名字的普通 DynamicVar：变量键（名字）放在 PowerId 里，Name 也一起给上（和 VarNameOf 的约定一致）
                ? new Var("DynamicVar", alias, amount, false, false, alias)
                : new Var(kind, power, amount, kind == "Cards", kind == "Energy", alias)));
        }
        // 公式变量：`new ForgeFormula<种类>Var[<状态>](["名字", ]static (card, target) => (……), [增量m])`
        // 种类后缀和普通变量名一一对应（Damage / Block / HpLoss / Heal / Power）——
        // VarNameOf 靠它算变量键（`Damage` / `VulnerablePower`…），升级增量就是按这个键对回效果的。
        //
        // ⚠ 公式里可能套很深（`(((card.Owner)?.PlayerCombatState?.TurnNumber ?? 0) * (3m))`），
        //   想用一个「配平括号」的正则把整个参数表吃下来会失手（实测：真·现算公式那条整条漏掉，
        //   于是回读又报「升级增量找不到对应效果」）—— 所以只在**这一行**里取值：
        //     别名 = 第一个字符串字面量；升级增量 = 结尾那个 `, Nm)`；数值 = 纯常数公式里的 `(Nm)`。
        foreach (Match m in Regex.Matches(block, @"new ForgeFormula(\w+?)Var(?:<(\w+)>)?\("))
        {
            int lineEnd = block.IndexOf('\n', m.Index);
            if (lineEnd < 0) lineEnd = block.Length;
            string line = block.Substring(m.Index, lineEnd - m.Index);
            string suffix = m.Groups[1].Value;
            string? power = m.Groups[2].Success ? m.Groups[2].Value : null;
            Match aliasMatch = Regex.Match(line, @"\(""(\w+)"",");
            string? alias = aliasMatch.Success ? aliasMatch.Groups[1].Value : null;
            // 数值：**纯常数公式**（老存档迁过来的固定值就是这种）取 `(8m)`；真·现算公式取不到就记 0 ——
            // 数值本身由生成端写的 `// CET:AmountFormula=` 标记负责还原，这里只要「种类 + 名字 + 顺序」对。
            Match constMatch = Regex.Match(line, @"=>\s*\((-?[\d.]+)m\)");
            decimal fxAmount = constMatch.Success
                ? decimal.Parse(constMatch.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0m;
            // 升级增量（生成端写在参数表最后那个 `, 6m)`）不用在这里还原 —— 它由 `OnUpgrade` 里那句
            // `UpgradeValueBy(6m)` 负责（下面 ParseUpgrade 按变量名对回效果）。这里只要「种类 + 名字 + 顺序」对。
            found.Add((m.Index, new Var(suffix, power, fxAmount, false, false, alias)));
        }
        return found.OrderBy(x => x.At).Select(x => x.V).ToList();
    }

    /// <summary>生成代码里的一层块（循环 / 条件）。</summary>
    private sealed record Block(bool IsLoop, string Count, ConditionSpec? Cond);

    /// <summary>把效果语句反推成 EffectSpec（本文件的核心）。</summary>
    private static void ParseEffects(IList<EffectSpec> into, string body, List<Var> vars,
        Dictionary<string, string> nameToPowerId, EffectCtx ctx, RecoveryResult result, string where,
        IReadOnlyList<string>? petClassNames = null)
    {
        petClassNames ??= Array.Empty<string>();
        // 「按生命值算」的宠物效果（三个伙伴攻击 / 牺牲伙伴）会往 CanonicalVars 里多写 2~3 个计算变量，
        // 那些变量**不能**按顺序参与 NextVar（否则后面所有效果的数值全错位）。
        // 这里先把它们整组摘出来（同一条效果会连着写 CalculationBase + ExtraDamage + CalculatedDamage），
        // 剩下的变量顺序就和「普通效果」一一对应了。
        var calcVars = new List<Var>();
        {
            var rest = new List<Var>(vars.Count);
            foreach (var v in vars)
            {
                if (IsCalcVar(v)) { calcVars.Add(v); continue; }
                rest.Add(v);
            }
            vars = rest;
        }
        var lines = body.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var frames = new List<Block>();          // 当前所在的块（循环 / 条件），按嵌套顺序
        int varIdx = 0;
        int hitsLiteral = 1;                     // 前面出现过的 int hits = N;
        bool randomFoes = false;                 // 出现了 List<Creature> foes（随机挑敌人的写法）
        bool foesRemoved = false;                // 随机挑敌人且不允许重复
        int i = 0;

        string? LoopTop() => frames.LastOrDefault(f => f.IsLoop)?.Count;
        ConditionSpec? CondTop() => frames.LastOrDefault(f => f.Cond is not null)?.Cond;

        // 新增的那批宠物效果：生成时会写一行 `// CET:PetEffect=<Kind> CET:PetFormula=…`，
        // 这里先记下来，等真正那条语句出现时再按它还原（见 PetEffectFromMarker）。
        string? pendingPetMarker = null;
        int pendingPetMultiplier = 0;

        // 「全部召唤物」（PetGen.AllId）：生成时会把一条效果按宠物**逐只展开**成 N 份代码，
        // 每份前面写一行 `// CET:PetAll=<组号>`（同一组共用组号）。回读时把同组的第 2..N 份丢掉，
        // 只留第一份并把 PetSummon 设成 "*" —— 这样「生成 → 回读」才是一条进、一条出。
        string? pendingPetAllGroup = null;
        var allPetGroups = new HashSet<string>(StringComparer.Ordinal);

        // 「数值公式」（需求：「另加一栏『数值公式』」）：生成时在那条效果前面单独写一行
        // `// CET:AmountFormula=<公式>`，这里先记下来、由紧跟着那条效果的 Done 认领。
        // 为什么要标记：公式在生成的代码里已经变成算完的表达式（`(__pM * (3m))`），
        // 从表达式**反推不回**用户写的原文（也认不出用了哪些参数）。
        string? pendingFormula = null;
        // 次数类公式（用户要求⑦）：生成时写一行 `// CET:CountFormulas=Times=<公式>|Repeat=<公式>|…`，
        // 同样由紧跟着那条效果的 Done 认领（键=公式，用 | 分隔）。
        var pendingCountFormulas = new Dictionary<string, string>(StringComparer.Ordinal);
        // 条件数值的公式（用户要求⑦）：`// CET:CondFull=Kind=…|Amount=…|…|AmountFormula=…`，
        // 整条条件都在这行里（守卫那一行已经变成算完的表达式，反推不回来）。
        ConditionSpec? pendingCondFull = null;
        // 作用对象是「队友」的效果：生成出来的语句和「指定敌人」一模一样（都是 cardPlay.Target），
        // 反推不出这一档，所以生成时写一行 `// CET:TargetSide=Ally|AllAllies`，由紧跟着那条效果认领。
        // V0.2.132：新档「全体单位」也走这一行标记；「全部友方不包括召唤物」在同一行补 ` CET:AllyNoPets=1`。
        string? pendingTargetSide = null;
        bool pendingAllyNoPets = false;
        // 「不同效果的概率之间是否独立」（V0.2.132）：`// CET:ChanceIndependent=1 CET:ChancePct=70`
        // 由紧跟着那条效果认领（只在显式勾了「独立」时才写）。
        decimal? pendingChanceIndependent = null;

        // 「给予卡牌关键词」：生成时写一行 `// CET:GiveKeyword=<本体枚举名 或 custom:键> CET:GiveKeywordTemp=0/1`。
        // 关键词和「是不是临时」只能靠这行标记还原；数值 / 选牌方式 / 哪一摞牌从紧跟着的那几行代码里读，
        // 之后那几行（AddKeyword / 临时 Power / foreach…）全部跳过，免得被当成别的效果。
        bool skipGivenKeywordBody = false;

        // 「自己搞定一整段」的新效果（CET:Effect=…，见 CSharpCodeGen.SelfContainedMarker）：
        // 标记行收尾之后，把它的实现代码整段跳过（不跳的话预见的 CardCmd.Discard 会被当成「丢弃卡牌」、
        // 毒性爆发的 PowerCmd.Apply<PoisonPower> 会被当成「施加中毒」）。
        bool skipSelfContainedBody = false;
        // 标记行里写的效果种类：有几段实现**没有**独特记号（例：「禁止获得格挡」的正数分支就是一句
        // PowerCmd.Apply<NoBlockPower>），只能按种类认下来，否则会被再当成一条普通效果多回读一条。
        string? selfContainedKind = null;

        // 「生成 / 变化卡牌」的附加设置（CET:SpawnPick= 标记）：标记行先存着，等真正那条效果出现时配上。
        EffectSpec? pendingSpawn = null;

        // 「范围界定」（需求 ③：选牌类效果限定只对某一类牌 / 某一组牌动手）的标记行里额外带着效果种类
        // （CET:Kind=）—— 那条效果的代码万一没被认出来，也不会把范围配到**另一个种类**的效果身上。
        // 老工程（没这一项）保持原样：见了谁都配。
        string? pendingSpawnKind = null;

        // 「从哪里选牌」：消耗 / 变化 / 丢弃的选牌语句里带着它（FromHand* = 手牌，PileType.X = 那一摞）。
        // 记在暂存里，等真正的动作语句（CardCmd.Exhaust / Transform / Discard）出现时再配给那条效果 ——
        // 以前完全没回读这个字段，回读出来的「从抽牌堆消耗 / 变化」会静默退回手牌。
        string? pendingSelectPile = null;

        // 「强化指定卡牌」：生成时写一行 `// CET:BoostCard=<卡类名> CET:BoostStat=Damage|Block`。
        // 目标卡与强化什么只能靠它还原（代码里只剩「Apply 到某个 ForgeBoost Power」这一句）；
        // 数值走 CanonicalVars 里的 Boost 变量（别名 Boost2 由 NextVar 顺位找）。
        string? pendingBoostMarker = null;

        // 牺牲伙伴生成的是「先算收益 → CreatureCmd.Kill(宠物) → 再 GainBlock / Attack」，
        // 收益那一句既可能是前面的 `decimal gain/dmg = …`（已跳过）也可能是后面的动作行。
        // 所以看到 Kill 时**只记住宠物**，等收益动作行出现时再拼成一条效果 ——
        // 在 Kill 那行就收尾的话，后面的 GainBlock / Attack 会被再当成一条普通效果 → 多回读一条。
        string? sacKillPet = null;

        // 需求⑩「卡牌时机」：ParseCard 会把各时机钩子的方法体拼在 OnPlay 后面，每个方法体开头有
        // 生成器写的 `// CET:CardTiming=<子时机>` 标记。这里记下来 —— 之后解析到的效果如果自己没有
        // 条件（CondTop() 为 null），就归属到这个子时机上（直到下一个标记出现）。
        // 为什么不包一层假 if：钩子体里有 EmitRepeated 产生的裸 `{`/`}`，`}` 会把假 if 的栈帧弹掉。
        string? timingCond = null;

        // 收尾：把「每条效果自己的条件」带上（生成器是用一层 if 包的）
        void Done(EffectSpec e)
        {
            e.Condition = CondTop()
                ?? (timingCond is null ? new ConditionSpec() : new ConditionSpec { Kind = "CardTiming", Timing = timingCond });
            // 「数值公式」：紧跟在标记后面的那条效果认领它（见上面的 pendingFormula）。
            // 必须在别的分支之前认领 —— Done 后面有 early return（全部召唤物的同组第 2..N 份），
            // 那条路径上也不能把这个待领的标记漏给下一条效果。
            if (pendingFormula is not null)
            {
                e.AmountFormula = pendingFormula;
                pendingFormula = null;
            }
            // 次数类公式（用户要求⑦）：同一行标记带着全部键，一起认领
            if (pendingCountFormulas.Count > 0)
            {
                foreach (var kv in pendingCountFormulas) e.SetCountFormulaFromRecovery(kv.Key, kv.Value);
                pendingCountFormulas.Clear();
            }
            // 条件数值的公式（用户要求⑦）：整条条件从标记里重建（守卫表达式反推不回来）
            if (pendingCondFull is not null)
            {
                e.Condition = pendingCondFull;
                pendingCondFull = null;
            }
            // 「作用对象 = 队友」的标记（Ally / AllAllies）：同样由紧跟着那条效果认领。
            if (pendingTargetSide is not null)
            {
                e.TargetSide = pendingTargetSide;
                if (pendingAllyNoPets) e.AllAlliesExcludePets = true;
                pendingTargetSide = null;
                pendingAllyNoPets = false;
            }
            // 「不同效果的概率之间是否独立」（V0.2.132）：标记里带着百分比 —— 一条效果自己独立掷一次。
            if (pendingChanceIndependent is { } chancePct)
            {
                e.ChanceEnabled = true;
                e.ChanceIndependent = true;
                e.ChancePercent = chancePct;
                pendingChanceIndependent = null;
            }
            // 「生成 / 变化卡牌」的附加设置（范围限定 / 升级 / 免费…）：标记行存下来的那几项配到这条效果上。
            // 「范围界定」（需求 ③：消耗 / 丢弃 / 升级 / 给关键词 / 复制 / 重放 / 回合结束自动打出 /
            // 从三摞牌拿牌 / 预见）用的是同一份标记，所以这里把「选牌类效果」也认下来；
            // 带 CET:Kind= 的标记再核对一次种类（防止那条效果的代码没被认出来时，范围串到别的效果身上）。
            if (pendingSpawn is not null
                && (e.UsesSpawnOptions || e.UseCardScopeFilter)
                && (pendingSpawnKind is null || string.Equals(pendingSpawnKind, e.Kind, StringComparison.Ordinal)))
            {
                e.SpawnPick = pendingSpawn.SpawnPick;
                e.SpawnFilter = pendingSpawn.SpawnFilter;
                e.SpawnChoice = pendingSpawn.SpawnChoice;
                e.SpawnUpgraded = pendingSpawn.SpawnUpgraded;
                e.SpawnFree = pendingSpawn.SpawnFree;
                e.SpawnFreeThisTurn = pendingSpawn.SpawnFreeThisTurn;
                e.SpawnUpgradedThisTurn = pendingSpawn.SpawnUpgradedThisTurn;
                // 需求 ⑦：生成 / 变化出来的卡带上的关键词（原样还原用户选的那个引用）
                e.SpawnKeyword = pendingSpawn.SpawnKeyword;
                // 「从范围里自己挑一张」（用户要求）
                if (pendingSpawn.SpawnChosen) e.SpawnChosen = true;
                pendingSpawn = null;
                pendingSpawnKind = null;
            }
            if (pendingPetAllGroup is not null)
            {
                string grp = pendingPetAllGroup;
                pendingPetAllGroup = null;      // 每次收尾都清掉：下一个效果要重新出现标记才算同一组
                // 只有宠物类效果才认这个组号（组号是宠物展开时写的；万一漏到普通效果上，
                // 也不能把「造成伤害」这种效果标成「全部召唤物」，更不能因为「同组已见过」把它丢掉）
                if (EffectCatalog.IsPetKind(e.Kind))
                {
                    if (!allPetGroups.Add(grp))
                    {
                        pendingPetMarker = null;    // 同一组的第 2..N 份（同一条效果的逐只展开）→ 丢掉
                        return;
                    }
                    e.PetSummon = PetGen.AllId;
                }
            }
            into.Add(e);
            pendingPetMarker = null;
        }

        // 「格挡 / 回复生命 / 失去生命 / 最大生命」这几种效果**也能对敌人生效**，所以回读时必须认出作用对象。
        // 生成的目标写法只有三种：cardPlay.Target（指定敌人）/ foe（全体 · 随机）/ 其余（自己的 base.Owner.Creature）。
        // 以前这几条不设 TargetSide，于是 EffectSpec 的默认值 "Enemy" 一路留着 ——
        // 回读出来的「给自己加 5 点格挡」会变成「给指定敌人加格挡」（下次生成就真的加到敌人身上了）。
        string SideOfTarget(string text)
        {
            // 「全体单位」（V0.2.132）：循环体是 CombatState.Creatures（两边所有存活单位）。
            // 卡牌链还有 `// CET:TargetSide=AllUnits` 标记兜底；这里认一下，让遗物 / 状态链也能反推。
            if (text.Contains("CombatState.Creatures", StringComparison.Ordinal))
                return "AllUnits";
            if (text.Contains("cardPlay.Target", StringComparison.Ordinal) || text.Contains("target", StringComparison.Ordinal))
                return "Enemy";
            if (text.Contains("foe", StringComparison.Ordinal))
                return randomFoes ? "RandomEnemies" : "AllEnemies";
            return "Self";
        }

        // 「按生命值算」的宠物攻击 / 牺牲伙伴的收益：都用 `base.DynamicVars.CalculatedDamage` /
        // `CalculatedBlock` 取（本体那两个计算变量是固定名字的），所以这里按名字从 calcVars 里捞。
        decimal CalcAmount(string kind) => TakeCalcVar(calcVars, kind) ?? 0m;
        if (where == "ProbeRecMulti")
        {
            int n = lines.Count(l => l.Trim().Length > 0);
            string first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            string last = lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        }

        // 牺牲伙伴那条效果里的宠物（`await CreatureCmd.Kill(__uiCheckPet);`）。
        // 生成时机：收益是伤害时 Kill 在 Attack **之前**；收益是格挡时 Kill 在 GainBlock **之前**（当前行之后）。
        // 所以两个方向各扫几行（只在这个效果的邻域里找，不会串到别的效果上）。
        string? KillPetVar()
        {
            for (int k = i - 1; k >= 0 && k >= i - 6; k--)
            {
                string? v = Match(lines[k].Trim(), @"CreatureCmd\.Kill\((\w+)\)");
                if (v is not null) return v;
            }
            for (int k = i; k < lines.Count && k <= i + 8; k++)
            {
                string? v = Match(lines[k].Trim(), @"CreatureCmd\.Kill\((\w+)\)");
                if (v is not null) return v;
            }
            return null;
        }

        // 新增的那批宠物效果：按标记注释还原（标记里带着种类 + 公式 + 倍率，见 CSharpCodeGen.MarkerText）。
        // 返回 null 表示这条语句认不出来（调用处会记进 Unparsed，不静默丢）。
        EffectSpec? PetEffectFromMarker(string? petVarName, out string? why)
        {
            why = null;
            if (pendingPetMarker is null)
            {
                why = "（生成的代码里没写 // CET:PetEffect= 标记）";
                return null;
            }
            string kindId = Match(pendingPetMarker, @"CET:PetEffect=(\w+)") ?? "";
            string formula = Match(pendingPetMarker, @"CET:PetFormula=(\w+)") ?? "";
            var e = new EffectSpec();
            switch (kindId)
            {
                case "PetCalcAttack":
                    e.Kind = formula switch
                    {
                        "curhp" => "PetDamageByCurHp",
                        "missinghp" => "PetDamageByMissingHp",
                        _ => "PetDamageByMaxHp",
                    };
                    e.Amount = CalcAmount("CalculationBase");
                    break;
                case "PetSacrificeBlock":
                case "PetSacrificeDamage":
                case "PetSacrificePower":
                    e.Kind = "PetSacrifice";
                    e.PetSacrificeGain = kindId switch
                    {
                        "PetSacrificeDamage" => "Damage",
                        "PetSacrificePower" => "Power",
                        _ => "Block",
                    };
                    e.PetSacrificeFormula = formula switch
                    {
                        "fixed" => "Fixed",
                        "curhp" => "CurHp",
                        _ => "MaxHp",
                    };
                    if (pendingPetMultiplier > 0) e.PetSacrificeMultiplier = pendingPetMultiplier;
                    // 收益是「获得增益 / 减益」时，要挂的是哪个状态写在**同一行标记**里（CET:PetPower=…）
                    if (e.PetSacrificeGain == "Power") e.PowerId = Match(pendingPetMarker, @"CET:PetPower=(\w+)");
                    break;
                case "PetHeal": e.Kind = "PetHeal"; break;
                case "PetLoseHp": e.Kind = "PetLoseHp"; break;
                case "PetGainMaxHp": e.Kind = "PetGainMaxHp"; break;
                case "PetApplyPower":
                    e.Kind = "PetApplyPower";
                    e.PowerId = Match(pendingPetMarker, @"CET:PetPower=(\w+)");
                    break;
                case "PetGuardOn": e.Kind = "PetGuardOn"; break;
                case "PetGuardOff": e.Kind = "PetGuardOff"; break;
                default:
                    why = $"(认不出来的宠物效果标记「{kindId}」)";
                    return null;
            }
            if (petVarName is not null) e.PetSummon = PetClassOfVar(petVarName, petClassNames);
            return e;
        }

        while (i < lines.Count)
        {
            string raw = lines[i];
            string line = raw.Trim();
            i++;
            if (line.Length == 0) continue;
            // 需求⑩：生成器为「卡牌时机」写的两句**胶水代码**，它们不是效果，回读时整行跳过 ——
            //   · `await Task.CompletedTask;`：保证「所有效果都挂在钩子上」的卡，OnPlay 里也有 await（不然 CS1998）；
            //   · `...ForgeGivenKeywordTiming.Notify(...)`：把「被给予自定义关键词」转给卡自己的钩子那一步。
            if (line.StartsWith("await Task.CompletedTask;", StringComparison.Ordinal)) continue;
            if (line.Contains("CET:GivenKeywordNotify", StringComparison.Ordinal)) continue;
            if (line.StartsWith("//"))
            {
                // 需求⑩「卡牌时机」：这个标记说明「接下来的效果挂在这个子时机的钩子上」（见 Done / timingCond）
                string? timingRaw = Match(line, @"CET:CardTiming=(\w+)");
                if (timingRaw is not null)
                {
                    timingCond = timingRaw;
                    continue;
                }
                string? mk = Match(line, @"CET:PetEffect=(\w+)");
                if (mk is not null)
                {
                    pendingPetMarker = line;
                    pendingPetMultiplier = (int)Dec(line, @"CET:PetMul=([\d.]+)", 0);
                }
                // 「全部召唤物」的组号（生成器在每一份展开代码前都写一遍）
                string? allGrp = Match(line, @"CET:PetAll=(\w+)");
                if (allGrp is not null) pendingPetAllGroup = allGrp;
                // 「数值公式」（需求：「另加一栏『数值公式』」）：公式里有空格 / 运算符，
                // 所以抓到**行尾**（标记固定独占一行，后面不会再有别的内容）。
                string? formulaRaw = Match(line, @"CET:AmountFormula=(.*)$");
                if (formulaRaw is not null)
                    pendingFormula = CSharpCodeGen.UnescapeMarker(formulaRaw.Trim());
                // 次数类公式（用户要求⑦）：`CET:CountFormulas=Times=<公式>|Repeat=<公式>|…`
                string? countRaw = Match(line, @"CET:CountFormulas=(.*)$");
                if (countRaw is not null)
                {
                    pendingCountFormulas.Clear();
                    foreach (string piece in CSharpCodeGen.UnescapeMarker(countRaw.Trim()).Split('|'))
                    {
                        int eq = piece.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = piece[..eq].Trim(), value = piece[(eq + 1)..].Trim();
                        if (EffectSpec.CountFormulaKeys.Contains(key, StringComparer.Ordinal) && value.Length > 0)
                            pendingCountFormulas[key] = value;
                    }
                }
                // 「不同效果的概率之间是否独立」（V0.2.132）：生成端**只在勾了「独立」时**才写这一行 ——
                // 所以看到它就等于「这条效果勾了概率 + 勾了独立」，百分比也在同一行里（回读本来不回读概率，
                // 见 EffectSpec.ChanceIndependent 的注释）。默认档一个字节都不会出现。
                if (line.Contains("CET:ChanceIndependent=1", StringComparison.Ordinal))
                    pendingChanceIndependent = Dec(line, @"CET:ChancePct=([\d.]+)", 50m);
                // 条件数值的公式（用户要求⑦）：整条条件都在这一行里
                string? condRaw = Match(line, @"CET:CondFull=(.*)$");
                if (condRaw is not null)
                    pendingCondFull = ConditionFromMarker(CSharpCodeGen.UnescapeMarker(condRaw.Trim()));
                // 「强化指定卡牌」的标记：目标卡 + 强化什么（数值看后面的 Boost 变量）
                string? boostRaw = Match(line, @"CET:BoostCard=(\S+)");
                if (boostRaw is not null) pendingBoostMarker = line;
                // 「给予卡牌关键词」的标记：关键词 + 是不是临时；数值 / 方式 / 哪一摞牌看接下来那几行
                string? gkRaw = Match(line, @"CET:GiveKeyword=(\S+)");
                if (gkRaw is not null)
                {
                    var gk = new EffectSpec
                    {
                        Kind = "GiveKeyword",
                        TempKeyword = line.Contains("CET:GiveKeywordTemp=1", StringComparison.Ordinal),
                        GivenKeyword = gkRaw.StartsWith("custom:", StringComparison.Ordinal) ? gkRaw[7..] : gkRaw,
                    };
                    string peek = string.Join("\n", lines.Skip(i).Take(6));
                    string? handSel = Match(peek, @"(CardSelectCmd\.FromHand\()");
                    string? pileSel = Match(peek, @"CardSelectCmd\.FromCombatPile\(choiceContext, PileType\.(\w+)");
                    string? randLoop = Match(peek, @"for \(int __kwIdx = 0; __kwIdx < (\w+); __kwIdx\+\+\)");
                    if (handSel is not null || pileSel is not null)
                    {
                        gk.CardPick = "Chosen";
                        gk.SelectPile = pileSel switch { "Draw" => "Draw", "Discard" => "Discard", "Exhaust" => "Exhaust", "Top" => "Top", _ => "Hand" };
                        string? cnt = Match(peek, @"CardSelectorPrefs\([^,]+,\s*(\w+)\)");
                        if (cnt is not null)
                        {
                            if (cnt == "x") gk.AmountIsX = true;
                            else gk.Amount = Dec(cnt, @"(\d+)", 1);
                        }
                    }
                    else if (randLoop is not null)
                    {
                        gk.CardPick = "Random";
                        gk.SelectPile = Match(peek, @"NextItem\(PileType\.(\w+)") switch
                        {
                            "Draw" => "Draw",
                            "Discard" => "Discard",
                            "Exhaust" => "Exhaust",
                            // 「牌顶」那一档生成的取法是「抽牌堆 .FirstOrDefault()」（没有 NextItem）
                            _ => peek.Contains("PileType.Draw.GetPile", StringComparison.Ordinal)
                                 && peek.Contains("FirstOrDefault()", StringComparison.Ordinal) ? "Top" : "Hand",
                        };
                        if (randLoop == "x") gk.AmountIsX = true;
                        else if (int.TryParse(randLoop, out int gkN)) gk.Amount = gkN;
                    }
                    else
                    {
                        gk.Amount = 0;                     // 数值 0 = 这张牌自己
                    }
                    Done(gk);
                    skipGivenKeywordBody = true;
                }
                // 「自己搞定一整段」的新效果（毒性爆发 / 预见 / 升级卡牌 / 大限已至）：
                // 标记那一行就带着全部信息（种类 / 数值 / 选牌方式 / 哪一摞），后面那几行是它的实现，整段跳过。
                string? scRaw = Match(line, @"CET:Effect=(\w+)");
                if (scRaw is not null)
                {
                    var sc = new EffectSpec
                    {
                        Kind = scRaw,
                        Amount = Dec(line, @"CET:Amount=(-?[\d.]+)", 0),
                        CardPick = Match(line, @"CET:Pick=(\w+)") ?? "Chosen",
                        SelectPile = Match(line, @"CET:Pile=(\w+)") ?? "Hand",
                        AmountIsStack = line.Contains("CET:Stack=1", StringComparison.Ordinal),
                        AmountIsX = line.Contains("CET:X=1", StringComparison.Ordinal),
                        // 「下回合生效」的生成类效果：标记行上多带了 CET:NextTurn=1（生成类专用）
                        NextTurn = line.Contains("CET:NextTurn=1", StringComparison.Ordinal),
                        Copies = Math.Max(1, (int)Dec(line, @"CET:Copies=(-?[\d.]+)", 1)),
                        ReplayTimes = Math.Max(1, (int)Dec(line, @"CET:Replay=(-?[\d.]+)", 1)),
                        // 「放置卡牌」放到哪一摞（手牌 / 抽牌堆 / 弃牌堆 / 消耗牌堆 / 牌顶）
                        PlaceTo = Match(line, @"CET:PlaceTo=(\w+)") ?? "Hand",
                    };
                    if (sc.Kind == "Outbreak") sc.TargetSide = "AllEnemies";
                    if (sc.Kind == "TimesUp") sc.TargetSide = "Enemy";
                    // 标记行里顺手带上的「哪一个状态 / 打谁」（「翻倍状态层数」要用）
                    string? scPower = Match(line, @"CET:Power=""?(\w+)");   // Lit.Str 会带引号
                    if (scPower is not null)
                        sc.PowerId = nameToPowerId.TryGetValue(scPower, out string? scPid) ? scPid : scPower;
                    string? scSide = Match(line, @"CET:Side=(\w+)");
                    if (scSide is not null) sc.TargetSide = scSide;
                    // 「用目标卡填满手牌」选的是哪张卡（代码里只剩 CreateCard<那张卡>）
                    string? scCard = Match(line, @"CET:Card=(\S+)");
                    if (scCard is not null) sc.SpawnCardId = scCard;
                    // 「刮削」的条件 / 操作：生成时**只有不是默认值**才写这两项（默认那一档的标记行
                    // 与老版本逐字一致）。所以这里缺项就补默认值 —— 老工程回读出来的行为完全不变。
                    // 条件可能是 `Keyword:XXX`（冒号），所以不能只认 \w。
                    if (sc.Kind == "ScrapeDraw")
                    {
                        sc.ScrapeCond = Match(line, @"CET:ScrapeCond=([^\s]+)") ?? EffectCatalog.ScrapeCondNonZeroCost;
                        sc.ScrapeOp = Match(line, @"CET:ScrapeOp=([^\s]+)") ?? EffectCatalog.ScrapeOpDiscard;
                    }
                    // 需求 ⑨：获得药水 —— 「指定还是随机 / 指定的是哪一瓶」只能靠标记还原
                    //（随机档的代码里连药水名都没有）。生成时**只在非默认值**才写这两项，
                    // 所以缺项按默认补：PotionPick 缺 = Fixed，Potion 缺 = 没选。
                    if (sc.Kind == "GainPotion")
                    {
                        sc.PotionPick = Match(line, @"CET:PotionPick=(\w+)") ?? "Fixed";
                        sc.PotionId = Match(line, @"CET:Potion=([A-Za-z_]\w*)");
                    }
                    // 「给予卡牌关键词」+「下回合生效」（V0.2.133）：延迟分支把关键词写在标记行上
                    //（` CET:Given=<关键词>`）—— 那一条的 CET:GiveKeyword= 标记在**延迟状态类体内**，
                    // 卡这边看不到，不从这里认就会丢配置。
                    if (sc.Kind == "GiveKeyword")
                    {
                        string? given = Match(line, @"CET:Given=(\w+)");
                        if (!string.IsNullOrWhiteSpace(given)) sc.GivenKeyword = given;
                    }
                    // 这几种效果在 CanonicalVars 里也各有一个变量（CardsVar / PowerVar<PoisonPower>），
                    // 这里顺手把它认领掉 —— 否则后面同类型的「抽牌」「施加中毒」会捞到错的那一个。
                    if (sc.Kind is "UpgradeCard" or "Scry" or "CopyCard" or "ReplayCard" or "TurnEndPlay" or "ScrapeDraw")
                        NextVar(vars, ref varIdx, "Cards");
                    if (sc.Kind == "Outbreak") NextVar(vars, ref varIdx, "Power:PoisonPower");
                    // 「大限已至」的变量是计算三件套，ParseEffects 开头已经把 calcVars 摘出去了，不用认领
                    ApplyLoop(sc, frames);
                    Done(sc);
                    skipSelfContainedBody = true;
                    selfContainedKind = sc.Kind;
                }
                // 「作用对象 = 队友」的标记行（只有 Ally / AllAllies 会写）：先存着，等紧跟着那条效果认领
                string? tsRaw = Match(line, @"CET:TargetSide=(\w+)");
                if (tsRaw is not null)
                {
                    pendingTargetSide = tsRaw;
                    // V0.2.132：「全部友方不包括召唤物」跟在同一行末尾（` CET:AllyNoPets=1`）
                    pendingAllyNoPets = line.Contains("CET:AllyNoPets=1", StringComparison.Ordinal);
                    continue;
                }
                // 「生成 / 变化卡牌」的范围限定 + 「生成出来的卡怎么处理」：
                // 标记那一行先存成一份「只填了这几个字段」的效果，等真正那条效果出现时再配上（见 ApplySpawnOpts）。
                string? spawnPick = Match(line, @"CET:SpawnPick=(\w+)");
                if (spawnPick is not null)
                {
                    // 范围限定可能是自定义关键词分组（`Keyword:FATE`），所以这里不能只认 \w（冒号会被截断）
                    string? flt = Match(line, @"CET:SpawnFilter=([^\s]+)");
                    string? choice = Match(line, @"CET:SpawnChoice=(\d+)");
                    // 「范围界定」（需求 ③）用的也是这份标记，只是额外带一个 CET:Kind=（见 pendingSpawnKind）
                    pendingSpawnKind = Match(line, @"CET:Kind=(\w+)");
                    pendingSpawn = new EffectSpec
                    {
                        SpawnPick = spawnPick,
                        SpawnFilter = flt is null or "-" ? "" : flt,
                        SpawnChoice = choice is not null && int.TryParse(choice, out int ch) && ch > 1 ? ch : 1m,
                        SpawnUpgraded = line.Contains("CET:SpawnUp=1", StringComparison.Ordinal),
                        SpawnFree = line.Contains("CET:SpawnFree=1", StringComparison.Ordinal),
                        SpawnFreeThisTurn = line.Contains("CET:SpawnFreeTurn=1", StringComparison.Ordinal),
                        SpawnUpgradedThisTurn = line.Contains("CET:SpawnUpTurn=1", StringComparison.Ordinal),
                        // 需求 ⑦：「生成 / 变化出来的卡带上关键词」。空 = 没配（默认档的标记行里没有这一项）
                        SpawnKeyword = Match(line, @"CET:SpawnKeyword=(\S+)") ?? "",
                        // 「从范围里自己挑一张」（用户要求）：只在勾了的时候才在标记行里
                        SpawnChosen = line.Contains("CET:Chosen=1", StringComparison.Ordinal),
                    };
                }
                continue;
            }

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
            // 牺牲伙伴的收益暂存（真正的动作是后面的 Kill + GainBlock / DamageCmd.Attack）
            if (line.StartsWith("decimal gain = ") || line.StartsWith("decimal dmg = ")) continue;
            // 选牌 / 变化 / 丢弃的前置语句（真正的动作在后面的 foreach + await 里）。
            // 顺便把「从哪里选牌」记下来（自己选的写法里只有这一行说了是哪一摞：
            // 手牌走 CardSelectCmd.FromHand / FromHandForDiscard，别的摞走 FromCombatPile(…, PileType.X)）。
            if (line.StartsWith("var toTransform") || line.StartsWith("var toExhaust")
                || line.StartsWith("var toDiscard") || line.StartsWith("var pick"))
            {
                pendingSelectPile = PileFromSelectCode(line) ?? pendingSelectPile;
                continue;
            }
            // 随机选牌那一行（CardModel? pick = …NextItem(PileType.X…)）：也同样记下是哪一摞
            if (line.StartsWith("CardModel? pick = ", StringComparison.Ordinal))
            {
                pendingSelectPile = PileFromSelectCode(line) ?? pendingSelectPile;
                continue;
            }

            // 「给予卡牌关键词」的代码行：效果本身在标记那一行就收尾了，这里只把这些语句跳过去
            // （花括号 / for / foreach 在上面已经交给 frames 处理，这里只认真正的语句）
            if (skipGivenKeywordBody)
            {
                bool gkBody = line.StartsWith("var __kwCards", StringComparison.Ordinal)
                    || line.StartsWith("CardModel? __kwCard", StringComparison.Ordinal)
                    || line.StartsWith("if (__kwCard is null) break;", StringComparison.Ordinal)
                    || line.Contains("AddKeyword(CardKeyword.", StringComparison.Ordinal)
                    || line.Contains("GiveSingleTurnRetain()", StringComparison.Ordinal)
                    || line.Contains("GiveSingleTurnSly()", StringComparison.Ordinal)
                    || line.Contains("AddGivenCustomKeyword(", StringComparison.Ordinal)
                    || line.Contains("ForgeTempKeywordPower", StringComparison.Ordinal);
                if (gkBody) continue;
                skipGivenKeywordBody = false;
            }

            // 「自己搞定一整段」的效果实现（见上面的 CET:Effect 标记）：整段跳过。
            // 这些行都是生成器为这四种效果固定写出来的（循环 / 施加 / 触发 / 选牌 / 丢掉 / 攻击链）。
            if (skipSelfContainedBody)
            {
                bool scBody = line.StartsWith("var __scryTop", StringComparison.Ordinal)
                    || line.StartsWith("if (__scryTop.Count", StringComparison.Ordinal)
                    || line.StartsWith("var __scryDiscard", StringComparison.Ordinal)
                    || line.StartsWith("if (__scryDiscard.Count", StringComparison.Ordinal)
                    || line.StartsWith("await CardCmd.Discard(choiceContext, __scryDiscard)", StringComparison.Ordinal)
                    // 「刮削」那一段的实现（抽 N 张 + 丢掉非 0 费的）：效果本身由 CET:Effect=ScrapeDraw 收尾
                    || line.Contains("__scraped", StringComparison.Ordinal)
                    // 「用目标卡填满手牌」那一段的实现（算还差几张 + 逐张 CreateCard + 加进手牌）
                    || line.Contains("__fillTo", StringComparison.Ordinal)
                    || line.Contains("__filled", StringComparison.Ordinal)
                    || line.StartsWith("var toUpgrade", StringComparison.Ordinal)
                    || line.EndsWith("CardCmd.Upgrade(c);", StringComparison.Ordinal)
                    || line.StartsWith("CardCmd.Upgrade(pick);", StringComparison.Ordinal)
                    // 「升级卡牌（战斗内）+ 随机」那一支的 `if (pick is null) break;`：
                    // 它是上面那句 `CardModel? pick = …` 的下一行，而那句被「选牌暂存」那条分支 continue 掉了，
                    // 于是这个开关在这里被关掉 → 紧跟着的 `CardCmd.Upgrade(pick);` 被当成**升级牌组**（全局）多回读一条。
                    // （探针实测：一张「随机升级手牌 1 张」的牌回读后多出一条 UpgradeCardGlobal。）
                    || line.StartsWith("if (pick is null) break;", StringComparison.Ordinal)
                    || line.StartsWith("PoisonPower? poison = ", StringComparison.Ordinal)
                    || line.StartsWith("if (poison is not null)", StringComparison.Ordinal)
                    || line.StartsWith("await PowerCmd.Apply<PoisonPower>(", StringComparison.Ordinal)
                    || line.StartsWith("await DamageCmd.Attack(base.DynamicVars.CalculatedDamage)", StringComparison.Ordinal)
                    || line.StartsWith(".FromCard(this, cardPlay)", StringComparison.Ordinal)
                    || line.StartsWith(".Targeting(cardPlay.Target)", StringComparison.Ordinal)
                    || line.StartsWith(".WithHitFx(", StringComparison.Ordinal)
                    || line.StartsWith(".Execute(choiceContext);", StringComparison.Ordinal)
                    // 「生成 / 变化出来的卡」的附加处理（升级 / 免费 / 仅本回合…）以及随机生成的辅助变量：
                    // 这些行都是生成器为那几种设置固定写的（__gen / __made / __tempUp / __transformPool …），
                    // 效果本身已经从池子 / CreateCard 那一行认出来了，这里整段跳过。
                    || line.Contains("__tempUp", StringComparison.Ordinal)
                    || line.StartsWith("CardCmd.Upgrade(gained);", StringComparison.Ordinal)
                    || line.StartsWith("CardCmd.Upgrade(__", StringComparison.Ordinal)
                    // 「复制卡牌」那一段的实现（选牌 / 取原牌 / 克隆 / 放进手牌）：
                    // 效果本身已经由标记行 CET:Effect=CopyCard 收尾了，这里整段跳过。
                    || line.Contains("__copyFrom", StringComparison.Ordinal)
                    || line.Contains("__src", StringComparison.Ordinal)
                    || line.Contains("__clone", StringComparison.Ordinal)
                    || line.Contains("__copyIdx", StringComparison.Ordinal)
                    || line.Contains("__copyPickIdx", StringComparison.Ordinal)
                    // 「放置卡牌」那一段的实现（自己选 / 随机 / 牌顶拿 + Add 到目标那一摞）：
                    // 效果本身已经由标记行 CET:Effect=PlaceCard 收尾了，这里整段跳过。
                    || line.Contains("__place", StringComparison.Ordinal)
                    // 数值 0 = 这张牌自己那一支：`await CardPileCmd.Add(this, PileType.X, CardPilePosition.Y);`
                    || (line.StartsWith("await CardPileCmd.Add(this, PileType.", StringComparison.Ordinal)
                        && line.Contains("CardPilePosition.", StringComparison.Ordinal))
                    // 「卡牌奖励」（非战斗胜利后那种）的实现行：识别行是 CreateForReward 那一行
                    // （所以 __rewardCards / __rewardOptions 那两行**不能**放进这个跳过名单）。
                    || line.Contains("__pickedReward", StringComparison.Ordinal)
                    || line.Contains("__granted", StringComparison.Ordinal)
                    // 「重放卡牌」/「回合结束时自动打出」的实现行（识别行是各自的标记行）
                    || line.Contains("__replayFrom", StringComparison.Ordinal)
                    || line.Contains("__replayCard", StringComparison.Ordinal)
                    // 重放：候选池 / 取几张 / 整批预览（生成时为了「池子空了不弹空选牌界面」加的那几行）
                    || line.Contains("__replayPool", StringComparison.Ordinal)
                    || line.Contains("__replayWant", StringComparison.Ordinal)
                    || line.Contains("__replayPicked", StringComparison.Ordinal)
                    || line.Contains("__tePicks", StringComparison.Ordinal)
                    // 负数 = 反向：扣层数（PowerCmd.ModifyAmount + __negPower）与失去格挡（LoseBlock）
                    || line.Contains("__negPower", StringComparison.Ordinal)
                    || line.Contains("__dblPower", StringComparison.Ordinal)
                    || line.Contains("__expTarget", StringComparison.Ordinal)
                    || line.Contains("__clrPower", StringComparison.Ordinal)
                    || line.Contains("__clrP", StringComparison.Ordinal)
                    || line.Contains("CreatureCmd.LoseBlock(", StringComparison.Ordinal)
                    || line.Contains("ForgeTurnEndPlayPower", StringComparison.Ordinal)
                    // 「丢弃卡牌（数值 0 = 这张牌自己）」：整个实现就是一句 CardCmd.Discard(choiceContext, this)
                    || (selfContainedKind == "DiscardCard" && line.Contains("CardCmd.Discard(choiceContext, this)", StringComparison.Ordinal))
                    // 「下回合生效」的生成类：实现是「挂一个 ForgeDelayedXxx 延迟状态」那一句（效果本身已经由
                    // CET:Effect= 标记行收尾），注释行也一起跳过。
                    // ⚠ V0.2.133：**通用**延迟分支也是同一句 `PowerCmd.Apply<<角色>ForgeDelayed<种类>…>`，
                    //   所以这里不能只看「注释行」—— 认不出来就会把那条效果**再回读一遍**成 ApplyPower
                    //   （探针实测：GainPotion 的「随机」被覆盖成「指定」、ApplyPower 的作用对象丢成「自己」）。
                    || (line.Contains("PowerCmd.Apply<", StringComparison.Ordinal)
                        && line.Contains("ForgeDelayed", StringComparison.Ordinal))
                    || line.StartsWith("// 下回合生效：", StringComparison.Ordinal)
                    // 「禁止获得格挡」的正数分支：只有一句 PowerCmd.Apply<NoBlockPower>，没有 __negPower 那种记号
                    //（不按标记里的种类认下来，它会被再当成一条「施加状态」—— 探针实测多回读出一条）
                    || (selfContainedKind == "ForbidBlock" && line.Contains("NoBlockPower", StringComparison.Ordinal))
                    // 「抽到就减费」：整个实现就是一句 PowerCmd.Apply<角色>ForgeDrawCostPower（或负数的扣层数）
                    || (selfContainedKind == "CheapenDraw" && line.Contains("ForgeDrawCostPower", StringComparison.Ordinal))
                    // 「禁止生命回复」：同样是一句 PowerCmd.Apply<角色>ForgeNoHealPower
                    || (selfContainedKind == "ForbidHeal" && line.Contains("ForgeNoHealPower", StringComparison.Ordinal))
                    // 「获得药水」（需求 ⑨）：标记行已经把效果收尾了，后面这几行是它的实现 ——
                    // 固定档是一句 TryToProcure<T>（瓶数 > 1 时外面还有一层 for），
                    // 随机档是 PotionFactory 那三行 + TryToProcure。
                    || (selfContainedKind == "GainPotion"
                        && (line.Contains("__potionIdx", StringComparison.Ordinal)
                            || line.Contains("__randPotion", StringComparison.Ordinal)
                            || line.Contains("PotionCmd.TryToProcure", StringComparison.Ordinal)
                            || line.Contains("PotionFactory.CreateRandomPotion", StringComparison.Ordinal)));
                if (scBody) continue;
                skipSelfContainedBody = false;
            }

            // 「生成 / 变化卡牌」的按范围随机 + 附加处理生成的**辅助语句**：这些行不是任何一条效果本身
            // （效果在 `CardModel __gen = ` / `GetDistinctForCombat` / `CardCmd.Transform` 那几行认），
            // 所以无条件跳过 —— 不能等「标记跳过」那个开关：它们可能在识别行**之前**就出现了。
            // 注意：**不能**笼统地跳过「含 __made 的行」——`await CardCmd.Transform(c, __made);` 正是识别行；
            // 只有「声明它」和「升级它」两行要跳。
            if (line.StartsWith("CardModel __made = ", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(__made)", StringComparison.Ordinal)
                || line.Contains("__newCard", StringComparison.Ordinal)
                || line.Contains("__newDeck", StringComparison.Ordinal)
                || line.Contains("__transformPool", StringComparison.Ordinal)
                || line.Contains("__addPool", StringComparison.Ordinal)
                || line.StartsWith("CardModel? __pickCard = ", StringComparison.Ordinal)
                || line.StartsWith("var __tempUp", StringComparison.Ordinal)
                || line.Contains("__tempUp.Track(", StringComparison.Ordinal)
                // 需求 ⑦：「生成 / 变化出来的卡带上关键词」生成的两种语句 ——
                // 效果本身已经从标记行 / CreateCard / Transform 那几行认出来了，这里不能把它们再当成一条「给予关键词」。
                // （`__tr.cardAdded?` 是「本体随机变化」那一支接住本体返回的新卡用的，`__tr?.cardAdded?` 是「填了目标卡」那一支）
                || line.Contains(".AddKeyword(CardKeyword.", StringComparison.Ordinal)
                || line.Contains(".AddGivenCustomKeyword(", StringComparison.Ordinal)
                // 斩杀条件那两行辅助语句（效果本身在 DamageCmd.Attack 那一行认）
                || line.StartsWith("__fatalOk = ", StringComparison.Ordinal)
                || line.StartsWith("__lastAttack = null;", StringComparison.Ordinal)
                || line.StartsWith("bool __fatalOk = false;", StringComparison.Ordinal)
                || line.StartsWith("MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? __lastAttack", StringComparison.Ordinal)
                || line.Contains("AddGeneratedCardToCombat(__gen", StringComparison.Ordinal)
                || line.Contains("__gen.SetToFree", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(__gen)", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(gained);", StringComparison.Ordinal)
                || line.Contains("else CardCmd.Upgrade(__gen)", StringComparison.Ordinal)
                // 「多选1」生成的辅助行：候选列表那行**是**识别行（下面那条 GetDistinctForCombat），
                // 所以这里只跳「判断候选数 / 弹选牌界面 / 拿到选中的那张」这几种固定写法。
                || line.StartsWith("if (__candidates.Count", StringComparison.Ordinal)
                // 「从范围里自己挑一张」那一段（变量名用 __chosenCand，和「多选1」那套 __candidates 分开，
                // 免得这条跳过规则影响「多选1」的回读）
                || line.Contains("__chosenCand", StringComparison.Ordinal)
                || line.StartsWith("CardModel? __picked = ", StringComparison.Ordinal)
                || line.StartsWith("if (__picked is not null)", StringComparison.Ordinal)
                || line.Contains("__picked", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(__", StringComparison.Ordinal))
                continue;

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
                // 是哪一只：调用里的 `<X>Cmd.Summon` 那个 X 就是宠物类名（= 稳定标识）
                e.PetSummon = Match(line, @"(\w+)Cmd\.Summon\(choiceContext");
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
                // 「全部召唤物」的召唤：组号可能写在**上一行的注释**里（卡牌：单独一行 // CET:PetAll=…）
                // 也可能挂在**这一行末尾**（遗物：和宠物指令同一行的注释）。
                string? sumGrp = Match(line, @"CET:PetAll=(\w+)") ?? pendingPetAllGroup;
                pendingPetAllGroup = null;
                if (sumGrp is not null)
                {
                    if (!allPetGroups.Add(sumGrp)) continue;
                    e.PetSummon = PetGen.AllId;
                }
                into.Add(e);   // 没有「效果级条件」（宠物守卫不是条件）
                continue;
            }

            // 攻击链：多行。
            // 带「斩杀」条件的卡把结果记进 __lastAttack（`__lastAttack = await DamageCmd.Attack(…)`），
            // 所以两种前缀都要认。
            if (line.StartsWith("await DamageCmd.Attack(", StringComparison.Ordinal)
                || line.StartsWith("__lastAttack = await DamageCmd.Attack(", StringComparison.Ordinal))
            {
                // 斩杀那条判据（__fatalOk = …）不是效果，跳过
                var chain = new StringBuilder(line);
                while (!line.EndsWith(";") && i < lines.Count)
                {
                    line = lines[i].Trim();
                    i++;
                    chain.Append(' ').Append(line);
                }
                string ch = chain.ToString();
                // 「按生命值算」的伙伴攻击（我们新加的那三种）：生成时在这条效果的第一行写了标记，
                // 而且伤害取自固定名字的 base.DynamicVars.CalculatedDamage —— 必须**先**分流，
                // 否则下面会把它当成普通 PetAttack（数值取不到，配置静默丢一半）。
                if (pendingPetMarker is not null
                    && (ch.Contains("CalculatedDamage") || Match(ch, @"\(decimal\)\(?__\w") is not null))
                {
                    string? petVar0 = Match(ch, @"\.FromPetAttacker\((\w+)\)");
                    var calc = PetEffectFromMarker(petVar0, out string? why0);
                    if (calc is null) { result.Unparsed.Add($"{where}: {line} {why0}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                    if (ch.Contains(".TargetingAllOpponents")) calc.TargetSide = "AllEnemies";
                    else if (ch.Contains(".TargetingRandomOpponents"))
                    {
                        calc.TargetSide = "RandomEnemies";
                        calc.AllowDuplicates = ch.Contains("allowDuplicates: true");
                    }
                    // 数值 = 升级增量：走计算变量时从 CalculationBase 捞（基础值恒为 0，真正的增量由
                    // OnUpgrade 那段认回来）；「全部召唤物」是内联写法
                    // （(decimal)__pet.MaxHp + (base.IsUpgraded ? N : 0m)），没有 CalculationBase 可捞，
                    // 所以直接把这个 N 读回 **UpgradeAmount**（和走计算变量时落在同一个字段上，否则
                    // 「生成 → 回读 → 再生成」会把升级增量丢掉）。
                    calc.Amount = CalcAmount("CalculationBase");
                    if (calc.Amount == 0m)
                        calc.UpgradeAmount = Dec(ch, @"base\.IsUpgraded \? (-?[\d.]+)m? : 0m", 0);
                    string hits0 = Match(ch, @"\.WithHitCount\(([^)]*)\)") ?? "1";
                    if (calc.TargetSide == "RandomEnemies")
                    {
                        if (hits0 == "x") calc.RepeatIsX = true;
                        else if (hits0 == "hits") calc.RepeatCount = hitsLiteral;
                        else calc.RepeatCount = Math.Max(1, (int)Dec(hits0, @"([\d.]+)", 1));
                    }
                    else
                    {
                        if (hits0 == "x") calc.RepeatIsX = true;
                        else calc.RepeatCount = Math.Max(1, (int)Dec(hits0, @"([\d.]+)", 1));
                    }
                    ApplyLoop(calc, frames);
                    Done(calc);
                    continue;
                }
                // 牺牲伙伴（收益是**伤害**）：先 decimal dmg = …（上面已跳过）、Kill，然后 DamageCmd.Attack(dmg)
                if (pendingPetMarker is not null && ch.Contains("DamageCmd.Attack(dmg)"))
                {
                    string killed = KillPetVar() ?? sacKillPet ?? "";
                    sacKillPet = null;
                    var sac = PetEffectFromMarker(killed, out string? why1);
                    if (sac is null) { result.Unparsed.Add($"{where}: {line} {why1}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                    if (sac.PetSacrificeFormula == "Fixed")
                        FillAmount(sac, NextVar(vars, ref varIdx, "Damage"), nameToPowerId);
                    else sac.Amount = CalcAmount("CalculationBase");
                    sac.TargetSide = ch.Contains(".TargetingAllOpponents") ? "AllEnemies" : "Enemy";
                    ApplyLoop(sac, frames);
                    Done(sac);
                    continue;
                }
                // 「伙伴攻击」也是 DamageCmd.Attack 链，区别只在攻击者被换成了宠物。
                // 现在是 `.FromPetAttacker(__<宠物类名>)`（老工程里是 .FromMonster(pet.Monster)）——
                // 两种都认，目标解析和下面普通伤害完全一样，所以这里分一次流就行。
                bool fromMonster = ch.Contains(".FromPetAttacker(") || ch.Contains(".FromMonster(");
                var e = new EffectSpec { Kind = fromMonster ? "PetAttack" : "Damage", TargetSide = "Enemy" };
                // 是哪一只：`.FromPetAttacker(__uiCheckPet)` 里的局部变量名反推回宠物类名
                string? petVar = Match(ch, @"\.FromPetAttacker\((\w+)\)");
                if (petVar is not null) e.PetSummon = PetClassOfVar(petVar, petClassNames);
                // 老工程（上一版）写的是 .FromMonster(pet.Monster)：认得出是「伙伴攻击」，但反推不出是哪一只
                // （那时生成器只支持一只召唤物）→ 留一条笔记，免得用户以为「哪一只」被静默丢了。
                else if (ch.Contains(".FromMonster(") && petClassNames.Count > 0)
                    result.Notes.Add($"{where}：这条「伙伴攻击」是老版本生成的（.FromMonster），"
                        + $"恢复不出是哪一只召唤物 —— 生成时会自动用第一只「{petClassNames[0]}」，"
                        + "需要的话到效果里重新选一次。");
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
                // 牺牲伙伴（收益是**格挡**）：Kill 已经在上一行出现过 → 这一句才是那条效果的收尾
                // （固定收益时数值是普通 BlockVar，按生命值算时是计算三件套）。
                if (sacKillPet is not null)
                {
                    string? killed = sacKillPet;
                    sacKillPet = null;
                    var sacBlock = PetEffectFromMarker(killed, out string? whySacBlock);
                    if (sacBlock is null) { result.Unparsed.Add($"{where}: {line} {whySacBlock}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                    if (sacBlock.PetSacrificeFormula == "Fixed")
                        FillAmount(sacBlock, NextVar(vars, ref varIdx, "Block"), nameToPowerId);
                    else sacBlock.Amount = CalcAmount("CalculationBase");
                    ApplyLoop(sacBlock, frames);
                    Done(sacBlock);
                    continue;
                }
                var e = new EffectSpec { Kind = "Block", TargetSide = SideOfTarget(line) };
                // 卡牌 / 遗物都声明了 Block 变量，能按变量拿；**自定义状态的触发器没有 CanonicalVars**
                // （数值是生成期的字面量），所以这里必须留下「按第 2 个参数的字面量兜底」这条路，
                // 否则从工程反推存档时「获得 3 点格挡」会静默变成 0（探针实测踩到）。
                FillAmount(e, NextVar(vars, ref varIdx, "Block"), nameToPowerId, fallbackExpr: ArgAt(line, 1));
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

            // 负数「获得能量」= 扣除（生成的是 LoseEnergy(System.Math.Abs(…))）：回读成负数
            if (line.StartsWith("await PlayerCmd.LoseEnergy(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Energy" };
                FillAmount(e, NextVar(vars, ref varIdx, "Energy"), nameToPowerId, fallbackExpr: AbsInner(ArgAt(line, 0)));
                e.Amount = -Math.Abs(e.Amount);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await PlayerCmd.GainGold(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Gold" };
                // 数值走 GoldVar（生成时声明的是 new GoldVar(...)，取的是索引器）——
                // 直接 FillExpr(ArgAt(line,0)) 认不出 base.DynamicVars["Gold"].BaseValue，会静默变 0。
                FillAmount(e, NextVar(vars, ref varIdx, "Gold"), nameToPowerId, fallbackExpr: ArgAt(line, 0));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 负数「获得金币」= 扣除（生成的是 LoseGold(System.Math.Abs(…))）：回读成负数
            if (line.StartsWith("await PlayerCmd.LoseGold(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Gold" };
                FillAmount(e, NextVar(vars, ref varIdx, "Gold"), nameToPowerId, fallbackExpr: AbsInner(ArgAt(line, 0)));
                e.Amount = -Math.Abs(e.Amount);
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

            // ===== 新增的那批宠物效果（生成时带 // CET:PetEffect= 标记，按标记还原）=====
            // 治疗伙伴 / 伙伴最大生命：目标参数是宠物局部变量（__xxx）→ 和普通「回复生命 / 最大生命」区分开。
            if (line.StartsWith("await CreatureCmd.Heal(", StringComparison.Ordinal)
                && PetClassOfVar(ArgAt(line, 0), petClassNames) is not null)
            {
                var e = PetEffectFromMarker(ArgAt(line, 0), out string? whyHeal);
                if (e is null) { result.Unparsed.Add($"{where}: {line} {whyHeal}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                // 数值在 CanonicalVars 里（HealVar）→ 按**种类**从 vars 里取（取出即删，
                // 免得被后面别的效果按顺序捡走）。注意不能去 calcVars 里找：那里只有计算三件套。
                e.Amount = TakeCalcVar(vars, "Heal") ?? 0m;
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.GainMaxHp(", StringComparison.Ordinal)
                && PetClassOfVar(ArgAt(line, 0), petClassNames) is not null)
            {
                var e = PetEffectFromMarker(ArgAt(line, 0), out string? whyMax);
                if (e is null) { result.Unparsed.Add($"{where}: {line} {whyMax}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                e.Amount = TakeCalcVar(vars, "MaxHp") ?? 0m;
                Done(e);
                continue;
            }

            // 伙伴失去生命：`CreatureCmd.Damage(choiceContext, __pet, 值, ValueProp…, 来源)`
            //
            // 两个坑：
            //   ① 生成的语句是**跨两行**的（值 + ValueProp 组合 + dealer/cardPlay 各占一行），
            //      所以不能靠 ArgAt 取参数 —— 那个要求本行有闭合的右括号，取不到就整行认不出来；
            //   ② 老工程里 `CreatureCmd.Damage(choiceContext, __pet, …)` 也可能是别的意思，
            //      所以只认**带 PetLoseHp 标记**的那种（老工程没有标记，行为与以前完全一致）。
            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, __", StringComparison.Ordinal)
                && pendingPetMarker is not null
                && pendingPetMarker.Contains("CET:PetEffect=PetLoseHp", StringComparison.Ordinal))
            {
                string petArg = Match(line, @"await CreatureCmd\.Damage\(choiceContext, (\w+),") ?? "";
                var e = PetEffectFromMarker(petArg, out string? whyLoss);
                if (e is null) { result.Unparsed.Add($"{where}: {line} {whyLoss}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                e.Amount = TakeCalcVar(vars, "HpLoss") ?? 0m;
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 牺牲伙伴的 `await CreatureCmd.Kill(__pet);`：只记住是哪一只宠物，等收益动作行收尾
            //（收益是伤害时动作在 Kill **之后**、是格挡时也在之后；收益那两句由上面的 GainBlock / Attack 分支处理）。
            if (line.Contains("CreatureCmd.Kill(__"))
            {
                sacKillPet = Match(line, @"CreatureCmd\.Kill\((\w+)\)");
                continue;
            }

            // 替主人承伤（关）：`await PowerCmd.Remove<ForgePetGuardianPower>(__pet);`
            if (line.Contains($"PowerCmd.Remove<") && line.Contains(PetGen.GuardianPowerSuffix)
                && line.Contains(">("))
            {
                var e = PetEffectFromMarker(Match(line, @"PowerCmd\.Remove<\w+>\((\w+)\)"), out string? whyOff);
                if (e is null) { result.Unparsed.Add($"{where}: {line} {whyOff}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.Heal(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Heal", TargetSide = SideOfTarget(line) };
                // 生成的是 base.DynamicVars["Heal"].BaseValue（HealVar 没有同名属性 → 索引器写法）
                FillAmountOrExpr(e, ArgAt(line, 1), NextVar(vars, ref varIdx, "Heal"), nameToPowerId, "Heal");
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.GainMaxHp(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "MaxHp", TargetSide = SideOfTarget(line) };
                FillAmountOrExpr(e, ArgAt(line, 1), NextVar(vars, ref varIdx, "MaxHp"), nameToPowerId, "MaxHp");
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.LoseMaxHp(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "MaxHp", TargetSide = SideOfTarget(line) };
                FillExpr(e, ArgAt(line, 2));
                e.Amount = -e.Amount;
                Done(e);
                continue;
            }

            // 「对全体敌人造成伤害」（遗物 / 状态）：生成的是
            //   await CreatureCmd.Damage(choiceContext, base.Owner.Creature.CombatState.HittableEnemies, …);
            // ⚠ 必须排在下面那条「自己吃伤害」**前面** —— 两条的前缀都是 `Damage(choiceContext, base.Owner`，
            //   而下面那条只看「下一个变量是不是 DamageVar」来分流；遗物是**多条触发时机各自的效果**
            //   共用同一份 CanonicalVars 顺序，`varIdx` 在前面的时机里已经被别的变量推走了，
            //   于是「对全体敌人造成 7 点伤害」会被当成「失去生命」回读成 0 点 + 目标自己
            //   （实测：回读再生成就真的变成扣自己血）。按**目标实参**直接分流最稳。
            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, base.Owner.Creature.CombatState.HittableEnemies", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Damage", TargetSide = "AllEnemies" };
                FillAmountOrExpr(e, ArgAt(line, 2), NextVar(vars, ref varIdx, "Damage"), nameToPowerId, "Damage");
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, base.Owner", StringComparison.Ordinal))
            {
                // 「自己吃伤害」这一句是**三种效果共用**的生成代码：
                //   · 失去生命 HpLoss      → base.DynamicVars["HpLoss"]
                //   · 造成伤害 + 作用对象=自己 → base.DynamicVars.Damage（就是这一条，本体的伤害链走不了自己）
                //   · 回复生命填负数        → base.DynamicVars["Heal"]（负数 = 扣血）
                // 怎么区分：看**这条效果声明的那个变量**（CanonicalVars 的顺序 = 效果顺序）。
                // 以前一律当成「失去生命」→ 「造成伤害（自己）」的卡回读后变成失去生命，
                // 而且它的 DamageVar 没人认领 → 还会多报一句「升级增量找不到对应效果」（用户 SparkleMod 的打击踩到了）。
                var peek = PeekVar(vars, varIdx);
                if (peek?.Kind == "Damage")
                {
                    var dmg = new EffectSpec { Kind = "Damage", TargetSide = "Self" };
                    FillAmountOrExpr(dmg, ArgAt(line, 2), NextVar(vars, ref varIdx, "Damage"), nameToPowerId, "Damage");
                    ApplyLoop(dmg, frames);
                    Done(dmg);
                    continue;
                }
                var e = new EffectSpec { Kind = "HpLoss", TargetSide = SideOfTarget(line) };
                // 生成的是 base.DynamicVars["HpLoss"].BaseValue（同样没有同名属性，走索引器）
                FillAmountOrExpr(e, ArgAt(line, 2), NextVar(vars, ref varIdx, "HpLoss"), nameToPowerId, "HpLoss");
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 击晕：生成的是 `await CreatureCmd.Stun(目标);`（本体卡「口哨」那句 API）。
            // 目标从参数认：base.Owner.Creature = 自己；cardPlay.Target = 指定敌人；
            // foe 在 for 循环里 = 随机 N 个、在 foreach 里 = 全体、在 if (foe is not null) 里 = 指定敌人（遗物那种）。
            if (line.StartsWith("await CreatureCmd.Stun(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "Stun" };
                if (line.Contains("base.Owner.Creature", StringComparison.Ordinal)) e.TargetSide = "Self";
                else if (line.Contains("cardPlay.Target", StringComparison.Ordinal)) e.TargetSide = "Enemy";
                else if (frames.Any(f => f.IsLoop)) e.TargetSide = randomFoes ? "RandomEnemies" : "AllEnemies";
                else e.TargetSide = "Enemy";
                if (e.TargetSide == "RandomEnemies")
                {
                    e.AllowDuplicates = !foesRemoved;
                    ApplyLoop(e, frames, randomHits: true);
                }
                else ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.StartsWith("CreatureCmd.LoseBlock(", StringComparison.Ordinal) || line.StartsWith("await CreatureCmd.LoseBlock(", StringComparison.Ordinal))
            {
                // 负数格挡 = 失去格挡（生成的是 LoseBlock(choiceContext, 目标, System.Math.Abs(数值), remover)）
                var e = new EffectSpec { Kind = "Block", TargetSide = SideOfTarget(line) };
                // 同上面那条：自定义状态的触发器没有 Block 变量，按第 3 个参数（值）兜底
                FillAmount(e, NextVar(vars, ref varIdx, "Block"), nameToPowerId, fallbackExpr: AbsInner(ArgAt(line, 2)));
                if (e.Amount > 0) e.Amount = -e.Amount;        // 回读成负数（生成时是 Math.Abs）
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            if (line.Contains("PowerCmd.ModifyAmount(choiceContext, __negPower,"))
            {
                // 负数「施加状态」= 扣除层数（生成的是先 GetPower<T>() 再 ModifyAmount(-(Math.Abs(数值))））
                var mNeg = Regex.Match(line, @"GetPower<(\w+)>\(\)");
                var mPower = mNeg.Success ? mNeg : Regex.Match(line, @"__negPower, -\(System\.Math\.Abs\(base\.DynamicVars\[""(\w+)""\]");
                if (!mPower.Success) { result.Unparsed.Add($"{where}: {line}"); continue; }
                string power = mNeg.Success ? mPower.Groups[1].Value : mPower.Groups[1].Value;
                var eNeg = new EffectSpec
                {
                    Kind = "ApplyPower",
                    PowerId = nameToPowerId.TryGetValue(power, out var pid) ? pid : power,
                };
                // 目标：GetPower<T>() 是从谁身上取的（自己 / 选中的敌人 / 全体里的 foe）
                eNeg.TargetSide = line.Contains(".GetPower<" + power + ">()") && line.Contains("cardPlay.Target.GetPower<")
                    ? "TargetEnemy"
                    : SideOfTarget(line);
                // 数值按名字去 CanonicalVars 里取（生成时写的是 new PowerVar<X>(-3m)，
                // 变量名/泛型都是这个状态类名 —— 和宠物那条一样按名字找，别靠顺序）
                int atNeg = vars.FindIndex(v => v.Kind == "Power"
                    && (string.Equals(v.Name, power, StringComparison.Ordinal)
                        || string.Equals(v.PowerId, power, StringComparison.Ordinal)));
                if (atNeg >= 0) { eNeg.Amount = -Math.Abs(vars[atNeg].Amount); vars.RemoveAt(atNeg); }
                else
                {
                    // 找不到变量就别硬猜：列出来让用户人工补（静默回读成 0 会把配置吃掉）
                    result.Unparsed.Add($"{where}: 负数「施加状态」的层数没认出来（{power}）");
                    continue;
                }
                ApplyLoop(eNeg, frames);
                Done(eNeg);
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

                // 牺牲伙伴（收益是**获得增益 / 减益**）：Kill 已经在上一行出现过 → 这一句才是那条效果的收尾
                //（生成顺序是「先算层数 → CreatureCmd.Kill → PowerCmd.Apply」，所以状态的目标恒为 base.Owner.Creature）。
                // 不认下来的话，这一句会被当成一条普通的「施加增益 / 减益」→ 多回读一条、层数还对不上。
                if (sacKillPet is not null && string.Equals(target, "base.Owner.Creature", StringComparison.Ordinal))
                {
                    string killed = sacKillPet;
                    sacKillPet = null;
                    var sacPower = PetEffectFromMarker(killed, out string? whySacPower);
                    if (sacPower is null) { result.Unparsed.Add($"{where}: {line} {whySacPower}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                    // 固定值那份声明的是普通 BlockVar（收益是状态时也走格挡那一套变量）；
                    // 按生命值算的那份一样是计算三件套 → 升级增量落在 CalculationBase 上。
                    if (sacPower.PetSacrificeFormula == "Fixed")
                        FillAmount(sacPower, NextVar(vars, ref varIdx, "Block"), nameToPowerId);
                    else sacPower.Amount = CalcAmount("CalculationBase");
                    ApplyLoop(sacPower, frames);
                    Done(sacPower);
                    continue;
                }

                // ===== 新增的那批宠物效果：给伙伴施加状态 / 替主人承伤（开）=====
                // 目标参数是宠物局部变量（__xxx）而不是 base.Owner.Creature / cardPlay.Target / foe。
                if (PetClassOfVar(target, petClassNames) is not null)
                {
                    var pe = PetEffectFromMarker(target, out string? whyApply);
                    if (pe is null) { result.Unparsed.Add($"{where}: {line} {whyApply}"); pendingPetMarker = null; pendingPetAllGroup = null; continue; }
                    // 给伙伴施加状态：数值按**名字**去 CanonicalVars 里取（生成时写的就是
                    // base.DynamicVars["PetPower<PowerId>"].BaseValue，升级增量也按这个名字对回去）
                    if (pe.Kind == "PetApplyPower")
                    {
                        string? vn = Match(line, @"base\.DynamicVars\[""(\w+)""\]");
                        // 生成时写的是 new PowerVar<StrengthPower>("PetPowerStrengthPower", 2m)：
                        // 索引器里的键是**变量名**（不是泛型参数 T），所以两个都按一下。
                        int at = vn is null ? -1 : vars.FindIndex(v => v.Kind == "Power"
                            && (string.Equals(v.Name, vn, StringComparison.Ordinal)
                                || string.Equals(v.PowerId, vn, StringComparison.Ordinal)));
                        if (at >= 0) { pe.Amount = vars[at].Amount; vars.RemoveAt(at); }
                    }
                    ApplyLoop(pe, frames);
                    Done(pe);
                    continue;
                }

                var e = new EffectSpec { Kind = "ApplyPower", PowerId = power };
                if (power.EndsWith("ForgeExtraTurnPower", StringComparison.Ordinal))
                {
                    e.Kind = "ExtraTurn";
                    e.PowerId = null;
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                // 负数「获得能量」+「下回合生效」：打的是我们生成的「下回合少 N 点能量」负债 Power
                // （本体的 EnergyNextTurnPower 走 GainEnergy，负数会被忽略）→ 认回「获得能量 -N + 下回合」。
                if (power.EndsWith("ForgeEnergyNextTurnDebtPower", StringComparison.Ordinal))
                {
                    e.Kind = "Energy";
                    e.PowerId = null;
                    e.NextTurn = true;
                    FillAmount(e, NextVar(vars, ref varIdx, "Energy"), nameToPowerId,
                        fallbackExpr: AbsInner(parts.Count > 0 ? parts[0].Trim() : ""));
                    e.Amount = -Math.Abs(e.Amount);
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                // 透支能量：生成的是我们自己的负债 Power（<角色>ForgeEnergyDebtPower）—— 认回「透支能量」这一条。
                // 数值走的是本体 EnergyVar（键 = Energy，不是这个 Power 的名字），所以这里按 "Energy" 去取。
                if (power.EndsWith("ForgeEnergyDebtPower", StringComparison.Ordinal))
                {
                    e.Kind = "OverdraftEnergy";
                    e.PowerId = null;
                    FillAmount(e, NextVar(vars, ref varIdx, "Energy"), nameToPowerId);
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                // 强化指定卡牌：打的是我们生成的强化 Power（<角色>ForgeBoost<目标卡><Damage|Block>Power）。
                // 目标卡 / 强化什么按标记还原（标记丢了就从类名里抠），数值走 CanonicalVars 的 Boost 变量。
                if (power.Contains("ForgeBoost", StringComparison.Ordinal))
                {
                    e.Kind = "BoostCard";
                    e.PowerId = null;
                    string? card = pendingBoostMarker is null ? null : Match(pendingBoostMarker, @"CET:BoostCard=(\S+)");
                    if (card is null || card == "?")
                    {
                        card = BoostTargetOf(power);
                        if (card is null)
                            result.Unparsed.Add($"{where}: 强化指定卡牌的目标卡没认出来（{power}）");
                    }
                    e.SpawnCardId = card;
                    e.BoostStat = pendingBoostMarker is not null
                        && pendingBoostMarker.Contains("CET:BoostStat=Block", StringComparison.Ordinal)
                        ? "Block" : (power.EndsWith("BlockPower", StringComparison.Ordinal) ? "Block" : "Damage");
                    // 这条效果永远作用在自己身上（挂一张强化 Power），目标对象固定「自己」——
                    // 和校验器那句「作用对象固定为自己」保持一致，列表里也就不会显示成「→ 单体敌人」
                    e.TargetSide = "Self";
                    pendingBoostMarker = null;
                    FillAmount(e, NextVar(vars, ref varIdx, "Boost"), nameToPowerId);
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                if (power == "BlockNextTurnPower") { e.Kind = "Block"; e.NextTurn = true; }
                else if (power == "DrawCardsNextTurnPower" || power.Contains("ForgeDelayedDraw", StringComparison.Ordinal)) { e.Kind = "Draw"; e.NextTurn = true; }
                else if (power == "EnergyNextTurnPower" || power.Contains("ForgeDelayedEnergy", StringComparison.Ordinal)) { e.Kind = "Energy"; e.NextTurn = true; }
                // 额外资源量 + 下回合生效：本体 StarNextTurnPower（回合开始时 GainStars 再自毁）
                else if (power == "StarNextTurnPower") { e.Kind = "ExtraResource"; e.NextTurn = true; }
                // 生成类的「下回合生效」（V0.2.88）：延迟状态类名是 <角色>ForgeDelayed<Gen|TransGlobal|AddGlobal|
                // RemoveGlobal|UpGlobal><卡类名>（见 Naming.DelayedPowerSuffix）——
                // 必须**排在上面那条通用的 ForgeDelayed 之前**，否则会被当成「下回合施加状态」、
                // PowerId 里留下 "GenShiv" 这种垃圾（回读再生成会报「增益 / 减益未选择有效的 Power」）。
                // 卡/范围/张数/放进哪一摞 由紧在前面的 CET:SpawnPick= 标记行提供（pendingSpawn 会在 Done 里配上）。
                else if (DelayedSpawnStem(power, out string dKind, out string dTail))
                {
                    e.Kind = dKind;
                    e.NextTurn = true;
                    // 目标卡：类名尾巴就是那张卡的类名（标记行里没有卡名）。随机档的尾巴是 Rnd<范围>，
                    // 那种情况范围已经在 CET:SpawnFilter= 里了，这里不覆盖。
                    if (dTail.Length > 0 && !dTail.StartsWith("Rnd", StringComparison.Ordinal)) e.SpawnCardId = dTail;
                }
                // 生成类的延迟效果：张数（= 延迟状态的层数）在这句 Apply 的第三个实参上 ——
                // 有 Cards 变量的（生成卡牌）走变量，没变量的（全局那几种）是字面量。
                if (e.NextTurn && CSharpCodeGen.IsDelayedSpawnKind(e.Kind))
                {
                    if (line.Contains("base.DynamicVars.Cards.BaseValue", StringComparison.Ordinal))
                        FillAmount(e, NextVar(vars, ref varIdx, "Cards"), nameToPowerId);
                    else
                    {
                        string? lit = Match(line, @"Creature, (-?[\d.]+)m,");
                        if (lit is not null && decimal.TryParse(lit, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal nv))
                            e.Amount = nv;
                    }
                    ApplyLoop(e, frames);
                    Done(e);
                    continue;
                }
                // 「下回合生效」的**通用延迟状态**（V0.2.133）：类名是 <角色>ForgeDelayed<种类><尾巴><作用对象>
                // （见 Naming.DelayedPowerSuffix）——必须排在下面那条通用的 ForgeDelayed 之前，
                // 否则会被当成「下回合施加状态」、PowerId 里留下 "Damage" 这种垃圾。
                // 种类 / 数值 / 目标卡 / 状态 / 作用对象 由紧在前面的 `CET:Effect=` 标记行提供
                //（那个分支已经在前面把这条效果造好并跳过了整段实现，这里只是兜底认回「下回合」）。
                else if (EffectCatalog.IsDelayedEffectKindNamed(power))
                {
                    string kind = EffectCatalog.DelayedEffectKindOf(power);
                    if (kind.Length > 0 && e.Kind == "ApplyPower" && string.IsNullOrEmpty(e.PowerId))
                    {
                        e.Kind = kind;
                        e.NextTurn = true;
                        string? lit = Match(line, @"ChoiceContext, \w+, (-?[\d.]+)m,");
                        if (lit is not null && decimal.TryParse(lit, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal nv))
                            e.Amount = nv;
                    }
                }
                else if (power.Contains("ForgeDelayed", StringComparison.Ordinal))
                {
                    e.NextTurn = true;
                    // 类名 = <角色类名>ForgeDelayed<状态类名>（见 Naming.DelayedPowerSuffix）——
                    // 必须把前缀剥掉，不然 PowerId 会留在「SevenForgeDelayedWeakPower」这种**类名**上，
                    // 回读出来的存档再生成时报「增益 / 减益未选择有效的 Power」、整个存档生成不过去
                    //（「回读 → 再生成」的回归里踩到：ProfileFactory.Sample 的诅咒牌就是「下回合生效的虚弱」）。
                    int at = power.IndexOf("ForgeDelayed", StringComparison.Ordinal);
                    string real = power.Substring(at + "ForgeDelayed".Length);
                    if (real.Length > 0) e.PowerId = real;
                }
                // 临时增益：打的是我们生成的临时 Power（<角色>ForgeTemp<状态>）——
                // 从类名里把真正的状态名抠回来（前缀 ForgeTemp，后缀 Power），从而认回「临时增益」这一条。
                else if (power.Contains("ForgeTemp", StringComparison.Ordinal))
                {
                    string? real = TempPowerTargetOf(power, out bool tempNextTurn, out bool tempNegative);
                    if (real is not null)
                    {
                        e.Kind = "TempPower";
                        e.PowerId = real;
                        // 类名带了 NextTurn 尾巴 = 「下回合开始时才撤」那一档（生成时就是这么区分的）
                        if (tempNextTurn) e.TempPowerReverse = TempPowerReverses.NextTurnStart;
                        // 类名带了 Abs 尾巴 = 负数档（临时减益：先扣层、到点加回来）。
                        // ⚠ 回读时**必须把数值填成负数**，否则再生成一遍就会变成「加层」——
                        // 数值本身没写进工程（PowerVar 的初始值在别处、这里只想认回语义），
                        // 所以按类名给一个 -1 打底、真值由下面那段从 CanonicalVars 里读。
                        if (tempNegative) e.Amount = -Math.Abs(e.Amount == 0m ? 1m : e.Amount);
                    }
                    else
                        result.Unparsed.Add($"{where}: 临时增益的状态没认出来（{power}）");
                }

                if (e.Kind is "ApplyPower" or "TempPower" or "Damage")
                {
                    // 目标：cardPlay.Target = 单体敌人；HittableEnemies 整串 = 全体；
                    //      foe 出现在「随机挑敌人」的循环里 = 随机敌人，出现在 foreach 里 = 全体
                    // ⚠ Damage 也要一起判：遗物的伤害生成的是
                    //   `CreatureCmd.Damage(choiceContext, base.Owner.Creature.CombatState.HittableEnemies, …)` ——
                    //   不判的话「对全体敌人造成伤害」回读成默认的「失去生命」，
                    //   再生成一遍就真的变成了扣自己血（实测踩到的回读丢配置）。
                    if (target.Contains("cardPlay.Target")) e.TargetSide = "Enemy";
                    else if (target.Contains("HittableEnemies")) e.TargetSide = "AllEnemies";
                    else if (target == "foe")
                    {
                        // foe 有三种来源，靠**在不在循环里**分：
                        //   · 没有循环 → 遗物那条 `Creature? foe = …FirstOrDefault();` = 指定敌人（一个）
                        //   · foreach 循环 → 全体；for 循环 + List<Creature> foes → 随机 N 个
                        // （以前不看循环，遗物的「指定敌人」回读成「全体」，再生成就真变成全体了）
                        bool inLoop = frames.Any(f => f.IsLoop);
                        e.TargetSide = !inLoop ? "Enemy"
                            : (randomFoes ? "RandomEnemies" : "AllEnemies");
                    }
                    else if (e.Kind == "Damage")
                    {
                        // 伤害那条链只有「指定敌人 / 全体敌人 / 随机敌人」三种目标；认不出来时别乱改
                        // （其余情况保持解析器给的默认值）
                    }
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
                    // 额外资源量：CanonicalVars 里声明的是 StarsVar（键 Stars；同名多条会起别名 Stars2，NextVar 自己往后找）
                    "ExtraResource" => "Stars",
                    // 临时增益：CanonicalVars 里声明的是**真正那个状态**的 PowerVar（不是临时 Power 的），
                    // 所以按 e.PowerId 找；别的（含延迟）用生成代码里那个 Power 名找。
                    "TempPower" => "Power:" + (e.PowerId ?? power),
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

            // 「生成卡牌」带附加处理（升级 / 免费 / 仅本回合…）时的写法：
            //   指定卡：CardModel __gen = …CombatState.CreateCard(ModelDb.Card<X>(), base.Owner);
            //   按范围：foreach (CardModel __gen in CardFactory.GetDistinctForCombat(base.Owner, __genPool, N, …))
            // 两种都是「效果已经在这行认出来了」，后面那些 __gen / __tempUp 的处理行交给
            // skipSelfContainedBody 整段跳过；「放进哪一摞」从后面那句 AddGeneratedCardToCombat 里 peek 出来。
            if (line.StartsWith("CardModel __gen = ", StringComparison.Ordinal))
            {
                var e = new EffectSpec
                {
                    Kind = "GenerateCard",
                    SpawnCardId = Match(line, @"ModelDb\.Card<(\w+)>"),
                    SpawnTo = PeekSpawnToPile(lines, i),
                };
                // 生成张数 = 里面那层 for (__genIdx) 的上限（外层 for(i) 才是「生效次数」）
                e.Amount = LoopValue(LoopTop() ?? "1");
                e.Times = OuterRepeatTimes(frames);
                Done(e);
                skipSelfContainedBody = true;
                continue;
            }
            if (line.Contains("CardFactory.GetDistinctForCombat(", StringComparison.Ordinal))
            {
                var e = new EffectSpec
                {
                    Kind = "GenerateCard",
                    SpawnTo = PeekSpawnToPile(lines, i),
                };
                string? cnt = Match(line, @"GetDistinctForCombat\(base\.Owner, __genPool, (\w+),");
                // 多选1（标记里写了 CET:SpawnChoice=M，M > 1）：这一行里的数字是**候选张数**，
                // 真正生成出来只有 1 张 —— 所以「数值」要写回 1，候选张数由标记里的 SpawnChoice 带着。
                bool isChoice = pendingSpawn?.SpawnChoice > 1m;
                if (isChoice) e.Amount = 1;
                else if (cnt == "x") e.AmountIsX = true;
                else if (cnt is not null) e.Amount = decimal.Parse(cnt, CultureInfo.InvariantCulture);
                e.Times = OuterRepeatTimes(frames);
                Done(e);
                skipSelfContainedBody = true;
                continue;
            }
            if (line.StartsWith("var __genPool = ", StringComparison.Ordinal)) continue;   // 上面那条的池子

            // 变化卡牌：填了目标卡 → CardCmd.Transform(原卡, 新卡)；留空 → CardCmd.TransformToRandom。
            // **两种都要认**（以前只认 Transform，留空那种会被记成「没认出来」）。
            // 需求 ⑦：选了「变化出来的卡带上关键词」时，生成的是 `var __tr = await CardCmd.Transform(…)`
            // （要接住本体返回的新卡），所以这里用 Contains 而不是 StartsWith —— 两种写法都要认。
            if (line.Contains("await CardCmd.Transform(", StringComparison.Ordinal)
                || line.Contains("await CardCmd.TransformToRandom(", StringComparison.Ordinal))
            {
                // 「自己选」走 foreach (… in toTransform / toTransformDeck)；「随机」走循环 + TransformToRandom。
                // 全局（改牌组）那一种：自己选的局部变量是 toTransformDeck，随机那支抓的是 PileType.Deck。
                // 判据用**离当前行最近的那一条选牌语句**，不能拿整段 body.Contains ——
                // 同一张牌上同时有「战斗里的变化」和「牌组里的变化」时，整段判断会把前者误判成全局（实测踩过）。
                bool globalTransform = false, chosenTransform = false;
                for (int back = 1; back <= 30 && i - back >= 0; back++)
                {
                    string prev = lines[i - back];
                    if (prev.Contains("in toTransformDeck", StringComparison.Ordinal)) { globalTransform = true; chosenTransform = true; break; }
                    if (prev.Contains("in toTransform", StringComparison.Ordinal)) { chosenTransform = true; break; }
                }
                if (!chosenTransform && pendingSelectPile == "Deck") globalTransform = true;
                var e = new EffectSpec
                {
                    Kind = globalTransform ? "TransformCardGlobal" : "TransformCard",
                    CardPick = chosenTransform ? "Chosen" : "Random",
                    SelectPile = pendingSelectPile is "Draw" or "Discard" or "Exhaust" or "Top" ? pendingSelectPile : "Hand",
                };
                pendingSelectPile = null;
                string? target = Match(line, @"CreateCard<(\w+)>");
                e.SpawnCardId = target;
                string? count = chosenTransform ? Match(body, @"TransformSelectionPrompt, (\d+)") : null;
                if (count is not null) e.Amount = decimal.Parse(count, CultureInfo.InvariantCulture);
                else e.Amount = LoopValue(LoopTop() ?? "1");
                bool randomTransform = e.IsSpawnRandom || body.Contains("__transformPool", StringComparison.Ordinal);
                Done(e);
                // 「按范围随机变化」那一支后面还有 __newCard / __made / CardCmd.Upgrade(__made) 几行 → 整段跳过
                if (randomTransform) skipSelfContainedBody = true;
                continue;
            }

            // 「消耗卡牌」的**自己选**那一支生成的是**单行** foreach（整段写在一行里）：
            //   foreach (CardModel c in toExhaust) await CardCmd.Exhaust(choiceContext, c);
            // 上面那条分支只认「await CardCmd.Exhaust(…」开头的行（随机那一支是单独一行），
            // 于是「自己选消耗」的存档回读时会掉这条效果、并多出一行「没认出来」（探针实测）。
            if (line.StartsWith("foreach (CardModel c in toExhaust) await CardCmd.Exhaust(", StringComparison.Ordinal))
            {
                var e = new EffectSpec
                {
                    Kind = "ExhaustCard",
                    CardPick = "Chosen",
                    SelectPile = pendingSelectPile is "Draw" or "Discard" or "Exhaust" or "Top" ? pendingSelectPile : "Hand",
                };
                pendingSelectPile = null;
                // 张数在**上一行**（var toExhaust = … ExhaustSelectionPrompt, N）——
                // 只往回看几行，别用整个方法体去 Match（一张牌里两条消耗时会认到第一条的张数）
                string? chosen = null;
                for (int k = i - 2; k >= 0 && k >= i - 6; k--)
                {
                    chosen = Match(lines[k].Trim(), @"ExhaustSelectionPrompt, (\d+)");
                    if (chosen is not null) break;
                }
                e.Amount = chosen is not null ? decimal.Parse(chosen, CultureInfo.InvariantCulture) : LoopValue(LoopTop() ?? "1");
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
                    SelectPile = pendingSelectPile is "Draw" or "Discard" or "Exhaust" or "Top" ? pendingSelectPile : "Hand",
                };
                pendingSelectPile = null;
                string? count = chosenExhaust ? Match(body, @"ExhaustSelectionPrompt, (\d+)") : null;
                e.Amount = count is not null
                    ? decimal.Parse(count, CultureInfo.InvariantCulture)
                    : (LoopValue(LoopTop() ?? "1"));
                Done(e);
                continue;
            }

            // 丢弃卡牌（新效果）：自己选是 CardCmd.Discard(choiceContext, toDiscard)，
            // 随机是循环里 CardCmd.Discard(choiceContext, pick) —— 两种都是同一句，靠 body / 暂存区分。
            if (line.StartsWith("await CardCmd.Discard(", StringComparison.Ordinal))
            {
                bool chosenDiscard = line.Contains("toDiscard", StringComparison.Ordinal);
                var e = new EffectSpec
                {
                    Kind = "DiscardCard",
                    CardPick = chosenDiscard ? "Chosen" : "Random",
                    SelectPile = pendingSelectPile is "Draw" or "Discard" or "Exhaust" or "Top" ? pendingSelectPile : "Hand",
                };
                pendingSelectPile = null;
                string? count = chosenDiscard ? Match(body, @"DiscardSelectionPrompt, (\d+)") : null;
                e.Amount = count is not null
                    ? decimal.Parse(count, CultureInfo.InvariantCulture)
                    : LoopValue(LoopTop() ?? "1");
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

            // 其它时机上的卡牌奖励（战斗中当场弹 N 选一 / 遗物「获得时」发一张）：生成的是四行
            //   var __rewardOptions = CardCreationOptions.ForNonCombatWithDefaultOdds(…);
            //   List<CardModel> __rewardCards = CardFactory.CreateForReward(owner, N, __rewardOptions)…;
            //   CardModel? __pickedReward = await CardSelectCmd.FromChooseACardScreen(choiceContext, __rewardCards, owner, canSkip: true);
            //   CardModel __granted = __pickedReward ?? __rewardCards.FirstOrDefault();
            //   if (__granted is not null) { await CardPileCmd.Add(__granted, PileType.Deck); … }
            // 这一支以前**没有回读**（只认了「战斗胜利后」那种 AddExtraReward 写法），
            // 于是「获得时」这种新触发时机上放卡牌奖励会留下一行「没认出来」。
            if (line.Contains("CardFactory.CreateForReward(", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "CardReward", TargetSide = "Self" };
                string? n = Match(line, @"CreateForReward\([^,]+, (\d+),");
                e.Amount = n is not null ? decimal.Parse(n, CultureInfo.InvariantCulture) : LoopValue(LoopTop() ?? "1");
                ApplyLoop(e, frames);
                Done(e);
                skipSelfContainedBody = true;   // 后面那几行（选牌 / 加进牌组）都是它的实现
                continue;
            }

            // 「从抽牌堆 / 弃牌堆拿牌到手牌（自己选）」——生成的是两行：
            //   var __taken = (await CardSelectCmd.FromCombatPile(choiceContext, PileType.X.GetPile(base.Owner), base.Owner,
            //                      new CardSelectorPrefs(base.SelectionScreenPrompt, N))).ToList();
            //   if (__taken.Count > 0) await CardPileCmd.Add(__taken, PileType.Hand);
            // 以前**完全没有回读这两种效果**（回读后被静默丢掉，只在「没认出来」里列一行）——
            // 用这两种效果的存档（例如 SparkleMod 的「逃脱 / 移形换影」）会掉效果，所以补上。
            if (line.StartsWith("var __taken = ", StringComparison.Ordinal))
            {
                string pile = Match(line, @"PileType\.(\w+)\.GetPile") ?? "Draw";
                var e = new EffectSpec { Kind = pile switch { "Discard" => "TakeFromDiscard", "Exhaust" => "TakeFromExhaust", _ => "TakeFromDraw" } };
                string? n = Match(line, @"SelectionScreenPrompt, (\w+)\)");
                if (n == "x") e.AmountIsX = true;
                else if (n is not null) e.Amount = decimal.Parse(n, CultureInfo.InvariantCulture);
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }
            if (line.StartsWith("if (__taken.Count > 0) await CardPileCmd.Add(", StringComparison.Ordinal))
                continue;   // 上面那条效果的收尾语句（效果已经在 __taken 那一行收尾了）

            // 药水 / 遗物造成的伤害：生成的是
            //   await CreatureCmd.Damage(choiceContext, target, base.DynamicVars.Damage.BaseValue, ValueProp.Move, base.Owner.Creature);
            //   （全体敌人那条是同一个调用，只是第 2 个参数是 CombatState.HittableEnemies）
            // 卡牌走的是 DamageCmd.Attack 链（上面那条分支），所以这里只剩「药水 / 遗物」这种直接调用。
            // 判据用**结尾的「ValueProp.Move, base.Owner.Creature)」**（只有这一处这么写）——
            // 不能笼统地认「CreatureCmd.Damage(choiceContext, X, …)」，那会把「失去生命（对敌人）」也吞进来
            // （它长这样：…, ValueProp.Unblockable | Unpowered | Move, this, null)）。
            // 以前没有这条分支 → 药水里的伤害回读后会丢（只在「没认出来」里列一行）。
            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, ", StringComparison.Ordinal)
                && line.Contains("ValueProp.Move, base.Owner.Creature)", StringComparison.Ordinal))
            {
                string targetArg = ArgAt(line, 1);
                var e = new EffectSpec
                {
                    Kind = "Damage",
                    TargetSide = targetArg.Contains("HittableEnemies") ? "AllEnemies"
                        : targetArg == "target" ? "Enemy" : "Self",
                };
                // 数值：变量优先（生成时声明的是 DamageVar），认不出表达式就按字面量
                FillAmount(e, NextVar(vars, ref varIdx, "Damage"), nameToPowerId, fallbackExpr: ArgAt(line, 2));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 「失去生命（对象不是自己）」：生成的是
            //   await CreatureCmd.Damage(choiceContext, foe, 值, ValueProp.Unblockable | Unpowered | Move, this, null);
            // （对敌人 / 全体 / 随机都是这一句，目标参数是循环变量。）
            // 数值同样可能来自 HpLossVar，也可能被「回复生命填负数」共用 → 看下一个变量是谁。
            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, ", StringComparison.Ordinal)
                && line.Contains("Unblockable | ValueProp.Unpowered | ValueProp.Move", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "HpLoss", TargetSide = SideOfTarget(line) };
                var peekLoss = PeekVar(vars, varIdx);
                if (peekLoss?.Kind == "Damage")
                {
                    // 同一个写法、但这条效果声明的是 DamageVar → 是「造成伤害（作用对象 = 自己/敌人）」
                    var dmg = new EffectSpec { Kind = "Damage", TargetSide = e.TargetSide };
                    FillAmountOrExpr(dmg, ArgAt(line, 2), NextVar(vars, ref varIdx, "Damage"), nameToPowerId, "Damage");
                    ApplyLoop(dmg, frames);
                    Done(dmg);
                    continue;
                }
                FillAmountOrExpr(e, ArgAt(line, 2), NextVar(vars, ref varIdx, "HpLoss"), nameToPowerId, "HpLoss");
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 自定义状态触发器里造成的伤害：生成的是
            //   await CreatureCmd.Damage(choiceContext, other, 值, ValueProp.Unpowered, null, null, null);
            // （dealer 传 null = 不算「你造成的伤害」，见 CustomPowerGen.EmitDamage；
            //   目标是自己 / 全体 / 随机 / 单个敌人靠目标参数与所在的循环区分）
            if (line.StartsWith("await CreatureCmd.Damage(choiceContext, ", StringComparison.Ordinal)
                && line.Contains("ValueProp.Unpowered, null, null, null)", StringComparison.Ordinal))
            {
                string targetArg = ArgAt(line, 1);
                var e = new EffectSpec
                {
                    Kind = "Damage",
                    TargetSide = targetArg.Contains("base.Owner,") || targetArg == "base.Owner" ? "Self"
                        : targetArg.Contains("HittableEnemies") ? "AllEnemies"
                        : frames.Any(f => f.IsLoop) ? (randomFoes ? "RandomEnemies" : "AllEnemies")
                        : "Enemy",
                };
                if (e.TargetSide == "RandomEnemies") e.AllowDuplicates = !foesRemoved;
                // 数值：状态触发器里写的是 base.Amount（= 状态层数）或字面量，没有动态变量要认领
                FillExpr(e, ArgAt(line, 2));
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // ===== 全局改牌组那三种效果（获得 / 变化 / 删除卡牌（全局））=====
            // 以前**完全没回读** → 用了它们的存档（例如 SparkleMod 的「幕后黑手」）会掉效果。
            // 生成代码：
            //   获得：  CardModel gained = base.Owner.RunState.CreateCard(ModelDb.Card<X>(), base.Owner);
            //           await CardPileCmd.Add(gained, PileType.Deck);
            //   删除：  var toRemove = (await CardSelectCmd.FromDeckForRemoval(…RemoveSelectionPrompt, N…)).ToList();
            //           foreach (CardModel c in toRemove) await CardPileCmd.RemoveFromDeck(c);
            //           随机：CardModel? pick = …NextItem(PileType.Deck.GetPile(…).Cards.Where(c => c.IsRemovable)); → RemoveFromDeck(pick)
            //   变化：  var toTransformDeck = (await CardSelectCmd.FromDeckGeneric(…TransformSelectionPrompt, N…)).ToList();
            //           foreach (CardModel c in toTransformDeck) { await CardCmd.Transform[ToRandom](c, …) }
            if (line.StartsWith("CardModel gained = ", StringComparison.Ordinal))
            {
                var e = new EffectSpec
                {
                    Kind = "AddCardGlobal",
                    SpawnCardId = Match(line, @"ModelDb\.Card<(\w+)>"),
                };
                e.Amount = LoopValue(LoopTop() ?? "1");
                Done(e);
                skipSelfContainedBody = true;   // 随机那支后面还有 __pickCard / CardCmd.Upgrade(gained) 几行
                continue;
            }
            if (line.Contains("CardPileCmd.Add(gained, PileType.Deck)")) continue;      // 上面那条的收尾

            if (line.StartsWith("var toRemove = ", StringComparison.Ordinal))
            {
                // 「自己选从牌组删牌」：数值在 prefs 里，真正的动作在下面那行
                var e = new EffectSpec { Kind = "RemoveCardGlobal", CardPick = "Chosen" };
                string? n = Match(line, @"RemoveSelectionPrompt, (\d+)\)");
                e.Amount = n is not null ? decimal.Parse(n, CultureInfo.InvariantCulture) : 1m;
                Done(e);
                continue;
            }
            if (line.EndsWith("CardPileCmd.RemoveFromDeck(c);", StringComparison.Ordinal))
                continue;                                                              // 上面那条的收尾
            if (line.StartsWith("await CardPileCmd.RemoveFromDeck(", StringComparison.Ordinal))
            {
                // 随机删牌（在 for 循环里）
                var e = new EffectSpec { Kind = "RemoveCardGlobal", CardPick = "Random" };
                e.Amount = LoopValue(LoopTop() ?? "1");
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 升级牌组里的牌（全局）：生成的是
            //   自己选：var toUpgradeDeck = (await CardSelectCmd.FromDeckForUpgrade(…UpgradeSelectionPrompt, N…)).ToList();
            //           foreach (CardModel c in toUpgradeDeck) CardCmd.Upgrade(c);
            //   随机：  CardModel? pick = …NextItem(PileType.Deck…Where(c => c.IsUpgradable)); → CardCmd.Upgrade(pick);
            // （战斗里的「升级卡牌」是标记认的 CET:Effect=UpgradeCard，所以这里剩下的只有全局那一种）
            if (line.StartsWith("var toUpgradeDeck = ", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "UpgradeCardGlobal", CardPick = "Chosen" };
                string? n = Match(line, @"UpgradeSelectionPrompt, (\d+)\)");
                e.Amount = n is not null ? decimal.Parse(n, CultureInfo.InvariantCulture) : 1m;
                Done(e);
                continue;
            }
            if (line.EndsWith("CardCmd.Upgrade(c);", StringComparison.Ordinal)) continue;   // 上面那条的收尾
            if (line.StartsWith("CardCmd.Upgrade(pick);", StringComparison.Ordinal))
            {
                var e = new EffectSpec { Kind = "UpgradeCardGlobal", CardPick = "Random" };
                e.Amount = LoopValue(LoopTop() ?? "1");
                ApplyLoop(e, frames);
                Done(e);
                continue;
            }

            // 自定义状态里「回合结束时移除自己」那一行：`await PowerCmd.Remove(this);` 是生成器按
            // 「勾了回合结束移除」自动写的（ParseCustomPower 已经按它设了 RemoveAtTurnEnd），
            // 不是一条用户配的效果 —— 以前它会被记成「没认出来」，让用户以为配置丢了。
            if (ctx == EffectCtx.Power && line.StartsWith("await PowerCmd.Remove(this);", StringComparison.Ordinal))
                continue;

            // 锻造节点图：OnPlay 末尾那一句「调用节点图」的转发，还有节点图那一整段（都写在同一张牌/遗物/药水里）
            // 都不是用户配的「效果」—— 回读认不出来会平白多一行「没认出来」，让用户以为配置丢了。
            // 注意：这一阶段节点图本身**不从工程回读**（配置里那份还在存档里），所以这里只是跳过。
            if (line.StartsWith("await ForgeGraphOnPlay(", StringComparison.Ordinal)) continue;
            // 「升级前 / 升级后」两张布的分派（OnPlay 开头那两行），也不是用户配的效果
            if (line.Contains("ForgeGraphOnPlayUpgraded(", StringComparison.Ordinal)
                || line.Contains("ForgeGraphOnPlay(choiceContext", StringComparison.Ordinal)) continue;
            // 自定义状态的「每回合衰减」（EmitDecay 写的那段；配置从类开头的注释里读回来了）
            if (line.Contains("PowerCmd.ModifyAmount(choiceContext, this, -", StringComparison.Ordinal)) continue;
            if (line.Contains("锻造节点图") || line.Contains("fg:", StringComparison.Ordinal)) continue;
            if (line.StartsWith("decimal __fg", StringComparison.Ordinal) || line.StartsWith("decimal __p", StringComparison.Ordinal)
                || line.StartsWith("decimal __X", StringComparison.Ordinal) || line.StartsWith("decimal __Y", StringComparison.Ordinal)
                || line.StartsWith("Player __fgPlayer", StringComparison.Ordinal) || line.StartsWith("Creature __fgSelf", StringComparison.Ordinal)
                || line.StartsWith("Creature? __fgTarget", StringComparison.Ordinal) || line.StartsWith("Creature __fgWatch", StringComparison.Ordinal)
                || line.StartsWith("if (__fg", StringComparison.Ordinal) || line.StartsWith("__fgUses", StringComparison.Ordinal))
                continue;

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
                // 上面那段（逐行扫 effect 体）已经认出来的就别重复加：召唤物是**列表**，
                // 一张牌上可以召唤多只，所以这里按「哪一只（+ 数值语义）」去重，不能只按 Kind 去重。
                string? cls2 = Match(l, @"(\w+)Cmd\.Summon\(choiceContext");
                bool configured = l.Contains("CET:PetHp=configured");
                if (into.Any(x => x.Kind == "SummonPet"
                        && (string.Equals(x.PetSummon ?? "", cls2 ?? "", StringComparison.Ordinal)
                            || PetGen.IsAll(x.PetSummon))     // 「全部召唤物」：这一行就是它逐只展开出来的
                        && (x.Amount <= 0m) == configured)) continue;
                var e = new EffectSpec { Kind = "SummonPet", TargetSide = "Self", PetSummon = cls2 };
                if (!configured) FillExpr(e, ArgAt(l, 2));
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
        // 循环变量名不一定是 i：生成器在「随机挑敌人 / 随机击晕」里用的是 __foeIdx / __relFoeIdx / __stunIdx…
        // （以前只认 `i < N`，于是「随机 2 个敌人」回读回来变成 1 个 —— 用户看到的像是配置被改了）
        string? m = Match(head, @"\w+ < (\d+)");
        if (m is not null) return m;
        if (System.Text.RegularExpressions.Regex.IsMatch(head, @"\w+ < x\b")) return "x";
        if (head.Contains("(int)base.Amount")) return "stack";
        return "?";
    }

    private static int LoopValue(string s) => int.TryParse(s, out int n) ? n : 1;

    /// <summary>
    /// 往后几行里找「生成出来的卡放进哪一摞」（<c>AddGeneratedCardToCombat(__gen, PileType.X, …)</c>）。
    /// 为什么往后看：带附加处理的生成卡写法里，那一句在升级 / 免费那几行**之后**。
    /// </summary>
    private static string PeekSpawnToPile(List<string> lines, int from, int window = 12)
    {
        foreach (string l in lines.Skip(from).Take(window))
        {
            string? pile = Match(l, @"AddGeneratedCardToCombat\(\w+, PileType\.(\w+)");
            if (pile is not null) return pile;
        }
        return "Hand";
    }

    /// <summary>
    /// 外面那层「生效次数」的重复次数：frames 里最里面那个循环是「生成几张」或 foreach，
    /// 倒数第二个（如果有）才是 EmitRepeated 包的 <c>for (int i = 0; i &lt; N; i++)</c>。
    /// </summary>
    private static int OuterRepeatTimes(IReadOnlyList<Block> frames)
    {
        var loops = frames.Where(f => f.IsLoop).ToList();
        if (loops.Count < 2) return 1;
        return Math.Max(1, LoopValue(loops[^2].Count));
    }

    /// <summary>
    /// 从生成的临时 Power 类名（<c>&lt;角色&gt;ForgeTemp&lt;状态&gt;[Abs][NextTurn]</c>）里把真正的状态名抠回来。
    /// 生成时后缀就是那个状态的类名（例：SparkleForgeTempStrengthPower → StrengthPower），
    /// 所以「ForgeTemp」之后那一段就是 <see cref="EffectSpec.PowerId"/>。
    ///
    /// ⚠ 尾巴要**从后往前**剥：先 <c>NextTurn</c>（下回合开始时才撤）、再 <c>Abs</c>（数值是负数 = 临时减益）——
    /// 顺序反了会把状态名抠成 <c>StrengthPowerNextTurn</c>（那样就认不回本体状态了）。
    /// </summary>
    private static string? TempPowerTargetOf(string className, out bool nextTurn, out bool negative)
    {
        nextTurn = false;
        negative = false;
        const string marker = "ForgeTemp";
        int at = className.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        string raw = className[(at + marker.Length)..].Trim();
        if (raw.EndsWith("NextTurn", StringComparison.Ordinal))
        {
            nextTurn = true;
            raw = raw[..^"NextTurn".Length];
        }
        if (raw.EndsWith("Abs", StringComparison.Ordinal))
        {
            negative = true;
            raw = raw[..^"Abs".Length];
        }
        return raw.Length == 0 ? null : raw;
    }

    private static string? TempPowerTargetOf(string className) => TempPowerTargetOf(className, out _, out _);

    /// <summary>
    /// 自检用：从临时 Power 的类名认出「哪一档撤法」（带 <c>NextTurn</c> 尾巴 = 下回合开始时才撤）。
    /// public 是因为自检在 App 那个程序集里（<c>internal</c> 的成员跨程序集看不见）。
    /// </summary>
    public static string TempPowerReverseForCheck(string className)
    {
        TempPowerTargetOf(className, out bool nextTurn, out _);
        return nextTurn ? TempPowerReverses.NextTurnStart : TempPowerReverses.TurnEnd;
    }

    /// <summary>自检用：从临时 Power 的类名认出「是不是负数档」（带 <c>Abs</c> 尾巴 = 临时减益）。</summary>
    public static bool TempPowerIsNegativeForCheck(string className)
    {
        TempPowerTargetOf(className, out _, out bool negative);
        return negative;
    }

    /// <summary>
    /// 从生成的强化 Power 类名（<c>&lt;角色&gt;ForgeBoost&lt;目标卡&gt;&lt;Damage|Block&gt;Power</c>）
    /// 里把目标卡类名抠回来 —— 只在 <c>// CET:BoostCard=</c> 标记丢了的时候兜底用。
    /// 例：SparkleForgeBoostSparkleStrikeDamagePower → SparkleStrike。
    /// </summary>
    private static string? BoostTargetOf(string className)
    {
        const string marker = "ForgeBoost";
        int at = className.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        string raw = className[(at + marker.Length)..];
        foreach (string tail in new[] { "DamagePower", "BlockPower" })
            if (raw.EndsWith(tail, StringComparison.Ordinal))
            {
                string card = raw[..^tail.Length];
                return card.Length == 0 || card == "Card" ? null : card;
            }
        return null;
    }

    /// <summary>看一眼「下一个还没被认领的变量」是谁（不消费它）。用于区分生成代码一样、但变量不同的效果。</summary>
    private static Var? PeekVar(List<Var> vars, int idx) => idx >= 0 && idx < vars.Count ? vars[idx] : null;

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
                // 金币：生成时用 GoldVar（键 = Gold，走索引器 base.DynamicVars["Gold"].BaseValue）。
                // 以前这里没有 "Gold" 分支 → 取不到变量、FillExpr 又认不出索引器表达式 → 金币数值静默变成 0。
                "Gold" => v.Kind == "Gold",
                "Stars" => v.Kind == "Stars",
                // 伙伴攻击：生成时用的是我们自己起名的普通 DynamicVar "PetDamage"（不是 DamageVar）
                "PetDamage" => v.Kind == "DynamicVar" && string.Equals(v.PowerId, "PetDamage", StringComparison.Ordinal),
                // 回复生命 / 失去生命 / 最大生命：本体那三种变量（键就是 Heal / HpLoss / MaxHp）。
                // 生成的是 base.DynamicVars["Heal"].BaseValue 这种**索引器**写法（这三个没有同名属性），
                // 以前这里直接把表达式丢给 FillExpr → 认不出来 → 数值静默变成 0（自检抓到的）。
                "Heal" => v.Kind == "Heal",
                "HpLoss" => v.Kind == "HpLoss",
                "MaxHp" => v.Kind == "MaxHp",
                // 强化指定卡牌：生成时用的是普通 DynamicVar，名字是 Boost（同一张牌上第二次起叫 Boost2）
                "Boost" => v.Kind == "DynamicVar" && v.PowerId is not null
                    && (v.PowerId == "Boost"
                        || (v.PowerId.StartsWith("Boost", StringComparison.Ordinal)
                            && v.PowerId.Length > 5 && v.PowerId[5..].All(char.IsDigit))),
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
        // 两种写法都要认：
        //   · 普通效果 → base.DynamicVars["Damage"].UpgradeValueBy(3m)
        //   · 「按生命值算」的宠物效果 → base.DynamicVars.CalculationBase.UpgradeValueBy(3m)
        //     （那个计算变量的名字在本体里是固定死的，只能用属性写法，不是索引器）
        foreach (Match m in Regex.Matches(body, @"base\.DynamicVars(?:\[""(\w+)""\]|\.(\w+))\.UpgradeValueBy\((-?[\d.]+)m\)"))
        {
            string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            decimal delta = decimal.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
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
        // 「按生命值算」的宠物效果：升级增量生成时写在 CalculationBase 上
        // （那个计算变量的名字是本体固定死的，改不了），所以按名字特判一下，
        // 配给这张牌里**第一条**这种效果（生成侧也是这么累加的）。
        if (name == "CalculationBase")
            return card.Effects.FirstOrDefault(e => IsCalcPetKindName(e.Kind) && CSharpCodeGen.CanonicalVarNeedsPetCmd(e));

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
    private static bool HasVar(EffectSpec e) =>
        // 「全部召唤物」+「按生命值算收益」：生成侧走内联计算，**不声明**动态变量
        // （见 CSharpCodeGen.IsAllPetsInlineCalc / HasNoDynamicVar）—— 这里必须一起排除，
        // 否则 CanonicalVars 的变量和「有变量的效果」会错位一格，升级增量会加到别的效果上。
        CSharpCodeGen.IsAllPetsInlineCalc(e) ? false
        // 「数值 0 = 这张牌自己」（丢弃卡牌）：那一档**没有张数变量**（生成侧 HasNoDynamicVar 也认它），
        // 这里必须一起排除，否则 CanonicalVars 的变量和「有变量的效果」会错位一格。
        : CSharpCodeGen.AmountZeroIsSelf(e) ? false
        : e.Kind switch
    {
        "Damage" or "Block" or "Draw" or "Energy" => e.AmountIsStack == false && !e.AmountIsX,
        // 升级卡牌 / 预见：都用本体的 CardsVar（键 = Cards）
        "UpgradeCard" or "Scry" => !e.AmountIsX,
        // 刮削：同样是 CardsVar（数值 = 抽几张）
        "ScrapeDraw" => !e.AmountIsX,
        // 毒性爆发：PowerVar<PoisonPower>（键 = PoisonPower），升级增量落在它上面
        "Outbreak" => true,
        // 大限已至：计算三件套（CalculationBase + ExtraDamage + CalculatedDamage），
        // 升级增量按 CalculationBase 走（和「按生命值算的伙伴攻击」同一套，见 IsCalcVar 的处理）
        "TimesUp" => true,
        // 金币：生成侧确实会声明 GoldVar（HasNoDynamicVar 没有排除它）——这里以前漏了它，
        // 于是「金币」后面那条带变量的效果会整体错位一格，升级增量会加到别的效果上。
        "Gold" => e.AmountIsStack == false && !e.AmountIsX,
        "ApplyPower" => true,
        // 临时增益：和「施加增益/减益」一样声明 PowerVar<那个状态>，升级增量也落在同一个变量上
        "TempPower" => true,
        // 强化指定卡牌：声明的是普通 DynamicVar「Boost」（数值 = X 时生成侧不声明变量）
        "BoostCard" => !e.AmountIsX,
        // 额外资源量：只有「正数获得」才声明 StarsVar（花费走 CanonicalStarCost，不占变量）——
        // 少了这一条，这类牌的升级增量会按错误的序号对到别的效果上（或者直接报「找不到对应效果」）。
        "ExtraResource" => e.Amount > 0,
        "PetAttack" => true,
        // 透支能量：和「获得能量」共用 EnergyVar（键 = Energy），所以升级增量按变量名对得上
        "OverdraftEnergy" => true,
        "SummonPet" => e.Amount > 0,
        // 新增的那批宠物效果：除了两条「替主人承伤」开关，其余都在 CanonicalVars 里有变量
        // （和生成侧的 CSharpCodeGen.HasNoDynamicVar 保持一致）
        "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp"
            or "PetHeal" or "PetLoseHp" or "PetGainMaxHp" or "PetSacrifice" or "PetApplyPower" => true,
        _ => false,
    };

    private static string VarNameOf(Var v) => v.Name ?? v.Kind switch
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

    /// <summary>
    /// 「按生命值算」的宠物效果在 Generated 代码里的**升级落点**：
    /// 那几个计算变量（CalculatedDamage / CalculatedBlock）的名字是本体固定死的，
    /// 升级增量写在 <c>CalculationBase</c> 上（见 CSharpCodeGen 的 OnUpgrade 生成）。
    /// 所以回读时把 <c>CalculationBase</c> 的增量配给那张牌里第一条这种效果。
    /// </summary>
    private static bool IsCalcPetKindName(string kind) =>
        kind is "PetDamageByMaxHp" or "PetDamageByCurHp" or "PetDamageByMissingHp" or "PetSacrifice"
            // 「大限已至」用的也是这套计算三件套（伤害 = 目标的灾厄层数），升级增量同样落在 CalculationBase 上
            or "TimesUp";

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
                // 永恒（IsRemovable / IsTransformable 都为 false）：诅咒「厄运」用的就是它
                case "Eternal": card.Eternal = true; break;
            }
        }
    }

    /// <summary>
    /// 从 <c>CET:CondFull=…</c> 标记（见 <see cref="CSharpCodeGen.ConditionMarker"/>）重建一条条件。
    /// 用在「条件的数值写了公式」时：那时守卫已经变成一整句算完的表达式，反推不回种类与公式。
    /// </summary>
    private static ConditionSpec? ConditionFromMarker(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string piece in raw.Split('|'))
        {
            int eq = piece.IndexOf('=');
            if (eq <= 0) continue;
            map[piece[..eq].Trim()] = piece[(eq + 1)..].Trim();
        }
        if (!map.TryGetValue("Kind", out string? kind) || kind.Length == 0) return null;
        string S(string key) => map.TryGetValue(key, out string? v) ? v : "";
        decimal D(string key) => decimal.TryParse(S(key), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal v) ? v : 0m;
        var cond = new ConditionSpec
        {
            Kind = kind,
            Amount = D("Amount"),
            Amount2 = D("Amount2"),
            PowerId = S("Power"),
            Target = S("Target").Length == 0 ? "Self" : S("Target"),
            WhenUnmet = S("WhenUnmet").Length == 0 ? "Unplayable" : S("WhenUnmet"),
        };
        if (S("Timing").Length > 0) cond.Timing = S("Timing");
        if (S("AmountFormula").Length > 0) cond.AmountFormula = S("AmountFormula");
        if (S("Amount2Formula").Length > 0) cond.Amount2Formula = S("Amount2Formula");
        return cond;
    }

    /// <summary>卡牌条件：从「// 条件：…」注释 + IsPlayable 反推。</summary>
    private static void ParseCardCondition(CardSpec card, string text, Dictionary<string, string> nameToPowerId,
        RecoveryResult result, string where)
    {
        // 「条件的数值写了公式」（用户要求⑦）时生成端会写 `// CET:CardCondFull=…`（整张牌这一条）：
        // 那时 IsPlayable / 描金边里是一整句算完的表达式，反推不回条件 —— 直接按标记重建，也不报「没认出来」。
        // ⚠ 效果自己那条条件用的是 `CET:CondFull=…`（在 OnPlay 里，由那条效果认领）——
        // 这里**只认**整张牌专用的那个标记，不然「某条效果的条件」会被认成「整张牌的条件」。
        if (Match(text, @"CET:CardCondFull=([^\r\n]*)") is { } fullRaw && ConditionFromMarker(CSharpCodeGen.UnescapeMarker(fullRaw.Trim())) is { } fromMarker)
        {
            fromMarker.WhenUnmet = text.Contains("IsPlayable =>") ? "Unplayable" : "NoEffect";
            card.Condition = fromMarker;
            return;
        }
        string? comment = Match(text, @"// 条件：(.+)");
        // 连「// 条件：…」注释都没有 = 这张牌**没有**「整张牌的条件」（生成端只要写了就会留下这行注释，
        // 两条分支都写）。这时 IsPlayable 里剩下的只可能是**某条效果**自己的条件（它们各有
        // `CET:CondFull=` 标记、又各自认领）——不能拿它当「整张牌的条件」，
        // 也不用为它报「条件没认出来」（那是效果的条件，不是整张牌的条件丢了）。
        if (comment is null) return;
        // 「需要已召唤伙伴」的守卫（`<X>Cmd.Get(base.Owner) != null`）是**生成器自动加的**，不是用户配的条件：
        // 先把它从 IsPlayable / 描金边的表达式里摘掉，否则会被当成「条件没认出来」记进 Unparsed
        //（用户看到「有生成代码没认出来」会以为配置丢了），也会混进条件表达式里解析不出来。
        string? conditionExpr = StripPetGuards(Match(text, @"IsPlayable => (.+);"))
            ?? StripPetGuards(Match(text, @"ShouldGlowGoldInternal => (.+);"));
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

    /// <summary>
    /// 摘掉生成器自动加的「需要已召唤伙伴」守卫项（<c>(XxxCmd.Get(base.Owner) != null)</c>），
    /// 只留用户真正配置的条件表达式。
    ///
    /// 为什么要有这一步：卡牌只要有宠物类效果，生成器就会往 IsPlayable / ShouldGlowGoldInternal
    /// 汇进 <c>(&lt;宠物&gt;Cmd.Get(base.Owner) != null)</c>（照本体 Osty 那批牌）。那是**运行时守卫**，
    /// 回读时既不该算成条件、也不该报「没认出来」。全是守卫时返回 null = 这张牌没有用户条件。
    /// </summary>
    private static string? StripPetGuards(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        var rest = expr.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !IsPetGuardOnly(x))
            .ToList();
        return rest.Count == 0 ? null : string.Join(" && ", rest);
    }

    /// <summary>
    /// 这一项是不是「纯粹的一只 / 多只宠物守卫」：
    /// <c>(A != null)</c>（单只）或 <c>(A != null || B != null)</c>（「全部召唤物」生成的是或运算形式）。
    /// 括号可能套了好几层（IsPlayable 的表达式外面本来就有一层），所以前后都按 <c>\(*</c> / <c>\)*</c> 收。
    /// </summary>
    private static bool IsPetGuardOnly(string item)
    {
        string s = item.Trim();
        if (s.Length == 0) return false;
        foreach (string part in s.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!Regex.IsMatch(part, @"^\(*\s*\w+Cmd\.Get\(base\.Owner\)\s*!=\s*null\s*\)*$")) return false;
        return true;
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
        // 条件里的「填负数 = 反向」是**生成时**把比较符翻过来的（>= ↔ <、<= ↔ >），
        // 所以回读要把翻转过的那些形式也认出来，并把数值取成负数（用户看到的还是原来那个 -N）。
        // 顺序：先认「区间外 / 区间内」（生命区间），再认翻转形式，最后认普通形式。
        decimal Num(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

        // 生命区间（在区间内 / 区间外）：生成的是
        //   (hp * 100 >= max * lo && hp * 100 <= max * hi)          ← 区间内
        //   (hp * 100 <  max * lo || hp * 100 >  max * hi)          ← 区间外（任一数值填了负数）
        var range = Regex.Match(e,
            @"CurrentHp \* 100 (?<o1>>=|<=|<|>) .*?MaxHp \* (?<lo>[\d.]+) (?<join>&&|\|\|) .*?CurrentHp \* 100 (?<o2>>=|<=|<|>) .*?MaxHp \* (?<hi>[\d.]+)");
        if (range.Success)
        {
            decimal lo = Num(range.Groups["lo"].Value), hi = Num(range.Groups["hi"].Value);
            bool outside = range.Groups["join"].Value == "||";
            return new ConditionSpec
            {
                Kind = "HpInRange",
                Amount = outside ? -lo : lo,
                Amount2 = hi,
            };
        }
        // 状态层数：>= N 是「至少 N 层」；< N 是「少于 N 层」（生成时填了负数）
        var power = Regex.Match(e, @"GetPowerAmount<(\w+)>\(\) >= ([\d.]+)");
        if (power.Success) return new ConditionSpec { Kind = "HasPowerAtLeast", PowerId = power.Groups[1].Value, Amount = Num(power.Groups[2].Value) };
        power = Regex.Match(e, @"GetPowerAmount<(\w+)>\(\) < ([\d.]+)");
        if (power.Success) return new ConditionSpec { Kind = "HasPowerAtLeast", PowerId = power.Groups[1].Value, Amount = -Num(power.Groups[2].Value) };
        // 「层数刚好达到 N 层」（用户要求）：生成的是
        //   power is XPower && power.Amount >= N && power.Amount - amount < N
        var reached = Regex.Match(e, @"power is (\w+) && power\.Amount >= ([\d.]+) && power\.Amount - amount < [\d.]+");
        if (reached.Success) return new ConditionSpec { Kind = "PowerJustReached", PowerId = reached.Groups[1].Value, Amount = Num(reached.Groups[2].Value) };
        var cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) <= ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtMost", Amount = Num(cards.Groups[1].Value) };
        cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) > ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtMost", Amount = -Num(cards.Groups[1].Value) };
        cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) >= ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtLeast", Amount = Num(cards.Groups[1].Value) };
        cards = Regex.Match(e, @"CardPile\.GetCards\(base\.Owner, PileType\.Hand\)\.Count\(\) < ([\d.]+)");
        if (cards.Success) return new ConditionSpec { Kind = "HandAtLeast", Amount = -Num(cards.Groups[1].Value) };
        // 「拥有额外资源量至少 N 点」：生成的是 ((base.Owner?.PlayerCombatState?.Stars) ?? 0) >= N；
        // < N 那个是填了负数（反向）
        var stars = Regex.Match(e, @"PlayerCombatState\?\.Stars\) \?\? 0\) >= ([\d.]+)");
        if (stars.Success) return new ConditionSpec { Kind = "ExtraResourceAtLeast", Amount = Num(stars.Groups[1].Value) };
        stars = Regex.Match(e, @"PlayerCombatState\?\.Stars\) \?\? 0\) < ([\d.]+)");
        if (stars.Success) return new ConditionSpec { Kind = "ExtraResourceAtLeast", Amount = -Num(stars.Groups[1].Value) };
        // 抽牌堆洗过牌（靠生成的记录器，见 CSharpCodeGen.ShuffleTrackerSource）
        if (e.Contains("ShuffleTracker", StringComparison.Ordinal) && e.Contains("ShuffledThisTurn", StringComparison.Ordinal))
            return new ConditionSpec { Kind = "ShuffledThisTurn" };
        if (e.Contains("ShuffleTracker", StringComparison.Ordinal) && e.Contains("ShuffledThisCombat", StringComparison.Ordinal))
            return new ConditionSpec { Kind = "ShuffledThisCombat" };
        if (e.Contains("CardType.Attack") && e.Contains("PileType.Hand")) return new ConditionSpec { Kind = "HandOnlyAttack" };
        if (e.Contains("CardType.Skill") && e.Contains("PileType.Hand")) return new ConditionSpec { Kind = "HandOnlySkill" };
        if (e.Contains("PileType.Draw")) return new ConditionSpec { Kind = "DrawPileEmpty" };
        if (e.Contains("PileType.Discard") && e.Contains("Any()")) return new ConditionSpec { Kind = "DiscardPileEmpty" };
        var hp = Regex.Match(e, @"CurrentHp \* 100 <= .*MaxHp \* ([\d.]+)");
        if (hp.Success) return new ConditionSpec { Kind = "HpBelowPercent", Amount = Num(hp.Groups[1].Value) };
        hp = Regex.Match(e, @"CurrentHp \* 100 >= .*MaxHp \* ([\d.]+)");
        if (hp.Success) return new ConditionSpec { Kind = "HpBelowPercent", Amount = -Num(hp.Groups[1].Value) };
        // 生成的是 `…CardPlaysFinished.Count(e => e.Actor == … && e.HappenedThisTurn(base.CombatState)) >= N`，
        // 里面**套着括号**，所以不能写 [^)]*（那样只能匹配到内层那个右括号 → 整条条件认不出来）。
        var played = Regex.Match(e, @"CardPlaysFinished\.Count\(.*?\)\s*>=\s*([\d.]+)");
        if (played.Success) return new ConditionSpec { Kind = "PlayedAtLeast", Amount = Num(played.Groups[1].Value) };
        played = Regex.Match(e, @"CardPlaysFinished\.Count\(.*?\)\s*<\s*([\d.]+)");
        if (played.Success) return new ConditionSpec { Kind = "PlayedAtLeast", Amount = -Num(played.Groups[1].Value) };
        if (e.Contains("CardPlaysFinished.Any") && e.Contains("== this")) return new ConditionSpec { Kind = "NotPlayedThisCombat" };
        var turn = Regex.Match(e, @"TurnNumber % ([\d.]+) == 0");
        if (turn.Success) return new ConditionSpec { Kind = "EveryNTurns", Amount = Num(turn.Groups[1].Value) };
        if (e.Contains("_condUsedThisCombat")) return new ConditionSpec { Kind = "OncePerCombat" };
        // 斩杀（本体 Fatal）：生成的是
        //   __lastAttack is not null && __fatalOk && __lastAttack.Results.SelectMany(hits => hits).Any(r => r.WasTargetKilled)
        if (e.Contains("__lastAttack") && e.Contains("WasTargetKilled")) return new ConditionSpec { Kind = "Fatal" };
        return null;
    }

    // ==================== 遗物 / 药水 / 自定义状态 ====================

    private static RelicSpec ParseRelic(string file, CharacterProfile profile, LocTables loc,
        Dictionary<string, string> nameToPowerId, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = ReadForRecovery(file);      // V0.2.128：锻造节点图那几段方法体整段抠掉（见 ReadForRecovery）
        string entry = EffectCatalog.SlugFor(cls);
        var relic = new RelicSpec
        {
            ClassName = cls,
            Name = loc.Get($"{entry}.title", cls),
            Rarity = Match(text, @"=>\s*RelicRarity\.(\w+)") ?? "Common",
        };
        // ===== V0.2.126：遗物 = **一份效果 + 一组触发时机** =====
        // 老工程（只有一个钩子）走下面那条老路径，回读结果逐字不变；
        // 多条时机时把类里**所有**钩子方法都扫出来（每个方法体 = 一条触发时机，
        // 只管「什么时候触发」），共享效果从**写了 CET:TriggerMode 标记的那一段**（或唯一那一段）解析**一次**。
        var relicHooks = RelicHookMethods(text);
        if (relicHooks.Count > 1)
        {
            var varsAll = ParseVars(text);
            foreach (var (hook, kind, body) in relicHooks)
            {
                var trig = RelicTriggerFromBody(hook, kind, body, text, nameToPowerId, result, cls, relic);
                if (trig is null) continue;
                relic.Triggers.Add(trig);
            }
            if (relic.Triggers.Count == 0) relic.Triggers.Add(new RelicTriggerSpec());
            // 共享效果：先看关系标记（All 模式只有「凑齐后跑效果」那一段有标记），
            // 没有标记（Any 模式）就用**第一段**的方法体解析一次（正式生成的 Any 模式每段都跑同一份效果）。
            relic.Effects.Clear();
            RelicTriggerModeFromText(text, relic);
            string effectsBody = SharedEffectsBody(relicHooks);
            ParseEffects(relic.Effects, effectsBody, varsAll, nameToPowerId, EffectCtx.Relic, result, cls, result.PetClassNames);
            ParseRelicTail(text, relic, nameToPowerId, result);
            return relic;
        }

        // 触发时机：先按钩子签名认；含糊钩子（AfterDamageReceived / BeforeSideTurnEnd /
        // AfterPowerAmountChanged…）在 RelicTriggerOfHook 里**故意返回空**，得靠方法体里的判据才能定下来 ——
        // 那一份已经由 RelicHookMethods 算好了，优先用它；没有再退回钩子签名 + 类里那句
        // `// 触发时机：<显示名>`（老工程里写的是改名前的显示名，所以两边都要认）。
        string singleHook = relicHooks.Count > 0 ? relicHooks[0].Hook : RelicHookOf(text);
        string singleKind = relicHooks.Count > 0 ? relicHooks[0].Kind
            : (RelicTriggerOfHook(singleHook) is { Length: > 0 } byHook ? byHook : RelicTriggerOfDisplay(text));
        string singleBody = relicHooks.Count > 0 ? relicHooks[0].Body : BodyOfHook(text);
        // ⚠ 先把 Triggers 清空：RelicSpec 建出来时**自带一条占位时机**（字段初始化器 new() 那条），
        // 直接 Add 会留下「占位 + 真」两条 —— 回读结果凭空多一条空时机。
        relic.Triggers.Clear();
        // 单条时机：效果是**遗物级**那一份（生成端单条路径读的就是 r.Effects），
        // 所以这里直接把方法体里的效果解析进 relic.Effects（老工程的回读结果逐字不变）。
        relic.Effects.Clear();
        RelicTriggerModeFromText(text, relic);
        var singleTrigger = RelicTriggerFromBody(singleHook, singleKind, singleBody, text,
            nameToPowerId, result, cls, relic);
        ParseEffects(relic.Effects, singleBody, ParseVars(text), nameToPowerId, EffectCtx.Relic,
            result, cls, result.PetClassNames);
        if (singleTrigger is not null)
        {
            relic.Triggers.Add(singleTrigger);
        }
        else
        {
            // 方法体认不出来（空遗物 / 老工程）：至少把触发时机留下
            relic.Triggers.Add(new RelicTriggerSpec { Kind = singleKind });
        }
        ParseRelicTail(text, relic, nameToPowerId, result);
        return relic;
    }

    /// <summary>
    /// 多时机遗物的**共享效果**要从哪个方法体里解析：生成端把关系标记
    /// （<c>CET:TriggerMode=</c>）只写在**真正跑共享效果的那一段**（All = 最后一条时机，
    /// Any = 第一条）—— 所以「带标记的那一段」就是答案；一个标记都没有（理论上不会发生）时退回最后一段。
    /// </summary>
    private static string SharedEffectsBody(List<(string Hook, string Kind, string Body)> hooks)
    {
        foreach (var h in hooks)
            if (h.Body is { } body && body.Contains("CET:TriggerMode=", StringComparison.Ordinal))
                return body;
        return hooks.Count > 0 && hooks[^1].Body is { } last ? last : "";
    }

    /// <summary>
    /// 诊断用：把某个遗物类文件里认出来的触发时机段落 / 共享效果 dump 出来
    /// （「生成 → 回读」对不上时用它看回读到底认出了什么；界面自检与探针会调）。
    /// </summary>
    public static IReadOnlyList<string> DebugRelicHooks(string file)
    {
        string text = File.ReadAllText(file, Encoding.UTF8);
        var list = new List<string>();
        var hooks = RelicHookMethods(text);
        list.Add("hooks=" + hooks.Count);
        foreach (var (hook, kind, body) in hooks)
            list.Add(hook + " | " + kind + " | len=" + (body?.Length ?? -1)
                + " | hasMarker=" + (body is not null && body.Contains("CET:TriggerMode=", StringComparison.Ordinal))
                + " | " + (body ?? "").Replace("\n", "⏎"));
        foreach (var (hook, kind, body) in hooks)
        {
            var t = RelicTriggerFromBody(hook, kind, body ?? "", text);
            list.Add("PARSE> " + hook + " → " + (t is null ? "(null)" : t.Kind + "/" + t.CardFilter + "/" + t.Amount + "/" + t.Scope));
        }
        string effectsBody = SharedEffectsBody(hooks);
        var fx = new List<EffectSpec>();
        ParseEffects(fx, effectsBody, ParseVars(text), new Dictionary<string, string>(StringComparer.Ordinal), EffectCtx.Relic,
            new RecoveryResult(), Path.GetFileNameWithoutExtension(file));
        list.Add("EFFECTS> 段长=" + effectsBody.Length + " 效果=" + string.Join("+", fx.Select(e => e.Kind + ":" + e.Amount)));
        return list;
    }

    /// <summary>遗物类的尾部共同部分：自定义描述 + 整只遗物的条件选项。</summary>
    private static void ParseRelicTail(string text, RelicSpec relic, Dictionary<string, string> nameToPowerId,
        RecoveryResult result)
    {
        // 遗物自定义描述（和卡牌同一套标记：本地化表里是拼好的，认不出来）
        if (ParseCustomDescription(text) is var (rdText, rdReplace))
        {
            relic.CustomDescription = rdText;
            relic.CustomDescriptionReplaces = rdReplace;
        }
        string? comment = Match(text, @"// 条件：(.+)");
        string? expr = Match(text, @"if \(!\((.+)\)\) return;") ?? Match(text, @"if \((_condUsedThisCombat)\)");
        var cond = ConditionFrom(comment, expr, nameToPowerId) ?? ConditionFromExpr(expr, nameToPowerId);
        if (cond is not null) relic.Condition = cond;
    }

    /// <summary>
    /// 从类文本里还原「触发时机之间的关系」（V0.2.126 的 <c>// CET:TriggerMode=Any|All CET:AndScope=…</c>）。
    ///
    /// 老工程（V0.2.125 及更早）没有这个标记，但那时勾「同时成立」会在方法体里写
    /// <c>// CET:RequireAll=1 CET:AndScope=X</c> —— 认它等于「全部满足才触发」（和存档迁移同一口径）。
    /// 两个标记都没有 = Any（默认）。
    /// </summary>
    private static void RelicTriggerModeFromText(string text, RelicSpec relic)
    {
        if (Match(text, @"CET:TriggerMode=(\w+)") is { } mode && mode.Length > 0)
        {
            relic.TriggerMode = mode;
            if (Match(text, @"CET:AndScope=(\w+)") is { } scope && scope.Length > 0) relic.AndScope = scope;
            return;
        }
        if (text.Contains("CET:RequireAll=1", StringComparison.Ordinal))
        {
            relic.TriggerMode = "All";
            if (Match(text, @"CET:AndScope=(\w+)") is { } oldScope && oldScope.Length > 0) relic.AndScope = oldScope;
        }
    }

    /// <summary>
    /// 遗物类里**所有触发钩子方法**（钩子名, 时机 id, 方法体）。
    ///
    /// 第 10 批：一只遗物能挂多条触发时机，生成端按「钩子」分组（同一个钩子只生成一个 override）——
    /// 所以回读也按钩子方法扫：每个方法体里的效果各自成一条 <see cref="RelicTriggerSpec"/>。
    /// 不该算触发时机的钩子会被 <see cref="RelicTriggerOfHook"/> 认不出来（返回空 id）而跳过；
    /// 「每场战斗只触发一次」写的清理方法 <c>AfterCombatEnd</c> 也在这里排掉（它不是触发时机）。
    /// 方法体里同时含两条时机时，按 <c>// 触发时机：</c> / <c>// CET:RequireAll=1</c> /
    /// <c>// CET:PowerWatch=</c> 这些**段落标记**切段（见 <see cref="SplitRelicTriggerSegments"/>）。
    /// </summary>
    private static List<(string Hook, string Kind, string Body)> RelicHookMethods(string text)
    {
        var list = new List<(string, string, string)>();
        bool hasOncePerCombat = text.Contains("_condUsedThisCombat", StringComparison.Ordinal);
        bool combatEndTiming = text.Contains("触发时机：战斗结束时（房间结算之前）", StringComparison.Ordinal);
        foreach (Match m in Regex.Matches(text, @"public (?:override )?async Task (\w+)\("))
        {
            string hook = m.Groups[1].Value;
            if (!RelicHookIsTrigger(hook)) continue;
            // 「每场战斗只触发一次」的清理钩子（AfterCombatEnd，只有那一句 _condUsedThisCombat = false;）
            // 不是触发时机 —— 别认成「战斗结束时」（老工程的判据，和 RelicHookOf 保持一致）
            if (hook == "AfterCombatEnd" && hasOncePerCombat && !combatEndTiming) continue;
            string body = MethodBody(text, hook);
            if (body.Length == 0) continue;
            // 同一个钩子对应多条遗物触发时机时（含糊的那几个），看方法体里的判据定下来。
            // ⚠ 用 `text`（整个类）而不是 body 判「战斗结束时」：那个标记写在注释里、不会变；
            //   `power.Amount - amount <` / `side != CombatSide.Enemy` 两个判据一定在方法体里。
            string? kind = RelicTriggerOfHook(hook);
            if (kind is null or "")
            {
                if (hook == "AfterPowerAmountChanged")
                    kind = body.Contains("power.Amount - amount <", StringComparison.Ordinal) ? "PowerReached" : "PowerChanged";
                else if (hook == "AfterDamageReceived")
                    kind = body.Contains("result.UnblockedDamage <= 0", StringComparison.Ordinal) ? "DamageReceived" : "EnemyDamaged";
                else if (hook == "AfterDamageGiven")
                    kind = body.Contains("dealer != base.Owner.Creature", StringComparison.Ordinal) ? "DamageDealt" : "EnemyDealtDamage";
                else if (hook == "AfterBlockGained")
                    kind = body.Contains("creature != base.Owner.Creature", StringComparison.Ordinal) ? "BlockGained" : "EnemyBlockGained";
                else if (hook == "BeforeSideTurnEnd")
                    kind = body.Contains("CombatSide.Enemy", StringComparison.Ordinal) ? "EnemyTurnEnd" : "PlayerTurnEnd";
                else
                    kind = "";
            }
            if (kind.Length == 0) continue;
            // 同一个钩子方法里可能合并了**好几条时机**（第 10 批：按钩子分组）→ 按段落标记切开，
            // 每段各自成一条触发时机。只有一段时原样返回（老工程回读结果一字不变）。
            foreach (string segment in SplitRelicTriggerSegments(body))
                list.Add((hook, kind, segment));
        }
        return list;
    }

    /// <summary>这个钩子方法名可能是遗物触发时机（含糊的那几个也算 —— 由 <see cref="RelicHookMethods"/> 再看判据定）。</summary>
    private static bool RelicHookIsTrigger(string hook) =>
        hook is "AfterObtained" or "BeforeSideTurnStart" or "AfterPlayerTurnStart" or "AfterCombatVictory"
            or "AfterCombatEnd" or "AfterAttack" or "AfterGoldGained" or "AfterShuffle"
            or "AfterEnergySpent" or "AfterStarsSpent" or "AfterCardGeneratedForCombat" or "AfterCardPlayed"
            or "AfterCardExhausted" or "AfterCardDrawn" or "AfterCardDiscarded" or "AfterDeath"
            or "AfterSideTurnStart" or "AfterCurrentHpChanged" or "AfterDamageReceived" or "AfterDamageGiven"
            or "AfterBlockGained" or "BeforeSideTurnEnd" or "AfterPowerAmountChanged" or "OnForgePowerRemovedAsync";

    /// <summary>
    /// 把遗物某个钩子方法体按**段落标记**切成几段（每段 = 一条触发时机）。
    ///
    /// 多时机遗物的一个钩子方法体长这样（见 <c>CSharpCodeGen.EmitRelicMultiTriggerHook</c>）：
    /// <code>
    /// // 触发时机：每回合开始时
    /// if (player == base.Owner)
    /// {
    ///     …效果…
    /// }
    /// // 触发时机：每回合结束时
    /// if (side == base.Owner.Creature.Side)
    /// {
    ///     …效果…
    /// }
    /// </code>
    /// 段首是这几种行之一：<c>// 触发时机：</c> / <c>// CET:RequireAll=1</c>（老格式）/
    /// <c>// CET:TriggerMode=</c>（V0.2.126）/ <c>// CET:PowerWatch=</c> / <c>// 条件（整只遗物）：</c>。
    /// **切段时按大括号配对**（进入标记行前先收尾上一段）—— 不按缩进猜，改写法也不会切错。
    /// 只有一段时返回整段（老工程回读逐字不变）。
    /// </summary>
    private static List<string> SplitRelicTriggerSegments(string body)
    {
        string norm = body.Replace("\r\n", "\n");
        string[] lines = norm.Split('\n');
        var starts = new List<(int Line, int Depth)>();
        int depth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            bool marker = t.StartsWith("// 触发时机：", StringComparison.Ordinal)
                || t.StartsWith("// CET:RequireAll=1", StringComparison.Ordinal)
                || t.StartsWith("// CET:TriggerMode=", StringComparison.Ordinal)
                || t.StartsWith("// CET:PowerWatch=", StringComparison.Ordinal)
                || t.StartsWith("// 条件（整只遗物）：", StringComparison.Ordinal);
            if (marker && depth == 0) starts.Add((i, depth));
            // 计数放在这一行之后（标记行之前不可能有 `{`，但 `}` 归上一段是正确的）
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
        }
        // ⚠ 有些时机**没有**任何段落标记（既不是含糊钩子、也没有 PowerWatch / CardFilter / 数值）——
        //   它那一段只有一句 `// 触发时机：…` 头注释 + 守卫 + 效果。这种情况下 starts 可能只有 1 个
        //   （甚至一个都没有），而**真正的内容从第一行注释就开始了**。不把段首补到那行，那一段就会在
        //   切段时被整段丢掉（实测：Any 模式两条时机的遗物回读只回来 1 条）。
        if (starts.Count == 0 || starts[0].Line > 0)
        {
            int firstComment = -1;
            depth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("//", StringComparison.Ordinal) && depth == 0) { firstComment = i; break; }
                depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            }
            if (firstComment >= 0 && (starts.Count == 0 || firstComment < starts[0].Line))
                starts.Insert(0, (firstComment, 0));
        }
        if (starts.Count < 2) return new List<string> { body };
        var segments = new List<string>();
        for (int s = 0; s < starts.Count; s++)
        {
            int from = starts[s].Line;
            int to = lines.Length - 1;
            if (s + 1 < starts.Count)
            {
                // 下一段的段首；如果它前面那一行是空行，一起切出去（空行留在上一段末尾会多一个空行）
                int next = starts[s + 1].Line;
                if (next - 1 > from && lines[next - 1].Trim().Length == 0) next--;
                to = next;
            }
            segments.Add(string.Join("\n", lines[from..to]));
        }
        return segments;
    }

    /// <summary>
    /// 从某个钩子方法体的一段里还原**一条**遗物触发时机的**触发条件**（盯哪个状态 / 到达 N 层 /
    /// 数值范围 / 只对哪类牌生效）。
    ///
    /// ⚠ V0.2.126 起**效果不在这里解析**（效果是整只遗物唯一那一份，由 <see cref="ParseRelic"/> 解析一次）。
    /// 参数 <paramref name="wholeText"/> / <paramref name="nameToPowerId"/> / <paramref name="result"/> /
    /// <paramref name="cls"/> 现在只为兼容旧调用点保留（老工程里有几处标记写在整个类上）。
    /// </summary>
    private static RelicTriggerSpec? RelicTriggerFromBody(string hook, string kind, string body, string wholeText,
        Dictionary<string, string>? nameToPowerId = null, RecoveryResult? result = null, string cls = "",
        RelicSpec? relic = null)
    {
        // 类型不认识 → 不是触发时机（RelicHookMethods 已经用 EffectCatalog 过滤过一遍，这里再兜一层）
        if (kind.Length == 0) return null;
        if (EffectCatalog.RelicTriggers.All(t => t.Id != kind)) return null;
        var trig = new RelicTriggerSpec { Kind = kind };
        // 「有内容」判据（决定这条时机算不算数）：认出了任何一项配置就算。
        // ⚠ 「只对哪类牌生效」也算 —— 否则「打出一张牌后（只对攻击牌生效）」那种只有筛选、
        //   没有别的配置的时机会被当成空段丢掉。
        bool recognized = false;
        // 「只对哪类牌生效」（用户要求，第 9 批 ④：遗物侧和状态侧同一套候选）：
        //   · 新档（诅咒 / 状态 / 关键词组 / 组）按生成端写的那行 `// CET:CardFilter=<id>` 还原
        //     （判据是展开出来的，从代码反推不回来）；
        //   · 老四档（攻击 / 技能 / 能力）生成的是守卫式判据 `if (cardPlay.Card.Type != CardType.Attack) return;`，
        //     直接从这句反推（和状态侧 ProjectRecovery.SplitCardPlayedBody 同一个口径）。
        if (Match(body, @"// CET:CardFilter=(\S+)") is { } relicCardFilter && relicCardFilter.Length > 0)
        {
            trig.CardFilter = relicCardFilter;
            recognized = true;
        }
        // 老四档（攻击 / 技能 / 能力）直接从守卫那句反推 —— 两种写法都要认：
        //   · 单条时机用的是 `!=`（`if (cardPlay.Card.Type != CardType.Attack) return;`）；
        //   · **多条时机**合并到同一个钩子时守卫被改写成 `==`（`if (… && cardPlay.Card.Type == CardType.Attack)`），
        //     以前只认 `!=` → 合并写法下筛选静默退回「任意」（实测）。
        else if (Match(body, @"cardPlay\.Card\.Type (?:!=|==) CardType\.(\w+)") is { } relicFilterType)
        {
            trig.CardFilter = relicFilterType;
            recognized = true;
        }
        // 盯哪个状态 / 到达 N 层（守卫语句以后改写法就会丢配置，所以生成端单独写了一行标记）
        if (Match(body, @"CET:PowerWatch=(\w*) CET:PowerReach=([\d.]+)") is { } watch && watch.Length > 0)
        {
            trig.PowerId = watch;
            recognized = true;
        }
        // 「某个状态被移除时」只写盯哪个状态（没有「到达 N 层」那一半）——
        // ⚠ 这里**不能**写 `$` 收尾：Match 用的是不带 Multiline 的 Regex，`$` 只认整个文本的末尾。
        else if (Match(body, @"CET:PowerWatch=(\w+)") is { } watchOnly)
        {
            trig.PowerId = watchOnly;
            recognized = true;
        }
        if (Match(body, @"CET:PowerWatch=\w* CET:PowerReach=([\d.]+)") is { } reach
            && decimal.TryParse(reach, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal reachV))
        {
            trig.Amount = reachV;
            recognized = true;
        }
        // 「花掉 N 点」的数值（用户要求）：按生成端那句注释还原（N + 范围）
        if (Match(body, @"数值：每累计花掉 ([\d.]+) 点触发一次（范围：(本回合|本场战斗)）") is { } relicSpend
            && decimal.TryParse(relicSpend, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal relicSpendV))
        {
            trig.Amount = relicSpendV;
            trig.Scope = body.Contains("范围：本场战斗", StringComparison.Ordinal) ? "Combat" : "Turn";
            recognized = true;
        }
        // 触发时机上的数值（每个 N 次 / 数值 ≥ N / 每 N 点）：生成端统一写了
        // `// CET:TriggerAmount=3m CET:TriggerScope=Turn` —— 按标记还原（最稳，守卫改写法也不丢）
        if (Match(body, @"CET:TriggerAmount=([\d.]+)m(?: CET:TriggerScope=(\w+))?") is { } relicAmt
            && decimal.TryParse(relicAmt, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal relicAmtV))
        {
            trig.Amount = relicAmtV;
            string? scope = Match(body, @"CET:TriggerAmount=[\d.]+m CET:TriggerScope=(\w+)");
            if (scope is not null) trig.Scope = scope;
            recognized = true;
        }
        // 「要和其他时机同时成立」（V0.2.122~V0.2.125 的每时机勾选框，和状态页同一个格式）→
        // 关系切成「全部满足才触发」；V0.2.126 的整只遗物关系标记由 RelicTriggerModeFromText 认。
        if (body.Contains("CET:RequireAll=1", StringComparison.Ordinal))
        {
            if (relic is not null) relic.TriggerMode = "All";
            trig.RequireAllTriggers = true;
            string? andScope = Match(body, @"CET:AndScope=(\w+)");
            if (andScope is not null && relic is not null) relic.AndScope = andScope;
            recognized = true;
        }
        // ⚠ 效果**不在这里**解析：V0.2.126 起效果是整只遗物唯一那一份，
        //   由 ParseRelic 从「负责跑效果的那一段」解析一次（见 ParseRelic 的注释）。
        // 这一段里没有任何**值类**配置（盯哪个状态 / 到达 N 层 / 数值 / 只对哪类牌生效 / 老「同时成立」标记）？
        // 那它是不是「一条真的触发时机」就看**钩子名本身**：钩子 → 时机 id 是**一对一**的那些
        // （AfterPlayerTurnStart=玩家回合开始时、AfterCardPlayed=打出一张牌后…）只要认出来就算数。
        // 含糊的那几个（AfterDamageReceived / BeforeSideTurnEnd / AfterPowerAmountChanged…）**故意不列**，
        // 它们靠 body 里的判据/标记（见 RelicHookMethods / RelicTriggerOfHook），所以这里只会漏掉
        // 「含糊钩子 + 一点配置都没填」那种空时机的重复段 —— 那正是我们要丢的。
        if (!recognized && hook.Length > 0
            && string.Equals(RelicTriggerOfHook(hook), kind, StringComparison.Ordinal))
            recognized = true;
        // 一段里什么都没认出来 → 这条不算（避免多出一条空时机）
        return recognized ? trig : null;
    }

    private static PotionSpec ParsePotion(string file, CharacterProfile profile, LocTables loc,
        Dictionary<string, string> nameToPowerId, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = ReadForRecovery(file);      // V0.2.128：锻造节点图那几段方法体整段抠掉（见 ReadForRecovery）
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
        ParseEffects(potion.Effects, BodyOf(text, "OnUse"), vars, nameToPowerId, EffectCtx.Potion, result, cls, result.PetClassNames);
        // 药水自定义描述（和卡牌同一套标记）
        if (ParseCustomDescription(text) is var (pdText, pdReplace))
        {
            potion.CustomDescription = pdText;
            potion.CustomDescriptionReplaces = pdReplace;
        }
        return potion;
    }

    /// <summary>
    /// 认「自定义描述」标记：生成时写的是
    /// <c>// CET:CustomDescReplace=0/1 CET:CustomDescription=&lt;换行转义过的文本&gt;</c>
    /// （卡牌 / 遗物 / 药水三处共用）。没有标记返回 null。
    /// </summary>
    private static (string Text, bool Replaces)? ParseCustomDescription(string text)
    {
        var m = Regex.Match(text, @"CET:CustomDescReplace=(\d) CET:CustomDescription=(.*)$", RegexOptions.Multiline);
        return m.Success
            ? (CSharpCodeGen.UnescapeMarker(m.Groups[2].Value.TrimEnd()), m.Groups[1].Value == "1")
            : null;
    }

    /// <summary>
    /// 认「升级后的自定义描述」标记：<c>// CET:UpgradedCustomDescription=&lt;换行转义过的文本&gt;</c>。
    /// 没有标记返回空串（老工程 / 没填过 → 升级后和上面那段一样）。
    /// </summary>
    private static string ParseUpgradedCustomDescription(string text)
    {
        var m = Regex.Match(text, @"CET:UpgradedCustomDescription=(.*)$", RegexOptions.Multiline);
        return m.Success ? CSharpCodeGen.UnescapeMarker(m.Groups[1].Value.TrimEnd()) : "";
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
    /// 回读召唤物：配置来自 <c>cs/Pet.cs</c>（**每一只**一个 <c>MonsterModel</c> 子类：类名 /
    /// 血量常量 <c>BaseHp</c> / 站位常量 <c>StandDistance</c> / 是否挂了守卫 Power —— 守卫是所有宠物
    /// **共用**的一个类，所以按「这一只的召唤命令里有没有挂它」逐只判定）+ 模组工程里那份
    /// <c>monsters.json</c>（中文名）。
    ///
    /// 为什么必须回读：**不同步改这里就会静默丢配置** —— 用户从工程恢复存档时，
    /// 召唤物的名字 / 血量 / 图片全没了，而卡牌上的「召唤伙伴」效果却还在，一生成就报错。
    ///
    /// 类是**按在文件里出现的顺序**读的（生成时也是按列表顺序写的），所以列表顺序能原样还原。
    /// </summary>
    private static void ParseSummon(string projectDir, string cs, CharacterProfile p, RecoveryResult result)
    {
        string file = Path.Combine(cs, "Pet.cs");
        if (!File.Exists(file)) return;
        string text = File.ReadAllText(file, Encoding.UTF8);

        // 每只召唤物一个 `public sealed class <类名> : MonsterModel` —— 顺便跳过守卫 Power 类
        // （它们继承的是 PowerModel，不会匹配这条正则）。
        var matches = Regex.Matches(text, @"public sealed class (\w+) : MonsterModel");
        if (matches.Count == 0)
        {
            result.Unparsed.Add("cs/Pet.cs 里找不到 `public sealed class X : MonsterModel`（召唤物类名没恢复）");
            return;
        }

        string locDir = Path.Combine(projectDir, p.ModId, "localization");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(locDir))
        {
            foreach (string f in Directory.GetFiles(locDir, "monsters.json", SearchOption.AllDirectories))
            {
                var dict = Json.ReadDict(f);
                foreach (var kv in dict) names[kv.Key] = kv.Value;
            }
        }

        foreach (Match m in matches)
        {
            string cls = m.Groups[1].Value;
            result.PetClassNames.Add(cls);
            // 这一只的类体（到下一个类声明为止）—— 里面的常量只属于它自己
            int start = m.Index;
            int end = text.Length;
            foreach (Match nxt in matches)
                if (nxt.Index > start) { end = nxt.Index; break; }
            string body = text.Substring(start, end - start);

            var spec = new SummonSpec
            {
                Enabled = true,
                ClassName = cls,
                Hp = Int(text: body, pattern: @"private const int BaseHp = (\d+);", fallback: 8),
                // 站位：生成的是 `private const float StandDistance = 110f;`
                StandDistance = (int)Dec(body, @"private const float StandDistance = ([\d.]+)f", SummonSpec.DefaultStandDistance),
            };
            if (spec.StandDistance <= 0) spec.StandDistance = SummonSpec.DefaultStandDistance;

            // 「替主人承伤」：现在**所有勾选的召唤物共用同一个守卫类**（PetGen.GuardianPowerClassName），
            // 判定落在「**这一只自己的召唤命令**里有没有**施加**共用守卫」上 ——
            //   · 不能只查整个文件：那样每只都会读到别只挂的那一行、全部被勾上；
            //   · 不能只查宠物类体：施加语句写在后面的命令助手类里，永远查不到 → 勾选被静默丢掉；
            //   · 也不能只找 `<守卫类>` 这个名字：守卫类自己的仲裁代码里也有
            //     `HasPower<ForgePetGuardianPower>()`，而「列表里最后一只宠物」的命令助手正好在**同一个**
            //     区段里（它后面没有第二个命令助手类了）→ 会被误判成勾了。所以这里认的是 **PowerCmd.Apply<T>**。
            // 另外兼容上一版生成的工程：那时是「每只一个守卫类」`ForgePetGuardian<宠物类名>`，
            // 那种类名只出现在这一只的挂载语句里，所以全文 Contains 就够（这里也含老格式的 Apply）。
            int cmdAt = text.IndexOf("public static class " + cls + "Cmd", StringComparison.Ordinal);
            string cmdBody = "";
            if (cmdAt >= 0)
            {
                int nextCls = text.IndexOf("public static class ", cmdAt + 1, StringComparison.Ordinal);
                cmdBody = nextCls > cmdAt ? text.Substring(cmdAt, nextCls - cmdAt) : text.Substring(cmdAt);
            }
            spec.TakesDamageForOwner =
                (cmdBody.Contains("PowerCmd.Apply<") && cmdBody.Contains(PetGen.GuardianPowerSuffix + ">"))
                || text.Contains("PowerCmd.Apply<" + PetGen.LegacyGuardianPowerClassOf(cls) + ">");

            string entry = EffectCatalog.SlugFor(cls);
            spec.Name = names.GetValueOrDefault(entry + ".name", cls);
            if (string.IsNullOrWhiteSpace(spec.Name)) spec.Name = cls;

            // 宠物图片：生成时放在 images/monsters/<entry 小写>.png
            string png = Path.Combine(projectDir, "images", "monsters", entry.ToLowerInvariant() + ".png");
            if (File.Exists(png)) spec.Image = png;

            p.Summons.Add(spec);
            bool hasScene = File.Exists(Path.Combine(projectDir, "scenes", "creature_visuals",
                entry.ToLowerInvariant() + ".tscn"));
            result.Notes.Add($"召唤物：{spec.Name}（{cls}，生命 {spec.Hp}，站位 {spec.StandDistance}"
                + (spec.TakesDamageForOwner ? "，替主人承伤" : "") + "）"
                + (hasScene ? "，有自定义视觉场景" : "，视觉用本体占位图"));
        }
    }

    /// <summary>
    /// 生成代码里「伙伴攻击」的局部变量名 → 宠物类名。
    /// 变量名规则见 <see cref="CSharpCodeGen.PetAttackVarName"/>：<c>__</c> + 类名首字母小写
    /// （<c>UiCheckPet</c> → <c>__uiCheckPet</c>）。按变量名反推不可靠（首字母大小写会丢信息），
    /// 所以拿工程里**实际存在的类名**来比对，比不出来就返回 null（老工程里的 <c>__pet</c> 就走这条，
    /// 那时只有一只召唤物，生成时会自动用第一只）。
    /// </summary>
    private static string? PetClassOfVar(string varName, IReadOnlyList<string> petClassNames)
    {
        string name = varName.TrimStart('_');
        if (name.Length == 0) return null;
        foreach (string cls in petClassNames)
        {
            if (cls.Length == 0) continue;
            string guess = char.ToLowerInvariant(cls[0]) + cls.Substring(1);
            if (string.Equals(guess, name, StringComparison.Ordinal)
                || string.Equals(cls, name, StringComparison.OrdinalIgnoreCase)) return cls;
        }
        return null;
    }

    private static CustomPowerSpec ParseCustomPower(string file, CharacterProfile profile, LocTables loc, RecoveryResult result)
    {
        string cls = Path.GetFileNameWithoutExtension(file);
        string text = ReadForRecovery(file);      // V0.2.128：锻造节点图那几段方法体整段抠掉（见 ReadForRecovery）
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
        // 每个钩子方法 = 一条触发时机。
        // ⚠ 「某个状态被移除时」不是 override（本体没有那个全局钩子，是工具补丁调过来的，见
        //   CSharpCodeGen.PowerRemovalSource）—— 所以这里连**不带 override** 的 async Task 方法一起收，
        //   认不出来的名字由 TriggerOfHook 返回空、跳过（不会误认）。
        foreach (Match m in Regex.Matches(text, @"public (?:override )?async Task (\w+)\(([^)]*)\)\s*\{"))
        {
            string hook = m.Groups[1].Value;
            string kind = TriggerOfHook(hook);
            if (kind.Length == 0) continue;
            // 自定义状态用的触发时机 id 现在和遗物**同一套**（用户要求两边菜单相同）。
            // 几个特别的：
            //   · AfterCombatEnd 既是「战斗胜利后」也是「战斗结束时（房间结算之前）」→ 看类里那句注释；
            //   · AfterPowerAmountChanged 既是「层数变化后」也是「到达 N 层时」→ 看有没有跨过 N 的判据。
            if (hook == "AfterCombatEnd" && text.Contains("触发时机：战斗结束时（房间结算之前）", StringComparison.Ordinal))
                kind = "CombatEnd";
            if (hook == "AfterPowerAmountChanged"
                && text.Contains("power.Amount - amount <", StringComparison.Ordinal))
                kind = "PowerReached";
            if (PowerTriggers.Find(kind) is null) continue;
            if (kind == "PlayerTurnEnd" && power.Triggers.Any(t => t.Kind == "PlayerTurnEnd")) continue;
            var trigger = new PowerTriggerSpec { Kind = kind };
            if (kind == "CardPlayed" && text.Contains("cardPlay.Card.Type == CardType."))
            {
                string? filter = Match(text, @"cardPlay\.Card\.Type == CardType\.(\w+)");
                if (filter is not null) trigger.CardFilter = filter;
            }
            // 「只对哪类牌生效」新加的那几档（诅咒 / 状态 / 关键词组 / 组，V0.2.118）靠标记行还原 ——
            // 它们的判据是「这一组有哪些牌」展开出来的 ModelId 比较，从代码反推不回来。
            if (kind == "CardPlayed" && Match(text, @"// CET:CardFilter=([^\r\n]+)") is { } cfRaw)
                trigger.CardFilter = cfRaw.Trim();
            // 「某个状态层数变化后 / 到达 N 层时 / 被移除时」盯的是哪个状态
            //（生成时是 if (power is StrengthPower)）+ 到达 N 层的 N
            if (kind is "PowerChanged" or "PowerReached" or "PowerRemoved")
            {
                string? watched = Match(text, @"if \(power is not (\w+)\)")
                    ?? Match(text, @"\(power is (\w+)\)")
                    ?? Match(text, @"if \(power is (\w+)\)");
                if (watched is not null) trigger.PowerId = watched;
                if (text.Contains("HittableEnemies.Contains(power.Owner)")) trigger.PowerTarget = "AnyEnemy";
                string? reach = Match(text, @"power\.Amount >= ([\d.]+)m && power\.Amount - amount <");
                if (reach is not null && decimal.TryParse(reach, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal rv))
                    trigger.TriggerAmount = rv;
            }
            string body = MethodBody(text, hook);
            // 「要和其他时机同时成立」（V0.2.117）：生成时在那条时机的实现里写了
            // `// CET:RequireAll=1 CET:AndScope=Turn` —— 按它还原（重置范围挂在整只状态上）。
            if (body.Contains("CET:RequireAll=1", StringComparison.Ordinal))
            {
                trigger.RequireAllTriggers = true;
                string? andScope = Match(body, @"CET:AndScope=(\w+)");
                if (andScope is not null) power.AndScope = andScope;
            }
            // 触发时机上的数值（每个 N 次 / 数值 ≥ N / 每 N 点）：按生成端那行标记还原
            if (Match(body, @"CET:TriggerAmount=([\d.]+)m(?: CET:TriggerScope=(\w+))?") is { } amtV
                && decimal.TryParse(amtV, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal amtD))
            {
                trigger.TriggerAmount = amtD;
                string? scopeV = Match(body, @"CET:TriggerAmount=[\d.]+m CET:TriggerScope=(\w+)");
                if (scopeV is not null) trigger.TriggerScope = scopeV;
            }
            // 「花掉 N 点」的数值（用户要求）：生成端在那一段前面写了
            // 「// 数值：每累计花掉 N 点触发一次（范围：本回合 / 本场战斗）」——按它还原
            if (Match(text, @"数值：每累计花掉 ([\d.]+) 点触发一次（范围：(本回合|本场战斗)）") is { } spendN
                && decimal.TryParse(spendN, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal spendV))
            {
                trigger.TriggerAmount = spendV;
                trigger.TriggerScope = text.Contains("范围：本场战斗", StringComparison.Ordinal) ? "Combat" : "Turn";
            }
            ParseEffects(trigger.Effects, body, vars, new Dictionary<string, string>(StringComparer.Ordinal), EffectCtx.Power, result, cls);
            // 「打出一张牌后」一个方法里可能有好几条（**不同筛选各一条**，V0.2.118 起筛选档还更多了）——
            // 这时按「筛选判据」把方法体切开、每段各自成一条触发时机；不切的话回读只认第一条，
            // 后面几条会**悄悄丢掉**（用户最烦这种「配置不见了」）。
            var cpSegments = kind == "CardPlayed" ? SplitCardPlayedBody(body) : new List<(string Filter, string Body)>();
            if (cpSegments.Count > 0)
            {
                power.Triggers.Remove(trigger);   // 上面那条只当「模板」用（挂着公共字段），下面按段重建
                foreach (var (segFilter, segBody) in cpSegments)
                {
                    var segTrigger = new PowerTriggerSpec
                    {
                        Kind = "CardPlayed",
                        CardFilter = segFilter,
                        TriggerAmount = trigger.TriggerAmount,
                        TriggerScope = trigger.TriggerScope,
                    };
                    if (segBody.Contains("CET:RequireAll=1", StringComparison.Ordinal))
                        segTrigger.RequireAllTriggers = true;
                    ParseEffects(segTrigger.Effects, segBody, vars, new Dictionary<string, string>(StringComparer.Ordinal), EffectCtx.Power, result, cls);
                    // 方法开头那几行（`if (cardPlay.Player != …) return;` 之类）不是一条时机 ——
                    // 它们会被切进「任意牌」那一段，但一条效果都没有，丢掉（否则会多出一条空时机）。
                    if (segTrigger.Effects.Count == 0 && segFilter == "Any") continue;
                    power.Triggers.Add(segTrigger);
                }
                continue;
            }
            power.Triggers.Add(trigger);
        }
        // 「回合结束就移除自己」：生成端在那行后面写了注释「// 临时状态：回合结束就没了」，按它认最准。
        // ⚠ 衰减那段里也有一句 `await PowerCmd.Remove(this);`（`if (base.Amount <= N) { Remove }`），
        //   光看有没有这句话会把「每回合减 1 层」误认成「挂了就回合结束移除」—— 那是两回事。
        bool hasDecayCode = text.Contains("每回合减", StringComparison.Ordinal)
            || text.Contains("SetAmount(base.Amount -", StringComparison.Ordinal);
        if (text.Contains("临时状态：回合结束就没了", StringComparison.Ordinal)
            || (!hasDecayCode && text.Contains("PowerCmd.Remove(this)", StringComparison.Ordinal)))
            power.RemoveAtTurnEnd = true;
        // 每回合衰减：生成端写的是「// 每回合减 N 层…」+ PowerCmd.ModifyAmount(choiceContext, this, -N m, …)
        // ⚠ 以前这里认的是老写法 SetAmount(base.Amount - N)，改成 ModifyAmount 之后就对不上了
        //   （回读会悄悄丢掉「每回合衰减」，还在界面上留一条「没认出来」）—— 两种都认。
        string? decay = Match(text, @"//\s*每回合减\s*(\d+)\s*层") ?? Match(text, @"SetAmount\(base\.Amount - (\d+)\)");
        if (decay is not null) power.DecayPerTurn = int.Parse(decay, CultureInfo.InvariantCulture);
        string? color = Match(text, @"""AmountColor""\s*=\s*""([0-9A-Fa-f]{6})""");
        if (color is not null) power.AmountColor = color;
        // 「覆盖血条」那条的颜色 / 从哪端开始：生成端在类开头单独写一行
        // // CET:HpBarColor=RRGGBB CET:HpBarSide=Left（它是运行时的补丁文件里用的，
        // 跟本类没有别的字符串联系，不写标记就反推不出来）。旧工程没有 Side 那一项 → 按默认「从右边」。
        string? barColor = Match(text, @"CET:HpBarColor=([0-9A-Fa-f]{6})");
        if (barColor is not null) power.HpBarColor = barColor;
        if (text.Contains("CET:HpBarSide=Left", StringComparison.Ordinal))
            power.HpBarSide = HpBarSides.Left;
        if (power.Triggers.Count == 0 && !power.RemoveAtTurnEnd && power.DecayPerTurn == 0)
            result.Notes.Add($"自定义状态「{power.Name}」没配触发时机（工程里就是这么生成的）");
        return power;
    }

    // ==================== 额外资源量 / 本体状态改写 / 先古之民 ====================

    private static void ParseExtraResource(string projectDir, string cs, CharacterProfile p, LocTables loc, RecoveryResult result,
        Dictionary<string, string> nameToPowerId)
    {
        string file = Path.Combine(cs, "ExtraResource.cs");
        if (!File.Exists(file)) { p.ExtraResource.Enabled = false; return; }
        string text = File.ReadAllText(file, Encoding.UTF8);
        p.ExtraResource.Enabled = true;
        p.ExtraResource.Initial = Int(text, @"InitialAmount = (\d+)", 0);
        p.ExtraResource.CarryOver = text.Contains("CarryOver = true");
        // 「在游戏中隐藏那只载体遗物」（V0.2.127 起补的回读规则）：勾了才会生成 cs/HideCarrierRelic.cs，
        // **文件在 = 勾着**（这个补丁除了这件事没有别的用途，也没有别的路径会写它）。
        p.ExtraResource.HideInGame = File.Exists(Path.Combine(cs, "HideCarrierRelic.cs"));
        // 「始终显示计数器」：勾了才在角色类里写这一句（见 CSharpCodeGen 的 ShouldAlwaysShowStarCounter）。
        {
            string charFile = Path.Combine(cs, p.CharacterClass + ".cs");
            p.ExtraResource.AlwaysShowCounter = File.Exists(charFile)
                && File.ReadAllText(charFile, Encoding.UTF8).Contains("ShouldAlwaysShowStarCounter => true");
        }
        // 「数量上限」：基础值写成 ExtraResourceUiConst.MaxAmount（那个文件只在「配了名字/图标」或
        // 「用得到上限机制」时才生成 —— 填了上限就一定会生成，所以读得到就一定是准的）。
        {
            string uiFile = Path.Combine(cs, "ExtraResourceUi.cs");
            if (File.Exists(uiFile))
                p.ExtraResource.Max = Int(File.ReadAllText(uiFile, Encoding.UTF8), @"MaxAmount = (\d+)", 0);
        }
        // 用户在「遗物」页给这只隐藏起始遗物加的效果：生成时挂在它的 BeforeSideTurnStart（带 choiceContext）上。
        p.ExtraResource.Effects.Clear();
        string extraBody = BodyOf(text, "BeforeSideTurnStart(");
        if (extraBody.Length > 0)
            ParseEffects(p.ExtraResource.Effects, extraBody, ParseVars(text), nameToPowerId, EffectCtx.Relic, result,
                Path.GetFileNameWithoutExtension(file), result.PetClassNames);
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
        // 「自定义描述」（V0.2.127 起补的回读规则）：它写在静态悬停文案表里
        //（`<CharEntry>_EXTRA_RESOURCE.description`）。⚠ 没填时生成的是**工具自动写**的那段
        //（`LocalizationGen.ExtraResourceAutoDescription`）—— 那份**不能**当成「用户填的自定义描述」
        // 回读回去，否则每恢复一次就长出一段自己写的文字。所以：剥掉生成时补的 `[img]…[/img]` 前缀后
        // 和自动文本比，一样就是「没填」，不一样才是用户自己写的。
        {
            string key = $"{Naming.From(p).CharEntry}_EXTRA_RESOURCE.description";
            string stored = (loc.Get(key, "") ?? "").Trim();
            if (stored.Length > 0)
            {
                stored = System.Text.RegularExpressions.Regex.Replace(stored, @"^\[img\][^\[]*\[/img\]", "");
                if (!string.Equals(stored.Trim(), LocalizationGen.ExtraResourceAutoDescription(p), StringComparison.Ordinal))
                    p.ExtraResource.Description = stored.Trim();
            }
        }
        result.Notes.Add($"额外资源量：名字「{p.ExtraResource.Name}」开场 {p.ExtraResource.Initial} 点"
            + (p.ExtraResource.CarryOver ? "（跨战斗继承）" : "（每场重置）")
            + (p.ExtraResource.Max > 0 ? $"、上限 {p.ExtraResource.Max}" : "、不限上限")
            + (p.ExtraResource.AlwaysShowCounter ? "、始终显示计数器" : "")
            + (p.ExtraResource.HideInGame ? "、游戏里隐藏载体遗物" : "")
            + (string.IsNullOrWhiteSpace(p.ExtraResource.Icon) ? "" : "，图标已找回"));
    }

    /// <summary>
    /// 多人联机宝箱房：自定义「拿取」手势 + 自定义催促文本。
    ///
    /// 这两项都在生成的 <c>cs/TreasureGesture.cs</c> 里，但**反推不出来**：
    /// 「拿取」那张图在工程里叫 <c>..._grab.png</c>（本体没有这张图，只有我们写的补丁会用），
    /// 催促文本更是一个写死在常量里的字符串。所以生成时在那份文件开头写了一行标记
    /// （<c>// CET:TreasureGrab=... CET:TreasureFightText=...</c>），这里按标记还原。
    ///
    /// 四张本体手势（选择 / 剪刀 / 石头 / 布）**不在这里还原**：那四个文件在工程里永远存在
    /// （没上传时是占位图），反推不出用户当年传的是哪张 —— 和其它美术槽位一样，
    /// 只有配置里还指着的那份会被 <see cref="CopyAssets"/> 搬到存档旁边。
    /// </summary>
    private static void ParseTreasureGesture(string projectDir, string cs, CharacterProfile p, RecoveryResult result)
    {
        p.Art.TreasureFightText = "";
        p.Art.GestureGrab = null;
        string file = Path.Combine(cs, "TreasureGesture.cs");
        if (!File.Exists(file)) return;
        string text = File.ReadAllText(file, Encoding.UTF8);

        string? fight = Match(text, @"CET:TreasureFightText=([^\r\n]*)");
        if (fight is not null) p.Art.TreasureFightText = CSharpCodeGen.UnescapeMarker(fight.Trim());

        // 标记里写的是 res:// 路径，这里只取 res:// 后面那段（工程内的相对路径）
        string? grab = Match(text, @"CET:TreasureGrab=res://(\S*)");
        if (!string.IsNullOrEmpty(grab))
        {
            string src = Path.Combine(projectDir, grab.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(src)) p.Art.GestureGrab = src;
        }

        var bits = new List<string>();
        if (p.Art.GestureGrab is not null) bits.Add("「拿取」手势已找回");
        if (p.Art.TreasureFightText.Length > 0) bits.Add($"催促文本「{p.Art.TreasureFightText}」");
        result.Notes.Add("联机宝箱房：" + (bits.Count > 0 ? string.Join(" / ", bits) : "只生成了补丁，没有可还原的配置"));
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

    /// <summary>
    /// 回读「诅咒 / 先古卡」某一类的外观：工程里有没有那个外观池文件，以及它的卡框材质名。
    ///   · 材质名 = <c>&lt;角色&gt;_&lt;类&gt;_frame</c> → 是自定义 RRGGBB，颜色从材质文件的 HSV 反算；
    ///   · 别的名字 → 本体框色（card_frame_blue 这种）。
    /// </summary>
    private static void ParseSpecialStyle(CharacterProfile p, string projectDir, string cs,
        string poolClass, SpecialCardStyleSpec style, string stem)
    {
        style.Frame = "";
        style.FrameColor = "";
        string file = Path.Combine(cs, poolClass + ".cs");
        if (!File.Exists(file)) return;
        string? frame = Match(File.ReadAllText(file, Encoding.UTF8), @"CardFrameMaterialPath => ""([^""]+)""");
        if (string.IsNullOrWhiteSpace(frame)) return;
        if (string.Equals(frame, Naming.From(p).SpecialFrameMaterial(stem), StringComparison.Ordinal))
        {
            style.Frame = SpecialCardStyleSpec.CustomFrame;
            style.FrameColor = FrameColorFromMaterial(projectDir, frame) ?? "";
        }
        else
        {
            style.Frame = frame;
        }
    }

    /// <summary>
    /// 从生成出来的**卡框材质**反推颜色。
    ///
    /// 本体的卡框材质是 <c>shaders/hsv.gdshader</c> 的 ShaderMaterial，参数是 <c>h / s / v</c>
    /// （**不是** color），文件在 <c>materials/cards/frames/&lt;名字&gt;_mat.tres</c>。
    ///
    /// 这里以前有两个错：路径少了两层（写成 <c>materials/&lt;名字&gt;_mat.tres</c>）、
    /// 还按 <c>shader_parameter/color</c> 找颜色 —— 所以**从来没读成功过**：
    /// 恢复出来的「自定义边框颜色」会静默退回默认红（D62000）。现在按 h/s/v 反算 RGB。
    /// </summary>
    private static string FrameColorFromMaterial(string projectDir, string frameName)
    {
        string file = Path.Combine(projectDir, "materials", "cards", "frames", frameName + "_mat.tres");
        if (!File.Exists(file)) return null!;
        string text = File.ReadAllText(file, Encoding.UTF8);

        // 老工程 / 别的写法：直接写了 color
        var color = Regex.Match(text, @"shader_parameter/color = Color\(([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\)");
        if (color.Success)
        {
            static string Byte(double v) => ((int)Math.Round(v * 255)).ToString("X2", CultureInfo.InvariantCulture);
            return Byte(double.Parse(color.Groups[1].Value, CultureInfo.InvariantCulture))
                 + Byte(double.Parse(color.Groups[2].Value, CultureInfo.InvariantCulture))
                 + Byte(double.Parse(color.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        // 本体和生成器用的都是 h/s/v
        var hsv = Regex.Match(text,
            @"shader_parameter/h = ([\d.]+)[\s\S]*?shader_parameter/s = ([\d.]+)[\s\S]*?shader_parameter/v = ([\d.]+)");
        if (!hsv.Success) return null!;
        PngUtil.HsvToRgb(
            double.Parse(hsv.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(hsv.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(hsv.Groups[3].Value, CultureInfo.InvariantCulture),
            out double r, out double g, out double b);
        static string Hex(double v) => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture);
        return Hex(r) + Hex(g) + Hex(b);
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
    /// <summary>
    /// 读一个生成出来的类文件，并**把「锻造节点图」那几段方法体整段抠掉**（V0.2.128）。
    ///
    /// 为什么要抠：锻造页那张节点图生成的钩子方法（<c>ForgeGraphOnPlay</c> / <c>AfterFlush</c> /
    /// <c>ForgeGraphOnTrigger</c> / 遗物那个和它自己触发时机同名的钩子…）**和卡牌页的效果钩子是同一批方法名**：
    ///   · 图的时机选「被消耗 / 被丢弃 / 被抽到 / 被保留」时，方法名正好是 <c>AfterCardExhausted</c> /
    ///     <c>AfterFlush</c>…，回读会把图里的代码当成「卡牌时机」的效果去解析；
    ///   · 遗物那边更直接：<c>RelicHookMethods</c> 会把类里**所有**钩子方法都当成一条触发时机。
    /// 节点图的字段这一阶段**不从工程回读**（配置存档里那份还在），所以这些语句一条都不该参与解析 ——
    /// 以前它们会平白留下好几行「没认出来」，让用户以为配置丢了（探针实测 6 条）。
    ///
    /// 认法 = 方法体里有生成端写的 <c>// ===== 锻造节点图</c> 那一行（EmitHook 固定写它）。
    /// 只抠方法体、不动别的：类里其它方法（OnPlay / 触发时机钩子 / OnUpgrade…）逐字保持原样。
    /// </summary>
    private static string ReadForRecovery(string file)
    {
        string text = File.ReadAllText(file, Encoding.UTF8);
        if (!text.Contains("锻造节点图", StringComparison.Ordinal)) return text;
        // 行尾保持原样（生成的类文件是 CRLF；按原约定拼回去，别让回读顺手改了行尾）
        bool crlf = text.Contains("\r\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var keep = new List<string>(lines.Length);
        int i = 0;
        while (i < lines.Length)
        {
            // 方法签名行：`public override async Task X(...)`（生成端把 `{` 写在下一行）
            bool mayBeMethod = lines[i].TrimStart().StartsWith("public ", StringComparison.Ordinal)
                || lines[i].TrimStart().StartsWith("protected ", StringComparison.Ordinal)
                || lines[i].TrimStart().StartsWith("private ", StringComparison.Ordinal)
                || lines[i].TrimStart().StartsWith("internal ", StringComparison.Ordinal);
            if (mayBeMethod && i + 1 < lines.Length && lines[i + 1].Trim() == "{")
            {
                int end = i + 1, depth = 0, j = i + 1;
                for (; j < lines.Length; j++)
                {
                    foreach (char c in lines[j])
                    {
                        if (c == '{') depth++;
                        else if (c == '}') depth--;
                    }
                    if (depth <= 0) { end = j; break; }
                }
                bool forge = false;
                for (int k = i; k <= end && k < lines.Length; k++)
                    if (lines[k].Contains("锻造节点图", StringComparison.Ordinal)) { forge = true; break; }
                if (forge)
                {
                    keep.Add("    // （锻造节点图的代码：这一阶段不从工程回读 —— 配置存档里那份还在）");
                    i = end + 1;
                    continue;
                }
                for (int k = i; k <= end && k < lines.Length; k++) keep.Add(lines[k]);
                i = end + 1;
                continue;
            }
            keep.Add(lines[i]);
            i++;
        }
        return string.Join(crlf ? "\r\n" : "\n", keep);
    }

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

    private static string MethodBody(string text, string hook)
    {
        // 本体钩子是 override；「某个状态被移除时」不是（那是工具补丁调过来的方法，见
        // CSharpCodeGen.PowerRemovalSource）→ 两种写法都认。
        string body = BodyOf(text, "public override async Task " + hook + "(");
        return body.Length > 0 ? body : BodyOf(text, "public async Task " + hook + "(");
    }

    private static string BodyOfHook(string text)
    {
        foreach (Match m in Regex.Matches(text, @"public (?:override )?async Task (\w+)\("))
        {
            string body = MethodBody(text, m.Groups[1].Value);
            if (body.Length > 0) return body;
        }
        return "";
    }

    /// <summary>
    /// 「下回合生成」那几种延迟状态的类名 →（效果种类, 卡类名尾巴）。
    ///
    /// 类名形如 <c>&lt;角色&gt;ForgeDelayedGenShiv</c> / <c>&lt;角色&gt;ForgeDelayedAddGlobalShiv</c>
    /// （见 <see cref="Naming.DelayedPowerSuffix"/>）：**卡类名只能从这段尾巴还原** ——
    /// 卡这边那句 <c>PowerCmd.Apply&lt;…&gt;</c> 上没有卡名（卡名在延迟状态类的**方法体**里，
    /// 而方法体在 cs/DelayedPowers.cs，回读不解析那个文件）。
    /// </summary>
    private static bool DelayedSpawnStem(string power, out string kind, out string tail)
    {
        (string Stem, string Kind)[] map =
        [
            // 长前缀排前面（"ForgeDelayedGen" 是别人的前缀，不能先匹配）
            ("ForgeDelayedTransGlobal", "TransformCardGlobal"),
            ("ForgeDelayedAddGlobal", "AddCardGlobal"),
            ("ForgeDelayedRemoveGlobal", "RemoveCardGlobal"),
            ("ForgeDelayedUpGlobal", "UpgradeCardGlobal"),
            ("ForgeDelayedCopy", "CopyCard"),
            ("ForgeDelayedTrans", "TransformCard"),
            ("ForgeDelayedGen", "GenerateCard"),
        ];
        foreach (var (stem, k) in map)
        {
            int at = power.IndexOf(stem, StringComparison.Ordinal);
            if (at < 0) continue;
            kind = k;
            tail = power[(at + stem.Length)..];
            return true;
        }
        kind = "";
        tail = "";
        return false;
    }

    private static string RelicHookOf(string text) =>        // 「每场战斗只触发一次」的遗物会多写一个 AfterCombatEnd（用来清标记），它不是触发时机，别认成 CombatVictory
        Match(text, @"public override async Task (?!AfterCombatEnd)(\w+)\(") ?? "";

    /// <summary>
    /// 遗物触发时机的「显示名 → id」兜底（钩子含糊时用）。
    ///
    /// 用户要求把遗物 / 状态的触发时机合并成同一套之后，有几条的**显示名改过**
    ///（例：遗物以前的「受到伤害时（每一下）」= 现在和状态一致的「自己受到伤害后（每一下）」）——
    /// 老工程里写的是老名字，所以这里把老名字也认下来（不然老工程回读会丢掉触发时机）。
    /// </summary>
    private static string RelicTriggerOfDisplay(string text)
    {
        var alias = new (string Display, string Id)[]
        {
            ("受到伤害时（每一下）", "DamageReceived"),
            ("造成伤害时（每一下）", "DamageDealt"),
            ("每回合开始时", "PlayerTurnStart"),
            ("每回合结束时", "PlayerTurnEnd"),
            ("战斗胜利时", "CombatVictory"),
            ("每消耗一个能量", "EnergySpent"),
            ("每消耗一个额外资源量", "StarsSpent"),
            ("受到攻击后（连击只算一次）", "Attacked"),
            ("某个状态层数变化时（升或降都触发）", "PowerChanged"),
        };
        foreach (var (display, id) in alias)
            if (text.Contains("// 触发时机：" + display, StringComparison.Ordinal)) return id;
        return EffectCatalog.RelicTriggers
            .FirstOrDefault(t => text.Contains("// 触发时机：" + t.Display, StringComparison.Ordinal))?.Id ?? "";
    }

    /// <summary>
    /// 遗物的触发时机 = 钩子签名 → 时机 id。
    ///
    /// 为什么不能照 <see cref="TriggerOfHook"/> 那样一一对应：合并触发时机之后，**同一个钩子**在遗物这边
    /// 对应多条时机（例：<c>BeforeSideTurnEnd</c> 既是「每回合结束时」也是「敌人回合结束时」；
    /// <c>AfterDamageReceived</c> 既是「受到伤害时（每一下）」也是「敌人受到伤害后」；
    /// <c>AfterPowerAmountChanged</c> 既是「到达 N 层时」也是「层数变化时」）—— 光看钩子分不出来。
    /// 所以这里**只在唯一确定时**给出 id，含糊的一律返回空 → 落到生成器写在类里的那句
    /// `// 触发时机：<显示名>` 注释兜底（见 <see cref="ParseRelic"/>）。
    /// </summary>
    private static string RelicTriggerOfHook(string hook)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AfterObtained"] = "Obtained",
            ["BeforeSideTurnStart"] = "CombatStart",
            ["AfterPlayerTurnStart"] = "PlayerTurnStart",
            ["AfterCombatVictory"] = "CombatVictory",
            ["AfterCombatEnd"] = "CombatEnd",
            ["AfterAttack"] = "Attacked",
            ["AfterGoldGained"] = "GoldGained",
            ["AfterShuffle"] = "Shuffle",
            ["AfterEnergySpent"] = "EnergySpent",
            ["AfterStarsSpent"] = "StarsSpent",
            ["AfterCardGeneratedForCombat"] = "CardGenerated",
            ["AfterCardPlayed"] = "CardPlayed",
            ["AfterCardExhausted"] = "CardExhausted",
            ["AfterCardDrawn"] = "CardDrawn",
            ["AfterCardDiscarded"] = "CardDiscarded",
            ["AfterDeath"] = "EnemyDeath",
            ["AfterSideTurnStart"] = "EnemyTurnStart",
            ["AfterCurrentHpChanged"] = "HpLost",
            // 含糊的（同一个钩子对应多条遗物时机）**故意不列**：
            //   AfterDamageReceived（受到伤害时（每一下） / 敌人受到伤害后）
            //   AfterDamageGiven（造成伤害时（每一下） / 敌人造成伤害后）
            //   AfterBlockGained（自己获得格挡后 / 敌人获得格挡后）
            //   BeforeSideTurnEnd（每回合结束时 / 敌人回合结束时）
            //   AfterPowerAmountChanged（到达 N 层时 / 层数变化时）
            // 这些靠类里那句 `// 触发时机：<显示名>` 注释兜底。
        };
        return map.TryGetValue(hook, out string? id) ? id : "";
    }

    private static string TriggerOfHook(string hook) => hook switch
    {
        // 获得时（拾取时生效的遗物钩子）
        "AfterObtained" => "Obtained",
        "BeforeSideTurnStart" => "CombatStart",
        "AfterPlayerTurnStart" => "PlayerTurnStart",
        "BeforeSideTurnEnd" => "TurnEnd",
        "AfterCombatVictory" => "CombatVictory",
        // 自定义状态的「战斗胜利后」现在生成的是 AfterCombatEnd（本体发战斗奖励的钩子就是它）
        "AfterCombatEnd" => "CombatVictory",
        "AfterDamageReceived" => "DamageReceived",
        // 自己失去生命时（生命值真的少了那一下；本体钩子 AfterCurrentHpChanged，delta < 0）
        "AfterCurrentHpChanged" => "HpLost",
        // 受到攻击后（一次攻击只触发一次的那个钩子）
        "AfterAttack" => "Attacked",
        "AfterGoldGained" => "GoldGained",
        // 抽牌堆打乱洗牌时（本体先古遗物「大～抱抱 BiiigHug」那条钩子）
        "AfterShuffle" => "Shuffle",
        "AfterCardPlayed" => "CardPlayed",
        "AfterCardExhausted" => "CardExhausted",
        "AfterCardDrawn" => "CardDrawn",
        "AfterCardDiscarded" => "CardDiscarded",
        // 「某个状态被移除时」：本体没有那个钩子，生成的是工具补丁调过来的方法（名字固定，见
        // CSharpCodeGen.PowerRemovedMethod）。
        "OnForgePowerRemovedAsync" => "PowerRemoved",
        "AfterDamageGiven" => "DamageDealt",
        "AfterBlockGained" => "BlockGained",
        "AfterDeath" => "EnemyDeath",
        "BeforeCombatStart" => "CombatStart",
        "AfterEnergySpent" => "EnergySpent",
        "AfterStarsSpent" => "StarsSpent",
        // 生成卡牌时（用户要求新增）：本体 AfterCardGeneratedForCombat
        "AfterCardGeneratedForCombat" => "CardGenerated",
        "AfterPowerAmountChanged" => "PowerChanged",
        _ => "",
    };

    private static string? Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// 读一行 <c>// CET:</c> 标记里的「键列表」（生成端用 <c>|</c> 分隔、<c>\n</c> 转义，见
    /// <see cref="CSharpCodeGen.EscapeMarker"/>）。
    /// V0.2.136 用于 <c>CET:UpgradeKeywordAdd</c> / <c>CET:UpgradeKeywordRemove</c>（升级后获得 / 失去的自定义关键词）。
    /// 没有这行标记（老工程）→ 返回空列表、**不报错**（那两栏保持空，和以前逐字一样）。
    ///
    /// ⚠ 值的边界写得刻意保守（<c>[^\r\n]*?</c> 后面跟「下一个 <c>CET:</c> 或行尾」）：
    /// 两个标记常常长在同一行上（<c>// CET:UpgradeKeywordAdd=FATE CET:UpgradeKeywordRemove=VOID</c>），
    /// 而 Add 为空时就是 <c>Add= CET:UpgradeKeywordRemove=VOID</c> —— 贪婪匹配会把后面那个标记整段吃进值里
    /// （实测：恢复出来的「升级后获得」变成了「FATE CET:UpgradeKeywordRemove=VOID」）。
    /// </summary>
    private static List<string> MarkerList(string text, string name)
    {
        string? raw = Match(text, @"CET:" + Regex.Escape(name) + @"=(.*?)(?=\s+CET:|[\r\n]|$)");
        if (raw is null) return new List<string>();
        return CSharpCodeGen.UnescapeMarker(raw.Trim())
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>
    /// 从「选牌」那一句生成代码里认出「从哪里选牌」：
    ///   · 自己选手牌 → <c>CardSelectCmd.FromHand(…)</c> / <c>FromHandForDiscard(…)</c>
    ///   · 别的摞   → <c>CardSelectCmd.FromCombatPile(choiceContext, PileType.X…)</c>（自己选）
    ///                或 <c>NextItem(PileType.X…)</c>（随机抓）
    /// 认不出来返回 null（调用处保留上一次的值 / 退回手牌）。
    /// </summary>
    /// <summary>
    /// 把「打出一张牌后」那个方法体按**筛选判据**切成几段（每段 = 一条触发时机）。
    ///
    /// 为什么要切：同一个方法里可以有好几条「打出一张牌后」（攻击一条 / 技能一条，或者按组筛的各一条），
    /// 以前回读只认第一条、后面几条**悄悄丢掉**（用户最烦这种「配置不见了」）。段首是这两行之一：
    ///   · 老写法：<c>if (cardPlay.Card.Type == CardType.Attack) {</c>
    ///   · 新写法（诅咒 / 状态 / 关键词组 / 组）：<c>// CET:CardFilter=Curse</c> + 紧跟着那句 <c>if (…)</c>
    /// 第一段判据之前那些行（没有筛选、直接跑的效果）归到 <c>Any</c> 那一段。
    /// 只有一段时返回空列表 = 走原来的单条路径（老工程回读行为完全不变）。
    /// </summary>
    private static List<(string Filter, string Body)> SplitCardPlayedBody(string body)
    {
        var starts = new List<(int Index, string Filter)>();
        string[] lines = body.Replace("\r\n", "\n").Split('\n');
        int at = 0;
        string? pendingMarker = null;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            string? marker = Match(line, @"^// CET:CardFilter=(\S+)$");
            if (marker is not null) { pendingMarker = marker; at += raw.Length + 1; continue; }
            if (line.StartsWith("if (cardPlay.Card.", StringComparison.Ordinal))
            {
                string? ty = Match(line, @"cardPlay\.Card\.Type == CardType\.(\w+)");
                starts.Add((at, pendingMarker ?? ty ?? "Any"));
                pendingMarker = null;
            }
            at += raw.Length + 1;
        }
        if (starts.Count == 0) return new List<(string, string)>();
        var segments = new List<(string Filter, string Body)>();
        if (body[..starts[0].Index].Split('\n').Any(l => l.Trim().Length > 0 && !l.Trim().StartsWith("//", StringComparison.Ordinal)))
            segments.Add(("Any", body[..starts[0].Index]));
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1].Index : body.Length;
            segments.Add((starts[i].Filter, body[starts[i].Index..end]));
        }
        return segments.Count > 1 ? segments : new List<(string, string)>();
    }

    private static string? PileFromSelectCode(string line)    {
        string? pile = Match(line, @"FromCombatPile\(choiceContext, PileType\.(\w+)")
            ?? Match(line, @"NextItem\(PileType\.(\w+)");
        if (pile is not null) return pile switch { "Draw" => "Draw", "Discard" => "Discard", "Exhaust" => "Exhaust", "Deck" => "Deck", _ => "Hand" };
        // 「牌顶」（V0.2.115）那一档生成的是「抽牌堆 .FirstOrDefault()」—— 既没有 FromCombatPile 也没有 NextItem，
        // 不看这一条就会被当成默认的「手牌」（回读会丢配置）。两个特征必须**在同一行**上才算数
        //（FirstOrDefault() 到处都是，只有和 PileType.Draw.GetPile 同行才是我们的牌顶取法）。
        if (line.Contains("PileType.Draw.GetPile", StringComparison.Ordinal)
            && (line.Contains("FirstOrDefault()", StringComparison.Ordinal) || line.Contains(".Take(", StringComparison.Ordinal)))
            return "Top";
        if (line.Contains("CardSelectCmd.FromHand", StringComparison.Ordinal)) return "Hand";
        return null;
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

    /// <summary>
    /// 把生成代码里的 <c>System.Math.Abs(内层)</c> 外壳剥掉（负数「获得能量 / 获得金币」生成的就是这层壳），
    /// 剥不掉就原样返回。剥完再交给 FillExpr / FillAmount，就能拿回「-2」这种字面量。
    /// </summary>
    private static string AbsInner(string expr)
    {
        expr = expr.Trim();
        var m = Regex.Match(expr, @"^System\.Math\.Abs\((.+)\)$");
        return m.Success ? m.Groups[1].Value.Trim() : expr;
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
