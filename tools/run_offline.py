#!/usr/bin/env python3
"""Build the cross-platform projects and run the offline acceptance suite."""
from __future__ import annotations

import argparse
import datetime as dt
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import hashlib
import json
import uuid

REMOTE_BASELINE = "c86167a887cb3c1691dd12fa2d19385d937e8364"
PHASES = (
    ("D0", "CommandLab Phase 6", "CommandLab Phase 7", 15),
    ("E1-E11", "CommandLab Phase 7", "CommandLab Phase 8", 51),
    ("E12", "CommandLab Phase 8", "CommandLab Offline Measurements", 10),
)


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


def run(command: list[str], cwd: Path, env: dict[str, str], log, label: str) -> str:
    print(f"\n[{label}] {' '.join(command)}", flush=True)
    proc = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    output: list[str] = []
    assert proc.stdout is not None
    for line in proc.stdout:
        print(line, end="", flush=True)
        log.write(line)
        output.append(line)
    code = proc.wait()
    if code:
        raise RuntimeError(f"{label} failed with exit code {code}")
    return "".join(output)


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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", help="path to .NET 10 dotnet executable; defaults to DOTNET or PATH")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    head, status = git_info(root)
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
    artifacts_root = root / "tests" / "artifacts"
    report_dir = artifacts_root / f"offline-{run_id}"
    report_dir.mkdir(parents=True, exist_ok=False)
    meta = [
        f"Source git HEAD: {head}",
        f"Working tree: {status}",
        f"Initial remote source reference: {REMOTE_BASELINE} (GitHub content export)",
        f".NET SDK: {version}",
        "Runtime=NOT_RUN (Bannerlord game runtime)",
        f"Python: {sys.version.split()[0]}",
        f"Started UTC: {dt.datetime.now(dt.timezone.utc).isoformat()}",
    ]
    print("\n".join(meta), flush=True)
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
    meta.append(f"Source files SHA-256: {digest.hexdigest()}")
    (report_dir / "run-info.txt").write_text("\n".join(meta) + "\n", encoding="utf-8")
    (report_dir / "run-info.json").write_text(json.dumps({
        "git_head": head, "working_tree": status, "initial_remote_source_reference": REMOTE_BASELINE,
        "dotnet_sdk": version, "python": sys.version.split()[0],
        "bannerlord_runtime": "NOT_RUN", "source_files_sha256": digest.hexdigest(),
        "started_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    }, indent=2) + "\n", encoding="utf-8")
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"

    with tempfile.TemporaryDirectory(prefix="tactical-command-lab-offline-") as tmp:
        shadow = Path(tmp)
        for name in ("src",):
            shutil.copytree(root / name, shadow / name, ignore=shutil.ignore_patterns("bin", "obj"))
        shutil.copytree(root / "tests" / "CommandLab.Tests", shadow / "tests" / "CommandLab.Tests",
                        ignore=shutil.ignore_patterns("bin", "obj", "artifacts"))
        aggregate = (report_dir / "run.log").open("w", encoding="utf-8", newline="")
        try:
            projects = [
                ("src/CommandLab.Core/CommandLab.Core.csproj", "net10.0"),
                ("src/CommandLab.Core/CommandLab.Core.csproj", "netstandard2.0"),
                ("src/CommandLab.Telemetry/CommandLab.Telemetry.csproj", "net10.0"),
                ("src/CommandLab.Telemetry/CommandLab.Telemetry.csproj", "netstandard2.0"),
                ("src/CommandLab.Simulation/CommandLab.Simulation.csproj", "net10.0"),
                ("tests/CommandLab.Tests/CommandLab.Tests.csproj", "net10.0"),
            ]
            for project, framework in projects:
                run([dotnet, "build", project, "--framework", framework, "--nologo", "-m:1",
                     "-p:BuildInParallel=false", "-p:UseSharedCompilation=false"], shadow, env,
                    aggregate, f"build {Path(project).stem} {framework}")
            test_output = run([dotnet, "run", "--project", "tests/CommandLab.Tests/CommandLab.Tests.csproj",
                               "--framework", "net10.0", "--no-build"], shadow, env, aggregate, "offline tests")
            # Run any standard-library unittest files added for offline telemetry helpers.
            telemetry_tests = root / "tests" / "offline_telemetry"
            if not telemetry_tests.is_dir() or not any(telemetry_tests.rglob("test_*.py")):
                raise RuntimeError("required tests/offline_telemetry unittest suite is missing")
            python_tests = sorted({p.parent for base in (telemetry_tests, root / "tools") if base.exists()
                                   for p in base.rglob("test_*.py")})
            if not python_tests:
                raise RuntimeError("required Python telemetry unittest suite contains no tests")
            for test_dir in python_tests:
                py_output = run([sys.executable, "-m", "unittest", "discover", "-s", str(test_dir), "-p", "test_*.py", "-v"],
                                root, env, aggregate, f"python unittest {test_dir.relative_to(root)}")
                ran = re.search(r"Ran (\d+) tests?", py_output)
                if not ran or int(ran.group(1)) < 1:
                    raise RuntimeError(f"Python unittest discovered no tests in {test_dir.relative_to(root)}")
            expected_total = 0
            for label, start, end, expected in PHASES:
                passed, failed = phase_count(test_output, start, end)
                expected_total += expected
                print(f"Acceptance {label}: {passed}/{expected}, failed={failed}", flush=True)
                if passed != expected or failed != 0:
                    raise RuntimeError(f"{label} acceptance mismatch: passed {passed}/{expected}, failed {failed}")
            measure_header = "CommandLab Offline Measurements"
            if measure_header in test_output:
                measure_passed, measure_failed = phase_count(test_output, measure_header, None)
                print(f"Acceptance MEASURE: {measure_passed} checks, failed={measure_failed}", flush=True)
                if measure_passed != 26 or measure_failed != 0:
                    raise RuntimeError(f"MEASURE acceptance mismatch: passed {measure_passed}/26, failed {measure_failed}")
            else:
                raise RuntimeError(f"test output is missing phase header: {measure_header}")
            total_checks = expected_total + measure_passed
            print(f"Acceptance total: {total_checks}/{total_checks} checks (including MEASURE)", flush=True)
            # Preserve the repository's historical reports; copy this run's outputs to a unique directory.
            produced = shadow / "tests" / "artifacts"
            if produced.exists():
                for path in produced.iterdir():
                    destination = report_dir / path.name
                    if path.is_file():
                        shutil.copy2(path, destination)
                    elif path.is_dir():
                        shutil.copytree(path, destination)
            aggregate.write(f"\nAcceptance total: {total_checks}/{total_checks} checks (including MEASURE)\n")
        except (RuntimeError, OSError) as exc:
            aggregate.write(f"\nERROR: {exc}\n")
            print(f"ERROR: {exc}", file=sys.stderr)
            return 1
        finally:
            aggregate.close()
    print(f"Reports: {report_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
