using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// 英文类名的「**冻结**」+ 失效引用的「**自动修回**」。
///
/// 为什么需要它（用户实测：每次构建都得手工去重排英文类名，否则老是构建失败）：
///   · 卡牌 / 遗物 / 药水 / 召唤物 / 自定义状态在**留空类名**时是按**位置**自动编号的
///     （<c>&lt;角色&gt;Card9</c>、<c>&lt;角色&gt;Relic1</c>、<c>&lt;角色&gt;Pet2</c>…）；
///   · 用户一旦在中间插入 / 删除一条，后面那些「自动编号」的模型就**集体改名**；
///   · 而效果里的引用存的是**类名字符串**（<c>SpawnCardId</c> 目标卡、<c>PetSummon</c> 哪只召唤物、
///     <c>PowerId</c> 自定义状态）—— 于是引用指向了别的模型（静默出错）或直接找不到（构建失败）。
///
/// 两步解决：
///   ① **修引用**：先用上一次构建留下的类名表（<c>&lt;ModId&gt;.forge_classmap.json</c>，
///      写在工程目录的**上一级**，不会被导出进 pck）把失效的引用按「名字」认回来 ——
///      旧类名 → 旧名字 → 现在那个同名的模型 → 写回新的类名；
///   ② **冻结类名**：所有留空的类名一次性写实（存进存档）。以后再加 / 删 / 拖动其它条目，
///      已经固定下来的那些名字都不会再变（占位的空条目仍按老规则自动编号，但不会再影响别人）。
///
/// 这样「每次构建重新排列出来的类名」总能和引用对上，不需要用户手工重排。
/// </summary>
public static class NameStabilizer
{
    /// <summary>类名表文件名（写在哪见 <see cref="MapPathOf"/>）。</summary>
    public const string MapFileNameSuffix = ".forge_classmap.json";

    /// <summary>这次做了哪些修复 / 冻结（给生成日志用）。</summary>
    public sealed record Report(bool Changed, List<string> Notes);

    private sealed class ClassMap
    {
        public int Version { get; set; } = 1;
        /// <summary>类名 → 当时的显示名（用来把失效引用按名字认回来）。</summary>
        public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 对一份配置做「修引用 + 冻结类名」，并写下来更新后的类名表。
    /// <paramref name="projectRoot"/> 为空时跳过读写类名表（只做冻结）。
    /// </summary>
    public static Report Stabilize(CharacterProfile p, string? projectRoot = null)
    {
        var notes = new List<string>();
        string modId = Naming.From(p).ModId;
        ClassMap old = LoadMap(projectRoot, modId);
        bool changed = RepairReferences(p, old, notes);
        // 自定义关键词有一模一样的问题：英文标识留空时按位置自动编号（KEYWORD_1 / KEYWORD_2…），
        // 中间插入 / 删除一条后面就集体改名 —— 而卡牌上的「自定义关键词」、效果里的
        // 「给予卡牌关键词」和「范围限定 = 自定义关键词那一组（Keyword:XXX）」存的全是这个键。
        changed |= RepairKeywords(p, old, notes);
        changed |= Freeze(p, notes);
        changed |= FreezeKeywords(p, notes);
        // **每次都写**类名表（不只是这次改了名字才写）：下一次构建要靠它把「引用的是哪个模型」认回来，
        // 所以哪怕这次一个名字都没固定（用户全填了类名），这张表也必须是最新的。
        try { SaveMap(projectRoot, p, modId); }
        catch (Exception ex) { notes.Add($"[警告] 英文类名表写入失败（下次构建只能靠「不存在」判断引用）：{ex.Message}"); }
        return new Report(changed, notes);
    }

    /// <summary>
    /// 类名表的路径：工程目录的**上一级**（那里不会被 Godot 导出进 pck），文件名按 **ModId** 取 ——
    /// 不按工程目录名（= 存档名）：存档改个名、或者同一份配置另存一份，类名表也要跟着一起用，
    /// 不然「引用修不回来」就会又变成手工重排。
    /// </summary>
    public static string? MapPathOf(string? projectRoot, string? modId = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) return null;
        string full;
        try { full = Path.GetFullPath(projectRoot); } catch { return null; }
        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent)) return null;
        string stem = string.IsNullOrWhiteSpace(modId) ? Path.GetFileName(full) : modId!.Trim();
        return Path.Combine(parent, stem + MapFileNameSuffix);
    }

    // ==================== ① 修引用 ====================

    private static bool RepairReferences(CharacterProfile p, ClassMap old, List<string> notes)
    {
        var n = Naming.From(p);
        bool changed = false;

        // 现在可用的类名（只看我们自己的模型）
        var cardByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // 当前类名 → 我们的卡
        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard) continue;
            cardByName[n.CardClassName(p, c)] = c.Name;
        }
        var pets = PetGen.All(p).ToList();
        var powers = new List<(string Class, string Name)>();
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (cp is null) continue;
            powers.Add((CustomPowerGen.ClassNameOf(p, cp, i), (cp.Name ?? "").Trim()));
        }

        foreach (var e in AllEffects(p))
        {
            if (e is null) continue;

            // 召唤物：先按类名表认「这个名字原来是哪只」，再退回「Pet2 → 第 2 只」「只剩一只就是它」
            if (!string.IsNullOrWhiteSpace(e.PetSummon) && !PetGen.IsAll(e.PetSummon))
            {
                var hit = MatchPet(pets, old, e.PetSummon);
                if (hit is not null && !string.Equals(hit.ClassName, e.PetSummon!.Trim(), StringComparison.Ordinal))
                {
                    notes.Add($"引用的召唤物「{e.PetSummon!.Trim()}」已自动改成「{hit.DisplayName}」（{hit.ClassName}）");
                    e.PetSummon = hit.ClassName;
                    changed = true;
                }
            }

            // 目标卡 / 强化目标：类名表里那个名字现在指的是**别的**卡（中间插 / 删过）→ 改回原来那张；
            // 没有类名表（升级后第一次构建）时只能靠「这个名字已经不存在」来判断。
            if (!string.IsNullOrWhiteSpace(e.SpawnCardId))
            {
                var hit = MatchCard(p, n, old, e.SpawnCardId!, cardByName);
                if (hit is not null && !string.Equals(n.CardClassName(p, hit), e.SpawnCardId!.Trim(), StringComparison.Ordinal))
                {
                    string cls = n.CardClassName(p, hit);
                    notes.Add($"引用的卡牌「{e.SpawnCardId!.Trim()}」已自动改成「{hit.Name}」（{cls}）");
                    e.SpawnCardId = cls;
                    changed = true;
                }
            }

            // 自定义状态：本体状态 / 不认识的类名不动，只修「上一次是我们自己的那个」
            if (!string.IsNullOrWhiteSpace(e.PowerId) && !EffectCatalog.IsCustomPower(e.PowerId))
            {
                var hit = MatchPower(powers, old, e.PowerId!);
                if (hit is { } hp && !string.Equals(hp.Class, e.PowerId!.Trim(), StringComparison.Ordinal))
                {
                    notes.Add($"引用的状态「{e.PowerId!.Trim()}」已自动改成「{hp.Name}」（{hp.Class}）");
                    e.PowerId = hp.Class;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// 这个「目标卡」引用现在应该指向哪张卡：
    ///   ① 类名表里记着它当时的名字 → 现在那张**同名**的卡（中间插 / 删导致编号变了时靠这条纠正）；
    ///   ② 否则：这个名字现在还能对上我们自己的卡 / 本体卡 → 不动（返回那张 / null）；
    ///   ③ 否则：这个名字已经完全不存在了 → 表里也没有 → 返回 null（交给校验器报错，让用户重新选）。
    /// </summary>
    private static CardSpec? MatchCard(CharacterProfile p, Naming n, ClassMap old, string reference,
        Dictionary<string, string> ourCards)
    {
        string want = reference.Trim();
        if (old.Names.TryGetValue(want, out string? oldName))
        {
            var byName = p.AllCards.FirstOrDefault(c => c is not null && !c.IsVanillaCard
                && string.Equals((c.Name ?? "").Trim(), oldName.Trim(), StringComparison.Ordinal));
            if (byName is not null) return byName;
        }
        if (ourCards.TryGetValue(want, out string? ourName))
            return p.AllCards.FirstOrDefault(c => c is not null && !c.IsVanillaCard
                && string.Equals((c.Name ?? "").Trim(), ourName.Trim(), StringComparison.Ordinal));
        return null;
    }

    private static PetGen.PetDef? MatchPet(IReadOnlyList<PetGen.PetDef> pets, ClassMap old, string reference)
    {
        if (pets.Count == 0) return null;
        string want = reference.Trim();
        // ① 上一次构建的名字对得上（这一条同时管「还在」和「已经改名」两种情况）
        if (old.Names.TryGetValue(want, out string? oldName))
        {
            var byName = pets.FirstOrDefault(d => string.Equals(d.DisplayName.Trim(), oldName.Trim(), StringComparison.Ordinal));
            if (byName is not null) return byName;
        }
        // ② 现在这个名字还能直接对上 → 不动
        var direct = pets.FirstOrDefault(d => string.Equals(d.ClassName, want, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;
        // ③ 「<角色>Pet / <角色>Pet2 …」这种自动编号 → 按序号认（第 1 只 / 第 2 只…）
        var m = System.Text.RegularExpressions.Regex.Match(want, @"Pet(\d*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            int idx = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
            if (idx >= 1 && idx <= pets.Count) return pets[idx - 1];
        }
        // ④ 只剩一只启用着 → 就是它（老存档只有一只召唤物时的行为）
        return pets.Count == 1 ? pets[0] : null;
    }

    private static (string Class, string Name)? MatchPower(List<(string Class, string Name)> powers, ClassMap old, string reference)
    {
        string want = reference.Trim();
        if (old.Names.TryGetValue(want, out string? oldName))
        {
            var byName = powers.FirstOrDefault(x => string.Equals(x.Name, oldName.Trim(), StringComparison.Ordinal));
            if (byName.Name is not null) return byName;
        }
        var direct = powers.FirstOrDefault(x => string.Equals(x.Class, want, StringComparison.OrdinalIgnoreCase));
        return direct.Name is null ? null : direct;
    }



    // ==================== ② 冻结类名 ====================

    private static bool Freeze(CharacterProfile p, List<string> notes)
    {
        bool changed = false;
        var n = Naming.From(p);
        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard || Naming.IsValidIdentifier(c.ClassName)) continue;
            c.ClassName = n.CardClassName(p, c);
            notes.Add($"英文类名已固定：卡牌「{c.Name}」→ {c.ClassName}");
            changed = true;
        }
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            if (r is null || Naming.IsValidIdentifier(r.ClassName)) continue;
            r.ClassName = n.RelicClassName(r, i);
            notes.Add($"英文类名已固定：遗物「{r.Name}」→ {r.ClassName}");
            changed = true;
        }
        for (int i = 0; i < p.Potions.Count; i++)
        {
            var s = p.Potions[i];
            if (s is null || Naming.IsValidIdentifier(s.ClassName)) continue;
            s.ClassName = n.PotionClassName(s, i);
            notes.Add($"英文类名已固定：药水「{s.Name}」→ {s.ClassName}");
            changed = true;
        }
        for (int i = 0; i < p.Summons.Count; i++)
        {
            var sp = p.Summons[i];
            if (sp is null || Naming.IsValidIdentifier(sp.ClassName)) continue;
            sp.ClassName = PetGen.ClassNameOf(p, sp);
            notes.Add($"英文类名已固定：召唤物「{sp.Name}」→ {sp.ClassName}");
            changed = true;
        }
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (cp is null || Naming.IsValidIdentifier(cp.ClassName)) continue;
            cp.ClassName = CustomPowerGen.ClassNameOf(p, cp, i);
            notes.Add($"英文类名已固定：自定义状态「{cp.Name}」→ {cp.ClassName}");
            changed = true;
        }
        return changed;
    }

    // ==================== ③ 自定义关键词：修引用 + 冻结键 ====================

    /// <summary>
    /// 自定义关键词的引用修回：卡牌上勾的「自定义关键词」、效果里的「给予卡牌关键词」、
    /// 以及「范围限定 = 自定义关键词那一组」（<c>Keyword:XXX</c>）存的全是**键**。
    /// 键留空时是按位置自动编号的（<c>KEYWORD_1</c>…），所以中间插入 / 删除一条会让后面的键集体改名，
    /// 老引用要么失效（校验器报「引用了不存在的自定义关键词」）要么指到别的关键词上（静默串组）。
    /// 这里按「上一次构建的名字 → 现在那个同名的关键词」认回来；认不到再看 <c>KEYWORD_&lt;n&gt;</c> 的序号。
    /// </summary>
    private static bool RepairKeywords(CharacterProfile p, ClassMap old, List<string> notes)
    {
        var all = KeywordGen.All(p);
        if (all.Count == 0) return false;
        bool changed = false;

        string? Repair(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            string want = reference!.Trim();
            static bool Same(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

            // ① **先看类名表**：这个键上一次构建时叫什么名字 → 现在那个同名的关键词的键。
            //    为什么不先问「现在还能不能认出来」：键被重排之后 `KEYWORD_2` 依然「认得出来」，
            //    只是它已经变成**另一个关键词**了（静默串组）—— 只能靠名字判断「还是不是同一个」。
            if (old.Names.TryGetValue(want, out string? oldName))
            {
                foreach (var (spec, key) in all)
                {
                    if (!Same(KeywordGen.DisplayName(spec, key), oldName)) continue;
                    return string.Equals(key, want, StringComparison.OrdinalIgnoreCase) ? null : key;
                }
            }
            // ② 现在认得出来（用户手填的键 / 中文名 / 本体关键词）→ 不动
            if (KeywordGen.Find(p, want) is not null) return null;
            // ③ 「KEYWORD_3」这种自动编号 → 第 3 个关键词（和 KeywordGen.Normalize 的老规则一致）
            var m = System.Text.RegularExpressions.Regex.Match(want, @"^KEYWORD_(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int idx) && idx >= 1 && idx <= all.Count)
                return all[idx - 1].Key;
            return null;
        }

        foreach (var c in p.AllCards)
        {
            if (c?.KeywordIds is null || c.KeywordIds.Count == 0) continue;
            for (int i = 0; i < c.KeywordIds.Count; i++)
            {
                string? fixedKey = Repair(c.KeywordIds[i]);
                if (fixedKey is null) continue;
                notes.Add($"卡牌「{c.Name}」引用的自定义关键词「{c.KeywordIds[i]}」已自动改成「{fixedKey}」");
                c.KeywordIds[i] = fixedKey;
                changed = true;
            }
        }
        foreach (var e in AllEffects(p))
        {
            if (e is null) continue;
            string? fixedKey = Repair(e.GivenKeyword);
            if (fixedKey is not null)
            {
                notes.Add($"「给予卡牌关键词」引用的「{e.GivenKeyword}」已自动改成「{fixedKey}」");
                e.GivenKeyword = fixedKey;
                changed = true;
            }
            // 范围限定 = 自定义关键词那一组（Keyword:XXX）
            if (EffectCatalog.IsKeywordFilter(e.SpawnFilter))
            {
                string? key = EffectCatalog.KeywordFilterKey(e.SpawnFilter);
                string? fixedFilterKey = Repair(key);
                if (fixedFilterKey is not null)
                {
                    notes.Add($"「范围限定」选的自定义关键词「{key}」已自动改成「{fixedFilterKey}」");
                    e.SpawnFilter = EffectCatalog.KeywordFilterPrefix + fixedFilterKey;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>把留空的自定义关键词「英文标识」写实（<c>KEYWORD_1</c>…），以后插删别的关键词都不会改名。</summary>
    private static bool FreezeKeywords(CharacterProfile p, List<string> notes)
    {
        bool changed = false;
        var all = KeywordGen.All(p);
        for (int i = 0; i < all.Count; i++)
        {
            var spec = all[i].Spec;
            if (Naming.IsValidIdentifier(spec.Key)) continue;   // 已经是合法标识符（用户填了），不动
            string key = all[i].Key;
            spec.Key = key;
            notes.Add($"关键词的英文标识已固定：「{KeywordGen.DisplayName(spec, key)}」→ {key}");
            changed = true;
        }
        if (changed) KeywordGen.Normalize(p);   // 顺手把卡牌上的引用规整成这些键
        return changed;
    }

    // ==================== 类名表读写 ====================

    private static ClassMap LoadMap(string? projectRoot, string? modId)
    {
        string? path = MapPathOf(projectRoot, modId);
        if (path is null || !File.Exists(path)) return new ClassMap();
        try
        {
            var map = JsonSerializer.Deserialize<ClassMap>(File.ReadAllText(path, Encoding.UTF8));
            return map ?? new ClassMap();
        }
        catch { return new ClassMap(); }
    }

    private static void SaveMap(string? projectRoot, CharacterProfile p, string? modId)
    {
        string? path = MapPathOf(projectRoot, modId);
        if (path is null) throw new InvalidOperationException("拿不到类名表路径（工程目录为空）");
        {
            var map = new ClassMap();
            foreach (var c in p.AllCards)
            {
                if (c is null || c.IsVanillaCard) continue;
                map.Names[Naming.From(p).CardClassName(p, c)] = (c.Name ?? "").Trim();
            }
            foreach (var r in p.Relics)
                if (r is not null) map.Names[Naming.From(p).RelicClassName(r, p.Relics.IndexOf(r))] = (r.Name ?? "").Trim();
            foreach (var s in p.Potions)
                if (s is not null) map.Names[Naming.From(p).PotionClassName(s, p.Potions.IndexOf(s))] = (s.Name ?? "").Trim();
            foreach (var (spec, def) in PetGen.All(p).Select(d => (d.Spec, d)))
                map.Names[def.ClassName] = (def.DisplayName ?? "").Trim();
            for (int i = 0; i < p.CustomPowers.Count; i++)
                if (p.CustomPowers[i] is { } cp) map.Names[CustomPowerGen.ClassNameOf(p, cp, i)] = (cp.Name ?? "").Trim();
            // 自定义关键词：键 → 名字（下次构建靠它把失效的引用按名字认回来）
            foreach (var (spec, key) in KeywordGen.All(p))
                map.Names[key] = (KeywordGen.DisplayName(spec, key) ?? "").Trim();

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);   // 输出目录可能还没建（第一次构建）
            // 中文不转义（\uXXXX）：这份表是给「下次构建修引用」用的，出了问题用户也可能要打开看一眼
            File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }), new UTF8Encoding(false));
        }
    }

    /// <summary>存档里所有效果（卡牌 / 诅咒 / 先古卡 / 遗物 / 药水 / 自定义状态触发器）。</summary>
    private static IEnumerable<EffectSpec> AllEffects(CharacterProfile p)
    {
        foreach (var c in p.AllCards)
            if (c?.Effects is { } list)
                foreach (var e in list) yield return e;
        foreach (var r in p.Relics)
            if (r?.Effects is { } list)
                foreach (var e in list) yield return e;
        foreach (var s in p.Potions)
            if (s?.Effects is { } list)
                foreach (var e in list) yield return e;
        foreach (var cp in p.CustomPowers)
        {
            if (cp?.Triggers is null) continue;
            foreach (var t in cp.Triggers)
                if (t?.Effects is { } list)
                    foreach (var e in list) yield return e;
        }
    }
}
