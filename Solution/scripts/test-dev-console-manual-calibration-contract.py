#!/usr/bin/env python3
"""Regression guard: infrastructure startup/reset must not invoke FS calibration."""
from pathlib import Path
import re

script = (Path(__file__).resolve().parent / "dev-console-infrastructure.ps1").read_text(encoding="utf-8")

def body(name: str) -> str:
    match = re.search(r"function\s+" + re.escape(name) + r"\s*\{", script, re.I)
    if not match:
        raise AssertionError(f"Missing {name}")
    start = match.end()
    depth = 1
    for i in range(start, len(script)):
        if script[i] == "{":
            depth += 1
        elif script[i] == "}":
            depth -= 1
            if depth == 0:
                return script[start:i]
    raise AssertionError(f"Unclosed {name}")

for name in ("Invoke-AllStages",):
    assert "Invoke-ModelStage" not in body(name), f"{name} invokes FS calibration"

reset = re.search(r"'reset'\s*\{(?P<body>.*?)\n\s*'clean'\s*\{", script, re.S)
assert reset, "Reset branch missing"
assert "Invoke-ModelStage" not in reset.group("body"), "Reset invokes FS calibration"
assert "Invoke-ModelStage" in script, "Explicit manual model action must remain"
assert re.search(r"'model'\s*\{\s*Invoke-ModelStage\s*\}", script), "Manual model action missing"
print("PASS: startup/reset do not calibrate; manual model action preserved")

reference = body('Invoke-ReferenceStage')
assert 'Assert-FrozenBirthReference' in reference, 'Reference stage must verify frozen demographic snapshot'
assert 'Invoke-ModelStage' not in reference, 'Reference stage must not calibrate'
