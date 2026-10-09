#!/usr/bin/env python3
"""C3.2f2c preparation: static-only guard; never runs SQL or Docker."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
FILE = ROOT / "scripts/e2e-private-recovery-transaction-hold.sql"

def main():
    text = FILE.read_text(encoding="utf-8")
    assert "DB_NAME() <> N'JornadaE2E'" in text
    assert "Jornada.EnvironmentProfile" in text and "Development" in text
    assert "silver.pessoa_observacao" in text and "identidade.vinculo_fonte" in text
    assert "CREATE OR ALTER TRIGGER identidade.tr_ci_c3_2f2c_hold_first_write" in text
    assert "FROM inserted i" in text
    assert "JOIN silver.pessoa_observacao p ON p.pessoa_observacao_id = i.pessoa_observacao_id" in text
    assert "JOIN ingestao.lote l ON l.lote_id = p.lote_id" in text
    assert "JOIN ingestao.entrega e ON e.entrega_id = l.entrega_id" in text
    assert "e.idempotency_key = N'$(RecoveryKey)'" in text
    assert "ci-e2e-recovery-" in text
    assert re.search(r"IF @attempt = 1\s+WAITFOR DELAY", text)
    assert re.search(r"ELSE IF @attempt >= 2\s+WAITFOR DELAY", text)
    assert text.count("WAITFOR DELAY") == 2
    # Silver persistence uses INSERT ... OUTPUT INSERTED without INTO; an
    # AFTER INSERT trigger ON Silver would break the SQL Server statement.
    assert "ON identidade.vinculo_fonte\\nAFTER INSERT" in text
    assert "ON silver.pessoa_observacao\\nAFTER INSERT" not in text
    executable = "\n".join(line for line in text.splitlines()
                           if not line.lstrip().startswith("--"))
    for dangerous in ("JornadaLocal", "DROP DATABASE", "RESTORE DATABASE",
                      "ALTER DATABASE", "TRUNCATE TABLE", "DELETE FROM", "UPDATE "):
        assert dangerous not in executable
    assert "CREATE OR ALTER TRIGGER" in text and text.count("\nGO\n") >= 2
    print("C3.2f2c: static private transaction barrier contract PASS (NO DB)")

if __name__ == "__main__":
    main()
