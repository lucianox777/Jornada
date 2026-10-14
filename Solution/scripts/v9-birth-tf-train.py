#!/usr/bin/env python3
"""Diagnostic TRAIN-only population frequencies for missing V9 birth units.

Not a calibrated TF/LLR adjustment; no production activation.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path

def audit(rows):
    train = [r for r in rows if r["split"] == "TRAIN"]
    if not train:
        raise ValueError("TRAIN population missing")
    persons = {}
    for row in train:
        identity = str(row["truth_id"])
        if not identity:
            raise ValueError("empty truth id")
        birth = str(row.get("birth_date") or "").strip()
        year = str(row.get("birth_year") or birth[:4]).strip()
        current = (birth, year)
        if identity in persons and persons[identity] != current:
            raise ValueError("inconsistent birth data for base identity")
        persons[identity] = current
    result = {"status":"TRAIN_BASE_FREQUENCY_DIAGNOSTIC_NOT_CALIBRATED_TF",
              "train_base_people":len(persons),"units":{}}
    for name, index in (("birth_date",0),("birth_year",1)):
        counts = collections.Counter(pair[index] for pair in persons.values() if pair[index])
        present = sum(counts.values())
        result["units"][name] = {
            "present_base_people":present,
            "missing_base_people":len(persons)-present,
            "distinct_values":len(counts),
            "singleton_values":sum(v==1 for v in counts.values()),
            "values_observed_at_least_five":sum(v>=5 for v in counts.values()),
            "largest_value_count":max(counts.values(),default=0),
            "largest_value_share":max(counts.values(),default=0)/present if present else None,
            "frequencies_sha256":hashlib.sha256(json.dumps(sorted(counts.items()),ensure_ascii=False).encode()).hexdigest()}
    return result

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--rows",required=True)
    p.add_argument("--out",required=True)
    args=p.parse_args()
    rows=[json.loads(line) for line in Path(args.rows).read_text(encoding="utf-8").splitlines() if line.strip()]
    Path(args.out).write_text(json.dumps(audit(rows),indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
if __name__=="__main__":
    main()
