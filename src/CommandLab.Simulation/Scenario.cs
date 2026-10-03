// CommandLab.Simulation / 装置：虚拟时间驱动的场景执行器 + 恢复探针
// 契约（05 §1.1）：唯一时间源 VirtualClock；禁止 Thread.Sleep；单一播种 RNG；显式注册序可配置；replay_hash 校验。

using CommandLab.Core;
using CommandLab.Telemetry;

namespace CommandLab.Simulation;

/// <summary>
/// 虚拟延迟队列（E6 必需）：命令发出后延迟 N 秒才投递到写入口，用于产生真实的"在途"语义。
/// 05 §E6 Failure Criteria 明确要求：若命令被同步执行，本实验未真正产生在途语义，须引入延迟队列重做。
/// </summary>
public sealed class CommandDelayQueue
{
    private readonly List<(double DueAt, int Seq, WriteRequest Req)> _queue = new();
    private int _seq;

    public int Pending => _queue.Count;

    public void Enqueue(WriteRequest req, double delay, double now)
    {
        _queue.Add((now + delay, _seq++, req));
    }

    public void Dispatch(double now, Action<WriteRequest> submit)
    {
        if (_queue.Count == 0) return;
        // 按 (到期时间, 入队序) 排序，保证投递序确定且与遍历无关
        var due = _queue.Where(x => x.DueAt <= now + 1e-9)
                        .OrderBy(x => x.DueAt).ThenBy(x => x.Seq)
                        .ToList();
        if (due.Count == 0) return;
        _queue.RemoveAll(x => due.Any(d => d.Seq == x.Seq));
        foreach (var d in due) submit(d.Req);
    }
}

public sealed class ScenarioContext
{
    public required ScenarioDefinition Definition { get; init; }
    public required VirtualClock Clock { get; init; }
    public required WritePort Port { get; init; }
    public required AuthorityService Authority { get; init; }
    public required TaskRegistry Tasks { get; init; }
    public required ControlledEntity Entity { get; init; }
    public required SeededRandom Rng { get; init; }
    public required TelemetryCollector Telemetry { get; init; }
    public required CommandDelayQueue Delay { get; init; }

    public string? TaskStateOf(string taskId)
    {
        foreach (var t in Tasks.Tasks)
            if (string.Equals(t.Id, taskId, StringComparison.Ordinal))
                return t.State.ToString();
        return null;
    }
}

public sealed record TimedActionSpec(double At, string Name, Action<ScenarioContext> Action);

public sealed class ScenarioDefinition
{
    public required string Id { get; init; }
    public int Seed { get; init; } = 1;
    public double Dt { get; init; } = 0.1;
    public double DurationSeconds { get; init; } = 60.0;
    public WritePortPolicy PortPolicy { get; init; } = new();
    public string DefaultOwnerId { get; init; } = "Native";
    public MetricsConfig Metrics { get; init; } = new();
    public bool EnableTaskSweep { get; init; } = true;

    /// <summary>显式注册序 —— E1 靠置换本列表证明"顺序敏感"这一性质本身。</summary>
    public List<Func<ScenarioContext, ICommandSource>> SourceFactories { get; init; } = new();

    public List<TimedActionSpec> Actions { get; init; } = new();

    public ScenarioDefinition WithSourceOrder(IEnumerable<int> permutation)
    {
        var list = SourceFactories.ToList();
        var reordered = permutation.Select(i => list[i]).ToList();
        return new ScenarioDefinition
        {
            Id = Id,
            Seed = Seed,
            Dt = Dt,
            DurationSeconds = DurationSeconds,
            PortPolicy = PortPolicy,
            DefaultOwnerId = DefaultOwnerId,
            Metrics = Metrics,
            EnableTaskSweep = EnableTaskSweep,
            SourceFactories = reordered,
            Actions = Actions
        };
    }
}

public sealed class ScenarioResult
{
    public required string ScenarioId { get; init; }
    public required string ReplayHash { get; init; }
    public required MetricSet Metrics { get; init; }
    public required TelemetryCollector Telemetry { get; init; }
    public required IReadOnlyList<string> SourceOrder { get; init; }
    public required DomainSnapshot[] FinalState { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public double SimulationSeconds { get; init; }
}

public static class ScenarioRunner
{
    public static ScenarioResult Run(ScenarioDefinition def)
    {
        var clock = new VirtualClock(def.Dt);
        var telemetry = new TelemetryCollector();
        var authority = new AuthorityService(telemetry, def.DefaultOwnerId);
        var port = new WritePort(clock, authority, telemetry, def.PortPolicy);
        var entity = new ControlledEntity("E0");
        port.RegisterEntity(entity);
        var rng = new SeededRandom(def.Seed);
        var tasks = new TaskRegistry(telemetry);

        var ctx = new ScenarioContext
        {
            Definition = def,
            Clock = clock,
            Port = port,
            Authority = authority,
            Tasks = tasks,
            Entity = entity,
            Rng = rng,
            Telemetry = telemetry,
            Delay = new CommandDelayQueue()
        };

        var sources = new List<ICommandSource>();
        foreach (var f in def.SourceFactories) sources.Add(f(ctx));

        // 度量口径自检：若没有任何源的 Id 等于 DefaultOwnerId，则所有写入都会被判为越权，指标会系统性虚高
        var warnings = new List<string>();
        if (sources.Count > 0 && sources.All(s => !string.Equals(s.Id, def.DefaultOwnerId, StringComparison.Ordinal)))
            warnings.Add($"no source id matches DefaultOwnerId '{def.DefaultOwnerId}'; " +
                         "unauthorized_writes would be inflated by writer/owner id mismatch");

        var fired = new bool[def.Actions.Count];
        long steps = (long)Math.Round(def.DurationSeconds / def.Dt);

        for (long i = 0; i < steps; i++)
        {
            clock.Advance();
            authority.SweepExpired(clock.Now);
            if (def.EnableTaskSweep) tasks.Sweep(clock.Now);

            foreach (var s in sources) s.Evaluate();

            ctx.Delay.Dispatch(clock.Now, port.Submit);

            for (int k = 0; k < def.Actions.Count; k++)
            {
                if (fired[k]) continue;
                if (clock.Now + 1e-9 >= def.Actions[k].At)
                {
                    fired[k] = true;
                    def.Actions[k].Action(ctx);
                }
            }

            port.FlushTick();
        }

        var metrics = MetricsCalculator.Compute(telemetry, clock.Now, tasks.Tasks, def.Metrics);

        return new ScenarioResult
        {
            ScenarioId = def.Id,
            ReplayHash = telemetry.ReplayHash(),
            Metrics = metrics,
            Telemetry = telemetry,
            SourceOrder = sources.Select(s => s.Id).ToList(),
            Warnings = warnings,
            FinalState = entity.Snapshot(),
            SimulationSeconds = clock.Now
        };
    }
}

/// <summary>
/// Release 恢复探针：捕获 acquisition 前基线，Release + 观测窗口后比对（05 §1.5 release_recovery_failures）。
/// </summary>
public sealed class RecoveryProbe
{
    private readonly ScenarioContext _ctx;
    private readonly NativeSource? _ns;
    private DomainSnapshot[]? _baseline;
    private int _baselineTimers;
    public int Failures { get; private set; }
    public List<string> Notes { get; } = new();

    public RecoveryProbe(ScenarioContext ctx, NativeSource? ns = null)
    {
        _ctx = ctx;
        _ns = ns;
    }

    public void CaptureBaseline()
    {
        _baseline = _ctx.Entity.Snapshot();
        _baselineTimers = _ns?.ActiveTimerCount ?? 0;
    }

    public bool Check(string label)
    {
        if (_baseline is null) throw new InvalidOperationException("baseline not captured");
        var now = _ctx.Entity.Snapshot();
        bool ok = now.ValueEquals(_baseline);
        int timers = _ns?.ActiveTimerCount ?? 0;
        bool timerOk = timers == _baselineTimers;

        if (!ok || !timerOk)
        {
            Failures++;
            var diffs = new List<string>();
            for (int i = 0; i < now.Length; i++)
                if (now[i].Value != _baseline[i].Value)
                    diffs.Add($"{now[i].Domain}: {_baseline[i].Value} -> {now[i].Value}");
            if (!timerOk) diffs.Add($"timers: {_baselineTimers} -> {timers}");
            Notes.Add($"[{label}] recovery FAILED: {string.Join("; ", diffs)}");
        }
        else
        {
            Notes.Add($"[{label}] recovery ok");
        }
        return ok && timerOk;
    }
}
