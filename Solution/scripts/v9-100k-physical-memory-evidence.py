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

def resolve_runner_cgroup(proc_cgroup_text, mountinfo_text):
    """Return cgroup-v2 directory only when /proc path and mount root reconcile."""
    memberships = [line.split("::", 1)[1] for line in proc_cgroup_text.splitlines()
                   if line.startswith("0::")]
    if len(memberships) != 1:
        return None
    member = Path(memberships[0])
    if not member.is_absolute():
        return None
    for line in mountinfo_text.splitlines():
        left, sep, right = line.partition(" - ")
        if not sep or not right.startswith("cgroup2 "):
            continue
        fields = left.split()
        if len(fields) < 5:
            continue
        root, mountpoint = Path(fields[3]), Path(fields[4])
        try:
            relative = member.relative_to(root)
        except ValueError:
            continue
        return mountpoint / relative
    return None


def runner_cgroup_directory():
    try:
        directory = resolve_runner_cgroup(
            Path("/proc/self/cgroup").read_text(encoding="ascii"),
            Path("/proc/self/mountinfo").read_text(encoding="ascii"))
        if directory is not None and (directory / "memory.events").is_file():
            return directory
    except (OSError, ValueError):
        pass
    return None


def oom_events(cgroup_directory=None):
    try:
        directory = cgroup_directory if cgroup_directory is not None else runner_cgroup_directory()
        if directory is None:
            return {"oom": None, "oom_kill": None}
        lines = (directory / "memory.events").read_text(encoding="ascii").splitlines()
        values = {}
        for line in lines:
            key, value = line.split()
            if key in ("oom", "oom_kill") and re.fullmatch(r"[0-9]+", value):
                values[key] = int(value)
        return {key: values.get(key) for key in ("oom", "oom_kill")}
    except (OSError, ValueError):
        return {"oom": None, "oom_kill": None}


def parse_proc_stat_ppid(stat):
    # /proc/PID/stat comm may contain spaces and parentheses; parse after LAST ')'.
    right = stat.rfind(")")
    if right < 0:
        return None
    fields = stat[right + 1:].split()
    if len(fields) < 2 or not fields[1].isdecimal():
        return None
    return int(fields[1])


def process_tree_rss_kib(pid, proc_root=Path("/proc")):
    # Find ALL descendants by PPID, not the thread-main-only /children file.
    children = {}
    rss = {}
    for directory in proc_root.iterdir():
        if not directory.name.isdecimal():
            continue
        try:
            child_pid = int(directory.name)
            parent_pid = parse_proc_stat_ppid((directory / "stat").read_text())
            if parent_pid is None:
                continue
            children.setdefault(parent_pid, []).append(child_pid)
            for line in (directory / "status").read_text().splitlines():
                if line.startswith("VmRSS:"):
                    rss[child_pid] = int(line.split()[1])
                    break
        except (OSError, ValueError, IndexError):
            continue
    pending = [pid]
    seen = set()
    total = 0
    while pending:
        current = pending.pop()
        if current in seen:
            continue
        seen.add(current)
        total += rss.get(current, 0)
        pending.extend(children.get(current, []))
    return total if any(x in rss for x in seen) else None


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


def parse_sql_cgroup_snapshot(raw):
    # Aggregate-only, cgroup v2. Unknown/missing keys never mean zero OOM.
    parts = raw.split("PEAK_KIB:", 1)
    if len(parts) != 2:
        return None
    try:
        counters = {}
        for line in parts[0].splitlines():
            name, value = line.split()
            if name in ("oom", "oom_kill") and re.fullmatch(r"[0-9]+", value):
                counters[name] = int(value)
        peak = parts[1].strip()
        if not re.fullmatch(r"[0-9]+", peak):
            return None
        if "oom" not in counters or "oom_kill" not in counters:
            return None
        return {"oom": counters["oom"], "oom_kill": counters["oom_kill"],
                "peakKiB": int(peak) // 1024}
    except ValueError:
        return None

def sql_cgroup_snapshot(container):
    # Read only fixed cgroup files inside SQL. No shell expansions, env or inspect
    # payloads are printed; unavailable cgroup-v2 capability stays unknown.
    try:
        probe = subprocess.run(["docker", "exec", container, "sh", "-c",
                                "cat /sys/fs/cgroup/memory.events && "
                                "printf 'PEAK_KIB:' && cat /sys/fs/cgroup/memory.peak"],
                               text=True, capture_output=True, timeout=5,
                               check=False)
        if probe.returncode != 0:
            return None
        return parse_sql_cgroup_snapshot(probe.stdout)
    except (OSError, subprocess.TimeoutExpired):
        return None

def parse_positive_limit(raw):
    value = raw.strip()
    if value == "max":
        return None
    return int(value) if re.fullmatch(r"[0-9]+", value) and int(value) > 0 else None


def sql_memory_limits(container):
    """Numeric, allowlisted configuration only. Unknown != unlimited."""
    values = {"sqlMemoryMemorylimitMb": None, "sqlEnvironmentMemoryLimitMb": None, "sqlDockerHardLimitKiB": None,
              "sqlCgroupMemoryMaxKiB": None}
    # SQL Server's mssql-conf setting, if explicit. No raw stdout in receipt.
    try:
        cmd = subprocess.run(
            ["docker", "exec", container, "/opt/mssql/bin/mssql-conf",
             "get", "memory.memorylimitmb"],
            capture_output=True, text=True, timeout=6, check=False)
        if cmd.returncode == 0:
            numeric = re.findall(r"(?<![A-Za-z0-9])([0-9]{3,7})(?![A-Za-z0-9])", cmd.stdout)
            if len(numeric) == 1:
                values["sqlMemoryMemorylimitMb"] = int(numeric[0])
    except (OSError, subprocess.TimeoutExpired):
        pass
    # Read numeric SQL env override only; do not publish raw environment.
    try:
        cmd = subprocess.run(
            ["docker", "exec", container, "sh", "-c",
             'printf "%s" "${MSSQL_MEMORY_LIMIT_MB:-unset}"'],
            capture_output=True, text=True, timeout=5, check=False)
        if cmd.returncode == 0:
            value = parse_positive_limit(cmd.stdout)
            if value is not None:
                values["sqlEnvironmentMemoryLimitMb"] = value
    except (OSError, subprocess.TimeoutExpired):
        pass
    # Docker's configured hard limit is zero if not explicitly set.
    try:
        cmd = subprocess.run(
            ["docker", "inspect", "--format", "{{.HostConfig.Memory}}", container],
            capture_output=True, text=True, timeout=5, check=False)
        if cmd.returncode == 0:
            bytes_limit = parse_positive_limit(cmd.stdout)
            if bytes_limit is not None:
                values["sqlDockerHardLimitKiB"] = bytes_limit // 1024
    except (OSError, subprocess.TimeoutExpired):
        pass
    # Actual kernel cgroup-v2 memory.max, independently from Docker metadata.
    try:
        cmd = subprocess.run(
            ["docker", "exec", container, "cat", "/sys/fs/cgroup/memory.max"],
            capture_output=True, text=True, timeout=5, check=False)
        if cmd.returncode == 0:
            bytes_limit = parse_positive_limit(cmd.stdout)
            if bytes_limit is not None:
                values["sqlCgroupMemoryMaxKiB"] = bytes_limit // 1024
    except (OSError, subprocess.TimeoutExpired):
        pass
    return values


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
    runner_cgroup = runner_cgroup_directory()
    start_events = oom_events(runner_cgroup) if runner_cgroup else {"oom": None, "oom_kill": None}
    before = mem_available_kib()
    samples = 0
    sql_max = None
    sql_oom_observed = False
    sql_inspect_samples = 0
    sql_cgroup_first = None
    sql_limits = None
    sql_cgroup_last = None
    sql_cgroup_samples = 0
    sql_cgroup_oom_delta_max = None
    sql_cgroup_oom_kill_delta_max = None
    sql_cgroup_peak_kib = None
    worker_max = None
    host_min = before
    child = subprocess.Popen(command)
    # SQL memory configuration is a point-in-time snapshot, not a 10-s query.
    sql_limits = sql_memory_limits(args.sql_container)
    try:
        while child.poll() is None:
            sql = docker_metrics(args.sql_container)
            sql_oom = docker_oom_killed(args.sql_container)
            if sql_oom is not None:
                sql_inspect_samples += 1
                sql_oom_observed = sql_oom_observed or sql_oom
            cg = sql_cgroup_snapshot(args.sql_container)
            if cg is not None:
                sql_cgroup_samples += 1
                if sql_cgroup_first is None:
                    sql_cgroup_first = cg
                sql_cgroup_last = cg
                sql_cgroup_peak_kib = max(sql_cgroup_peak_kib or 0, cg["peakKiB"])
                sql_cgroup_oom_delta_max = max(
                    sql_cgroup_oom_delta_max or 0,
                    max(0, cg["oom"] - sql_cgroup_first["oom"]))
                sql_cgroup_oom_kill_delta_max = max(
                    sql_cgroup_oom_kill_delta_max or 0,
                    max(0, cg["oom_kill"] - sql_cgroup_first["oom_kill"]))
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
    finish_events = oom_events(runner_cgroup) if runner_cgroup else {"oom": None, "oom_kill": None}
    final_sql_oom = docker_oom_killed(args.sql_container)
    if final_sql_oom is not None:
        sql_inspect_samples += 1
        sql_oom_observed = sql_oom_observed or final_sql_oom
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
        "runnerCgroupV2Resolved": runner_cgroup is not None,
        "cgroupOomEventDelta": deltas["oom"],
        "cgroupOomKillDelta": deltas["oom_kill"],
        "sqlContainerOomKilled": True if sql_oom_observed else (False if sql_inspect_samples else None),
        "sqlContainerOomSamples": sql_inspect_samples,
        "sqlMemoryMemorylimitMb": (sql_limits or {}).get("sqlMemoryMemorylimitMb"),
        "sqlEnvironmentMemoryLimitMb": (sql_limits or {}).get("sqlEnvironmentMemoryLimitMb"),
        "sqlDockerHardLimitKiB": (sql_limits or {}).get("sqlDockerHardLimitKiB"),
        "sqlCgroupMemoryMaxKiB": (sql_limits or {}).get("sqlCgroupMemoryMaxKiB"),
        "sqlMemoryConfigurationScope": "EXPLICIT_SETTINGS_ONLY_NULL_MEANS_UNKNOWN_OR_NATIVE_DEFAULT",
        "sqlCgroupV2ValidSamples": sql_cgroup_samples,
        "sqlCgroupV2MemoryPeakKiB": sql_cgroup_peak_kib,
        "sqlCgroupV2OomEventDeltaObserved": sql_cgroup_oom_delta_max,
        "sqlCgroupV2OomKillDeltaObserved": sql_cgroup_oom_kill_delta_max,
        "sqlMemoryLimitChanged": False,
        "scientificPlanChanged": False,
        "testUsed": False, "goldUsed": False, "activated": False
    }
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, sort_keys=True, indent=2)
        stream.write("\n")
    marker = ("OOM_SIGNAL_OBSERVED" if (sql_cgroup_oom_kill_delta_max or 0) > 0 or (deltas["oom_kill"] or 0) > 0 or
              receipt["sqlContainerOomKilled"] is True else
              "NO_OOM_SIGNAL_OBSERVED_NOT_PROOF_OF_ABSENCE")
    print("V9_PHYSICAL_MEMORY_EVIDENCE:" + marker + "; worker_exit=" + str(exit_code), flush=True)
    return exit_code

if __name__ == "__main__":
    sys.exit(main())
