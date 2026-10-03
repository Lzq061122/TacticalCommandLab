// BannerlordProbe / 宿主侧 Telemetry 扩展字段
//
// 规则（HANDOFF §8）：Core 的 WriteAttempt 行保持逐字段不变，宿主专有字段另起一行以 HOST| 开头。
// 这样 Phase 10 的实测与 Phase 7/8 的仿真可以同表对比（同一 CanonicalLine），又能看到原生上下文。

using System.Globalization;
using CommandLab.Core;
using TaleWorlds.MountAndBlade;

namespace CommandLab.BannerlordProbe;

public static class HostContext
{
    /// <summary>当前 Behavior 名（可归因用）。取不到返回 "-"，绝不抛到宿主调用栈。</summary>
    public static string BehaviorOf(Formation formation)
    {
        try
        {
            var behavior = formation.AI?.ActiveBehavior;
            return behavior is null ? "-" : behavior.GetType().Name;
        }
        catch
        {
            return "-";
        }
    }

    /// <summary>当前 Behavior 权重。反射取值：权重属性名在不同版本间不稳定，取不到就记 "-"（登记为口径限制）。</summary>
    public static string WeightOf(Formation formation)
    {
        try
        {
            var behavior = formation.AI?.ActiveBehavior;
            if (behavior is null) return "-";
            var type = behavior.GetType();
            var prop = type.GetProperty("Weight") ?? type.GetProperty("WeightFactor");
            var value = prop?.GetValue(behavior);
            return value is null ? "-" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
        }
        catch
        {
            return "-";
        }
    }

    public static string TeamOf(Formation formation)
    {
        try
        {
            var team = formation.Team;
            if (team is null) return "-";
            return team.IsPlayerTeam ? "player" : "nonplayer";
        }
        catch
        {
            return "-";
        }
    }

    public static string Line(double timestamp, string entityId, CommandDomain domain, string hook,
                              Formation formation, string raw, string prevRaw, string newRaw,
                              bool originalMethodExecuted, bool orderChanged)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "HOST|t={0:F4}|E={1}|D={2}|hook={3}|team={4}|idx={5}|behavior={6}|weight={7}|raw={8}|prev={9}|new={10}|ome={11}|chg={12}",
            timestamp, entityId, domain, hook, TeamOf(formation), formation.Index,
            BehaviorOf(formation), WeightOf(formation), raw, prevRaw, newRaw,
            originalMethodExecuted ? 1 : 0, orderChanged ? 1 : 0);
    }
}
