#!/usr/bin/env python3
"""Lightweight parity check runner for the Python reference parsers.

This avoids external test dependencies so it can run in restricted environments.
"""

from __future__ import annotations

import sys
from pathlib import Path


def main() -> int:
    repo_root = Path(__file__).resolve().parents[1]
    logs_dir = repo_root / "generated"
    if not logs_dir.exists():
        print("[error] generated/ directory missing; nothing to validate.", file=sys.stderr)
        return 1

    python_ref_dir = Path(__file__).resolve().parent
    sys.path.insert(0, str(python_ref_dir))

    try:
        from plc_visualizer.parsers import parser_registry  # type: ignore
    except Exception as exc:  # pragma: no cover - diagnostic
        print(f"[error] Failed to import parser registry: {exc}", file=sys.stderr)
        return 1

    test_matrix = [
        ("plc_debug_parser_01.log", "plc_debug"),
        ("plc_debug_parser_02.log", "plc_debug"),
        ("plc_debug_parser_03.log", "plc_debug"),
        ("plc_debug_parser_04.log", "plc_debug"),
        ("plc_debug_parser_05.log", "plc_debug"),
        ("plc_tab_parser_01.log", "plc_tab"),
        ("plc_tab_parser_02.log", "plc_tab"),
        ("plc_tab_parser_03.log", "plc_tab"),
        ("plc_tab_parser_04.log", "plc_tab"),
        ("plc_tab_parser_05.log", "plc_tab"),
    ]

    failures: list[str] = []

    for filename, parser_name in test_matrix:
        log_path = logs_dir / filename
        if not log_path.exists():
            print(f"[skip] {filename} not present; skipping.")
            continue

        try:
            result = parser_registry.parse(str(log_path), parser_name=parser_name, num_workers=1)
        except Exception as exc:
            failures.append(f"{filename}: parser threw {exc!r}")
            continue

        if not getattr(result, "success", False) or result.data is None:
            failures.append(f"{filename}: parse reported failure ({result.errors})")
            continue

        if result.errors:
            failures.append(f"{filename}: encountered errors {result.errors}")
            continue

        if not result.data.entries:
            failures.append(f"{filename}: no entries returned")
            continue

        first = result.data.entries[0]
        if not getattr(first, "device_id", ""):
            failures.append(f"{filename}: first entry missing device id")
        if any("::" not in key for key in result.data.signals):
            failures.append(f"{filename}: at least one signal key missing '::'")

        if not getattr(first, "signal_name", ""):
            failures.append(f"{filename}: first entry missing signal name")

        print(f"[ok] {filename}: {len(result.data.entries)} entries, {len(result.data.signals)} signals")

    if failures:
        print("[summary] failures detected:", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    print("[summary] all sample logs parsed successfully.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
