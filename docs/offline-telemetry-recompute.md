# Offline telemetry recomputation

Run the standard-library-only Python tool against one existing telemetry text file:

```sh
python3 tools/recompute_telemetry.py path/to/telemetry.txt
```

It reads the file without starting Bannerlord, writes one JSON report to stdout, and records `runtime: NOT_RUN`. A missing input path exits 2; a malformed recognized row exits 1; a structurally readable log with insufficient evidence exits 0 and explains the evidence status in JSON. No database, configuration file, or third-party package is used.

The report includes the input SHA-256. `EVENT_FILE_MISSING` means the input path could not be read, or the readable file contains no canonical WriteAttempt rows. `FORMAT_ERROR` means a required calculation field, recognized row, or required numeric/flag value is malformed. `NO_RELEVANT_EVIDENCE` means there are WriteAttempt rows but no accepted effective Lab write. `EVIDENCE_INSUFFICIENT` means Lab changes exist but no matching override was observed. `EVIDENCE_FOUND` reports a recomputed H1 pair; it is not an experimental pass. `experiment_qualification` remains `UNKNOWN` unless a `HARMONY_PATCH_FAILED` note makes it `INELIGIBLE`. The offline evidence does not independently establish patch effectiveness, traffic completeness, or entity identity reliability, so it never declares the full experiment passed.

Canonical attempts are rows beginning with `t=`. For calculation, the parser validates finite nonnegative `t`, nonempty `E`, `D`, and `W`, `D` in `Movement|Facing|Arrangement`, and binary `chg`/`rej`. It rejects duplicate fields and malformed values. It does not claim whole-record validation: unrelated canonical fields are retained by the source format but not interpreted here; for example, `exp=-Infinity` is valid collector output and is ignored. AUTH, TASK, HOST, CENSUS, and NOTE rows do not add WriteAttempts. SHADOW rows use their own parsed `t`, `E`, and `D`; HOST projections and notes are not re-counted.

An accepted effective Lab write has `W=Lab`, `rej=0`, and `chg=1`. During the original-order replay, the tool remembers the latest such Lab event per `(E,D)` and pairs it with the first later `W=Native`, `rej=0`, `chg=1` event for that key whose timestamp is not earlier. Original file order resolves equal timestamps. `first_latency_seconds` is the first pair in replay order, while `pairs` records the first observed Native-tagged coverage per key. `Native` is only the telemetry writer tag; the report does not attribute it to a specific Vanilla caller.

The X5 lower-bound estimate groups SHADOW changes and effective WriteAttempts by `(E,D)`, then sums `max(0, shadow_count - effective_write_count)` across keys. It is `EVIDENCE_INSUFFICIENT` if either SHADOW rows or WriteAttempt rows are absent. A reported zero means no positive count gap was observed in that comparison; it does not prove complete coverage.

## Historical source checks

The following existing repository logs were read at commit `c86167a887cb3c1691dd12fa2d19385d937e8364`. The files under `tests/offline_telemetry/fixtures` are immutable test copies/excerpts; original `tests/artifacts` files were not edited.

| Historical source | Size / SHA-256 | Recomputed result |
| --- | --- | --- |
| `tests/artifacts/host/valid-20260923-2154-census-baseline/telemetry-20260923-215427-mission-start.txt` | 196,719 bytes / `bd134f2a290afe059d1b51f5e7b53517250ef32c5a111b9a660c535e048180ae` | 166 attempts, 128 effective writes, 0 accepted effective Lab writes, 1,734 SHADOW rows, X5 lower-bound 1,710; `NO_RELEVANT_EVIDENCE`, qualification `UNKNOWN`. |
| `tests/artifacts/host/valid-20261004-0133-census-baseline/telemetry-20261004-013303-census-baseline.txt` | 597,132 bytes / `b88390bb69800fc401b0e385292e28e2760505c4df0d2b779c0ce2eb3a15a53a` | 1,518 attempts, 136 effective writes, 0 accepted effective Lab writes, 53 SHADOW rows, X5 lower-bound 0; `NO_RELEVANT_EVIDENCE`, qualification `UNKNOWN`. Zero is not a completeness claim. |
| `tests/artifacts/host/invalid-20260922-1046-p0fail/telemetry-20260922-104700-tick.txt` | 38 bytes / `29601b82de5c3b8f2f5b2d8c98bb22b80c1eb92b3b1df6a872e01d4d13b483c4` | One `NOTE|...|HARMONY_PATCH_FAILED`, no attempts; `EVENT_FILE_MISSING`, qualification `INELIGIBLE`. |

The first and third source logs have exact full-file copies in the new fixture directory and are tested with fixed expected hashes and counts. The first log predates the newer globally unique `T{team}F{formation}` naming scheme; the calculator uses entity values exactly as logged and does not infer identity reliability.

## Example

For the 2026-10-04 historical baseline, the report includes:

```json
{
  "assessment_status": "NO_RELEVANT_EVIDENCE",
  "experiment_qualification": "UNKNOWN",
  "runtime": "NOT_RUN",
  "evidence": {
    "first_override_latency": {"status": "INSUFFICIENT", "first_latency_seconds": null},
    "x5_coverage_gap_lower_bound": {
      "status": "ESTIMATED",
      "count": 0,
      "interpretation": "zero means no observed positive gap; it does not prove complete coverage"
    }
  }
}
```

Run tests with:

```sh
python3 -m unittest discover -s tests/offline_telemetry -v
```
