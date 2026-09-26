using System.Text.Json;

namespace Sts2CharForge.Core.Effects;

/// <summary>先古之民对话里的一句（本体给「任意角色」写的那版，只当参考）。</summary>
public sealed record AncientSlotLine(int LineIndex, bool Repeating, string VanillaAncient, string VanillaChar);

/// <summary>本体给「任意角色（ANY）/ 首次到访」写的一段对话（我们自己写的对话不受它限制，只当参考）。</summary>
public sealed record AncientSlot(int DialogueIndex, string Scope, IReadOnlyList<AncientSlotLine> Lines)
{
    public bool Repeating => Lines.Count > 0 && Lines[0].Repeating;

    public string Display =>
        (Scope == "firstVisitEver" ? "本体：首次见到他" : $"本体：第 {DialogueIndex + 1} 段")
        + $" · {Lines.Count} 句" + (Repeating ? " · 可重复" : "");

    public string VanillaFirstLine
    {
        get
        {
            foreach (var l in Lines)
            {
                string t = l.VanillaAncient.Length > 0 ? l.VanillaAncient : l.VanillaChar;
                if (t.Length > 0) return t.Replace("\n", " / ");
            }
            return "";
        }
    }
}

/// <summary>一位先古之民（达弗 / 妮欧 / 建筑师 …）。</summary>
public sealed record AncientEntry(string Id, string Epithet, IReadOnlyList<AncientSlot> NativeSlots)
{
    public string Display => Epithet.Length > 0 ? $"{Epithet}（{Id}）" : Id;

    /// <summary>本体有没有给「任意角色」写对话（建筑师就没有；我们自己写的对话靠补丁注入，不受这个限制）。</summary>
    public bool HasNativeAnyDialogue => NativeSlots.Count > 0;

    /// <summary>
    /// 本体这位先古之民**本来可能给**的遗物（类名，从本体源码 Core/Models/Events/&lt;他&gt;.cs 里扫出来的）。
    /// 界面上「遗物出现概率」的下拉就用它当候选，免得用户瞎填类名。
    /// </summary>
    public IReadOnlyList<string> RelicCandidateIds { get; init; } = Array.Empty<string>();

    /// <summary>他的事件类名（Darv / Neow …）；补丁就是打在这个类上的。</summary>
    public string EventClassName { get; init; } = "";
}

/// <summary>
/// 先古之民目录：从本机解包工程里的 <c>localization/zhs/ancients.json</c> 读出有哪些先古之民。
///
/// 关于「怎么让自定义角色和他们有对话」：
///   本体 AncientEventModel 的对话是按「角色 ID」查字典的（CharacterDialogues["IRONCLAD"] = …），
///   自定义角色不在那个字典里，所以本体只会在 JSON 里找 <c>&lt;先古之民&gt;.talk.ANY.*</c>（任意角色那组）。
///   本工具的做法是给模组加一个补丁：运行时把我们的角色**塞进** CharacterDialogues，
///   于是 <c>&lt;先古之民&gt;.talk.&lt;我们的角色&gt;.*</c> 就能生效，而且对话句数由我们自己定（不受本体限制）。
///   这个目录只负责列出「有哪些先古之民」以及本体原文（给界面当参考）。
/// </summary>
public static class AncientCatalog
{
    public static IReadOnlyList<AncientEntry> Ancients { get; private set; } = Array.Empty<AncientEntry>();

    public static string CatalogStatus { get; private set; } = "尚未加载";

    /// <summary>本体所有遗物的类名（从解包工程 src/Core/Models/Relics/*.cs 扫出来），界面上给个中文名。</summary>
    public static IReadOnlyList<(string Id, string Name)> VanillaRelics { get; private set; } =
        Array.Empty<(string, string)>();

    public static string? VanillaRelicName(string? relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return null;
        foreach (var (id, name) in VanillaRelics)
            if (string.Equals(id, relicId.Trim(), StringComparison.Ordinal)) return name;
        return null;
    }

    public static AncientEntry? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : Ancients.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void Initialize(string? vanillaProject, string? gameDataDir = null)
    {
        var list = new List<AncientEntry>();
        string status;

        var file = FindLocFile(vanillaProject, gameDataDir);
        if (file is null)
        {
            status = "读不到先古之民列表（需要在本机指定「解包工程」目录）";
        }
        else
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
                           ?? new Dictionary<string, string>();
                list = Parse(dict);
                status = $"已从本机解包工程读取（{list.Count} 位先古之民）";
            }
            catch (Exception e)
            {
                status = "先古之民列表解析失败：" + e.Message;
            }
        }

        Ancients = list;
        CatalogStatus = status;

        // 顺带扫一遍本体遗物清单（界面上「遗物出现概率」要显示中文名 + 校验类名）
        VanillaRelics = ScanVanillaRelics(vanillaProject, gameDataDir);
        // 每位先古之民「本来可能给哪些遗物」也扫出来（读本体那位的事件源码）
        if (list.Count > 0) Ancients = WithRelicCandidates(list, vanillaProject);
    }

    /// <summary>本体遗物：类名 + 中文名（中文名从 localization/zhs/relics.json 读，读不到就显示类名）。</summary>
    private static List<(string Id, string Name)> ScanVanillaRelics(string? vanillaProject, string? gameDataDir)
    {
        var result = new List<(string, string)>();
        try
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rel in new[] { "localization/zhs/relics.json", "localization/eng/relics.json" })
            {
                if (!string.IsNullOrWhiteSpace(vanillaProject))
                {
                    string p = Path.Combine(vanillaProject, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p))
                    {
                        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p));
                        if (map is not null) foreach (var kv in map) names.TryAdd(kv.Key, kv.Value);
                    }
                }
            }

            var dir = string.IsNullOrWhiteSpace(vanillaProject)
                ? null
                : Path.Combine(vanillaProject, "src", "Core", "Models", "Relics");
            if (dir is not null && Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "*.cs"))
                {
                    string cls = Path.GetFileNameWithoutExtension(file);
                    if (cls.StartsWith("Mock", StringComparison.Ordinal)) continue;
                    string slug = EffectCatalog.SlugFor(cls);
                    string name = names.TryGetValue(slug + ".title", out var zh) && zh.Length > 0 ? zh : cls;
                    result.Add((cls, name));
                }
            }
        }
        catch { /* 扫不到就算了（界面上退化成手动填类名） */ }
        return result.OrderBy(r => r.Item2, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 给每位先古之民找出「他本来可能给的遗物」：读本体 <c>src/Core/Models/Events/&lt;类名&gt;.cs</c>，
    /// 扫 <c>RelicOption&lt;X&gt;</c> 和 <c>ModelDb.Relic&lt;X&gt;()</c> 里的类名。
    /// </summary>
    private static List<AncientEntry> WithRelicCandidates(List<AncientEntry> list, string? vanillaProject)
    {
        if (string.IsNullOrWhiteSpace(vanillaProject)) return list;
        string dir = Path.Combine(vanillaProject, "src", "Core", "Models", "Events");
        if (!Directory.Exists(dir)) return list;

        // 先古之民 Id（DARV）→ 事件类名（Darv）：按「驼峰转大写下划线」的逆规则找文件
        var byClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string f in Directory.GetFiles(dir, "*.cs"))
            byClass[Path.GetFileNameWithoutExtension(f)] = f;

        var done = new List<AncientEntry>();
        foreach (var a in list)
        {
            string? cls = byClass.Keys.FirstOrDefault(k => EffectCatalog.SlugFor(k) == a.Id);
            if (cls is null || !byClass.TryGetValue(cls, out string? file)) { done.Add(a); continue; }
            try
            {
                string text = File.ReadAllText(file);
                var ids = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(text, @"(?:RelicOption|ModelDb\.Relic)<(\w+)>"))
                    if (!ids.Contains(m.Groups[1].Value)) ids.Add(m.Groups[1].Value);
                done.Add(a with { RelicCandidateIds = ids, EventClassName = cls });
            }
            catch { done.Add(a with { EventClassName = cls }); }
        }
        return done;
    }

    private static string? FindLocFile(string? vanillaProject, string? gameDataDir)
    {
        var cands = new List<string>();
        if (!string.IsNullOrWhiteSpace(vanillaProject))
        {
            cands.Add(Path.Combine(vanillaProject, "localization", "zhs", "ancients.json"));
            cands.Add(Path.Combine(vanillaProject, "localization", "eng", "ancients.json"));
        }
        foreach (var c in cands) if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>列出所有先古之民（只要能找到 &lt;id&gt;.talk.* 就算），并顺带记下本体给任意角色写的那组对话。</summary>
    private static List<AncientEntry> Parse(Dictionary<string, string> dict)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // (id, scope, x, y) → 原文
        var slots = new Dictionary<(string Id, string Scope, int X, int Y), (string Ancient, string Char)>();

        foreach (var kv in dict)
        {
            string[] parts = kv.Key.Split('.');
            if (parts.Length != 5 || parts[1] != "talk") continue;
            string id = parts[0];
            if (id is "ERROR" or "PROCEED") continue;
            ids.Add(id);

            string scope = parts[2];
            if (scope != "ANY" && scope != "firstVisitEver") continue;

            string xy = parts[3];
            bool repeating = xy.EndsWith("r", StringComparison.Ordinal);
            if (repeating) xy = xy[..^1];
            string[] nums = xy.Split('-');
            if (nums.Length != 2 || !int.TryParse(nums[0], out int x) || !int.TryParse(nums[1], out int y)) continue;

            var key = (id, scope, x, y);
            slots.TryGetValue(key, out var cur);
            cur = parts[4] switch
            {
                "ancient" => (kv.Value, cur.Char),
                "char" => (cur.Ancient, kv.Value),
                _ => cur,
            };
            slots[key] = cur;
        }

        var result = new List<AncientEntry>();
        foreach (string id in ids)
        {
            var native = new List<AncientSlot>();
            foreach (var byDialogue in slots.Keys.Where(k => k.Id == id).GroupBy(k => (k.Scope, k.X)))
            {
                var lines = byDialogue.OrderBy(k => k.Y)
                    .Select(k => new AncientSlotLine(k.Y,
                        dict.Keys.Any(t => t.StartsWith($"{id}.talk.{k.Scope}.{k.X}-{k.Y}r.", StringComparison.Ordinal)),
                        slots[k].Ancient, slots[k].Char))
                    .ToList();
                native.Add(new AncientSlot(byDialogue.Key.X, byDialogue.Key.Scope, lines));
            }
            native = native.OrderBy(d => d.Scope == "firstVisitEver" ? 0 : 1).ThenBy(d => d.DialogueIndex).ToList();

            string epithet = dict.TryGetValue(id + ".epithet", out var ep) ? ep : "";
            result.Add(new AncientEntry(id, epithet, native));
        }

        return result
            .OrderByDescending(a => a.Id == "THE_ARCHITECT")     // 建筑师（本体最终 Boss）放最前
            .ThenBy(a => a.Epithet, StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }
}
