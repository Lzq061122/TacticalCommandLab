// BannerlordProbe / 运行时：把宿主 Formation 接到 Core 的 WritePort / AuthorityService / Telemetry 上
//
// 分工（红线）：
//   * 是否放行  -> 只由 CommandLab.Core 的 WritePort.Evaluate 决定，本文件不复制任何判定逻辑
//   * 何时记录  -> Harmony Prefix 进入本文件，转手交给 WritePort，结果原样作用到宿主的 return true/false
//   * 战术语义  -> 本工程一条都没有；对外只有 HOLD / ADVANCE / RETREAT
// 唯一新增的宿主侧口径是 intent_hold_share（归属窗口内"实际值 == 意图值"的时长占比）。

using System.Globalization;
using System.Text;
using CommandLab.Core;
using CommandLab.Telemetry;
using TaleWorlds.MountAndBlade;

namespace CommandLab.BannerlordProbe;

/// <summary>单个 Formation 在 Core 侧的镜像绑定。</summary>
public sealed class FormationBinding
{
    private readonly string[] _lastRaw = new string[3];

    public FormationBinding(Formation formation, string entityId, string alias, ControlledEntity entity)
    {
        Formation = formation;
        EntityId = entityId;
        Alias = alias;
        Entity = entity;
        for (int i = 0; i < _lastRaw.Length; i++) _lastRaw[i] = "-";
    }

    public Formation Formation { get; }
    public string EntityId { get; }

    /// <summary>脚本里用的短名（P0/N0…）。只用于解析，不唯一：多 Team 会撞名。</summary>
    public string Alias { get; }

    public ControlledEntity Entity { get; }

    public HostOrderProjection MovementProjection { get; } = new HostOrderProjection();
    public HostOrderProjection FacingProjection { get; } = new HostOrderProjection();
    public HostOrderProjection ArrangementProjection { get; } = new HostOrderProjection();

    /// <summary>最近一次观测到的原生 MovementOrder 原始值，用于 Release 时的恢复对照组。</summary>
    public MovementOrder? LastNativeMovement { get; set; }

    /// <summary>夺取前 SetControlledByAI 的值；null 表示本轮没动过它。</summary>
    public bool? ControlledByAiBefore { get; set; }

    public string LastRaw(CommandDomain domain) => _lastRaw[(int)domain];

    public string TakeRaw(CommandDomain domain, string raw)
    {
        var prev = _lastRaw[(int)domain];
        _lastRaw[(int)domain] = raw;
        return prev;
    }

    public HostOrderProjection ProjectionOf(CommandDomain domain) => domain switch
    {
        CommandDomain.Movement => MovementProjection,
        CommandDomain.Facing => FacingProjection,
        _ => ArrangementProjection
    };
}

/// <summary>
/// intent_hold_share 采样器：归属窗口内按 Mission tick 累加"实际值 == 意图值"的时长。
/// 支持多窗口累计（一条时间线里可能有多次 cmd）。窗口从 Open 起算，到 Close 结束。
/// </summary>
public sealed class HoldMeter
{
    private double _windowStart = -1.0;
    private double _lastSample = -1.0;
    private double _held;
    private double _cumHeld;
    private double _cumWindow;
    private bool _open;
    private CommandValue _lastActual = CommandValue.Unset;

    public CommandValue Intent { get; private set; } = CommandValue.Unset;
    public int Samples { get; private set; }
    public int Matches { get; private set; }
    public int Windows { get; private set; }
    public bool IsOpen => _open;

    /// <summary>当前（未关闭）窗口的时长；已关闭窗口计入 Cumulative。</summary>
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
        Intent = intent;
        WindowSeconds = 0.0;
    }

    public void Sample(double now, CommandValue actual)
    {
        if (!_open || now <= _lastSample) return;
        var dt = now - _lastSample;
        Samples++;
        if (actual == Intent) { _held += dt; Matches++; }
        _lastSample = now;
        _lastActual = actual;
        WindowSeconds = Math.Max(0.0, now - _windowStart);
    }

    /// <summary>
    /// 末段处理：最后一次采样到 Close 之间没有新采样点，按"状态未变"把这段时长计入同一判定。
    /// 等价假设是 Order 只会在采样间隔内发生变化 —— 采样频率高于 FormationAI 周期（0.5s）时成立。
    /// </summary>
    public double Close(double now)
    {
        if (_open)
        {
            if (now > _lastSample && Samples > 0) Sample(now, _lastActual);
            var w = Samples > 0 ? Math.Max(0.0, Math.Max(now, _lastSample) - _windowStart) : 0.0;
            _cumWindow += w;
            _cumHeld += _held;
            _held = 0.0;
            WindowSeconds = w;
            Windows++;
            _open = false;
        }
        return Share();
    }

    /// <summary>窗口内"实体值 == 意图值"的时长占比。窗口为 0 时返回 0（不得当作 100%）。</summary>
    public double Share()
    {
        var total = _cumWindow + (_open ? WindowSeconds : 0.0);
        return total > 0 ? (_cumHeld + _held) / total : 0.0;
    }
}

public sealed class ProbeRuntime
{
    [ThreadStatic] private static bool _inLabWrite;

    private readonly ProbeConfig _config;
    private readonly ProbeClock _clock;
    private readonly TelemetryCollector _telemetry;
    private readonly AuthorityService _authority;
    private readonly WritePort _port;
    private readonly List<string> _hostLines = new List<string>();
    private readonly Dictionary<Formation, FormationBinding> _bindings =
        new Dictionary<Formation, FormationBinding>(ByReference.Instance);
    private readonly Dictionary<string, Lease> _leases = new Dictionary<string, Lease>(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _acquiredAt = new Dictionary<string, double>(StringComparer.Ordinal);
    private readonly Dictionary<string, HoldMeter> _holds = new Dictionary<string, HoldMeter>(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, HoldMeter>> _holdHistory = new List<KeyValuePair<string, HoldMeter>>();
    private readonly ScriptRunner _script;

    // 已留过痕的孤儿 Formation（Team 为 null 或 Index < 0，无法唯一标识）。按引用去重。
    private readonly HashSet<Formation> _orphans = new HashSet<Formation>(ByReference.Instance);

    // ---- 覆盖率取证（Phase 10 新增）----
    // census：每个写入口 hook 的调用次数（含放行）。0 次 = 无流量或 patch 未生效，两者由 Patches.Applied 列表区分。
    private readonly Dictionary<string, int> _census = new Dictionary<string, int>(StringComparer.Ordinal);
    // shadow：按 tick 采样宿主真值得到的变化次数。与写入口记录的生效变更数之差 = 绕过写入口的写入量。
    private readonly Dictionary<string, CommandValue> _sampledLast = new Dictionary<string, CommandValue>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sampledChanges = new Dictionary<string, int>(StringComparer.Ordinal);

    public ProbeRuntime(ProbeConfig config)
    {
        _config = config;
        _clock = new ProbeClock();
        _telemetry = new TelemetryCollector();
        _authority = new AuthorityService(_telemetry, ProbeConfig.NativeWriterId);
        _port = new WritePort(_clock, _authority, _telemetry, config.ToPolicy());
        _script = new ScriptRunner(this, config.ScriptPath);
        Current = this;
    }

    public static ProbeRuntime? Current { get; private set; }

    public ProbeConfig Config => _config;
    public ProbeClock Clock => _clock;
    public TelemetryCollector Telemetry => _telemetry;
    public WritePort Port => _port;
    public ScriptRunner Script => _script;
    public WriteAttempt? LastAttempt { get; private set; }
    public int ReleaseRecoveryFailures { get; private set; }
    public double ReactionLatency { get; private set; } = -1.0;

    /// <summary>Lab 生效写入 -> 第一次原生生效覆盖的时间差。H1 的"多久被覆盖"。</summary>
    public double NativeOverrideLatency { get; private set; } = -1.0;
    private double _lastLabChangeAt = -1.0;

    /// <summary>
    /// 已落盘的崩溃报告份数。用于去重：同一个异常每 tick 都会抛，不做去重会把磁盘写爆。
    /// 详见 <see cref="RecordCrash"/>。
    /// </summary>
    private int _crashReportsWritten;

    /// <summary>
    /// `Formation` 键的**引用相等**比较器（net472 没有内置的 ReferenceEqualityComparer）。
    ///
    /// 为什么必须用它 —— **这是 2026-09-22 崩溃的根因**：
    /// `Formation` 重写了 `GetHashCode()`，其 IL（ilscan 取证，v1.4.8）为
    /// ```
    /// Ldfld    Formation::Team              // 直接读字段
    /// Callvirt Team::get_TeamIndex          // ← Team 为 null 时抛 NullReferenceException
    /// Ldc_i4_s 10 / Mul                     // TeamIndex * 10
    /// Ldfld    Formation::FormationIndex / Add
    /// ```
    /// 而 `Dictionary<Formation, _>` 的 `TryGetValue` 与索引器赋值**都会调用 `GetHashCode()`**
    /// ⇒ Mission 初始化阶段只要宿主把一个 `Team == null` 的 Formation 传进写入口，
    /// `Bind()` 的第一行就会 NRE，并（在只有 ProbeRuntime 内部兜底的旧版本里）**穿透宿主调用栈崩掉游戏**。
    ///
    /// 语义不变：`Formation` 未重写 `Equals`，默认就是引用比较，所以原本的行为本就等价于引用比较；
    /// 换成显式比较器**只避开宿主那个会抛异常的 GetHashCode，并省掉一次 `Team.get_TeamIndex` 调用**。
    /// </summary>
    private sealed class ByReference : IEqualityComparer<Formation>
    {
        public static readonly ByReference Instance = new ByReference();
        public bool Equals(Formation x, Formation y) => ReferenceEquals(x, y);
        public int GetHashCode(Formation obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static string Key(string entityId, CommandDomain domain) =>
        entityId + "|" + domain.ToString();

    // ---------------- 绑定 ----------------

    /// <summary>
    /// 全局唯一实体名。为什么不再用 P/N + Index：Formation.Index 只在**单个 Team 内**唯一，
    /// 于是所有非玩家 Team 的 Index 0 全塌缩成同一个 "N0"，Core 侧被当成同一个实体（D1）。
    /// </summary>
    public string EntityIdOf(Formation formation, int teamSeq = -1)
    {
        if (teamSeq < 0) teamSeq = TeamSeqOf(formation);
        return "T" + teamSeq.ToString(CultureInfo.InvariantCulture)
             + "F" + formation.Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>脚本用的短名。不唯一（多 Team 会撞名），仅供解析，歧义时拒绝执行。</summary>
    public string AliasOf(Formation formation)
    {
        string side;
        try { side = formation.Team is not null && formation.Team.IsPlayerTeam ? "P" : "N"; }
        catch { side = "N"; }
        return side + formation.Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Team 在 Mission.Current.Teams 里的序号（引用相等）。定位不到返回 -1。</summary>
    private static int TeamSeqOf(Formation formation)
    {
        try
        {
            var team = formation.Team;
            var mission = Mission.Current;
            if (team is null || mission is null) return -1;
            var teams = mission.Teams;
            if (teams is null) return -1;
            var seq = 0;
            foreach (var t in teams)
            {
                if (ReferenceEquals(t, team)) return seq;
                seq++;
            }
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 唯一绑定收口点。**返回 null 表示拒绝了绑定**：该 Formation 无法被唯一标识，
    /// 调用方必须处理，不得继续把它当成实体使用。
    ///
    /// 为什么必须拒绝（2026-10-03 实测）：EntityId = T{teamSeq}F{index}，而 Team 为 null 的
    /// Formation 的 teamSeq 推导为 -1、Index 又多为 -1 ⇒ 12 个不同对象全部塌缩成同一个 "T-1F-1"。
    /// SampleShadow 按 EntityId 聚合 key，于是它们互相把对方的值变化当成自己的，
    /// 一次性造出 15036 条假 unobserved。拒绝绑定比给重复名加自增后缀更安全 —— 后缀会让实体的
    /// 名字取决于遍历顺序，违反可重放原则。
    /// </summary>
    public FormationBinding? Bind(Formation formation, int teamSeq = -1)
    {
        if (formation is null) return null;
        if (_bindings.TryGetValue(formation, out var existing)) return existing;

        // Team / Index 必须都可读且 Index >= 0，否则 EntityId 必然撞名。
        var teamDesc = "throw";
        var idx = -1;
        try
        {
            teamDesc = formation.Team is null ? "null" : "set";
            idx = formation.Index;
        }
        catch
        {
            // 保持已赋的 teamDesc="throw" / idx=-1，下面按不可标识处理。
        }

        if (teamDesc != "set" || idx < 0)
        {
            NoteOrphan(formation, teamDesc, idx);
            return null;
        }

        var id = EntityIdOf(formation, teamSeq);
        var entity = new ControlledEntity(id);
        _port.RegisterEntity(entity);
        var binding = new FormationBinding(formation, id, AliasOf(formation), entity);
        _bindings[formation] = binding;
        return binding;
    }

    /// <summary>
    /// 被跳过的孤儿 Formation 留痕，**每个对象只留一次**（BindAll 每 tick 都会走一遍，
    /// 不去重会每帧刷屏并写爆 dump）。
    /// </summary>
    private void NoteOrphan(Formation formation, string teamDesc, int idx)
    {
        if (!_orphans.Add(formation)) return;
        Note("ORPHAN_FORMATION_SKIPPED team=" + teamDesc
             + " idx=" + idx.ToString(CultureInfo.InvariantCulture));
    }

    // ---------------- 写入口闸门 ----------------

    private bool Enabled(CommandDomain domain, WritePath path)
    {
        if (domain == CommandDomain.Movement) return _config.PatchMovement;
        if (domain == CommandDomain.Facing) return _config.PatchFacing;
        return _config.PatchArrangement;
    }

    /// <summary>
    /// Harmony Prefix 的唯一入口。返回 true = 放行原方法，false = 拦截（对应 WriteAttempt.OriginalMethodExecuted）。
    /// 判定不在这里发生：只是把宿主调用翻译成 WriteRequest 交给 Core 的写入口。
    /// </summary>
    /// <summary>
    /// Harmony Prefix 的唯一入口，**带异常兜底**。
    ///
    /// 为什么必须兜底（2026-09-22 实测）：本方法在宿主调用写入口时**同步**执行，异常会直接穿透到
    /// 宿主调用栈并使游戏崩溃（实测崩溃异常码 `0xe0434352` = CLR 未处理异常，出错模块 KERNELBASE.dll）。
    /// 探针是实验装置，**绝不能弄崩用户的游戏**。但兜底同时必须留痕（<see cref="RecordCrash"/>），
    /// 否则就变成"用防御性代码掩盖真实缺陷"。
    /// </summary>
    public bool Gate(Formation formation, CommandDomain domain, WritePath path, string hook,
                     CommandValue incoming, string raw)
    {
        try
        {
            return GateCore(formation, domain, path, hook, incoming, raw);
        }
        catch (Exception ex)
        {
            RecordCrash("Gate", ex, hook + " raw=" + raw);
            return true;   // 放行：宁可少拦一次，也不能让游戏崩
        }
    }

    private bool GateCore(Formation formation, CommandDomain domain, WritePath path, string hook,
                          CommandValue incoming, string raw)
    {
        if (!Enabled(domain, path)) return true;

        var binding = Bind(formation);
        if (binding is null) { Bump(hook); return true; }   // 无法唯一标识 ⇒ 放行原方法，绝不把异常抛到宿主
        var rec = _authority.GetOrCreate(binding.EntityId, domain);
        var lease = ActiveLease(binding.EntityId, domain);
        bool isLab = _inLabWrite;
        var prevRaw = binding.TakeRaw(domain, raw);

        // 写入口普查：不论放行还是拦截都计数。用于区分"该口没有流量"与"patch 没打上"。
        Bump(hook);

        var req = new WriteRequest
        {
            EntityId = binding.EntityId,
            Domain = domain,
            Value = incoming,
            WriterId = isLab ? ProbeConfig.LabWriterId : ProbeConfig.NativeWriterId,
            Path = path,
            LeaseId = isLab ? lease?.LeaseId : null,
            LeaseToken = isLab ? lease?.Token ?? 0 : 0,
            Generation = isLab ? rec.Generation : 0,
            Reason = hook
        };

        var attempt = _port.SubmitImmediate(req);
        LastAttempt = attempt;

        if (isLab && attempt.OrderChanged && ReactionLatency < 0 &&
            _acquiredAt.TryGetValue(Key(binding.EntityId, domain), out var at))
        {
            ReactionLatency = attempt.Timestamp - at;
        }

        // H1 的核心量：Lab 的意图值被原生覆盖需要多久。
        if (!attempt.Rejected && attempt.OrderChanged)
        {
            if (isLab)
            {
                _lastLabChangeAt = attempt.Timestamp;
            }
            else if (_lastLabChangeAt >= 0 && NativeOverrideLatency < 0)
            {
                NativeOverrideLatency = attempt.Timestamp - _lastLabChangeAt;
            }
        }

        _hostLines.Add(HostContext.Line(
            attempt.Timestamp, binding.EntityId, domain, hook, formation,
            raw, prevRaw, attempt.Rejected ? prevRaw : raw,
            attempt.OriginalMethodExecuted, attempt.OrderChanged));

        if (!attempt.Rejected && !isLab && domain == CommandDomain.Movement)
        {
            binding.LastNativeMovement = NeutralOrders.ToMovement(incoming);
        }

        return !attempt.Rejected;
    }

    // ---------------- Lab 侧动作 ----------------

    /// <summary>下达一条中性指令。返回值 null 表示宿主侧没有产生写入尝试。</summary>
    public WriteAttempt? Command(Formation formation, CommandDomain domain, CommandValue value)
    {
        if (domain != CommandDomain.Movement)
        {
            // 红线：probe 从不下达 Facing / Arrangement 指令，这两域只做观察。
            return null;
        }

        var binding = Bind(formation);
        if (binding is null) return null;
        _inLabWrite = true;
        try
        {
            LastAttempt = null;
            formation.SetMovementOrder(NeutralOrders.ToMovement(value));
        }
        finally
        {
            _inLabWrite = false;
        }

        // 意图窗口从这里起算：只有真正下达了指令，intent_hold_share 才有意义。
        // （Phase 9 的缺陷：Acquire 时只 new 了 HoldMeter 却从不 Open，导致 hold 恒为 0%）
        if (LastAttempt is not null && domain == CommandDomain.Movement)
        {
            var key = Key(binding.EntityId, domain);
            if (_holds.TryGetValue(key, out var hold)) hold.Open(_clock.Now, value);
        }
        return LastAttempt;
    }

    /// <summary>恢复用：原样重发最后一次观测到的原生 MovementOrder（不引入任何新语义）。</summary>
    public WriteAttempt? ReapplyLastNative(Formation formation)
    {
        var binding = Bind(formation);
        if (binding is null) return null;
        if (binding.LastNativeMovement is null) return null;

        _inLabWrite = true;
        try
        {
            LastAttempt = null;
            formation.SetMovementOrder(binding.LastNativeMovement.Value);
        }
        finally
        {
            _inLabWrite = false;
        }
        return LastAttempt;
    }

    // ---------------- Acquire / Renew / Release ----------------

    public Lease? Acquire(Formation formation, CommandDomain domain, double now)
    {
        var binding = Bind(formation);
        if (binding is null) return null;

        if (_config.UseControlledByAiRoute && domain == CommandDomain.Movement)
        {
            try
            {
                binding.ControlledByAiBefore = formation.IsAIControlled;
                formation.SetControlledByAI(false, formation.IsSplittableByAI);
            }
            catch
            {
                ReleaseRecoveryFailures++;
            }
        }

        var lease = _authority.Acquire(binding.EntityId, domain, ProbeConfig.LabWriterId,
                                       _config.Level, _config.TtlSeconds, now);
        if (lease is null) return null;

        var key = Key(binding.EntityId, domain);
        _leases[key] = lease;
        _acquiredAt[key] = now;
        var hold = new HoldMeter();
        _holds[key] = hold;
        return lease;
    }

    public Lease? ActiveLease(string entityId, CommandDomain domain) =>
        _leases.TryGetValue(Key(entityId, domain), out var l) ? l : null;

    public bool Renew(Formation formation, CommandDomain domain, double now)
    {
        var binding = Bind(formation);
        if (binding is null) return false;
        var lease = ActiveLease(binding.EntityId, domain);
        return lease is not null && _authority.Renew(lease, _config.RenewExtendSeconds, now);
    }

    /// <summary>交还归属。返回本次释放的恢复失败数（被拒的恢复写入 + 抛异常的恢复动作）。</summary>
    public int Release(Formation formation, CommandDomain domain, double now)
    {
        var binding = Bind(formation);
        // 孤儿 Formation 无法定位其租约，与"没有活跃租约"同样计一次恢复失败。
        if (binding is null) { ReleaseRecoveryFailures++; return 1; }
        var lease = ActiveLease(binding.EntityId, domain);
        if (lease is null) { ReleaseRecoveryFailures++; return 1; }

        int failures = 0;
        if (_config.RestoreBeforeRelease) failures += Restore(binding, domain);

        _authority.Release(lease, now);

        var key = Key(binding.EntityId, domain);
        _leases.Remove(key);

        if (_holds.TryGetValue(key, out var hold))
        {
            hold.Close(now);
            _holdHistory.Add(new KeyValuePair<string, HoldMeter>(key, hold));
            _holds.Remove(key);
        }

        if (!_config.RestoreBeforeRelease) failures += Restore(binding, domain);

        ReleaseRecoveryFailures += failures;
        return failures;
    }

    private int Restore(FormationBinding binding, CommandDomain domain)
    {
        int failures = 0;
        var mode = _config.Restore;

        if ((mode & ReleaseRestoreMode.ReapplyLastNativeOrder) != 0)
        {
            var attempt = ReapplyLastNative(binding.Formation);
            if (attempt is null || attempt.Rejected) failures++;
        }

        if ((mode & ReleaseRestoreMode.ResetBehaviorWeights) != 0)
        {
            try { binding.Formation.AI?.ResetBehaviorWeights(); }
            catch { failures++; }
        }

        if ((mode & ReleaseRestoreMode.RestoreControlledByAi) != 0 &&
            binding.ControlledByAiBefore is bool before)
        {
            try { binding.Formation.SetControlledByAI(before, binding.Formation.IsSplittableByAI); }
            catch { failures++; }
        }

        return failures;
    }

    // ---------------- 每 tick ----------------

    public void Tick(float dt)
    {
        // 每帧入口同样必须兜底：本方法由 MissionBehavior.OnMissionTick 驱动，异常会穿透到
        // 宿主 Mission 更新循环。与 Gate 的区别是这里不返回值，只记录并跳过本帧。
        try
        {
            TickCore(dt);
        }
        catch (Exception ex)
        {
            RecordCrash("Tick", ex, "dt=" + dt.ToString("F4", CultureInfo.InvariantCulture));
        }
    }

    private void TickCore(float dt)
    {
        _clock.Advance(dt);
        var now = _clock.Now;

        _authority.SweepExpired(now);
        DropDeadLeases(now);

        if (_config.AutoRenew)
        {
            var keys = new List<string>(_leases.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var lease = _leases[key];
                var rec = _authority.TryGet(lease.EntityId, lease.Domain);
                if (rec is not null && rec.HasActiveLease(now))
                    _authority.Renew(lease, _config.RenewExtendSeconds, now);
            }
        }

        // 必须在 SampleShadow 与 _script.Tick 之前：前者遍历 _bindings，后者按名字解析实体。
        BindAll();
        SampleShadow(now);
        SampleHolds(now);
        _script.Tick(now);
    }

    /// <summary>
    /// 全量镜像：把当前 Mission 里所有 Team 的所有 Formation 登记为可观测实体。
    ///
    /// 为什么必须在每 tick 主动做（2026-09-22 实测教训）：
    ///   `[census]` 的计数发生在 `Gate() → Bind(formation)` 之后，而脚本的实体名解析
    ///   （`ScriptRunner.TryCommand` → `FindBinding`）**要求实体已存在**，否则静默 return。
    ///   而绑定此前只在 patch 触发时才创建 ⇒ **鸡生蛋死锁**：没有绑定就没有 Gate，
    ///   没有 Gate 就没有绑定。结果是 `[census]` / `[coverage]` 两段全空，
    ///   把"没有观测对象"伪装成"原生没有写入"这一架构事实 —— 正是 P0b 要防的假数据。
    ///   （会话 12 曾试图用脚本里的 `cmd` 触碰来建立绑定，但那同样要先过 FindBinding，无效。）
    ///
    /// 安全性：`Bind` 只做 Core 侧注册（`_port.RegisterEntity` + `_bindings` 字典写入），
    /// 不触碰宿主任何状态。每 tick 调用，`Bind` 内部 `TryGetValue` 短路，成本可忽略。
    /// 不过滤空编队：漏绑会重新引入"没有观测对象"，而空编队的真值读出来是 Unset、会被跳过，无污染。
    /// </summary>
    private void BindAll()
    {
        try
        {
            var mission = Mission.Current;
            if (mission is null) return;

            var ti = 0;
            foreach (var team in mission.Teams)
            {
                if (team is null) { ti++; continue; }
                var formations = team.FormationsIncludingEmpty;
                if (formations is null) { ti++; continue; }

                foreach (var formation in formations)
                {
                    if (formation is not null) Bind(formation, ti);
                }
                ti++;
            }
        }
        catch
        {
            // 宿主 API 在 Mission 生命周期边界可能抛异常；观测不能影响游戏本身。
        }
    }

    private void DropDeadLeases(double now)
    {
        var dead = new List<string>();
        foreach (var kv in _leases)
        {
            var rec = _authority.TryGet(kv.Value.EntityId, kv.Value.Domain);
            if (rec is null || !rec.HasActiveLease(now)) dead.Add(kv.Key);
        }
        foreach (var key in dead)
        {
            _leases.Remove(key);
            if (_holds.TryGetValue(key, out var hold))
            {
                hold.Close(now);
                _holdHistory.Add(new KeyValuePair<string, HoldMeter>(key, hold));
                _holds.Remove(key);
            }
        }
    }

    // ---------------- 覆盖率取证 ----------------

    private void Bump(string hook)
    {
        _census.TryGetValue(hook, out var n);
        _census[hook] = n + 1;
    }

    /// <summary>观测型写入口（不拦截）的计数入口：SetControlledByAI / SetFormOrder / SetFiringOrder / SetRidingOrder。</summary>
    public void Observe(Formation formation, string hook, string raw)
    {
        // 与 Gate 同理：本方法在宿主 Prefix 里同步执行，异常会穿透崩游戏。
        try
        {
            Bump(hook);
            var binding = Bind(formation);
            if (binding is null) return;   // census 已计；孤儿不写 CENSUS 行
            _hostLines.Add(string.Format(CultureInfo.InvariantCulture,
                "CENSUS|t={0:F4}|E={1}|hook={2}|raw={3}", _clock.Now, binding.EntityId, hook, raw));
        }
        catch (Exception ex)
        {
            RecordCrash("Observe", ex, hook);
        }
    }

    /// <summary>
    /// 影子采样：每 tick 读宿主真值，统计"值发生了多少次变化"。
    /// 与写入口记录的生效变更数相减，即得"绕过写入口的写入量"——这是写入口覆盖率唯一的实测口径，
    /// 因为未 patch 的路径（02 §8.2 的 Arrangement 双 Timer）根本不会产生 WriteAttempt。
    /// </summary>
    private void SampleShadow(double now)
    {
        foreach (var kv in _bindings)
        {
            var binding = kv.Value;
            for (int d = 0; d < 3; d++)
            {
                var domain = (CommandDomain)d;
                var actual = ActualValue(binding, domain);
                if (actual == CommandValue.Unset) continue;

                var key = Key(binding.EntityId, domain);
                if (_sampledLast.TryGetValue(key, out var last))
                {
                    if (last != actual)
                    {
                        _sampledChanges.TryGetValue(key, out var n);
                        _sampledChanges[key] = n + 1;
                        _hostLines.Add(string.Format(CultureInfo.InvariantCulture,
                            "SHADOW|t={0:F4}|E={1}|D={2}|from={3}|to={4}",
                            now, binding.EntityId, domain, last, actual));
                    }
                }
                _sampledLast[key] = actual;
            }
        }
    }

    /// <summary>某实体某域在写入口记录的生效变更数（放行且值真的变了）。</summary>
    private int PortEffectiveChanges(string entityId, CommandDomain domain)
    {
        int n = 0;
        foreach (var a in _telemetry.Attempts)
        {
            if (a.EntityId == entityId && a.Domain == domain && !a.Rejected && a.OrderChanged) n++;
        }
        return n;
    }

    /// <summary>覆盖率行：采样到的变化 - 写入口记录的生效变化 = 绕过写入口的写入（下限估计）。</summary>
    public IReadOnlyList<string> CoverageRows()
    {
        var rows = new List<string>();
        var keys = new List<string>(_sampledLast.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var parts = key.Split('|');
            if (parts.Length != 2) continue;
            if (!Enum.TryParse(parts[1], true, out CommandDomain domain)) continue;

            _sampledChanges.TryGetValue(key, out var sampled);
            var port = PortEffectiveChanges(parts[0], domain);
            var unobserved = sampled > port ? sampled - port : 0;
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "{0,-24} sampled_changes={1,-4} port_changes={2,-4} unobserved={3,-4} {4}",
                key, sampled, port, unobserved,
                unobserved > 0 ? "<-- 存在绕过写入口的写入" : ""));
        }
        return rows;
    }

    public IReadOnlyList<string> CensusRows()
    {
        var rows = new List<string>();
        var keys = new List<string>(_census.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0,-40} calls={1}", key, _census[key]));
        return rows;
    }

    private void SampleHolds(double now)
    {
        if (_holds.Count == 0) return;
        foreach (var kv in _holds)
        {
            var lease = _leases.TryGetValue(kv.Key, out var l) ? l : null;
            if (lease is null) { kv.Value.Close(now); continue; }

            var binding = FindBinding(lease.EntityId);
            if (binding is null) continue;

            var actual = ActualValue(binding, lease.Domain);
            kv.Value.Sample(now, actual);
        }
    }

    public FormationBinding? FindBinding(string entityId)
    {
        foreach (var kv in _bindings)
            if (string.Equals(kv.Value.EntityId, entityId, StringComparison.Ordinal)) return kv.Value;
        return null;
    }

    /// <summary>按短名解析。可能命中多个（多 Team 同 Index），由调用方决定是拒绝还是取唯一解。</summary>
    public IReadOnlyList<FormationBinding> ResolveByAlias(string alias)
    {
        var hits = new List<FormationBinding>();
        foreach (var kv in _bindings)
            if (string.Equals(kv.Value.Alias, alias, StringComparison.Ordinal)) hits.Add(kv.Value);
        return hits;
    }

    /// <summary>读取宿主当前真值（不是镜像）—— hold 必须按真值算，否则会高估控制权。</summary>
    public CommandValue ActualValue(FormationBinding binding, CommandDomain domain)
    {
        var formation = binding.Formation;
        try
        {
            switch (domain)
            {
                case CommandDomain.Movement:
                    var current = formation.GetReadonlyMovementOrderReference();
                    if (NeutralOrders.TryDecode(current, out var mv)) return mv;
                    return binding.MovementProjection.Project(current.OrderType.ToString());
                case CommandDomain.Facing:
                    return binding.FacingProjection.Project(formation.FacingOrder.OrderEnum.ToString());
                default:
                    return binding.ArrangementProjection.Project(formation.ArrangementOrder.OrderEnum.ToString());
            }
        }
        catch
        {
            return CommandValue.Unset;
        }
    }

    // ---------------- 输出 ----------------

    public MetricSet ComputeMetrics()
    {
        var set = MetricsCalculator.Compute(_telemetry, _clock.Now, null,
            new MetricsConfig { NativeWriterId = ProbeConfig.NativeWriterId, TEval = _config.TEval });
        if (ReactionLatency >= 0) set.ReactionLatency = ReactionLatency;
        set.ReleaseRecoveryFailures = ReleaseRecoveryFailures;
        return set;
    }

    public IReadOnlyList<string> HoldRows()
    {
        var rows = new List<string>();

        void Emit(string key, HoldMeter hold, bool active)
        {
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "{0,-24} {1} intent={2,-8} hold={3,6:F1}%  window={4:F2}s  samples={5}  matches={6}",
                key, active ? "OPEN " : "CLOSED", NeutralOrders.NameOf(hold.Intent),
                hold.Share() * 100.0, hold.CumulativeSeconds + (hold.IsOpen ? hold.WindowSeconds : 0.0),
                hold.Samples, hold.Matches));
        }

        foreach (var kv in _holdHistory) Emit(kv.Key, kv.Value, false);

        var keys = new List<string>(_holds.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys) Emit(key, _holds[key], true);

        return rows;
    }

    /// <summary>
    /// 判据自检（Telemetry 支撑，不靠画面）。阈值取自 07a 的硬门限：
    ///   G1 unauthorized_writes == 0     G2 intent_hold_share >= 95%
    ///   P0 patch 生效性门禁：写入口必须真的被 patch，否则"没有写入"是假数据
    /// </summary>
    public IReadOnlyList<string> VerdictRows(string tag)
    {
        var rows = new List<string>();
        var m = ComputeMetrics();

        // P0：patch 生效性。Applied 列表来自 Harmony.GetPatchedMethods()，与流量无关。
        var applied = Patches.Applied.Count;
        rows.Add(Row("P0", "harmony patched methods > 0", applied.ToString(CultureInfo.InvariantCulture),
            applied > 0));
        // P0 FAIL 时把根因直接摆在 verdict 里：否则阅读者只会看到"0 个方法被 patch"而无从下手。
        if (Patches.Failure is not null) rows.Add("P0     patch failure cause: " + Patches.Failure);

        // P0b：本轮是否有写入口流量（无流量则该域结论不可采信）
        var totalCalls = 0;
        foreach (var v in _census.Values) totalCalls += v;
        rows.Add(Row("P0b", "write-port traffic > 0", totalCalls.ToString(CultureInfo.InvariantCulture),
            totalCalls > 0));

        // P0c：EntityId 唯一性自检。重复会让多个 Formation 在 Core 侧塌缩成同一个实体，
        // 该轮所有 lease / hold / unauthorized 统计随之失真，所以必须让装置自己报警而不是靠人眼对账。
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var dupOrder = new List<string>();
        foreach (var kv in _bindings)
        {
            var id = kv.Value.EntityId;
            counts.TryGetValue(id, out var n);
            counts[id] = n + 1;
            if (n == 1) dupOrder.Add(id);   // 第二次见到才算重复
        }
        var uniqueCount = counts.Count;
        var bindingCount = _bindings.Count;
        rows.Add(Row("P0c", "entity_id unique",
            uniqueCount.ToString(CultureInfo.InvariantCulture) + "/" + bindingCount.ToString(CultureInfo.InvariantCulture),
            uniqueCount == bindingCount));
        if (dupOrder.Count > 0)
        {
            var shown = new List<string>();
            for (int i = 0; i < dupOrder.Count && i < 12; i++)
                shown.Add(dupOrder[i] + " x" + counts[dupOrder[i]].ToString(CultureInfo.InvariantCulture));
            var extra = dupOrder.Count - shown.Count;
            if (extra > 0) shown.Add("…(+" + extra.ToString(CultureInfo.InvariantCulture) + ")");
            rows.Add("P0c    duplicated entity_id: " + string.Join(", ", shown));
        }

        var prefix = tag.Split('-')[0].ToLowerInvariant();

        if (prefix == "h1")
        {
            rows.Add(Row("H1-a", "native_overrides > 0 (不夺取必被覆盖)",
                m.NativeOverrides.ToString(CultureInfo.InvariantCulture), m.NativeOverrides > 0));
            rows.Add(Row("H1-b", "Movement 写入口有原生流量",
                CensusOf("Formation.SetMovementOrder").ToString(CultureInfo.InvariantCulture),
                CensusOf("Formation.SetMovementOrder") > 0));
            rows.Add(Row("H1-c", "first native override latency (s)",
                NativeOverrideLatency < 0 ? "no-override" : NativeOverrideLatency.ToString("F3", CultureInfo.InvariantCulture),
                true));
            rows.Add(Row("H1-d", "lab_overrides (Lab 反覆盖原生)",
                m.LabOverrides.ToString(CultureInfo.InvariantCulture), true));
        }
        else if (prefix == "h2")
        {
            rows.Add(Row("G1", "unauthorized_writes == 0",
                m.UnauthorizedWrites.ToString(CultureInfo.InvariantCulture), m.UnauthorizedWrites == 0));
            var hold = BestHold();
            rows.Add(Row("G2", "intent_hold_share >= 95%",
                hold is null ? "no-window" : (hold.Share() * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "%",
                hold is not null && hold.Share() >= 0.95));
            rows.Add(Row("G3", "stability grade != Oscillating",
                m.Grade.ToString(), m.Grade != StabilityGrade.Oscillating));
        }
        else if (prefix == "h3")
        {
            rows.Add(Row("H3-a", "SetControlledByAI 被观测到",
                CensusOf("Formation.SetControlledByAI").ToString(CultureInfo.InvariantCulture),
                CensusOf("Formation.SetControlledByAI") > 0));
            rows.Add(Row("H3-b", "unauthorized_writes == 0",
                m.UnauthorizedWrites.ToString(CultureInfo.InvariantCulture), m.UnauthorizedWrites == 0));
            var hold3 = BestHold();
            rows.Add(Row("G2", "intent_hold_share >= 95%",
                hold3 is null ? "no-window" : (hold3.Share() * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "%",
                hold3 is not null && hold3.Share() >= 0.95));
        }
        else if (prefix == "rel")
        {
            rows.Add(Row("REL", "release_recovery_failures == 0",
                ReleaseRecoveryFailures.ToString(CultureInfo.InvariantCulture), ReleaseRecoveryFailures == 0));
        }

        rows.Add(Row("X1", "release_recovery_failures", ReleaseRecoveryFailures.ToString(CultureInfo.InvariantCulture), true));
        rows.Add(Row("X2", "oscillation_frequency", m.OscillationFrequency.ToString("F4", CultureInfo.InvariantCulture), true));
        rows.Add(Row("X3", "order_changes_per_second", m.OrderChangesPerSecond.ToString("F4", CultureInfo.InvariantCulture), true));
        rows.Add(Row("X4", "reaction_latency", m.ReactionLatency.ToString("F4", CultureInfo.InvariantCulture), true));
        rows.Add(Row("X5", "unobserved_writes (采样到但写入口没记录)",
            TotalUnobserved().ToString(CultureInfo.InvariantCulture), TotalUnobserved() == 0));
        return rows;
    }

    private int TotalUnobserved()
    {
        int total = 0;
        foreach (var kv in _sampledLast)
        {
            var parts = kv.Key.Split('|');
            if (parts.Length != 2) continue;
            if (!Enum.TryParse(parts[1], true, out CommandDomain domain)) continue;
            _sampledChanges.TryGetValue(kv.Key, out var sampled);
            var port = PortEffectiveChanges(parts[0], domain);
            if (sampled > port) total += sampled - port;
        }
        return total;
    }

    private static string Row(string id, string text, string actual, bool pass) =>
        string.Format(CultureInfo.InvariantCulture, "{0,-6} {1,-42} actual={2,-14} {3}",
            id, text, actual, pass ? "PASS" : "FAIL");

    private int CensusOf(string hook) => _census.TryGetValue(hook, out var n) ? n : 0;

    /// <summary>取"最后一个已关闭窗口"；没有已关闭窗口时取当前打开窗口（末段外推）。</summary>
    private HoldMeter? BestHold()
    {
        if (_holdHistory.Count > 0) return _holdHistory[_holdHistory.Count - 1].Value;
        foreach (var kv in _holds) return kv.Value;
        return null;
    }

    public void Dump(string tag)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_config.DumpDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            var telemetryPath = System.IO.Path.Combine(_config.DumpDir, $"telemetry-{stamp}-{tag}.txt");
            using (var w = new System.IO.StreamWriter(telemetryPath, false, Encoding.UTF8))
            {
                foreach (var line in _telemetry.CanonicalLines()) w.WriteLine(line);
                foreach (var line in _hostLines) w.WriteLine(line);
            }

            var metricsPath = System.IO.Path.Combine(_config.DumpDir, $"metrics-{stamp}-{tag}.txt");
            using (var w = new System.IO.StreamWriter(metricsPath, false, Encoding.UTF8))
            {
                w.WriteLine("[config]");
                w.WriteLine(DescribeConfig());
                w.WriteLine();
                w.WriteLine("[metrics]");
                foreach (var row in ComputeMetrics().Rows()) w.WriteLine(row);
                w.WriteLine();
                w.WriteLine("[verdict]");
                foreach (var row in VerdictRows(tag)) w.WriteLine(row);
                w.WriteLine();
                w.WriteLine("[hold]");
                foreach (var row in HoldRows()) w.WriteLine(row);
                w.WriteLine();
                w.WriteLine("[census]");
                foreach (var row in CensusRows()) w.WriteLine(row);
                w.WriteLine();
                w.WriteLine("[coverage]");
                foreach (var row in CoverageRows()) w.WriteLine(row);
                w.WriteLine();
                w.WriteLine("[patches]");
                w.WriteLine("failure = " + (Patches.Failure ?? "(none)"));
                w.WriteLine("count   = " + Patches.Applied.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var pm in Patches.Applied) w.WriteLine(pm);
                w.WriteLine();
                w.WriteLine("[projection]");
                foreach (var kv in _bindings)
                {
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}  alias={1}  mv={2}  fa={3}  ar={4}",
                        kv.Value.EntityId,
                        kv.Value.Alias,
                        kv.Value.MovementProjection.Describe(),
                        kv.Value.FacingProjection.Describe(),
                        kv.Value.ArrangementProjection.Describe()));
                }
            }

            LastDumpPath = metricsPath;
        }
        catch (Exception ex)
        {
            LastDumpError = ex.Message;
        }
    }

    /// <summary>宿主侧自由文本留痕（不进入 Core 的 WriteAttempt 流，因此不影响 14 项指标）。</summary>
    public void Note(string text)
    {
        _hostLines.Add(string.Format(CultureInfo.InvariantCulture,
            "NOTE|t={0:F4}|{1}", _clock.Now, text));
    }

    /// <summary>
    /// 启动哨兵：SubModule 加载成功后**立即**落一个独立文件，不等任何 Mission / dump 时机。
    ///
    /// 为什么需要（2026-09-22 实测教训）：游戏崩溃后，Bannerlord 的默认日志**不记录**普通 SubModule
    /// 的加载，而本探针的产物只在 Mission tick 里落盘。于是"崩溃发生在 Mission 初始化阶段"时，
    /// 磁盘上没有任何本探针的痕迹 —— **无法区分"SubModule 没加载（崩溃与探针无关）"与
    /// "加载了但在 Mission 里崩了"**。这个文件就是那个区分点。
    /// </summary>
    public void WriteBootMarker(string harmonyId)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_config.DumpDir);
            var path = System.IO.Path.Combine(_config.DumpDir, "boot-marker.txt");
            var sb = new StringBuilder();
            sb.AppendLine("CommandLabProbe boot marker");
            sb.AppendLine("at            = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("harmonyId     = " + harmonyId);
            sb.AppendLine("patchedCount  = " + Patches.Applied.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("patchFailure  = " + (Patches.Failure ?? "(none)"));
            sb.AppendLine("--- patched methods ---");
            foreach (var pm in Patches.Applied) sb.AppendLine(pm);
            sb.AppendLine("--- pending notes ---");
            foreach (var line in _hostLines) sb.AppendLine(line);
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // 哨兵失败不能影响游戏启动。
        }
    }

    /// <summary>
    /// Patch 结果哨兵：在**延迟 patch 完成之后**立即落盘（时序约束见 docs/10 §12.9）。
    ///
    /// 与 <see cref="WriteBootMarker"/> 分开的原因：patch 现在被推迟到**第一次 Mission tick**，
    /// 所以 boot marker 只能证明"SubModule 已加载（尚未 patch）"，patch 的实际结果由本文件记录。
    /// 其中 `missionCurrentNull` 是这次修复的**关键自证字段** —— 它直接回答
    /// "patch 时 `Mission.Current` 到底是不是 null"（即 `MovementOrder..cctor()` 会不会再失败）。
    /// </summary>
    public void WritePatchMarker()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_config.DumpDir);
            var path = System.IO.Path.Combine(_config.DumpDir, "patch-marker.txt");
            var sb = new StringBuilder();
            sb.AppendLine("CommandLabProbe patch marker (deferred to first mission tick)");
            sb.AppendLine("at                 = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("patchedCount       = " + Patches.Applied.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("patchFailure       = " + (Patches.Failure ?? "(none)"));
            sb.AppendLine("missionCurrentNull = " + (Mission.Current is null ? "YES" : "no"));
            sb.AppendLine("--- patched methods ---");
            foreach (var pm in Patches.Applied) sb.AppendLine(pm);
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // 同 WriteBootMarker：诊断失败不得影响游戏。
        }
    }

    /// <summary>
    /// 崩溃证据落盘。**独立文件、不走 Dump 流程** —— 因为抛异常时可能还没到任何 dump 时机，
    /// 而 _hostLines 全在内存里会随之丢失（这正是 2026-09-22 首崩时"磁盘上什么都没有"的原因）。
    ///
    /// 去重：只在第 1 次 + 每第 50 次写盘，避免同一异常每 tick 重复写爆磁盘。
    /// </summary>
    private void RecordCrash(string where, Exception ex, string? context)
    {
        try
        {
            _crashReportsWritten++;
            if (_crashReportsWritten > 1 && _crashReportsWritten % 50 != 0) return;

            System.IO.Directory.CreateDirectory(_config.DumpDir);
            var path = System.IO.Path.Combine(_config.DumpDir,
                "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");

            var sb = new StringBuilder();
            sb.AppendLine("=== CommandLabProbe crash report ===");
            sb.AppendLine("where    = " + where);
            sb.AppendLine("context  = " + (context ?? "-"));
            sb.AppendLine("occurrence = " + _crashReportsWritten.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("t(mission) = " + _clock.Now.ToString("F4", CultureInfo.InvariantCulture));
            sb.AppendLine("--- exception chain ---");
            for (var e = ex; e is not null; e = e.InnerException)
            {
                sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                if (e.StackTrace is not null) sb.AppendLine(e.StackTrace);
                sb.AppendLine();
            }
            sb.AppendLine("--- hostLines (tail 200) ---");
            var start = Math.Max(0, _hostLines.Count - 200);
            for (int i = start; i < _hostLines.Count; i++) sb.AppendLine(_hostLines[i]);
            sb.AppendLine("--- bindings ---");
            foreach (var kv in _bindings)
                sb.AppendLine(kv.Value.EntityId + "  idx=" + SafeIndex(kv.Value.Formation));

            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // 连崩溃报告都写不出来时，静默 —— 绝不能因为记录崩溃而制造新崩溃。
        }
    }

    /// <summary>供 AppDomain.UnhandledException 处理器调用（崩溃发生在宿主代码里时也要留痕）。</summary>
    public void RecordFatal(string where, Exception ex) => RecordCrash(where, ex, "appdomain");

    private static string SafeIndex(Formation formation)
    {
        try { return formation.Index.ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }

    public string? LastDumpPath { get; private set; }
    public string? LastDumpError { get; private set; }

    public string DescribeConfig()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "level={0} ttl={1}s autoRenew={2} enforceGated={3} enforceNonGated={4} " +
            "patch(mv/fa/ar)={5}/{6}/{7} arrangementPath={8} controlledByAiRoute={9} " +
            "restore={10} restoreBeforeRelease={11} tEval={12}",
            _config.Level, _config.TtlSeconds, _config.AutoRenew,
            _config.EnforceOnGated, _config.EnforceOnNonGated,
            _config.PatchMovement, _config.PatchFacing, _config.PatchArrangement,
            _config.ArrangementPath, _config.UseControlledByAiRoute,
            _config.Restore, _config.RestoreBeforeRelease, _config.TEval);
    }
}
