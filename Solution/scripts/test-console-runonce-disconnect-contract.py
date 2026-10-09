#!/usr/bin/env python3
"""C3.3b3a static safety and negative contract; no Docker, SQL or processes."""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
API=ROOT/"src/Jornada.DevConsole/Program.cs"
ENTRY=ROOT/"install/container-test/worker-entrypoint.sh"
RUNONCE=ROOT/"scripts/console-private-worker-runonce.py"
E2E=ROOT/"scripts/e2e-console-runonce-disconnect-proof.py"
BOOT=ROOT/"scripts/e2e-private-sql-bootstrap-runtime.sh"


def reject(path: Path, args: tuple[str,...], env: dict[str,str]) -> None:
    result=subprocess.run([sys.executable,str(path),*args],cwd=ROOT,
                          env={"PATH":os.environ.get("PATH",""),"HOME":"/tmp",**env},
                          capture_output=True,text=True,timeout=5,check=False)
    assert result.returncode==2 and not result.stdout, (path.name,result.stderr)


def main() -> None:
    route=API.read_text(encoding="utf-8")
    shell=ENTRY.read_text(encoding="utf-8")
    worker=RUNONCE.read_text(encoding="utf-8")
    e2e=E2E.read_text(encoding="utf-8")
    bootstrap=BOOT.read_text(encoding="utf-8")
    for path in (RUNONCE,E2E):
        compile(path.read_text(encoding="utf-8"),str(path),"exec")
        reject(path,(),{})
        reject(path,("processor",),{})
        reject(path,(),{"GITHUB_ACTIONS":"true","CI":"true",
                        "GITHUB_REPOSITORY":"other/repo"})
    subprocess.run(["bash","-n",str(ENTRY)],check=True)

    # HTTP transport cancellation must never cancel the actual Compose job.
    route=route[route.index('app.MapPost("/api/workers/{worker}/run-once"'):]
    route=route[:route.index('app.MapPost("/api/session-counts/reset"')]
    assert 'IHostApplicationLifetime application' in route
    assert 'worker,runtime,application.ApplicationStopping' in route
    assert 'worker,runtime,ct' not in route
    assert 'HttpContext context' in route
    assert 'System.Net.IPAddress.IsLoopback(remote)' in route
    assert 'reader.Enabled(runtime)' in route
    assert 'RequestAborted' in route
    assert 'app.MapPost("/api/workers/{worker}/run-once"' in route
    assert 'controller.RunOnceAsync(' in route
    assert 'app.MapPost("/api/workers/supervisor"' not in route

    # The bounded synthetic hold is injected by the guarded E2E runner only,
    # while inside a real oneoff container. It is never a product default.
    assert 'JORNADA_WORKERS_E2E_TEST_RUNONCE_HOLD_SECONDS' in worker
    assert 'hold in ("", "8")' in worker
    assert 'sys.argv[1] == "processor"' in worker
    assert 'JORNADA_WORKERS_E2E_TEST_RUNONCE_HOLD_SECONDS=8' in worker
    assert 'JORNADA_WORKERS_E2E_TEST_RUNONCE_HOLD_SECONDS' in shell
    assert '[[ "$mode" == "--run-once" && "$worker" == "Processor"' in shell
    assert 'sleep 8' in shell and 'exec dotnet "$dll"' in shell
    assert shell.index('sleep 8') < shell.index('exec dotnet "$dll"')
    for unsafe in ("JornadaLocal","DROP DATABASE","docker volume prune",
                   "docker system prune","docker compose down"):
        assert unsafe not in worker+e2e

    # The operational test must verify a TRUE Docker oneoff, disconnect
    # the HTTP socket, refuse global ON, await Docker cleanup, then restore ON.
    assert "client.shutdown(socket.SHUT_RDWR)" in e2e
    assert '"RUN_ONCE"' in e2e
    assert 'label=com.docker.compose.oneoff=True' in e2e
    assert 'restart_policy' in e2e and '=="no"' in e2e
    # The backend advertises RUN_ONCE as soon as admission wins, before
    # Compose creates the real container. The E2E must poll until BOTH
    # status sources are true to avoid a CI scheduling false negative.
    assert 'if rows["processor"]["state"]=="RUN_ONCE":' in e2e
    assert 'len(containers)<=1' in e2e
    assert 'real active Docker oneoff and backend RUN_ONCE never coincided' in e2e
    assert 'denied==409' in e2e
    assert "not oneoffs(project)" in e2e
    assert 'global_mode_after' in e2e
    assert '"cancel_with_confirmation_implemented":False' in e2e
    assert 'python3 scripts/e2e-console-runonce-disconnect-proof.py' in bootstrap
    print("C3.3b3a: PASS HTTP disconnect ownership and private oneoff guards (offline)")


if __name__=="__main__":
    main()
