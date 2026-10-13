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
        // V0.2.153：**先按当前序号把「按类名存的引用」搬一次，再冻结类名**（RenumberCardNames 的注释里
        // 写的就是「必须排在 Freeze 之前」）。不搬的话：引用里存的是上一轮的自动名，而 Freeze 把卡片的
        // 显式类名按**现在的行号**写实 —— 两者对不上，引用就落到同名的另一张卡身上
        //（用户实测报「点击构建后目标卡里的东西会变」）。
        changed |= RenumberCardNames(p, notes) > 0;
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
    /// 卡牌的「自动类名跟着序号走」+ **把按类名存的卡牌引用一起改过来**（V0.2.130 用户要求第 1 条）。
    ///
    /// <para>为什么不能只调 <see cref="Naming.RenumberCardClassNames"/>：卡牌是**唯一一种会被别的模型
    /// 按类名引用**的东西（效果里的「目标卡」<c>SpawnCardId</c>、「转化为先古卡」的类名清单、
    /// 锻造图里的「目标卡」字段）。遗物 / 药水重编号时没有这个问题（没人按类名引用它们）。
    /// 自动名一改号，那些引用就会指向**同名的新主人**（比如原来叫 <c>XCard7</c> 的卡改成 <c>XCard4</c>
    /// 之后，第 7 行那张卡正好也叫 <c>XCard7</c>）或者干脆不存在 —— 必须同一次改掉。
    /// 靠 <see cref="RepairReferences"/> 那张「上一次构建的类名表」是**不够**的：那张表只在上一次
    /// 构建真的落盘之后才对，而 CLI（<c>--build</c>）连着跑两次时存档一个字节都没变（用户没保存），
    /// 表却已经翻到新一轮了 —— 实测就会出现「第一次生成对、第二次生成引用跑到别的卡身上」（不幂等）。</para>
    ///
    /// <para>这一步是**纯内存**变换（不改存档），所以「加载 → 生成」连跑多少次结果都一样：
    /// 每次都从同一份存档出发、算出同一张「旧名 → 新名」表、改出同样的结果。</para>
    /// </summary>
    /// <param name="notes">改过哪些引用（给生成日志用）。</param>
    /// <returns>按新序号重算了几张卡（和 <see cref="Naming.RenumberCardClassNames"/> 的计数一致）。</returns>
    public static int RenumberCardNames(CharacterProfile p, List<string>? notes = null)
    {
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        // ⚠ V0.2.137：**三个列表分别编号**（每页各自 1..N，前缀 Card / Curse / Ancient）——
        // 传 p.AllCards 会退回老口径那种「一条连续编号、只有一个 Card 前缀」。
        var n = Naming.From(p);
        int count = n.RenumberCardClassNames(p.Cards, renamed)
                  + n.RenumberCardClassNames(p.Curses, renamed)
                  + n.RenumberCardClassNames(p.AncientCards, renamed);
        if (renamed.Count > 0) RepairCardClassReferences(p, renamed, notes);
        return count;
    }

    /// <summary>
    /// 把「按卡牌类名存的引用」按 <paramref name="renamed"/>（旧类名 → 新类名）改过来。
    /// 覆盖：效果里的「目标卡」（<c>EffectSpec.SpawnCardId</c>，卡牌 / 遗物 / 药水 / 自定义状态 /
    /// 额外资源量 / 锻造图效果模块都算）、「转化为先古卡」的类名清单、锻造图老字段里的「目标卡」。
    /// </summary>
    private static void RepairCardClassReferences(CharacterProfile p, Dictionary<string, string> renamed, List<string>? notes)
    {
        void Fix(EffectSpec? e, string where)
        {
            if (e is null) return;
            string want = (e.SpawnCardId ?? "").Trim();
            if (want.Length > 0 && renamed.TryGetValue(want, out string? to))
            {
                notes?.Add($"{where}引用的卡牌「{want}」已跟着新序号改成「{to}」");
                e.SpawnCardId = to;
            }
            if (e.AncientRows is not { Count: > 0 }) return;
            foreach (var row in e.AncientRows)
            {
                if (row?.AncientCards is null) continue;
                for (int i = 0; i < row.AncientCards.Count; i++)
                {
                    string oldName = (row.AncientCards[i] ?? "").Trim();
                    if (oldName.Length == 0 || !renamed.TryGetValue(oldName, out string? to2)) continue;
                    notes?.Add($"{where}「转化为先古卡」里引用的「{oldName}」已跟着新序号改成「{to2}」");
                    row.AncientCards[i] = to2;
                }
            }
        }

        foreach (var e in AllEffects(p)) Fix(e, "");
        if (p.ExtraResource?.Effects is { } erEffects)
            foreach (var e in erEffects) Fix(e, "额外资源量遗物的效果");
        // 用户上传的卡面是按**类名**存的（`ArtSpec.CardPortraits[cls]`，见 ArtGenerator 与「上传卡面」）——
        // 自动名改号之后那一条就再也对不上了，卡面会**静悄悄地退回占位图**，或者更糟：
        // 那个名字现在属于**另一张卡**，于是「卡面变成别的卡的图」（用户实测报过）。
        //
        // ⚠ V0.2.156：这里必须**一次性重排**（先算完整张新表再整体换上），不能「边遍历边搬」——
        //   改号是**一张名字的置换**，把某张牌往上拖就会形成**环**（例：[A,B,C] → [B,C,A] 时
        //   A1→A3、A2→A1、A3→A2 是个三元环）。顺序搬 + 「目标已有图就跳过」的写法在环上会把
        //   某几张图的落点搞错（先搬的那条占了后面那条的目标键 → 后面那条被跳过 → 图留在别人身上）。
        //   一次性重排对环天然正确：每个旧键各自搬到它的新键上，互不干扰。
        if (p.Art?.CardPortraits is { Count: > 0 } portraits)
        {
            // 一次性重排：**从零重建**一张新表 —— 每个旧键各自搬到它的新键上。
            // ⚠ 不能「在拷贝上按对 Remove(旧键) / 赋值(新键)」：环上（12→13、13→14、14→1）
            //   先搬的那条已经把值放进了 13，第二条 Remove(13) 会把**刚放进去的那张图**删掉 →
            //   原 12 的图整个丢掉（实测就是这样丢的）。
            var rebuilt = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in portraits)
            {
                string target = renamed.TryGetValue(key, out string? to) ? to : key;
                rebuilt[target] = value;
                if (!string.Equals(target, key, StringComparison.Ordinal))
                    notes?.Add($"上传的卡面（{key}）已跟着新类名挂到「{target}」上");
            }
            bool anyMoved = portraits.Any((KeyValuePair<string, string> kv) => renamed.ContainsKey(kv.Key));
            if (anyMoved)
            {
                portraits.Clear();
                foreach (var (k, v) in rebuilt) portraits[k] = v;
            }
        }
        foreach (var g in ForgeEffectBridge.GraphsOf(p))
        {
            if (g?.Nodes is null) continue;
            foreach (var node in g.Nodes)
            {
                if (node is null) continue;
                Fix(node.EffectSpec, "锻造图【效果】模块");
                // ⚠ 老存档的【效果】模块以 Fields 为事实来源（EffectFromLegacy = true）——
                //   spec 改了不算数，字段也要一起改。
                string f = node.Get("SpawnCardId").Trim();
                if (f.Length > 0 && renamed.TryGetValue(f, out string? to3))
                {
                    notes?.Add($"锻造图【效果】模块里「目标卡」填的「{f}」已跟着新序号改成「{to3}」");
                    node.SetField("SpawnCardId", to3);
                }
            }
        }
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

    /// <summary>
    /// 把「留空的类名」永久写实。
    ///
    /// <para>⚠ V0.2.157：写实的时候**必须避让已经被占用的名字**。自动类名是按「它自己那一页列表的第几位」
    /// 数出来的（<c>&lt;角色&gt;Card37</c>…），而这个号**可能已经属于别人** —— 最典型的是
    /// 「上一轮冻结过的旧名字」：用户删掉前面一张卡之后，原来第 37 位的那张仍然叫 <c>…Card37</c>
    /// （冻结过的名字不跟着位置走），而**新排到第 37 位**的那张空类名卡也会算出 <c>…Card37</c>
    /// → 两张卡同一个类名。本体的模型 ID 只按类名算（忽略命名空间），重名的直接后果就是校验器那条
    /// 「卡牌类名重复：GunnerRainyCard37」把构建拦死（用户实测报过）。</para>
    ///
    /// <para>⚠ 避让用的替代名**不能是「末尾数字加一」**（<c>…Card37</c> → <c>…Card38</c>）：
    /// 那个形状仍然算「工具自动名」（<see cref="Naming.IsAutoCardClassName"/>），下一次生成 / 构建时
    /// <see cref="Naming.RenumberCardClassNames"/> 会把它按**当前位置**改回 <c>…Card37</c>，
    /// 撞名原样复现（校验器那条「长得像自动编号…下次会被改名」的提示说的就是这件事）。
    /// 所以替代名末尾缀一个字母（<c>…Card37X</c>）：认不出是自动名 → 再也不会被改号。</para>
    /// </summary>
    private static bool Freeze(CharacterProfile p, List<string> notes)
    {
        bool changed = false;
        var n = Naming.From(p);

        // 已经被占着的类名：**五种模型一起收集** —— 本体模型库只按类名算 ID，卡牌 / 遗物 / 药水 /
        // 召唤物 / 自定义状态**互相之间**撞名同样会让游戏加载模型库时抛 DuplicateModelException。
        // ⚠ 只收「已经有确定类名」的：留空的那些还没占位（它们等下面几段自己来领）。
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in p.AllCards)
            if (c is not null && Naming.IsValidIdentifier(c.ClassName)) used.Add(n.CardClassName(p, c));
        foreach (var r in p.Relics)
            if (r is not null && Naming.IsValidIdentifier(r.ClassName)) used.Add(r.ClassName!.Trim());
        foreach (var s in p.Potions)
            if (s is not null && Naming.IsValidIdentifier(s.ClassName)) used.Add(s.ClassName!.Trim());
        foreach (var sp in p.Summons)
            if (sp is not null && Naming.IsValidIdentifier(sp.ClassName)) used.Add(sp.ClassName!.Trim());
        foreach (var cp in p.CustomPowers)
            if (cp is not null && Naming.IsValidIdentifier(cp.ClassName)) used.Add(cp.ClassName!.Trim());

        // 领一个名字：想要的那个被占了就缀字母（理由见方法注释里那段「为什么不能加数字」）。
        string Claim(string want)
        {
            if (used.Add(want)) return want;
            for (int i = 1; ; i++)
            {
                string alt = i == 1 ? want + "X" : want + "X" + i.ToString();
                if (used.Add(alt)) return alt;
            }
        }

        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard || Naming.IsValidIdentifier(c.ClassName)) continue;
            string want = n.CardClassName(p, c);
            string cls = Claim(want);
            if (!string.Equals(cls, want, StringComparison.Ordinal))
                notes.Add($"[警告] 卡牌「{c.Name}」算出来的自动类名 {want} 已经被别的模型占着，已改用 {cls}"
                    + "（这张卡如果上传过卡面 / 被别的效果按类名引用过，请在界面上重新选一次）");
            c.ClassName = cls;
            notes.Add($"英文类名已固定：卡牌「{c.Name}」→ {cls}");
            changed = true;
        }
        for (int i = 0; i < p.Relics.Count; i++)
        {
            var r = p.Relics[i];
            if (r is null || Naming.IsValidIdentifier(r.ClassName)) continue;
            string want = n.RelicClassName(r, i);
            string cls = Claim(want);
            if (!string.Equals(cls, want, StringComparison.Ordinal))
                notes.Add($"[警告] 遗物「{r.Name}」算出来的自动类名 {want} 已经被别的模型占着，已改用 {cls}");
            r.ClassName = cls;
            notes.Add($"英文类名已固定：遗物「{r.Name}」→ {cls}");
            changed = true;
        }
        for (int i = 0; i < p.Potions.Count; i++)
        {
            var s = p.Potions[i];
            if (s is null || Naming.IsValidIdentifier(s.ClassName)) continue;
            string want = n.PotionClassName(s, i);
            string cls = Claim(want);
            if (!string.Equals(cls, want, StringComparison.Ordinal))
                notes.Add($"[警告] 药水「{s.Name}」算出来的自动类名 {want} 已经被别的模型占着，已改用 {cls}");
            s.ClassName = cls;
            notes.Add($"英文类名已固定：药水「{s.Name}」→ {cls}");
            changed = true;
        }
        for (int i = 0; i < p.Summons.Count; i++)
        {
            var sp = p.Summons[i];
            if (sp is null || Naming.IsValidIdentifier(sp.ClassName)) continue;
            string want = PetGen.ClassNameOf(p, sp);
            string cls = Claim(want);
            if (!string.Equals(cls, want, StringComparison.Ordinal))
                notes.Add($"[警告] 召唤物「{sp.Name}」算出来的自动类名 {want} 已经被别的模型占着，已改用 {cls}");
            sp.ClassName = cls;
            notes.Add($"英文类名已固定：召唤物「{sp.Name}」→ {cls}");
            changed = true;
        }
        for (int i = 0; i < p.CustomPowers.Count; i++)
        {
            var cp = p.CustomPowers[i];
            if (cp is null || Naming.IsValidIdentifier(cp.ClassName)) continue;
            string want = CustomPowerGen.ClassNameOf(p, cp, i);
            string cls = Claim(want);
            if (!string.Equals(cls, want, StringComparison.Ordinal))
                notes.Add($"[警告] 自定义状态「{cp.Name}」算出来的自动类名 {want} 已经被别的模型占着，已改用 {cls}");
            cp.ClassName = cls;
            notes.Add($"英文类名已固定：自定义状态「{cp.Name}」→ {cls}");
            changed = true;
        }

        // 冻结完还要再拆一次「已经写实的名字之间」的重名 —— 见 DedupeCardClassNames 的注释。
        changed |= DedupeCardClassNames(p, n, Claim, notes);
        return changed;
    }

    /// <summary>
    /// 把**已经有了类名**的卡牌之间重名的那些拆开（V0.2.157 修）。
    ///
    /// <para>为什么「冻结之后」还会重名：自动名是按**稀有度决定的那一页**数位置的
    /// （<see cref="Naming.CardListOf"/>：普通卡 / 诅咒 / 先古各数各的），而一张卡**躺在哪一页**
    /// 是另一回事 —— 一张稀有度写着「普通」、却被放进「诅咒卡」页的卡，数位置时去普通卡页里
    /// 找不到自己（<see cref="Naming.CardRowOf"/> 给 0），而生成端
    /// <see cref="Naming.RenumberCardClassNames"/> 会按**它所在那一页的行号**给它编号
    /// （那一页的第 37 行 → <c>…Card37</c>），于是和**普通卡页第 37 行**那张正牌撞名。
    /// 用户实测报的就是这条：「卡牌类名重复：GunnerRainyCard37」（校验器那条**错误**会拦死构建）。</para>
    ///
    /// <para>拆法：一组重名里保留**「页对得上」**的那张（<see cref="Naming.CardListOf"/> 选中的页里
    /// 真有它 —— 那张的号是按规矩数出来的），其余的（**工具自动名形状**的）改成 <c>…37X</c>
    /// 这种不再被改号的稳定名；**用户手填**的类名一个字符都不动 —— 那种情况校验器会照旧报错，
    /// 让用户自己决定留哪张（工具不去猜他手打的名字是什么意思）。</para>
    /// </summary>
    private static bool DedupeCardClassNames(CharacterProfile p, Naming n, Func<string, string> claim, List<string> notes)
    {
        bool changed = false;
        var groups = new Dictionary<string, List<CardSpec>>(StringComparer.Ordinal);
        foreach (var c in p.AllCards)
        {
            if (c is null || c.IsVanillaCard || !Naming.IsValidIdentifier(c.ClassName)) continue;
            string cls = n.CardClassName(p, c);
            if (!groups.TryGetValue(cls, out var list)) groups[cls] = list = new List<CardSpec>();
            list.Add(c);
        }
        foreach (var (cls, list) in groups)
        {
            if (list.Count < 2) continue;
            // 保留「页对得上」的那张（自动名就是按那一页数出来的）；两张都对页 / 都错页时按列表先后。
            CardSpec keep = list.FirstOrDefault(c => Naming.CardListOf(p, c).Any(x => ReferenceEquals(x, c))) ?? list[0];
            foreach (var c in list)
            {
                if (ReferenceEquals(c, keep)) continue;
                if (!n.IsAutoCardClassName(c.ClassName))
                {
                    notes.Add($"[警告] 卡牌「{c.Name}」的类名 {cls} 和卡牌「{keep.Name}」重复，而这个名字是手填的 —— "
                        + "工具不动手填的类名，请到「卡牌」页把它们改成两个不同的名字。");
                    continue;
                }
                string old = c.ClassName!.Trim();
                string cls2 = claim(cls);      // cls 已被占 → 必然给一个 …X 的替代名
                c.ClassName = cls2;
                notes.Add($"[警告] 卡牌「{c.Name}」的类名 {old} 和卡牌「{keep.Name}」重复"
                    + "（它躺的那一页和稀有度对不上，数位置时数出了同一个号），已改成 " + cls2
                    + "（这张卡如果上传过卡面 / 被别的效果按类名引用过，请在界面上重新选一次）");
                changed = true;
            }
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
            FsSafe.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions
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
        {
            // V0.2.126：遗物的效果是**整只遗物唯一那一份**（触发时机只说明「什么时候触发」）
            if (r?.Effects is not { } list) continue;
            foreach (var e in list) yield return e;
        }
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
