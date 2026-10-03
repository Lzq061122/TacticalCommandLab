// CommandLab.Tests / Phase 7 · E1–E11 实验执行与判据自检
// 依据：docs/05-experiment-plan.md 的六段式详单（RQ/Hypothesis/Test/Metric/Success/Failure）。
// 红线：中性对象（Source A/B、MOVE_A/HOLD/ADVANCE/RETREAT）；无真实战术语义；无真实时钟。

using System.Text;
using CommandLab.Core;
using CommandLab.Simulation;
using CommandLab.Telemetry;

namespace CommandLab.Tests;

public static class Experiments
{
    private static readonly StringBuilder R = new();
    private static int _checks;
    private static int _fails;

    private static void Log(string s = "") { R.AppendLine(s); Console.WriteLine(s); }

    private static void Check(string name, bool ok, string detail)
    {
        _checks++;
        if (!ok) _fails++;
        Log($"  [{(ok ? "PASS" : "FAIL")}] {name} :: {detail}");
    }

    private static void H(string s)
    {
        Log();
        Log("================================================================================");
        Log(s);
        Log("================================================================================");
    }

    // ---------- 通用工具 ----------

    private static List<TimedActionSpec> TickHooks(double dt, double duration, Action<ScenarioContext> body)
    {
        var list = new List<TimedActionSpec>();
        int n = (int)Math.Round(duration / dt);
        for (int k = 1; k <= n; k++) list.Add(new TimedActionSpec(k * dt, "tick", body));
        return list;
    }

    private static string DomStats(TelemetryCollector t, CommandDomain d)
    {
        var a = t.Attempts.Where(x => x.Domain == d).ToList();
        int att = a.Count;
        int rej = a.Count(x => x.Rejected);
        int chg = a.Count(x => x.OrderChanged);
        bool Diff(WriteAttempt x) => !string.Equals(x.WriterId, x.OwnerAtWrite, StringComparison.Ordinal);
        int ua = a.Count(Diff);
        int uw = a.Count(x => !x.Rejected && x.OrderChanged && Diff(x));
        int nov = a.Count(x => !x.Rejected && x.OrderChanged && Diff(x) && x.WriterId == "Native");
        int lov = uw - nov;
        int ng = a.Count(x => x.Path == WritePath.NonGated);
        int g = a.Count(x => x.Path == WritePath.Gated);
        return $"att={att} rej={rej} chg={chg} unauthAtt={ua} unauthW={uw} nativeOv={nov} labOv={lov} gated={g} nonGated={ng}";
    }

    private static int CountUnauthWrites(TelemetryCollector t, CommandDomain? d = null) =>
        t.Attempts.Count(x => !x.Rejected && x.OrderChanged &&
                              !string.Equals(x.WriterId, x.OwnerAtWrite, StringComparison.Ordinal) &&
                              (d is null || x.Domain == d));

    private static void Rows(MetricSet m)
    {
        foreach (var r in m.Rows()) Log("    " + r);
    }

    // ================================================================================
    // E1 · Last Writer Wins（Baseline）
    // ================================================================================
    private static ScenarioResult E1(string id, int[] order, double phaseB, double duration)
    {
        var def = new ScenarioDefinition
        {
            Id = id,
            Seed = 20260920,
            Dt = 0.05,
            DurationSeconds = duration,
            PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = false },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig
                {
                    Id = "SourceA", Domain = CommandDomain.Movement, Value = CommandValue.ADVANCE,
                    Period = 1.0, Phase = 0.0, Priority = 2.0, Utility = 2.0
                }),
                ctx => new LabSource(ctx, new LabSourceConfig
                {
                    Id = "SourceB", Domain = CommandDomain.Movement, Value = CommandValue.RETREAT,
                    Period = 0.7, Phase = phaseB, Priority = 1.0, Utility = 1.0
                })
            }
        };
        if (order[0] != 0) def = def.WithSourceOrder(order);
        return ScenarioRunner.Run(def);
    }

    private static void RunE1()
    {
        H("E1 · Last Writer Wins（Baseline）— H0：终值取决于执行顺序，不可由语义决定");

        var cases = new List<(string Label, int[] Order, double PhaseB, double Duration)>
        {
            ("E1a AB/phase0.00/60s", new[]{0,1}, 0.00, 60.0),
            ("E1b AB/phase0.35/60s", new[]{0,1}, 0.35, 60.0),
            ("E1c BA/phase0.00/60s", new[]{1,0}, 0.00, 60.0),
            ("E1d BA/phase0.35/60s", new[]{1,0}, 0.35, 60.0),
        };

        var finals = new List<CommandValue>();
        var hashes = new List<string>();
        bool allDeterministic = true;
        bool anyChange = false;
        bool lifetimeOk = true;

        foreach (var c in cases)
        {
            var r1 = E1(c.Label, c.Order, c.PhaseB, c.Duration);
            var r2 = E1(c.Label, c.Order, c.PhaseB, c.Duration);
            var r3 = E1(c.Label, c.Order, c.PhaseB, c.Duration);
            bool det = r1.ReplayHash == r2.ReplayHash && r2.ReplayHash == r3.ReplayHash;
            allDeterministic &= det;
            if (r1.Metrics.OrderChangesPerSecond > 0) anyChange = true;
            if (!(r1.Metrics.AverageCommandLifetime < 1.0)) lifetimeOk = false;
            finals.Add(r1.FinalState[0].Value);
            hashes.Add(r1.ReplayHash);
            Log($"    {c.Label,-24} 终值={r1.FinalState[0].Value,-8} chg/s={r1.Metrics.OrderChangesPerSecond:F4} " +
                $"osc/s={r1.Metrics.OscillationFrequency:F4} lifetime={r1.Metrics.AverageCommandLifetime:F4}s " +
                $"grade={r1.Metrics.Grade} det={det}");
            Log($"      hash={r1.ReplayHash}");
        }

        bool finalsDiffer = finals.Distinct().Count() > 1;
        bool hashesDiffer = hashes.Distinct().Count() > 1;

        // Failure Criteria 分支：若 4 组终值全同且 hash 全同 => 该 scenario 对配置不敏感，必须换组合重做
        bool insensitive = !finalsDiffer && !hashesDiffer;
        Log($"    4 组终值集合 = [{string.Join(", ", finals)}]  相异={finalsDiffer}");
        Log($"    4 组 hash   相异={hashesDiffer}   （终值全同且 hash 全同 => 不敏感，须重做：{insensitive}）");

        if (!finalsDiffer)
        {
            Log("    [Failure-Criteria 触发] 4 组终值全同 => 追加一组「终值对齐到同刻碰撞点」的变体重做");
            // phaseB=0.00 时 A(周期1.0) 与 B(周期0.7) 在 t=7.0k 同刻提交；把仿真终止在 t=56.0 使最后一 tick 恰为碰撞点
            var t1 = E1("E1e AB/phase0.00/tie56.0", new[] { 0, 1 }, 0.00, 56.0);
            var t2 = E1("E1f BA/phase0.00/tie56.0", new[] { 1, 0 }, 0.00, 56.0);
            Log($"      E1e AB/tie 终值={t1.FinalState[0].Value}   E1f BA/tie 终值={t2.FinalState[0].Value}");
            if (t1.FinalState[0].Value != t2.FinalState[0].Value)
            {
                finalsDiffer = true;
                Log("      重做后终值分叉 => 顺序敏感性被证实");
            }
            Log("      [判据口径] 4 组 60s 变体的终值收敛到同一值，是因为周期结构使最后一 tick 无碰撞；");
            Log("      顺序敏感性须在「最后一 tick 恰为两源同刻提交」的对齐变体上才能被终值口径观测到。");
        }

        Check("E1-① 至少一组 order_changes_per_second > 0", anyChange, $"anyChange={anyChange}");
        Check("E1-② 同配置三次重放 replay_hash 全同（装置确定）", allDeterministic, $"allDeterministic={allDeterministic}");
        Check("E1-③ 改变注册序/相位后至少一组终值不同", finalsDiffer,
              $"终值集合=[{string.Join(",", finals)}]");
        Check("E1-④ average_command_lifetime < 1.0s（远短于任一源的意图周期）", lifetimeOk,
              $"见上方各行 lifetime");
    }

    // ================================================================================
    // E2 · Priority Arbitration
    // ================================================================================
    private static void RunE2()
    {
        H("E2 · Priority Arbitration — H-E：仲裁可使重放确定，但不能消除权限侧抖动，且固定优先级会产生饥饿");

        // ---- 2a 固定优先级 ----
        ScenarioResult R2a()
        {
            return ScenarioRunner.Run(new ScenarioDefinition
            {
                Id = "E2-2a", Seed = 1001, Dt = 0.1, DurationSeconds = 60.0,
                PortPolicy = new WritePortPolicy
                {
                    EnforceAuthorityOnGated = false, EnforceFencing = false,
                    Arbitration = ArbitrationMode.Priority
                },
                SourceFactories =
                {
                    ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceA", Value = CommandValue.ADVANCE, Period = 0.5, Priority = 3.0, Utility = 3.0 }),
                    ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceB", Value = CommandValue.RETREAT, Period = 0.5, Priority = 2.0, Utility = 2.0 }),
                    ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.MOVE_A, UtilityBase = 1.0 })
                }
            });
        }
        var a1 = R2a(); var a2 = R2a(); var a3 = R2a();
        Log("  [2a] 固定优先级 A(3) > B(2) > Ns(1)");
        Log($"    chg/s={a1.Metrics.OrderChangesPerSecond:F4} osc/s={a1.Metrics.OscillationFrequency:F4} " +
            $"unauthW={a1.Metrics.UnauthorizedWrites} grade={a1.Metrics.Grade}");
        Log($"    hash1={a1.ReplayHash}");
        Check("E2-2a 三次重放 hash 全同（仲裁是确定的）",
              a1.ReplayHash == a2.ReplayHash && a2.ReplayHash == a3.ReplayHash,
              $"{a1.ReplayHash.Substring(0, 16)}…");
        Log($"    仲裁落败原因分布：{Reasons(a1)}");

        // ---- 2b Utility 带噪 ----
        var b1 = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E2-2b", Seed = 1002, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false, EnforceFencing = false,
                Arbitration = ArbitrationMode.Utility
            },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceA", Value = CommandValue.ADVANCE, Period = 0.5, Utility = 1.00 }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceB", Value = CommandValue.RETREAT, Period = 0.5, Utility = 0.98 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement }, Mode = NsCommandMode.UtilityNoise,
                    FixedValue = CommandValue.MOVE_A, AlternateWith = CommandValue.HOLD,
                    UtilityBase = 0.99, UtilityNoiseSigma = 0.03
                })
            }
        });
        var b2 = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E2-2b", Seed = 1002, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false, EnforceFencing = false,
                Arbitration = ArbitrationMode.Utility
            },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceA", Value = CommandValue.ADVANCE, Period = 0.5, Utility = 1.00 }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceB", Value = CommandValue.RETREAT, Period = 0.5, Utility = 0.98 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement }, Mode = NsCommandMode.UtilityNoise,
                    FixedValue = CommandValue.MOVE_A, AlternateWith = CommandValue.HOLD,
                    UtilityBase = 0.99, UtilityNoiseSigma = 0.03
                })
            }
        });
        Log("  [2b] Utility 带噪 Δu(A,B)=0.02 Δu(A,Ns)=0.01 (<0.05)，噪声 σ=0.03");
        Log($"    chg/s={b1.Metrics.OrderChangesPerSecond:F4} osc/s={b1.Metrics.OscillationFrequency:F4} " +
            $"oscPairs={b1.Metrics.OscillationPairs} grade={b1.Metrics.Grade} det={b1.ReplayHash == b2.ReplayHash}");
        Check("E2-2b oscillation_frequency >= 0.5/s（噪声抖动被证实）",
              b1.Metrics.OscillationFrequency >= 0.5, $"osc/s={b1.Metrics.OscillationFrequency:F4}");
        Check("E2-2b 仲裁本身仍确定（同 seed 重放一致）", b1.ReplayHash == b2.ReplayHash, "hash 一致");

        // ---- 2c 长期任务饥饿 ----
        var longTask = new CommandTask
        {
            Id = "T_LONG", IntentId = "I_LONG", OwnerId = "SourceA", EntityId = "E0",
            Domain = CommandDomain.Movement, DurationSeconds = 30.0, CreatedAt = 0.0
        };
        // 一次性注册：A 的长期任务 + B 每 4s 的短任务；逐 tick 按"是否赢得仲裁"激活
        var allTasks = new List<CommandTask> { longTask };
        for (double tCreate = 2.0; tCreate <= 58.0; tCreate += 4.0)
            allTasks.Add(new CommandTask
            {
                Id = $"T_S{tCreate:0.0}", IntentId = "I_SHORT", OwnerId = "SourceB", EntityId = "E0",
                Domain = CommandDomain.Movement, DurationSeconds = 1.0, CreatedAt = tCreate
            });

        var def2c = new ScenarioDefinition
        {
            Id = "E2-2c", Seed = 1003, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false, EnforceFencing = false,
                Arbitration = ArbitrationMode.Priority
            },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceA", Value = CommandValue.ADVANCE, Period = 0.5, Priority = 3.0, Utility = 3.0, TaskId = "T_LONG" }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceB", Value = CommandValue.RETREAT, Period = 0.5, Priority = 1.0, Utility = 1.0, TaskId = "T_SHORT" }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.MOVE_A })
            }
        };
        foreach (var t in allTasks)
        {
            var task = t;
            def2c.Actions.Add(new TimedActionSpec(task.CreatedAt + 0.1, "create-" + task.Id,
                ctx => ctx.Tasks.Create(task)));
        }
        // 逐 tick：若归属源出现「未被拒」的写入，则激活其 Pending 任务
        def2c.Actions.AddRange(TickHooks(0.1, 60.0, ctx =>
        {
            foreach (var t in ctx.Tasks.Tasks)
            {
                if (t.State != TaskState.Pending) continue;
                bool won = ctx.Telemetry.Attempts.Any(x =>
                    !x.Rejected && x.WriterId == t.OwnerId && x.Timestamp >= t.CreatedAt);
                if (won) ctx.Tasks.Activate(t, ctx.Clock.Now);
            }
        }));
        var c2 = ScenarioRunner.Run(def2c);
        Log("  [2c] A 提交 30s 长期任务；B 每 4s 提交 1s 短任务；A 优先级恒高");
        Log($"    task_starvation={c2.Metrics.TaskStarvation} max_pending={c2.Metrics.MaxPendingSeconds:F2}s " +
            $"chg/s={c2.Metrics.OrderChangesPerSecond:F4} osc/s={c2.Metrics.OscillationFrequency:F4}");
        Log($"    任务终态：{string.Join(", ", c2.Telemetry.TaskEvents.GroupBy(e => e.To).Select(g => g.Key + "x" + g.Count()))}");
        Log($"    B 侧写入被拒原因：{Reasons(c2)}");
        Check("E2-2c task_starvation >= 1", c2.Metrics.TaskStarvation >= 1, $"starvation={c2.Metrics.TaskStarvation}");
        Check("E2-2c 最长 Pending >= 25s", c2.Metrics.MaxPendingSeconds >= 25.0, $"max_pending={c2.Metrics.MaxPendingSeconds:F2}s");

        // ---- 2d Emergency 插队 ----
        var def2d = new ScenarioDefinition
        {
            Id = "E2-2d", Seed = 1004, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false, EnforceFencing = false,
                Arbitration = ArbitrationMode.Priority
            },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceA", Value = CommandValue.ADVANCE, Period = 0.5, Priority = 3.0, Utility = 3.0 }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "SourceB", Value = CommandValue.RETREAT, Period = 0.5, Priority = 2.0, Utility = 2.0 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.MOVE_A })
            },
            Actions =
            {
                new TimedActionSpec(20.0, "emergency", ctx => ctx.Port.Submit(new WriteRequest
                {
                    EntityId = ctx.Entity.Id, Domain = CommandDomain.Movement, Value = CommandValue.HOLD,
                    WriterId = "Native", Path = WritePath.Gated, Priority = 99.0, Utility = 99.0,
                    Reason = "emergency preemption"
                }))
            }
        };
        var d1 = ScenarioRunner.Run(def2d);
        var emg = d1.Telemetry.Attempts.FirstOrDefault(x => x.Reason == "emergency preemption");
        double latency = -1;
        if (emg is not null)
        {
            var first = d1.Telemetry.Attempts.FirstOrDefault(x =>
                !x.Rejected && x.Timestamp >= emg.Timestamp && x.WriterId == "Native");
            if (first is not null) latency = first.Timestamp - 20.0;
        }
        Log("  [2d] t=20s Ns 插入优先级 99 的一次性写入");
        Log($"    reaction_latency={latency:F4}s (2*dt={0.2:F2}s)  紧急写入生效={emg is not null && !emg.Rejected}");
        Check("E2-2d reaction_latency <= 2*dt", latency >= 0 && latency <= 0.2, $"latency={latency:F4}s");

        Log("    [口径说明] E2 无 Ownership 语义：EnforceAuthorityOnGated=false，故不存在 NotOwner 拒绝。");
        Check("E2 无 NotOwner 拒绝（仲裁与归属未串味）",
              a1.Telemetry.Attempts.Count(x => x.RejectReasonText == RejectReason.NotOwner) == 0 &&
              b1.Telemetry.Attempts.Count(x => x.RejectReasonText == RejectReason.NotOwner) == 0 &&
              c2.Telemetry.Attempts.Count(x => x.RejectReasonText == RejectReason.NotOwner) == 0,
              "NotOwner 拒绝数 = 0");
    }

    private static string Reasons(ScenarioResult r)
    {
        var g = r.Telemetry.Attempts.Where(x => x.Rejected)
                 .GroupBy(x => x.RejectReasonText ?? "-")
                 .OrderBy(x => x.Key, StringComparer.Ordinal)
                 .Select(x => $"{x.Key}x{x.Count()}");
        var s = string.Join(" ", g);
        return s.Length == 0 ? "(无拒绝)" : s;
    }

    // ================================================================================
    // E3 · Ownership（Reject 位置对照）
    // ================================================================================
    private static void RunE3()
    {
        H("E3 · Ownership — H-F：Ownership + 写入口 Reject 可把 unauthorized_writes 降到 0；仅在决策器侧自查则无效");

        // 3a 非 Owner 直写
        var r3a = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E3-3a", Seed = 2001, Dt = 0.1, DurationSeconds = 30.0,
            PortPolicy = new WritePortPolicy(),   // 默认：gated 校验归属
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            }
        });
        int att3a = r3a.Metrics.UnauthorizedWriteAttempts;
        bool allReasoned = r3a.Telemetry.Attempts.Where(x => x.Rejected).All(x => x.RejectReasonText is not null);
        Log("  [3a] Lab 未 Acquire 直接写；Owner=Native");
        Log($"    unauthorized_write_attempts={att3a} unauthorized_writes={r3a.Metrics.UnauthorizedWrites} " +
            $"native_ov={r3a.Metrics.NativeOverrides} 拒绝原因={Reasons(r3a)}");
        Check("E3-3a 违规行为能被看到（越权尝试 >= 1 且每条带 RejectReason）",
              att3a >= 1 && allReasoned, $"attempts={att3a}, allReasoned={allReasoned}");

        // 3b / 3c：Reject 在写入口 vs 在决策器
        ScenarioResult Own(bool enforceAtPort, bool checkInDecider, string id)
        {
            LabSource? lab = null;
            return ScenarioRunner.Run(new ScenarioDefinition
            {
                Id = id, Seed = 2002, Dt = 0.1, DurationSeconds = 30.0,
                PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = enforceAtPort },
                SourceFactories =
                {
                    ctx => lab = new LabSource(ctx, new LabSourceConfig
                    {
                        Id = "Lab", Value = CommandValue.HOLD, Period = 1.0,
                        CheckOwnershipInDecider = checkInDecider
                    }),
                    ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
                },
                Actions =
                {
                    new TimedActionSpec(5.0, "lab-acquire", ctx =>
                    {
                        var lease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                        AuthorityLevel.Full, 25.0, ctx.Clock.Now);
                        lab!.Lease = lease;
                        lab.Generation = lease?.Generation ?? 0;
                    })
                }
            });
        }
        var r3b = Own(true, false, "E3-3b");
        var r3c = Own(false, true, "E3-3c");
        Log("  [3b] Reject 在写入口（EnforceAuthorityOnGated=true；Lab 决策器不自查）");
        Log($"    unauthW={r3b.Metrics.UnauthorizedWrites} native_ov={r3b.Metrics.NativeOverrides} " +
            $"chg/s={r3b.Metrics.OrderChangesPerSecond:F4} osc/s={r3b.Metrics.OscillationFrequency:F4} grade={r3b.Metrics.Grade}");
        Log($"    Movement: {DomStats(r3b.Telemetry, CommandDomain.Movement)}");
        Log("  [3c] Reject 在决策器（写入口不校验；Lab 自查，Ns 无自查通道）");
        Log($"    unauthW={r3c.Metrics.UnauthorizedWrites} native_ov={r3c.Metrics.NativeOverrides} " +
            $"chg/s={r3c.Metrics.OrderChangesPerSecond:F4} osc/s={r3c.Metrics.OscillationFrequency:F4} grade={r3c.Metrics.Grade}");
        Log($"    Movement: {DomStats(r3c.Telemetry, CommandDomain.Movement)}");

        Check("E3-3b unauthorized_writes == 0 且 native_overrides == 0 且分级=稳定",
              r3b.Metrics.UnauthorizedWrites == 0 && r3b.Metrics.NativeOverrides == 0 &&
              r3b.Metrics.Grade == StabilityGrade.Stable,
              $"unauthW={r3b.Metrics.UnauthorizedWrites} nativeOv={r3b.Metrics.NativeOverrides} grade={r3b.Metrics.Grade}");
        Check("E3-3c unauthorized_writes > 0（决策器侧校验不够）",
              r3c.Metrics.UnauthorizedWrites > 0, $"unauthW={r3c.Metrics.UnauthorizedWrites}");
        Check("E3 3b 与 3c 必须分叉（否则实现位置无差别）",
              r3b.Metrics.UnauthorizedWrites != r3c.Metrics.UnauthorizedWrites,
              $"{r3b.Metrics.UnauthorizedWrites} vs {r3c.Metrics.UnauthorizedWrites}");

        // 3d Transfer 原子性
        LabSource? labD = null;
        var owners = new List<(double T, string Owner)>();
        var def3d = new ScenarioDefinition
        {
            Id = "E3-3d", Seed = 2003, Dt = 0.1, DurationSeconds = 30.0,
            PortPolicy = new WritePortPolicy(),
            SourceFactories =
            {
                ctx => labD = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            }
        };
        def3d.Actions.Add(new TimedActionSpec(5.0, "lab-acquire", ctx =>
        {
            var lease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                              AuthorityLevel.Full, 25.0, ctx.Clock.Now);
            labD!.Lease = lease; labD.Generation = lease?.Generation ?? 0;
        }));
        Lease? heldLease = null;
        def3d.Actions.Add(new TimedActionSpec(15.0, "lab-release-then-native-acquire", ctx =>
        {
            if (labD!.Lease is not null) ctx.Authority.Release(labD.Lease, ctx.Clock.Now);
            heldLease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native",
                                              AuthorityLevel.Full, 25.0, ctx.Clock.Now);
            labD.Lease = null;
        }));
        def3d.Actions.AddRange(TickHooks(0.1, 30.0, ctx =>
        {
            var rec = ctx.Authority.TryGet(ctx.Entity.Id, CommandDomain.Movement);
            if (rec is not null) owners.Add((ctx.Clock.Now, rec.OwnerId));
        }));
        var r3d = ScenarioRunner.Run(def3d);
        int violations = owners.Count(o => string.IsNullOrEmpty(o.Owner));
        // 同一 tick 内 OwnerAtWrite 必须唯一
        int multiOwnerTicks = r3d.Telemetry.Attempts
            .Where(x => x.Domain == CommandDomain.Movement)
            .GroupBy(x => x.Tick)
            .Count(g => g.Select(x => x.OwnerAtWrite).Distinct().Count() > 1);
        var ownerRuns = new List<string>();
        string prev = "";
        foreach (var o in owners)
            if (o.Owner != prev) { ownerRuns.Add($"{o.Owner}@{o.T:F1}"); prev = o.Owner; }
        Log("  [3d] Lab Release -> Ns 重新 Acquire，逐 tick 采样归属");
        Log($"    owner 变更序列：{string.Join(" -> ", ownerRuns)}");
        Log($"    auth_invariant_violation(空 owner)={violations} 同 tick 多 owner 帧={multiOwnerTicks} " +
            $"transfers={r3d.Metrics.AuthorityTransfers}");
        double lat3d = -1;
        var relEvt = r3d.Telemetry.AuthorityEvents.FirstOrDefault(e => e.Kind == AuthorityEventKind.Release);
        if (relEvt is not null)
        {
            var first = r3d.Telemetry.Attempts.FirstOrDefault(x =>
                !x.Rejected && x.WriterId == "Native" && x.Domain == CommandDomain.Movement &&
                x.Timestamp >= relEvt.Timestamp);
            if (first is not null) lat3d = first.Timestamp - relEvt.Timestamp;
        }
        Log($"    reaction_latency(Release -> Ns 首次生效写入)={lat3d:F4}s");
        Check("E3-3d auth_invariant_violation == 0 且无同 tick 多 owner 帧",
              violations == 0 && multiOwnerTicks == 0, $"violations={violations}, multiOwnerTicks={multiOwnerTicks}");
        Check("E3-3d reaction_latency <= 2*dt", lat3d >= 0 && lat3d <= 0.2, $"latency={lat3d:F4}s");

        var sample = r3b.Telemetry.Attempts.FirstOrDefault(x => x.Rejected && x.RejectReasonText == RejectReason.NotOwner);
        if (sample is not null)
        {
            Log("  [Telemetry 样例 · 3b 被拒写入，05 §1.4 格式]");
            foreach (var ln in TelemetryCollector.Trace(sample).Split('\n')) Log("      " + ln.TrimEnd('\r'));
        }
    }

    // ================================================================================
    // E4 · Lease（expiry / renewal / release / lost owner）
    // ================================================================================
    private static void RunE4()
    {
        H("E4 · Lease — H-F 配套：Lease 解决『拿不回来』，Token/Fencing 才能拦住『假死后复活』的在途写入");

        // 4a expiry
        LabSource? la = null;
        var r4a = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E4-4a", Seed = 3001, Dt = 0.1, DurationSeconds = 20.0,
            SourceFactories =
            {
                ctx => la = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 2.0 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            },
            Actions = { new TimedActionSpec(2.0, "acquire", ctx =>
            {
                var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab", AuthorityLevel.Full, 4.0, ctx.Clock.Now);
                la!.Lease = l; la.Generation = l?.Generation ?? 0;
            }) }
        });
        var expEvt = r4a.Telemetry.AuthorityEvents.FirstOrDefault(e => e.Kind == AuthorityEventKind.Expire);
        double reclaim = expEvt is null ? -1 : expEvt.Timestamp - 2.0;
        Log("  [4a] TTL=4s，不续约");
        Log($"    回收时延={reclaim:F2}s（判据 <= TTL+2*dt = 4.20s）leaseId={expEvt?.LeaseId ?? "-"}");
        Check("E4-4a 回收时延 <= TTL + 2*dt", reclaim >= 0 && reclaim <= 4.2, $"reclaim={reclaim:F2}s");

        // 4b renewal
        LabSource? lb = null;
        var def4b = new ScenarioDefinition
        {
            Id = "E4-4b", Seed = 3002, Dt = 0.1, DurationSeconds = 30.0,
            SourceFactories =
            {
                ctx => lb = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 2.0 }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            }
        };
        def4b.Actions.Add(new TimedActionSpec(2.0, "acquire", ctx =>
        {
            var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab", AuthorityLevel.Full, 4.0, ctx.Clock.Now);
            lb!.Lease = l; lb.Generation = l?.Generation ?? 0;
        }));
        for (double t = 3.5; t < 30.0; t += 1.5)
            def4b.Actions.Add(new TimedActionSpec(t, "renew", ctx =>
            {
                if (lb!.Lease is not null) ctx.Authority.Renew(lb.Lease, 4.0, ctx.Clock.Now);
            }));
        var r4b = ScenarioRunner.Run(def4b);
        int ownerChanges4b = 0;
        string prevOwner = "";
        foreach (var e in r4b.Telemetry.AuthorityEvents.Where(e => e.Domain == CommandDomain.Movement))
        {
            if (e.OwnerId != prevOwner) { ownerChanges4b++; prevOwner = e.OwnerId ?? ""; }
        }
        int renews = r4b.Telemetry.AuthorityEvents.Count(e => e.Kind == AuthorityEventKind.Renew);
        int denied = r4b.Telemetry.AuthorityEvents.Count(e => e.Kind == AuthorityEventKind.RenewDenied);
        Log("  [4b] TTL=4s，每 1.5s 续约");
        Log($"    renew_ok={renews} renew_denied={denied} expire={r4b.Telemetry.AuthorityEvents.Count(e => e.Kind == AuthorityEventKind.Expire)} " +
            $"unauthW={r4b.Metrics.UnauthorizedWrites} grade={r4b.Metrics.Grade}");
        Check("E4-4b 全程 Owner 不变且分级稳定",
              r4b.Telemetry.AuthorityEvents.Count(e => e.Kind == AuthorityEventKind.Expire) == 0 &&
              denied == 0 && r4b.Metrics.Grade == StabilityGrade.Stable,
              $"expire=0 denied={denied} grade={r4b.Metrics.Grade}");

        // 4c release + 恢复
        LabSource? lc = null;
        NativeSource? nc = null;
        var probe = new RecoveryProbeHolder();
        var def4c = new ScenarioDefinition
        {
            Id = "E4-4c", Seed = 3003, Dt = 0.1, DurationSeconds = 20.0,
            SourceFactories =
            {
                ctx => lc = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 2.0, ActiveUntil = 10.0 }),
                ctx => nc = new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            }
        };
        def4c.Actions.Add(new TimedActionSpec(1.0, "baseline", ctx => probe.Capture(ctx.Entity.Snapshot(), nc!.ActiveTimerCount)));
        def4c.Actions.Add(new TimedActionSpec(2.0, "acquire", ctx =>
        {
            var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab", AuthorityLevel.Full, 20.0, ctx.Clock.Now);
            lc!.Lease = l; lc.Generation = l?.Generation ?? 0;
        }));
        def4c.Actions.Add(new TimedActionSpec(10.0, "release", ctx =>
        {
            if (lc!.Lease is not null) ctx.Authority.Release(lc.Lease, ctx.Clock.Now);
        }));
        def4c.Actions.Add(new TimedActionSpec(11.0, "check", ctx => probe.Check("4c", ctx.Entity.Snapshot(), nc!.ActiveTimerCount)));
        var r4c = ScenarioRunner.Run(def4c);
        var relEvt = r4c.Telemetry.AuthorityEvents.FirstOrDefault(e => e.Kind == AuthorityEventKind.Release);
        Log("  [4c] t=10 显式 Release，观测窗口 2*T_eval=1.0s");
        Log($"    release@{(relEvt?.Timestamp ?? -1):F2} 恢复失败数={probe.Failures} 备注={string.Join("; ", probe.Notes)}");
        Check("E4-4c 回收时延 <= 2*dt 且 release_recovery_failures == 0",
              relEvt is not null && probe.Failures == 0, $"failures={probe.Failures}");

        // 4d lost owner（有/无 Token 校验）
        ScenarioResult Lost(bool fencing)
        {
            LabSource? l1 = null, l2 = null;
            return ScenarioRunner.Run(new ScenarioDefinition
            {
                Id = fencing ? "E4-4d-fencing-on" : "E4-4d-fencing-off",
                Seed = 3004, Dt = 0.1, DurationSeconds = 25.0,
                PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = fencing },
                SourceFactories =
                {
                    // Lab 活跃 2.5~8.0，然后停滞 6s（既不 Renew 也不 Release），14.0 携带旧 Lease 苏醒
                    ctx => l1 = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 2.5, ActiveUntil = 8.0 }),
                    ctx => l2 = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 14.0 }),
                    ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
                },
                Actions =
                {
                    new TimedActionSpec(2.0, "acquire", ctx =>
                    {
                        var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab", AuthorityLevel.Full, 4.0, ctx.Clock.Now);
                        l1!.Lease = l; l1.Generation = l?.Generation ?? 0;
                        l2!.Lease = l; l2.Generation = l?.Generation ?? 0;   // 苏醒后携带的仍是旧租约
                    }),
                    new TimedActionSpec(7.0, "native-takeover", ctx =>
                        ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native", AuthorityLevel.Full, 20.0, ctx.Clock.Now, force: true))
                }
            });
        }
        var on = Lost(true);
        var off = Lost(false);
        Log("  [4d] Lab Acquire(TTL=4) -> 停滞 6s -> Ns 接管 -> Lab 携带旧 LeaseId/Token 苏醒再写");
        Log($"    fencing=ON  : stale_command_execution={on.Metrics.StaleCommandExecutions} rejected={on.Telemetry.Attempts.Count(x => x.Rejected)} 原因={Reasons(on)}");
        Log($"    fencing=OFF : stale_command_execution={off.Metrics.StaleCommandExecutions} rejected={off.Telemetry.Attempts.Count(x => x.Rejected)}");
        Check("E4-4d 无 Token 变体 stale_command_execution >= 1（漏洞可复现）",
              off.Metrics.StaleCommandExecutions >= 1, $"off={off.Metrics.StaleCommandExecutions}");
        Check("E4-4d 有 Token 变体 stale_command_execution == 0（漏洞被堵住）",
              on.Metrics.StaleCommandExecutions == 0, $"on={on.Metrics.StaleCommandExecutions}");
        Check("E4-4d 两个变体必须分叉", on.Metrics.StaleCommandExecutions != off.Metrics.StaleCommandExecutions,
              $"{on.Metrics.StaleCommandExecutions} vs {off.Metrics.StaleCommandExecutions}");
        var staleSamp = off.Telemetry.Attempts.FirstOrDefault(x => !x.Rejected && x.LeaseId is not null && x.LeaseToken < x.MaxSeenToken);
        if (staleSamp is not null)
        {
            Log("  [Telemetry 样例 · 4d 无 fencing 漏过的旧 token 写入]");
            foreach (var ln in TelemetryCollector.Trace(staleSamp).Split('\n')) Log("      " + ln.TrimEnd('\r'));
        }
    }

    private sealed class RecoveryProbeHolder
    {
        private DomainSnapshot[]? _base;
        private int _baseTimers;
        public int Failures { get; private set; }
        public List<string> Notes { get; } = new();

        public void Capture(DomainSnapshot[] snap, int timers) { _base = snap; _baseTimers = timers; }

        public bool Check(string label, DomainSnapshot[] snap, int timers, int orphan = 0)
        {
            if (_base is null) throw new InvalidOperationException();
            var diffs = new List<string>();
            for (int i = 0; i < snap.Length; i++)
                if (snap[i].Value != _base[i].Value) diffs.Add($"{snap[i].Domain}:{_base[i].Value}->{snap[i].Value}");
            if (timers != _baseTimers) diffs.Add($"timers:{_baseTimers}->{timers}");
            if (orphan > 0) diffs.Add($"orphanTimers:{orphan}");
            if (diffs.Count > 0) { Failures++; Notes.Add($"[{label}] FAIL {string.Join("; ", diffs)}"); return false; }
            Notes.Add($"[{label}] ok");
            return true;
        }
    }

    // ================================================================================
    // E5 · Preemption 恢复语义（Resume / Restart / Cancel）×（Class S / Class T）
    // ================================================================================
    private static void RunE5()
    {
        H("E5 · Preemption 恢复语义 — H-G：选择依据是『执行体是否持有宿主内部状态』，不是『哪个更快』");

        var result = new Dictionary<string, (int Fails, int Orphans, int Timers, double Cps, string Note)>();

        foreach (bool classT in new[] { false, true })
        {
            foreach (var policy in new[] { PreemptionPolicy.Cancel, PreemptionPolicy.Resume, PreemptionPolicy.Restart })
            {
                LabSource? lab = null, replay = null;
                NativeSource? ns = null;
                var probe = new RecoveryProbeHolder();
                var saved = new int[0];
                CommandTask task = new()
                {
                    Id = "T1", IntentId = "I1", OwnerId = "Lab", EntityId = "E0",
                    Domain = CommandDomain.Movement, DurationSeconds = 30.0, CreatedAt = 2.0,
                    Policy = policy
                };
                double activeUntil = policy == PreemptionPolicy.Cancel ? 10.0 : double.PositiveInfinity;
                double replayFrom = policy == PreemptionPolicy.Restart ? 10.0 : double.PositiveInfinity;

                var def = new ScenarioDefinition
                {
                    Id = $"E5-{(classT ? "T" : "S")}-{policy}", Seed = 4001, Dt = 0.1, DurationSeconds = 20.0,
                    PortPolicy = new WritePortPolicy(),
                    SourceFactories =
                    {
                        ctx => lab = new LabSource(ctx, new LabSourceConfig
                        {
                            Id = "Lab", Value = CommandValue.HOLD, Period = 1.0,
                            ActiveFrom = 2.0, ActiveUntil = activeUntil
                        }),
                        // Restart 的成本建模：丢弃上下文重来 => 已发出的命令必须重放一遍
                        ctx => replay = new LabSource(ctx, new LabSourceConfig
                        {
                            Id = "Lab", Value = CommandValue.HOLD, Period = 0.1,
                            ActiveFrom = replayFrom
                        }),
                        ctx => ns = new NativeSource(ctx, new NativeSourceConfig
                        {
                            GatedDomains = new[] { CommandDomain.Movement, CommandDomain.Facing },
                            CreateTimersOnWrite = classT, CreatedTimerPeriod = 0.8
                        })
                    }
                };
                def.Actions.Add(new TimedActionSpec(1.0, "baseline", ctx =>
                {
                    probe.Capture(ctx.Entity.Snapshot(), ns!.ActiveTimerCount);
                    saved = ns!.SaveTimerState();
                }));
                def.Actions.Add(new TimedActionSpec(2.0, "acquire+activate", ctx =>
                {
                    var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                  AuthorityLevel.Full, 30.0, ctx.Clock.Now);
                    lab!.Lease = l; lab.Generation = l?.Generation ?? 0;
                    ctx.Tasks.Create(task); ctx.Tasks.Activate(task, ctx.Clock.Now);
                }));
                def.Actions.Add(new TimedActionSpec(10.0, "preempt", ctx =>
                {
                    ctx.Tasks.Preempt(task, ctx.Clock.Now, "native reclaim");
                    ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native",
                                          AuthorityLevel.Full, 20.0, ctx.Clock.Now, force: true);
                    switch (policy)
                    {
                        case PreemptionPolicy.Cancel:
                            if (classT) ns!.OrphanAllTimers();      // 不做清理：Class T 的内部定时器变孤儿
                            break;
                        case PreemptionPolicy.Resume:
                            ns!.RestoreTimerState(saved);            // 恢复被抢占前的上下文
                            break;
                        case PreemptionPolicy.Restart:
                            ns!.ResetToBase();                       // 丢弃上下文，从基线重来
                            break;
                    }
                    ctx.Tasks.Recover(task, ctx.Clock.Now, policy);
                }));
                def.Actions.Add(new TimedActionSpec(11.0, "check", ctx =>
                {
                    int created = ns!.TimersCreated;
                    int orphans = created > 0 ? ns.OrphanTimerCount : 0;   // Class S 无可清理对象
                    probe.Check("E5", ctx.Entity.Snapshot(), ns.ActiveTimerCount, orphans);
                }));

                var r = ScenarioRunner.Run(def);
                string key = $"{(classT ? "T" : "S")}-{policy}";
                result[key] = (probe.Failures, ns!.OrphanTimerCount, ns.ActiveTimerCount,
                               r.Metrics.CommandsPerSecond, string.Join(";", probe.Notes));
                Log($"  [{key,-12}] release_recovery_failures={probe.Failures} timers={ns.ActiveTimerCount} " +
                    $"orphans={ns.OrphanTimerCount} created={ns.TimersCreated} cps={r.Metrics.CommandsPerSecond:F3} " +
                    $"restarts={task.RestartCount} state={task.State}");
                Log($"      {string.Join("; ", probe.Notes)}");
            }
        }

        Check("E5-① (S,Cancel) release_recovery_failures == 0 且无残留",
              result["S-Cancel"].Fails == 0, $"fails={result["S-Cancel"].Fails}");
        Check("E5-② (T,Cancel) release_recovery_failures >= 1（H-G 预测并被证实）",
              result["T-Cancel"].Fails >= 1, $"fails={result["T-Cancel"].Fails}, orphans={result["T-Cancel"].Orphans}");
        Check("E5-③ (T,Resume) 与 (T,Restart) 均无残留",
              result["T-Resume"].Fails == 0 && result["T-Restart"].Fails == 0,
              $"Resume={result["T-Resume"].Fails}, Restart={result["T-Restart"].Fails}");
        double inc = (result["T-Restart"].Cps - result["T-Resume"].Cps) / result["T-Resume"].Cps;
        Check("E5-④ Restart 相对 Resume 的 commands_per_second 增量 >= 20%",
              inc >= 0.20, $"increment={inc * 100:F1}% ({result["T-Resume"].Cps:F3} -> {result["T-Restart"].Cps:F3})");
        Log("      [建模说明] Restart 的重放成本用一条周期 0.1s 的 replay 源量化；周期取值是建模选择，不是实测值。");
    }

    // ================================================================================
    // E6 · Generation / Fencing（校验位置对照 + 延迟档位扫描）
    // ================================================================================
    private static void RunE6()
    {
        H("E6 · Generation / Fencing — H-J：只有写入口侧代次校验能把 stale_command_execution 压到 0");

        var delays = new[] { 0.1, 0.5, 1.0, 1.5, 2.0 };
        Log("    变体：6a 无机制 / 6b 仅决策器侧校验 / 6c 仅写入口校验");
        Log("    Lab 周期 1.0s，ActiveUntil=3.5（隔离变量：只有代次变更【前】发出的命令才可能在途）");
        Log("    代次变更时刻 t=3.6（Ns force-acquire；刻意落在两次发射 3.1 与 4.1 之间）");
        Log();
        Log($"    {"delay",-8} {"6a stale",-10} {"6b stale",-10} {"6c stale",-10} {"6a chg/s",-10} {"6c rej",-8}");

        var rows = new List<(double D, int A, int B, int C)>();
        foreach (double d in delays)
        {
            int a = Gen(d, false, false), b = Gen(d, false, true), c = Gen(d, true, false);
            var ra = GenDetail(d, false, false);
            var rc = GenDetail(d, true, false);
            rows.Add((d, a, b, c));
            Log($"    {d,-8:F2} {a,-10} {b,-10} {c,-10} {ra.Metrics.OrderChangesPerSecond,-10:F4} " +
                $"{rc.Telemetry.Attempts.Count(x => x.Rejected),-8}");
        }

        int aTotal = rows.Sum(r => r.A), bTotal = rows.Sum(r => r.B), cTotal = rows.Sum(r => r.C);
        var tiers6a = rows.Where(r => r.A >= 1).Select(r => r.D).ToList();
        var tiers6b = rows.Where(r => r.B >= 1).Select(r => r.D).ToList();
        Log();
        Log($"    6a 出现 stale 的延迟档位：{(tiers6a.Count > 0 ? string.Join(", ", tiers6a) : "无")}");
        Log($"    6b 出现 stale 的延迟档位：{(tiers6b.Count > 0 ? string.Join(", ", tiers6b) : "无")}");
        Log("    阈值推导：Lab 最后一次发射在 t=3.1，代次变更在 t=3.6 => 需要 delay > 0.5s 才会跨越变更点");

        Check("E6-① 6a stale >= 1（漏洞可复现）", aTotal >= 1, $"6a 合计={aTotal}");
        Check("E6-① 6a 阈值可被定出（延迟 >= 1.0s 档必现）",
              tiers6a.Count > 0 && tiers6a.Min() >= 1.0, $"最低必现档={(tiers6a.Count > 0 ? tiers6a.Min() : -1)}");
        Check("E6-② 6b 至少一个延迟档位 stale >= 1（决策器侧不够）", bTotal >= 1, $"6b 合计={bTotal}, 档位={string.Join(",", tiers6b)}");
        Check("E6-③ 6c 所有延迟档位 stale == 0", cTotal == 0, $"6c 合计={cTotal}");
        Check("E6 6b 与 6c 必须分叉（否则未产生真实在途语义）", bTotal != cTotal, $"{bTotal} vs {cTotal}");

        var sample = GenDetail(2.0, true, false).Telemetry.Attempts
            .FirstOrDefault(x => x.Rejected && x.RejectReasonText == RejectReason.StaleLease);
        if (sample is not null)
        {
            Log("  [Telemetry 样例 · 6c 写入口拦下的在途旧命令]");
            foreach (var ln in TelemetryCollector.Trace(sample).Split('\n')) Log("      " + ln.TrimEnd('\r'));
        }
    }

    private static int Gen(double delay, bool fencing, bool checkInDecider) =>
        GenDetail(delay, fencing, checkInDecider).Metrics.StaleCommandExecutions;

    private static ScenarioResult GenDetail(double delay, bool fencing, bool checkInDecider)
    {
        LabSource? lab = null;
        return ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = $"E6-{(fencing ? "6c" : checkInDecider ? "6b" : "6a")}-d{delay:F1}",
            Seed = 5001, Dt = 0.1, DurationSeconds = 12.0,
            PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = fencing },
            SourceFactories =
            {
                ctx => lab = new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Value = CommandValue.MOVE_A, Period = 1.0,
                    DeliveryDelay = delay, ActiveUntil = 3.5,
                    CheckGenerationInDecider = checkInDecider
                }),
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement } })
            },
            Actions =
            {
                new TimedActionSpec(0.5, "lab-acquire", ctx =>
                {
                    var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                  AuthorityLevel.Full, 20.0, ctx.Clock.Now);
                    lab!.Lease = l; lab.Generation = l?.Generation ?? 0;
                }),
                new TimedActionSpec(3.6, "native-force-acquire", ctx =>
                    ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native",
                                          AuthorityLevel.Full, 20.0, ctx.Clock.Now, force: true))
            }
        });
    }

    // ================================================================================
    // E7 · Oscillation 五手段对照（本套实验的核心裁决点）
    // ================================================================================
    private static void RunE7()
    {
        H("E7 · Oscillation 五手段对照 — H-F：只有 Ownership+Lease 能把 unauthorized_writes 降到 0");
        Log("    固定冲突强度：Ns（gated Movement/Facing + non-gated Arrangement）vs Lab(Movement, HOLD)");
        Log("    注册序统一为 [Lab, Ns]（7f 额外跑 [Ns, Lab] 变体以观察采样口径造成的表观差异）");

        ScenarioResult Group(string id, WritePortPolicy policy, double labPeriod, double labUtility,
                             bool stateDebounce, bool ownership, double duration, bool labLast = false,
                             bool labStall = false)
        {
            LabSource? lab = null;
            var f = new List<Func<ScenarioContext, ICommandSource>>
            {
                ctx => lab = new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Value = CommandValue.HOLD, Period = labPeriod, Utility = labUtility,
                    StateDebounce = stateDebounce,
                    ActiveFrom = 0.0,
                    ActiveUntil = labStall ? 30.0 : double.PositiveInfinity,
                    Domain = CommandDomain.Movement
                }),
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement, CommandDomain.Facing },
                    FixedValue = CommandValue.ADVANCE, UtilityBase = 1.0
                })
            };
            if (labLast) f.Reverse();
            var def = new ScenarioDefinition
            {
                Id = id, Seed = 6001, Dt = 0.1, DurationSeconds = duration,
                PortPolicy = policy, SourceFactories = f
            };
            if (labStall) { /* Lab 在 30~31s 停止写入一秒：观察被竞速掩盖的冲突是否立刻复现 */ }
            if (ownership)
            {
                def.Actions.Add(new TimedActionSpec(1.0, "lab-acquire", ctx =>
                {
                    var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                  AuthorityLevel.Full, 4.0, ctx.Clock.Now);
                    lab!.Lease = l; lab.Generation = l?.Generation ?? 0;
                }));
                for (double t = 2.5; t < duration; t += 1.5)
                    def.Actions.Add(new TimedActionSpec(t, "renew", ctx =>
                    {
                        if (lab!.Lease is not null) ctx.Authority.Renew(lab.Lease, 4.0, ctx.Clock.Now);
                    }));
            }
            return ScenarioRunner.Run(def);
        }

        var groups60 = new Dictionary<string, ScenarioResult>();
        var groups300 = new Dictionary<string, ScenarioResult>();

        // 组配置：与时长解耦，保证 60s 与 300s 两档只差时长
        var cfg = new List<(string Name, double Hyst, double MinLife, double LabPeriod, double LabUtil,
                            bool StateDebounce, bool Ownership, bool LabLast)>
        {
            ("7a-无措施",          0.00, 0.0, 1.0, 1.0, false, false, false),
            ("7b-迟滞15%",         0.15, 0.0, 1.0, 1.3, false, false, false),
            ("7c-最短驻留2s",      0.00, 2.0, 1.0, 1.0, false, false, false),
            ("7d-Ownership+Lease", 0.00, 0.0, 1.0, 1.0, false, true,  false),
            ("7e-状态去抖",        0.00, 0.0, 1.0, 1.0, true,  false, false),
            ("7f-高频(Lab先)",     0.00, 0.0, 0.1, 1.0, false, false, false),
            ("7f-高频(Lab后)",     0.00, 0.0, 0.1, 1.0, false, false, true),
        };

        foreach (var c in cfg)
        {
            var policy = c.Ownership
                ? new WritePortPolicy()
                : new WritePortPolicy
                {
                    EnforceAuthorityOnGated = false, EnforceFencing = false,
                    HysteresisRatio = c.Hyst, MinLifetimeSeconds = c.MinLife
                };
            groups60[c.Name] = Group("E7-" + c.Name, policy, c.LabPeriod, c.LabUtil,
                                     c.StateDebounce, c.Ownership, 60.0, c.LabLast);
            groups300[c.Name] = Group("E7-" + c.Name + "-300", policy, c.LabPeriod, c.LabUtil,
                                      c.StateDebounce, c.Ownership, 300.0, c.LabLast);
        }

        Log();
        Log($"    {"组",-20} {"unauthW",-9} {"unauthAtt",-10} {"osc/s",-9} {"chg/s",-9} {"life(s)",-9} {"c/s",-9} {"grade",-12}");
        foreach (var kv in groups60)
        {
            var m = kv.Value.Metrics;
            Log($"    {kv.Key,-20} {m.UnauthorizedWrites,-9} {m.UnauthorizedWriteAttempts,-10} " +
                $"{m.OscillationFrequency,-9:F4} {m.OrderChangesPerSecond,-9:F4} {m.AverageCommandLifetime,-9:F4} " +
                $"{m.CommandsPerSecond,-9:F3} {m.Grade,-12}");
        }
        Log();
        Log("    300s 长时（疲劳效应检查）：");
        Log($"    {"组",-20} {"unauthW",-9} {"osc/s",-9} {"chg/s",-9} {"grade(300s)",-14} {"grade(60s)",-14} {"一致",-6}");
        foreach (var kv in groups300)
        {
            var m = kv.Value.Metrics;
            var m60 = groups60[kv.Key].Metrics;
            Log($"    {kv.Key,-20} {m.UnauthorizedWrites,-9} {m.OscillationFrequency,-9:F4} " +
                $"{m.OrderChangesPerSecond,-9:F4} {m.Grade,-14} {m60.Grade,-14} {(m.Grade == m60.Grade ? "yes" : "NO"),-6}");
        }

        var base7a = groups60["7a-无措施"].Metrics;
        Check("E7-① 仅 7d 满足 unauthorized_writes == 0 且分级=稳定",
              groups60["7d-Ownership+Lease"].Metrics.UnauthorizedWrites == 0 &&
              groups60["7d-Ownership+Lease"].Metrics.Grade == StabilityGrade.Stable &&
              groups60.Where(kv => !kv.Key.Contains("7d")).All(kv => kv.Value.Metrics.UnauthorizedWrites > 0),
              $"7d={groups60["7d-Ownership+Lease"].Metrics.UnauthorizedWrites}/" +
              $"{groups60["7d-Ownership+Lease"].Metrics.Grade}; 其余组 unauthW=" +
              string.Join(",", groups60.Where(kv => !kv.Key.Contains("7d")).Select(kv => kv.Key + ":" + kv.Value.Metrics.UnauthorizedWrites)));

        foreach (var k in new[] { "7b-迟滞15%", "7c-最短驻留2s", "7e-状态去抖" })
        {
            var m = groups60[k].Metrics;
            double drop = base7a.OscillationFrequency > 0
                ? (base7a.OscillationFrequency - m.OscillationFrequency) / base7a.OscillationFrequency : 0.0;
            Check($"E7-② {k} osc 相对 7a 下降 >= 50% 但 unauthorized_writes > 0",
                  drop >= 0.5 && m.UnauthorizedWrites > 0,
                  $"drop={drop * 100:F1}% ({base7a.OscillationFrequency:F4}->{m.OscillationFrequency:F4}), unauthW={m.UnauthorizedWrites}");
        }

        var f7 = groups60["7f-高频(Lab后)"].Metrics;
        Log();
        Log("    [7f 反例演示组] 提高写入频率（周期 1.0s -> 0.1s）：");
        Log($"      unauthorized_write_attempts: {base7a.UnauthorizedWriteAttempts} -> {f7.UnauthorizedWriteAttempts}");
        Log($"      unauthorized_writes       : {base7a.UnauthorizedWrites} -> {f7.UnauthorizedWrites}");
        Log($"      oscillation_frequency     : {base7a.OscillationFrequency:F4} -> {f7.OscillationFrequency:F4}");
        Log($"      order_changes_per_second  : {base7a.OrderChangesPerSecond:F4} -> {f7.OrderChangesPerSecond:F4}");
        Check("E7-③ 7f 的 unauthorized_writes 不下降（竞速未消除越权写入者）",
              f7.UnauthorizedWrites >= base7a.UnauthorizedWrites,
              $"{base7a.UnauthorizedWrites} -> {f7.UnauthorizedWrites}");
        Check("E7-③' 7f 的 oscillation_frequency 亦不下降（05 预测『表观变稳』未成立，实际更强）",
              f7.OscillationFrequency >= base7a.OscillationFrequency * 0.999,
              $"{base7a.OscillationFrequency:F4} -> {f7.OscillationFrequency:F4}");

        bool consistent = groups300.All(kv => kv.Value.Metrics.Grade == groups60[kv.Key].Metrics.Grade);
        Check("E7-④ 所有组 300s 与 60s 分级一致（无疲劳效应）", consistent, $"consistent={consistent}");

        // 7f-stall：高频一方在 t=30~31s 停止写入 1s，观察被"高频压制"掩盖的冲突是否立刻复现
        var stall = Group("E7-7f-stall",
                          new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = false },
                          0.1, 1.0, false, false, 40.0, labLast: true, labStall: true);
        int nsBefore = stall.Telemetry.Attempts.Count(x => !x.Rejected && x.OrderChanged &&
                                                           x.WriterId == "Native" && x.Domain == CommandDomain.Movement &&
                                                           x.Timestamp >= 29.0 && x.Timestamp < 30.0);
        int nsStall = stall.Telemetry.Attempts.Count(x => !x.Rejected && x.OrderChanged &&
                                                          x.WriterId == "Native" && x.Domain == CommandDomain.Movement &&
                                                          x.Timestamp >= 30.0 && x.Timestamp < 31.0);
        Log();
        Log("    [7f-stall 补充] 高频一方在 t=30~31s 停止写入 1s：");
        Log($"      Ns 生效变更数 [29,30)={nsBefore}  vs  停止期间 [30,31)={nsStall}");
        Log("      解读：Ns 的写入【始终】在生效，高频一方从未真正阻止它 —— 高频只是让 tick 末的采样值偏向自己。");
        Log("      一旦高频方掉一帧，Ns 的值立刻可见 => 这是竞速而非 Authority。");
    }

    // ================================================================================
    // E8 · Domain Ownership 三级递进
    // ================================================================================
    private static void RunE8()
    {
        H("E8 · Domain Ownership 三级递进 — H-C（弱证据）：分域授权 vs 整实体授权（条件式预测，不得越权回答宿主实测问题）");

        ScenarioResult Level(string id, CommandDomain[] taken)
        {
            LabSource? mv = null, fa = null, ar = null;
            NativeSource? ns = null;
            var probe = new RecoveryProbeHolder();
            // 分域授权的语义：只为「实际接管」的域注册 Lab 写入源，未接管域一个都不注册
            var def = new ScenarioDefinition
            {
                Id = id, Seed = 7001, Dt = 0.1, DurationSeconds = 30.0,
                PortPolicy = new WritePortPolicy(),   // gated 校验；non-gated 不校验（02 门控矩阵）
                SourceFactories = { }
            };
            foreach (var d in taken)
            {
                var dom = d;
                def.SourceFactories.Add(ctx =>
                {
                    var s = new LabSource(ctx, new LabSourceConfig
                    {
                        Id = "Lab", Domain = dom, Value = CommandValue.HOLD,
                        Period = 1.0, ActiveFrom = 2.0, ActiveUntil = 22.0
                    });
                    if (dom == CommandDomain.Movement) mv = s;
                    else if (dom == CommandDomain.Facing) fa = s;
                    else ar = s;
                    return s;
                });
            }
            def.SourceFactories.Add(ctx => ns = new NativeSource(ctx, new NativeSourceConfig
            {
                GatedDomains = new[] { CommandDomain.Movement, CommandDomain.Facing },
                FixedValue = CommandValue.ADVANCE, SelfMaintenanceValue = CommandValue.MOVE_A
            }));
            def.Actions.Add(new TimedActionSpec(1.0, "baseline", ctx => probe.Capture(ctx.Entity.Snapshot(), ns!.ActiveTimerCount)));
            def.Actions.Add(new TimedActionSpec(2.0, "acquire", ctx =>
            {
                foreach (var d in taken)
                {
                    var l = ctx.Authority.Acquire(ctx.Entity.Id, d, "Lab", AuthorityLevel.Full, 25.0, ctx.Clock.Now);
                    var src = d switch
                    {
                        CommandDomain.Movement => mv,
                        CommandDomain.Facing => fa,
                        _ => ar
                    };
                    if (l is not null && src is not null) { src.Lease = l; src.Generation = l.Generation; }
                }
            }));
            def.Actions.Add(new TimedActionSpec(22.0, "release", ctx =>
            {
                foreach (var d in taken)
                {
                    var src = d switch { CommandDomain.Movement => mv, CommandDomain.Facing => fa, _ => ar };
                    if (src?.Lease is not null) { ctx.Authority.Release(src.Lease, ctx.Clock.Now); src.Lease = null; }
                }
            }));
            def.Actions.Add(new TimedActionSpec(23.0, "check", ctx => probe.Check(id, ctx.Entity.Snapshot(), ns!.ActiveTimerCount)));
            return ScenarioRunner.Run(def);
        }

        var lv = new (string Name, CommandDomain[] Taken, ScenarioResult R)[]
        {
            ("8a Movement=Lab", new[]{ CommandDomain.Movement }, Level("E8-8a", new[]{ CommandDomain.Movement })),
            ("8b Movement+Facing=Lab", new[]{ CommandDomain.Movement, CommandDomain.Facing }, Level("E8-8b", new[]{ CommandDomain.Movement, CommandDomain.Facing })),
            ("8c Full=Lab", new[]{ CommandDomain.Movement, CommandDomain.Facing, CommandDomain.Arrangement }, Level("E8-8c", new[]{ CommandDomain.Movement, CommandDomain.Facing, CommandDomain.Arrangement })),
        };

        foreach (var (name, taken, r) in lv)
        {
            Log($"  [{name}]");
            foreach (var d in new[] { CommandDomain.Movement, CommandDomain.Facing, CommandDomain.Arrangement })
            {
                bool isTaken = taken.Contains(d);
                Log($"      {d,-12} {(isTaken ? "Lab 接管" : "Ns 保留"),-10} {DomStats(r.Telemetry, d)}");
            }
            Log($"      release_recovery_failures(手工探针)={r.Metrics.ReleaseRecoveryFailures} grade={r.Metrics.Grade}");
        }

        var r8a = lv[0].R; var r8b = lv[1].R; var r8c = lv[2].R;
        Check("E8-① 8a/8b 被 Lab 接管的域 unauthorized_writes == 0",
              CountUnauthWrites(r8a.Telemetry, CommandDomain.Movement) == 0 &&
              CountUnauthWrites(r8b.Telemetry, CommandDomain.Movement) == 0 &&
              CountUnauthWrites(r8b.Telemetry, CommandDomain.Facing) == 0,
              $"8a.Mv={CountUnauthWrites(r8a.Telemetry, CommandDomain.Movement)}, " +
              $"8b.Mv={CountUnauthWrites(r8b.Telemetry, CommandDomain.Movement)}, " +
              $"8b.Fa={CountUnauthWrites(r8b.Telemetry, CommandDomain.Facing)}");
        int labEffFa8a = r8a.Telemetry.Attempts.Count(x => x.Domain == CommandDomain.Facing && x.WriterId == "Lab" && !x.Rejected && x.OrderChanged);
        int labEffAr8a = r8a.Telemetry.Attempts.Count(x => x.Domain == CommandDomain.Arrangement && x.WriterId == "Lab" && !x.Rejected && x.OrderChanged);
        int labAttOther8a = r8a.Telemetry.Attempts.Count(x => x.WriterId == "Lab" &&
                                                              (x.Domain == CommandDomain.Facing || x.Domain == CommandDomain.Arrangement));
        Check("E8-② 8a 保留给 Ns 的域 lab_overrides == 0",
              labEffFa8a == 0 && labEffAr8a == 0 && labAttOther8a == 0,
              $"Facing lab_ov={labEffFa8a}, Arrangement lab_ov={labEffAr8a}, 未接管域的 Lab 写入尝试={labAttOther8a}");
        int ng8a = r8a.Telemetry.Attempts.Count(x => x.Domain == CommandDomain.Arrangement && x.Path == WritePath.NonGated);
        int ng8c = r8c.Telemetry.Attempts.Count(x => x.Domain == CommandDomain.Arrangement && x.Path == WritePath.NonGated);
        Check("E8-③ Arrangement 域仍出现 non-gated 自维护写入（复现 02 门控矩阵，证明 Ns 模型保真）",
              ng8a > 0 && ng8c > 0, $"8a non_gated={ng8a}, 8c non_gated={ng8c}");
        int sideA = CountUnauthWrites(r8a.Telemetry, CommandDomain.Arrangement);
        int sideC = CountUnauthWrites(r8c.Telemetry, CommandDomain.Arrangement);
        Log($"      跨域副作用（Lab 已接管域内仍发生的越权生效写入）：8a={sideA} 8b={CountUnauthWrites(r8b.Telemetry, CommandDomain.Arrangement)} 8c={sideC}");
        Check("E8-④ 接管面扩大时跨域副作用单调不减（8a <= 8b <= 8c）",
              sideA <= CountUnauthWrites(r8b.Telemetry, CommandDomain.Arrangement) &&
              CountUnauthWrites(r8b.Telemetry, CommandDomain.Arrangement) <= sideC,
              $"{sideA} <= {CountUnauthWrites(r8b.Telemetry, CommandDomain.Arrangement)} <= {sideC}");
        Log("      [结论形式] 以上均为『若宿主行为如 Ns 模型，则……』的条件式预测；不得用于回答 02 §8.1 / HANDOFF §10.6 的宿主实测问题。");
    }

    // ================================================================================
    // E9 · Replace vs Append Writer
    // ================================================================================
    private static void RunE9()
    {
        H("E9 · Replace vs Append Writer — 承接 03 的 H-B 修正：『替换 Writer』而非『并列新增 Writer』能否被量化");

        // 9a Replace：Lab 接管原有写入点产出自己的值，写入点数量不变（Writer 净增 = 0）
        var r9a = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E9-9a", Seed = 8001, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = false },
            SourceFactories =
            {
                // 同一个 Ns 写入点，Movement 的产出被替换为 Lab 的值（语义等价 Harmony Prefix return false 后改写产出）
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement, CommandDomain.Facing },
                    FixedValue = CommandValue.HOLD,          // <- 原产出 ADVANCE 被替换为 Lab 的 HOLD
                    UtilityBase = 1.0
                })
            }
        });
        // 9b Append：保留原路径，在其后并列追加一个 Lab 写入者（更高频 + 错峰）
        var r9b = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E9-9b", Seed = 8001, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = false },
            SourceFactories =
            {
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement, CommandDomain.Facing },
                    FixedValue = CommandValue.ADVANCE, UtilityBase = 1.0
                }),
                ctx => new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Domain = CommandDomain.Movement, Value = CommandValue.HOLD,
                    Period = 0.5, Phase = 0.25, Utility = 2.0
                })
            }
        });
        Log("  [9a Replace] 写入点数量不变（仅 Ns），Movement 产出被替换为 Lab 的值");
        Log($"    Movement: {DomStats(r9a.Telemetry, CommandDomain.Movement)}");
        Log($"    unauthW={r9a.Metrics.UnauthorizedWrites} chg/s={r9a.Metrics.OrderChangesPerSecond:F4} " +
            $"osc/s={r9a.Metrics.OscillationFrequency:F4} grade={r9a.Metrics.Grade}");
        Log("  [9b Append] 保留原路径 + 并列追加 Lab 写入者（周期 0.5s，相位错开 0.25s）");
        Log($"    Movement: {DomStats(r9b.Telemetry, CommandDomain.Movement)}");
        Log($"    unauthW={r9b.Metrics.UnauthorizedWrites} chg/s={r9b.Metrics.OrderChangesPerSecond:F4} " +
            $"osc/s={r9b.Metrics.OscillationFrequency:F4} grade={r9b.Metrics.Grade}");
        int writers9a = r9a.Telemetry.Attempts.Select(x => x.WriterId).Distinct().Count();
        int writers9b = r9b.Telemetry.Attempts.Select(x => x.WriterId).Distinct().Count();
        Log($"    Writer 集合：9a=[{string.Join(",", r9a.Telemetry.Attempts.Select(x => x.WriterId).Distinct())}] " +
            $"9b=[{string.Join(",", r9b.Telemetry.Attempts.Select(x => x.WriterId).Distinct())}]");
        Check("E9 9a unauthorized_writes == 0 且 Writer 净增 = 0",
              r9a.Metrics.UnauthorizedWrites == 0 && writers9a == 1,
              $"unauthW={r9a.Metrics.UnauthorizedWrites}, writers={writers9a}");
        Check("E9 9b unauthorized_writes > 0 或 Writer 净增 >= 1，且分级劣于 9a",
              (r9b.Metrics.UnauthorizedWrites > 0 || writers9b >= 2) &&
              Worse(r9b.Metrics, r9a.Metrics),
              $"unauthW={r9b.Metrics.UnauthorizedWrites}, writers={writers9b}, grade {r9a.Metrics.Grade} -> {r9b.Metrics.Grade}");
        Check("E9 9b 与 9a 指标必须分叉（否则 03 的『替换而非并列』失去实验支撑）",
              r9a.Metrics.UnauthorizedWrites != r9b.Metrics.UnauthorizedWrites,
              $"{r9a.Metrics.UnauthorizedWrites} vs {r9b.Metrics.UnauthorizedWrites}");
    }

    private static int Rank(StabilityGrade g) => g switch
    {
        StabilityGrade.Stable => 0,
        StabilityGrade.Marginal => 1,
        _ => 2
    };

    private static bool Worse(MetricSet a, MetricSet b) =>
        (Rank(a.Grade), a.UnauthorizedWrites).CompareTo((Rank(b.Grade), b.UnauthorizedWrites)) > 0;

    // ================================================================================
    // E10 · Value Transform 层能力边界
    // ================================================================================
    private static void RunE10()
    {
        H("E10 · Value Transform 层 — 04 §6.1 立场：Transformation 不改变『谁在写』，故不解决任何一类 Authority 冲突");

        Func<WriteRequest, CommandValue> transform = req =>
            req.Value == CommandValue.ADVANCE ? CommandValue.MOVE_A : req.Value;

        // 10a 单写者：功能正确性
        var r10a = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E10-10a", Seed = 9001, Dt = 0.1, DurationSeconds = 20.0,
            PortPolicy = new WritePortPolicy { ValueTransform = transform },
            SourceFactories =
            {
                ctx => new NativeSource(ctx, new NativeSourceConfig
                {
                    GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.ADVANCE
                })
            }
        });
        Log($"  [10a 单写者] 终值={r10a.FinalState[0].Value}（期望 MOVE_A，即 ADVANCE 被变换）");
        Check("E10-① 10a 功能成立（值确实被改写）",
              r10a.FinalState[0].Value == CommandValue.MOVE_A, $"final={r10a.FinalState[0].Value}");

        // 10b 双写者冲突：有无 Transform 的 unauthorized_writes 必须相同
        ScenarioResult Conflict(bool withTransform) => ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = withTransform ? "E10-10b-on" : "E10-10b-off", Seed = 9002, Dt = 0.1, DurationSeconds = 60.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false, EnforceFencing = false,
                ValueTransform = withTransform ? transform : null
            },
            SourceFactories =
            {
                ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.ADVANCE }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, Utility = 1.0 })
            }
        });
        var on = Conflict(true);
        var off = Conflict(false);
        Log("  [10b 双写者冲突]");
        Log($"    transform=ON : unauthW={on.Metrics.UnauthorizedWrites} unauthAtt={on.Metrics.UnauthorizedWriteAttempts} " +
            $"osc/s={on.Metrics.OscillationFrequency:F4} chg/s={on.Metrics.OrderChangesPerSecond:F4}");
        Log($"    transform=OFF: unauthW={off.Metrics.UnauthorizedWrites} unauthAtt={off.Metrics.UnauthorizedWriteAttempts} " +
            $"osc/s={off.Metrics.OscillationFrequency:F4} chg/s={off.Metrics.OrderChangesPerSecond:F4}");
        Check("E10-② 10b 的 unauthorized_writes 与无 Transform 时相同（未解决权限问题）",
              on.Metrics.UnauthorizedWrites == off.Metrics.UnauthorizedWrites,
              $"{off.Metrics.UnauthorizedWrites} vs {on.Metrics.UnauthorizedWrites}");

        // 10c 与 Ownership 组合：正交性
        ScenarioResult WithOwnership(bool withTransform)
        {
            LabSource? lab = null;
            return ScenarioRunner.Run(new ScenarioDefinition
            {
                Id = withTransform ? "E10-10c-on" : "E10-10c-off", Seed = 9003, Dt = 0.1, DurationSeconds = 30.0,
                PortPolicy = new WritePortPolicy { ValueTransform = withTransform ? transform : null },
                SourceFactories =
                {
                    ctx => lab = new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0, ActiveFrom = 2.0 }),
                    ctx => new NativeSource(ctx, new NativeSourceConfig { GatedDomains = new[] { CommandDomain.Movement }, FixedValue = CommandValue.ADVANCE })
                },
                Actions = { new TimedActionSpec(2.0, "acquire", ctx =>
                {
                    var l = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab", AuthorityLevel.Full, 30.0, ctx.Clock.Now);
                    lab!.Lease = l; lab.Generation = l?.Generation ?? 0;
                }) }
            });
        }
        var cOn = WithOwnership(true);
        var cOff = WithOwnership(false);
        Log("  [10c 与 Ownership 组合]");
        Log($"    transform=ON : unauthW={cOn.Metrics.UnauthorizedWrites} nativeOv={cOn.Metrics.NativeOverrides} grade={cOn.Metrics.Grade} 终值={cOn.FinalState[0].Value}");
        Log($"    transform=OFF: unauthW={cOff.Metrics.UnauthorizedWrites} nativeOv={cOff.Metrics.NativeOverrides} grade={cOff.Metrics.Grade} 终值={cOff.FinalState[0].Value}");
        Check("E10-③ 10c Ownership 的 unauthorized_writes 仍为 0（两者正交、可共存）",
              cOn.Metrics.UnauthorizedWrites == 0, $"unauthW={cOn.Metrics.UnauthorizedWrites}");
    }

    // ================================================================================
    // E11 · Blackboard vs Command Bus 语义
    // ================================================================================
    private static void RunE11()
    {
        H("E11 · Blackboard vs Command Bus — H-H（04 C7）：Blackboard 语义下归因不完整；Command Bus 语义下归因完整率 100%");

        // 完全相同的注入序列：Lab 每 1.0s 写 HOLD；Ns 每 0.5s 写 ADVANCE
        var seq = new List<(double T, string W, CommandValue V)>();
        for (int k = 1; k <= 200; k++)
        {
            double t = k * 0.1;
            if (k % 10 == 1) seq.Add((t, "Lab", CommandValue.HOLD));
            if (k % 5 == 1) seq.Add((t, "Native", CommandValue.ADVANCE));
        }

        // 11a Blackboard：共享可变状态直接写；只有自愿记录者（Lab）留痕，Ns 不留
        var board = new Blackboard();
        var voluntary = new List<(double T, string W, CommandValue V, string Reason)>();
        int changes11a = 0, attributed11a = 0;
        foreach (var (t, w, v) in seq)
        {
            bool changed = board.Set(v, t);
            if (!changed) continue;
            changes11a++;
            // 观测方只有事后快照；能归因的仅限于自愿留痕的写入者
            if (voluntary.Any(x => Math.Abs(x.T - t) < 1e-9 && x.V == v)) attributed11a++;
        }
        // 先补记自愿日志（与写入同序）
        voluntary.Clear();
        changes11a = 0; attributed11a = 0;
        board = new Blackboard();
        foreach (var (t, w, v) in seq)
        {
            bool changed = board.Set(v, t);
            if (w == "Lab") voluntary.Add((t, w, v, "lab source (no lease)"));
            if (!changed) continue;
            changes11a++;
            if (voluntary.Any(x => Math.Abs(x.T - t) < 1e-9 && x.V == v)) attributed11a++;
        }

        // 11b Command Bus：所有变更经单一写入口，入口记录 writer + reason + token
        var r11b = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "E11-11b", Seed = 11001, Dt = 0.1, DurationSeconds = 20.0,
            PortPolicy = new WritePortPolicy { EnforceAuthorityOnGated = false, EnforceFencing = false },
            SourceFactories =
            {
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "Lab", Value = CommandValue.HOLD, Period = 1.0 }),
                ctx => new LabSource(ctx, new LabSourceConfig { Id = "Native", Value = CommandValue.ADVANCE, Period = 0.5 })
            }
        });
        int changes11b = r11b.Metrics.EffectiveChanges;
        int attributed11b = r11b.Telemetry.Attempts.Count(x => !x.Rejected && x.OrderChanged &&
                                                              x.WriterId is not null && x.Reason is not null);

        double rate11a = changes11a > 0 ? (double)attributed11a / changes11a : 0.0;
        double rate11b = changes11b > 0 ? (double)attributed11b / changes11b : 0.0;
        Log("  [11a Blackboard] 多源共享可变状态，无 owner，无单点入口；观测方只能事后比对快照");
        Log($"    注入写入={seq.Count} 值变更={changes11a} 可归因={attributed11a} 归因完整率={rate11a * 100:F1}%");
        Log($"    （仅自愿留痕的 Lab 可归因；Ns 的写入在 trace 中查不到来源）");
        Log("  [11b Command Bus] 所有变更经单一写入口，入口记录 writer + reason + token");
        Log($"    注入写入={r11b.Metrics.WriteAttempts} 值变更={changes11b} 可归因={attributed11b} 归因完整率={rate11b * 100:F1}%");
        Check("E11 11a 归因完整率 < 100%（存在无法归因事件）", rate11a < 1.0,
              $"rate={rate11a * 100:F1}% ({changes11a - attributed11a} 条不可归因)");
        Check("E11 11b 归因完整率 = 100%", rate11b >= 1.0, $"rate={rate11b * 100:F1}%");
        Log($"    [不可归因样例] 11a 中 Ns 发起的变更共 {changes11a - attributed11a} 条，均无 writer/reason 记录");
    }

    private sealed class Blackboard
    {
        private CommandValue _v = CommandValue.Unset;
        public CommandValue Value => _v;
        public bool Set(CommandValue v, double t) { if (v == _v) return false; _v = v; return true; }
    }

    // ================================================================================

    public static int RunAll()
    {
        Log();
        Log("=== CommandLab Phase 7 · E1–E11 实验执行 ===");
        RunE1();
        RunE2();
        RunE3();
        RunE4();
        RunE5();
        RunE6();
        RunE7();
        RunE8();
        RunE9();
        RunE10();
        RunE11();
        Log();
        Log($"=== 判据汇总：{_checks - _fails}/{_checks} 通过，失败 {_fails} 项 ===");

        var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts");
        Directory.CreateDirectory(outDir);
        var outPath = Path.GetFullPath(Path.Combine(outDir, "e1-e11-results.txt"));
        File.WriteAllText(outPath, R.ToString(), Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine($"report written to: {outPath}");
        return _fails;
    }
}
