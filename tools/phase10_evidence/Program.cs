using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using CommandLab.Core;
using CommandLab.Telemetry;

// Accept explicit input paths so the preserved source copy can be rerun from repository root.
var repo = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(repo, "src/CommandLab.Telemetry/Metrics.cs")))
    repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var run05 = args.Length > 0 ? args[0] : Path.Combine(repo, "_audit/_local-phase10-20261006/run05-h2-suppression/telemetry-20261006-215546-h2-suppression.txt");
var run06 = args.Length > 1 ? args[1] : Path.Combine(repo, "_audit/_local-phase10-20261006/run06-h2-coverage/telemetry-20261006-215736-h2-coverage.txt");
var outDir = args.Length > 2 ? args[2] : Path.Combine(repo, "_audit/_phase10-closure-20261007");
Directory.CreateDirectory(outDir);

var result = new[] { Analyze("run05", run05, outDir), Analyze("run06", run06, outDir) };
File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result.Select(r => r.Global), new JsonSerializerOptions { WriteIndented = true }));

static RunResult Analyze(string name, string path, string outDir)
{
    var lines = File.ReadAllLines(path);
    var telemetry = new TelemetryCollector();
    var lineOfAttempt = new Dictionary<WriteAttempt, int>();
    var eventLine = new List<int>();
    var auth = new List<AuthorityEvent>();
    var parsed = new List<AttemptRow>();
    for (var i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        if (line.StartsWith("t=", StringComparison.Ordinal))
        {
            var f = Fields(line);
            var a = new WriteAttempt
            {
                Timestamp = D(f["t"]), Tick = L(f["tick"]), EntityId = f["E"], Domain = E<CommandDomain>(f["D"]),
                Path = E<WritePath>(f["P"]), WriterId = f["W"], OwnerAtWrite = f["O"], AuthorityLevel = (AuthorityLevel)int.Parse(f["L"], CultureInfo.InvariantCulture),
                LeaseId = Null(f["lease"]), LeaseToken = L(f["tok"]), MaxSeenToken = L(f["max"]), Generation = L(f["gen"]), RecordGeneration = L(f["rgen"]),
                LeaseExpiryAt = D(f["exp"]), Command = E<CommandValue>(f["cmd"]), PrevValue = E<CommandValue>(f["prev"]), NewValue = E<CommandValue>(f["new"]),
                OriginalMethodExecuted = B(f["ome"]), OrderChanged = B(f["chg"]), Rejected = B(f["rej"]), RejectReasonText = Null(f["why"]),
                TaskId = Null(f["task"]), TaskState = Null(f["ts"]), IntentId = Null(f["int"]), Utility = D(f["u"]), Priority = D(f["p"])
            };
            telemetry.OnWriteAttempt(a); lineOfAttempt[a] = i + 1;
            parsed.Add(new AttemptRow(i + 1, a));
        }
        else if (line.StartsWith("AUTH|", StringComparison.Ordinal))
        {
            var f = Fields(line);
            var e = new AuthorityEvent { Timestamp = D(f["t"]), Kind = E<AuthorityEventKind>(f["kind"]), EntityId = f["E"], Domain = E<CommandDomain>(f["D"]),
                OwnerId = Null(f["own"]), PreviousOwnerId = Null(f["prev"]), Level = f.TryGetValue("L", out var lv) ? (AuthorityLevel)int.Parse(lv, CultureInfo.InvariantCulture) : 0,
                LeaseId = Null(f.GetValueOrDefault("lease", "-")), Token = L(f.GetValueOrDefault("tok", "0")), Generation = L(f.GetValueOrDefault("gen", "0")), Reason = Null(f.GetValueOrDefault("why", "-")) };
            telemetry.OnAuthorityEvent(e); auth.Add(e); eventLine.Add(i + 1);
        }
        else if (line.StartsWith("TASK|", StringComparison.Ordinal))
        {
            var f = Fields(line);
            telemetry.OnTaskEvent(new TaskEvent { Timestamp = D(f["t"]), TaskId = f["id"], From = E<TaskState>(f["from"]), To = E<TaskState>(f["to"]), Reason = Null(f.GetValueOrDefault("why", "-")) });
        }
    }

    var reported = ReadReportedMetrics(Path.Combine(Path.GetDirectoryName(path)!, name == "run05" ? "metrics-20261006-215546-h2-suppression.txt" : "metrics-20261006-215736-h2-coverage.txt"));
    var metrics = MetricsCalculator.Compute(telemetry, 33.0, config: new MetricsConfig { TEval = 0.5 });
    var groups = parsed.Select(x => x).Where(x => !x.A.Rejected && x.A.OrderChanged)
        .GroupBy(x => (x.A.EntityId, x.A.Domain)).OrderBy(g => g.Key.EntityId, StringComparer.Ordinal).ThenBy(g => g.Key.Domain)
        .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Line).ToList());

    var acquire = auth.Where(e => e.Kind == AuthorityEventKind.Acquire).OrderBy(e => e.Timestamp).First();
    var firstHold = parsed.Where(x => !x.A.Rejected && x.A.WriterId == "Lab" && x.A.NewValue == CommandValue.HOLD && x.A.Timestamp >= acquire.Timestamp)
        .OrderBy(x => x.A.Timestamp).ThenBy(x => x.Line).First();
    var release = auth.Where(e => e.Kind is AuthorityEventKind.Release or AuthorityEventKind.Expire).OrderBy(e => e.Timestamp).FirstOrDefault();
    var dumpRange = InferDump(reported, parsed, groups);
    var windows = new List<Window>
    {
        new("pre_acquire", 0, acquire.Timestamp), new("acquire_to_first_accepted_lab_hold", acquire.Timestamp, firstHold.A.Timestamp),
        new("first_lab_hold_to_release", firstHold.A.Timestamp, release?.Timestamp ?? dumpRange.Center),
        new("release_to_dump", release?.Timestamp ?? dumpRange.Center, dumpRange.Center)
    };
    var changesByWindow = windows.ToDictionary(w => w.Name, _ => 0);
    var pairsByWindow = windows.ToDictionary(w => w.Name, _ => 0);
    var lifetimeSamples = new List<LifetimeRow>();
    var keyRows = new List<KeyRow>();
    foreach (var (key, rows) in groups)
    {
        var seq = rows.Select(x => x.A.NewValue).ToList();
        var triples = new List<TripleRow>();
        for (int i = 2; i < rows.Count; i++)
            if (seq[i] == seq[i - 2] && seq[i - 1] != seq[i])
            {
                var windowsTouched = new[] { InWindow(windows, rows[i - 2].A.Timestamp), InWindow(windows, rows[i - 1].A.Timestamp), InWindow(windows, rows[i].A.Timestamp) }.Distinct().ToArray();
                var row = new TripleRow(rows[i - 2].Line, rows[i - 2].A.Timestamp, seq[i - 2].ToString(), rows[i - 1].Line, rows[i - 1].A.Timestamp, seq[i - 1].ToString(), rows[i].Line, rows[i].A.Timestamp, seq[i].ToString(), InWindow(windows, rows[i].A.Timestamp), windowsTouched.Length > 1, windowsTouched);
                triples.Add(row); pairsByWindow[row.AttributedWindow]++;
            }
        foreach (var r in rows) changesByWindow[InWindow(windows, r.A.Timestamp)]++;
        var samples = new List<LifetimeRow>();
        for (int i = 1; i < rows.Count; i++)
            samples.Add(MakeLife(key.EntityId, key.Domain.ToString(), "completed", rows[i - 1], rows[i], null, dumpRange, windows));
        if (rows.Count == 1)
            samples.Add(MakeLife(key.EntityId, key.Domain.ToString(), "single_change_right_censored", rows[0], null, dumpRange.Center, dumpRange, windows));
        lifetimeSamples.AddRange(samples);
        keyRows.Add(new KeyRow(key.EntityId, key.Domain.ToString(), rows.Count,
            rows.Select(x => new ChangeRow(x.Line, x.A.Timestamp, x.A.PrevValue.ToString(), x.A.NewValue.ToString(), x.A.WriterId)).ToList(),
            rows.Count > 1 ? rows[^1].Line : null, triples, samples));
    }

    var windowRows = windows.Select(w => new WindowRow(w.Name, w.Start, w.End, changesByWindow[w.Name], pairsByWindow[w.Name],
        lifetimeSamples.Count(s => InWindow(windows, s.EndT ?? s.DumpT!.Value) == w.Name),
        lifetimeSamples.Sum(s => s.Overlap.GetValueOrDefault(w.Name)))).ToList();
    var computed = MetricObject(metrics);
    var inferredMetricSet = MetricsCalculator.Compute(telemetry, dumpRange.Center, config: new MetricsConfig { TEval = 0.5 });
    var inferredMetrics = MetricObject(inferredMetricSet);
    var expectedLifeCount = groups.Values.Sum(g => Math.Max(0, g.Count - 1) + (g.Count == 1 ? 1 : 0));
    var completedLifeCount = groups.Values.Sum(g => Math.Max(0, g.Count - 1));
    var censoredLifeCount = groups.Values.Count(g => g.Count == 1);
    var maxMeanErrorFromPrintedTimestampRounding = (completedLifeCount * 0.0001 + censoredLifeCount * 0.00005) / Math.Max(1, expectedLifeCount);
    var conservation = new
    {
        perKeyChanges = keyRows.Sum(k => k.Changes), globalEffectiveChanges = metrics.EffectiveChanges,
        changesMatch = keyRows.Sum(k => k.Changes) == metrics.EffectiveChanges,
        perKeyPairs = keyRows.Sum(k => k.Pairs.Count), globalPairs = metrics.OscillationPairs,
        pairsMatch = keyRows.Sum(k => k.Pairs.Count) == metrics.OscillationPairs,
        lifetimeSamples = lifetimeSamples.Count, lifetimeSamplesFromProductionRule = expectedLifeCount,
        lifetimeCountMatchesRule = lifetimeSamples.Count == expectedLifeCount,
        perKeyLifetimeSamples = keyRows.Sum(k => k.LifetimeSamples.Count),
        perKeyLifetimeSamplesMatchGlobal = keyRows.Sum(k => k.LifetimeSamples.Count) == lifetimeSamples.Count,
        windowChanges = changesByWindow.Values.Sum(), windowPairs = pairsByWindow.Values.Sum(),
        windowChangesMatchGlobal = changesByWindow.Values.Sum() == metrics.EffectiveChanges,
        windowPairsMatchGlobal = pairsByWindow.Values.Sum() == metrics.OscillationPairs,
        lifetimeOverlapSeconds = windowRows.Sum(w => w.LifetimeOverlapSeconds),
        lifetimeSampleSeconds = lifetimeSamples.Sum(s => s.Duration),
        lifetimeOverlapsMatchSamples = Math.Abs(windowRows.Sum(w => w.LifetimeOverlapSeconds) - lifetimeSamples.Sum(s => s.Duration)) < 1e-8,
        lifetimeAvgAtInferredDumpCenterUsingPrintedTimestamps = inferredMetricSet.AverageCommandLifetime,
        reportedLifetimeAvg = double.Parse(reported["average_command_lifetime"], CultureInfo.InvariantCulture),
        maxMeanErrorFromEachSourceTimestampRoundedToF4 = maxMeanErrorFromPrintedTimestampRounding,
        excludedRejectedAttempts = parsed.Count(x => x.A.Rejected),
        excludedAcceptedUnchangedAttempts = parsed.Count(x => !x.A.Rejected && !x.A.OrderChanged)
    };
    var inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    var summary = new GlobalSummary(name, Path.GetFullPath(path), inputSha256, parsed.Count, parsed.Count(x => x.A.Rejected), parsed.Count(x => !x.A.Rejected && x.A.OrderChanged), groups.Count,
        keyRows.Sum(k => k.Pairs.Count), lifetimeSamples.Count, computed, inferredMetrics, reported, dumpRange, acquire.Timestamp, firstHold.A.Timestamp, firstHold.Line, release?.Timestamp, release is null,
        changesByWindow.Values.Sum(), pairsByWindow.Values.Sum(), windows.Select(w => new { w.Name, changes = changesByWindow[w.Name], pairs = pairsByWindow[w.Name] }).ToArray(),
        conservation, reported.GetValueOrDefault("stability_grade", "?"));
    var output = new RunResult(summary, keyRows, windowRows, parsed.Where(x => x.A.Rejected || !x.A.OrderChanged).Select(x => new ExcludedRow(x.Line, x.A.Timestamp, x.A.EntityId, x.A.Domain.ToString(), x.A.WriterId, x.A.NewValue.ToString(), x.A.Rejected, x.A.OrderChanged, x.A.RejectReasonText)).ToList());
    File.WriteAllText(Path.Combine(outDir, name + "-diagnostics.json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    WriteCsv(Path.Combine(outDir, name + "-keys.csv"), ["entity", "domain", "accepted_effective_changes", "aba_pairs", "lifetime_samples", "excluded_multi_change_tail_row"],
        keyRows.Select(k => new[] { k.Entity, k.Domain, k.Changes.ToString(CultureInfo.InvariantCulture), k.Pairs.Count.ToString(CultureInfo.InvariantCulture), k.LifetimeSamples.Count.ToString(CultureInfo.InvariantCulture), k.ExcludedMultiChangeTailRow?.ToString(CultureInfo.InvariantCulture) ?? "" }));
    WriteCsv(Path.Combine(outDir, name + "-changes.csv"), ["entity", "domain", "source_row", "t_f4", "prev_context", "new_value", "writer"],
        keyRows.SelectMany(k => k.AcceptedEffectiveChanges.Select(c => new[] { k.Entity, k.Domain, c.Row.ToString(CultureInfo.InvariantCulture), N(c.T), c.Prev, c.New, c.Writer })));
    WriteCsv(Path.Combine(outDir, name + "-triples.csv"), ["entity", "domain", "row_a", "t_a_f4", "a", "row_b", "t_b_f4", "b", "row_c", "t_c_f4", "c", "third_event_window", "crosses_window_boundary", "windows_touched"],
        keyRows.SelectMany(k => k.Pairs.Select(p => new[] { k.Entity, k.Domain, p.Row1.ToString(CultureInfo.InvariantCulture), N(p.T1), p.A, p.Row2.ToString(CultureInfo.InvariantCulture), N(p.T2), p.B, p.Row3.ToString(CultureInfo.InvariantCulture), N(p.T3), p.C, p.AttributedWindow, p.CrossesWindowBoundary.ToString(), string.Join(";", p.WindowsTouched) })));
    WriteCsv(Path.Combine(outDir, name + "-lifetimes.csv"), ["entity", "domain", "sample_kind", "start_row", "start_t_f4", "end_row", "end_t_f4", "dump_low_inferred", "dump_high_inferred", "duration_center", "duration_low", "duration_high", "pre_acquire_overlap", "acquire_hold_gap_overlap", "hold_release_overlap", "release_dump_overlap", "end_window"],
        keyRows.SelectMany(k => k.LifetimeSamples.Select(s => new[] { k.Entity, k.Domain, s.Kind, s.StartRow.ToString(CultureInfo.InvariantCulture), N(s.StartT), s.EndRow?.ToString(CultureInfo.InvariantCulture) ?? "", s.EndT is null ? "" : N(s.EndT.Value), s.DumpLow is null ? "" : N(s.DumpLow.Value), s.DumpHigh is null ? "" : N(s.DumpHigh.Value), N(s.Duration), N(s.DurationLow), N(s.DurationHigh), N(s.Overlap["pre_acquire"]), N(s.Overlap["acquire_to_first_accepted_lab_hold"]), N(s.Overlap["first_lab_hold_to_release"]), N(s.Overlap["release_to_dump"]), s.EndWindow ?? "dump_right_censor" })));
    return output;
}

static LifetimeRow MakeLife(string entity, string domain, string kind, AttemptRow start, AttemptRow? end, double? dump, DumpRange dumpRange, List<Window> windows)
{
    var a = start.A.Timestamp; var b = end?.A.Timestamp ?? dump!.Value;
    var overlap = windows.ToDictionary(w => w.Name, w => Math.Max(0.0, Math.Min(b, w.End) - Math.Max(a, w.Start)));
    return new LifetimeRow(entity, domain, kind, start.Line, start.A.Timestamp, end?.Line, end?.A.Timestamp, end is null ? dumpRange.Low : null, end is null ? dumpRange.High : null, dump, b - a,
        end is null ? Math.Max(0, dumpRange.Low - a) : b - a, end is null ? Math.Max(0, dumpRange.High - a) : b - a,
        end is null ? null : InWindow(windows, b), overlap);
}

static DumpRange InferDump(Dictionary<string, string> m, List<AttemptRow> rows, Dictionary<(string EntityId, CommandDomain Domain), List<AttemptRow>> groups)
{
    var constraints = new List<(string name, double lo, double hi)> { ("simulation_seconds F2", 32.995, 33.005) };
    AddRatio("commands_per_second", rows.Count, "commands_per_second");
    AddRatio("order_changes_per_second", rows.Count(x => !x.A.Rejected && x.A.OrderChanged), "order_changes_per_second");
    var pairs = groups.Values.Sum(g => { var v = g.Select(x => x.A.NewValue).ToList(); return Enumerable.Range(2, Math.Max(0, v.Count - 2)).Count(i => v[i] == v[i - 2] && v[i - 1] != v[i]); });
    AddRatio("oscillation_frequency", pairs, "oscillation_frequency");
    var lo = constraints.Max(x => x.lo); var hi = constraints.Min(x => x.hi);
    void AddRatio(string key, int count, string metric)
    {
        var r = double.Parse(m[metric], CultureInfo.InvariantCulture);
        constraints.Add((metric + " F4", count / (r + .00005), count / (r - .00005)));
    }
    return new DumpRange(lo, hi, (lo + hi) / 2, constraints.Select(x => new Constraint(x.name, x.lo, x.hi)).ToList());
}

static string InWindow(List<Window> ws, double t) => ws.FirstOrDefault(w => t >= w.Start && t < w.End)?.Name ?? (t >= ws[^1].End ? "after_dump" : "before_start");
static Dictionary<string, string> Fields(string line)
{
    var parts = line.Split('|'); var f = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var p in parts)
    {
        var ix = p.IndexOf('='); if (ix > 0) f[p[..ix]] = p[(ix + 1)..];
    }
    if (parts[0] == "AUTH") { var ix = parts[1].IndexOf('='); f[parts[1][..ix]] = parts[1][(ix + 1)..]; f["kind"] = parts[2]; }
    else if (parts[0] == "TASK") { var ix = parts[1].IndexOf('='); f[parts[1][..ix]] = parts[1][(ix + 1)..]; f["id"] = parts[2]; f["from"] = parts[3].Split("->")[0]; f["to"] = parts[3].Split("->")[1]; }
    return f;
}
static Dictionary<string, string> ReadReportedMetrics(string path)
{
    var inMetrics = false;
    return File.ReadAllLines(path).Where(line =>
    {
        if (line.Trim() == "[metrics]") { inMetrics = true; return false; }
        if (line.StartsWith("[", StringComparison.Ordinal)) inMetrics = false;
        return inMetrics && line.Contains('=');
    }).Select(x => x.Split(new[] { '=' }, 2)).ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.Ordinal);
}
static string N(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
static void WriteCsv(string path, string[] header, IEnumerable<string[]> rows)
{
    static string Q(string s) => s.IndexOfAny([',', '"', '\r', '\n']) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
    using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
    writer.WriteLine(string.Join(",", header.Select(Q)));
    foreach (var row in rows) writer.WriteLine(string.Join(",", row.Select(Q)));
}
static string? Null(string s) => s is "-" or "" ? null : s;
static bool B(string s) => s == "1";
static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
static long L(string s) => long.Parse(s, CultureInfo.InvariantCulture);
static T E<T>(string s) where T : struct, Enum => Enum.Parse<T>(s, false);
static object MetricObject(MetricSet m) => new { m.SimulationSeconds, m.WriteAttempts, m.CommandsPerSecond, m.EffectiveChanges, m.OrderChangesPerSecond, m.UnauthorizedWriteAttempts, m.UnauthorizedWrites, m.NativeOverrides, m.LabOverrides, m.AuthorityTransfers, m.Preemptions, m.AverageCommandLifetime, m.OscillationPairs, m.OscillationFrequency, m.StaleCommandExecutions, m.TaskStarvation, m.MaxPendingSeconds, grade = m.Grade.ToString(), thresholds = new { stableUnauthorizedWritesMax = 0, stableOscillationFrequencyMax = 0.05, stableOrderChangesPerSecondMax = 0.2, oscillatingFrequencyMin = 0.5, oscillatingLifetimeLessThanSeconds = 1.0 }, stableChecks = new { unauthorizedWritesZero = m.UnauthorizedWrites == 0, oscFrequencyAtMost005 = m.OscillationFrequency <= 0.05, changesPerSecondAtMost02 = m.OrderChangesPerSecond <= 0.2 }, oscillationChecks = new { frequencyAtLeast05 = m.OscillationFrequency >= 0.5, lifetimeBelow1Second = m.EffectiveChanges > 0 && m.AverageCommandLifetime < 1.0 }, triggers = new[] { m.OscillationFrequency >= 0.5 ? "oscillation_frequency>=0.5" : null, m.EffectiveChanges > 0 && m.AverageCommandLifetime < 1.0 ? "average_command_lifetime<1.0" : null }.Where(x => x is not null) };

record AttemptRow(int Line, WriteAttempt A);
record Window(string Name, double Start, double End);
record ChangeRow(int Row, double T, string Prev, string New, string Writer);
record TripleRow(int Row1, double T1, string A, int Row2, double T2, string B, int Row3, double T3, string C, string AttributedWindow, bool CrossesWindowBoundary, string[] WindowsTouched);
record LifetimeRow(string Entity, string Domain, string Kind, int StartRow, double StartT, int? EndRow, double? EndT, double? DumpLow, double? DumpHigh, double? DumpT, double Duration, double DurationLow, double DurationHigh, string? EndWindow, Dictionary<string, double> Overlap);
record KeyRow(string Entity, string Domain, int Changes, List<ChangeRow> AcceptedEffectiveChanges, int? ExcludedMultiChangeTailRow, List<TripleRow> Pairs, List<LifetimeRow> LifetimeSamples);
record WindowRow(string Name, double Start, double End, int Changes, int Pairs, int LifetimeSamplesEndingHere, double LifetimeOverlapSeconds);
record ExcludedRow(int Line, double T, string Entity, string Domain, string Writer, string Value, bool Rejected, bool OrderChanged, string? RejectReason);
record Constraint(string Name, double Low, double High);
record DumpRange(double Low, double High, double Center, List<Constraint> Constraints);
record GlobalSummary(string Run, string Input, string InputSha256, int Attempts, int RejectedAttempts, int AcceptedEffectiveChanges, int KeysWithChanges, int OscillationPairs, int LifetimeSampleCount, object RecomputedAtReported33Seconds, object RecomputedAtInferredDumpCenter, Dictionary<string, string> Reported, DumpRange InferredDumpSeconds, double AcquireT, double FirstAcceptedLabHoldT, int FirstAcceptedLabHoldRow, double? ReleaseT, bool ReleaseEventMissing, int WindowAttributedChanges, int WindowAttributedPairs, object[] WindowEventTotals, object Conservation, string ReportedGrade);
record RunResult(GlobalSummary Global, List<KeyRow> Keys, List<WindowRow> Windows, List<ExcludedRow> ExcludedAttempts);
