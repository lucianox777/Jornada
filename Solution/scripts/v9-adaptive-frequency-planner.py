#!/usr/bin/env python3
"""Experimental per-query blocking plan using TRAIN cell frequencies. Never activates linkage."""
import argparse
import collections
import json
from pathlib import Path

DEFAULT_PASSES = [
    ("nome_normalizado", "data_nascimento"),
    ("nome_fonetico", "data_nascimento"),
    ("nome_mae_normalizado", "data_nascimento"),
    ("nome_mae_fonetico", "data_nascimento"),
    ("nome_normalizado", "nome_mae_normalizado"),
    ("nome_fonetico", "nome_mae_fonetico"),
]
def normalized(row, fields):
    key = tuple(str(row.get(field) or "").strip() for field in fields)
    return key if all(key) else None

def train_counts(rows, passes=DEFAULT_PASSES):
    required = {"nome_normalizado", "nome_fonetico", "nome_mae_normalizado", "nome_mae_fonetico"}
    if any(not required.issubset(row) for row in rows):
        raise ValueError("canonical normalized and phonetic name projections required")
    counts = {fields: collections.Counter() for fields in passes}
    identities = collections.defaultdict(list)
    for row in rows:
        if row.get("particao") != "TRAIN":
            continue
        if row.get("base_person_id") is None:
            raise ValueError("TRAIN base_person_id required")
        identities[str(row["base_person_id"])].append(row)
    for identity_rows in identities.values():
        for fields in passes:
            keys = {key for row in identity_rows if (key := normalized(row, fields))}
            for key in keys:
                counts[fields][key] += 1
    return counts

def plan(row, counts, cap=30, max_passes=3):
    if cap < 2 or max_passes < 1:
        raise ValueError("invalid cap/max_passes")
    ranked = []
    for fields, counter in counts.items():
        key = normalized(row, fields)
        if key is not None:
            frequency = counter.get(key, 0)
            # Zero means unseen, not impossible. Query it before ruling it out.
            ranked.append((frequency, fields, key))
    ranked.sort(key=lambda item: (item[0], item[1]))
    selected = []
    for frequency, fields, key in ranked:
        if len(selected) >= max_passes:
            break
        selected.append({"fields": list(fields), "key": list(key), "train_identity_frequency": frequency,
                         "over_cap_in_train": frequency > cap})
    return selected

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rows", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--cap", type=int, default=30)
    parser.add_argument("--max-passes", type=int, default=3)
    args = parser.parse_args()
    rows = [json.loads(line) for line in Path(args.rows).read_text(encoding="utf-8").splitlines() if line.strip()]
    counts = train_counts(rows)
    report = {"diagnostic_only": True, "frequency_source": "TRAIN_DISTINCT_TRUTH_IDENTITIES",
              "caution": "TRAIN frequency is not the live index fanout; do not drop over-cap passes without fallback",
              "plans": [{"id": row["observacao_id"], "split": row.get("particao"), "passes": plan(row, counts, args.cap, args.max_passes)} for row in rows]}
    Path(args.out).write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
if __name__ == "__main__":
    main()
