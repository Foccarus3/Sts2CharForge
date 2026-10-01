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
        p.Art.ModImage = Move(p.Art.ModImage);
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
        string text = File.ReadAllText(file, Encoding.UTF8);
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
        if (text.Contains("ResolveEnergyXValue() + (base.IsUpgraded ? 1 : 0)")) p_xPlus(card);

        var vars = ParseVars(text);
        // ParseEffects 会**消耗** vars（按种类 / 名字取走变量值，避免被后面的效果按顺序捡走），
        // 所以给 ParseUpgrade 一份**独立的副本**：升级增量要靠「变量在 CanonicalVars 里的位置」
        // 反查效果，被消耗过就对不上号了（会变成「升级增量找不到对应效果」）。
        var varsForUpgrade = new List<Var>(vars);
        // 诅咒：效果写在 OnTurnEndInHand 里（不是 OnPlay），而且没有「打出的目标」这一说 ——
        // 语句是照遗物那套生成的，所以按**遗物**的上下文解析（ctx 用 Card 会去认 cardPlay.Target）
        bool curse = card.IsCurseCard;
        ParseEffects(card.Effects, BodyOf(text, curse ? "OnTurnEndInHand" : "OnPlay"), vars, nameToPowerId,
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
        var list = new List<Var>();
        string block = BlockAfter(text, "CanonicalVars =>");
        // **一次按声明顺序扫完** —— 顺序必须和生成代码里一致：升级增量是按「变量在 CanonicalVars 里
        // 的第几个」反查效果的，顺序错了增量就配到别的效果上（自检测出来过）。
        //   · 带名字的普通 DynamicVar（召唤伙伴的 PetHp / PetDamage、强化指定卡牌的 Boost）名字在第一个参数；
        //   · 其它变量（DamageVar / BlockVar / PowerVar<T> …）既可能带名字（同种变量第二次的别名、
        //     牺牲伙伴的收益），也可能不带名字。
        // 以前 DynamicVar 是**先单独扫一遍**、其余再扫一遍，等于把 DynamicVar 全排到最前面 ——
        // 一张牌上同时有「伤害」和「强化指定卡牌」时，两边的升级增量会互换。
        foreach (Match m in Regex.Matches(block, @"new (\w+)Var(?:<(\w+)>)?\((?:""(\w+)"",\s*)?(-?[\d.]+)m?"))
        {
            string kind = m.Groups[1].Value;
            string? power = m.Groups[2].Success ? m.Groups[2].Value : null;
            string? alias = m.Groups[3].Success ? m.Groups[3].Value : null;
            decimal amount = decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (kind == "Dynamic")
                // 带名字的普通 DynamicVar：变量键（名字）放在 PowerId 里，Name 也一起给上（和 VarNameOf 的约定一致）
                list.Add(new Var("DynamicVar", alias, amount, false, false, alias));
            else
                list.Add(new Var(kind, power, amount, kind == "Cards", kind == "Energy", alias));
        }
        return list;
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

        // 「给予卡牌关键词」：生成时写一行 `// CET:GiveKeyword=<本体枚举名 或 custom:键> CET:GiveKeywordTemp=0/1`。
        // 关键词和「是不是临时」只能靠这行标记还原；数值 / 选牌方式 / 哪一摞牌从紧跟着的那几行代码里读，
        // 之后那几行（AddKeyword / 临时 Power / foreach…）全部跳过，免得被当成别的效果。
        bool skipGivenKeywordBody = false;

        // 「自己搞定一整段」的新效果（CET:Effect=…，见 CSharpCodeGen.SelfContainedMarker）：
        // 标记行收尾之后，把它的实现代码整段跳过（不跳的话预见的 CardCmd.Discard 会被当成「丢弃卡牌」、
        // 毒性爆发的 PowerCmd.Apply<PoisonPower> 会被当成「施加中毒」）。
        bool skipSelfContainedBody = false;

        // 「生成 / 变化卡牌」的附加设置（CET:SpawnPick= 标记）：标记行先存着，等真正那条效果出现时配上。
        EffectSpec? pendingSpawn = null;

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

        // 收尾：把「每条效果自己的条件」带上（生成器是用一层 if 包的）
        void Done(EffectSpec e)
        {
            e.Condition = CondTop() ?? new ConditionSpec();
            // 「生成 / 变化卡牌」的附加设置（范围限定 / 升级 / 免费…）：标记行存下来的那几项配到这条效果上
            if (pendingSpawn is not null && e.UsesSpawnOptions)
            {
                e.SpawnPick = pendingSpawn.SpawnPick;
                e.SpawnFilter = pendingSpawn.SpawnFilter;
                e.SpawnChoice = pendingSpawn.SpawnChoice;
                e.SpawnUpgraded = pendingSpawn.SpawnUpgraded;
                e.SpawnFree = pendingSpawn.SpawnFree;
                e.SpawnFreeThisTurn = pendingSpawn.SpawnFreeThisTurn;
                e.SpawnUpgradedThisTurn = pendingSpawn.SpawnUpgradedThisTurn;
                pendingSpawn = null;
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
            if (text.Contains("cardPlay.Target", StringComparison.Ordinal) || text.Contains("target", StringComparison.Ordinal))
                return "Enemy";
            if (text.Contains("foe", StringComparison.Ordinal))
                return randomFoes ? "RandomEnemies" : "AllEnemies";
            return "Self";
        }

        // 「按生命值算」的宠物攻击 / 牺牲伙伴的收益：都用 `base.DynamicVars.CalculatedDamage` /
        // `CalculatedBlock` 取（本体那两个计算变量是固定名字的），所以这里按名字从 calcVars 里捞。
        decimal CalcAmount(string kind) => TakeCalcVar(calcVars, kind) ?? 0m;

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
                    e.Kind = "PetSacrifice";
                    e.PetSacrificeGain = kindId == "PetSacrificeDamage" ? "Damage" : "Block";
                    e.PetSacrificeFormula = formula switch
                    {
                        "fixed" => "Fixed",
                        "curhp" => "CurHp",
                        _ => "MaxHp",
                    };
                    if (pendingPetMultiplier > 0) e.PetSacrificeMultiplier = pendingPetMultiplier;
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
            if (line.StartsWith("//"))
            {
                string? mk = Match(line, @"CET:PetEffect=(\w+)");
                if (mk is not null)
                {
                    pendingPetMarker = line;
                    pendingPetMultiplier = (int)Dec(line, @"CET:PetMul=([\d.]+)", 0);
                }
                // 「全部召唤物」的组号（生成器在每一份展开代码前都写一遍）
                string? allGrp = Match(line, @"CET:PetAll=(\w+)");
                if (allGrp is not null) pendingPetAllGroup = allGrp;
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
                        gk.SelectPile = pileSel switch { "Draw" => "Draw", "Discard" => "Discard", _ => "Hand" };
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
                            _ => "Hand",
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
                        Copies = Math.Max(1, (int)Dec(line, @"CET:Copies=(-?[\d.]+)", 1)),
                    };
                    if (sc.Kind == "Outbreak") sc.TargetSide = "AllEnemies";
                    if (sc.Kind == "TimesUp") sc.TargetSide = "Enemy";
                    // 这几种效果在 CanonicalVars 里也各有一个变量（CardsVar / PowerVar<PoisonPower>），
                    // 这里顺手把它认领掉 —— 否则后面同类型的「抽牌」「施加中毒」会捞到错的那一个。
                    if (sc.Kind is "UpgradeCard" or "Scry" or "CopyCard") NextVar(vars, ref varIdx, "Cards");
                    if (sc.Kind == "Outbreak") NextVar(vars, ref varIdx, "Power:PoisonPower");
                    // 「大限已至」的变量是计算三件套，ParseEffects 开头已经把 calcVars 摘出去了，不用认领
                    ApplyLoop(sc, frames);
                    Done(sc);
                    skipSelfContainedBody = true;
                }
                // 「生成 / 变化卡牌」的范围限定 + 「生成出来的卡怎么处理」：
                // 标记那一行先存成一份「只填了这几个字段」的效果，等真正那条效果出现时再配上（见 ApplySpawnOpts）。
                string? spawnPick = Match(line, @"CET:SpawnPick=(\w+)");
                if (spawnPick is not null)
                {
                    // 范围限定可能是自定义关键词分组（`Keyword:FATE`），所以这里不能只认 \w（冒号会被截断）
                    string? flt = Match(line, @"CET:SpawnFilter=([^\s]+)");
                    string? choice = Match(line, @"CET:SpawnChoice=(\d+)");
                    pendingSpawn = new EffectSpec
                    {
                        SpawnPick = spawnPick,
                        SpawnFilter = flt is null or "-" ? "" : flt,
                        SpawnChoice = choice is not null && int.TryParse(choice, out int ch) && ch > 1 ? ch : 1m,
                        SpawnUpgraded = line.Contains("CET:SpawnUp=1", StringComparison.Ordinal),
                        SpawnFree = line.Contains("CET:SpawnFree=1", StringComparison.Ordinal),
                        SpawnFreeThisTurn = line.Contains("CET:SpawnFreeTurn=1", StringComparison.Ordinal),
                        SpawnUpgradedThisTurn = line.Contains("CET:SpawnUpTurn=1", StringComparison.Ordinal),
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
                    || line.StartsWith("var toUpgrade", StringComparison.Ordinal)
                    || line.EndsWith("CardCmd.Upgrade(c);", StringComparison.Ordinal)
                    || line.StartsWith("CardCmd.Upgrade(pick);", StringComparison.Ordinal)
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
                    // 「卡牌奖励」（非战斗胜利后那种）的实现行：识别行是 CreateForReward 那一行
                    // （所以 __rewardCards / __rewardOptions 那两行**不能**放进这个跳过名单）。
                    || line.Contains("__pickedReward", StringComparison.Ordinal)
                    || line.Contains("__granted", StringComparison.Ordinal);
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
                || line.Contains("AddGeneratedCardToCombat(__gen", StringComparison.Ordinal)
                || line.Contains("__gen.SetToFree", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(__gen)", StringComparison.Ordinal)
                || line.StartsWith("CardCmd.Upgrade(gained);", StringComparison.Ordinal)
                || line.Contains("else CardCmd.Upgrade(__gen)", StringComparison.Ordinal)
                // 「多选1」生成的辅助行：候选列表那行**是**识别行（下面那条 GetDistinctForCombat），
                // 所以这里只跳「判断候选数 / 弹选牌界面 / 拿到选中的那张」这几种固定写法。
                || line.StartsWith("if (__candidates.Count", StringComparison.Ordinal)
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

            if (line.StartsWith("await PowerCmd.Apply<", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"await PowerCmd\.Apply<(\w+)>\(([^;]*)\);");
                if (!m.Success) { result.Unparsed.Add($"{where}: {line}"); continue; }
                string power = m.Groups[1].Value;
                string args = m.Groups[2].Value;
                var parts = SplitArgs(args);
                string target = parts.Count > 1 ? parts[1].Trim() : "";

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
                else if (power.Contains("ForgeDelayed", StringComparison.Ordinal)) { e.NextTurn = true; }
                // 临时增益：打的是我们生成的临时 Power（<角色>ForgeTemp<状态>）——
                // 从类名里把真正的状态名抠回来（前缀 ForgeTemp，后缀 Power），从而认回「临时增益」这一条。
                else if (power.Contains("ForgeTemp", StringComparison.Ordinal))
                {
                    string? real = TempPowerTargetOf(power);
                    if (real is not null)
                    {
                        e.Kind = "TempPower";
                        e.PowerId = real;
                    }
                    else
                        result.Unparsed.Add($"{where}: 临时增益的状态没认出来（{power}）");
                }

                if (e.Kind is "ApplyPower" or "TempPower")
                {
                    // 目标：cardPlay.Target = 单体敌人；HittableEnemies 整串 = 全体；
                    //      foe 出现在「随机挑敌人」的循环里 = 随机敌人，出现在 foreach 里 = 全体
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
            if (line.StartsWith("await CardCmd.Transform(", StringComparison.Ordinal)
                || line.StartsWith("await CardCmd.TransformToRandom(", StringComparison.Ordinal))
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
                    SelectPile = pendingSelectPile is "Draw" or "Discard" ? pendingSelectPile : "Hand",
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

            if (line.StartsWith("await CardCmd.Exhaust(", StringComparison.Ordinal))
            {
                // 「自己选」走 foreach (… in toExhaust)；「随机」走循环 + NextItem(手牌)
                bool chosenExhaust = body.Contains("in toExhaust");
                var e = new EffectSpec
                {
                    Kind = "ExhaustCard",
                    CardPick = chosenExhaust ? "Chosen" : "Random",
                    SelectPile = pendingSelectPile is "Draw" or "Discard" ? pendingSelectPile : "Hand",
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
                    SelectPile = pendingSelectPile is "Draw" or "Discard" ? pendingSelectPile : "Hand",
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
                var e = new EffectSpec { Kind = pile == "Discard" ? "TakeFromDiscard" : "TakeFromDraw" };
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
    /// 从生成的临时 Power 类名（<c>&lt;角色&gt;ForgeTemp&lt;状态&gt;</c>）里把真正的状态名抠回来。
    /// 生成时后缀就是那个状态的类名（例：SparkleForgeTempStrengthPower → StrengthPower），
    /// 所以「ForgeTemp」之后那一段就是 <see cref="EffectSpec.PowerId"/>。
    /// </summary>
    private static string? TempPowerTargetOf(string className)
    {
        const string marker = "ForgeTemp";
        int at = className.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        string raw = className[(at + marker.Length)..].Trim();
        return raw.Length == 0 ? null : raw;
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
        CSharpCodeGen.IsAllPetsInlineCalc(e) ? false : e.Kind switch
    {
        "Damage" or "Block" or "Draw" or "Energy" => e.AmountIsStack == false && !e.AmountIsX,
        // 升级卡牌 / 预见：都用本体的 CardsVar（键 = Cards）
        "UpgradeCard" or "Scry" => !e.AmountIsX,
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

    /// <summary>卡牌条件：从「// 条件：…」注释 + IsPlayable 反推。</summary>
    private static void ParseCardCondition(CardSpec card, string text, Dictionary<string, string> nameToPowerId,
        RecoveryResult result, string where)
    {
        string? comment = Match(text, @"// 条件：(.+)");
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
        ParseEffects(relic.Effects, BodyOfHook(text), vars, nameToPowerId, EffectCtx.Relic, result, cls, result.PetClassNames);
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
        // 获得时（拾取时生效的遗物钩子）
        "AfterObtained" => "Obtained",
        "BeforeSideTurnStart" => "CombatStart",
        "AfterPlayerTurnStart" => "PlayerTurnStart",
        "BeforeSideTurnEnd" => "TurnEnd",
        "AfterCombatVictory" => "CombatVictory",
        // 自定义状态的「战斗胜利后」现在生成的是 AfterCombatEnd（本体发战斗奖励的钩子就是它）
        "AfterCombatEnd" => "CombatVictory",
        "AfterDamageReceived" => "DamageReceived",
        // 受到攻击后（一次攻击只触发一次的那个钩子）
        "AfterAttack" => "Attacked",
        "AfterGoldGained" => "GoldGained",
        // 抽牌堆打乱洗牌时（本体先古遗物「大～抱抱 BiiigHug」那条钩子）
        "AfterShuffle" => "Shuffle",
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

    /// <summary>
    /// 从「选牌」那一句生成代码里认出「从哪里选牌」：
    ///   · 自己选手牌 → <c>CardSelectCmd.FromHand(…)</c> / <c>FromHandForDiscard(…)</c>
    ///   · 别的摞   → <c>CardSelectCmd.FromCombatPile(choiceContext, PileType.X…)</c>（自己选）
    ///                或 <c>NextItem(PileType.X…)</c>（随机抓）
    /// 认不出来返回 null（调用处保留上一次的值 / 退回手牌）。
    /// </summary>
    private static string? PileFromSelectCode(string line)
    {
        string? pile = Match(line, @"FromCombatPile\(choiceContext, PileType\.(\w+)")
            ?? Match(line, @"NextItem\(PileType\.(\w+)");
        if (pile is not null) return pile switch { "Draw" => "Draw", "Discard" => "Discard", "Deck" => "Deck", _ => "Hand" };
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