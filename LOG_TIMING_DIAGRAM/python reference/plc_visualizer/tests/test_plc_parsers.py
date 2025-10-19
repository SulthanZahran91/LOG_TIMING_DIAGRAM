"""Parity tests for PLC parser outputs against shipped sample logs."""

from __future__ import annotations

from pathlib import Path

import pytest

from plc_visualizer.parsers import parser_registry


def _root_dir() -> Path:
    return Path(__file__).resolve().parents[4]


def _logs_dir() -> Path:
    root = _root_dir()
    logs = root / "generated"
    if not logs.exists():
        pytest.skip("Sample logs directory missing.")
    return logs


def _parse(log_path: Path, parser_name: str):
    result = parser_registry.parse(str(log_path), parser_name=parser_name, num_workers=1)
    assert result.success, f"{log_path.name} failed to parse: {result.errors}"
    assert result.data is not None
    assert result.errors == [], f"{log_path.name} produced errors: {result.errors}"
    assert result.data.entry_count > 0
    return result


@pytest.mark.parametrize(
    ("filename", "parser_name"),
    [
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
    ],
)
def test_sample_logs_parse_without_errors(filename: str, parser_name: str) -> None:
    logs_dir = _logs_dir()
    log_path = logs_dir / filename
    if not log_path.exists():
        pytest.skip(f"{filename} not present in generated sample set.")

    result = _parse(log_path, parser_name)

    # Spot-check first entry characteristics to ensure consistent behavior.
    first = result.data.entries[0]
    assert first.timestamp.year >= 2023
    assert isinstance(first.device_id, str) and first.device_id
    assert isinstance(first.signal_name, str) and first.signal_name
    assert first.signal_type.name in {"BOOLEAN", "INTEGER", "STRING"}

    # Ensure unique signal keys are composed as expected (device::signal).
    for key in result.data.signals:
        assert "::" in key, f"Signal key {key!r} missing :: separator"
