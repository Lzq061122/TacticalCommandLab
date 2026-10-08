using System.Globalization;
using CommandLab.Core;

namespace CommandLab.Telemetry;

/// <summary>Production hold-window accumulator used by the host probe and offline tests.</summary>
public sealed class HoldMeter
{
    private double _windowStart = -1.0;
    private double _lastSample = -1.0;
    private double _held;
    private double _cumHeld;
    private double _cumWindow;
    private bool _open;
    private bool _hasWindowSample;
    private CommandValue _lastActual = CommandValue.Unset;

    public CommandValue Intent { get; private set; } = CommandValue.Unset;
    public int Samples { get; private set; }
    public int WindowSamples { get; private set; }
    public int Matches { get; private set; }
    public int Windows { get; private set; }
    public bool IsOpen => _open;
    public double WindowSeconds { get; private set; }
    public double CumulativeSeconds => _cumWindow;
    public double HeldSeconds => _cumHeld + _held;

    public void Open(double now, CommandValue intent)
    {
        if (_open) Close(now);
        _windowStart = now;
        _lastSample = now;
        _held = 0.0;
        _open = true;
        _hasWindowSample = false;
        _lastActual = CommandValue.Unset;
        WindowSamples = 0;
        Intent = intent;
        WindowSeconds = 0.0;
    }

    public void Sample(double now, CommandValue actual)
    {
        if (!_open || now <= _lastSample) return;
        var dt = now - _lastSample;
        Samples++;
        WindowSamples++;
        if (actual == Intent) { _held += dt; Matches++; }
        _lastSample = now;
        _lastActual = actual;
        _hasWindowSample = true;
        WindowSeconds = Math.Max(0.0, now - _windowStart);
    }

    /// <summary>Integrates the final sampled state to the window boundary; an unsampled window stays zero.</summary>
    public double Close(double now)
    {
        if (_open)
        {
            if (_hasWindowSample && now > _lastSample) Sample(now, _lastActual);
            var w = _hasWindowSample ? Math.Max(0.0, Math.Max(now, _lastSample) - _windowStart) : 0.0;
            _cumWindow += w;
            _cumHeld += _held;
            _held = 0.0;
            WindowSeconds = w;
            Windows++;
            _open = false;
        }
        return Share();
    }

    public double Share()
    {
        var total = _cumWindow + (_open ? WindowSeconds : 0.0);
        return total > 0 ? (_cumHeld + _held) / total : 0.0;
    }
}

/// <summary>Associates accepted Lab writes with the first later native override in the same entity/domain.</summary>
public sealed class NativeOverrideTracker
{
    private readonly struct Key : IEquatable<Key>
    {
        public Key(string entityId, CommandDomain domain) { EntityId = entityId; Domain = domain; }
        public string EntityId { get; }
        public CommandDomain Domain { get; }
        public bool Equals(Key other) => Domain == other.Domain && string.Equals(EntityId, other.EntityId, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => (StringComparer.Ordinal.GetHashCode(EntityId) * 397) ^ (int)Domain;
    }

    private readonly Dictionary<Key, double> _lastLabChangeAt = new();
    private readonly string _labWriterId;
    private readonly string _nativeWriterId;

    public NativeOverrideTracker(string labWriterId, string nativeWriterId)
    {
        _labWriterId = labWriterId;
        _nativeWriterId = nativeWriterId;
    }

    public int AcceptedLabChanges { get; private set; }
    public double FirstNativeOverrideLatency { get; private set; } = -1.0;

    public void Observe(WriteAttempt attempt)
    {
        if (attempt.Rejected || !attempt.OrderChanged) return;
        var key = new Key(attempt.EntityId, attempt.Domain);
        if (string.Equals(attempt.WriterId, _labWriterId, StringComparison.Ordinal))
        {
            _lastLabChangeAt[key] = attempt.Timestamp;
            AcceptedLabChanges++;
        }
        else if (string.Equals(attempt.WriterId, _nativeWriterId, StringComparison.Ordinal) &&
                 FirstNativeOverrideLatency < 0.0 && _lastLabChangeAt.TryGetValue(key, out var labAt) &&
                 attempt.Timestamp >= labAt)
        {
            FirstNativeOverrideLatency = attempt.Timestamp - labAt;
        }
    }
}

/// <summary>Pure pass/fail calculations for experiment criteria; data collection remains in ProbeRuntime.</summary>
public static class ExperimentVerdicts
{
    public static bool H1AcceptedLabChanges(int acceptedLabChanges) => acceptedLabChanges > 0;

    public static bool H1ObservedNativeOverride(double firstNativeOverrideLatency) =>
        firstNativeOverrideLatency >= 0.0;

    public static bool HoldTargetMet(HoldMeter? hold) => hold is not null && hold.Share() >= 0.95;

    public static bool ReleaseRecoveryPassed(int releaseRecoveryFailures) => releaseRecoveryFailures == 0;
}

/// <summary>Stable first-seen projection into the Core observation slots, shared by probe and offline tests.</summary>
public sealed class HostOrderProjection
{
    private static readonly CommandValue[] Slots =
    {
        CommandValue.Obs0, CommandValue.Obs1, CommandValue.Obs2, CommandValue.Obs3,
        CommandValue.Obs4, CommandValue.Obs5, CommandValue.Obs6, CommandValue.Obs7,
        CommandValue.Obs8, CommandValue.Obs9, CommandValue.Obs10, CommandValue.Obs11,
        CommandValue.Obs12, CommandValue.Obs13, CommandValue.Obs14, CommandValue.Obs15
    };

    private readonly Dictionary<string, CommandValue> _map = new(StringComparer.Ordinal);
    private int _next;
    public int WrapCount { get; private set; }

    public CommandValue Project(string rawKey)
    {
        if (_map.TryGetValue(rawKey, out var v)) return v;
        if (_next >= Slots.Length) { _next = 0; WrapCount++; }
        v = Slots[_next++];
        _map[rawKey] = v;
        return v;
    }

    public string Describe() => string.Format(CultureInfo.InvariantCulture, "distinct={0} wrap={1}", _map.Count, WrapCount);
}
