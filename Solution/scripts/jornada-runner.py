#!/usr/bin/env python3
"""DT-11: entrada única, Bash/PowerShell, perfis fixos e resets explícitos."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_VERIFICATION_FAILED = 2
EXIT_INCOMPLETE = 3
EXIT_INVALID_PRECONDITION = 4
EXIT_INVALID_ARGS = 64
EXIT_CANCELLED = 130


class JornadaArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        self.print_usage(sys.stderr)
        self.exit(EXIT_INVALID_ARGS, f"{self.prog}: error: {message}\n")


SCRIPTS = Path(__file__).resolve().parent
ROOT = SCRIPTS.parent
DBS = {"local": "JornadaLocal", "e2e": "JornadaE2E", "synthetic": "JornadaSyntheticDev"}
OPERATIONS = {
    "test": ("local", "local-test", False),
    "e2e": ("e2e", "local-e2e", True),
    "synthetic": ("synthetic", "local-synthetic-calibration", True),
    "diagnose": ("synthetic", "local-synthetic-diagnostics", False),
    "ddl-upgrade": ("local", "local-ddl-upgrade", True),
    "cluster-status": ("local", "local-cluster", False),
}


def plan(profile, operation, action=None, shell="auto"):
    if profile not in DBS:
        raise ValueError("Perfil inválido.")
    db = DBS[profile]
    shell = ("powershell" if os.name == "nt" else "bash") if shell == "auto" else shell
    if shell not in ("powershell", "bash"):
        raise ValueError("Shell inválido.")
    if operation == "db":
        if action not in ("up", "status", "backfill", "reset"):
            raise ValueError("db aceita up/status/backfill/reset; down/clean afetariam volumes compartilhados.")
        if action == "backfill" and profile != "local":
            raise ValueError("Backfill apenas no perfil local.")
        script, destructive = "local-db", action == "reset"
        bash_args = [action] + (["--no-synthetic-corpus"] if profile != "local" and action in ("up", "reset") else [])
        ps_args = ["-Action", action, "-DatabaseName", db] + (["-NoSyntheticCorpus"] if profile != "local" and action in ("up", "reset") else [])
    else:
        if action is not None or operation not in OPERATIONS or OPERATIONS[operation][0] != profile:
            raise ValueError("Operação não admitida neste perfil.")
        _, script, destructive = OPERATIONS[operation]
        bash_args = ["status"] if operation == "cluster-status" else [db] if operation == "diagnose" else []
        ps_args = ["-Action", "status"] if operation == "cluster-status" else ["-DatabaseName", db] if operation == "diagnose" else []
    return dict(profile=profile, database=db, operation=operation, action=action, script=script,
                destructive=destructive, shell=shell, bash_args=bash_args, ps_args=ps_args)


def declared_db(path):
    if not path.is_file():
        raise ValueError("Arquivo de ambiente ausente: " + str(path))
    values = re.findall(r"(?m)^\s*JORNADA_SQL_DATABASE\s*=\s*([A-Za-z0-9_]+)\s*$",
                        path.read_text(encoding="utf-8-sig"))
    if len(set(values)) > 1:
        raise ValueError("Declarações conflitantes de JORNADA_SQL_DATABASE no .env.")
    return values[-1] if values else "JornadaLocal"


def execute(job, allow_reset=False, dry_run=False):
    if dry_run:
        safe = {k: v for k, v in job.items() if k not in ("bash_args", "ps_args")}
        safe["authorized"] = allow_reset
        print(json.dumps(safe, ensure_ascii=False))
        return EXIT_OK
    if job["destructive"] and not allow_reset:
        raise ValueError("Exige --allow-reset; nenhum banco foi alterado.")
    env = dict(os.environ)
    db = job["database"]
    if job["shell"] == "bash":
        binary = shutil.which("bash")
        argv = [binary, str(SCRIPTS / (job["script"] + ".sh")), *job["bash_args"]]
        env["JORNADA_SQL_DATABASE_OVERRIDE"] = db
    else:
        binary = shutil.which("pwsh") or shutil.which("powershell")
        argv = [binary, "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                str(SCRIPTS / (job["script"] + ".ps1")), *job["ps_args"]]
        config = Path(env.get("JORNADA_LOCAL_ENV_FILE") or
                      (ROOT / ".env.synthetic.local" if job["profile"] == "synthetic" else ROOT / ".env"))
        # PowerShell sintético exige arquivo separado; nunca copiar senhas para arquivo transitório.
        if job["profile"] == "synthetic" and declared_db(config) != DBS["synthetic"]:
            raise ValueError("Configure .env.synthetic.local com JORNADA_SQL_DATABASE=JornadaSyntheticDev.")
        if job["profile"] == "local" and job["operation"] == "test" and declared_db(config) != db:
            raise ValueError("Teste local exige configuração apontando a JornadaLocal.")
        env["JORNADA_LOCAL_ENV_FILE"] = str(config)
    if not binary:
        raise ValueError("Shell solicitado não instalado.")
    target_script = Path(argv[1] if job["shell"] == "bash" else argv[5])
    if not target_script.is_file():
        raise ValueError("Script especializado ausente: " + target_script.name)
    print(f"DT11: {job['profile']} -> {db}; {job['operation']}; sem reset de outros perfis.", flush=True)
    return subprocess.run(argv, cwd=ROOT, env=env, check=False).returncode


def main(argv=None):
    parser = JornadaArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True, choices=tuple(DBS))
    parser.add_argument("--shell", choices=("auto", "bash", "powershell"), default="auto")
    parser.add_argument("--allow-reset", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("operation", choices=("db", *OPERATIONS))
    parser.add_argument("action", nargs="?")
    try:
        args = parser.parse_args(argv)
    except SystemExit as exc:
        # --help remains OK; syntactically invalid arguments are INVALID_ARGS.
        return int(exc.code)
    try:
        return execute(plan(args.profile, args.operation, args.action, args.shell),
                       args.allow_reset, args.dry_run)
    except KeyboardInterrupt:
        print("DT11: execução cancelada pelo operador.", file=sys.stderr)
        return EXIT_CANCELLED
    except ValueError as exc:
        print("DT11: pré-condição recusada:", exc, file=sys.stderr)
        return EXIT_INVALID_PRECONDITION


if __name__ == "__main__":
    sys.exit(main())
