#!/usr/bin/env python3
"""C3.2a non-destructive Compose topology gate: config only, no containers/SQL."""
from __future__ import annotations

import json
import os
from pathlib import Path
import secrets
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
PROJECT = "jornada-workers-e2e-contract123"
WORKERS = {
    "processor": "Processor",
    "operations-maintenance": "OperationsMaintenance",
    "bronze-maintenance": "BronzeMaintenance",
}


def require(value: bool, message: str) -> None:
    if not value:
        raise AssertionError(message)


def inspect(extra: dict[str, str], *, continuous: bool = False) -> subprocess.CompletedProcess[str]:
    # Deliberately pass no local .env and NEVER invoke up, build, stop or down.
    env = {
        "PATH": os.environ.get("PATH", ""),
        "HOME": os.environ.get("HOME", "/tmp"),
        **extra,
    }
    return subprocess.run(
        ["docker", "compose", "--env-file", "/dev/null",
         *(["--profile", "continuous"] if continuous else []),
         "-f", str(COMPOSE), "config", "--format", "json"],
        cwd=ROOT, env=env, text=True,
        capture_output=True, timeout=30, check=False,
    )


def main() -> int:
    denied = inspect({})
    require(denied.returncode != 0, "Compose must fail without explicit sandbox name/secret")

    sql_secret = secrets.token_urlsafe(32) + "Aa9!"
    env = {"JORNADA_WORKERS_E2E_ID": "contract123",
           "JORNADA_WORKERS_E2E_SQL_PASSWORD": sql_secret}
    denied = inspect({"JORNADA_WORKERS_E2E_ID": "contract123"})
    require(denied.returncode != 0, "Compose must fail without dedicated E2E secret")
    default = inspect(env)
    require(default.returncode == 0,
            "Disposable Compose OFF mode failed config validation: " + default.stderr[:500])
    default_cfg = json.loads(default.stdout)
    require(set(default_cfg.get("services", {})) == {"sqlserver", "sql-bootstrap", "api", "resultado-api"},
            "Default supervisor OFF must keep both APIs alive but no resident workers")

    # Compose's default `config` excludes profile-gated services. Activate
    # the profile explicitly to inspect all three independently supervised
    # containers; this still runs only `config`, never `up`.
    result = inspect(env, continuous=True)
    require(result.returncode == 0,
            "Disposable Compose ON mode failed config validation: " + result.stderr[:500])
    cfg = json.loads(result.stdout)

    require(cfg.get("name") == PROJECT, "Compose did not retain isolated project name")
    services = cfg.get("services", {})
    require(set(services) == set(WORKERS) | {"sqlserver", "sql-bootstrap", "api", "resultado-api"},
            "Unexpected services or missing isolated API/ResultadoApi/worker")
    db = services["sqlserver"]
    require(db.get("restart") == "no", "SQL service must not auto-start workers")
    require(not db.get("ports") and not db.get("container_name"),
            "Sandbox SQL must expose no host port/fixed container name")
    require(db.get("environment", {}).get("MSSQL_SA_PASSWORD") == sql_secret,
            "SQL service secret not separately scoped")

    init = services["sql-bootstrap"]
    require(not init.get("profiles") and init.get("restart") == "no",
            "SQL bootstrap must run once in both supervisor modes")
    require(not init.get("volumes") and not init.get("ports")
            and not init.get("container_name") and not init.get("privileged"),
            "SQL bootstrap may not mount volumes or publish host resources")
    require(init.get("build", {}).get("dockerfile")
            == "install/console-dev-e2e/sql-bootstrap/Dockerfile",
            "SQL bootstrap must use restricted private build")
    require(set(init.get("depends_on", {})) == {"sqlserver"},
            "SQL bootstrap may depend only on private SQL")
    init_env = init.get("environment", {})
    require(init_env.get("JORNADA_WORKERS_E2E_SQL_BOOTSTRAP") == "true"
            and init_env.get("JORNADA_RUNTIME_MODE") == "DEV"
            and init_env.get("DOTNET_ENVIRONMENT") == "Development"
            and init_env.get("JORNADA_E2E_SQL_DATABASE") == "JornadaE2E"
            and init_env.get("JORNADA_SQL_DATABASE_OVERRIDE") == "JornadaE2E"
            and init_env.get("JORNADA_WORKERS_E2E_SQL_HOST") == "sqlserver"
            and init_env.get("SQLCMDPASSWORD") == sql_secret,
            "Bootstrap lost private DEV/E2E guard")

    # API/ResultadoApi are independently supervised, not in the continuous
    # profile. Workers may be killed without stopping or restarting the APIs.
    for name, arg, node, port in (
        ("api", "Api", "NODE1", "5080"),
        ("resultado-api", "ResultadoApi", "NODE2", "5081"),
    ):
        svc = services[name]
        require(not svc.get("profiles"), f"{name}: must stay alive in OFF")
        require("sql-bootstrap" in svc.get("depends_on", {})
                and svc["depends_on"]["sql-bootstrap"].get("condition")
                == "service_completed_successfully",
                f"{name}: must wait for private SQL bootstrap")
        require(svc.get("restart") == "unless-stopped",
                f"{name}: needs its own restart policy")
        require(svc.get("entrypoint") == ["/usr/local/bin/jornada-api-entrypoint"],
                f"{name}: must not use collective NODE entrypoint")
        require(svc.get("command") == [arg], f"{name}: invalid allowlisted API")
        require(not svc.get("ports") and not svc.get("container_name")
                and not svc.get("pid") and not svc.get("privileged")
                and not svc.get("network_mode"),
                f"{name}: shared process/host namespace or port detected")
        api_env = svc.get("environment", {})
        require(api_env.get("JORNADA_WORKER_ISOLATED_PROFILE") == "true"
                and api_env.get("JORNADA_RUNTIME_MODE") == "DEV"
                and api_env.get("JORNADA_E2E_SQL_DATABASE") == "JornadaE2E"
                and api_env.get("JORNADA_SQL_DATABASE_OVERRIDE") == "JornadaE2E"
                and api_env.get("JORNADA_NODE_ID") == node
                and api_env.get("ASPNETCORE_URLS") == f"http://0.0.0.0:{port}",
                f"{name}: incorrect DEV-only API guard or internal listener")
        require("Server=sqlserver,1433;Database=JornadaE2E;" in
                api_env.get("ConnectionStrings__Jornada", ""),
                f"{name}: points outside the private E2E SQL service")
        require(svc.get("healthcheck", {}).get("test"),
                f"{name}: must expose liveness healthcheck")
    require(services["resultado-api"]["environment"].get("JornadaApiBaseUrl")
            == "http://api:5080", "ResultadoApi must call the internal API")
    require(set(services["resultado-api"].get("depends_on", {}))
            == {"sqlserver", "sql-bootstrap", "api"}, "ResultadoApi service dependency drift")

    for name, arg in WORKERS.items():
        svc = services[name]
        require("sql-bootstrap" in svc.get("depends_on", {})
                and svc["depends_on"]["sql-bootstrap"].get("condition")
                == "service_completed_successfully",
                f"{name}: continuous worker must wait for private SQL bootstrap")
        require(svc.get("profiles") == ["continuous"],
                f"{name}: default OFF must not autostart")
        require(svc.get("restart") == "unless-stopped",
                f"{name}: supervisor policy must be independent")
        require(svc.get("entrypoint") == ["/usr/local/bin/jornada-worker-entrypoint"],
                f"{name}: must own its process boundary")
        require(svc.get("command") == [arg],
                f"{name}: worker allowlist command incorrect")
        require(not svc.get("ports") and not svc.get("container_name"),
                f"{name}: must never use host ports/fixed container_name")
        require(not svc.get("pid") and not svc.get("network_mode")
                and not svc.get("privileged"),
                f"{name}: PID/network namespace must remain isolated and unprivileged")
        worker_env = svc.get("environment", {})
        require(worker_env.get("JORNADA_WORKER_ISOLATED_PROFILE") == "true"
                and worker_env.get("DOTNET_ENVIRONMENT") == "Development"
                and worker_env.get("JORNADA_SQL_DATABASE_OVERRIDE") == "JornadaE2E",
                f"{name}: E2E guard missing")
        conn = worker_env.get("ConnectionStrings__Jornada", "")
        require("Database=JornadaE2E;" in conn and sql_secret in conn,
                f"{name}: wrong effective database connection")
        for dependency in svc.get("depends_on", {}):
            require(dependency in {"sqlserver", "sql-bootstrap"},
                    f"{name}: unexpected shared service dependency")

    for name, svc in services.items():
        for mount in svc.get("volumes", []):
            require(mount.get("type") == "volume"
                    and mount.get("source") in {"sql_data", "bronze", "staging"},
                    f"{name}: host bind or unexpected shared storage")
    for name, volume in cfg.get("volumes", {}).items():
        require(name in {"sql_data", "bronze", "staging"}
                and volume.get("name", "").startswith(PROJECT + "_")
                and volume.get("external") is not True,
                f"Global or external volume detected: {name}")
    for net in cfg.get("networks", {}).values():
        require(net.get("external") is not True,
                "External network not allowed in disposable Compose")
    print("WORKER DISPOSABLE COMPOSE: PASS (dry-run config only, 2 independent APIs + "
          "3 independent workers, default OFF, dedicated SQL/volumes/network; nothing started)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
