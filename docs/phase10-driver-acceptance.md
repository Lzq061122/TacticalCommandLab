# Local Phase 10 driver patch acceptance — 2026-10-06

Scope: minimal changes to `_audit/_tcl-step5.ps1` and its local evidence note. Do not modify CombatBrain or CBATH source, experiment policy, thresholds, core source, or Phase 11/12 work.

1. Preflight detects an already-running Bannerlord process and aborts without stopping it or calling `games_stop`.
2. Steam is launched hidden when the existing driver needs to start the game.
3. Missing runtime identity aborts. `active_modules` must exactly match the configured 11-module `bannerlord148-nocb` set and contain no AI mod.
4. The script parses under Windows PowerShell 5.1 and PowerShell 7 while retaining the existing GABS/MCP tool paths and lifecycle.
5. Each run archives raw probe responses, early and late formations, `get_last_run`, exception logs, and this run's `rgl_log`, `rgl_log_errors`, and `watchdog_log` files when present. It records the runtime PID/start time and loaded Probe/Core/Telemetry paths and on-disk SHA-256 where process module inspection permits.
6. Each run archives source HEAD, Git status/diff, driver script, the source DLL SHA-256 manifest, original script, actual seeded script, entity mapping, and the run result. A non-empty evidence directory is rejected to prevent overwrite.
7. `census-baseline` accepts an empty EntityId. Other canonical experiments require a full census identity (`T#F#`) or an explicit seeded script path. When EntityId is supplied, only entity tokens in action lines are replaced; timings, policies, and thresholds stay unchanged.
8. A completed mission uses `end_mission` then `games_stop`; fallback cleanup and residual checks target only the PID verified by the runtime identity for this run. Failed runs write `run-result.json` and return a nonzero process exit code.

Verification boundary: validate parsing and script adaptation without starting Bannerlord. The main agent performs game runtime acceptance and experiments.
