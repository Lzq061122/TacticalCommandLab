// BannerlordProbe / 宿主时钟
//
// 这是全套工程里唯一使用真实（任务）时间的实现，且只做一件事：把 Mission 的 dt 累加成 Now。
// 仿真侧仍是 VirtualClock（确定性），两者对 WritePort / Metrics 完全等价，故指标可同表对比。
// 注意：本类不参与任何"是否放行"的判定，判定只在 Core 的 WritePort.Evaluate 内发生。

using CommandLab.Core;

namespace CommandLab.BannerlordProbe;

public sealed class ProbeClock : ILabClock
{
    private double _now;
    private long _step;

    public double Dt { get; private set; }

    public long Step => _step;

    public double Now => _now;

    public void Advance(double dt)
    {
        Dt = dt;
        _now += dt;
        _step++;
    }

    public void Reset()
    {
        _now = 0.0;
        _step = 0;
        Dt = 0.0;
    }
}
