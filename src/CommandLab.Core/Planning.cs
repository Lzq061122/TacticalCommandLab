// CommandLab.Core / Intent + Task（含生命周期与终止条件）+ PreemptionPolicy + 观测记录类型
// 依据：05 §8 施工清单 —— Task 生命周期被 E2 2c / E5 / E6 逼出来；Transition 语义被 04 §6.4 第 1 条强制要求显式建模。

namespace CommandLab.Core;

public enum TerminationCondition
{
    TimeElapsed,      // 持续指定时长后 Done
    UntilSuperseded,  // 被后继 Intent 取代才结束
    Manual            // 只能由外部显式结束
}

public sealed class Intent
{
    public required string Id { get; init; }
    public required CommandValue Desired { get; init; }
    public double DurationSeconds { get; init; }
    public TerminationCondition Termination { get; init; } = TerminationCondition.TimeElapsed;
    public string? Reason { get; init; }
}

public enum TaskState
{
    Pending,
    Active,
    Preempted,
    Done,
    Cancelled
}

/// <summary>抢占恢复语义（04 §4.4 实时系统 Mode Change 三协议）。选择依据是数据一致性，不是性能。</summary>
public enum PreemptionPolicy
{
    Resume,   // 恢复旧任务上下文（Bounded）
    Restart,  // 丢弃上下文重来（Phased），成本更高但状态干净
    Cancel    // 立即终止不做清理（Immediate）
}

public sealed class CommandTask
{
    public required string Id { get; init; }
    public required string IntentId { get; init; }
    public required string OwnerId { get; init; }
    public required string EntityId { get; init; }
    public required CommandDomain Domain { get; init; }
    public required double DurationSeconds { get; init; }
    public PreemptionPolicy Policy { get; init; } = PreemptionPolicy.Cancel;
    public TaskState State { get; internal set; } = TaskState.Pending;
    public double CreatedAt { get; init; }
    public double ActivatedAt { get; internal set; } = -1.0;
    public double PreemptedAt { get; internal set; } = -1.0;
    public double EndedAt { get; internal set; } = -1.0;
    public int RestartCount { get; internal set; }
    public int PreemptCount { get; internal set; }

    public double PendingSeconds(double now) => State == TaskState.Pending ? now - CreatedAt : 0.0;
}

public sealed class TaskEvent
{
    public required double Timestamp { get; init; }
    public required string TaskId { get; init; }
    public required TaskState From { get; init; }
    public required TaskState To { get; init; }
    public string? Reason { get; init; }
}

public sealed class TaskRegistry
{
    private readonly List<CommandTask> _tasks = new();   // 注册序 = 遍历序
    private readonly ILabObserver _observer;

    public TaskRegistry(ILabObserver observer) => _observer = observer;

    public IReadOnlyList<CommandTask> Tasks => _tasks;

    public CommandTask Create(CommandTask task)
    {
        _tasks.Add(task);
        Emit(task, TaskState.Pending, TaskState.Pending, task.CreatedAt, "created");
        return task;
    }

    public void Activate(CommandTask task, double now)
    {
        if (task.State != TaskState.Pending) return;
        var from = task.State;
        task.State = TaskState.Active;
        task.ActivatedAt = now;
        Emit(task, from, task.State, now, "activated");
    }

    public void Preempt(CommandTask task, double now, string reason)
    {
        if (task.State is not (TaskState.Active or TaskState.Pending)) return;
        var from = task.State;
        task.State = TaskState.Preempted;
        task.PreemptedAt = now;
        task.PreemptCount++;
        Emit(task, from, task.State, now, reason);
    }

    public void Complete(CommandTask task, double now, string reason)
    {
        var from = task.State;
        task.State = TaskState.Done;
        task.EndedAt = now;
        Emit(task, from, task.State, now, reason);
    }

    public void Cancel(CommandTask task, double now, string reason)
    {
        var from = task.State;
        task.State = TaskState.Cancelled;
        task.EndedAt = now;
        Emit(task, from, task.State, now, reason);
    }

    /// <summary>按策略恢复被抢占任务。Restart 会计一次重启成本（E5 需要量化它）。</summary>
    public void Recover(CommandTask task, double now, PreemptionPolicy policy)
    {
        if (task.State != TaskState.Preempted) return;
        var from = task.State;
        switch (policy)
        {
            case PreemptionPolicy.Resume:
                task.State = TaskState.Active;
                Emit(task, from, task.State, now, "resume: restore previous context");
                break;
            case PreemptionPolicy.Restart:
                task.RestartCount++;
                task.ActivatedAt = now;
                task.State = TaskState.Active;
                Emit(task, from, task.State, now, "restart: discard context and re-run");
                break;
            case PreemptionPolicy.Cancel:
                task.State = TaskState.Cancelled;
                task.EndedAt = now;
                Emit(task, from, task.State, now, "cancel: no cleanup");
                break;
        }
    }

    /// <summary>到期扫描：TimeElapsed 类型任务到期自动 Done。按注册序遍历，保证确定性。</summary>
    public void Sweep(double now)
    {
        foreach (var t in _tasks)
        {
            if (t.State != TaskState.Active) continue;
            if (t.DurationSeconds <= 0) continue;
            if (t.ActivatedAt >= 0 && now - t.ActivatedAt >= t.DurationSeconds)
                Complete(t, now, "duration elapsed");
        }
    }

    private void Emit(CommandTask task, TaskState from, TaskState to, double now, string? reason) =>
        _observer.OnTaskEvent(new TaskEvent
        {
            Timestamp = now,
            TaskId = task.Id,
            From = from,
            To = to,
            Reason = reason
        });
}
