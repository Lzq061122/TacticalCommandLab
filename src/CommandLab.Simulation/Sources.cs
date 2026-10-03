// CommandLab.Simulation / 决策源：中性 Lab 侧源 + 播种随机

using CommandLab.Core;

namespace CommandLab.Simulation;

public interface ICommandSource
{
    string Id { get; }
    void Evaluate();
}

/// <summary>确定性 RNG（splitmix64）。框架内禁止出现任何未播种的随机。</summary>
public sealed class SeededRandom
{
    private ulong _state;

    public SeededRandom(int seed)
    {
        unchecked
        {
            _state = (ulong)(uint)seed ^ 0x9E3779B97F4A7C15UL;
            _state = (_state << 1) | 1UL;
            if (_state == 0) _state = 0xDEADBEEFUL;
        }
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public double NextGaussian()
    {
        var u1 = Math.Max(NextDouble(), 1e-12);
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

public sealed class LabSourceConfig
{
    public required string Id { get; init; }
    public CommandDomain Domain { get; init; } = CommandDomain.Movement;
    public CommandValue Value { get; init; } = CommandValue.HOLD;
    public double Period { get; init; } = 1.0;
    public double Phase { get; init; } = 0.0;
    public double Priority { get; init; } = 1.0;
    public double Utility { get; init; } = 1.0;
    public double ActiveFrom { get; init; } = 0.0;
    public double ActiveUntil { get; init; } = double.PositiveInfinity;
    public string? TaskId { get; init; }
    public string? IntentId { get; init; }

    /// <summary>绑定 Intent 时，命令值取 Intent.Desired（带终止条件的意图，E2 2c / E5 用）。</summary>
    public Intent? Intent { get; init; }

    /// <summary>E3 3c 对照组：把归属判断放在决策器侧（写入口不做校验）。</summary>
    public bool CheckOwnershipInDecider { get; init; } = false;

    /// <summary>E6 6b 对照组：把代次判断放在决策器侧（写入口不拦）。</summary>
    public bool CheckGenerationInDecider { get; init; } = false;

    /// <summary>投递延迟（秒）：>0 时经 CommandDelayQueue 投递，用于产生"在途"命令（E6）。</summary>
    public double DeliveryDelay { get; init; } = 0.0;

    /// <summary>E7 7e：只在显式转移条件满足时变更（状态去抖）。</summary>
    public bool StateDebounce { get; init; } = false;
}

public sealed class LabSource : ICommandSource
{
    private readonly ScenarioContext _ctx;
    private readonly LabSourceConfig _cfg;
    private double _next;
    private CommandValue _lastIssued = CommandValue.Unset;
    private readonly CommandValue _value;

    public LabSource(ScenarioContext ctx, LabSourceConfig cfg)
    {
        _ctx = ctx;
        _cfg = cfg;
        _next = cfg.Phase;
        _value = cfg.Intent?.Desired ?? cfg.Value;
    }

    public string Id => _cfg.Id;

    /// <summary>租约由场景在运行时绑定（4d 的"携带旧 token 再写"需要能切换绑定）。</summary>
    public Lease? Lease { get; set; }
    public long ForcedToken { get; set; } = 0;
    public bool UseForcedToken { get; set; } = false;
    public long Generation { get; set; } = 0;

    public void Evaluate()
    {
        var now = _ctx.Clock.Now;
        if (now < _cfg.ActiveFrom || now > _cfg.ActiveUntil) return;
        if (now + 1e-9 < _next) return;
        _next += _cfg.Period;

        if (_cfg.CheckOwnershipInDecider)
        {
            var rec = _ctx.Authority.TryGet(_ctx.Entity.Id, _cfg.Domain);
            if (rec is null || !string.Equals(rec.OwnerId, Id, StringComparison.Ordinal)) return;
        }

        if (_cfg.CheckGenerationInDecider && Generation > 0)
        {
            var rec = _ctx.Authority.TryGet(_ctx.Entity.Id, _cfg.Domain);
            if (rec is not null && Generation < rec.Generation) return;
        }

        if (_cfg.StateDebounce && _lastIssued == _value) return;

        _lastIssued = _value;

        var lease = Lease;
        var req = new WriteRequest
        {
            EntityId = _ctx.Entity.Id,
            Domain = _cfg.Domain,
            Value = _value,
            WriterId = Id,
            Path = WritePath.Gated,
            Priority = _cfg.Priority,
            Utility = _cfg.Utility,
            LeaseId = lease?.LeaseId,
            LeaseToken = UseForcedToken ? ForcedToken : (lease?.Token ?? 0),
            Generation = Generation,
            TaskId = _cfg.TaskId,
            TaskState = _cfg.TaskId is null ? null : _ctx.TaskStateOf(_cfg.TaskId),
            IntentId = _cfg.IntentId ?? _cfg.Intent?.Id,
            Reason = lease is null ? "lab source (no lease)" : $"lab source under {lease.LeaseId}"
        };

        if (_cfg.DeliveryDelay > 0) _ctx.Delay.Enqueue(req, _cfg.DeliveryDelay, now);
        else _ctx.Port.Submit(req);
    }
}
