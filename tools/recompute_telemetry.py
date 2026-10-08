#!/usr/bin/env python3
"""Offline, read-only recomputation of CommandLab telemetry evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any


ATTEMPT_FIELDS = (
    "t", "tick", "E", "D", "P", "W", "O", "L", "lease", "tok",
    "max", "gen", "rgen", "exp", "cmd", "prev", "new", "ome", "chg",
    "rej", "why", "task", "ts", "int", "u", "p",
)
REQUIRED_ATTEMPT_FIELDS = ("t", "E", "D", "W", "chg", "rej")
SHADOW_FIELDS = ("t", "E", "D", "from", "to")
KNOWN_DOMAINS = {"Movement", "Facing", "Arrangement"}


class FormatIssue(ValueError):
    def __init__(self, line_number: int, message: str):
        super().__init__(message)
        self.line_number = line_number
        self.message = message


def fields_from_pipe(line: str, prefix: str, line_number: int) -> dict[str, str]:
    """Parse a prefixed pipe row, rejecting duplicate/malformed fields."""
    pieces = line.split("|")
    if pieces[0] != prefix:
        raise FormatIssue(line_number, f"expected {prefix} row")
    result: dict[str, str] = {}
    for piece in pieces[1:]:
        if "=" not in piece:
            raise FormatIssue(line_number, f"field lacks '=': {piece!r}")
        key, value = piece.split("=", 1)
        if not key or not value:
            raise FormatIssue(line_number, f"empty key or value in {piece!r}")
        if key in result:
            raise FormatIssue(line_number, f"duplicate field {key!r}")
        result[key] = value
    return result


def require_fields(row: dict[str, str], names: tuple[str, ...], line_number: int) -> None:
    missing = [name for name in names if name not in row]
    if missing:
        raise FormatIssue(line_number, f"missing required field(s): {', '.join(missing)}")


def finite_nonnegative(value: str, name: str, line_number: int) -> float:
    # The collector emits invariant-culture F4 decimal numbers. Parsing only t is
    # necessary for ordering and latency; other double fields (such as exp=-Infinity)
    # are deliberately outside this tool's validation scope.
    try:
        number = float(value)
    except ValueError as exc:
        raise FormatIssue(line_number, f"{name} is not a decimal number: {value!r}") from exc
    if not math.isfinite(number) or number < 0:
        raise FormatIssue(line_number, f"{name} must be finite and nonnegative")
    return number


def binary_flag(value: str, name: str, line_number: int) -> bool:
    if value not in ("0", "1"):
        raise FormatIssue(line_number, f"{name} must be 0 or 1")
    return value == "1"


def parse_canonical_attempt(line: str, line_number: int) -> dict[str, Any]:
    pieces = line.split("|")
    if not pieces or not pieces[0].startswith("t="):
        raise FormatIssue(line_number, "expected canonical attempt beginning with t=")
    row: dict[str, str] = {"t": pieces[0][2:]}
    for piece in pieces[1:]:
        if "=" not in piece:
            raise FormatIssue(line_number, f"field lacks '=': {piece!r}")
        key, value = piece.split("=", 1)
        if not key or not value:
            raise FormatIssue(line_number, f"empty key or value in {piece!r}")
        if key in row:
            raise FormatIssue(line_number, f"duplicate field {key!r}")
        row[key] = value
    require_fields(row, REQUIRED_ATTEMPT_FIELDS, line_number)
    t = finite_nonnegative(row["t"], "t", line_number)
    if not row["E"] or not row["D"] or not row["W"]:
        raise FormatIssue(line_number, "E, D, and W must be nonempty")
    if row["D"] not in KNOWN_DOMAINS:
        raise FormatIssue(line_number, f"unsupported D domain: {row['D']!r}")
    changed = binary_flag(row["chg"], "chg", line_number)
    rejected = binary_flag(row["rej"], "rej", line_number)
    return {"line": line_number, "t": t, "E": row["E"], "D": row["D"],
            "W": row["W"], "chg": changed, "rej": rejected}


def parse_shadow(line: str, line_number: int) -> dict[str, Any]:
    row = fields_from_pipe(line, "SHADOW", line_number)
    require_fields(row, SHADOW_FIELDS, line_number)
    if not row["E"] or not row["D"]:
        raise FormatIssue(line_number, "SHADOW E and D must be nonempty")
    if row["D"] not in KNOWN_DOMAINS:
        raise FormatIssue(line_number, f"unsupported SHADOW D domain: {row['D']!r}")
    return {"line": line_number, "t": finite_nonnegative(row["t"], "t", line_number),
            "E": row["E"], "D": row["D"]}


def diagnostic_from_note(line: str, line_number: int) -> str | None:
    if not line.startswith("NOTE|"):
        return None
    message = line.split("|", 2)[-1]
    if "HARMONY_PATCH_FAILED" in message:
        return f"HARMONY_PATCH_FAILED at line {line_number}; experiment qualification is insufficient"
    return None


def recompute_bytes(raw: bytes, path: str) -> dict[str, Any]:
    sha256 = hashlib.sha256(raw).hexdigest()
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        return {
            "schema_version": 1,
            "input": {"path": path, "sha256": sha256, "status": "FORMAT_ERROR"},
            "assessment_status": "FORMAT_ERROR",
            "runtime": "NOT_RUN",
            "statistics": {},
            "evidence": {},
            "diagnostics": [f"input is not UTF-8: byte offset {exc.start}"],
        }

    attempts: list[dict[str, Any]] = []
    shadows: list[dict[str, Any]] = []
    diagnostics: list[str] = []
    patch_failed = False
    accepted_prefixes = ("AUTH|", "TASK|", "HOST|", "CENSUS|", "NOTE|")
    try:
        for line_number, line in enumerate(text.splitlines(), 1):
            if not line.strip():
                continue
            if line.startswith("t="):
                attempts.append(parse_canonical_attempt(line, line_number))
            elif line.startswith("SHADOW|"):
                shadows.append(parse_shadow(line, line_number))
            elif line.startswith(accepted_prefixes):
                note = diagnostic_from_note(line, line_number)
                if note:
                    diagnostics.append(note)
                    patch_failed = True
            else:
                raise FormatIssue(line_number, "unrecognized nonempty row prefix")
    except FormatIssue as exc:
        return {
            "schema_version": 1,
            "input": {"path": path, "sha256": sha256, "status": "FORMAT_ERROR"},
            "assessment_status": "FORMAT_ERROR",
            "runtime": "NOT_RUN",
            "statistics": {},
            "evidence": {},
            "diagnostics": [f"line {exc.line_number}: {exc.message}"],
        }

    accepted_lab = [a for a in attempts if a["W"] == "Lab" and not a["rej"] and a["chg"]]
    rejected_count = sum(a["rej"] for a in attempts)
    effective = [a for a in attempts if not a["rej"] and a["chg"]]

    # Match ProbeRuntime's tracker: remember the latest accepted Lab change for
    # each entity/domain and pair a Native-tagged effective change with it once.
    # File order breaks ties for equal timestamps.
    overrides: list[dict[str, Any]] = []
    latest_lab: dict[tuple[str, str], dict[str, Any]] = {}
    covered_keys: set[tuple[str, str]] = set()
    for attempt in attempts:
        key = (attempt["E"], attempt["D"])
        if attempt["W"] == "Lab" and not attempt["rej"] and attempt["chg"]:
            latest_lab[key] = attempt
        elif (attempt["W"] == "Native" and not attempt["rej"] and attempt["chg"]
              and key in latest_lab and key not in covered_keys
              and attempt["t"] >= latest_lab[key]["t"]):
            lab = latest_lab[key]
            overrides.append({
                "entity": attempt["E"], "domain": attempt["D"],
                "lab_line": lab["line"], "lab_t": lab["t"],
                "native_tagged_line": attempt["line"], "native_tagged_t": attempt["t"],
                "latency_seconds": round(attempt["t"] - lab["t"], 4),
            })
            covered_keys.add(key)

    writes_by_key: dict[tuple[str, str], int] = defaultdict(int)
    for attempt in effective:
        writes_by_key[(attempt["E"], attempt["D"])] += 1
    shadows_by_key: dict[tuple[str, str], int] = defaultdict(int)
    for shadow in shadows:
        shadows_by_key[(shadow["E"], shadow["D"])] += 1
    gap_by_key = {
        f"{entity}|{domain}": max(0, shadow_count - writes_by_key[(entity, domain)])
        for (entity, domain), shadow_count in sorted(shadows_by_key.items())
    }
    x5_gap_lower_bound = sum(gap_by_key.values())
    coverage_evidence_sufficient = bool(shadows) and bool(attempts)
    override_evidence_sufficient = bool(overrides)
    event_file_missing = not attempts

    if event_file_missing:
        assessment = "EVENT_FILE_MISSING"
    elif not accepted_lab:
        assessment = "NO_RELEVANT_EVIDENCE"
    elif not override_evidence_sufficient:
        assessment = "EVIDENCE_INSUFFICIENT"
    else:
        assessment = "EVIDENCE_FOUND"
    qualification = "INELIGIBLE" if patch_failed else "UNKNOWN"
    if shadows and not effective:
        diagnostics.append("no effective WriteAttempt rows; X5 compares SHADOW samples against zero observed effective writes")
    if not shadows:
        diagnostics.append("no SHADOW rows; X5 coverage evidence is unavailable")
    if not accepted_lab:
        diagnostics.append("no accepted effective Lab write; first-override latency cannot be established")
    if accepted_lab and not overrides:
        diagnostics.append("accepted Lab writes have no later same-entity/domain Native-tagged effective write")

    return {
        "schema_version": 1,
        "input": {"path": path, "sha256": sha256, "status": "PRESENT"},
        "assessment_status": assessment,
        "experiment_qualification": qualification,
        "runtime": "NOT_RUN",
        "statistics": {
            "canonical_write_attempts": len(attempts),
            "accepted_effective_lab_writes": len(accepted_lab),
            "rejected_write_attempts": rejected_count,
            "effective_write_attempts_all_writers": len(effective),
            "shadow_change_samples": len(shadows),
        },
        "evidence": {
            "event_file_missing": event_file_missing,
            "first_override_latency": {
                "status": "FOUND" if override_evidence_sufficient else "INSUFFICIENT",
                "first_latency_seconds": overrides[0]["latency_seconds"] if overrides else None,
                "pairs_scope": "first Native-tagged coverage per entity/domain; list order preserves event order",
                "pairs": overrides,
            },
            "x5_coverage_gap_lower_bound": {
                "status": "ESTIMATED" if coverage_evidence_sufficient else "EVIDENCE_INSUFFICIENT",
                "count": x5_gap_lower_bound if coverage_evidence_sufficient else None,
                "shadow_minus_effective_writes_by_entity_domain": gap_by_key,
                "interpretation": "zero means no observed positive gap; it does not prove complete coverage",
            },
        },
        "diagnostics": diagnostics,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Recompute evidence from one offline CommandLab telemetry file")
    parser.add_argument("telemetry_file", help="path to one existing telemetry text file")
    args = parser.parse_args(argv)
    path = Path(args.telemetry_file)
    try:
        raw = path.read_bytes()
    except OSError as exc:
        result = {
            "schema_version": 1,
            "input": {"path": str(path), "sha256": None, "status": "EVENT_FILE_MISSING"},
            "assessment_status": "EVENT_FILE_MISSING",
            "runtime": "NOT_RUN",
            "statistics": {},
            "evidence": {"event_file_missing": True},
            "diagnostics": [f"cannot read telemetry input: {exc.strerror or exc.__class__.__name__}"],
        }
        print(json.dumps(result, indent=2, sort_keys=True))
        return 2
    result = recompute_bytes(raw, str(path))
    print(json.dumps(result, indent=2, sort_keys=True))
    return 1 if result["assessment_status"] == "FORMAT_ERROR" else 0


if __name__ == "__main__":
    sys.exit(main())
