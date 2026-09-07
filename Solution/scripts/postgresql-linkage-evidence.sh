#!/usr/bin/env bash
set -euo pipefail

# Run from Solution. Logs and results are restricted to this CI-owned directory.
EVIDENCE="${JORNADA_LINKAGE_EVIDENCE_DIR:-.local/postgresql-linkage-evidence}"
mkdir -p "$EVIDENCE"
EVIDENCE="$(cd "$EVIDENCE" && pwd)"

record() {
    python3 - "$EVIDENCE/results.jsonl" "$1" "$2" "$3" "$4" "$5" <<'PY'
import json, sys
path, name, start, end, duration, code = sys.argv[1:]
with open(path, 'a', encoding='utf-8') as out:
    out.write(json.dumps({'stage': name, 'started_at': start, 'finished_at': end,
                          'duration_seconds': int(duration), 'exit_code': int(code),
                          'status': 'passed' if int(code) == 0 else 'failed'},
                         sort_keys=True) + '\n')
PY
}

case "${1:-}" in
    run)
        shift
        name="${1:?stage name required}"
        shift
        [[ "$name" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo 'Invalid stage name' >&2; exit 2; }
        (($# > 0)) || { echo 'Command required' >&2; exit 2; }
        printf 'started\n' > "$EVIDENCE/runner-started.txt"
        started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        start_seconds="$(date +%s)"
        printf '=== %s started %s ===\n' "$name" "$started"
        set +e
        "$@" 2>&1 | tee "$EVIDENCE/$name.log"
        statuses=("${PIPESTATUS[@]}")
        set -e
        rc="${statuses[0]}"
        if (( rc == 0 && statuses[1] != 0 )); then rc="${statuses[1]}"; fi
        ended="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        record "$name" "$started" "$ended" "$(( $(date +%s) - start_seconds ))" "$rc"
        printf '=== %s finished with exit code %s ===\n' "$name" "$rc"
        exit "$rc"
        ;;
    summary)
        python3 - "$EVIDENCE" <<'PY'
import datetime, json, os, pathlib, sys
root = pathlib.Path(sys.argv[1])
path = root / 'results.jsonl'
rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()] if path.exists() else []
expected = ['environment', 'restore', 'build', 'ddl', 'unit', 'policy', 'integration']
required = os.environ.get('JORNADA_LINKAGE_EVIDENCE_REQUIRED_STAGES', ' '.join(expected)).split()
if (not required or len(required) != len(set(required)) or
        not set(required).issubset(expected) or
        not {'environment', 'restore', 'build'}.issubset(required) or
        not {'unit', 'policy', 'integration'}.intersection(required)):
    raise SystemExit('Invalid required evidence stages')
by_name = {row['stage']: row for row in rows}
checks = []
for name in expected + sorted(set(by_name) - set(expected)):
    row = dict(by_name.get(name, {'stage': name, 'status': 'not_run', 'exit_code': None}))
    row['required'] = name in required
    if name not in by_name and name not in required:
        row['status'] = 'not_applicable'
    checks.append(row)
runner_started = (root / 'runner-started.txt').exists()
passed = (runner_started and all(by_name.get(name, {}).get('status') == 'passed' for name in required)
          and all(row.get('status') == 'passed' and row.get('exit_code') == 0 for row in rows))
summary = {'schema_version': 1, 'commit': os.environ.get('GITHUB_SHA'),
           'run_id': os.environ.get('GITHUB_RUN_ID'), 'run_attempt': os.environ.get('GITHUB_RUN_ATTEMPT'),
           'generated_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
           'runner_started': runner_started, 'required_stages': required,
           'outcome': 'passed' if passed else 'incomplete_or_failed', 'checks': checks}
(root / 'summary.json').write_text(json.dumps(summary, indent=2, sort_keys=True) + '\n', encoding='utf-8')
lines = ['### PostgreSQL Linkage validation', '', f"Commit: `{summary['commit']}`", '',
         '| Stage | Required | Status | Exit code |', '|---|---|---|---|']
lines += [f"| {row['stage']} | {row['required']} | {row['status']} | {row['exit_code'] if row['exit_code'] is not None else '—'} |" for row in checks]
lines += ['', 'Logs, MSBuild binary logs and TRX files are retained in the CI evidence artifact.',
          'If no runner-started marker exists, the job did not reach this script; inspect GitHub job-start diagnostics.']
text = '\n'.join(lines) + '\n'
print(text)
if os.environ.get('GITHUB_STEP_SUMMARY'):
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as out:
        out.write(text)
PY
        ;;
    *) echo 'Usage: postgresql-linkage-evidence.sh run STAGE COMMAND... | summary' >&2; exit 2 ;;
esac
