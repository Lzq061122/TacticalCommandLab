// BannerlordProbe / 中性指令映射 + 原生 Order 值的中性投影
//
// 红线：本工程只支持 HOLD / ADVANCE / RETREAT 三条中性指令，不承载任何战术语义。
// Facing / Arrangement 只做"观察 + 是否可拦截"，probe 从不主动下达这两域的指令。

using CommandLab.Core;
using CommandLab.Telemetry;
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
