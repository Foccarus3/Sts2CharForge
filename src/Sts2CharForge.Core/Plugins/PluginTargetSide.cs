using System;
using System.Linq;

namespace Sts2CharForge.Core.Plugins;

/// <summary>V0.3.104：插件对象在「作用对象」里的两段 C# 目标表达式。</summary>
public sealed record TargetSidePlan(string Single, string Foes);

/// <summary>
/// 把插件 <c>units[].foes/single</c> 展开成当前链的 C# 表达式。
///
/// 只允许 <c>{self}</c> / <c>{foe}</c> / <c>{combatState}</c> 三个占位符：
/// 四条链的 <c>self</c> 都是 Creature（卡牌/遗物/药水 <c>base.Owner.Creature</c>，状态 <c>base.Owner</c>），
/// 因此一个解析器通吃；锻造图另有自己的 <c>__fg*</c> 展开。
/// </summary>
public static class PluginTargetSide
{
    /// <summary>插件档位 → 目标表达式；不是插件档位返回 null。</summary>
    public static TargetSidePlan? Of(string? side, string selfExpr, string foeExpr)
    {
        PluginUnitSpec? u = PluginRegistry.UnitOf(side);
        if (u is null || u.Kinds is null || u.Kinds.Count == 0) return null;
        string Expand(string s) => (s ?? "")
            .Replace("{self}", selfExpr, StringComparison.Ordinal)
            .Replace("{foe}", foeExpr, StringComparison.Ordinal)
            .Replace("{combatState}", selfExpr + ".CombatState", StringComparison.Ordinal);
        string foes = Expand(u.Foes);
        string single = string.IsNullOrWhiteSpace(u.Single)
            ? $"({foes}).FirstOrDefault()"
            : Expand(u.Single);
        return new TargetSidePlan(single, foes);
    }

    /// <summary>这个 id 是不是允许用在某种效果上的插件档位。</summary>
    public static bool AllowedFor(string? side, string kind) =>
        PluginRegistry.UnitOf(side) is { } u
        && u.Kinds is not null && u.Kinds.Any(x => string.Equals(x, kind, StringComparison.OrdinalIgnoreCase));
}
