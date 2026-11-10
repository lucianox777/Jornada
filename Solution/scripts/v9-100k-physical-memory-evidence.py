#!/usr/bin/env python3
"""Read-only, bounded memory evidence while running the manual V9 physical worker.

Only aggregate KiB, OOM counters and booleans are recorded. Never log Docker
inspect JSON, process argv, exception text, credentials or environment values.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

SCHEMA = "V9_100K_PHYSICAL_MEMORY_EVIDENCE_V1"
UNITS = {"B": 1, "kB": 1000, "MB": 1000**2, "GB": 1000**3,
         "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "TiB": 1024**4}

def parse_docker_usage(text):
    # Docker stats prints e.g. "1.23GiB / 15.6GiB". Never retain raw text.
    match = re.fullmatch(r"\s*([0-9]+(?:\.[0-9]+)?)\s*(B|kB|MB|GB|KiB|MiB|GiB|TiB)\s*/.*", text)
    return int(float(match.group(1)) * UNITS[match.group(2)]) // 1024 if match else None

def mem_available_kib():
    try:
        for line in Path("/proc/meminfo").read_text(encoding="ascii").splitlines():
            if line.startswith("MemAvailable:"):
                return int(line.split()[1])
    except (OSError, ValueError, IndexError):
        pass
    return None

def oom_events():
    try:
        lines = Path("/sys/fs/cgroup/memory.events").read_text(encoding="ascii").splitlines()
        values = {k: int(v) for k, v in (line.split() for line in lines)}
        return {k: values.get(k) for k in ("oom", "oom_kill")}
    except (OSError, ValueError):
        return {"oom": None, "oom_kill": None}

def process_tree_rss_kib(pid):
    seen = set()
    pending = [pid]
    total = 0
    while pending:
        current = pending.pop()
        if current in seen:
            continue
        seen.add(current)
        try:
            for line in Path(f"/proc/{current}/status").read_text().splitlines():
                if line.startswith("VmRSS:"):
                    total += int(line.split()[1])
                    break
            children = Path(f"/proc/{current}/task/{current}/children").read_text().split()
            pending.extend(int(x) for x in children)
        except (OSError, ValueError, IndexError):
            continue
    return total or None

def docker_metrics(container):
    try:
        stats = subprocess.run(["docker", "stats", "--no-stream", "--format",
                                "{{.MemUsage}}", container], text=True,
                               capture_output=True, timeout=8, check=False)
        if stats.returncode != 0:
            return None
        return parse_docker_usage(stats.stdout.strip())
    except (OSError, subprocess.TimeoutExpired):
        return None

def docker_oom_killed(container):
    try:
        info = subprocess.run(["docker", "inspect", "--format",
                               "{{.State.OOMKilled}}", container], text=True,
                              capture_output=True, timeout=5, check=False)
        return info.stdout.strip() == "true" if info.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sql-container", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--interval-seconds", type=float, default=10)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command or args.interval_seconds < 2 or args.interval_seconds > 60:
        parser.error("invalid command or sampling interval")
    if not re.fullmatch(r"v9-probe-sql-[0-9]+-[0-9]+", args.sql_container):
        parser.error("invalid disposable SQL container name")
    if args.output.exists():
        parser.error("evidence output already exists")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    start_events = oom_events()
    before = mem_available_kib()
    samples = 0
    sql_max = None
    worker_max = None
    host_min = before
    child = subprocess.Popen(command)
    try:
        while child.poll() is None:
            sql = docker_metrics(args.sql_container)
            worker = process_tree_rss_kib(child.pid)
            available = mem_available_kib()
            samples += 1
            if sql is not None: sql_max = max(sql_max or 0, sql)
            if worker is not None: worker_max = max(worker_max or 0, worker)
            if available is not None: host_min = min(host_min if host_min is not None else available, available)
            if child.poll() is None:
                time.sleep(args.interval_seconds)
    except KeyboardInterrupt:
        child.terminate()
        child.wait()
    exit_code = child.wait()
    finish_events = oom_events()
    deltas = {
        k: max(0, finish_events[k] - start_events[k])
        if finish_events[k] is not None and start_events[k] is not None else None
        for k in ("oom", "oom_kill")
    }
    receipt = {
        "schema": SCHEMA,
        "scope": "READ_ONLY_RUNTIME_RESOURCE_EVIDENCE_NOT_CAUSAL_PROOF",
        "workerExitCode": exit_code,
        "samples": samples,
        "peakSqlContainerMemoryKiB": sql_max,
        "peakWorkerProcessTreeRssKiB": worker_max,
        "minHostMemAvailableKiB": host_min,
        "cgroupOomEventDelta": deltas["oom"],
        "cgroupOomKillDelta": deltas["oom_kill"],
        "sqlContainerOomKilled": docker_oom_killed(args.sql_container),
        "sqlMemoryLimitChanged": False,
        "scientificPlanChanged": False,
        "testUsed": False, "goldUsed": False, "activated": False
    }
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, sort_keys=True, indent=2)
        stream.write("\n")
    marker = ("OOM_SIGNAL_OBSERVED" if (deltas["oom_kill"] or 0) > 0 or
              receipt["sqlContainerOomKilled"] is True else
              "NO_OOM_SIGNAL_OBSERVED_NOT_PROOF_OF_ABSENCE")
    print("V9_PHYSICAL_MEMORY_EVIDENCE:" + marker + "; worker_exit=" + str(exit_code), flush=True)
    return exit_code

if __name__ == "__main__":
    sys.exit(main())
