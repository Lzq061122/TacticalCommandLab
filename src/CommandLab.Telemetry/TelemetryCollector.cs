// CommandLab.Telemetry / 采集 + 可重放哈希 + DecisionTrace 渲染
// 依据：05 §1.4 必录字段（含被拒写入）；字段名与 Phase 10 Harmony 实测保持一致，尤其 OriginalMethodExecuted。

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommandLab.Core;

namespace CommandLab.Telemetry;

public sealed class TelemetryCollector : ILabObserver
{
    private readonly List<WriteAttempt> _attempts = new();
    private readonly List<AuthorityEvent> _authority = new();
    private readonly List<TaskEvent> _tasks = new();

    public IReadOnlyList<WriteAttempt> Attempts => _attempts;
    public IReadOnlyList<AuthorityEvent> AuthorityEvents => _authority;
    public IReadOnlyList<TaskEvent> TaskEvents => _tasks;

    public void OnWriteAttempt(WriteAttempt attempt) => _attempts.Add(attempt);
    public void OnAuthorityEvent(AuthorityEvent e) => _authority.Add(e);
    public void OnTaskEvent(TaskEvent e) => _tasks.Add(e);

    public void Clear()
    {
        _attempts.Clear();
        _authority.Clear();
        _tasks.Clear();
    }

    /// <summary>规范化的单行序列化。同一 (scenario, seed, 配置) 必须产出逐字节相同的行序列。</summary>
    public static string CanonicalLine(WriteAttempt a)
    {
        static string N(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("t=").Append(N(a.Timestamp))
          .Append("|tick=").Append(a.Tick)
          .Append("|E=").Append(a.EntityId)
          .Append("|D=").Append(a.Domain)
          .Append("|P=").Append(a.Path)
          .Append("|W=").Append(a.WriterId)
          .Append("|O=").Append(a.OwnerAtWrite)
          .Append("|L=").Append((int)a.AuthorityLevel)
          .Append("|lease=").Append(a.LeaseId ?? "-")
          .Append("|tok=").Append(a.LeaseToken)
          .Append("|max=").Append(a.MaxSeenToken)
          .Append("|gen=").Append(a.Generation)
          .Append("|rgen=").Append(a.RecordGeneration)
          .Append("|exp=").Append(N(a.LeaseExpiryAt))
          .Append("|cmd=").Append(a.Command)
          .Append("|prev=").Append(a.PrevValue)
          .Append("|new=").Append(a.NewValue)
          .Append("|ome=").Append(a.OriginalMethodExecuted ? 1 : 0)
          .Append("|chg=").Append(a.OrderChanged ? 1 : 0)
          .Append("|rej=").Append(a.Rejected ? 1 : 0)
          .Append("|why=").Append(a.RejectReasonText ?? "-")
          .Append("|task=").Append(a.TaskId ?? "-")
          .Append("|ts=").Append(a.TaskState ?? "-")
          .Append("|int=").Append(a.IntentId ?? "-")
          .Append("|u=").Append(N(a.Utility))
          .Append("|p=").Append(N(a.Priority));
        return sb.ToString();
    }

    public IEnumerable<string> CanonicalLines()
    {
        foreach (var a in _attempts) yield return CanonicalLine(a);
        foreach (var e in _authority)
            yield return string.Format(CultureInfo.InvariantCulture,
                "AUTH|t={0:F4}|{1}|E={2}|D={3}|own={4}|prev={5}|L={6}|lease={7}|tok={8}|gen={9}|why={10}",
                e.Timestamp, e.Kind, e.EntityId, e.Domain, e.OwnerId, e.PreviousOwnerId,
                (int)e.Level, e.LeaseId, e.Token, e.Generation, e.Reason);
        foreach (var e in _tasks)
            yield return string.Format(CultureInfo.InvariantCulture,
                "TASK|t={0:F4}|{1}|{2}->{3}|why={4}",
                e.Timestamp, e.TaskId, e.From, e.To, e.Reason);
    }

    public string ReplayHash()
    {
        using var sha = SHA256.Create();
        foreach (var line in CanonicalLines())
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        // BitConverter 而非 Convert.ToHexString：后者是 net5+ API，netstandard2.0 不存在。
        return BitConverter.ToString(sha.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>05 §1.4 的人类可读 DecisionTrace。</summary>
    public static string Trace(WriteAttempt a)
    {
        static string N(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        var expiryText = double.IsNegativeInfinity(a.LeaseExpiryAt) ? "-" : N(a.LeaseExpiryAt);
        sb.AppendLine($"t={N(a.Timestamp)}  {a.EntityId}  Domain={a.Domain}  Owner={a.OwnerAtWrite}  Level={(int)a.AuthorityLevel}  Lease={a.LeaseId ?? "-"}  Token={a.LeaseToken}  Expiry={expiryText}");
        sb.AppendLine($"Writer={a.WriterId}  Path={a.Path}  OriginalMethodExecuted={(a.OriginalMethodExecuted ? "true" : "false")}  OrderChanged={(a.OrderChanged ? "true" : "false")}");
        sb.AppendLine($"Command={a.Command}  PrevValue={a.PrevValue}  NewValue={a.NewValue}");
        var rejectedText = a.Rejected ? "true" : "false";
        var rejectWhy = a.Rejected ? a.RejectReasonText : "-";
        sb.Append($"Rejected={rejectedText}  Reason={rejectWhy}  max_seen_token={a.MaxSeenToken}  gen={a.Generation}/{a.RecordGeneration}");
        if (a.TaskId is not null) sb.Append($"  Task={a.TaskId}({a.TaskState})");
        if (a.IntentId is not null) sb.Append($"  Intent={a.IntentId}");
        sb.AppendLine();
        sb.Append($"Decision: {a.Decision}").AppendLine();
        sb.Append($"Reason: {a.Reason}");
        return sb.ToString();
    }

    public IEnumerable<string> Traces()
    {
        foreach (var a in _attempts) yield return Trace(a);
    }
}
