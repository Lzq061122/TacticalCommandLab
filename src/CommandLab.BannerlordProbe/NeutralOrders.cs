// BannerlordProbe / 中性指令映射 + 原生 Order 值的中性投影
//
// 红线：本工程只支持 HOLD / ADVANCE / RETREAT 三条中性指令，不承载任何战术语义。
// Facing / Arrangement 只做"观察 + 是否可拦截"，probe 从不主动下达这两域的指令。

using System.Globalization;
using CommandLab.Core;
using TaleWorlds.MountAndBlade;

namespace CommandLab.BannerlordProbe;

public static class NeutralOrders
{
    /// <summary>中性指令 -> 宿主 MovementOrder。未知值一律落到 HOLD（不改变"只有一个中性出口"这一事实）。</summary>
    public static MovementOrder ToMovement(CommandValue v) => v switch
    {
        CommandValue.HOLD => MovementOrder.MovementOrderStop,
        CommandValue.ADVANCE => MovementOrder.MovementOrderAdvance,
        CommandValue.RETREAT => MovementOrder.MovementOrderRetreat,
        _ => MovementOrder.MovementOrderStop
    };

    /// <summary>宿主 MovementOrder -> 中性指令。无法归入三条中性指令时返回 false，由调用方走投影。</summary>
    public static bool TryDecode(MovementOrder order, out CommandValue value)
    {
        var t = order.OrderType;
        if (t == MovementOrder.MovementOrderStop.OrderType) { value = CommandValue.HOLD; return true; }
        if (t == MovementOrder.MovementOrderAdvance.OrderType) { value = CommandValue.ADVANCE; return true; }
        if (t == MovementOrder.MovementOrderRetreat.OrderType) { value = CommandValue.RETREAT; return true; }
        value = CommandValue.Unset;
        return false;
    }

    public static string NameOf(CommandValue v) => v == CommandValue.Unset ? "UNSET" : v.ToString();

    public static bool TryParse(string text, out CommandValue value)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "HOLD": value = CommandValue.HOLD; return true;
            case "ADVANCE": value = CommandValue.ADVANCE; return true;
            case "RETREAT": value = CommandValue.RETREAT; return true;
            default: value = CommandValue.Unset; return false;
        }
    }
}

/// <summary>
/// 把宿主原生 Order 值投影到中性观察槽（CommandValue.Obs0..Obs15）。
/// 目的只有一个：让"值是否发生了变化"在 Core 侧可被判定（OrderChanged / oscillation 依赖值不等）。
/// 槽位按首次出现顺序分配，超过 16 个不同值后回绕 —— 回绕会低估变化次数，故计数并可告警。
/// </summary>
public sealed class HostOrderProjection
{
    private static readonly CommandValue[] Slots =
    {
        CommandValue.Obs0, CommandValue.Obs1, CommandValue.Obs2, CommandValue.Obs3,
        CommandValue.Obs4, CommandValue.Obs5, CommandValue.Obs6, CommandValue.Obs7,
        CommandValue.Obs8, CommandValue.Obs9, CommandValue.Obs10, CommandValue.Obs11,
        CommandValue.Obs12, CommandValue.Obs13, CommandValue.Obs14, CommandValue.Obs15
    };

    private readonly Dictionary<string, CommandValue> _map = new(StringComparer.Ordinal);
    private int _next;

    /// <summary>槽位回绕次数。>0 意味着该域出现过 16 个以上不同值，变化次数会被低估。</summary>
    public int WrapCount { get; private set; }

    public CommandValue Project(string rawKey)
    {
        if (_map.TryGetValue(rawKey, out var v)) return v;
        if (_next >= Slots.Length) { _next = 0; WrapCount++; }
        v = Slots[_next++];
        _map[rawKey] = v;
        return v;
    }

    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "distinct={0} wrap={1}", _map.Count, WrapCount);
}
