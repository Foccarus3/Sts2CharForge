using System.Collections.ObjectModel;
using System.Text.Json;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>示例配置与 JSON 读写。</summary>
public static class ProfileFactory
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>新建存档的示例配置：在示例内容之外，默认带上初始卡组的「打击 ×5 + 防御 ×5」（见 AddVanillaBasics）。</summary>
    public static CharacterProfile Sample()
    {
        var p = SampleContent();
        AddVanillaBasics(p);
        return p;
    }

    private static CharacterProfile SampleContent() => new()
    {
        ModId = "ExampleMod",
        ModDisplayName = "七 (Seven)",
        Author = "your name",
        Version = "v1.0.0",
        CharacterClass = "Seven",
        DisplayName = "七",
        Description = "一个沉默的剑客。数字就是他的名字，也是他的全部。",
        StartingHp = 80,
        StartingGold = 99,
        Gender = "Masculine",
        DeathText = "……还没到时候。",
        DeadBanterText = "……",
        ArchitectDialogue = "七……一个连名字都懒得给全的数字。你也想给这座塔画上句号？\n七，够了。\n那就让我听听，这个数字能敲出多响的回声。",
        // 注意：这里不再设 StartWithBurningBlood —— 那个开关（以及燃烧之血兜底）已经删掉了
        // 注意：这里不再设 FillPoolsWithIronclad —— 那个开关已废弃，池子里绝不掺本体内容
        Cards =
        {
            new CardSpec
            {
                Name = "七式斩", ClassName = "SevenSlash", CardType = "Attack", Rarity = "Basic", Cost = 0,
                InStartingDeck = true, StartingCopies = 1,
                Effects = { new EffectSpec { Kind = "Damage", Amount = 10, UpgradeAmount = 10, TargetSide = "Enemy" } },
            },
            new CardSpec
            {
                Name = "七的重压", ClassName = "SevenCrush", CardType = "Attack", Rarity = "Common", Cost = 1,
                Effects =
                {
                    new EffectSpec { Kind = "Damage", Amount = 8, UpgradeAmount = 3, TargetSide = "Enemy" },
                    new EffectSpec { Kind = "ApplyPower", PowerId = "VulnerablePower", Amount = 1, UpgradeAmount = 1, TargetSide = "Enemy" },
                },
            },
            new CardSpec
            {
                Name = "七的防线", ClassName = "SevenGuard", CardType = "Skill", Rarity = "Uncommon", Cost = 1,
                Effects =
                {
                    new EffectSpec { Kind = "Block", Amount = 8, UpgradeAmount = 3 },
                    new EffectSpec { Kind = "Draw", Amount = 1, TargetSide = "Self" },
                },
            },
            new CardSpec
            {
                Name = "七的咆哮", ClassName = "SevenRoar", CardType = "Skill", Rarity = "Rare", Cost = 2,
                Effects =
                {
                    new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 2, UpgradeAmount = 1, TargetSide = "AllEnemies" },
                    new EffectSpec { Kind = "ApplyPower", PowerId = "StrengthPower", Amount = 2, UpgradeAmount = 1, TargetSide = "Self" },
                },
            },
            new CardSpec
            {
                Name = "七之架势", ClassName = "SevenStance", CardType = "Power", Rarity = "Rare", Cost = 1,
                Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "StrengthPower", Amount = 2, UpgradeAmount = 1, TargetSide = "Self" } },
            },
            new CardSpec
            {
                Name = "七之乱舞", ClassName = "SevenFlurry", CardType = "Attack", Rarity = "Rare", Cost = 2,
                Effects = { new EffectSpec { Kind = "Damage", Amount = 5, UpgradeAmount = 2, TargetSide = "RandomEnemies", RepeatCount = 4, AllowDuplicates = false } },
            },
            new CardSpec
            {
                Name = "七的预感", ClassName = "SevenForesight", CardType = "Skill", Rarity = "Common", Cost = 1,
                Effects = { new EffectSpec { Kind = "Draw", Amount = 1, UpgradeAmount = 1, TargetSide = "Self", NextTurn = true } },
            },
            new CardSpec
            {
                Name = "七的诅咒", ClassName = "SevenCurse", CardType = "Skill", Rarity = "Uncommon", Cost = 1,
                Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 2, UpgradeAmount = 1, TargetSide = "Enemy", NextTurn = true } },
            },
        },
        Relics =
        {
            new RelicSpec
            {
                Name = "七的护符", ClassName = "SevenCharm", Rarity = "Starter", IsStartingRelic = true,
                Trigger = "CombatStart",
                Effects = { new EffectSpec { Kind = "Block", Amount = 6 } },
            },
            new RelicSpec
            {
                Name = "余烬之心", ClassName = "EmberHeart", Rarity = "Common",
                Trigger = "PlayerTurnStart",
                Effects = { new EffectSpec { Kind = "Heal", Amount = 2 } },
            },
            new RelicSpec
            {
                Name = "猎手之眼", ClassName = "HuntersEye", Rarity = "Rare",
                Trigger = "PlayerTurnStart",
                Effects = { new EffectSpec { Kind = "Draw", Amount = 1, TargetSide = "Self" } },
            },
            new RelicSpec
            {
                Name = "战利之袋", ClassName = "SpoilBag", Rarity = "Uncommon",
                Trigger = "CombatVictory",
                Effects = { new EffectSpec { Kind = "Gold", Amount = 15 } },
            },
        },
        Potions =
        {
            new PotionSpec
            {
                Name = "七之瓶", ClassName = "SevenFlask", Rarity = "Common", Usage = "CombatOnly", TargetType = "AllEnemies",
                Effects = { new EffectSpec { Kind = "ApplyPower", PowerId = "WeakPower", Amount = 2 } },
            },
        },
    };

    /// <summary>
    /// 存档自动备份目录：<c>&lt;存档目录&gt;\_备份</c>。
    /// 每次覆盖保存之前，旧内容都会先复制一份到这里（文件名带时间戳），最多留 30 份。
    /// 为什么要这么做：实测踩过「误覆盖把 20+ 张卡的存档变成另一个存档的内容」，
    /// 当时没有任何历史版本可救，只能靠生成的工程反推。有了这个目录就不会再丢。
    /// </summary>
    public static string BackupFolderOf(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "_备份");

    /// <summary>覆盖保存前留一份旧内容（.bak + _备份\&lt;名字&gt;.&lt;时间戳&gt;.json）。公开版本给「删除存档」用。</summary>
    public static void KeepBackup(string fullPath)
    {
        if (!File.Exists(fullPath)) return;
        try
        {
            File.Copy(fullPath, fullPath + ".bak", true);
            string dir = BackupFolderOf(fullPath);
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(fullPath);
            string stamp = File.GetLastWriteTime(fullPath).ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(dir, $"{name}.{stamp}.json");
            if (!File.Exists(target)) File.Copy(fullPath, target, true);
            TrimBackups(dir, name, 30);
        }
        catch
        {
            // 备份失败不能挡住保存本身
        }
    }

    /// <summary>备份目录只留最近的 N 份（按文件名里的时间戳）。</summary>
    private static void TrimBackups(string dir, string name, int keep)
    {
        var files = new DirectoryInfo(dir).GetFiles(name + ".*.json")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();
        for (int i = keep; i < files.Count; i++)
        {
            try { files[i].Delete(); } catch { }
        }
    }

    public static void Save(CharacterProfile profile, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        profile.SaveName = Path.GetFileNameWithoutExtension(path);   // 工程目录名跟着存档文件名走
        string full = Path.GetFullPath(path);
        KeepBackup(full);
        File.WriteAllText(full, JsonSerializer.Serialize(profile, JsonOpts), new System.Text.UTF8Encoding(false));
    }

    public static CharacterProfile Load(string path)
    {
        var p = JsonSerializer.Deserialize<CharacterProfile>(File.ReadAllText(path), JsonOpts) ?? new CharacterProfile();
        p.SaveName = Path.GetFileNameWithoutExtension(path);
        return Normalize(p);
    }

    /// <summary>
    /// 初始卡组里的「打击 ×5 + 防御 ×5」—— 是**你自己的两张卡**（可以随便改数值 / 升级 / 效果 / 卡面），
    /// 只有英文类名固定成 <c>Strike</c> / <c>Defend</c>。
    ///
    /// 为什么类名用 Strike / Defend 而不是本体的 StrikeIronclad / DefendIronclad：
    /// 本体的 <c>ModelDb</c> 只用类名算 ID（忽略命名空间），自己定义 <c>StrikeIronclad</c> 会和本体撞 ID，
    /// 注册时抛 <c>DuplicateModelException</c>，模组加载当场失败。而 Strike / Defend 这两个裸名字
    /// 本体没有（只有 StrikeIronclad / StrikeSilent… 这些带角色后缀的），所以安全。
    ///
    /// 数值默认照抄本体打击 / 防御（1 费 · 6 伤害 / 5 格挡 · 升级 +3），并且标上本体卡标签
    /// <c>CardTag.Strike</c> / <c>CardTag.Defend</c> —— 本体那些「升级你的初始打击 / 防御」的遗物
    /// （妮欧的护符、叶敷剂、大胶囊、幽灵种子…）是按这个标签找牌的，不标它们就找不到你的牌。
    /// </summary>
    public static void AddVanillaBasics(CharacterProfile p)
    {
        // 顺序：打击（第 1 条）、防御（第 2 条）—— 和本体初始卡组的顺序一致
        foreach (var spec in StartingBasics(p))
        {
            if (p.Cards.Any(c => string.Equals(c.ClassName, spec.ClassName, StringComparison.OrdinalIgnoreCase)))
                continue;
            p.Cards.Add(spec);
        }
        MoveVanillaBasicsToTop(p);
    }

    /// <summary>
    /// 初始打击 / 防御两张卡的「出厂设置」：数值照抄本体（1 费 6 伤害 / 1 费 5 格挡，升级都 +3），
    /// 标上 Strike / Defend 标签，稀有度 Basic（本体那些按「Basic + 标签」查牌的遗物才认）。
    /// 不进奖励池（用户要求：这两张不该出现在奖励 / 商店里）。
    /// </summary>
    public static List<CardSpec> StartingBasics(CharacterProfile p)
    {
        var strike = new CardSpec
        {
            Name = "打击", ClassName = "Strike", CardType = "Attack", Rarity = "Basic", Cost = 1,
            InStartingDeck = true, StartingCopies = 5, InCardPool = true,
            Tags = { "Strike" },
        };
        strike.Effects.Clear();
        strike.Effects.Add(new EffectSpec { Kind = "Damage", Amount = 6m, UpgradeAmount = 3m, TargetSide = "Enemy" });

        var defend = new CardSpec
        {
            Name = "防御", ClassName = "Defend", CardType = "Skill", Rarity = "Basic", Cost = 1,
            InStartingDeck = true, StartingCopies = 5, InCardPool = true,
            Tags = { "Defend" },
        };
        defend.Effects.Clear();
        defend.Effects.Add(new EffectSpec { Kind = "Block", Amount = 5m, UpgradeAmount = 3m, TargetSide = "Self" });

        return new List<CardSpec> { strike, defend };
    }

    /// <summary>本体英文类名 → 我们自己那两张初始牌的类名（老存档迁移用）。</summary>
    private static string? BasicClassNameFor(string? vanillaClassName) => (vanillaClassName ?? "").Trim().ToLowerInvariant() switch
    {
        "strikeironclad" => "Strike",
        "defendironclad" => "Defend",
        _ => null,
    };

    /// <summary>
    /// 把初始卡组的打击 / 防御挪到卡牌列表最前面，并保证「打击在前、防御在后」。
    /// 只重排、不新增 —— 用户如果故意删掉了这两条，打开存档时不会被强行加回来。
    /// </summary>
    public static void MoveVanillaBasicsToTop(CharacterProfile p)
    {
        // 认识三种写法：我们自己的 Strike / Defend、老存档里的本体引用 StrikeIronclad / DefendIronclad
        static int Rank(CardSpec c)
        {
            string cls = (c.ClassName ?? "").Trim();
            if (string.Equals(cls, "Strike", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cls, "StrikeIronclad", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(cls, "Defend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cls, "DefendIronclad", StringComparison.OrdinalIgnoreCase)) return 1;
            return c.IsVanillaCard ? 2 : -1;
        }

        var basics = p.Cards.Where(c => Rank(c) >= 0).ToList();
        if (basics.Count == 0) return;
        if (basics.Count == 1 && ReferenceEquals(p.Cards[0], basics[0])) return;   // 已经在最前面了

        basics = basics.OrderBy(Rank).ToList();
        foreach (var b in basics) p.Cards.Remove(b);
        for (int i = basics.Count - 1; i >= 0; i--) p.Cards.Insert(0, basics[i]);
    }

    /// <summary>
    /// 打开老存档时的兼容处理：
    ///   1) 老版「条件选项」是整张牌一个的，现在改成每条效果各自一份 —— 自动搬到第一条效果上（行为不变）。
    ///   2) 老版「先古之民遗物出现概率」这个功能已经删掉了（改成「替换某一个遗物选项」），
    ///      老配置里的概率数据读不出来（JSON 里那个字段会被忽略），返回的提示里会说一声。
    ///   3) 老版「初始卡组包含 5 张打击 + 5 张防御（本体占位）」是个勾选框，现在改成卡牌列表里
    ///      两条「本体卡」条目 —— 老存档勾了这个开关的话，自动补上这两条（初始卡组内容完全不变）。
    /// </summary>
    public static CharacterProfile Normalize(CharacterProfile p)
    {
        // 自定义关键词：卡牌上的引用统一成「当前的本地化键」（用户改过英文标识后，老引用要跟着走）
        KeywordGen.Normalize(p);

        // 本体关键词改名：界面上是固定 7 行的表格，读老存档（或手写 JSON）时把缺的行补齐
        EnsureKeywordRenameRows(p);

        // 召唤物：老存档只能配一只（存在单个 Summon 对象里）→ 搬进列表；新存档就把每只的字段兜底规整
        NormalizeSummons(p);

        foreach (var card in p.Cards)
        {
            if (card.Condition is null || card.Condition.IsNone) continue;
            if (card.Effects.Count == 0) continue;                      // 没有效果就没有地方挂，留着不动
            if (card.Effects[0].Condition is null || card.Effects[0].Condition.IsNone)
                card.Effects[0].Condition = card.Condition;             // 整张牌一个条件 → 挂到第一条效果上
            card.Condition = new ConditionSpec();                        // 清掉老字段，免得生成两份
        }

        // ===== 初始卡组的打击 / 防御：老存档的各种旧形态统一成「你自己的两张卡」 =====
        // 形态 ①：极老的存档只有那个勾选框（IncludeVanillaStrikeDefend = true）→ 补上两条。
        // 形态 ②：稍早的存档里是「本体卡引用」两条（IsVanillaCard = true，生成时引用 ModelDb.Card<StrikeIronclad>()）
        //         → 就地改成你自己的 Strike / Defend（数值照抄本体打击 / 防御、标上 Strike / Defend 标签），
        //           初始张数和名字保留。为什么要改：引用本体卡时数值改不了，而且本体那些
        //           「升级初始打击 / 防御」的遗物是按 CardTag 找牌的，引用本体卡时对不上你自己的牌。
        // 形态 ②：稍早的存档里是「本体卡引用」两条（IsVanillaCard = true，生成时引用 ModelDb.Card<StrikeIronclad>()）
        //         → 就地改成你自己的 Strike / Defend（数值照抄本体打击 / 防御、标上 Strike / Defend 标签），
        //           初始张数和名字保留。为什么要改：引用本体卡时数值改不了，而且本体那些
        //           「升级初始打击 / 防御」的遗物是按 CardTag 找牌的，引用本体卡时对不上你自己的牌。
        foreach (var card in p.Cards.Where(c => c.IsVanillaCard).ToList())
        {
            string? resolved = BasicClassNameFor(card.ClassName);
            if (resolved is null) continue;                              // 不是打击 / 防御的本体引用：留着不动
            var template = StartingBasics(p).First(b => b.ClassName == resolved);
            card.IsVanillaCard = false;
            card.ClassName = template.ClassName;
            card.CardType = template.CardType;
            card.Rarity = template.Rarity;
            card.Cost = template.Cost;
            card.InCardPool = true;   // 必须在卡池里：本体「巨大扭蛋」用 CardPool.AllCards.First(Basic+Strike/Defend) 找它们，找不到会抛异常把游戏卡住
            card.Tags = new List<string>(template.Tags);
            if (card.Effects.Count == 0)
            {
                card.Effects.Clear();
                foreach (var e in template.Effects) card.Effects.Add(e);
            }
        }
        // 形态 ①：极老的存档只有那个勾选框（IncludeVanillaStrikeDefend = true）→ 补上两条。
        // （放在形态 ② 之后：如果上面刚转换过，这里会因为类名已存在而跳过，不会重复添加。）
        if (p.IncludeVanillaStrikeDefend)
        {
            AddVanillaBasics(p);
            p.IncludeVanillaStrikeDefend = false;                        // 只迁移一次
        }
        // 无论新旧存档：打击 / 防御都排在卡牌列表最上面（只重排、不新增）
        MoveVanillaBasicsToTop(p);
        return p;
    }

    /// <summary>
    /// 召唤物列表的载入规整：
    ///   1) **老存档迁移**：上一版只能配**一只**召唤伙伴，存在单个 <see cref="CharacterProfile.Summon"/> 对象里。
    ///      这里把它搬进 <see cref="CharacterProfile.Summons"/>（列表第 1 条），然后把老字段置成 null。
    ///      为什么只搬「勾了启用而且填了名字」的：老存档里那个对象是 new 出来的默认值也没意义，
    ///      全空的搬过来只会在「召唤物」页里凭空多一条空记录。
    ///   2) 每只的字段兜底：中文名没填就用最终会用的类名（否则宠物名牌上是英文变量名）、
    ///      血量 ≤ 0 按 8（新建存档的默认值；≤ 0 的宠物一上场就是死的）。
    ///
    /// 注意迁移的顺序：类名要按「在列表里的位置」推（留空时第一只叫 &lt;角色类名&gt;Pet、第二只 Pet2），
    /// 所以先按「排在现有条目之后」把老那条的名字算好，再把它加进列表 —— 顺序反了会和生成时算出来的名字对不上。
    /// </summary>
    public static void NormalizeSummons(CharacterProfile p)
    {
        p.Summons ??= new ObservableCollection<SummonSpec>();
        foreach (var s in p.Summons.Where(x => x is not null).ToList())
        {
            if (string.IsNullOrWhiteSpace(s.Name)) s.Name = PetGen.ClassNameOf(p, s);
            if (s.Hp <= 0) s.Hp = 8;
            if (s.StandDistance <= 0) s.StandDistance = SummonSpec.DefaultStandDistance;
        }

        var legacy = p.Summon;
        p.Summon = null;                 // 老字段读完就清掉：留着会让「生成 / 界面」两边各有一套数据
        if (legacy is null) return;
        // 「填过东西」的判定**不能只看 Enabled**：它的默认值就是 true（勾着的），
        // 老存档里那个全空对象照样是 true —— 那样会凭空多出一条空召唤物（列表里多一条、还可能多生成 cs/Pet.cs）。
        // 所以逐个字段和新建对象的默认值比：有一个不一样才算用户真的填过。
        var blank = new SummonSpec();
        bool meaningful = !legacy.Enabled
            || !string.IsNullOrWhiteSpace(legacy.ClassName)
            || !string.IsNullOrWhiteSpace(legacy.Name)
            || !string.IsNullOrWhiteSpace(legacy.Image)
            || legacy.TakesDamageForOwner
            || legacy.Hp != blank.Hp
            || legacy.StandDistance != blank.StandDistance;
        if (!meaningful) return;

        legacy.Enabled = true;           // 老存档有内容就当作启用（上一版默认也是不勾就不生成，这里只搬有内容的）
        if (string.IsNullOrWhiteSpace(legacy.Name)) legacy.Name = PetGen.ClassNameOf(p, legacy);
        if (legacy.Hp <= 0) legacy.Hp = 8;
        if (legacy.StandDistance <= 0) legacy.StandDistance = SummonSpec.DefaultStandDistance;
        p.Summons.Add(legacy);           // 加在列表最后（类名按位置推，所以上面先把名字算好）
    }

    /// <summary>
    /// 「本体关键词改名」在界面上是**固定 7 行**的表格（本体关键词是封闭枚举，不能增删），
    /// 所以载入存档后要把缺的行补齐：按 <see cref="VanillaKeywordCatalog.All"/> 的顺序
    /// 每条关键词一个 <see cref="VanillaKeywordRenameSpec"/>，已经有的（用户填过的）原样保留。
    ///
    /// 为什么要补齐而不是「有才显示」：表格必须始终是 7 行 —— 否则用户会以为本体只有自己填过的那几个关键词；
    /// 而且校验器判「名字撞车」要能拿到全部 7 个关键词的最终显示名。
    /// 原先下标的脏数据（重复 / 不认识的枚举名）也顺手清掉，免得表格里出现看不懂的行。
    /// </summary>
    public static void EnsureKeywordRenameRows(CharacterProfile p)
    {
        p.KeywordRenames ??= new ObservableCollection<VanillaKeywordRenameSpec>();
        var mine = p.KeywordRenames.Where(r => r is not null).ToList();
        p.KeywordRenames.Clear();
        foreach (var entry in VanillaKeywordCatalog.All)
        {
            // 同一个枚举名有多行时以最后一行填的为准（和生成逻辑一致）
            var hit = mine.LastOrDefault(r => string.Equals((r.KeywordId ?? "").Trim(), entry.Id, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
                hit = new VanillaKeywordRenameSpec { KeywordId = entry.Id };
            else if (string.IsNullOrWhiteSpace(hit.VanillaName))
                hit.VanillaName = entry.VanillaName;
            p.KeywordRenames.Add(hit);
        }
    }
}
