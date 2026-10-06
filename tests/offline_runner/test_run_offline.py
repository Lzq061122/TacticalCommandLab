from __future__ import annotations

import contextlib
import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock


REPO = Path(__file__).resolve().parents[2]
RUNNER_PATH = REPO / "tools" / "run_offline.py"
_spec = importlib.util.spec_from_file_location("run_offline_under_test", RUNNER_PATH)
assert _spec is not None and _spec.loader is not None
runner = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(runner)


PASSING_TEST = """import unittest
class SmokeTest(unittest.TestCase):
    def test_passes(self):
        self.assertTrue(True)
"""

FAILING_TEST = """import unittest
class SmokeTest(unittest.TestCase):
    def test_fails(self):
        self.assertTrue(False, 'deliberate fixture failure')
"""

SKIPPED_TEST = """import unittest
class SmokeTest(unittest.TestCase):
    @unittest.skip('deliberate fixture skip')
    def test_skipped(self):
        self.fail('must not execute')
"""


class OfflineRunnerContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="offline-runner-contract-")
        self.root = Path(self.temp.name) / "repo"
        self.dotnet = Path(self.temp.name) / "fake-dotnet.py"
        self._make_repo()
        self._make_dotnet()

    def tearDown(self) -> None:
        self.temp.cleanup()

    def _make_repo(self) -> None:
        (self.root / "tools").mkdir(parents=True)
        (self.root / "src" / "CommandLab.Core").mkdir(parents=True)
        (self.root / "src" / "CommandLab.Core" / "CommandLab.Core.csproj").write_text("fixture", encoding="utf-8")
        tests = self.root / "tests"
        (tests / "CommandLab.Tests").mkdir(parents=True)
        (tests / "CommandLab.Tests" / "CommandLab.Tests.csproj").write_text("fixture", encoding="utf-8")
        self.telemetry_dir = tests / "offline_telemetry"
        self.runner_dir = tests / "offline_runner"
        self.telemetry_dir.mkdir()
        self.runner_dir.mkdir()
        (self.telemetry_dir / "test_fixture.py").write_text(PASSING_TEST, encoding="utf-8")
        (self.runner_dir / "test_fixture.py").write_text(PASSING_TEST, encoding="utf-8")
        old_artifacts = tests / "artifacts"
        old_artifacts.mkdir()
        (old_artifacts / "historical.txt").write_text("keep-this-byte-for-byte", encoding="utf-8")

    def _make_dotnet(self) -> None:
        script = r'''#!/usr/bin/env python3
import os
from pathlib import Path
import sys

args = sys.argv[1:]
if args == ["--version"]:
    print("10.0.999")
    raise SystemExit(0)
if args and args[0] == "build":
    framework = args[args.index("--framework") + 1]
    print("fake build " + framework)
    if os.environ.get("FAKE_BUILD_FAIL") == framework:
        raise SystemExit(17)
    raise SystemExit(0)
if args and args[0] == "run":
    counts = {"CommandLab Phase 6": 15, "CommandLab Phase 7": 51,
              "CommandLab Phase 8": 10, "CommandLab Offline Measurements": 26}
    if os.environ.get("FAKE_D0_MISMATCH"):
        counts["CommandLab Phase 6"] = 14
    for header, count in counts.items():
        print(header)
        for _ in range(count):
            print("[PASS] fake-check")
    artifacts = Path.cwd() / "tests" / "artifacts"
    artifacts.mkdir(parents=True, exist_ok=True)
    (artifacts / "generated.txt").write_text("this-run", encoding="utf-8")
    print("fake offline test process completed")
    raise SystemExit(int(os.environ.get("FAKE_TEST_EXIT", "0")))
print("unexpected fake dotnet arguments", args)
raise SystemExit(99)
'''
        self.dotnet.write_text(script, encoding="utf-8")

    def _invoke(self, **env_overrides: str) -> tuple[int, str, str, Path | None]:
        argv = ["run_offline.py", "--dotnet", str(self.dotnet)]
        stdout = io.StringIO()
        stderr = io.StringIO()
        env = os.environ.copy()
        env.update(env_overrides)
        actual_run = runner.run
        actual_subprocess_run = runner.subprocess.run

        def portable_dotnet_run(command, cwd, command_env, log, label):
            if command and command[0] == str(self.dotnet):
                command = [runner.sys.executable, str(self.dotnet), *command[1:]]
            return actual_run(command, cwd, command_env, log, label)

        def portable_version_run(command, *args, **kwargs):
            if command and command[0] == str(self.dotnet):
                command = [runner.sys.executable, str(self.dotnet), *command[1:]]
            return actual_subprocess_run(command, *args, **kwargs)

        with (mock.patch.object(runner, "__file__", str(self.root / "tools" / "run_offline.py")),
              mock.patch.object(runner.sys, "argv", argv),
              mock.patch.dict(runner.os.environ, env, clear=True),
              mock.patch.object(runner, "run", side_effect=portable_dotnet_run),
              mock.patch.object(runner.subprocess, "run", side_effect=portable_version_run),
              contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr)):
            code = runner.main()
        output = stdout.getvalue()
        report_dir = None
        for line in output.splitlines():
            if line.startswith("Reports: "):
                report_dir = Path(line.removeprefix("Reports: "))
        return code, output, stderr.getvalue(), report_dir

    def _result(self, report_dir: Path | None) -> dict:
        self.assertIsNotNone(report_dir)
        assert report_dir is not None
        return json.loads((report_dir / "result.json").read_text(encoding="utf-8"))

    def test_success_records_every_stage_and_preserves_historical_artifacts(self) -> None:
        code, _, stderr, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 0, stderr)
        self.assertEqual(result["schema"], 1)
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["runtime"], "NOT_RUN")
        self.assertEqual(len(result["source"]["sha256"]), 64)
        self.assertEqual(result["source"]["git_head"], "UNKNOWN")
        self.assertEqual([item["status"] for item in result["builds"]], ["PASS"] * 6)
        self.assertEqual([result["checks"][name]["passed"] for name in ("D0", "E1-E11", "E12", "MEASURE")],
                         [15, 51, 10, 26])
        python_rows = result["checks"]["Python"]
        self.assertEqual({row["suite"] for row in python_rows},
                         {"tests/offline_telemetry", "tests/offline_runner"})
        self.assertTrue(all(row["status"] == "PASS" and row["passed"] > 0 for row in python_rows))
        self.assertEqual((self.root / "tests" / "artifacts" / "historical.txt").read_text(encoding="utf-8"),
                         "keep-this-byte-for-byte")
        assert report_dir is not None
        self.assertEqual((report_dir / "generated.txt").read_text(encoding="utf-8"), "this-run")
        self.assertTrue((report_dir / "summary.md").is_file())
        run_info = json.loads((report_dir / "run-info.json").read_text(encoding="utf-8"))
        self.assertEqual(run_info["source_files_sha256"], result["source"]["sha256"])

    def test_build_failure_is_recorded_and_nonzero(self) -> None:
        code, _, stderr, report_dir = self._invoke(FAKE_BUILD_FAIL="netstandard2.0")
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertIn("failed", stderr.lower())
        self.assertEqual(result["status"], "FAIL")
        self.assertTrue(result["failed_stage"].startswith("build "))
        self.assertTrue(any(row["status"] == "FAIL" and row["exit_code"] == 17 for row in result["builds"]))
        self.assertTrue((report_dir / "run.log").is_file())
        self.assertTrue((report_dir / "summary.md").is_file())

    def test_build_process_start_failure_marks_attempted_build_failed(self) -> None:
        real_popen = runner.subprocess.Popen

        def fail_build(command, *args, **kwargs):
            if len(command) > 2 and command[2] == "build":
                raise OSError("simulated process start failure")
            return real_popen(command, *args, **kwargs)

        with mock.patch.object(runner.subprocess, "Popen", side_effect=fail_build):
            code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertTrue(result["failed_stage"].startswith("build "))
        attempted = next(row for row in result["builds"] if row["status"] == "FAIL")
        self.assertIsNone(attempted["exit_code"])
        self.assertIn("simulated process start failure", attempted["error"])

    def test_source_copy_failure_is_recorded_as_runner_failure(self) -> None:
        with mock.patch.object(runner.shutil, "copytree", side_effect=OSError("simulated source copy failure")):
            code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failed_stage"], "sourcecopy")
        self.assertIn("simulated source copy failure", result["error"])

    def test_dotnet_test_process_failure_is_recorded_and_nonzero(self) -> None:
        code, _, _, report_dir = self._invoke(FAKE_TEST_EXIT="9")
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failed_stage"], "offline_tests")
        self.assertEqual(result["checks"]["D0"]["status"], "PASS")
        self.assertEqual(result["runtime"], "NOT_RUN")

    def test_acceptance_count_mismatch_records_actual_count_and_nonzero(self) -> None:
        code, _, _, report_dir = self._invoke(FAKE_D0_MISMATCH="1")
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failed_stage"], "D0")
        self.assertEqual(result["checks"]["D0"],
                         {"status": "FAIL", "passed": 14, "expected": 15, "failed": 0})

    def test_python_suite_with_zero_discovered_tests_fails(self) -> None:
        (self.runner_dir / "test_fixture.py").write_text("import unittest\n", encoding="utf-8")
        code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["failed_stage"], "python unittest tests/offline_runner")
        row = next(row for row in result["checks"]["Python"] if row["suite"] == "tests/offline_runner")
        self.assertEqual(row["status"], "FAIL")
        self.assertEqual(row["discovered"], 0)
        self.assertEqual(row["passed"], 0)

    def test_required_python_suite_missing_is_failed_discovery(self) -> None:
        (self.telemetry_dir / "test_fixture.py").unlink()
        code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failed_stage"], "python_discovery")
        self.assertIn("tests/offline_telemetry", result["error"])
        self.assertEqual(result["checks"]["Python"], [])

    def test_python_suite_failure_counts_actual_failure(self) -> None:
        (self.telemetry_dir / "test_fixture.py").write_text(FAILING_TEST, encoding="utf-8")
        code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertEqual(result["failed_stage"], "python unittest tests/offline_telemetry")
        row = next(row for row in result["checks"]["Python"] if row["suite"] == "tests/offline_telemetry")
        self.assertEqual(row["status"], "FAIL")
        self.assertEqual(row["discovered"], 1)
        self.assertEqual(row["passed"], 0)
        self.assertEqual(row["failed"], 1)

    def test_all_python_tests_skipped_is_not_a_pass(self) -> None:
        (self.runner_dir / "test_fixture.py").write_text(SKIPPED_TEST, encoding="utf-8")
        code, _, _, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        row = next(row for row in result["checks"]["Python"] if row["suite"] == "tests/offline_runner")
        self.assertEqual(row["status"], "FAIL")
        self.assertEqual(row["discovered"], 1)
        self.assertEqual(row["skipped"], 1)
        self.assertEqual(row["passed"], 0)

    def test_missing_sdk_keeps_exit_two_and_precise_stderr(self) -> None:
        argv = ["run_offline.py", "--dotnet", str(self.root / "no-such-dotnet")]
        stdout = io.StringIO()
        stderr = io.StringIO()
        with (mock.patch.object(runner, "__file__", str(self.root / "tools" / "run_offline.py")),
              mock.patch.object(runner.sys, "argv", argv),
              contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr)):
            code = runner.main()
        self.assertEqual(code, 2)
        self.assertEqual(stdout.getvalue(), "")
        self.assertEqual(stderr.getvalue().strip(), f"ERROR: dotnet executable does not exist: {self.root / 'no-such-dotnet'}")

    def test_report_artifact_copy_failure_prevents_pass(self) -> None:
        with mock.patch.object(runner.shutil, "copy2", side_effect=OSError("simulated report copy failure")):
            code, _, stderr, report_dir = self._invoke()
        result = self._result(report_dir)
        self.assertEqual(code, 1)
        self.assertIn("artifact_copy", stderr)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failed_stage"], "artifact_copy")
        self.assertIn("simulated report copy failure", result["error"])
        self.assertTrue((report_dir / "summary.md").is_file())

    def test_run_id_collision_does_not_overwrite_existing_report(self) -> None:
        fixed_time = dt.datetime(2026, 1, 2, tzinfo=dt.timezone.utc)

        class FixedDateTime:
            @staticmethod
            def now(_tz=None):
                return fixed_time

        collision = self.root / "tests" / "artifacts" / "offline-20260102T000000.000000Z-deadbeef"
        collision.mkdir()
        sentinel = collision / "result.json"
        sentinel.write_text("historical-result", encoding="utf-8")
        with (mock.patch.object(runner, "dt", mock.Mock(datetime=FixedDateTime, timezone=dt.timezone)),
              mock.patch.object(runner.uuid, "uuid4", return_value=mock.Mock(hex="deadbeef"))):
            code, stdout, stderr, report_dir = self._invoke()
        self.assertEqual(code, 1)
        self.assertIsNone(report_dir)
        self.assertNotIn("Reports:", stdout)
        self.assertIn("File exists", stderr)
        self.assertEqual(sentinel.read_text(encoding="utf-8"), "historical-result")


if __name__ == "__main__":
    unittest.main()
