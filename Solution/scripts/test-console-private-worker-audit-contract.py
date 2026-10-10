#!/usr/bin/env python3
"""C3.3d: negative contract for isolated append-only worker journal (offline)."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
JOURNAL=ROOT/"src/Jornada.DevConsole/IsolatedWorkerAuditJournal.cs"
CTRL=ROOT/"src/Jornada.DevConsole/IsolatedWorkerSupervisorModeController.cs"
API=ROOT/"src/Jornada.DevConsole/Program.cs"
BOOT=ROOT/"scripts/e2e-private-sql-bootstrap-runtime.sh"
STOP=ROOT/"scripts/e2e-console-worker-stop-proof.py"
STATUS=ROOT/"src/Jornada.DevConsole/IsolatedWorkerSupervisorStatusReader.cs"

def main():
    j=JOURNAL.read_text(encoding="utf-8")
    c=CTRL.read_text(encoding="utf-8")
    p=API.read_text(encoding="utf-8")
    b=BOOT.read_text(encoding="utf-8")
    for token in (
        "reader.Enabled(runtime)",
        "GITHUB_RUN_ID","GITHUB_RUN_ATTEMPT",
        'new(["processor", "operations-maintenance", "bronze-maintenance"]',
        'new(["ADMITIDO", "SUCESSO", "ERRO"]',
        'category is "SUPERVISAO"',
        'category is "RUN_ONCE" or "PARAR_PROCESSO"',
        '"c3-3d-private-worker-audit"',
        '"events.jsonl"',
        "FileMode.Append","FileOptions.WriteThrough",
        "stream.Flush(flushToDisk: true)",
        "lock (gate)",
        "DateTimeOffset.UtcNow",
        "JsonSerializer.Serialize",
    ):
        assert token in j, f"journal guard or storage missing: {token}"
    assert "ConsolePrivateWorkerAudit" not in j or "new FileStream" in j
    for secret in ("cpf", "nome_mae", "ConnectionStrings", "SqlConnection",
                   "request.Query", "request.Body", "HttpContext",
                   "DockerHost", "JornadaLocal", "prune", "down -v"):
        assert secret not in j, f"journal contains forbidden sensitive/host access: {secret}"
    for category in ("SUPERVISAO","RUN_ONCE","PARAR_PROCESSO"):
        for outcome in ("ADMITIDO","SUCESSO","ERRO"):
            assert f'audit.Record("{category}"' in c, f"missing live audit category {category}"
            assert f'"{outcome}"' in c
    assert "IsolatedWorkerAuditJournal audit" in c
    assert "builder.Services.AddSingleton<IsolatedWorkerAuditJournal>()" in p
    assert 'C3.3d: PASS private write-through journal survived Console processes' in b
    assert '"timestampUtc","project","category","operation","outcome"' in b
    assert "PARAR_PROCESSO" in b and "RUN_ONCE" in b
    # Only the DevConsole controller writes worker-audit success. A direct
    # subprocess invoking Docker CLI proves stop but bypasses the journal.
    stop=STOP.read_text(encoding="utf-8")
    compile(stop, str(STOP), "exec")
    assert '"/api/workers/{worker}/stop"' in stop
    assert '"POST"' in stop and '"confirmed": True' in stop
    assert '"confirmed": False' in stop
    assert 'result.get("mode") == "ERRO"' in stop
    assert 'after[worker]["state"] == "PARADO"' in stop
    assert 'not ready' in stop and 'console.terminate()' in stop
    # Docker ps -aq emits 12-char IDs. A confirmed HTTP stop requires a
    # full 64-char immutable ID, resolved by verified Docker inspect.
    status=STATUS.read_text(encoding="utf-8")
    assert 'var fullId = value.GetProperty("Id").GetString();' in status
    assert 'fullId.Length != 64' in status
    assert 'fullId.StartsWith(id, StringComparison.Ordinal)' in status
    assert 'new DockerServiceSnapshot(\n            fullId' in status
    assert 'members[worker]["containerId"] == cid' in stop
    print("C3.3d: PASS private allowlisted audit journal guard/append (offline)")

if __name__=="__main__":
    main()
