#!/usr/bin/env python3
"""Enforce standard runner resource limits, without logging private data."""
import argparse,json,os,shutil,signal,subprocess,time
from pathlib import Path
def sample(root):
    mem={}
    for line in Path("/proc/meminfo").read_text().splitlines():
        name,value=line.split(":",1);mem[name]=int(value.split()[0])*1024
    return dict(timeUnix=time.time(),memoryTotal=mem["MemTotal"],memoryAvailable=mem["MemAvailable"],
                freeDisk=shutil.disk_usage(root).free)
def main():
    p=argparse.ArgumentParser();p.add_argument("--root",type=Path,required=True);p.add_argument("cmd",nargs=argparse.REMAINDER);a=p.parse_args()
    deadline=int(os.environ["JORNADA_PHASE4_DEADLINE_UNIX"])
    start=time.time();reason=None;peak=0;lowmem=None;lowdisk=None
    def check():
        nonlocal peak,lowmem,lowdisk
        s=sample(a.root);peak=max(peak,s["memoryTotal"]-s["memoryAvailable"])
        lowmem=s["memoryAvailable"] if lowmem is None else min(lowmem,s["memoryAvailable"])
        lowdisk=s["freeDisk"] if lowdisk is None else min(lowdisk,s["freeDisk"])
        if s["timeUnix"]>=deadline-300:return "TIME_BUDGET"
        if s["memoryAvailable"]<max(512*1024**2,int(.08*s["memoryTotal"])):return "MEMORY_BUDGET"
        if s["freeDisk"]<1024**3:return "DISK_BUDGET"
    reason=check();code=1
    if not reason:
        child=subprocess.Popen(a.cmd[1:] if a.cmd[0]=="--" else a.cmd,start_new_session=True)
        while child.poll() is None:
            reason=check()
            if reason:
                os.killpg(child.pid,signal.SIGTERM)
                try:child.wait(timeout=15)
                except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL);child.wait()
                break
            time.sleep(2)
        code=child.wait()
    result=dict(status="STOPPED_RESOURCE" if reason else "COMPLETE" if code==0 else "EXECUTION_FAILED",
                stopReason=reason,exitCode=code,wallSeconds=time.time()-start,peakHostMemoryUsed=peak,
                minMemoryAvailable=lowmem,minDiskFree=lowdisk,deadlineUnix=deadline)
    out=Path(os.environ["RUNNER_TEMP"])/"v9-scale-resource-receipt.json"
    out.write_text(json.dumps(result,sort_keys=True))
    print("V9_SCALE_RESOURCE_"+(reason or result["status"]))
    raise SystemExit(1 if reason or code else 0)
if __name__=="__main__":main()
