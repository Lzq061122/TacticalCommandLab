// CommandLab.Core / 单一写入口 WritePort
// 硬约束（05 §8）：Reject 与 Token/Generation 校验必须实现在本文件内部，不得放进 Source 的决策函数。
// 结构保证：ControlledEntity 的状态 setter 是 internal，只有本类（同 assembly）能改 -> 外部 Source 无法旁路写入口。

namespace CommandLab.Core;

public enum WritePath
{
    /// <summary>受控路径：宿主 Behavior 侧 Movement/Facing 写入，Mod 可拦截（对应 02 门控矩阵的 gated 侧）。</summary>
    Gated = 0,

    /// <summary>自维护路径：Arrangement 双 Timer / SubstituteOrder 兜底，不受 IsAIControlled 门控（02 §8.2）。</summary>
    NonGated = 1
}

public enum ArbitrationMode
{
    None,      // 提交序全部生效 —— Last Writer Wins 基线
    Priority,  // 同 tick 多候选按 (Priority, WriterId) 择一
    Utility    // 同 tick 多候选按 (Utility, WriterId) 择一
}

public static class RejectReason
{
    public const string NotOwner = "NotOwner";
    public const string LevelNoWriteRight = "LevelNoWriteRight";
    public const string StaleLease = "StaleLease";
    public const string StaleGeneration = "StaleGeneration";
    public const string MinLifetime = "MinLifetime";
    public const string Hysteresis = "Hysteresis";
    public const string LostArbitration = "LostArbitration";
}

public sealed class WritePortPolicy
{
    /// <summary>gated 路径是否做归属校验。置 false 用于 E3 3c（Reject 在决策器）对照组。</summary>
    public bool EnforceAuthorityOnGated { get; init; } = true;

    /// <summary>non-gated 路径是否做归属校验。默认 false —— 这是 02 门控矩阵的建模，不是漏洞。</summary>
    public bool EnforceAuthorityOnNonGated { get; init; } = false;

    /// <summary>Fencing（Token/Generation）校验。默认开。</summary>
    public bool EnforceFencing { get; init; } = true;

    public ArbitrationMode Arbitration { get; init; } = ArbitrationMode.None;

    /// <summary>命令最短驻留时间（秒），0 表示不启用。</summary>
    public double MinLifetimeSeconds { get; init; } = 0.0;

    /// <summary>迟滞带：新候选效用须 ≥ 当前效用 ×(1+ratio)。0 表示不启用（对应 02 的 PreserveExpireTime）。</summary>
    public double HysteresisRatio { get; init; } = 0.0;

    /// <summary>值变换层（E10）：只改值、永不抑制，带异常兜底。与 Authority 正交。</summary>
    public Func<WriteRequest, CommandValue>? ValueTransform { get; init; }
}

public sealed class WriteRequest
{
    public required string EntityId { get; init; }
    public required CommandDomain Domain { get; init; }
    public required CommandValue Value { get; init; }
    public required string WriterId { get; init; }
    public WritePath Path { get; init; } = WritePath.Gated;
    public double Priority { get; init; }
    public double Utility { get; init; }
    public string? LeaseId { get; init; }
    public long LeaseToken { get; init; }
    public long Generation { get; init; }
    public string? TaskId { get; init; }
    public string? TaskState { get; init; }
    public string? IntentId { get; init; }
    public string? Reason { get; init; }
}

public sealed class WriteAttempt
{
    public required double Timestamp { get; init; }
    public required long Tick { get; init; }
    public required string EntityId { get; init; }
    public required CommandDomain Domain { get; init; }
    public required WritePath Path { get; init; }
    public required string WriterId { get; init; }
    public required string OwnerAtWrite { get; init; }
    public required AuthorityLevel AuthorityLevel { get; init; }
    public string? LeaseId { get; init; }
    public long LeaseToken { get; init; }
    public long MaxSeenToken { get; init; }
    public long Generation { get; init; }
    public long RecordGeneration { get; init; }
    public double LeaseExpiryAt { get; init; }
    public required CommandValue Command { get; init; }
    public required CommandValue PrevValue { get; init; }
    public required CommandValue NewValue { get; init; }
    public bool OriginalMethodExecuted { get; init; }
    public bool OrderChanged { get; init; }
    public bool Rejected { get; init; }
    public string? RejectReasonText { get; init; }
    public string? TaskId { get; init; }
    public string? TaskState { get; init; }
    public string? IntentId { get; init; }
    public double Utility { get; init; }
    public double Priority { get; init; }
    public string? Decision { get; init; }
    public string? Reason { get; init; }
}

public sealed class WritePort
{
    private readonly struct Pending
    {
        public readonly int Seq;
        public readonly WriteRequest Req;
        public Pending(int seq, WriteRequest req) { Seq = seq; Req = req; }
    }

    private readonly ILabClock _clock;
    private readonly AuthorityService _authority;
    private readonly ILabObserver _observer;
    private WritePortPolicy _policy;
    private readonly Dictionary<string, ControlledEntity> _entities = new(StringComparer.Ordinal);
    private readonly List<Pending> _pending = new();
    private int _seq;

    public WritePort(ILabClock clock, AuthorityService authority, ILabObserver observer, WritePortPolicy policy)
    {
        _clock = clock;
        _authority = authority;
        _observer = observer;
        _policy = policy;
    }

    public WritePortPolicy Policy => _policy;

    /// <summary>
    /// 整体替换策略（宿主侧运行时切换对照条件用）。判定逻辑位置不变，仍在本类 Evaluate 内。
    /// </summary>
    public void SetPolicy(WritePortPolicy policy) => _policy = policy;

    public void RegisterEntity(ControlledEntity e) => _entities[e.Id] = e;

    public ControlledEntity Entity(string id) => _entities[id];

    /// <summary>提交候选。同一 tick 内先缓存，FlushTick 时统一裁决（保证仲裁与提交序无关）。</summary>
    public void Submit(WriteRequest req) => _pending.Add(new Pending(_seq++, req));

    /// <summary>
    /// 立即裁决单个候选并返回结果。宿主侧（Harmony Prefix）使用：调用方必须同步知道"放行 / 拦截"，
    /// 无法等到帧末 FlushTick。语义与 Submit+FlushTick 完全一致（同一 Evaluate），只是不做跨提交批仲裁。
    /// </summary>
    public WriteAttempt SubmitImmediate(WriteRequest req)
    {
        var single = new List<Pending> { new Pending(_seq++, req) };
        var resolved = Resolve(req.EntityId, req.Domain, single);
        return Evaluate(resolved[0]);
    }

    public void FlushTick()
    {
        if (_pending.Count == 0) return;

        // 分组：按首次出现顺序，避免 Dictionary 遍历序影响输出
        var order = new List<(string Entity, CommandDomain Domain)>();
        var groups = new Dictionary<(string, CommandDomain), List<Pending>>();
        foreach (var p in _pending)
        {
            var key = (p.Req.EntityId, p.Req.Domain);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Pending>();
                groups[key] = list;
                order.Add(key);
            }
            list.Add(p);
        }
        _pending.Clear();

        foreach (var key in order)
        {
            var list = groups[key];
            foreach (var p in Resolve(key.Entity, key.Domain, list))
                Evaluate(p);
        }
    }

    private List<Pending> Resolve(string entityId, CommandDomain domain, List<Pending> list)
    {
        if (_policy.Arbitration == ArbitrationMode.None || list.Count <= 1)
            return list;

        var ranked = new List<Pending>(list);
        // 稳定且顺序无关的排序键：同分时按 WriterId 字典序，而不是提交序
        ranked.Sort((a, b) =>
        {
            int c = _policy.Arbitration == ArbitrationMode.Priority
                ? b.Req.Priority.CompareTo(a.Req.Priority)
                : b.Req.Utility.CompareTo(a.Req.Utility);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Req.WriterId, b.Req.WriterId);
        });

        for (int i = 1; i < ranked.Count; i++)
        {
            var loser = ranked[i];
            Reject(loser.Req, _authority.GetOrCreate(loser.Req.EntityId, loser.Req.Domain),
                   RejectReason.LostArbitration,
                   $"lost arbitration to {ranked[0].Req.WriterId} ({_policy.Arbitration})");
        }
        return new List<Pending> { ranked[0] };
    }

    private WriteAttempt Evaluate(Pending p)
    {
        var req = p.Req;
        var entity = _entities[req.EntityId];
        var state = entity[req.Domain];
        var rec = _authority.GetOrCreate(req.EntityId, req.Domain);

        var now = _clock.Now;
        var prev = state.Value;
        bool rejected = false;
        string? rejectReason = null;
        string reason = req.Reason ?? "accepted";

        var value = req.Value;
        if (_policy.ValueTransform is not null)
        {
            try { value = _policy.ValueTransform(req); }
            catch { /* 变换层异常兜底：保持原值，不抑制写入（对齐 RBM OverrideSetMovementOrder 的 try/catch 语义） */ }
        }

        bool enforceAuthority = req.Path == WritePath.Gated
            ? _policy.EnforceAuthorityOnGated
            : _policy.EnforceAuthorityOnNonGated;

        if (enforceAuthority)
        {
            bool isOwner = string.Equals(rec.OwnerId, req.WriterId, StringComparison.Ordinal);
            if (isOwner && !rec.Level.CanWrite())
            {
                rejected = true;
                rejectReason = RejectReason.LevelNoWriteRight;
                reason = $"level {rec.Level} does not grant write right";
            }
            else if (!isOwner && rec.Level.IsExclusive())
            {
                rejected = true;
                rejectReason = RejectReason.NotOwner;
                reason = $"{req.Domain} authority owned by {rec.OwnerId} under {rec.Level} lease";
            }
        }

        if (!rejected && _policy.EnforceFencing)
        {
            if (req.LeaseId is not null &&
                (!string.Equals(req.LeaseId, rec.LeaseId, StringComparison.Ordinal) || req.LeaseToken < rec.MaxSeenToken))
            {
                rejected = true;
                rejectReason = RejectReason.StaleLease;
                reason = $"stale lease: token {req.LeaseToken} < max_seen {rec.MaxSeenToken}";
            }
            else if (req.Generation > 0 && req.Generation < rec.Generation)
            {
                rejected = true;
                rejectReason = RejectReason.StaleGeneration;
                reason = $"stale generation: {req.Generation} < current {rec.Generation}";
            }
        }

        if (!rejected && _policy.MinLifetimeSeconds > 0 && state.LastWriteAt >= 0 &&
            now - state.LastWriteAt < _policy.MinLifetimeSeconds)
        {
            rejected = true;
            rejectReason = RejectReason.MinLifetime;
            reason = $"min lifetime not elapsed ({(_policy.MinLifetimeSeconds - (now - state.LastWriteAt)):0.00}s left)";
        }

        if (!rejected && _policy.HysteresisRatio > 0 && state.LastUtility > 0 &&
            req.Utility < state.LastUtility * (1.0 + _policy.HysteresisRatio))
        {
            rejected = true;
            rejectReason = RejectReason.Hysteresis;
            reason = $"hysteresis: utility {req.Utility:0.000} < {state.LastUtility * (1.0 + _policy.HysteresisRatio):0.000}";
        }

        bool orderChanged = false;
        if (!rejected)
        {
            orderChanged = value != state.Value;
            state.Commit(value, req.WriterId, req.Utility, now);
        }

        bool staleButAccepted = !rejected && (
            (req.LeaseId is not null && (req.LeaseToken < rec.MaxSeenToken ||
             !string.Equals(req.LeaseId, rec.LeaseId, StringComparison.Ordinal))) ||
            (req.Generation > 0 && req.Generation < rec.Generation));

        var attempt = new WriteAttempt
        {
            Timestamp = now,
            Tick = _clock.Step,
            EntityId = req.EntityId,
            Domain = req.Domain,
            Path = req.Path,
            WriterId = req.WriterId,
            OwnerAtWrite = rec.OwnerId,
            AuthorityLevel = rec.Level,
            LeaseId = req.LeaseId,
            LeaseToken = req.LeaseToken,
            MaxSeenToken = rec.MaxSeenToken,
            Generation = req.Generation,
            RecordGeneration = rec.Generation,
            LeaseExpiryAt = rec.ExpiryAt,
            Command = req.Value,
            PrevValue = prev,
            NewValue = rejected ? prev : value,
            OriginalMethodExecuted = !rejected,
            OrderChanged = orderChanged,
            Rejected = rejected,
            RejectReasonText = rejectReason,
            TaskId = req.TaskId,
            TaskState = req.TaskState,
            IntentId = req.IntentId,
            Utility = req.Utility,
            Priority = req.Priority,
            Decision = rejected ? "reject write" : (orderChanged ? "apply write" : "apply write (no change)"),
            Reason = reason
        };

        _observer.OnWriteAttempt(attempt);
        return attempt;
    }

    private void Reject(WriteRequest req, AuthorityRecord rec, string rejectReason, string reason)
    {
        var now = _clock.Now;
        var entity = _entities[req.EntityId];
        var prev = entity[req.Domain].Value;
        _observer.OnWriteAttempt(new WriteAttempt
        {
            Timestamp = now,
            Tick = _clock.Step,
            EntityId = req.EntityId,
            Domain = req.Domain,
            Path = req.Path,
            WriterId = req.WriterId,
            OwnerAtWrite = rec.OwnerId,
            AuthorityLevel = rec.Level,
            LeaseId = req.LeaseId,
            LeaseToken = req.LeaseToken,
            MaxSeenToken = rec.MaxSeenToken,
            Generation = req.Generation,
            RecordGeneration = rec.Generation,
            LeaseExpiryAt = rec.ExpiryAt,
            Command = req.Value,
            PrevValue = prev,
            NewValue = prev,
            OriginalMethodExecuted = false,
            OrderChanged = false,
            Rejected = true,
            RejectReasonText = rejectReason,
            TaskId = req.TaskId,
            TaskState = req.TaskState,
            IntentId = req.IntentId,
            Utility = req.Utility,
            Priority = req.Priority,
            Decision = "reject write",
            Reason = reason
        });
    }
}
