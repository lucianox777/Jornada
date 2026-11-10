#!/usr/bin/env bash
# The PO's explicit opt-out for Python SQL memory sizing on manual V9 24x100k.
# Observe RAM/disk only; do NOT export or set a SQL/Docker memory limit.
# SQL will use its native defaults; the CSharp V9 governor remains active.
set -euo pipefail
umask 077

if (( $# != 2 )); then
  echo "V9_100K_SQL_NATIVE_GUARD: invalid invocation" >&2
  exit 2
fi
stage="$1"
output="$2"
if [[ "$stage" != "before-sql" && "$stage" != "before-fit" ]]; then
  echo "V9_100K_SQL_NATIVE_GUARD: invalid stage" >&2
  exit 2
fi
if [[ "${V9_100K_DISABLE_PYTHON_SQL_BUDGET:-}" != "true" ||
      ! "${EXACT_SHA:-}" =~ ^[0-9a-f]{40}$ ||
      "${EXACT_SHA:-}" != "${GITHUB_SHA:-}" ||
      "${GITHUB_REF:-}" != "refs/heads/master" ||
      "${GITHUB_EVENT_NAME:-}" != "workflow_dispatch" ]]; then
  echo "V9_100K_SQL_NATIVE_GUARD: unauthorized context" >&2
  exit 2
fi
# Never silently leave an inherited SQL cap in a request to disable sizing.
if [[ -n "${V9_SQL_MEMORY_LIMIT_MB:-}" || -n "${V9_SQL_CONTAINER_MEMORY_MB:-}" ]]; then
  echo "V9_100K_SQL_NATIVE_GUARD: unexpected SQL memory budget" >&2
  exit 2
fi

# Inspect current Linux runner RAM, bounded cgroup v2 memory and disk without Python.
mem_total_kib="$(awk '$1 == "MemTotal:" {print $2}' /proc/meminfo)"
mem_available_kib="$(awk '$1 == "MemAvailable:" {print $2}' /proc/meminfo)"
disk_free_kib="$(df -Pk "$PWD" | awk 'END {print $4}')"
for value in "$mem_total_kib" "$mem_available_kib" "$disk_free_kib"; do
  if [[ ! "$value" =~ ^[0-9]+$ ]]; then
    echo "V9_100K_SQL_NATIVE_GUARD: unreadable capacity" >&2
    exit 2
  fi
done

effective_available_kib="$mem_available_kib"
cgroup_remaining_json="null"
if [[ -f /sys/fs/cgroup/memory.max ]]; then
  cgroup_limit="$(cat /sys/fs/cgroup/memory.max)"
  if [[ "$cgroup_limit" != "max" ]]; then
    if [[ ! "$cgroup_limit" =~ ^[0-9]+$ || ! -f /sys/fs/cgroup/memory.current ]]; then
      echo "V9_100K_SQL_NATIVE_GUARD: invalid cgroup limit" >&2
      exit 2
    fi
    cgroup_used="$(cat /sys/fs/cgroup/memory.current)"
    if [[ ! "$cgroup_used" =~ ^[0-9]+$ ]]; then
      echo "V9_100K_SQL_NATIVE_GUARD: invalid cgroup consumption" >&2
      exit 2
    fi
    remaining_bytes=0
    if (( cgroup_limit > cgroup_used )); then
      remaining_bytes=$(( cgroup_limit - cgroup_used ))
    fi
    cgroup_remaining_kib=$(( remaining_bytes / 1024 ))
    cgroup_remaining_json="$cgroup_remaining_kib"
    if (( cgroup_remaining_kib < effective_available_kib )); then
      effective_available_kib="$cgroup_remaining_kib"
    fi
  fi
fi

# Not a scientific fitness proof: conservative minimum admission only.
gib_kib=1048576
total_ok=true
memory_ok=true
disk_ok=true
if [[ "$stage" == "before-sql" ]]; then
  (( mem_total_kib >= 12 * gib_kib )) || total_ok=false
  (( effective_available_kib >= 10 * gib_kib )) || memory_ok=false
  (( disk_free_kib >= 6 * gib_kib )) || disk_ok=false
else
  (( effective_available_kib >= 6 * gib_kib )) || memory_ok=false
  (( disk_free_kib >= 3 * gib_kib )) || disk_ok=false
fi
status="PASS_ADMISSION_ONLY_NOT_FIT_PROOF"
if [[ "$total_ok" != "true" || "$memory_ok" != "true" || "$disk_ok" != "true" ]]; then
  status="FAIL_CLOSED_INSUFFICIENT_RESOURCES"
fi

if [[ -e "$output" ]]; then
  echo "V9_100K_SQL_NATIVE_GUARD: output already exists" >&2
  exit 2
fi
mkdir -p "$(dirname "$output")"
(
  set -C
  cat > "$output" <<EOF
{
  "schema": "V9_100K_SQL_MEMORY_NATIVE_PREFLIGHT_V1",
  "stage": "$stage",
  "exactMasterSha": "$EXACT_SHA",
  "mode": "PYTHON_SQL_BUDGET_DISABLED_SQL_DEFAULT",
  "status": "$status",
  "checks": {
    "hostAtLeast12GiBBeforeSql": $total_ok,
    "effectiveRamAdmission": $memory_ok,
    "diskAdmission": $disk_ok
  },
  "resourceSample": {
    "hostRamKiB": $mem_total_kib,
    "hostRamAvailableKiB": $mem_available_kib,
    "effectiveRamAvailableKiB": $effective_available_kib,
    "cgroupRamRemainingKiB": $cgroup_remaining_json,
    "diskFreeKiB": $disk_free_kib
  },
  "sqlMemoryLimitMb": null,
  "sqlContainerMemoryMb": null,
  "workerRamReserveGiB": 6,
  "measurementScope": "CURRENT_LINUX_HOST_CGROUP_AND_DISK_NO_PYTHON_BUDGET",
  "fitted100k": false,
  "certified": false,
  "testUsed": false,
  "goldUsed": false,
  "activated": false
}
EOF
)
echo "V9_100K_SQL_NATIVE_GUARD: $status ($stage; no SQL memory budget set)"
if [[ "$status" != "PASS_ADMISSION_ONLY_NOT_FIT_PROOF" ]]; then
  exit 2
fi
