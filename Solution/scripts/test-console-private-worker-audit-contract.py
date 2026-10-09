#!/usr/bin/env python3
"""C3.3d: negative contract for isolated append-only worker journal (offline)."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
JOURNAL=ROOT/"src/Jornada.DevConsole/IsolatedWorkerAuditJournal.cs"
CTRL=ROOT/"src/Jornada.DevConsole/IsolatedWorkerSupervisorModeController.cs"
API=ROOT/"src/Jornada.DevConsole/Program.cs"
BOOT=ROOT/"scripts/e2e-private-sql-bootstrap-runtime.sh"

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
    print("C3.3d: PASS private allowlisted audit journal guard/append (offline)")

if __name__=="__main__":
    main()
