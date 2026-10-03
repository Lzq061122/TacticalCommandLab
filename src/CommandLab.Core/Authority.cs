// CommandLab.Core / 控制权：Level 声明 + Lease + 单调 Token + Generation
// 设计依据：04 §3.2.8（Authority Level 谱系）、04 §4.3（Lease 必须配 Fencing）、Phase 6 硬约束 2。

namespace CommandLab.Core;

/// <summary>
/// 授权等级谱系（迁移自 Sheridan / Playbook 自动化等级）。
/// 硬性要求：Authority 必须按 Level 声明，不得做成布尔 HasAuthority（Phase 8 的 H-I 依赖本谱系）。
/// </summary>
public enum AuthorityLevel
{
    None = 0,       // 无授权：不可写
    Monitor = 1,    // 只读观察：不可写
    Recommend = 2,  // 可写，非独占：其它源仍可写（仲裁优先级最低）
    Approval = 3,   // 可写，非独占
    Override = 4,   // 可写，非独占，仲裁优先级最高（文献明确警告的不稳定级）
    Full = 5        // 可写且独占：非 owner 写入在写入口被拒
}

public static class AuthorityRights
{
    public static bool CanWrite(this AuthorityLevel level) => level >= AuthorityLevel.Recommend;
    public static bool IsExclusive(this AuthorityLevel level) => level == AuthorityLevel.Full;

    /// <summary>非独占等级下用于仲裁的优先级；独占等级恒为最高。</summary>
    public static int ArbitrationPriority(this AuthorityLevel level) =>
        level == AuthorityLevel.Full ? 100 : (int)level;
}

public sealed class Lease
{
    public required string LeaseId { get; init; }
    public required string EntityId { get; init; }
    public required CommandDomain Domain { get; init; }
    public required string OwnerId { get; init; }
    public required AuthorityLevel Level { get; init; }
    public required long Token { get; init; }      // 单调 fencing token
    public required long Generation { get; init; } // 签发时的代次
    public required double IssuedAt { get; init; }
    public required double ExpiryAt { get; init; }
}

public sealed class AuthorityRecord
{
    public string OwnerId { get; internal set; } = "Native";
    public AuthorityLevel Level { get; internal set; } = AuthorityLevel.Full;
    public string? LeaseId { get; internal set; }
    public long Token { get; internal set; }           // 当前有效 lease 的 token
    public long MaxSeenToken { get; internal set; }    // 单调上界，fencing 依据
    public long Generation { get; internal set; }      // 每次归属变更 +1
    public double AcquiredAt { get; internal set; } = -1.0;
    public double ExpiryAt { get; internal set; } = double.NegativeInfinity;

    public bool HasActiveLease(double now) => LeaseId is not null && now < ExpiryAt;
}

public enum AuthorityEventKind
{
    Acquire,
    AcquireDenied,
    Renew,
    RenewDenied,
    Release,
    Expire,
    GenerationBump
}

public sealed class AuthorityEvent
{
    public required double Timestamp { get; init; }
    public required AuthorityEventKind Kind { get; init; }
    public required string EntityId { get; init; }
    public required CommandDomain Domain { get; init; }
    public string? OwnerId { get; init; }
    public string? PreviousOwnerId { get; init; }
    public AuthorityLevel Level { get; init; }
    public string? LeaseId { get; init; }
    public long Token { get; init; }
    public long Generation { get; init; }
    public string? Reason { get; init; }
}

/// <summary>归属事件与写入尝试的统一观测面。Telemetry 项目实现它（Core 不反向依赖 Telemetry）。</summary>
public interface ILabObserver
{
    void OnWriteAttempt(WriteAttempt attempt);
    void OnAuthorityEvent(AuthorityEvent authorityEvent);
    void OnTaskEvent(TaskEvent taskEvent);
}

public sealed class NullObserver : ILabObserver
{
    public static readonly NullObserver Instance = new();
    public void OnWriteAttempt(WriteAttempt attempt) { }
    public void OnAuthorityEvent(AuthorityEvent authorityEvent) { }
    public void OnTaskEvent(TaskEvent taskEvent) { }
}

public sealed class AuthorityService
{
    private readonly List<string> _keys = new();                       // 保持注册序，遍历确定
    private readonly Dictionary<string, AuthorityRecord> _records = new(StringComparer.Ordinal);
    private readonly ILabObserver _observer;
    private long _leaseCounter;

    public AuthorityService(ILabObserver observer, string defaultOwnerId = "Native")
    {
        _observer = observer;
        DefaultOwnerId = defaultOwnerId;
    }

    public string DefaultOwnerId { get; }
    public int TransferCount { get; private set; }

    private static string Key(string entityId, CommandDomain domain) =>
        entityId + "|" + domain.ToString();

    public AuthorityRecord GetOrCreate(string entityId, CommandDomain domain)
    {
        var k = Key(entityId, domain);
        if (!_records.TryGetValue(k, out var rec))
        {
            rec = new AuthorityRecord { OwnerId = DefaultOwnerId, Level = AuthorityLevel.Full };
            _records[k] = rec;
            _keys.Add(k);
        }
        return rec;
    }

    public AuthorityRecord? TryGet(string entityId, CommandDomain domain) =>
        _records.TryGetValue(Key(entityId, domain), out var r) ? r : null;

    public IReadOnlyList<string> Keys => _keys;

    public Lease? Acquire(string entityId, CommandDomain domain, string ownerId,
                          AuthorityLevel level, double ttl, double now, bool force = false)
    {
        var rec = GetOrCreate(entityId, domain);
        var held = rec.LeaseId is not null && now < rec.ExpiryAt;
        if (held && rec.OwnerId != ownerId && !force)
        {
            Emit(AuthorityEventKind.AcquireDenied, entityId, domain, rec, ownerId,
                 $"held by {rec.OwnerId} until t={rec.ExpiryAt:0.00}", now);
            return null;
        }

        var prevOwner = rec.OwnerId;
        rec.MaxSeenToken++;
        rec.Token = rec.MaxSeenToken;
        rec.Generation++;
        rec.OwnerId = ownerId;
        rec.Level = level;
        rec.AcquiredAt = now;
        rec.ExpiryAt = now + ttl;
        rec.LeaseId = "L" + (++_leaseCounter);
        TransferCount++;

        var lease = new Lease
        {
            LeaseId = rec.LeaseId,
            EntityId = entityId,
            Domain = domain,
            OwnerId = ownerId,
            Level = level,
            Token = rec.Token,
            Generation = rec.Generation,
            IssuedAt = now,
            ExpiryAt = rec.ExpiryAt
        };

        Emit(AuthorityEventKind.Acquire, entityId, domain, rec, prevOwner,
             $"level={level} ttl={ttl:0.00}", now);
        return lease;
    }

    public bool Renew(Lease lease, double extendSeconds, double now)
    {
        var rec = GetOrCreate(lease.EntityId, lease.Domain);
        if (rec.LeaseId != lease.LeaseId || rec.Token != lease.Token)
        {
            Emit(AuthorityEventKind.RenewDenied, lease.EntityId, lease.Domain, rec, lease.OwnerId,
                 $"token {lease.Token} stale (max_seen {rec.MaxSeenToken})", now);
            return false;
        }
        if (now >= rec.ExpiryAt)
        {
            Emit(AuthorityEventKind.RenewDenied, lease.EntityId, lease.Domain, rec, lease.OwnerId,
                 "lease already expired", now);
            return false;
        }
        rec.ExpiryAt = now + extendSeconds;
        Emit(AuthorityEventKind.Renew, lease.EntityId, lease.Domain, rec, lease.OwnerId,
             $"extended to t={rec.ExpiryAt:0.00}", now);
        return true;
    }

    public bool Release(Lease lease, double now)
    {
        var rec = GetOrCreate(lease.EntityId, lease.Domain);
        if (rec.LeaseId != lease.LeaseId || rec.Token != lease.Token)
        {
            Emit(AuthorityEventKind.Release, lease.EntityId, lease.Domain, rec, lease.OwnerId,
                 $"ignored: token {lease.Token} stale (max_seen {rec.MaxSeenToken})", now);
            return false;
        }
        rec.LeaseId = null;
        rec.Token = 0;
        rec.ExpiryAt = double.NegativeInfinity;
        rec.OwnerId = DefaultOwnerId;
        rec.Level = AuthorityLevel.Full;
        rec.Generation++;
        Emit(AuthorityEventKind.Release, lease.EntityId, lease.Domain, rec, lease.OwnerId,
             $"released to {DefaultOwnerId}", now);
        return true;
    }

    /// <summary>回收过期 lease。按注册序遍历，保证确定性。</summary>
    public int SweepExpired(double now)
    {
        int n = 0;
        foreach (var k in _keys)
        {
            var rec = _records[k];
            if (rec.LeaseId is not null && now >= rec.ExpiryAt)
            {
                var parts = k.Split('|');
                var entityId = parts[0];
                var domain = (CommandDomain)Enum.Parse(typeof(CommandDomain), parts[1]);
                rec.LeaseId = null;
                rec.Token = 0;
                rec.ExpiryAt = double.NegativeInfinity;
                rec.OwnerId = DefaultOwnerId;
                rec.Level = AuthorityLevel.Full;
                rec.Generation++;
                Emit(AuthorityEventKind.Expire, entityId, domain, rec, rec.OwnerId, "lease expired", now);
                n++;
            }
        }
        return n;
    }

    private void Emit(AuthorityEventKind kind, string entityId, CommandDomain domain,
                      AuthorityRecord rec, string? previousOwner, string? reason, double now = -1.0)
    {
        _observer.OnAuthorityEvent(new AuthorityEvent
        {
            Timestamp = now < 0 ? rec.AcquiredAt : now,
            Kind = kind,
            EntityId = entityId,
            Domain = domain,
            OwnerId = rec.OwnerId,
            PreviousOwnerId = previousOwner,
            Level = rec.Level,
            LeaseId = rec.LeaseId,
            Token = rec.Token,
            Generation = rec.Generation,
            Reason = reason
        });
    }
}
