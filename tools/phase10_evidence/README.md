# Phase 10 telemetry evidence

This is a read-only offline diagnostic. It references the production `CommandLab.Telemetry` and `CommandLab.Core` projects and writes machine output under `_audit/_phase10-closure-20261007/`.

From the repository root, run:

```powershell
dotnet run --project tools/phase10_evidence/Phase10Evidence.csproj
```

Optional arguments are the run05 telemetry path, run06 telemetry path, and output directory, in that order. The default inputs are the preserved Phase 10 telemetry files. The tool does not launch the game or change raw inputs, runtime code, metrics thresholds, or verdict logic.

Each `runXX-diagnostics.json` contains the `MetricsCalculator.Compute` replay at 33.00 seconds and at the center of a clearly labeled inferred dump-time interval, all accepted effective changes grouped by entity/domain with physical source line numbers, every complete ABA triple, production-rule lifetime samples with window overlaps, event-window attribution, excluded rejected/unchanged rows, and conservation totals. The companion `results.json` collects both runs.

Telemetry timestamps are printed to four decimal places and the reported simulation duration to two. The inferred dump interval uses only the printed duration and the command, effective-change, and oscillation rates. Average lifetime is a consistency check: each printed endpoint can differ from its source by up to 0.00005 seconds, so the output includes the corresponding bound on the mean. The inferred interval and its center are reconstruction aids, not claims about the exact original dump time.
