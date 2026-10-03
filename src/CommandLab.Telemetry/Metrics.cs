// CommandLab.Telemetry / 指标计算与稳定性分级
// 依据：05 §1.5 的精确定义与三级分级；HANDOFF §8 的 14 项指标。

using CommandLab.Core;

namespace CommandLab.Telemetry;

public enum StabilityGrade
{
    Stable,
    Marginal,
    Oscillating
}

public sealed class MetricsConfig
{
    public string NativeWriterId { get; init; } = "Native";
    public double TEval { get; init; } = 0.5;
}

public sealed class MetricSet
{
    public double SimulationSeconds { get; init; }
    public int WriteAttempts { get; init; }
    public double CommandsPerSecond { get; init; }
    public int EffectiveChanges { get; init; }
    public double OrderChangesPerSecond { get; init; }
    public int UnauthorizedWriteAttempts { get; init; }
    public int UnauthorizedWrites { get; init; }
    public int NativeOverrides { get; init; }
    public int LabOverrides { get; init; }
    public int AuthorityTransfers { get; init; }
    public int Preemptions { get; init; }
    public double AverageCommandLifetime { get; init; }
    public int OscillationPairs { get; init; }
    public double OscillationFrequency { get; init; }
    public double ReactionLatency { get; set; }          // 由场景回填：Acquire -> 首次生效写入
    public int StaleCommandExecutions { get; init; }
    public int TaskStarvation { get; set; }              // 结束时既未 Done 也未 Cancelled
    public double MaxPendingSeconds { get; set; }
    public int ReleaseRecoveryFailures { get; set; }     // 由 RecoveryProbe 回填
    public StabilityGrade Grade { get; init; }

    public bool IsFinite()
    {
        double[] vals =
        {
            CommandsPerSecond, OrderChangesPerSecond, AverageCommandLifetime,
            OscillationFrequency, ReactionLatency, MaxPendingSeconds
        };
        foreach (var v in vals)
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
        return true;
    }

    public IReadOnlyList<string> Rows()
    {
        var rows = new List<string>
        {
            $"simulation_seconds            = {SimulationSeconds:F2}",
            $"commands_per_second           = {CommandsPerSecond:F4}",
            $"order_changes_per_second      = {OrderChangesPerSecond:F4}",
            $"unauthorized_write_attempts   = {UnauthorizedWriteAttempts}",
            $"unauthorized_writes           = {UnauthorizedWrites}",
            $"native_overrides              = {NativeOverrides}",
            $"lab_overrides                 = {LabOverrides}",
            $"authority_transfers           = {AuthorityTransfers}",
            $"preemptions                   = {Preemptions}",
            $"average_command_lifetime      = {AverageCommandLifetime:F4}",
            $"oscillation_pairs             = {OscillationPairs}",
            $"oscillation_frequency         = {OscillationFrequency:F4}",
            $"reaction_latency              = {ReactionLatency:F4}",
            $"stale_command_execution       = {StaleCommandExecutions}",
            $"task_starvation               = {TaskStarvation}",
            $"max_pending_seconds           = {MaxPendingSeconds:F2}",
            $"release_recovery_failures     = {ReleaseRecoveryFailures}",
            $"stability_grade               = {Grade}"
        };
        return rows;
    }
}

public static class MetricsCalculator
{
    public static MetricSet Compute(TelemetryCollector telemetry, double simulationSeconds,
                                    IEnumerable<CommandTask>? tasks = null,
                                    MetricsConfig? config = null)
    {
        config ??= new MetricsConfig();
        var attempts = telemetry.Attempts;

        int effective = 0, unauthAttempts = 0, unauthWrites = 0, nativeOv = 0, labOv = 0, stale = 0;

        // 每 (实体,域) 的提交序列，用于 lifetime 与 oscillation
        var lifeTimes = new List<double>();
        var lastChangeAt = new Dictionary<(string, CommandDomain), double>();
        var seq = new Dictionary<(string, CommandDomain), List<CommandValue>>();
        var changeAt = new Dictionary<(string, CommandDomain), List<double>>();

        foreach (var a in attempts)
        {
            bool differentOwner = !string.Equals(a.WriterId, a.OwnerAtWrite, StringComparison.Ordinal);
            if (differentOwner) unauthAttempts++;

            if (a.Rejected) continue;

            bool staleAccepted =
                (a.LeaseId is not null && (a.LeaseToken < a.MaxSeenToken)) ||
                (a.Generation > 0 && a.Generation < a.RecordGeneration);
            if (staleAccepted) stale++;

            if (!a.OrderChanged) continue;
            effective++;

            var key = (a.EntityId, a.Domain);
            if (lastChangeAt.TryGetValue(key, out var prevAt)) lifeTimes.Add(a.Timestamp - prevAt);
            lastChangeAt[key] = a.Timestamp;

            if (!seq.TryGetValue(key, out var list)) { list = new List<CommandValue>(); seq[key] = list; }
            if (!changeAt.TryGetValue(key, out var tsList)) { tsList = new List<double>(); changeAt[key] = tsList; }
            list.Add(a.NewValue);
            tsList.Add(a.Timestamp);

            if (differentOwner)
            {
                unauthWrites++;
                if (string.Equals(a.WriterId, config.NativeWriterId, StringComparison.Ordinal)) nativeOv++;
                else labOv++;
            }
        }

        int oscillationPairs = 0;
        foreach (var kv in seq)
        {
            var values = kv.Value;
            for (int i = 2; i < values.Count; i++)
                if (values[i] == values[i - 2] && values[i - 1] != values[i])
                    oscillationPairs++;
        }

        // 校准（Phase 7）：只发生过 1 次生效变更的 (实体,域) 没有 lifetime 样本（未被下一次覆盖）。
        // 旧实现回退为 0.0，会把"值从头到尾没变过"误判成"命令活不过 2×T_eval"-> 误判抖动。
        // 正确语义是右删失：该值一直存活到仿真结束，故补 (simulationSeconds - 首次变更时刻)。
        foreach (var kv in seq)
        {
            if (kv.Value.Count != 1) continue;
            if (changeAt.TryGetValue(kv.Key, out var ts) && ts.Count == 1)
                lifeTimes.Add(Math.Max(0.0, simulationSeconds - ts[0]));
        }

        double seconds = simulationSeconds > 0 ? simulationSeconds : 1.0;
        double avgLifetime = lifeTimes.Count > 0 ? lifeTimes.Average() : 0.0;
        double oscFreq = oscillationPairs / seconds;

        int transfers = telemetry.AuthorityEvents.Count(e => e.Kind == AuthorityEventKind.Acquire);
        int preemptions = telemetry.TaskEvents.Count(e => e.To == TaskState.Preempted);

        int starvation = 0;
        double maxPending = 0.0;
        if (tasks is not null)
        {
            foreach (var t in tasks)
            {
                if (t.State is not (TaskState.Done or TaskState.Cancelled))
                {
                    starvation++;
                    maxPending = Math.Max(maxPending, t.PendingSeconds(simulationSeconds));
                }
            }
        }

        var ms = new MetricSet
        {
            SimulationSeconds = simulationSeconds,
            WriteAttempts = attempts.Count,
            CommandsPerSecond = attempts.Count / seconds,
            EffectiveChanges = effective,
            OrderChangesPerSecond = effective / seconds,
            UnauthorizedWriteAttempts = unauthAttempts,
            UnauthorizedWrites = unauthWrites,
            NativeOverrides = nativeOv,
            LabOverrides = labOv,
            AuthorityTransfers = transfers,
            Preemptions = preemptions,
            AverageCommandLifetime = avgLifetime,
            OscillationPairs = oscillationPairs,
            OscillationFrequency = oscFreq,
            StaleCommandExecutions = stale,
            TaskStarvation = starvation,
            MaxPendingSeconds = maxPending,
            Grade = StabilityGrade.Stable
        };

        return Grade(ms, config.TEval);
    }

    /// <summary>05 §1.5 三级分级：稳定 / 临界 / 抖动。判据不得在各实验里自行解释。</summary>
    public static MetricSet Grade(MetricSet m, double tEval)
    {
        bool stable = m.UnauthorizedWrites == 0 && m.OscillationFrequency <= 0.05 && m.OrderChangesPerSecond <= 0.2;
        bool oscillating = m.OscillationFrequency >= 0.5 || (m.EffectiveChanges > 0 && m.AverageCommandLifetime < 2 * tEval);

        var grade = stable ? StabilityGrade.Stable : (oscillating ? StabilityGrade.Oscillating : StabilityGrade.Marginal);

        return new MetricSet
        {
            SimulationSeconds = m.SimulationSeconds,
            WriteAttempts = m.WriteAttempts,
            CommandsPerSecond = m.CommandsPerSecond,
            EffectiveChanges = m.EffectiveChanges,
            OrderChangesPerSecond = m.OrderChangesPerSecond,
            UnauthorizedWriteAttempts = m.UnauthorizedWriteAttempts,
            UnauthorizedWrites = m.UnauthorizedWrites,
            NativeOverrides = m.NativeOverrides,
            LabOverrides = m.LabOverrides,
            AuthorityTransfers = m.AuthorityTransfers,
            Preemptions = m.Preemptions,
            AverageCommandLifetime = m.AverageCommandLifetime,
            OscillationPairs = m.OscillationPairs,
            OscillationFrequency = m.OscillationFrequency,
            ReactionLatency = m.ReactionLatency,
            StaleCommandExecutions = m.StaleCommandExecutions,
            TaskStarvation = m.TaskStarvation,
            MaxPendingSeconds = m.MaxPendingSeconds,
            ReleaseRecoveryFailures = m.ReleaseRecoveryFailures,
            Grade = grade
        };
    }
}
