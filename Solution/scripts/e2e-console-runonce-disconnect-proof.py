#!/usr/bin/env python3
"""C3.3b3a real E2E: client disconnect cannot release an active private RunOnce.

Uses only a local DevConsole child and a Compose oneoff in the already created
GitHub-hosted JornadaE2E project; no host ports or extra image builds.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
from urllib import error, request

ROOT=Path(__file__).resolve().parents[1]
DLL=ROOT/"src/Jornada.DevConsole/bin/Release/net10.0/Jornada.DevConsole.dll"
OUT=ROOT/".local/e2e/c3-3b3a-disconnect"
WORKERS={"processor","operations-maintenance","bronze-maintenance"}


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def call(base: str, path: str, method: str="GET", payload: dict | None=None) -> tuple[int,dict]:
    data=None if payload is None else json.dumps(payload).encode()
    req=request.Request(base+path,data=data,method=method,
                        headers={"Content-Type":"application/json"})
    try:
        response=request.urlopen(req,timeout=115)
    except error.HTTPError as ex:
        response=ex
    with response:
        return response.status,json.loads(response.read())


def state(base: str, expected_mode: str) -> dict:
    code, obj=call(base,"/api/workers/supervisor")
    require(code==200 and obj.get("mode")==expected_mode,"wrong global effective mode")
    require(obj.get("sqlRunning") is True and obj.get("apiReady") is True
            and obj.get("resultadoApiLive") is True,"private SQL/API not ready")
    rows=obj.get("workers")
    require(isinstance(rows,list) and len(rows)==3
            and {w.get("worker") for w in rows}==WORKERS,
            "worker list differs from allowlist")
    return obj


def oneoffs(project: str) -> list[dict]:
    p=subprocess.run(["docker","ps","-aq","--filter",
                      "label=com.docker.compose.project="+project,
                      "--filter","label=com.docker.compose.oneoff=True"],
                     capture_output=True,timeout=12,check=False)
    require(p.returncode==0,"Docker read-only private oneoff query failed")
    found=[]
    for cid in p.stdout.decode().splitlines():
        require(re.fullmatch(r"[0-9a-f]{12,64}",cid) is not None,"invalid container ID")
        r=subprocess.run(["docker","inspect","--format","{{json .}}",cid],
                         capture_output=True,timeout=10,check=False)
        require(r.returncode==0,"Docker private oneoff inspect failed")
        obj=json.loads(r.stdout)
        labels=obj["Config"]["Labels"]
        require(labels.get("com.docker.compose.project")==project
                and labels.get("com.docker.compose.oneoff")=="True"
                and labels.get("com.docker.compose.service")=="processor",
                "foreign/non-Processor container in private oneoff set")
        found.append({"id":cid,"pid":obj["State"]["Pid"],
                      "running":obj["State"]["Running"],
                      "restart_policy":obj["HostConfig"]["RestartPolicy"]["Name"]})
    return found


def main() -> None:
    run=os.environ.get("GITHUB_RUN_ID","")
    attempt=os.environ.get("GITHUB_RUN_ATTEMPT","")
    require(len(sys.argv)==1 and os.environ.get("GITHUB_ACTIONS")=="true"
            and os.environ.get("CI")=="true"
            and os.environ.get("GITHUB_REPOSITORY")=="lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST")=="true"
            and re.fullmatch(r"[0-9]{6,16}",run) is not None
            and re.fullmatch(r"[0-9]{1,3}",attempt) is not None
            and os.environ.get("JORNADA_WORKERS_E2E_ID")=="ci"+run+attempt
            and bool(os.environ.get("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            and not os.environ.get("DOCKER_HOST")
            and not os.environ.get("DOCKER_CONTEXT"),
            "only disposable private GitHub DEV E2E permitted")
    require(DLL.is_file(),"DevConsole built by existing E2E required")
    project=f"jornada-workers-e2e-ci{run}{attempt}"
    OUT.mkdir(parents=True,exist_ok=True)
    with socket.socket(socket.AF_INET,socket.SOCK_STREAM) as server:
        server.bind(("127.0.0.1",0))
        port=server.getsockname()[1]
    base=f"http://127.0.0.1:{port}"
    env={**os.environ,"JORNADA_RUNTIME_MODE":"DEV",
         "DOTNET_ENVIRONMENT":"Development",
         "ASPNETCORE_ENVIRONMENT":"Development",
         "ASPNETCORE_URLS":base,
         "JORNADA_WORKERS_E2E_TEST_RUNONCE_HOLD_SECONDS":"8"}
    with (OUT/"devconsole-disconnect.log").open("wb") as logs:
        child=subprocess.Popen(["dotnet",str(DLL)],cwd=ROOT,env=env,
                               stdin=subprocess.DEVNULL,stdout=logs,
                               stderr=subprocess.STDOUT)
        try:
            startup=time.monotonic()+35
            while True:
                require(child.poll() is None,"Console child exited before test")
                try:
                    c,v=call(base,"/api/version")
                    if c==200 and v.get("mode")=="DEV":
                        break
                except (OSError,ValueError):
                    pass
                require(time.monotonic()<startup,"DevConsole was not ready")
                time.sleep(0.2)
            initial=state(base,"ON")
            c,off=call(base,"/api/workers/supervisor","POST",{"mode":"OFF"})
            require(c==200 and off.get("mode")=="OFF","OFF failed")
            before=state(base,"OFF")
            require(not oneoffs(project),"pre-existing private oneoff")
            # One HTTP request is admitted; the client drops its socket while
            # a real Compose oneoff is running on a bounded CI-only hold.
            with socket.create_connection(("127.0.0.1",port),timeout=8) as client:
                client.sendall(
                    b"POST /api/workers/processor/run-once HTTP/1.1\r\n"
                    b"Host: 127.0.0.1\r\nContent-Length: 0\r\n"
                    b"Connection: keep-alive\r\n\r\n")
                deadline=time.monotonic()+30
                while True:
                    current=state(base,"OFF")
                    rows={x["worker"]:x for x in current["workers"]}
                    if rows["processor"]["state"]=="RUN_ONCE":
                        break
                    require(time.monotonic()<deadline,
                            "server did not register live Processor RunOnce")
                    time.sleep(0.15)
                containers=oneoffs(project)
                require(len(containers)==1 and containers[0]["running"] is True
                        and int(containers[0]["pid"])>1
                        and containers[0]["restart_policy"]=="no",
                        "RUN_ONCE state did not match a real active Docker oneoff")
                # Explicitly disconnect with the job active.
                client.shutdown(socket.SHUT_RDWR)
            disconnected_at=time.monotonic()
            after_disconnect=state(base,"OFF")
            by_name={x["worker"]:x for x in after_disconnect["workers"]}
            require(by_name["processor"]["state"]=="RUN_ONCE",
                    "client abort released job tracking before container exit")
            denied,refused=call(base,"/api/workers/supervisor","POST",{"mode":"ON"})
            require(denied==409,"global ON illegally began during disconnected RunOnce")
            # Do not trust a timeout/sleep: require the *real* finite Docker
            # oneoff to exit/--rm, followed by a fresh OFF backend observation.
            deadline=time.monotonic()+65
            finished=None
            while time.monotonic()<deadline:
                current=state(base,"OFF")
                if (all(x["state"]=="PARADO" for x in current["workers"])
                        and not oneoffs(project)):
                    finished=current
                    break
                time.sleep(0.5)
            require(finished is not None,"finite job did not finish and remove oneoff")
            require(time.monotonic()-disconnected_at>=1,
                    "oneoff ended before disconnect was actually tested")
            ready,enabled=call(base,"/api/workers/supervisor","POST",{"mode":"ON"})
            require(ready==200 and enabled.get("mode")=="ON",
                    "global ON not restored after disconnected job completed")
            end=state(base,"ON")
            require(all(x["state"]=="ATIVO" and isinstance(x.get("hostPid"),int)
                        and x["hostPid"]>1 for x in end["workers"]),
                    "three residents did not resume after client disconnect")
            require(not oneoffs(project),"oneoff still exists after ON")
            summary={
                "status":"PASS","scenario":"C3.3b3a_disconnect_does_not_cancel_oneoff",
                "compose_project":project,"worker":"processor",
                "client_disconnected_while_oneoff_running":True,
                "docker_oneoff_label_verified":True,
                "backend_runonce_stayed_visible":True,
                "global_on_rejected_while_active":True,
                "finite_container_removed_before_on":True,
                "no_user_data_access":True,
                "global_mode_after":"ON",
                "cancel_with_confirmation_implemented":False}
            (OUT/"summary.json").write_text(
                json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
            print("C3.3b3a: PASS disconnected HTTP while real oneoff active; "
                  "mode stayed OFF; ON denied; finite ended, ON restored")
        finally:
            if child.poll() is None:
                child.terminate()  # ONLY our short-lived DevConsole child
                try: child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=5)


if __name__=="__main__":
    try: main()
    except (RuntimeError,OSError,ValueError,KeyError) as exc:
        # RuntimeError messages originate from fixed require() assertions above.\n        # Do not print arbitrary OS/HTTP exception details (may contain secrets).\n        detail = ": " + str(exc) if type(exc) is RuntimeError else ""\n        failure = {"status":"FAIL","scenario":"C3.3b3a_disconnect_does_not_cancel_oneoff",\n                   "exception_type":type(exc).__name__,\n                   "assertion":str(exc) if type(exc) is RuntimeError else "non-assertion runtime failure"}\n        OUT.mkdir(parents=True,exist_ok=True)\n        (OUT/"failure.json").write_text(json.dumps(failure,indent=2)+"\\n",encoding="utf-8")\n        print("C3.3b3a: REJECTED "+type(exc).__name__+detail,flush=True)
        sys.exit(2)
