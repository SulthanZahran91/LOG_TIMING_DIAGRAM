import sys
from pathlib import Path

_root = Path(__file__).resolve().parents[3]
_python_reference = _root / "python reference"

python_ref_str = str(_python_reference)
if python_ref_str not in sys.path:
    sys.path.insert(0, python_ref_str)
