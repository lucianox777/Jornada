#!/usr/bin/env python3
"""Live admission and SQL budget for the manual V9 100k campaign ONLY.

No scientific parameter, corpus, historical pin, FDR, TEST or Gold is changed.
The generator-only 100k resource profile is never used as fit memory evidence.
"""
import argparse
import json
import re
import shutil
from pathlib import Path

GIB = 1024 ** 3
MIB = 1024 ** 2
SCHEMA = "V9_100K_EXECUTION_RESOURCE_BUDGET_V1"
SQL_TIER_MB = (2048, 3072, 4096)
WORKER_RESERVE_GIB = 6
HOST_RESERVE_GIB = 1


def require(condition, message):
    if not condition:
        raise ValueError("V9_100K_RESOURCE_FAIL_CLOSED: " + message)


def read_bytes(path):
    path = Path(path)
    if not path.exists():
        return None
    raw = path.read_text(encoding="ascii").strip()
    if raw == "max":
        return None
    require(raw.isdecimal(), "unreadable cgroup memory value")
    return int(raw)


def sample(root):
    mem = {}
    for line in Path("/proc/meminfo").read_text(encoding="ascii").splitlines():
        if ":" in line:
            key, value = line.split(":", 1)
            if key in ("MemTotal", "MemAvailable"):
                mem[key] = int(value.strip().split()[0]) * 1024
    require(set(mem) == {"MemTotal", "MemAvailable"}, "missing Linux memory readings")
    cgroup_max = read_bytes("/sys/fs/cgroup/memory.max")
    cgroup_used = read_bytes("/sys/fs/cgroup/memory.current")
    if cgroup_max is not None:
        require(cgroup_used is not None, "missing cgroup current usage")
        cgroup_remaining = max(0, cgroup_max - cgroup_used)
    else:
        cgroup_remaining = None
    free = shutil.disk_usage(root).free
    available = min(mem["MemAvailable"], cgroup_remaining) if cgroup_remaining is not None else mem["MemAvailable"]
    return {"hostRamBytes": mem["MemTotal"],
            "hostRamAvailableBytes": mem["MemAvailable"],
            "cgroupLimitBytes": cgroup_max,
            "cgroupUsedBytes": cgroup_used,
            "effectiveRamAvailableBytes": available, "diskFreeBytes": free}


def budget(stage, metrics, sha, previous=None):
    require(stage in ("plan-sql", "before-fit"), "invalid stage")
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{40}", sha),
            "exact immutable commit required")
    for key in ("hostRamBytes", "hostRamAvailableBytes",
                "effectiveRamAvailableBytes", "diskFreeBytes"):
        require(type(metrics.get(key)) is int and metrics[key] >= 0, "invalid " + key)
    available = metrics["effectiveRamAvailableBytes"]
    disk = metrics["diskFreeBytes"]
    if stage == "plan-sql":
        # Prefer less SQL memory on standard runners; a larger budget is
        # selected only on hosts with measured extra headroom, never by SKU.
        sql_mb = (4096 if available >= 26 * GIB else
                  3072 if available >= 18 * GIB else 2048)
        container_mb = sql_mb + 1024
        conditions = {
            "hostAtLeast12GiB": metrics["hostRamBytes"] >= 12 * GIB,
            "diskAtLeast6GiB": disk >= 6 * GIB,
            "reserveWorker6GiBPlusHost1GiBAfterSqlContainer":
                available >= (WORKER_RESERVE_GIB + HOST_RESERVE_GIB) * GIB + container_mb * MIB,
        }
    else:
        require(isinstance(previous, dict) and previous.get("schema") == SCHEMA
                and previous.get("stage") == "plan-sql"
                and previous.get("exactMasterSha") == sha
                and previous.get("status") == "PASS_ADMISSION_ONLY_NOT_FIT_PROOF",
                "missing successful same-SHA SQL budget receipt")
        sql_mb = previous.get("sqlMemoryLimitMb")
        container_mb = previous.get("sqlContainerMemoryMb")
        require(type(sql_mb) is int and sql_mb in SQL_TIER_MB
                and type(container_mb) is int and container_mb == sql_mb + 1024,
                "malformed SQL memory receipt")
        conditions = {"diskAtLeast3GiB": disk >= 3 * GIB,
                      "workerRamAvailableAtLeast6GiB": available >= WORKER_RESERVE_GIB * GIB}
    return {"schema": SCHEMA, "stage": stage, "exactMasterSha": sha,
            "status": ("PASS_ADMISSION_ONLY_NOT_FIT_PROOF" if all(conditions.values())
                       else "FAIL_CLOSED_INSUFFICIENT_RESOURCES"),
            "checks": conditions, "resourceSample": metrics,
            "sqlMemoryLimitMb": sql_mb, "sqlContainerMemoryMb": container_mb,
            "workerRamReserveGiB": WORKER_RESERVE_GIB,
            "measurementScope": "LIVE_RUNNER_AND_CGROUP_IF_BOUNDED_NOT_GENERATOR_PROFILE",
            "historical100kGeneratorResourceProfileReused": False,
            "fitted100k": False, "certified": False,
            "testUsed": False, "goldUsed": False, "activated": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", choices=("plan-sql", "before-fit"), required=True)
    parser.add_argument("--exact-sha", required=True)
    parser.add_argument("--resource-root", type=Path, default=Path.cwd())
    parser.add_argument("--previous", type=Path)
    parser.add_argument("--github-env", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require(args.stage != "before-fit" or args.previous is not None,
            "before-fit requires same-SHA receipt")
    require(args.stage != "before-fit" or args.github_env is None,
            "before-fit must never change an already running SQL budget")
    previous = json.loads(args.previous.read_text(encoding="utf-8")) if args.previous else None
    result = budget(args.stage, sample(args.resource_root), args.exact_sha, previous)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, sort_keys=True, indent=2)
        stream.write("\n")
    print("V9_100K_LIVE_RESOURCES " + result["status"] + " " + args.stage)
    if result["status"].startswith("FAIL"):
        raise SystemExit(2)
    if args.github_env:
        require(args.stage == "plan-sql", "only plan-sql may set SQL budget")
        with args.github_env.open("a", encoding="utf-8") as stream:
            stream.write("V9_SQL_MEMORY_LIMIT_MB=" + str(result["sqlMemoryLimitMb"]) + "\n")
            stream.write("V9_SQL_CONTAINER_MEMORY_MB=" + str(result["sqlContainerMemoryMb"]) + "\n")


if __name__ == "__main__":
    main()
