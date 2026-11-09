#!/usr/bin/env python3
"""Fail-closed standard GitHub runner RAM/disk admission, not a V9 100k PASS."""
import argparse
import json
import os
from pathlib import Path
import shutil

GIB = 1024 ** 3
SCHEMA = "V9_GITHUB_STANDARD_100K_CAPACITY_ADMISSION_V1"


def decide(stage, physical_ram, free_ram, free_disk):
    if stage not in ("before-sql", "before-calibration"):
        raise ValueError("unknown stage")
    if min(physical_ram, free_ram, free_disk) < 0:
        raise ValueError("negative resource sample")
    if stage == "before-sql":
        checks = {"hostRamAtLeast12GiB": physical_ram >= 12 * GIB,
                  "diskFreeAtLeast6GiB": free_disk >= 6 * GIB}
    else:
        checks = {"ramAvailableAtLeast4GiB": free_ram >= 4 * GIB,
                  "diskFreeAtLeast3GiB": free_disk >= 3 * GIB}
    return {
        "schema": SCHEMA, "stage": stage, "conditions": checks,
        "status": ("PASS_ADMISSION_ONLY_NOT_PHYSICAL_PROOF" if all(checks.values())
                   else "FAIL_CLOSED_INSUFFICIENT_RESOURCES"),
        "hostPhysicalRamBytes": physical_ram,
        "hostAvailableRamBytes": free_ram,
        "hostFreeDiskBytes": free_disk,
        "note": ("Empirical admission minima only, NOT proof that SQL+V9 24 fits "
                 "will finish. No change to model, search, 100k corpus or TEST."),
        "testUsed": False, "certified": False, "activated": False,
    }


def linux_meminfo():
    values = {}
    for line in Path("/proc/meminfo").read_text().splitlines():
        if ":" in line:
            key, val = line.split(":", 1)
            values[key] = int(val.strip().split()[0]) * 1024
    return values["MemTotal"], values["MemAvailable"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", choices=("before-sql", "before-calibration"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    total, available = linux_meminfo()
    report = decide(args.stage, total, available, shutil.disk_usage(os.getcwd()).free)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, sort_keys=True, indent=2)
        stream.write("\n")
    print("V9_STANDARD_GITHUB_CAPACITY " + report["status"] + " " + args.stage)
    if report["status"].startswith("FAIL"):
        raise SystemExit(2)


if __name__ == "__main__":
    main()
