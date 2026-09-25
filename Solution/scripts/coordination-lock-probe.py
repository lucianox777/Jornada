#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import subprocess
import time
import uuid
from pathlib import Path

RESOURCES = (
    "Jornada.Pipeline.ExclusiveRequest",
    "Jornada.Pipeline.Corpus",
)
MIN_WAIT_FRACTION = 0.50


def load_env(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        result[key.strip()] = value.strip()
    return result


def sqlcmd(root: Path, database: str, password: str, query: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            "docker", "compose", "--env-file", str(root / ".env"),
            "exec", "-T", "-e", "SQLCMDPASSWORD",
            "sqlserver", "/opt/mssql-tools18/bin/sqlcmd",
            "-S", "localhost", "-U", "sa", "-C", "-b",
            "-d", database, "-W", "-h", "-1", "-Q", query,
        ],
        cwd=root,
        env={**os.environ, "SQLCMDPASSWORD": password},
        text=True,
        capture_output=True,
        check=False,
    )


def scalar(root: Path, database: str, password: str, query: str) -> str:
    completed = sqlcmd(root, database, password, "SET NOCOUNT ON; " + query)
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or completed.stdout.strip() or "sqlcmd falhou")
    lines = [line.strip() for line in completed.stdout.replace("\r", "").splitlines() if line.strip()]
    if not lines:
        raise RuntimeError("sqlcmd não retornou valor escalar")
    return lines[-1]


def holder_query(resource: str, signal_table: str, delay_ms: int) -> str:
    seconds, rem = divmod(delay_ms, 1000)
    delay = f"00:00:{seconds:02d}.{rem:03d}"
    return f"""
IF OBJECT_ID('tempdb..{signal_table}') IS NOT NULL DROP TABLE {signal_table};
CREATE TABLE {signal_table}(acquired bit NOT NULL);
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=N'{resource}',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0;
IF @r<0 THROW 51990,'probe holder lock failed',1;
INSERT {signal_table}(acquired) VALUES(1);
WAITFOR DELAY '{delay}';
DECLARE @release int;
EXEC @release=sys.sp_releaseapplock @Resource=N'{resource}',@LockOwner='Session';
"""


def readiness_query(signal_table: str) -> str:
    escaped = signal_table.replace("'", "''")
    return (
        f"IF OBJECT_ID('tempdb..{escaped}') IS NULL SELECT 0; "
        f"ELSE EXEC(N'SELECT CASE WHEN EXISTS(SELECT 1 FROM {escaped}) THEN 1 ELSE 0 END;');"
    )


def probe(root: Path, database: str, password: str, resource: str, delay_ms: int) -> dict[str, object]:
    signal_table = f"##JornadaScaleLockProbe_{uuid.uuid4().hex}"
    command = [
        "docker", "compose", "--env-file", str(root / ".env"),
        "exec", "-T", "-e", "SQLCMDPASSWORD",
        "sqlserver", "/opt/mssql-tools18/bin/sqlcmd",
        "-S", "localhost", "-U", "sa", "-C", "-b",
        "-d", database, "-Q", holder_query(resource, signal_table, delay_ms),
    ]
    holder = subprocess.Popen(
        command, cwd=root, env={**os.environ, "SQLCMDPASSWORD": password},
        text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    try:
        deadline = time.monotonic() + 10.0
        while True:
            if holder.poll() is not None:
                stdout, stderr = holder.communicate()
                raise RuntimeError(
                    f"holder encerrou antes de confirmar aquisição de {resource}: "
                    f"{stderr.strip() or stdout.strip() or holder.returncode}"
                )
            ready = scalar(root, database, password, readiness_query(signal_table))
            if ready == "1":
                break
            if time.monotonic() >= deadline:
                raise RuntimeError(f"timeout aguardando holder confirmar lock {resource}")
            time.sleep(0.05)

        started = time.monotonic()
        result_text = scalar(
            root,
            database,
            password,
            f"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'{resource}',"
            "@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; "
            f"DECLARE @release int; IF @r>=0 EXEC @release=sys.sp_releaseapplock "
            f"@Resource=N'{resource}',@LockOwner='Session'; SELECT @r;",
        )
        wait_ms = round((time.monotonic() - started) * 1000)
        try:
            lock_result = int(result_text)
        except ValueError as exc:
            raise RuntimeError(f"resultado de applock inválido para {resource}: {result_text!r}") from exc

        stdout, stderr = holder.communicate(timeout=15)
        if holder.returncode != 0:
            raise RuntimeError(stderr.strip() or stdout.strip() or f"holder falhou: {holder.returncode}")

        minimum_wait = int(delay_ms * MIN_WAIT_FRACTION)
        if lock_result != 1:
            raise RuntimeError(
                f"probe de {resource} não comprovou contenção: lockResult={lock_result}; esperado 1"
            )
        if wait_ms < minimum_wait:
            raise RuntimeError(
                f"probe de {resource} esperou apenas {wait_ms} ms; mínimo={minimum_wait} ms"
            )

        return {
            "resource": resource,
            "contentionConfirmed": True,
            "lockResult": lock_result,
            "waitMilliseconds": wait_ms,
        }
    finally:
        if holder.poll() is None:
            holder.kill()
            holder.communicate()


def main() -> int:
    parser = argparse.ArgumentParser(description="Executa probe sincronizado de contenção dos applocks do pipeline.")
    parser.add_argument("--root", required=True)
    parser.add_argument("--database", required=True)
    parser.add_argument("--delay-ms", type=int, default=3000)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    if args.delay_ms <= 0:
        raise SystemExit("ERRO: --delay-ms deve ser > 0")
    env = load_env(root / ".env")
    password = env.get("JORNADA_SQL_SA_PASSWORD")
    if not password:
        raise SystemExit("ERRO: JORNADA_SQL_SA_PASSWORD ausente em .env")

    report = {
        "holderDelayMilliseconds": args.delay_ms,
        "exclusiveRequest": probe(root, args.database, password, RESOURCES[0], args.delay_ms),
        "corpus": probe(root, args.database, password, RESOURCES[1], args.delay_ms),
    }
    print(json.dumps(report, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
