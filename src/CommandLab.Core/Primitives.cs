// CommandLab.Core / 时间、中性对象、实体状态
// 红线：本文件及整个 Core 层禁止出现任何真实战术语义与宿主类型名。

namespace CommandLab.Core;

/// <summary>
/// 时钟抽象。仿真侧由 <see cref="VirtualClock"/> 提供确定性虚拟时间；
/// 宿主侧（BannerlordProbe）提供任务时间，两者对 WritePort/Metrics 完全等价，故指标可同表对比。
/// </summary>
public interface ILabClock
{
    double Dt { get; }
    long Step { get; }
    double Now { get; }
}

/// <summary>唯一时间源。Now 由步数乘 dt 得出，避免浮点累加漂移；禁止真实时钟进入判定路径。</summary>
public sealed class VirtualClock : ILabClock
{
    private long _step;

    public VirtualClock(double dt)
    {
        if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        Dt = dt;
    }

    public double Dt { get; }
    public long Step => _step;
    public double Now => _step * Dt;

    public void Advance() => _step++;
    public void Reset() => _step = 0;
}

public enum CommandDomain
{
    Movement = 0,
    Facing = 1,
    Arrangement = 2
}

/// <summary>中性命令符号，无战术语义。</summary>
public enum CommandValue
{
    Unset = 0,
    MOVE_A = 1,
    HOLD = 2,
    ADVANCE = 3,
    RETREAT = 4,

    // 中性观察槽（Phase 9 新增）：宿主侧把无法归入 HOLD/ADVANCE/RETREAT 的原生 Order 值
    // 投影到这些槽位，只用于判定"值是否发生了变化"，不含任何战术语义。
    // 纯追加，不改动 0..4 的取值，故对既有仿真与 replay_hash 无影响。
    Obs0 = 16,
    Obs1 = 17,
    Obs2 = 18,
    Obs3 = 19,
    Obs4 = 20,
    Obs5 = 21,
    Obs6 = 22,
    Obs7 = 23,
    Obs8 = 24,
    Obs9 = 25,
    Obs10 = 26,
    Obs11 = 27,
    Obs12 = 28,
    Obs13 = 29,
    Obs14 = 30,
    Obs15 = 31
}

public sealed class DomainState
{
    internal DomainState(CommandDomain domain) => Domain = domain;

    public CommandDomain Domain { get; }
    public CommandValue Value { get; internal set; } = CommandValue.Unset;
    public string? LastWriterId { get; internal set; }
    public double LastWriteAt { get; internal set; } = -1.0;
    public int EffectiveWriteCount { get; internal set; }
    public double LastUtility { get; internal set; }

    internal void Commit(CommandValue value, string writerId, double utility, double now)
    {
        Value = value;
        LastWriterId = writerId;
        LastWriteAt = now;
        LastUtility = utility;
        EffectiveWriteCount++;
    }
}

public readonly record struct DomainSnapshot(
    CommandDomain Domain,
    CommandValue Value,
    string? LastWriterId,
    double LastWriteAt,
    int EffectiveWriteCount);

/// <summary>被控实体。状态只能在 Core 内部改写 —— 写入口 <see cref="WritePort"/> 是唯一通道。</summary>
public sealed class ControlledEntity
{
    private readonly DomainState[] _states;

    public ControlledEntity(string id)
    {
        Id = id;
        var domains = (CommandDomain[])Enum.GetValues(typeof(CommandDomain));
        _states = new DomainState[domains.Length];
        foreach (var d in domains) _states[(int)d] = new DomainState(d);
    }

    public string Id { get; }
    public IReadOnlyList<DomainState> Domains => _states;
    public DomainState this[CommandDomain d] => _states[(int)d];

    public DomainSnapshot[] Snapshot()
    {
        var r = new DomainSnapshot[_states.Length];
        for (int i = 0; i < _states.Length; i++)
        {
            var s = _states[i];
            r[i] = new DomainSnapshot(s.Domain, s.Value, s.LastWriterId, s.LastWriteAt, s.EffectiveWriteCount);
        }
        return r;
    }
}

public static class DomainSnapshotExtensions
{
    public static bool ValueEquals(this DomainSnapshot[] a, DomainSnapshot[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].Domain != b[i].Domain || a[i].Value != b[i].Value)
                return false;
        return true;
    }
}
