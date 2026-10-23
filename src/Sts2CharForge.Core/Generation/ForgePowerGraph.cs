using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sts2CharForge.Core.Effects;
using Sts2CharForge.Core.Profile;

namespace Sts2CharForge.Core.Generation;

/// <summary>
/// V0.2.194：**锻造页「状态列表」那一类布的界面侧助手**（用户原话：「我要的不是模块，是锻造页左边
/// 『要锻造哪一类』里多一个『状态列表』，像卡牌列表 / 药水列表 / 遗物列表那样，用那块布给自定义状态编效果」）。
///
/// 生成端的分工（见 <see cref="CustomPowerGen.Source"/> / <see cref="ForgeGraphGen.PowerTriggersOf"/>）：
///   · 图非空 → 方法壳（签名 / 归属守卫 / choiceContext / 数值闸门 / 衰减移除）照「自定义状态」页那条链，
///     「跑效果」换成图上渲染好的正文；
///   · 图空 / null → **老路**（Triggers + Effects），生成文本一个字符都不变（已用 WhiteCloak 31 只状态对拍验过）。
///
/// 这个文件只放**界面要用的那几件事**：
///   · <see cref="HasLegacyContent"/>：这只状态在「自定义状态」页配过东西没有（决定能不能直接给空布）；
///   · <see cref="EnsureGraph"/>：没有老内容时给一块只有【触发时机】的空布；
///   · <see cref="MigrateToGraph"/>：「把现在的触发时机 + 效果搬到这块布上」那个按钮的实现。
///
/// ⚠ 为什么不能「选中就自动建布」：<c>ForgeGraphSpec.IsEmpty</c> 判的是「一个模块都没有」——
/// 只要布上有【触发时机】就不算空，生成端立刻切到「图说了算」，老配置的效果就全丢了。
/// 所以自动建布**只在没有老内容时**发生（<see cref="CanAutoCreate"/>）。
/// </summary>
public static class ForgePowerGraph
{
    /// <summary>这只状态在「自定义状态」页配过触发时机 / 效果没有。</summary>
    public static bool HasLegacyContent(CustomPowerSpec? cp)
    {
        if (cp?.Triggers is null) return false;
        foreach (var t in cp.Triggers)
            if (t is not null && (t.Effects.Count > 0 || t.Kind.Length > 0)) return true;
        return false;
    }

    /// <summary>可以直接给它建一块空布吗（没图、而且**没有老内容** —— 有老内容必须先按按钮搬）。</summary>
    public static bool CanAutoCreate(CustomPowerSpec? cp) =>
        cp is not null && !ForgeGraphGen.UsesGraph(cp) && !HasLegacyContent(cp);

    /// <summary>没有图时给一块只有【触发时机】的空布（默认挑「玩家回合开始时」）。老内容不受影响（调用方先用 CanAutoCreate 判）。</summary>
    public static ForgeGraphSpec EnsureGraph(CustomPowerSpec cp)
    {
        if (cp.ForgeGraph is not null) return cp.ForgeGraph;
        string timing = ForgeTimings.For(ForgeTimings.PowerScope).FirstOrDefault()?.Id ?? "PlayerTurnStart";
        cp.ForgeGraph = ForgeGraphSpec.NewWithTiming(timing);
        return cp.ForgeGraph;
    }

    /// <summary>
    /// 把「自定义状态」页那套触发时机 + 效果**搬到这块布上**（界面上那个按钮）。
    ///
    /// 口径：
    ///   · 每条触发时机 → 一块【触发时机】，效果按顺序串成一条链；
    ///   · 效果**复用同一份 <see cref="EffectSpec"/> 对象**（所以在「自定义状态」页接着改也一样生效，
    ///     而且 <c>CountMode</c>（最多 N 张 / 恰好 N 张）这类「只有 spec 才知道」的东西不会丢）；
    ///   · 同一个本体钩子上的第 2 条起**不搬**（布上两块落在同一个钩子 = 同一个类里两个同名 override，
    ///     校验器会报错拦住）—— 这些会写进 <paramref name="notes"/>，让界面原样告诉用户。
    /// </summary>
    public static ForgeGraphSpec MigrateToGraph(CustomPowerSpec cp, out List<string> notes)
    {
        notes = new List<string>();
        var g = new ForgeGraphSpec();
        double y = ForgeGraphCatalog.CanvasCenterY;
        var seenHooks = new HashSet<string>(StringComparer.Ordinal);
        int moved = 0;
        foreach (var t in cp.Triggers.Where(x => x is not null).ToList())
        {
            string kind = PowerTriggers.Canonical(t.Kind);
            var timing = ForgeTimings.Find(kind, ForgeTimings.PowerScope);
            if (timing is null)
            {
                notes.Add($"触发时机「{PowerTriggers.Find(kind)?.Display ?? kind}」在状态那块布上没有对应项，没有搬过去。");
                continue;
            }
            if (!seenHooks.Add(timing.Hook))
            {
                notes.Add($"有两条触发时机落在同一个钩子上（都走「{timing.Display}」那条路）—— 布上只搬了先遇到的那一条，"
                    + "生成时会报「同一个钩子只能有一块」，请把它们合成一块（用【条件】/【if】区分）。");
                continue;
            }

            var trigger = new ForgeNodeSpec
            {
                Kind = "Trigger",
                X = ForgeGraphCatalog.CanvasCenterX - 260,
                Y = y,
            };
            trigger.SetField("Timing", timing.Id);
            if (!string.IsNullOrWhiteSpace(t.PowerId)) trigger.SetField("PowerId", t.PowerId.Trim());
            // 「只对哪类牌生效」（打牌类时机用得到）：原样搬过去 —— 不搬就是**静默的行为变化**
            //（「只对某一组牌生效」会变成「任意一张牌都触发」）。
            if (!string.IsNullOrWhiteSpace(t.CardFilter) && !string.Equals(t.CardFilter, "Any", StringComparison.Ordinal))
                trigger.SetField("CardFilter", t.CardFilter.Trim());
            if (string.Equals(timing.Id, ForgeTimings.PowerReached, StringComparison.Ordinal))
                trigger.SetField("PowerAmount", t.TriggerAmount.ToString("0.####", CultureInfo.InvariantCulture));
            // V0.2.270：「层数变化方向」也搬过去（不搬就等于静默变回「升降都算」）
            if (!string.IsNullOrWhiteSpace(t.PowerDirection) && string.Equals(timing.Id, ForgeTimings.PowerChanged, StringComparison.Ordinal))
                trigger.SetField("PowerDirection", t.PowerDirection.Trim());
            if (t.TriggerScope is "Combat" or "Turn") trigger.SetField("CountScope", t.TriggerScope);
            g.Nodes.Add(trigger);
            moved++;

            string prev = trigger.Id;
            double x = ForgeGraphCatalog.CanvasCenterX;
            foreach (var e in t.Effects.Where(x => x is not null).ToList())
            {
                var node = new ForgeNodeSpec { Kind = "Effect", X = x, Y = y };
                node.EffectSpec = e;                 // 同一份对象：两边一起改
                node.EffectFromLegacy = false;       // 「新的效果编辑器说了算」（生成端读 spec 里那些只有它才知道的栏位）
                ForgeEffectBridge.MirrorToFields(node);   // 把 spec 回写成图上那些老字段（生成端读的就是它们）
                ForgeEffectBridge.Bind(node);             // 接着在界面上改也照旧回写
                g.Nodes.Add(node);
                g.Links.Add(new ForgeLinkSpec { From = prev, To = node.Id, FromPort = 0, ToPort = 0 });
                prev = node.Id;
                x += 240;
                moved++;
            }
            y += 220;
        }
        cp.ForgeGraph = g;
        notes.Add(moved == 0
            ? "这只状态在「自定义状态」页上也是空的：布上现在只有一个起点，接着往上接就行。"
            : $"已经把「自定义状态」页那套配置搬到布上（{moved} 个模块）。**从这一刻起这块布说了算**："
              + "以后改触发时机 / 效果请在锻造页这块布上改（「自定义状态」页那几栏会锁住并提示来这里）。");
        return g;
    }
}
