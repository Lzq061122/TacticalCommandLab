using CommandLab.Core;
using CommandLab.Telemetry;

namespace CommandLab.Tests;

public static class MeasurementTests
{
    public static void Run(Action<string, bool, string> check)
    {
        HoldWindowCases(check);
        ProjectionCases(check);
        CoverageCases(check);
        VerdictCases(check);
    }

    private static void HoldWindowCases(Action<string, bool, string> check)
    {
        var empty = new HoldMeter();
        check("hold empty share is zero", empty.Share() == 0.0, $"share={empty.Share()}");
        empty.Open(10.0, CommandValue.HOLD);
        empty.Close(15.0);
        check("zero-sample window adds no observed time", empty.CumulativeSeconds == 0.0 && empty.Samples == 0,
            $"seconds={empty.CumulativeSeconds}, samples={empty.Samples}");
        check("zero-sample window cannot pass hold target", !ExperimentVerdicts.HoldTargetMet(empty), $"share={empty.Share()}");

        var meter = new HoldMeter();
        meter.Open(0.0, CommandValue.HOLD);
        meter.Sample(1.0, CommandValue.HOLD);
        var firstWindowSamplesBeforeClose = meter.Samples;
        meter.Close(2.0);
        check("window tail extends last observed state to close", meter.CumulativeSeconds == 2.0 && meter.Share() == 1.0,
            $"seconds={meter.CumulativeSeconds}, share={meter.Share()}");
        check("close boundary integrates final segment", meter.Samples == firstWindowSamplesBeforeClose + 1,
            $"samples={meter.Samples}");

        var firstSamples = meter.Samples;
        var firstSeconds = meter.CumulativeSeconds;
        meter.Open(2.0, CommandValue.ADVANCE);
        meter.Close(8.0); // No samples in the new window: prior HOLD must not testify for ADVANCE.
        check("new unsampled window cannot reuse prior-window sample", meter.Samples == firstSamples && meter.CumulativeSeconds == firstSeconds,
            $"samples={meter.Samples}/{firstSamples}, seconds={meter.CumulativeSeconds}/{firstSeconds}");

        meter.Open(8.0, CommandValue.ADVANCE);
        meter.Sample(9.0, CommandValue.ADVANCE);
        meter.Close(10.0);
        check("multiple sampled windows accumulate their independent durations", meter.CumulativeSeconds == 4.0 && meter.Share() == 1.0,
            $"seconds={meter.CumulativeSeconds}, share={meter.Share()}");
        int priorSamples = meter.Samples;
        meter.Sample(20.0, CommandValue.HOLD); // Closed-window and time-crossing sample is ignored.
        check("closed and out-of-window samples are ignored", meter.Samples == priorSamples && meter.CumulativeSeconds == 4.0,
            $"samples={meter.Samples}, seconds={meter.CumulativeSeconds}");

        var edges = new HoldMeter();
        edges.Open(5.0, CommandValue.HOLD);
        edges.Sample(5.0, CommandValue.HOLD);
        edges.Sample(4.0, CommandValue.HOLD);
        edges.Sample(6.0, CommandValue.HOLD);
        edges.Sample(6.0, CommandValue.RETREAT);
        edges.Close(5.0);
        check("same-time, duplicate, backward samples and backward close do not inflate time",
            edges.Samples == 1 && edges.CumulativeSeconds == 1.0 && edges.Share() == 1.0,
            $"samples={edges.Samples}, seconds={edges.CumulativeSeconds}, share={edges.Share()}");
    }

    private static void ProjectionCases(Action<string, bool, string> check)
    {
        var projection = new HostOrderProjection();
        var expected = new[]
        {
            CommandValue.Obs0, CommandValue.Obs1, CommandValue.Obs2, CommandValue.Obs3,
            CommandValue.Obs4, CommandValue.Obs5, CommandValue.Obs6, CommandValue.Obs7,
            CommandValue.Obs8, CommandValue.Obs9, CommandValue.Obs10, CommandValue.Obs11,
            CommandValue.Obs12, CommandValue.Obs13, CommandValue.Obs14, CommandValue.Obs15
        };
        bool aligned = true;
        for (int i = 0; i < expected.Length; i++) aligned &= projection.Project("raw-" + i) == expected[i];
        check("16 raw orders map to Obs0..Obs15 in order", aligned, projection.Describe());
        var stable = projection.Project("raw-0") == CommandValue.Obs0;
        var wrapped = projection.Project("raw-16") == CommandValue.Obs0 && projection.WrapCount == 1;
        check("projection preserves known mappings and counts first wrap", stable && wrapped, projection.Describe());
    }

    private static void CoverageCases(Action<string, bool, string> check)
    {
        var tracker = new NativeOverrideTracker("Lab", "Native");
        tracker.Observe(Attempt("E1", CommandDomain.Facing, "Lab", 1.0, changed: true));
        tracker.Observe(Attempt("E1", CommandDomain.Movement, "Native", 2.0, changed: true));
        tracker.Observe(Attempt("E2", CommandDomain.Movement, "Native", 2.5, changed: true));
        check("accepted Lab change in another domain cannot create Movement coverage", tracker.AcceptedLabChanges == 1,
            $"accepted={tracker.AcceptedLabChanges}");
        check("other entity and domain events cannot create H1 coverage", tracker.FirstNativeOverrideLatency < 0.0,
            $"latency={tracker.FirstNativeOverrideLatency}");
        check("H1-a may pass on an accepted change while H1-c still fails without matched override",
            ExperimentVerdicts.H1AcceptedLabChanges(tracker.AcceptedLabChanges) &&
            !ExperimentVerdicts.H1ObservedNativeOverride(tracker.FirstNativeOverrideLatency),
            $"accepted={tracker.AcceptedLabChanges}, latency={tracker.FirstNativeOverrideLatency}");

        var invalid = new NativeOverrideTracker("Lab", "Native");
        invalid.Observe(Attempt("E2", CommandDomain.Movement, "Lab", 3.0, changed: true, rejected: true));
        invalid.Observe(Attempt("E2", CommandDomain.Movement, "Lab", 3.5, changed: false));
        invalid.Observe(Attempt("E2", CommandDomain.Movement, "Native", 4.0, changed: true));
        check("rejected and unchanged writes cannot establish either side of coverage",
            invalid.AcceptedLabChanges == 0 && invalid.FirstNativeOverrideLatency < 0.0,
            $"accepted={invalid.AcceptedLabChanges}, latency={invalid.FirstNativeOverrideLatency}");
        check("H1-a accepted-Lab predicate remains an independent gate",
            !ExperimentVerdicts.H1AcceptedLabChanges(invalid.AcceptedLabChanges), $"accepted={invalid.AcceptedLabChanges}");
        check("H1-c no-override predicate remains an independent gate",
            !ExperimentVerdicts.H1ObservedNativeOverride(invalid.FirstNativeOverrideLatency), $"latency={invalid.FirstNativeOverrideLatency}");

        var valid = new NativeOverrideTracker("Lab", "Native");
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Lab", 4.0, changed: true));
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Lab", 5.0, changed: true));
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Native", 5.0, changed: true, rejected: true));
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Native", 5.5, changed: false));
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Native", 6.0, changed: true));
        valid.Observe(Attempt("E1", CommandDomain.Movement, "Native", 7.0, changed: true));
        check("first effective same-entity same-domain native write uses latest Lab baseline once",
            valid.AcceptedLabChanges == 2 && Math.Abs(valid.FirstNativeOverrideLatency - 1.0) < 0.000001,
            $"accepted={valid.AcceptedLabChanges}, latency={valid.FirstNativeOverrideLatency}");
        check("H1-a accepted-Lab predicate passes independently", ExperimentVerdicts.H1AcceptedLabChanges(valid.AcceptedLabChanges),
            $"accepted={valid.AcceptedLabChanges}");
        check("H1-c observed-override predicate passes independently", ExperimentVerdicts.H1ObservedNativeOverride(valid.FirstNativeOverrideLatency),
            $"latency={valid.FirstNativeOverrideLatency}");

        var boundary = new NativeOverrideTracker("Lab", "Native");
        boundary.Observe(Attempt("E3", CommandDomain.Movement, "Lab", 5.0, changed: true));
        boundary.Observe(Attempt("E3", CommandDomain.Movement, "Native", 4.0, changed: true));
        boundary.Observe(Attempt("E3", CommandDomain.Movement, "Native", 5.0, changed: true));
        check("equal-time native override has zero latency and backward event is ignored",
            boundary.FirstNativeOverrideLatency == 0.0 && boundary.AcceptedLabChanges == 1,
            $"accepted={boundary.AcceptedLabChanges}, latency={boundary.FirstNativeOverrideLatency}");
    }

    private static void VerdictCases(Action<string, bool, string> check)
    {
        check("H2/H3 target absent cannot pass", !ExperimentVerdicts.HoldTargetMet(null), "no-window");
        var zero = new HoldMeter();
        zero.Open(1.0, CommandValue.HOLD);
        zero.Close(1.0);
        check("H2/H3 zero-duration target window cannot pass", !ExperimentVerdicts.HoldTargetMet(zero), $"share={zero.Share()}");
        var passing = new HoldMeter();
        passing.Open(0.0, CommandValue.HOLD);
        passing.Sample(1.0, CommandValue.HOLD);
        passing.Close(1.0);
        check("H2/H3 sampled target hold can pass", ExperimentVerdicts.HoldTargetMet(passing), $"share={passing.Share()}");

        check("REL zero recovery failures can pass", ExperimentVerdicts.ReleaseRecoveryPassed(0), "failures=0");
        check("REL recovery failure cannot pass", !ExperimentVerdicts.ReleaseRecoveryPassed(1), "failures=1");
    }

    private static WriteAttempt Attempt(string entity, CommandDomain domain, string writer, double time,
                                        bool changed, bool rejected = false) => new()
    {
        Timestamp = time,
        Tick = 0,
        EntityId = entity,
        Domain = domain,
        Path = WritePath.Gated,
        WriterId = writer,
        OwnerAtWrite = "Native",
        AuthorityLevel = AuthorityLevel.Full,
        LeaseToken = 0,
        MaxSeenToken = 0,
        Generation = 0,
        RecordGeneration = 0,
        LeaseExpiryAt = double.NegativeInfinity,
        Command = CommandValue.HOLD,
        PrevValue = CommandValue.RETREAT,
        NewValue = changed && !rejected ? CommandValue.HOLD : CommandValue.RETREAT,
        OriginalMethodExecuted = !rejected,
        OrderChanged = changed,
        Rejected = rejected
    };
}
