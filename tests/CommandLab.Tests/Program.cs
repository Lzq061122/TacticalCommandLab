// CommandLab.Tests / Phase 6 装置门禁（D0）+ 施工自检
// 说明：本文件跑的是「装置是否可信」，不是 E1–E11 的实验结果（那是 Phase 7）。
// 运行：dotnet run --project tests/CommandLab.Tests

using System.Text;
using CommandLab.Core;
using CommandLab.Simulation;
using CommandLab.Telemetry;

namespace CommandLab.Tests;

public static class Program
{
    private static readonly StringBuilder Report = new();
    private static int _failures;
    private static int _checks;

    private static void Log(string line)
    {
        Report.AppendLine(line);
        Console.WriteLine(line);
    }

    private static void Check(string name, bool ok, string detail)
    {
        _checks++;
        if (!ok) _failures++;
        Log($"  [{(ok ? "PASS" : "FAIL")}] {name} :: {detail}");
    }

    public static int Main()
    {
        Log("=== CommandLab Phase 6 · D0 装置门禁 ===");
        Log("");

        // ---------- D0-S1: 双源无仲裁 LWW ----------
        ScenarioDefinition D0S1() => new()
        {
            Id = "D0-S1",
            Seed = 20260920,
            Dt = 0.1,
            DurationSeconds = 60.0,
            // LWW 基线：无 Ownership 概念（E1 语义）
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
                    Period = 0.7, Phase = 0.35, Priority = 1.0, Utility = 1.0
                })
            }
        };

        Log("[D0-1] 同一 scenario 跑 3 次，replay_hash 必须逐字节相同");
        var h1 = ScenarioRunner.Run(D0S1());
        var h2 = ScenarioRunner.Run(D0S1());
        var h3 = ScenarioRunner.Run(D0S1());
        Log($"    run1 hash = {h1.ReplayHash}");
        Log($"    run2 hash = {h2.ReplayHash}");
        Log($"    run3 hash = {h3.ReplayHash}");
        Check("replay determinism 3/3",
              h1.ReplayHash == h2.ReplayHash && h2.ReplayHash == h3.ReplayHash,
              $"attempts={h1.Telemetry.Attempts.Count}, sim={h1.SimulationSeconds:F1}s");
        Check("scenario 确有写入（装置不是空转）",
              h1.Metrics.EffectiveChanges > 0,
              $"effective_changes={h1.Metrics.EffectiveChanges}, order_changes_per_second={h1.Metrics.OrderChangesPerSecond:F4}");

        // ---------- D0-2: 置换注册序 ----------
        Log("");
        Log("[D0-2] 置换 Source 注册序 (A,B) -> (B,A)，记录本 scenario 是否对顺序敏感");
        var permuted = D0S1().WithSourceOrder(new[] { 1, 0 });
        var p1 = ScenarioRunner.Run(permuted);
        var p2 = ScenarioRunner.Run(permuted);
        Log($"    permuted hash = {p1.ReplayHash}");
        Log($"    permuted 重跑  = {p2.ReplayHash}");
        Check("置换后自身仍可复现", p1.ReplayHash == p2.ReplayHash, "permuted run 2/2 identical");
        bool orderSensitive = p1.ReplayHash != h1.ReplayHash;
        Log($"    ORDER_SENSITIVE = {orderSensitive}  (基础序终值={h1.FinalState[0].Value}, 置换序终值={p1.FinalState[0].Value})");
        Check("顺序敏感性已记录（E1 需要该性质被观测到）", orderSensitive,
              $"A,B->终值 {h1.FinalState[0].Value} vs B,A->终值 {p1.FinalState[0].Value}");

        // ---------- D0-3: 空跑 ----------
        Log("");
        Log("[D0-3] 空跑 60s 无写入：分母型指标必须有限，不得 NaN/Infinity");
        var empty = ScenarioRunner.Run(new ScenarioDefinition
        {
            Id = "D0-S3-empty",
            Seed = 7,
            DurationSeconds = 60.0
        });
        Check("无 NaN / Infinity", empty.Metrics.IsFinite(), $"grade={empty.Metrics.Grade}");
        Check("空跑零写入", empty.Metrics.WriteAttempts == 0, $"attempts={empty.Metrics.WriteAttempts}");
        foreach (var row in empty.Metrics.Rows()) Log("    " + row);

        // ---------- 施工自检（不是实验结果，仅证明机械可用） ----------
        Log("");
        Log("=== 施工自检（Phase 6 机械可用性，非 E1–E11 结论） ===");

        LabSource? lab = null;
        NativeSource? ns = null;
        var ownership = new ScenarioDefinition
        {
            Id = "P6-SELFCHECK-ownership",
            Seed = 11,
            DurationSeconds = 30.0,
            PortPolicy = new WritePortPolicy(),   // 默认：gated 校验归属，non-gated 不校验（02 门控矩阵）
            SourceFactories =
            {
                ctx => lab = new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Domain = CommandDomain.Movement, Value = CommandValue.HOLD,
                    Period = 1.0, Priority = 5.0, Utility = 5.0
                }),
                ctx => ns = new NativeSource(ctx, new NativeSourceConfig())
            },
            Actions =
            {
                new TimedActionSpec(5.0, "lab-acquire", ctx =>
                {
                    var lease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement,
                                                     "Lab", AuthorityLevel.Full, 20.0, ctx.Clock.Now);
                    lab!.Lease = lease;
                    lab.Generation = lease?.Generation ?? 0;
                })
            }
        };

        var own = ScenarioRunner.Run(ownership);
        foreach (var row in own.Metrics.Rows()) Log("    " + row);

        foreach (var w in own.Warnings) Log($"    [WARN] {w}");

        Log("    生效写入序列（前 12 条）：");
        foreach (var a in own.Telemetry.Attempts.Where(a => !a.Rejected && a.OrderChanged).Take(12))
            Log($"      t={a.Timestamp:F2} {a.Domain,-11} {a.WriterId,-8} owner={a.OwnerAtWrite,-8} {a.PrevValue} -> {a.NewValue} ({a.Path})");

        Log("    越权生效写入明细：");
        foreach (var a in own.Telemetry.Attempts.Where(a => !a.Rejected && a.OrderChanged &&
                                                            !string.Equals(a.WriterId, a.OwnerAtWrite, StringComparison.Ordinal)))
            Log($"      t={a.Timestamp:F2} {a.Domain,-11} writer={a.WriterId} owner={a.OwnerAtWrite} {a.PrevValue} -> {a.NewValue} ({a.Path})");

        Log("    被拒写入原因分布：");
        foreach (var g in own.Telemetry.Attempts.Where(a => a.Rejected)
                             .GroupBy(a => a.RejectReasonText ?? "-")
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
            Log($"      {g.Key,-18} x{g.Count()}");

        Check("Ownership 生效期间无越权生效写入", own.Metrics.UnauthorizedWrites == 0,
              $"unauthorized_writes={own.Metrics.UnauthorizedWrites}, attempts={own.Metrics.WriteAttempts}");
        Check("被拒写入都带 RejectReason（可归因）",
              own.Telemetry.Attempts.Where(a => a.Rejected).All(a => a.RejectReasonText is not null),
              $"rejected={own.Telemetry.Attempts.Count(a => a.Rejected)}");
        int gated = own.Telemetry.Attempts.Count(a => a.Path == WritePath.Gated);
        int nonGated = own.Telemetry.Attempts.Count(a => a.Path == WritePath.NonGated);
        Check("Ns 双写入路径均存在且独立（建模红线）", gated > 0 && nonGated > 0,
              $"gated_attempts={gated}, non_gated_attempts={nonGated}");

        var rejectedSample = own.Telemetry.Attempts.FirstOrDefault(a => a.Rejected);
        if (rejectedSample is not null)
        {
            Log("");
            Log("    DecisionTrace 样例（被拒写入）：");
            foreach (var line in TelemetryCollector.Trace(rejectedSample).Split('\n'))
                Log("      " + line.TrimEnd('\r'));
        }

        // ---------- 开关接线自检：Fencing 开关是否真的参与写入口判定 ----------
        // 这不是 E4 的结论，只是确认 05 §8 施工清单里的 Fencing 开关不是死代码。
        // 若两变体结果相同，Phase 7 的 E4 4d 会直接失去判别力。
        Log("");
        Log("[P6-SELFCHECK-fencing] 旧 Lease 持有者在被抢占后仍写入：开/关 Fencing 两个变体必须分叉");

        LabSource? staleLab = null;
        ScenarioDefinition StaleLeaseScenario(bool fencing) => new()
        {
            Id = fencing ? "P6-SELFCHECK-fencing-on" : "P6-SELFCHECK-fencing-off",
            Seed = 3,
            DurationSeconds = 12.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false,   // 隔离变量：只看 fencing 开关
                EnforceAuthorityOnNonGated = false,
                EnforceFencing = fencing
            },
            SourceFactories =
            {
                ctx => staleLab = new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Domain = CommandDomain.Movement, Value = CommandValue.HOLD, Period = 1.0
                })
            },
            Actions =
            {
                new TimedActionSpec(2.0, "lab-acquire", ctx =>
                {
                    // 绑定租约：之后 Lab 的每次写入都会携带 LeaseId/Token，这是 fencing 判定所需的输入
                    var lease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                      AuthorityLevel.Full, 10.0, ctx.Clock.Now);
                    staleLab!.Lease = lease;
                    staleLab.Generation = lease?.Generation ?? 0;
                }),
                new TimedActionSpec(4.0, "native-force-acquire", ctx =>
                    ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native",
                                          AuthorityLevel.Full, 10.0, ctx.Clock.Now, force: true))
            }
        };

        var fenceOn = ScenarioRunner.Run(StaleLeaseScenario(true));
        var fenceOff = ScenarioRunner.Run(StaleLeaseScenario(false));
        Log($"    fencing=on  : stale_command_execution={fenceOn.Metrics.StaleCommandExecutions}, rejected={fenceOn.Telemetry.Attempts.Count(a => a.Rejected)}");
        Log($"    fencing=off : stale_command_execution={fenceOff.Metrics.StaleCommandExecutions}, rejected={fenceOff.Telemetry.Attempts.Count(a => a.Rejected)}");
        Check("Fencing 开 -> 旧 token 写入被拦", fenceOn.Metrics.StaleCommandExecutions == 0,
              $"stale_command_execution={fenceOn.Metrics.StaleCommandExecutions}");
        Check("Fencing 关 -> 旧 token 写入漏过（漏洞可复现）", fenceOff.Metrics.StaleCommandExecutions >= 1,
              $"stale_command_execution={fenceOff.Metrics.StaleCommandExecutions}");
        Check("两个变体必须分叉（否则 E4 4d 无判别力）",
              fenceOn.Metrics.StaleCommandExecutions != fenceOff.Metrics.StaleCommandExecutions,
              $"{fenceOn.Metrics.StaleCommandExecutions} vs {fenceOff.Metrics.StaleCommandExecutions}");

        // ---------- 开关接线自检：在途命令（延迟队列）+ 代次 fencing ----------
        // 这不是 E6 的结论，只是确认 05 §E6 Failure Criteria 要求的"真实在途语义"确实存在。
        Log("");
        Log("[P6-SELFCHECK-inflight] 延迟投递命令在代次变更后抵达：开/关代次校验必须分叉");

        LabSource? inflightLab = null;
        ScenarioDefinition InflightScenario(bool fencing) => new()
        {
            Id = fencing ? "P6-SELFCHECK-inflight-on" : "P6-SELFCHECK-inflight-off",
            Seed = 5,
            DurationSeconds = 12.0,
            PortPolicy = new WritePortPolicy
            {
                EnforceAuthorityOnGated = false,
                EnforceAuthorityOnNonGated = false,
                EnforceFencing = fencing
            },
            SourceFactories =
            {
                ctx => inflightLab = new LabSource(ctx, new LabSourceConfig
                {
                    Id = "Lab", Domain = CommandDomain.Movement, Value = CommandValue.MOVE_A,
                    Period = 1.0, DeliveryDelay = 0.5
                })
            },
            Actions =
            {
                new TimedActionSpec(1.0, "lab-acquire", ctx =>
                {
                    var lease = ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Lab",
                                                      AuthorityLevel.Full, 10.0, ctx.Clock.Now);
                    inflightLab!.Lease = lease;
                    inflightLab.Generation = lease?.Generation ?? 0;
                }),
                new TimedActionSpec(4.0, "native-force-acquire", ctx =>
                    ctx.Authority.Acquire(ctx.Entity.Id, CommandDomain.Movement, "Native",
                                          AuthorityLevel.Full, 10.0, ctx.Clock.Now, force: true))
            }
        };

        var inflightOn = ScenarioRunner.Run(InflightScenario(true));
        var inflightOff = ScenarioRunner.Run(InflightScenario(false));
        Log($"    gen-check=on  : stale_command_execution={inflightOn.Metrics.StaleCommandExecutions}");
        Log($"    gen-check=off : stale_command_execution={inflightOff.Metrics.StaleCommandExecutions}");
        Check("在途命令确实跨越了代次变更（延迟队列有效）", inflightOff.Metrics.StaleCommandExecutions >= 1,
              $"off 变体 stale={inflightOff.Metrics.StaleCommandExecutions}");
        Check("代次校验开 -> 在途旧命令被拦", inflightOn.Metrics.StaleCommandExecutions == 0,
              $"on 变体 stale={inflightOn.Metrics.StaleCommandExecutions}");
        Check("两个变体必须分叉（否则 E6 6b/6c 无判别力）",
              inflightOn.Metrics.StaleCommandExecutions != inflightOff.Metrics.StaleCommandExecutions,
              $"{inflightOn.Metrics.StaleCommandExecutions} vs {inflightOff.Metrics.StaleCommandExecutions}");

        // ---------- 汇总 ----------
        Log("");
        Log($"=== 汇总：{_checks - _failures}/{_checks} 通过，失败 {_failures} 项 ===");
        Log($"D0-S1 replay_hash      = {h1.ReplayHash}");
        Log($"D0-S1-permuted hash    = {p1.ReplayHash}");
        Log($"D0-S3-empty hash       = {empty.ReplayHash}");
        Log($"P6-SELFCHECK hash      = {own.ReplayHash}");
        Log($"P6-FENCING-ON hash     = {fenceOn.ReplayHash}");
        Log($"P6-FENCING-OFF hash    = {fenceOff.ReplayHash}");
        Log($"P6-INFLIGHT-ON hash    = {inflightOn.ReplayHash}");
        Log($"P6-INFLIGHT-OFF hash   = {inflightOff.ReplayHash}");

        var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts");
        Directory.CreateDirectory(outDir);
        var outPath = Path.GetFullPath(Path.Combine(outDir, "d0-gate.txt"));
        File.WriteAllText(outPath, Report.ToString(), Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine($"report written to: {outPath}");

        if (_failures != 0) return 1;

        // D0 门禁通过后继续跑 Phase 7 的 E1–E11（结果写入 tests/artifacts/e1-e11-results.txt）
        int e1to11 = Experiments.RunAll();

        // 再跑 Phase 8 的 E12（Authority Level 谱系对照，补 H-I；结果写入 tests/artifacts/e12-level-spectrum.txt）
        int e12 = ArchitectureComparison.RunAll();

        return e1to11 == 0 && e12 == 0 ? 0 : 1;
    }
}
