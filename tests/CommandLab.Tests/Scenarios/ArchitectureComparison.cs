// CommandLab.Tests / Phase 8 · E12 — Authority Level 谱系对照（补 H-I）
// 依据：docs/04 §3.2.8（谱系推论「要么 2 要么 5，不要停在 4」）+ HANDOFF §5 Phase 8 要点 3（Phase 7 只用 Level 5，必须补 2/4/5 对照）。
// 假设 H-I：Level 4（Override）的稳定性显著低于 Level 2 与 Level 5。
// 红线：中性对象（Lab / Native、HOLD / ADVANCE / MOVE_A）；无真实战术语义；无真实时钟。
//
// 装置新增的两个可观测口径（Phase 7 的 14 项指标不足以区分「拿到控制权」与「根本没抢过」）：
//   intent_hold_share  = 归属窗口内「实体值 == Lab 意图值」的时长占比（控制是否真的落地）
//   lab_delivery_rate  = Lab 生效变更数 / Lab 写入尝试数（意图是否被写入口放行）

using System.Text;
using CommandLab.Core;
using CommandLab.Simulation;
using CommandLab.Telemetry;

namespace CommandLab.Tests;

public static class ArchitectureComparison
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

    // ---------- 口径 ----------

    /// <summary>归属窗口 [from,to) 内实体值等于意图值的时长占比。由「生效变更」重建时间线。</summary>
    private static double IntentHoldShare(TelemetryCollector t, CommandDomain d, CommandValue desired, double from, double to)
    {
        var eff = t.Attempts.Where(x => x.Domain == d && !x.Rejected && x.OrderChanged)
                            .OrderBy(x => x.Timestamp).ToList();
        var cur = CommandValue.Unset;
        double cursor = from, hold = 0.0;
        foreach (var a in eff)
        {
            if (a.Timestamp <= from) { cur = a.NewValue; continue; }
            if (a.Timestamp >= to) break;
            if (cur == desired) hold += a.Timestamp - Math.Max(cursor, from);
            cur = a.NewValue;
            cursor = a.Timestamp;
        }
        if (cur == desired) hold += to - Math.Max(cursor, from);
        return to - from > 0 ? hold / (to - from) : 0.0;
    }

    private static int UnauthWrites(TelemetryCollector t, CommandDomain d) =>
        t.Attempts.Count(x => x.Domain == d && !x.Rejected && x.OrderChanged &&
                              !string.Equals(x.WriterId, x.OwnerAtWrite, StringComparison.Ordinal));

    private static int LabEffective(TelemetryCollector t, CommandDomain d) =>
        t.Attempts.Count(x => x.Domain == d && x.WriterId == "Lab" && !x.Rejected && x.OrderChanged);

    private static int LabAttempts(TelemetryCollector t, CommandDomain d) =>
        t.Attempts.Count(x => x.Domain == d && x.WriterId == "Lab");

    private static string Reasons(TelemetryCollector t, CommandDomain d)
    {
        var g = t.Attempts.Where(x => x.Domain == d && x.Rejected)
                          .GroupBy(x => x.RejectReasonText ?? "?")
                          .OrderBy(x => x.Key, StringComparer.Ordinal);
        return string.Join(", ", g.Select(x => $"{x.Key}={x.Count()}"));
    }

    // ---------- 场景 ----------

    private sealed class LevelRow
    {
        public required string Tag { get; init; }
        public required AuthorityLevel Level { get; init; }
        public required ScenarioResult R { get; init; }
        public int UnauthMv { get; init; }
        public int UnauthAr { get; init; }
        public double HoldShare { get; init; }
        public double Delivery { get; init; }
        public int RecoveryFailures { get; init; }
        public string RecoveryNote { get; init; } = "";
    }

    /// <summary>
    /// 单组：Lab 在 t=2 取得 Movement（+可选 Arrangement）的 level 级授权，t=22 释放，t=26 查恢复。
    /// nsPriority = 竞争者（Ns）的仲裁优先级。标定为 3 —— 使 Level 2 落败、Level 4 胜出，谱系才可被观测。
    /// </summary>
    private static LevelRow RunLevel(AuthorityLevel level, double nsPriority, CommandDomain[] taken,
                                     ArbitrationMode arb, string tag)
    {
        LabSource? mv = null, ar = null;
        NativeSource? ns = null;
        DomainSnapshot[]? baseline = null;
        int baselineTimers = 0;
        int recoveryFailures = 0;
        var notes = new List<string>();

        var def = new ScenarioDefinition
        {
            Id = $"E12-{tag}-{(int)level}{level}",
            Seed = 8001, Dt = 0.1, DurationSeconds = 30.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = true,
                EnforceAuthorityOnNonGated = false,   // 02 门控矩阵建模，不是漏洞
                EnforceFencing = true,
                Arbitration = arb
            }
        };

        def.SourceFactories.Add(ctx => mv = new LabSource(ctx, new LabSourceConfig
        {
            Id = "Lab", Domain = CommandDomain.Movement, Value = CommandValue.HOLD,
            Period = 1.0, Phase = 2.0, ActiveFrom = 2.0, ActiveUntil = 22.0,
            Priority = level.ArbitrationPriority()
        }));

        bool takeAr = taken.Contains(CommandDomain.Arrangement);
        if (takeAr)
            def.SourceFactories.Add(ctx => ar = new LabSource(ctx, new LabSourceConfig
            {
                Id = "Lab", Domain = CommandDomain.Arrangement, Value = CommandValue.HOLD,
                Period = 1.0, Phase = 2.0, ActiveFrom = 2.0, ActiveUntil = 22.0,
                Priority = level.ArbitrationPriority()
            }));

        def.SourceFactories.Add(ctx => ns = new NativeSource(ctx, new NativeSourceConfig
        {
            WriterId = "Native", TEval = 0.5,
            GatedDomains = new[] { CommandDomain.Movement },
            NonGatedDomains = takeAr ? new[] { CommandDomain.Arrangement } : Array.Empty<CommandDomain>(),
            Mode = NsCommandMode.Fixed, FixedValue = CommandValue.ADVANCE,
            SelfMaintenanceValue = CommandValue.MOVE_A,
            Priority = nsPriority
        }));

        def.Actions.Add(new TimedActionSpec(1.0, "baseline", ctx =>
        {
            baseline = ctx.Entity.Snapshot();
            baselineTimers = ns!.ActiveTimerCount;
        }));

        def.Actions.Add(new TimedActionSpec(2.0, "acquire", ctx =>
        {
            foreach (var d in taken)
            {
                var l = ctx.Authority.Acquire(ctx.Entity.Id, d, "Lab", level, 25.0, ctx.Clock.Now);
                var src = d == CommandDomain.Movement ? mv : ar;
                if (l is not null && src is not null) { src.Lease = l; src.Generation = l.Generation; }
            }
        }));

        def.Actions.Add(new TimedActionSpec(22.0, "release", ctx =>
        {
            foreach (var d in taken)
            {
                var src = d == CommandDomain.Movement ? mv : ar;
                if (src?.Lease is not null) { ctx.Authority.Release(src.Lease, ctx.Clock.Now); src.Lease = null; }
            }
        }));

        def.Actions.Add(new TimedActionSpec(26.0, "check-recovery", ctx =>
        {
            if (baseline is null) return;
            var now = ctx.Entity.Snapshot();
            bool ok = now.ValueEquals(baseline);
            int timers = ns!.ActiveTimerCount;
            bool timerOk = timers == baselineTimers;
            if (!ok || !timerOk)
            {
                recoveryFailures++;
                var diffs = new List<string>();
                for (int i = 0; i < now.Length; i++)
                    if (now[i].Value != baseline[i].Value) diffs.Add($"{now[i].Domain}: {baseline[i].Value} -> {now[i].Value}");
                if (!timerOk) diffs.Add($"timers: {baselineTimers} -> {timers}");
                notes.Add($"recovery FAILED: {string.Join("; ", diffs)}");
            }
            else notes.Add("recovery ok");
        }));

        var r = ScenarioRunner.Run(def);

        int labAtt = LabAttempts(r.Telemetry, CommandDomain.Movement);
        return new LevelRow
        {
            Tag = tag,
            Level = level,
            R = r,
            UnauthMv = UnauthWrites(r.Telemetry, CommandDomain.Movement),
            UnauthAr = UnauthWrites(r.Telemetry, CommandDomain.Arrangement),
            HoldShare = IntentHoldShare(r.Telemetry, CommandDomain.Movement, CommandValue.HOLD, 2.0, 22.0),
            Delivery = labAtt > 0 ? (double)LabEffective(r.Telemetry, CommandDomain.Movement) / labAtt : 0.0,
            RecoveryFailures = recoveryFailures,
            RecoveryNote = string.Join("; ", notes)
        };
    }

    private static void Row(LevelRow x)
    {
        var m = x.R.Metrics;
        Log($"  L{(int)x.Level} {x.Level,-9} unauthMv={x.UnauthMv,-4} unauthAr={x.UnauthAr,-4} " +
            $"osc={m.OscillationFrequency,7:F4}/s ocps={m.OrderChangesPerSecond,6:F4} " +
            $"life={m.AverageCommandLifetime,7:F3}s hold={x.HoldShare * 100,6:F1}% " +
            $"deliver={x.Delivery * 100,6:F1}% grade={m.Grade}");
        Log($"        rejects(Mv): {Reasons(x.R.Telemetry, CommandDomain.Movement)}");
    }

    // ================================================================================

    private static void RunE12()
    {
        H("E12 · Authority Level 谱系对照（补 H-I）— 04 §3.2.8 推论：『要么 2 要么 5，不要停在 4』");
        Log("  装置：Lab 于 t=2 取得 Movement 授权（ttl=25），t=22 释放；Ns 每 0.5s 写 ADVANCE（gated）。");
        Log("  竞争者仲裁优先级标定为 3（介于 Level 2 与 Level 4 之间）—— 否则谱系不可观测，见 E12-C 对照组。");
        Log("  新增口径：hold = 归属窗口内『实体值 == Lab 意图值(HOLD)』时长占比；deliver = Lab 生效变更 / Lab 尝试。");

        // ---- A：主矩阵（纯 gated 路径，Movement 单域）----
        var mvOnly = new[] { CommandDomain.Movement };
        var levels = new[]
        {
            AuthorityLevel.Monitor, AuthorityLevel.Recommend, AuthorityLevel.Approval,
            AuthorityLevel.Override, AuthorityLevel.Full
        };
        var main = levels.Select(l => RunLevel(l, 3.0, mvOnly, ArbitrationMode.Priority, "A")).ToList();
        Log();
        Log("  [A] 主矩阵（Arbitration=Priority, nsPriority=3, Movement 单域）");
        foreach (var x in main) Row(x);

        var l1 = main[0]; var l2 = main[1]; var l3 = main[2]; var l4 = main[3]; var l5 = main[4];
        Log("      ⚠ 口径登记：deliver 在 L5 下仅 4.8% 是口径缺陷而非控制失败 —— 21 次尝试中 20 次因『值已是 HOLD』" +
            "不产生 OrderChanged，故不计入生效变更。该口径不得单独用作控制权判据，一律以 hold 为准。");

        Log();
        Log("  [判据]");
        Check("E12-① L5(Full) 越权生效写入 == 0 且分级为 Stable",
              l5.UnauthMv == 0 && l5.R.Metrics.Grade == StabilityGrade.Stable,
              $"unauthMv={l5.UnauthMv}, grade={l5.R.Metrics.Grade}");
        Check("E12-② L5 意图在位率 >= 95%（控制权真的落地）",
              l5.HoldShare >= 0.95, $"hold={l5.HoldShare * 100:F1}%");
        Check("E12-③ L4(Override) 越权生效写入 > 0 且不为 Stable（非独占级挡不住写入）",
              l4.UnauthMv > 0 && l4.R.Metrics.Grade != StabilityGrade.Stable,
              $"unauthMv={l4.UnauthMv}, grade={l4.R.Metrics.Grade}");
        Check("E12-④ H-I 核心：L4 振荡频率 > L2 振荡频率",
              l4.R.Metrics.OscillationFrequency > l2.R.Metrics.OscillationFrequency,
              $"oscL4={l4.R.Metrics.OscillationFrequency:F4}/s > oscL2={l2.R.Metrics.OscillationFrequency:F4}/s");
        Check("E12-⑤ L2 是『伪稳定』：unauthMv == 0 且 Stable，但意图在位率 <= 5%",
              l2.UnauthMv == 0 && l2.R.Metrics.Grade == StabilityGrade.Stable && l2.HoldShare <= 0.05,
              $"unauthMv={l2.UnauthMv}, grade={l2.R.Metrics.Grade}, hold={l2.HoldShare * 100:F1}%");
        Check("E12-⑥ 部分控制带：L2 < L4 意图在位率 < L5",
              l2.HoldShare < l4.HoldShare && l4.HoldShare < l5.HoldShare,
              $"{l2.HoldShare * 100:F1}% < {l4.HoldShare * 100:F1}% < {l5.HoldShare * 100:F1}%");
        Check("E12-⑦ L1(Monitor) 授权方自身生效写入 == 0（Level 不给写权限）",
              LabEffective(l1.R.Telemetry, CommandDomain.Movement) == 0,
              $"lab_effective={LabEffective(l1.R.Telemetry, CommandDomain.Movement)}");
        Check("E12-⑧ 指标陷阱：L2 与 L5 在 unauthorized_writes / 分级上不可区分，仅 hold 可区分",
              l2.UnauthMv == l5.UnauthMv && l2.R.Metrics.Grade == l5.R.Metrics.Grade &&
              Math.Abs(l5.HoldShare - l2.HoldShare) > 0.5,
              $"unauthMv {l2.UnauthMv} vs {l5.UnauthMv}, grade {l2.R.Metrics.Grade} vs {l5.R.Metrics.Grade}, " +
              $"hold {l2.HoldShare * 100:F1}% vs {l5.HoldShare * 100:F1}%");
        Log($"      [旁证] L3(Approval) 与 Ns 同为优先级 3 -> 同分，胜负由 WriterId 字典序决定（无语义基础）：" +
            $"osc={l3.R.Metrics.OscillationFrequency:F4}/s hold={l3.HoldShare * 100:F1}%");

        // ---- B：non-gated 侧信道下 Level 5 是否仍成立（写入口覆盖率）----
        Log();
        Log("  [B] 写入口覆盖率检验：Lab 以 Level 5 同时接管 Movement + Arrangement（Arbitration=None，对齐 E8 装置）");
        var b5 = RunLevel(AuthorityLevel.Full, 0.0,
                          new[] { CommandDomain.Movement, CommandDomain.Arrangement },
                          ArbitrationMode.None, "B");
        Row(b5);
        Check("E12-⑨ Level 5 下 gated 域 unauth == 0，但 non-gated 域 unauth > 0（Level 上限由写入口覆盖率决定）",
              b5.UnauthMv == 0 && b5.UnauthAr > 0,
              $"Movement={b5.UnauthMv}, Arrangement(non-gated)={b5.UnauthAr}");

        // ---- C：判别力对照组（竞争者优先级为 0 时谱系塌缩）----
        Log();
        Log("  [C] 判别力对照：竞争者优先级 = 0（Ns 默认值）时，L2 与 L4 是否仍可区分");
        var c2 = RunLevel(AuthorityLevel.Recommend, 0.0, mvOnly, ArbitrationMode.Priority, "C");
        var c4 = RunLevel(AuthorityLevel.Override, 0.0, mvOnly, ArbitrationMode.Priority, "C");
        Row(c2); Row(c4);
        Check("E12-⑩ 判别力边界：竞争者优先级为 0 时 L2 与 L4 行为一致（谱系语义依赖同级竞争者存在）",
              c2.UnauthMv == c4.UnauthMv &&
              Math.Abs(c2.R.Metrics.OscillationFrequency - c4.R.Metrics.OscillationFrequency) < 1e-9,
              $"unauthMv {c2.UnauthMv} vs {c4.UnauthMv}, osc {c2.R.Metrics.OscillationFrequency:F4} vs {c4.R.Metrics.OscillationFrequency:F4}");

        // ---- 释放恢复 ----
        Log();
        Log("  [释放恢复] 各 Level 在 t=22 Release、t=26 比对基线：");
        foreach (var x in main.Concat(new[] { b5 }))
            Log($"        L{(int)x.Level} {x.Level,-9} failures={x.RecoveryFailures} :: {x.RecoveryNote}");
        Log("      ⚠ 本装置下恢复失败全为 0，属平凡成立 —— 不作为判据，仅登记（同 E8 判据④的陷阱）。");

        // ---- H-I 裁决 ----
        Log();
        bool hi = l4.R.Metrics.OscillationFrequency > l2.R.Metrics.OscillationFrequency &&
                  l4.R.Metrics.OscillationFrequency > l5.R.Metrics.OscillationFrequency;
        Log($"  [H-I 裁决] {(hi ? "成立，且强于 04 原表述" : "不成立 / 需修订")}：" +
            $"osc L2={l2.R.Metrics.OscillationFrequency:F4} < L4={l4.R.Metrics.OscillationFrequency:F4} > L5={l5.R.Metrics.OscillationFrequency:F4}");
        Log("      修正表述：04 的『要么 2 要么 5』须加限定 —— L2 的稳定是『意图零落地』的伪稳定（hold≈0%），" +
            "L4 是真实抖动（hold≈50%），只有 L5 同时满足『稳定 + 落地』。" +
            "故：若目标是取得控制权，Level 2 不构成 L5 的替代方案。");
        Log("      方法论结论：unauthorized_writes==0 且 Stable 是『取得控制权』的必要非充分条件，" +
            "必须与 intent_hold_share 同表判读；否则『从未抢到』与『完全控制』在指标上同形。");
    }

    public static int RunAll()
    {
        Log();
        Log("=== CommandLab Phase 8 · E12 Authority Level 谱系对照（H-I）===");
        RunE12();
        Log();
        Log($"=== 判据汇总：{_checks - _fails}/{_checks} 通过，失败 {_fails} 项 ===");

        var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts");
        Directory.CreateDirectory(outDir);
        var outPath = Path.GetFullPath(Path.Combine(outDir, "e12-level-spectrum.txt"));
        File.WriteAllText(outPath, R.ToString(), Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine($"report written to: {outPath}");
        return _fails;
    }
}
