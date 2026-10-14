"""Conservative validation-only selection guard for V9 experiments."""
from pathlib import Path
import json

def validate_snapshot(directory):
    root = Path(directory)
    manifest = root / "manifest.json"
    if not manifest.is_file():
        raise ValueError("missing snapshot manifest")
    info = json.loads(manifest.read_text(encoding="utf-8"))
    required = ("canonical_observations.csv", "train_name_frequencies.json",
                "train_joint_name_frequencies.json")
    if any(not (root / name).is_file() for name in required):
        raise ValueError("missing frozen frequency artifact")
    if info.get("canonical_projection_parity_verified") is not True:
        raise ValueError("canonical C# projection parity is not certified")
    return info

def choose(arms, validation_metrics, certified):
    if not certified:
        raise ValueError("statistical certification required")
    if not arms or any(arm not in validation_metrics for arm in arms):
        raise ValueError("missing validation evidence")
    raise ValueError("no automatic strategy selection before FDR and cost policy is implemented")
