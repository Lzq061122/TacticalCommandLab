// CommandLab.Simulation / 原生侧仿真源 Ns
// 建模红线（05 §1.3 + Phase 6 硬约束）：gated 与 non-gated 必须是两条独立路径，禁止合并实现。
//    gated     = Behavior 侧 Movement/Facing 写入，可被 IsAIControlled 门控
//    non-gated = Arrangement 双 Timer + SubstituteOrder 兜底回写，不受门控（02 §8.2）
// 参数全部取自 02 的 IL 取证：T_eval=0.5s、错峰 ×0.1、PreserveExpireTime 迟滞、AllowAiTicking 全局闸。

using CommandLab.Core;

namespace CommandLab.Simulation;

public enum NsCommandMode
{
    Fixed,          // 恒定输出一个命令
    Alternate,      // 每次评估交替（用于制造确定性翻转）
    UtilityNoise    // 效用带播种噪声（E2 2b）
}

public sealed class NativeSourceConfig
{
    /// <summary>写入者标识。必须与场景的 DefaultOwnerId 一致，否则所有 Ns 写入都会被计为越权（度量口径陷阱）。</summary>
    public string WriterId { get; init; } = "Native";

    public double TEval { get; init; } = 0.5;
    public double PhaseOffset { get; init; } = 0.0;
    public bool AllowAiTicking { get; init; } = true;
    public CommandDomain[] GatedDomains { get; init; } = { CommandDomain.Movement, CommandDomain.Facing };
    public CommandDomain[] NonGatedDomains { get; init; } = { CommandDomain.Arrangement };

    public NsCommandMode Mode { get; init; } = NsCommandMode.Fixed;
    public CommandValue FixedValue { get; init; } = CommandValue.ADVANCE;
    public CommandValue AlternateWith { get; init; } = CommandValue.RETREAT;
    public double UtilityBase { get; init; } = 1.0;
    public double UtilityNoiseSigma { get; init; } = 0.0;

    /// <summary>仲裁优先级（E12 新增，默认 0 —— 与既有实验完全一致，不改变 E1–E11 任何结果）。
    /// 用途：让 Authority Level 谱系在「存在权重居于其间竞争者」的条件下可观测（04 §3.2.8）。</summary>
    public double Priority { get; init; } = 0.0;

    /// <summary>PreserveExpireTime 迟滞：非当前行为折扣。</summary>
    public bool Hysteresis { get; init; } = false;
    public double HysteresisDiscount { get; init; } = 0.15;

    /// <summary>Arrangement 自维护双 Timer 周期（02 已证两个独立 Timer）。</summary>
    public double[] SelfMaintenancePeriods { get; init; } = { 0.5, 1.2 };
    public CommandValue SelfMaintenanceValue { get; init; } = CommandValue.MOVE_A;
    public bool SubstituteOrderFallback { get; init; } = true;

    /// <summary>E5 Class T：持有宿主内部状态 —— 被写时创建常驻自维护定时器。</summary>
    public bool CreateTimersOnWrite { get; init; } = false;
    public double CreatedTimerPeriod { get; init; } = 0.8;
}

public sealed class NativeSource : ICommandSource
{
    private sealed class Timer
    {
        public double Period;
        public double Next;
        public bool Orphan;   // 由被抢占/被取消的任务遗留，无人负责清理
    }

    private readonly ScenarioContext _ctx;
    private readonly NativeSourceConfig _cfg;
    private readonly List<Timer> _timers = new();
    private double _nextEval;
    private int _evalCount;
    private CommandValue _lastIssued = CommandValue.Unset;

    public NativeSource(ScenarioContext ctx, NativeSourceConfig cfg)
    {
        _ctx = ctx;
        _cfg = cfg;
        _nextEval = cfg.PhaseOffset;
        foreach (var p in cfg.SelfMaintenancePeriods)
            _timers.Add(new Timer { Period = p, Next = p });
    }

    public string Id => _cfg.WriterId;

    public int ActiveTimerCount => _timers.Count;
    public int OrphanTimerCount => _timers.Count(t => t.Orphan);
    public int TimersCreated { get; private set; }
    public int NonGatedWrites { get; private set; }
    public int GatedWrites { get; private set; }

    public void Evaluate()
    {
        if (!_cfg.AllowAiTicking) return;
        var now = _ctx.Clock.Now;

        // ---- gated 路径：周期性评估并写入 ----
        if (now + 1e-9 >= _nextEval)
        {
            _nextEval += _cfg.TEval;
            _evalCount++;
            foreach (var d in _cfg.GatedDomains)
            {
                var value = Choose(d);
                var utility = Utility();
                GatedWrites++;
                _ctx.Port.Submit(new WriteRequest
                {
                    EntityId = _ctx.Entity.Id,
                    Domain = d,
                    Value = value,
                    WriterId = Id,
                    Path = WritePath.Gated,
                    Priority = _cfg.Priority,
                    Utility = utility,
                    Reason = "native behavior evaluation"
                });
            }
        }

        // ---- non-gated 路径：常驻自维护定时器（不受 IsAIControlled 门控）----
        if (_cfg.CreateTimersOnWrite) ScanForTimerCreation();

        for (int i = 0; i < _timers.Count; i++)
        {
            var t = _timers[i];
            if (now + 1e-9 < t.Next) continue;
            t.Next += t.Period;
            foreach (var d in _cfg.NonGatedDomains)
            {
                NonGatedWrites++;
                _ctx.Port.Submit(new WriteRequest
                {
                    EntityId = _ctx.Entity.Id,
                    Domain = d,
                    Value = _cfg.SelfMaintenanceValue,
                    WriterId = Id,
                    Path = WritePath.NonGated,
                    Priority = _cfg.Priority,
                    Utility = _cfg.UtilityBase,
                    Reason = t.Orphan ? "self-maintenance timer (orphan)" : "self-maintenance timer"
                });
            }
        }

        // ---- SubstituteOrder 兜底：值尚不可用时回写（02 §8.2：IsApplicable==false 才触发，非每 tick 无条件）----
        if (_cfg.SubstituteOrderFallback)
        {
            foreach (var d in _cfg.NonGatedDomains)
            {
                var st = _ctx.Entity[d];
                var applicable = st.EffectiveWriteCount > 0 && _timers.Count > 0;
                if (!applicable)
                {
                    NonGatedWrites++;
                    _ctx.Port.Submit(new WriteRequest
                    {
                        EntityId = _ctx.Entity.Id,
                        Domain = d,
                        Value = _cfg.SelfMaintenanceValue,
                        WriterId = Id,
                        Path = WritePath.NonGated,
                        Priority = _cfg.Priority,
                        Utility = _cfg.UtilityBase,
                        Reason = "substitute order fallback"
                    });
                }
            }
        }
    }

    private void ScanForTimerCreation()
    {
        foreach (var d in _cfg.GatedDomains.Concat(_cfg.NonGatedDomains))
        {
            var st = _ctx.Entity[d];
            if (st.EffectiveWriteCount > 0 && !_createdDomains.Contains(d))
            {
                _createdDomains.Add(d);
                _timers.Add(new Timer { Period = _cfg.CreatedTimerPeriod, Next = _ctx.Clock.Now + _cfg.CreatedTimerPeriod });
                TimersCreated++;
            }
        }
    }

    // 用 List 而非 HashSet：遍历序必须显式确定（05 §1.1）
    private readonly List<CommandDomain> _createdDomains = new();

    private CommandValue Choose(CommandDomain d)
    {
        switch (_cfg.Mode)
        {
            case NsCommandMode.Alternate:
            {
                var v = _lastIssued == _cfg.FixedValue ? _cfg.AlternateWith : _cfg.FixedValue;
                _lastIssued = v;
                return v;
            }
            case NsCommandMode.UtilityNoise:
            {
                var noise = _cfg.UtilityNoiseSigma > 0 ? _ctx.Rng.NextGaussian() * _cfg.UtilityNoiseSigma : 0.0;
                var v = noise >= 0 ? _cfg.FixedValue : _cfg.AlternateWith;
                _lastIssued = v;
                return v;
            }
            default:
                _lastIssued = _cfg.FixedValue;
                return _cfg.FixedValue;
        }
    }

    private double Utility()
    {
        var u = _cfg.UtilityBase;
        if (_cfg.UtilityNoiseSigma > 0) u += _ctx.Rng.NextGaussian() * _cfg.UtilityNoiseSigma;
        if (_cfg.Hysteresis && _lastIssued != CommandValue.Unset) u *= (1.0 - _cfg.HysteresisDiscount);
        return u;
    }

    // ---- 抢占恢复语义支持（E5）----
    public int[] SaveTimerState() => _timers.Select(t => (int)Math.Round(t.Period * 1000)).ToArray();

    public void RestoreTimerState(int[] saved)
    {
        _timers.Clear();
        foreach (var p in saved)
            _timers.Add(new Timer { Period = p / 1000.0, Next = _ctx.Clock.Now + p / 1000.0 });
    }

    public void ResetToBase()
    {
        _timers.Clear();
        _createdDomains.Clear();
        foreach (var p in _cfg.SelfMaintenancePeriods)
            _timers.Add(new Timer { Period = p, Next = _ctx.Clock.Now + p });
    }

    /// <summary>Cancel 语义：不清理，定时器变孤儿 —— 这是 H-G 预测的残留来源。</summary>
    public void OrphanAllTimers()
    {
        foreach (var t in _timers) t.Orphan = true;
    }
}
