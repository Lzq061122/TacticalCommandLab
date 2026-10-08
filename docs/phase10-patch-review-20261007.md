# Phase 10 patch review — 2026-10-07

## Version boundary

Version A is frozen at commit `e57e157e839e334f532d7f875224491af6bd140d`. Version B is the cleanup repair version; its commit is recorded in the final closure index. The run04–run11 measurements were captured earlier at source HEAD `adab4cf008a8dd6e83f8e12c03f1423483cb98f0` plus the four-file runtime/code diff saved in each run's `git-diff.txt`; neither A nor B is the measurement-time HEAD. B changes driver cleanup reporting only and has not been measured in Bannerlord.

## Cause and behavior change

In A, `end_mission` and `games_stop` exceptions were logged without setting `$runFailed`, `Stop-Process` errors were suppressed, and a non-empty owned-PID residual list did not affect `run-result.status` or the final exit code. Thus the driver could write `COMPLETE` and return 0 after cleanup failure. B now marks cleanup exceptions as failure, recognizes explicit JSON-RPC `error` and MCP `result.isError=true` cleanup responses, records force-stop errors with the owned PID, and marks residual owned PIDs as failure before writing `run-result.json` and exiting (`tools/run_phase10.ps1:104–110, 266–308, 318`). Cleanup and residual selection still use `$startedGameIds`, whose value was reduced to the PID verified by `cbath/get_build_identity` before cleanup (`tools/run_phase10.ps1:199–202`).

The reusable driver's acceptance-document source now points at tracked `docs/phase10-driver-acceptance.md` (`tools/run_phase10.ps1:66`). Archived run directories retain their original `driver-script.ps1` and acceptance snapshots.

## Existing evidence

- All eight run04–run11 directories contain the same archived driver SHA-256 `541C4CE45C46718EC0633E77A8EA69A8196BB3EF72750357672F29377EB6CDC3`; their saved outcomes are `COMPLETE`, `cleanup=clean`, and no `exception.log`. These establish the historical runs ended cleanly, but do not exercise cleanup failure paths.
- Each run's `git-diff.txt` contains only the two Probe runtime files and offline runner/test files. The resulting measurements apply to that saved code-only working-tree snapshot. Later documentation/HANDOFF changes are not part of the recorded runtime code diff.
- The final offline artifact records six successful builds, D0 15/15, E1–E11 51/51, E12 10/10, MEASURE 26/26, and Python 14/14 plus 9/9; it records `runtime=NOT_RUN` and `working_tree=dirty` (`tests/artifacts/offline-20261006T135059.876430Z-5638d296/result.json`). It is offline regression evidence, not a game run.
- The archived BannerlordProbe net472 build log reports 0 warnings and 0 errors (`_audit/_local-phase10-20261006/probe-final-build.log`). Runs 09/10 recorded accepted Move reapply with matching immediate type and position; run11 recorded the expected rejected post-release reapply (`docs/local-phase10-verification-20261006.md:72–74, 96`).
- The measured driver is byte-identical to the frozen A snapshot and each run04–run11 archived copy. The separate recovered 179-line text is explicitly marked byte-identity-unverified (`_audit/_local-phase10-20261006/driver-recovery-info.json`); it is not treated as the original driver.

## B verification

`tests/phase10_driver/test_cleanup_ast.ps1` parses `tools/run_phase10.ps1`, extracts its actual cleanup-response checker, `finally` statement AST, and final `if ($runFailed) { exit 1 }` AST, then executes those driver nodes with mocked game/MCP/process functions and temporary evidence directories. Each case runs in an isolated PowerShell child process; the parent records the child's actual exit code. PowerShell 7.6.5 and Windows PowerShell 5.1.26100.9444 both passed all eight cases: normal cleanup (`COMPLETE/0`), thrown fallback `end_mission` and `games_stop` failures, JSON-RPC `error` and MCP `result.isError=true` responses, `Stop-Process` failure, a residual owned PID (each `FAIL/1`), and an unrelated live PID (`COMPLETE/0`, external PID untouched). Normal `games_stop` uses the archived success envelope shape with `result.structuredContent.code="terminated"`. Both error-envelope cases preserve their raw body and still fail after the owned PID exits. Failure cases write `cleanup-result.txt`, `run-result.json`, and `artifact-manifest.json`. The harness launched isolated PowerShell test child processes; `Get-Process` and `Stop-Process` were mocked, and no real game process was started or controlled. No external MCP/CBATH, Steam, or game call was made. Console logs are in `_audit/_phase10-closure-20261007/closuretestlogs/`.

## Limits

B has not been deployed or run in Bannerlord, and no host build was repeated for this cleanup-only PowerShell change. The isolated test uses synthetic standard error envelopes because the archived runtime evidence contains only a successful `games_stop` response (`result.structuredContent.code="terminated"`); no live failed response was recorded. The driver intentionally recognizes only JSON-RPC `error` property and MCP `result.isError=true`, without inferring failure from undocumented result codes.
