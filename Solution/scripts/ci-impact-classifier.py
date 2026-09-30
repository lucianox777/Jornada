#!/usr/bin/env python3
from __future__ import annotations
import argparse,json
from pathlib import Path
FLAGS=("docs","dotnet","sql","integration","linkage","security","contracts","dependencies","workflow","full")
def classify(paths):
 out={k:False for k in FLAGS}; reasons={k:[] for k in FLAGS}
 def mark(k,p): out[k]=True; reasons[k].append(p)
 for raw in paths:
  p=raw.replace("\\","/").lstrip("./"); low=p.lower()
  if low.startswith("solution/docs/") or low.endswith((".md",".txt")): mark("docs",p); continue
  if low.startswith(".github/workflows/") or low=="solution/scripts/ci-impact-classifier.py": mark("workflow",p); mark("full",p); continue
  if low.endswith((".sln",".csproj",".props",".targets","packages.lock.json")) or low in ("global.json","nuget.config"): mark("dependencies",p); mark("full",p); continue
  if low.startswith("solution/database/") or low.endswith(".sql"): mark("sql",p); mark("integration",p); continue
  if low.startswith(("solution/src/","solution/tests/")):
   mark("dotnet",p)
   if "linkage" in low: mark("linkage",p)
   if any(x in low for x in ("security","authorization","access.")): mark("security",p)
   if "integration.tests" in low: mark("integration",p)
   if any(x in low for x in ("contracts","api/","resultado.api")): mark("contracts",p)
   continue
  if low.startswith("solution/config/linkage/"): mark("linkage",p); mark("dotnet",p); continue
  if low.startswith(("solution/config/","solution/scripts/","solution/docker")) or low.endswith((".yml",".yaml")): mark("full",p); continue
  mark("full",p)
 out["any_dotnet"]=any(out[k] for k in ("dotnet","integration","linkage","security","contracts","full"))
 out["run_unit"]=out["any_dotnet"]; out["run_deterministic"]=out["dotnet"] or out["dependencies"] or out["full"]
 out["run_security"]=out["security"] or out["dependencies"] or out["workflow"] or out["full"]
 out["run_integration"]=out["integration"] or out["sql"] or out["linkage"] or out["contracts"] or out["full"]
 out["run_harness"]=out["linkage"] or out["integration"] or out["sql"] or out["full"]
 out["run_ddl"]=out["sql"] or out["full"]; out["run_e2e"]=out["linkage"] or out["integration"] or out["sql"] or out["contracts"] or out["full"]
 return {"flags":out,"reasons":reasons,"paths":paths}
def main():
 ap=argparse.ArgumentParser(); ap.add_argument("--files-from",required=True); ap.add_argument("--summary"); a=ap.parse_args()
 paths=[x.strip() for x in Path(a.files_from).read_text().splitlines() if x.strip()]; result=classify(paths)
 payload=json.dumps(result,ensure_ascii=False,indent=2,sort_keys=True)
 if a.summary: Path(a.summary).write_text(payload+"\n",encoding="utf-8")
 print(payload)
 for k,v in result["flags"].items(): print(f"{k}={'true' if v else 'false'}")
if __name__=="__main__": main()
