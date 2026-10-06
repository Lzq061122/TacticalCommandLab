#!/usr/bin/env python3
"""Build the cross-platform projects and run the offline acceptance suite."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import uuid

REMOTE_BASELINE = "c86167a887cb3c1691dd12fa2d19385d937e8364"
PHASES = (
    ("D0", "CommandLab Phase 6", "CommandLab Phase 7", 15),
    ("E1-E11", "CommandLab Phase 7", "CommandLab Phase 8", 51),
    ("E12", "CommandLab Phase 8", "CommandLab Offline Measurements", 10),
)
BUILD_TARGETS = (
    ("src/CommandLab.Core/CommandLab.Core.csproj", "net10.0"),
    ("src/CommandLab.Core/CommandLab.Core.csproj", "netstandard2.0"),
    ("src/CommandLab.Telemetry/CommandLab.Telemetry.csproj", "net10.0"),
    ("src/CommandLab.Telemetry/CommandLab.Telemetry.csproj", "netstandard2.0"),
    ("src/CommandLab.Simulation/CommandLab.Simulation.csproj", "net10.0"),
    ("tests/CommandLab.Tests/CommandLab.Tests.csproj", "net10.0"),
)
MEASURE_HEADER = "CommandLab Offline Measurements"
PYTHON_TEST_ROOTS = ("tests/offline_telemetry", "tests/offline_runner", "tools")


def git_info(root: Path) -> tuple[str, str]:
    try:
        head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], check=True,
                              capture_output=True, text=True).stdout.strip()
        dirty = subprocess.run(["git", "-C", str(root), "status", "--porcelain"], check=True,
                               capture_output=True, text=True).stdout.strip()
        return head, "dirty" if dirty else "clean"
    except (OSError, subprocess.CalledProcessError):
        return "UNKNOWN", "not-a-git-worktree"


def resolve_dotnet(explicit: str | None) -> str:
    candidate = explicit or os.environ.get("DOTNET") or shutil.which("dotnet")
    if not candidate:
        raise RuntimeError("dotnet was not found; install .NET 10 SDK or pass --dotnet /path/to/dotnet")
    resolved = shutil.which(candidate) if os.path.sep not in candidate else candidate
    if not resolved or not Path(resolved).is_file():
        raise RuntimeError(f"dotnet executable does not exist: {candidate}")
    return str(Path(resolved).resolve())


def run(command: list[str], cwd: Path, env: dict[str, str], log, label: str) -> tuple[str, int]:
    print(f"\n[{label}] {' '.join(command)}", flush=True)
    proc = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    output: list[str] = []
    assert proc.stdout is not None
    for line in proc.stdout:
        print(line, end="", flush=True)
        log.write(line)
        output.append(line)
    return "".join(output), proc.wait()


def phase_count(output: str, start: str, end: str | None) -> tuple[int, int]:
    begin = output.find(start)
    if begin < 0:
        raise RuntimeError(f"test output is missing phase header: {start}")
    finish = output.find(end, begin + len(start)) if end else len(output)
    if end and finish < 0:
        raise RuntimeError(f"test output is missing phase boundary: {end}")
    section = output[begin:finish]
    passed = len(re.findall(r"\[PASS\]", section))
    failed = len(re.findall(r"\[FAIL\]", section))
    return passed, failed


def source_digest(root: Path) -> str:
    source_roots = [root / name for name in ("src", "tests", "tools") if (root / name).exists()]
    excluded_dirs = {"bin", "obj", "artifacts", "__pycache__", ".git"}
    source_paths = sorted(p for base in source_roots for p in base.rglob("*")
                          if p.is_file()
                          and (p.suffix in {".cs", ".csproj", ".py"}
                               or (p.suffix == ".txt" and "offline_telemetry" in p.parts
                                   and "fixtures" in p.parts))
                          and not excluded_dirs.intersection(p.relative_to(base).parts))
    digest = hashlib.sha256()
    for path in source_paths:
        digest.update(path.relative_to(root).as_posix().encode("utf-8") + b"\0")
        digest.update(path.read_bytes() + b"\0")
    return digest.hexdigest()


def result_template(run_id: str, head: str, tree_status: str, python_version: str) -> dict:
    checks = {
        label: {"status": "NOT_RUN", "passed": 0, "expected": expected, "failed": 0}
        for label, _, _, expected in PHASES
    }
    checks["MEASURE"] = {"status": "NOT_RUN", "passed": 0, "expected": 26, "failed": 0}
    return {
        "schema": 1,
        "status": "FAIL",
        "runtime": "NOT_RUN",
        "run_id": run_id,
        "source": {"sha256": None, "git_head": head, "working_tree": tree_status,
                   "reference": REMOTE_BASELINE},
        "sdk": {"dotnet": None, "python": python_version},
        "builds": [
            {"project": project, "framework": framework, "status": "NOT_RUN",
             "exit_code": None, "error": None}
            for project, framework in BUILD_TARGETS
        ],
        "checks": {**checks, "Python": []},
        "failed_stage": None,
        "error": None,
        "logs": {"run": "run.log", "summary": "summary.md", "run_info": "run-info.json"},
    }


def write_json(path: Path, value: dict) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def write_summary(path: Path, result: dict) -> None:
    lines = ["# Offline verification", "", f"Status: **{result['status']}**",
             f"Runtime: `{result['runtime']}`", f"Run ID: `{result['run_id']}`"]
    if result["failed_stage"]:
        lines.extend([f"Failed stage: `{result['failed_stage']}`", f"Error: {result['error'] or ''}"])
    lines.extend(["", "## Builds", "", "| Project | Framework | Status | Exit | Error |",
                  "|---|---|---:|---:|---|"])
    for build in result["builds"]:
        lines.append(f"| {build['project']} | {build['framework']} | {build['status']} | "
                     f"{build['exit_code']} | {build['error'] or ''} |")
    lines.extend(["", "## Checks", "", "| Suite | Status | Passed | Expected | Failed |",
                  "|---|---:|---:|---:|---:|"])
    for name in ("D0", "E1-E11", "E12", "MEASURE"):
        row = result["checks"][name]
        lines.append(f"| {name} | {row['status']} | {row['passed']} | {row['expected']} | {row['failed']} |")
    for row in result["checks"]["Python"]:
        lines.append(f"| Python: {row['suite']} | {row['status']} | {row['passed']} | "
                     f"{row['expected']} | {row['failed']} |")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def python_result(output: str, exit_code: int, suite: str) -> dict:
    ran = re.search(r"Ran\s+(\d+)\s+tests?", output)
    summary = re.search(r"(?m)^(FAILED|OK)(?:\s*\(([^)]*)\))?$", output)
    failures = errors = skipped = 0
    if summary and summary.group(2):
        values = dict((key, int(value)) for key, value in
                      re.findall(r"(failures|errors|skipped)=(\d+)", summary.group(2)))
        failures = values.get("failures", 0)
        errors = values.get("errors", 0)
        skipped = values.get("skipped", 0)
    discovered = int(ran.group(1)) if ran else 0
    failed = failures + errors
    passed = max(0, discovered - failed - skipped)
    error = None
    if not ran or discovered < 1:
        error = "Python unittest discovered no tests"
    elif discovered - skipped < 1:
        error = "Python unittest executed no tests (all discovered tests were skipped)"
    elif exit_code != 0:
        error = f"Python unittest exited with code {exit_code}"
    elif failed:
        error = f"Python unittest reported {failed} failures/errors"
    elif not summary or summary.group(1) != "OK":
        error = "Python unittest summary is missing or unsuccessful"
    return {"suite": suite, "status": "PASS" if error is None else "FAIL",
            "passed": passed, "expected": discovered, "failed": failed,
            "discovered": discovered, "skipped": skipped, "exit_code": exit_code, "error": error}


def discover_python_suites(root: Path) -> list[Path]:
    telemetry = root / "tests" / "offline_telemetry"
    runner = root / "tests" / "offline_runner"
    if not telemetry.is_dir() or not any(telemetry.rglob("test_*.py")):
        raise RuntimeError("required tests/offline_telemetry unittest suite is missing")
    if not runner.is_dir() or not any(runner.rglob("test_*.py")):
        raise RuntimeError("required tests/offline_runner unittest suite is missing")
    suites = sorted({p.parent for name in PYTHON_TEST_ROOTS
                     for p in (root / name).rglob("test_*.py") if p.is_file()})
    if not suites:
        raise RuntimeError("required Python unittest suites contain no tests")
    return suites


def mark_failure(result: dict, stage: str, error: object) -> None:
    result["status"] = "FAIL"
    if result["failed_stage"] is None:
        result["failed_stage"] = stage
        result["error"] = str(error)
    else:
        result["error"] = f"{result['error']}; {stage}: {error}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", help="path to .NET 10 dotnet executable; defaults to DOTNET or PATH")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    head, tree_status = git_info(root)
    try:
        dotnet = resolve_dotnet(args.dotnet)
        version = subprocess.run([dotnet, "--version"], cwd=root, check=True, capture_output=True,
                                 text=True, encoding="utf-8").stdout.strip()
        if not version.startswith("10."):
            raise RuntimeError(f".NET 10 SDK required; found {version}")
    except (RuntimeError, subprocess.CalledProcessError, OSError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    run_id = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ") + "-" + uuid.uuid4().hex[:8]
    report_dir = root / "tests" / "artifacts" / f"offline-{run_id}"
    result = result_template(run_id, head, tree_status, sys.version.split()[0])
    created_report_dir = False
    try:
        report_dir.mkdir(parents=True, exist_ok=False)
        created_report_dir = True
        result_path = report_dir / "result.json"
        result["sdk"]["dotnet"] = version
        result["source"]["sha256"] = source_digest(root)
        write_json(result_path, result)
        start_time = dt.datetime.now(dt.timezone.utc).isoformat()
        run_info = {
            "git_head": head, "working_tree": tree_status,
            "initial_remote_source_reference": REMOTE_BASELINE, "dotnet_sdk": version,
            "python": sys.version.split()[0], "bannerlord_runtime": "NOT_RUN",
            "source_files_sha256": result["source"]["sha256"], "started_utc": start_time,
        }
        (report_dir / "run-info.json").write_text(json.dumps(run_info, indent=2) + "\n", encoding="utf-8")
        meta = [f"Source git HEAD: {head}", f"Working tree: {tree_status}",
                f"Initial remote source reference: {REMOTE_BASELINE} (GitHub content export)",
                f".NET SDK: {version}", "Runtime=NOT_RUN (Bannerlord game runtime)",
                f"Python: {sys.version.split()[0]}", f"Started UTC: {start_time}",
                f"Source files SHA-256: {result['source']['sha256']}"]
        (report_dir / "run-info.txt").write_text("\n".join(meta) + "\n", encoding="utf-8")
        result["logs"]["run_info"] = "run-info.json"
    except (OSError, RuntimeError) as exc:
        mark_failure(result, "run_metadata", exc)
        print(f"ERROR: {exc}", file=sys.stderr)
        try:
            if created_report_dir:
                write_json(report_dir / "result.json", result)
                write_summary(report_dir / "summary.md", result)
        except OSError:
            pass
        return 1

    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
    log_path = report_dir / "run.log"
    shadow: Path | None = None
    primary_failure = False
    try:
        with tempfile.TemporaryDirectory(prefix="tactical-command-lab-offline-") as tmp:
            shadow = Path(tmp)
            with log_path.open("w", encoding="utf-8", newline="") as aggregate:
                try:
                    for name in ("src",):
                        shutil.copytree(root / name, shadow / name,
                                        ignore=shutil.ignore_patterns("bin", "obj"))
                    shutil.copytree(root / "tests" / "CommandLab.Tests", shadow / "tests" / "CommandLab.Tests",
                                    ignore=shutil.ignore_patterns("bin", "obj", "artifacts"))
                except OSError as exc:
                    mark_failure(result, "sourcecopy", exc)
                    raise RuntimeError(str(exc)) from exc

                for index, (project, framework) in enumerate(BUILD_TARGETS):
                    label = f"build {Path(project).stem} {framework}"
                    try:
                        output, code = run([dotnet, "build", project, "--framework", framework, "--nologo", "-m:1",
                                            "-p:BuildInParallel=false", "-p:UseSharedCompilation=false"],
                                           shadow, env, aggregate, label)
                        entry = result["builds"][index]
                        entry["exit_code"] = code
                        entry["status"] = "PASS" if code == 0 else "FAIL"
                        if code:
                            entry["error"] = f"exit code {code}"
                            raise RuntimeError(f"{label} failed with exit code {code}")
                    except (OSError, RuntimeError) as exc:
                        entry = result["builds"][index]
                        if entry["status"] == "NOT_RUN":
                            entry["status"] = "FAIL"
                            entry["error"] = str(exc)
                        mark_failure(result, label, exc)
                        primary_failure = True
                        raise
                    write_json(result_path, result)

                try:
                    test_output, test_code = run(
                        [dotnet, "run", "--project", "tests/CommandLab.Tests/CommandLab.Tests.csproj",
                         "--framework", "net10.0", "--no-build"], shadow, env, aggregate, "offline tests")
                except OSError as exc:
                    mark_failure(result, "offline_tests", exc)
                    primary_failure = True
                    test_output, test_code = "", None
                for label, start, end, expected in PHASES:
                    try:
                        passed, failed = phase_count(test_output, start, end)
                        status = "PASS" if passed == expected and failed == 0 else "FAIL"
                        result["checks"][label] = {"status": status, "passed": passed,
                                                   "expected": expected, "failed": failed}
                        if status == "FAIL":
                            raise RuntimeError(f"{label} acceptance mismatch: passed {passed}/{expected}, failed {failed}")
                    except RuntimeError as exc:
                        if label not in result["checks"] or result["checks"][label]["status"] == "NOT_RUN":
                            result["checks"][label] = {"status": "FAIL", "passed": 0,
                                                       "expected": expected, "failed": 0}
                        mark_failure(result, label, exc)
                        primary_failure = True
                        break
                try:
                    passed, failed = phase_count(test_output, MEASURE_HEADER, None)
                    result["checks"]["MEASURE"] = {"status": "PASS" if passed == 26 and failed == 0 else "FAIL",
                                                    "passed": passed, "expected": 26, "failed": failed}
                    if passed != 26 or failed != 0:
                        raise RuntimeError(f"MEASURE acceptance mismatch: passed {passed}/26, failed {failed}")
                except RuntimeError as exc:
                    result["checks"]["MEASURE"]["status"] = "FAIL"
                    mark_failure(result, "MEASURE", exc)
                    primary_failure = True

                if test_code:
                    mark_failure(result, "offline_tests", f"test runner exited with code {test_code}")
                    primary_failure = True

                if not primary_failure:
                    try:
                        suites = discover_python_suites(root)
                    except RuntimeError as exc:
                        mark_failure(result, "python_discovery", exc)
                        primary_failure = True
                        suites = []
                    write_json(result_path, result)
                    for test_dir in suites:
                        suite = test_dir.relative_to(root).as_posix()
                        label = f"python unittest {suite}"
                        try:
                            py_output, py_code = run(
                                [sys.executable, "-m", "unittest", "discover", "-s", str(test_dir),
                                 "-p", "test_*.py", "-v"], root, env, aggregate, label)
                            py_row = python_result(py_output, py_code, suite)
                            result["checks"]["Python"].append(py_row)
                            if py_row["status"] != "PASS":
                                mark_failure(result, label, py_row["error"])
                                primary_failure = True
                                write_json(result_path, result)
                                break
                        except OSError as exc:
                            py_row = python_result("", 1, suite)
                            py_row["error"] = str(exc)
                            result["checks"]["Python"].append(py_row)
                            mark_failure(result, label, exc)
                            primary_failure = True
                            write_json(result_path, result)
                            break
                        write_json(result_path, result)

                if not primary_failure:
                    result["status"] = "PASS"
                    for row in result["checks"].values():
                        if isinstance(row, dict) and row.get("status") != "PASS":
                            result["status"] = "FAIL"
                            mark_failure(result, "result_validation", "a required check did not pass")
                            break
                        if isinstance(row, list) and any(item["status"] != "PASS" for item in row):
                            result["status"] = "FAIL"
                            mark_failure(result, "result_validation", "a Python suite did not pass")
                            break
                    if not result["checks"]["Python"]:
                        result["status"] = "FAIL"
                        mark_failure(result, "result_validation", "no Python suites were recorded")
                # Preserve outputs from successful test execution before the temporary source tree is removed.
                produced = shadow / "tests" / "artifacts"
                if produced.exists():
                    try:
                        for path in produced.iterdir():
                            destination = report_dir / path.name
                            if destination.exists():
                                raise FileExistsError(f"refusing to overwrite report artifact: {destination.name}")
                            if path.is_file():
                                shutil.copy2(path, destination)
                            elif path.is_dir():
                                shutil.copytree(path, destination)
                    except OSError as exc:
                        mark_failure(result, "artifact_copy", exc)
                        primary_failure = True
                        print(f"ERROR: artifact_copy: {exc}", file=sys.stderr)

                if result["status"] == "FAIL" and result["failed_stage"]:
                    aggregate.write(f"\nERROR: {result['failed_stage']}: {result['error']}\n")
    except (OSError, RuntimeError) as exc:
        if result["failed_stage"] is None:
            mark_failure(result, "runner", exc)
        primary_failure = True
        print(f"ERROR: {exc}", file=sys.stderr)

    if primary_failure:
        result["status"] = "FAIL"
    try:
        write_summary(report_dir / "summary.md", result)
        write_json(report_dir / "result.json", result)
    except OSError as exc:
        mark_failure(result, "report_write", exc)
        result["status"] = "FAIL"
        print(f"ERROR: report_write: {exc}", file=sys.stderr)
        try:
            write_summary(report_dir / "summary.md", result)
            write_json(report_dir / "result.json", result)
        except OSError:
            pass
        return 1

    print(f"Reports: {report_dir}")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
