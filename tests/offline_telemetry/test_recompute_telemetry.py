import hashlib
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = Path(__file__).resolve().parent / "fixtures"
SCRIPT = ROOT / "tools" / "recompute_telemetry.py"


def load(name):
    return (FIXTURES / name).read_bytes()


class RecomputeTelemetryTests(unittest.TestCase):
    def test_synthetic_pairs_and_x5_keyed_counts(self):
        raw = load("synthetic_pairs.txt")
        result = __import__("importlib.util").util.spec_from_file_location("recompute_telemetry", SCRIPT)
        module = __import__("importlib.util").util.module_from_spec(result)
        result.loader.exec_module(module)
        report = module.recompute_bytes(raw, "synthetic_pairs.txt")

        self.assertEqual(report["input"]["sha256"], hashlib.sha256(raw).hexdigest())
        self.assertEqual(report["assessment_status"], "EVIDENCE_FOUND")
        self.assertEqual(report["experiment_qualification"], "UNKNOWN")
        self.assertNotEqual(report["assessment_status"], "PASS")
        self.assertEqual(report["runtime"], "NOT_RUN")
        self.assertEqual(report["statistics"]["canonical_write_attempts"], 14)
        self.assertEqual(report["statistics"]["accepted_effective_lab_writes"], 5)
        self.assertEqual(report["statistics"]["rejected_write_attempts"], 1)
        self.assertEqual(report["statistics"]["effective_write_attempts_all_writers"], 12)

        latency = report["evidence"]["first_override_latency"]
        self.assertEqual(latency["status"], "FOUND")
        self.assertEqual(latency["first_latency_seconds"], 0.8)
        self.assertEqual([(x["entity"], x["domain"]) for x in latency["pairs"]],
                         [("A", "Movement"), ("C", "Movement"), ("E", "Movement")])
        self.assertEqual(latency["pairs"][1]["latency_seconds"], 0.0)
        self.assertEqual(latency["pairs"][1]["lab_line"] + 1, latency["pairs"][1]["native_tagged_line"])
        self.assertEqual(latency["pairs"][2]["latency_seconds"], 0.5)
        self.assertEqual(latency["pairs"][2]["lab_t"], 5.5)
        self.assertTrue(all(x["native_tagged_line"] > x["lab_line"] for x in latency["pairs"]))
        self.assertNotIn("D", [x["entity"] for x in latency["pairs"]])

        x5 = report["evidence"]["x5_coverage_gap_lower_bound"]
        self.assertEqual(x5["status"], "ESTIMATED")
        self.assertEqual(x5["count"], 2)
        self.assertEqual(x5["shadow_minus_effective_writes_by_entity_domain"]["A|Movement"], 1)
        self.assertEqual(x5["shadow_minus_effective_writes_by_entity_domain"]["B|Facing"], 1)
        self.assertEqual(x5["shadow_minus_effective_writes_by_entity_domain"]["A|Facing"], 0)
        self.assertIn("does not prove", x5["interpretation"])

    def test_historical_valid_excerpt_has_no_lab_evidence(self):
        module = self._module()
        report = module.recompute_bytes(load("historical_valid_excerpt.txt"), "historical_valid_excerpt.txt")
        self.assertEqual(report["statistics"]["canonical_write_attempts"], 2)
        self.assertEqual(report["statistics"]["shadow_change_samples"], 1)
        self.assertEqual(report["assessment_status"], "NO_RELEVANT_EVIDENCE")
        self.assertEqual(report["experiment_qualification"], "UNKNOWN")
        self.assertEqual(report["evidence"]["first_override_latency"]["status"], "INSUFFICIENT")
        self.assertEqual(report["evidence"]["x5_coverage_gap_lower_bound"]["count"], 1)
        self.assertEqual(report["runtime"], "NOT_RUN")

    def test_full_historical_valid_log_has_independent_expected_counts(self):
        raw = load("historical_valid_full_20260923.txt")
        report = self._module().recompute_bytes(raw, "historical_valid_full_20260923.txt")
        self.assertEqual(hashlib.sha256(raw).hexdigest(),
                         "bd134f2a290afe059d1b51f5e7b53517250ef32c5a111b9a660c535e048180ae")
        self.assertEqual(report["statistics"]["canonical_write_attempts"], 166)
        self.assertEqual(report["statistics"]["accepted_effective_lab_writes"], 0)
        self.assertEqual(report["statistics"]["effective_write_attempts_all_writers"], 128)
        self.assertEqual(report["statistics"]["shadow_change_samples"], 1734)
        self.assertEqual(report["evidence"]["x5_coverage_gap_lower_bound"]["count"], 1710)
        self.assertEqual(report["assessment_status"], "NO_RELEVANT_EVIDENCE")

    def test_full_historical_invalid_file_detects_patch_failure(self):
        raw = load("historical_invalid_full.txt")
        report = self._module().recompute_bytes(raw, "historical_invalid_full.txt")
        self.assertEqual(hashlib.sha256(raw).hexdigest(),
                         "29601b82de5c3b8f2f5b2d8c98bb22b80c1eb92b3b1df6a872e01d4d13b483c4")
        self.assertEqual(report["assessment_status"], "EVENT_FILE_MISSING")
        self.assertEqual(report["experiment_qualification"], "INELIGIBLE")
        self.assertIn("HARMONY_PATCH_FAILED", " ".join(report["diagnostics"]))

    def test_historical_patch_failure_is_ineligible_and_has_no_events(self):
        report = self._module().recompute_bytes(load("historical_invalid.txt"), "historical_invalid.txt")
        self.assertEqual(report["assessment_status"], "EVENT_FILE_MISSING")
        self.assertEqual(report["experiment_qualification"], "INELIGIBLE")
        self.assertTrue(report["evidence"]["event_file_missing"])
        self.assertIn("HARMONY_PATCH_FAILED", " ".join(report["diagnostics"]))
        self.assertEqual(report["evidence"]["x5_coverage_gap_lower_bound"]["status"], "EVIDENCE_INSUFFICIENT")

    def test_failed_patch_blocks_qualification_even_with_h1_pair(self):
        raw = load("synthetic_pairs.txt") + b"\nNOTE|t=4.2000|HARMONY_PATCH_FAILED\n"
        report = self._module().recompute_bytes(raw, "failed_patch_with_events.txt")
        self.assertEqual(report["evidence"]["first_override_latency"]["status"], "FOUND")
        self.assertEqual(report["assessment_status"], "EVIDENCE_FOUND")
        self.assertEqual(report["experiment_qualification"], "INELIGIBLE")

    def test_format_errors_are_rejected(self):
        module = self._module()
        for raw in (
            load("format_error.txt"),
            b"t=NaN|E=A|D=Movement|W=Lab|chg=1|rej=0\n",
            b"t=Infinity|E=A|D=Movement|W=Lab|chg=1|rej=0\n",
            b"t=1.0000|E=A|D=Movement|W=Lab|chg=2|rej=0\n",
            b"t=1.0000|E=A|D=Movement|W=Lab|chg=1|rej=0|E=B\n",
            b"t=1.0000|E=|D=Movement|W=Lab|chg=1|rej=0\n",
            b"t=1.0000|E=A|D=|W=Lab|chg=1|rej=0\n",
            b"t=1.0000|E=A|D=Movement|W=|chg=1|rej=0\n",
            b"t=1.0000|E=A|D=UnknownDomain|W=Lab|chg=1|rej=0\n",
            b"garbage row\n",
            b"SHADOW|t=1.0|E=A|D=Movement|from=HOLD\n",
            b"SHADOW|t=1.0|E=A|D=UnknownDomain|from=HOLD|to=ADVANCE\n",
        ):
            with self.subTest(raw=raw[:40]):
                report = module.recompute_bytes(raw, "bad.txt")
                self.assertEqual(report["assessment_status"], "FORMAT_ERROR")
                self.assertEqual(report["input"]["status"], "FORMAT_ERROR")

    def test_no_shadow_rows_never_claim_x5_estimate(self):
        raw = ("\n".join(load("synthetic_pairs.txt").decode().splitlines()[:2]) + "\n").encode()
        report = self._module().recompute_bytes(raw, "no_shadow.txt")
        x5 = report["evidence"]["x5_coverage_gap_lower_bound"]
        self.assertEqual(x5["status"], "EVIDENCE_INSUFFICIENT")
        self.assertIsNone(x5["count"])

    def test_cli_exit_codes_and_json(self):
        good = subprocess.run([sys.executable, str(SCRIPT), str(FIXTURES / "historical_valid_excerpt.txt")],
                              capture_output=True, text=True, check=False)
        self.assertEqual(good.returncode, 0)
        self.assertEqual(json.loads(good.stdout)["assessment_status"], "NO_RELEVANT_EVIDENCE")
        bad = subprocess.run([sys.executable, str(SCRIPT), str(FIXTURES / "format_error.txt")],
                             capture_output=True, text=True, check=False)
        self.assertNotEqual(bad.returncode, 0)
        self.assertEqual(json.loads(bad.stdout)["assessment_status"], "FORMAT_ERROR")
        missing = subprocess.run([sys.executable, str(SCRIPT), str(FIXTURES / "does_not_exist.txt")],
                                 capture_output=True, text=True, check=False)
        self.assertNotEqual(missing.returncode, 0)
        self.assertEqual(json.loads(missing.stdout)["assessment_status"], "EVENT_FILE_MISSING")

    @staticmethod
    def _module():
        import importlib.util

        spec = importlib.util.spec_from_file_location("recompute_telemetry", SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module


if __name__ == "__main__":
    unittest.main()
