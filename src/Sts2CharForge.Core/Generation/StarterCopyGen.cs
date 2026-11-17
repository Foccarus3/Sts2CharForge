namespace Sts2CharForge.Core.Generation;

using System.Text.Json;
using Sts2CharForge.Core.Profile;

/// <summary>
/// V0.3.83：把「一张卡 + StartingCopies &gt; 1」展开成「一张原卡 + N-1 张独立副本类」。
///
/// 为什么需要：多人实测（SerenMod：Strike×4 / Defend×4）里，同一 CardModel 类在初始卡组
/// 重复多份会让主机/客户端对「手里是哪一张」的解析不一致，回合后 checksum 分叉；
/// 把每份复制品变成独立 CardModel（类名不同、显示名/效果相同）可稳定同步。
///
/// 只改 **本次生成的临时 profile**：用户存档 / 界面里的 StartingCopies 保持原样，
/// 下次生成仍然按同一规则展开。
/// </summary>
public static class StarterCopyGen
{
    /// <summary>展开后的 profile；不需要展开时原样返回（不产生任何文本变化）。</summary>
    public static CharacterProfile Expand(CharacterProfile profile, Action<string>? log = null)
    {
        if (profile.Cards.All(c => c is null || !c.InStartingDeck || c.IsVanillaCard || c.StartingCopies <= 1))
            return profile;

        var copy = CloneProfile(profile);
        var n = Naming.From(copy);
        var existing = new HashSet<string>(
            copy.AllCards.Where(c => c is not null && !c.IsVanillaCard).Select(c => n.CardClassName(copy, c)),
            StringComparer.Ordinal);

        int made = 0;
        foreach (var card in copy.Cards.ToList())
        {
            if (card is null || card.IsVanillaCard || !card.InStartingDeck) continue;
            int count = Math.Max(1, card.StartingCopies);
            if (count <= 1) continue;

            string baseName = n.CardClassName(copy, card);
            card.StartingCopies = 1;

            for (int i = 2; i <= count; i++)
            {
                var extra = CloneCard(card);
                extra.StartingCopies = 1;
                extra.InCardPool = false;          // 复制品不进奖励池；只保证初始卡组有独立模型
                extra.InStartingDeck = true;

                string candidate = baseName + "Copy" + i;
                int suffix = 2;
                while (existing.Contains(candidate))
                    candidate = baseName + "Copy" + i + "_" + suffix++;
                extra.ClassName = candidate;
                extra.StarterCopyOf = baseName;
                // 卡图 / 卡面是**按类名**查的（p.Art.CardPortraits[cls]）：
                // 副本换了类名，必须继承原卡的这张图，否则只有原卡有卡图。
                if (copy.Art.CardPortraits.TryGetValue(baseName, out string? portrait)
                    && !string.IsNullOrWhiteSpace(portrait))
                    copy.Art.CardPortraits[candidate] = portrait;
                existing.Add(candidate);
                // 追加到列表末尾：不能插在原卡后面 —— 那样会改变后面卡牌的列表序号，
                // 自动类名（<角色>CardN）会整体错位。副本用显式类名，追加不碰已有卡。
                copy.Cards.Add(extra);
                made++;
            }
        }

        if (made == 0) return profile;
        log?.Invoke($"  初始卡多份：展开 {made} 个独立副本 CardModel（避免多人同名实例不同步）");
        return copy;
    }

    private static CharacterProfile CloneProfile(CharacterProfile p)
    {
        string json = JsonSerializer.Serialize(p, ProfileFactory.JsonOpts);
        var copy = JsonSerializer.Deserialize<CharacterProfile>(json, ProfileFactory.JsonOpts)
            ?? throw new InvalidOperationException("复制存档失败");
        // SaveName 是 [JsonIgnore]：不补回来的话生成目录会从「存档名」退回 ModId，
        // 和用户工程目录不一致（--buildtest / 从工程恢复会找不到项目）。
        copy.SaveName = p.SaveName;
        return copy;
    }

    private static CardSpec CloneCard(CardSpec c)
    {
        string json = JsonSerializer.Serialize(c, ProfileFactory.JsonOpts);
        return JsonSerializer.Deserialize<CardSpec>(json, ProfileFactory.JsonOpts)
            ?? throw new InvalidOperationException("复制卡牌失败");
    }
}
