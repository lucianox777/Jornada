#!/usr/bin/env python3
"""Batch V9 in-memory candidate-pass selection; diagnostic only, never activates linkage."""
import argparse
import hashlib
import itertools
import json
import pathlib
import time

def load(path):
    rows = [json.loads(line) for line in pathlib.Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]
    ids = [str(row["id"]) for row in rows]
    if len(ids) != len(set(ids)):
        raise ValueError("duplicate observation id")
    return rows

def evaluate(rows, passes, cap):
    seen = set()
    results = []
    # Build truth pairs within identity groups rather than scanning every row pair.
    # This avoids O(N^2) work for a corpus dominated by singleton identities.
    truth_groups = {}
    for row in rows:
        if row.get("truth_id") is not None:
            truth_groups.setdefault(str(row["truth_id"]), []).append(str(row["id"]))
    total_truth = {
        tuple(sorted(pair))
        for group in truth_groups.values()
        for pair in itertools.combinations(group, 2)
    }
    for fields in passes:
        groups = {}
        for row in rows:
            key = tuple(str(row.get(f, "")).strip().upper() for f in fields)
            if not all(key):
                continue
            groups.setdefault(key, []).append(str(row["id"]))
        start = time.perf_counter()
        fresh = set()
        suppressed = 0
        for group in groups.values():
            if len(group) > cap:
                suppressed += 1
                continue
            fresh.update(tuple(sorted(pair)) for pair in itertools.combinations(group, 2))
        delta = fresh - seen
        new_m = len(delta & total_truth)
        seen.update(delta)
        sizes = sorted(len(group) for group in groups.values())
        def pct(q):
            return sizes[min(len(sizes)-1, int((len(sizes)-1)*q))] if sizes else 0
        results.append({"fields":fields,"new_pairs":len(delta),"new_m":new_m,"new_u":len(delta)-new_m,"cumulative_pairs":len(seen),"recall":len(seen & total_truth)/len(total_truth) if total_truth else None,"fanout_p50":pct(.5),"fanout_p95":pct(.95),"fanout_p99":pct(.99),"fanout_max":max(sizes,default=0),"suppressed_cells":suppressed,"seconds":round(time.perf_counter()-start,6)})
    return {"eligible_m":len(total_truth),"captured_m":len(seen & total_truth),"unique_candidates":len(seen),"passes":results}

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--rows",required=True,help="JSONL observations, with id/truth_id and projected string fields")
    parser.add_argument("--configs",required=True,help="JSON array of {name,passes:[[field,...]],fanout_cap:int}")
    parser.add_argument("--out",required=True)
    args=parser.parse_args()
    rows=load(args.rows)
    configurations=json.loads(pathlib.Path(args.configs).read_text(encoding="utf-8"))
    if not isinstance(configurations,list) or not configurations:
        raise ValueError("nonempty config list required")
    report={"diagnostic_only":True,"selection_split":"TRAIN_OR_VALIDATION_ONLY","rows_sha256":hashlib.sha256(pathlib.Path(args.rows).read_bytes()).hexdigest(),"configs_sha256":hashlib.sha256(pathlib.Path(args.configs).read_bytes()).hexdigest(),"results":[]}
    for config in configurations:
        cap=int(config["fanout_cap"])
        passes=config["passes"]
        if cap<2 or not passes or any(not fields for fields in passes):
            raise ValueError("invalid pass/cap")
        report["results"].append({"name":config["name"],"fanout_cap":cap,**evaluate(rows,passes,cap)})
    pathlib.Path(args.out).write_text(json.dumps(report,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
if __name__=="__main__":
    main()
