"""Compatibility layer exposing utils under the historical namespace."""

from importlib import import_module
from types import ModuleType


def __getattr__(name: str) -> ModuleType:
    """Lazy-load utility modules from the plc_visualizer package."""
    module = import_module(f"plc_visualizer.utils.{name}")
    globals()[name] = module
    return module


__all__ = [
    "viewport_state",
]
