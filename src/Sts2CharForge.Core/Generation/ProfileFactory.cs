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
        // V0.2.256（用户报「弹窗 配置解析失败：The JSON value could not be converted to System.String.
        // Path: $.Version」）：存档里某一项本该是**字符串**、却被写成数字 / true / false 时，
        // 以前整份存档直接读不进来（手写一句 `"Version": 1` 就够）。现在宽容一点：
        // 数字 / 布尔按原文当字符串读进来（写出时仍然是带引号的字符串，不改存档格式）。
        Converters = { new LenientStringConverter() },
    };

    /// <summary>
    /// 宽容的 string 转换器（V0.2.256）：读的时候接受 字符串 / 数字 / true / false / null，
    /// 数字和布尔按**原文**变成字符串（`1.5` → "1.5"、`true` → "true"）；写的时候一律写字符串。
    /// 只用来救人（别人手写的存档、复制粘贴掉了引号），不改变我们自己的输出格式。
    /// </summary>
    private sealed class LenientStringConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                // 数字 / true / false / 对象 / 数组：一律拿原文（至少不会整份存档都读不进来）
                _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    /// <summary>新建存档的示例配置：在示例内容之外，默认带上初始卡组的「打击 ×5 + 防御 ×5」（见 AddVanillaBasics）。</summary>
    public static CharacterProfile Sample()
    {
        var p = SampleContent();
        AddVanillaBasics(p);
        // 新建的存档一律按**新版**锻造图端口（不然刚建好 → 保存 → 再打开会被迁移一次）
        p.ForgeGraphPortVersion = ForgeGraphGen.PortVersion;
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
            FsSafe.Copy(fullPath, fullPath + ".bak", true);
            string dir = BackupFolderOf(fullPath);
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(fullPath);
            string stamp = File.GetLastWriteTime(fullPath).ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(dir, $"{name}.{stamp}.json");
            if (!File.Exists(target)) FsSafe.Copy(fullPath, target, true);
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
        // 存档 JSON 也在用户自己的目录里，同样可能被属性 / 占用挡住（保存失败要给人话，见 FsSafe）
        FsSafe.WriteAllText(full, JsonSerializer.Serialize(profile, JsonOpts), new System.Text.UTF8Encoding(false));
    }

    public static CharacterProfile Load(string path)
    {
        var p = JsonSerializer.Deserialize<CharacterProfile>(File.ReadAllText(path), JsonOpts) ?? new CharacterProfile();
        p.SaveName = Path.GetFileNameWithoutExtension(path);
        // 老存档的锻造图要按新版【if】端口迁移一次（条件从右边的插槽挪到左边的入口）——
        // 不做的话「是 / 否」两支会悄悄对调（端口下标变了，见 ForgeGraphGen.MigrateIfPorts）。
        ForgeGraphGen.MigrateIfPorts(p);
        // 老存档的状态触发时机 id 迁到现在的 id（用户要求两边菜单相同后改过三条：
        // TurnStart → PlayerTurnStart、TurnEnd → PlayerTurnEnd、DamageTaken → DamageReceived）——
        // 不迁的话界面上那个下拉会因为「列表里没有这个值」显示成空白。
        foreach (var cp in p.CustomPowers)
            foreach (var t in cp.Triggers)
                t.Kind = Effects.PowerTriggers.Canonical(t.Kind);
        // 用户要求（V0.2.112）：「效果那边只要保留公式、去掉数值框」——支持公式的 5 种效果从此只编辑公式。
        // 老存档里那 5 种的**固定数值**在这里自动转成公式（例：伤害 6 → 公式 "6"），
        // 否则界面上看不到那个数字、用户会以为配置丢了（生成结果不变：同一个数值）。
        MigrateAmountsToFormulas(p);
        // V0.2.146：反过来那一头 ——「非那 5 种」的效果里，公式框填了一个**纯常数**（例：1）的，
        // 折进固定数值（用户会顺手把数字填进公式框，以前会直接被校验器拦下来）。
        FoldPlainConstantAmountFormulas(p);
        return Normalize(p);
    }

    /// <summary>
    /// 把**卡牌**上那 5 种支持公式的效果里「只有固定数值、还没写公式」的那些转成公式
    /// （<c>Amount</c> 原样留着，公式优先）。只动这 5 种；「= X」那几种开关不动（X 是另一套语义）。
    ///
    /// ⚠ 只迁**卡牌**：公式目前只做在卡牌上（遗物 / 药水 / 状态那几条链没有「打出这张牌」的上下文，
    /// 校验器会拦住），那些效果照旧用「数值」框 —— 迁过去会让整次生成报错。
    /// </summary>
    private static void MigrateAmountsToFormulas(CharacterProfile p)
    {
        foreach (var c in p.Cards)
        {
            foreach (var e in c.Effects)
            {
                if (e is null) continue;
                if (!EffectSpec.FormulaSupports(e.Kind)) continue;
                if (e.AmountIsXEffective) continue;                                     // 「数值 = X」：不是固定数值
                if (!string.IsNullOrWhiteSpace(e.AmountFormula)) continue;     // 已经写了公式
                if (e.Amount == 0m) continue;                                  // 0 = 本来就不生效
                e.AmountFormula = e.Amount.ToString("0.############################",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }
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
    /// 把「数值公式」框里写的**纯常数**（例：<c>1</c> / <c>2+3</c> / <c>=1</c>）折进左边的固定数值、清空公式框。
    ///
    /// <para><b>为什么要有这一步</b>（用户报的）：「生成卡牌 / 抽牌 / 消耗…」这些种类的数值必须在**生成期**
    /// 定下来，所以以前会直接报错拦住（「不支持『数值公式』……请清空公式、改用左边的固定数值」）。
    /// 但用户会顺手把 <c>1</c> 填进公式框，看到那句报错只会觉得「填个 1 为什么不行」——
    /// 纯常数本来就能在生成期折出来，于是现在**自动**折好：数值 = 那个常数，公式框清空
    /// （等价于填在左边的固定数值框里，生成结果完全一样）。</para>
    ///
    /// <para>⚠ 只折「不支持完整公式的种类」（<see cref="EffectSpec.FormulaSupports"/> 为 false 的那些）：
    /// 那 5 种在卡牌上**本来就把公式栏当正牌编辑器**（<c>"6"</c> 是常态），折了会和
    /// <see cref="MigrateAmountsToFormulas"/> 来回打架。写 <c>X</c> / <c>M*3</c> / <c>HandCount</c> 的
    /// 一律**原样留着**（那是运行时语义，由校验器按老口径报错拦住）。幂等。</para>
    ///
    /// <returns>折掉了几条（给日志 / 自检用）。</returns>
    /// </summary>
    public static int FoldPlainConstantAmountFormulas(CharacterProfile p)
    {
        int folded = 0;
        foreach (var e in CSharpCodeGen.AllEffects(p))
        {
            if (e is null) continue;
            if (EffectSpec.FormulaSupports(e.Kind)) continue;   // 那 5 种：公式栏是正牌编辑器，一个字都别动
            if (e.AmountIsXEffective) continue;                 // 「数值 = X」：另一套语义
            if (string.IsNullOrWhiteSpace(e.AmountFormula)) continue;
            if (!ForgeNumbers.TryFormulaConst(p, e.AmountFormula, out decimal value)) continue;
            e.AmountFormula = "";
            e.Amount = value;
            folded++;
        }
        return folded;
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
        // V0.2.272（用户报「独立概率生效好像也有的问题」）：存档里出现过「ChancePercent=50 但 ChanceEnabled=false」——
        // 界面上「概率生效」和「概率各自独立」是两栏，只填百分比不勾勾选就整段不生成（看起来就是「概率没生效」）。
        // 读存档时统一归一化：填了 1~99 的百分比、或写了概率公式 → 一律视为开启。
        foreach (var fx in CSharpCodeGen.AllEffects(p))
        {
            if (fx is null || fx.ChanceEnabled) continue;
            bool hasFormula = !string.IsNullOrWhiteSpace(fx.ChanceFormula);
            if (!hasFormula && fx.ChancePercent is <= 0m or >= 100m) continue;
            fx.ChanceEnabled = true;
        }
        // 自定义关键词：卡牌上的引用统一成「当前的本地化键」（用户改过英文标识后，老引用要跟着走）
        KeywordGen.Normalize(p);

        // 本体关键词改名：界面上是固定 7 行的表格，读老存档（或手写 JSON）时把缺的行补齐
        EnsureKeywordRenameRows(p);

        // 召唤物：老存档只能配一只（存在单个 Summon 对象里）→ 搬进列表；新存档就把每只的字段兜底规整
        NormalizeSummons(p);

        // 遗物（V0.2.126，用户要求）：老存档里「效果挂在每一条触发时机上」→ 按顺序合并成遗物唯一那份效果；
        // 勾过「要和其他时机同时成立」的自动切到「全部满足才触发」。见 NormalizeRelicEffects。
        NormalizeRelicEffects(p);

        // 锻造【效果】模块（V0.2.129，用户拍板 Q1=A）：模块上的老字段（ForgeNodeField 那一串）
        // → 幂等迁出一份共用的 EffectSpec（新编辑器读它）。**不回写老字段**，
        // 生成端照旧只读老字段 —— 老存档的生成结果 / 卡面文案逐字不变（见 ForgeEffectBridge）。
        ForgeEffectBridge.Normalize(p);

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
    /// 遗物：把「每条触发时机各自带的效果」合并成**整只遗物唯一的那份效果**（V0.2.126，用户要求）。
    ///
    /// 用户已确认的迁移口径：
    ///   ① 把各条触发时机原有的效果**按顺序**合并进遗物的效果列表；
    ///   ② **完全重复的只留一份**；
    ///   ③ 以前勾过「同时成立」的遗物自动切到「全部满足才触发」（<c>TriggerMode = All</c>），
    ///      重置范围沿用原来的 <c>AndScope</c>；
    ///   ④ 迁移完把时机上的老字段清空 / 置 false —— 新存档不再写它们（但**不能加 [JsonIgnore]**，
    ///      否则老存档里的效果读不进来）。
    ///
    /// <para><b>幂等</b>：合并完时机上的 <c>Effects</c> 就是空的了，第二次跑「没有任何时机带效果」→
    /// 一个字节都不改（<c>RelicSpec.Effects</c> 原样留着）。<c>TriggerMode</c> 也只在检测到
    /// <c>RequireAllTriggers</c> 时才写 <c>All</c>，写完那个标记就是 false 了。</para>
    ///
    /// <para><b>老存档（V0.2.121 及更早，只有一个时机、效果直接挂在遗物上）逐字不变</b>：那种存档的
    /// 效果本来就在遗物级 <c>Effects</c> 里（旧版是「第 1 条时机的代理」），时机上一条效果都没有 →
    /// 这个函数什么都不做：效果顺序 / 内容 / <c>TriggerMode=Any</c> 全都不变。</para>
    ///
    /// <para><b>「完全重复」的判据</b>：用 <see cref="EffectKeyOpts"/> 序列化后的**紧凑 JSON 字符串相等**
    /// （键顺序由类型定义决定、稳定），也就是「存到存档里写出来一模一样」。这样判与存档字面一致，
    /// 而且 <c>[JsonIgnore]</c> 的界面派生属性（Display / IsCardChain 之类）不参与比较 ——
    /// 手写一个字段白名单迟早会和 <c>EffectSpec</c> 以后新加的字段脱节。</para>
    /// </summary>
    public static void NormalizeRelicEffects(CharacterProfile p)
    {
        if (p.Relics is null) return;
        foreach (var r in p.Relics)
        {
            if (r is null || r.Triggers is null) continue;

            var triggerEffects = r.Triggers
                .Where(t => t is not null && t.Effects is { Count: > 0 })
                .SelectMany(t => t.Effects)
                .Where(e => e is not null)
                .ToList();
            if (triggerEffects.Count == 0) continue;      // 新存档 / 极老存档：没有要合并的东西（幂等的关键）

            // 以前勾过「要和其他时机同时成立」→ 关系切到「全部满足才触发」（重置范围沿用原 AndScope）
            bool anyRequireAll = r.Triggers.Any(t => t is not null && t.RequireAllTriggers);

            r.Effects ??= new ObservableCollection<EffectSpec>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in r.Effects) if (e is not null) seen.Add(EffectKey(e));   // 已有的也算一份
            int before = r.Effects.Count;
            foreach (var e in triggerEffects)
            {
                if (!seen.Add(EffectKey(e))) continue;      // 完全重复的只留一份
                r.Effects.Add(e);
            }

            foreach (var t in r.Triggers)
            {
                if (t is null) continue;
                t.Effects ??= new ObservableCollection<EffectSpec>();
                t.Effects.Clear();
                t.RequireAllTriggers = false;
            }
            if (anyRequireAll) r.TriggerMode = "All";
        }
    }

    /// <summary>
    /// 判定「两条效果完全重复」用的键：按存档口径序列化的紧凑 JSON（见 <see cref="NormalizeRelicEffects"/>）。
    /// </summary>
    private static string EffectKey(EffectSpec e) => JsonSerializer.Serialize(e, EffectKeyOpts);

    private static readonly JsonSerializerOptions EffectKeyOpts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
