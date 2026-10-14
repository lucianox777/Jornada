#!/usr/bin/env python3
"""Indexed SQLite diagnostic for P prefix 1..4 and mother's first 1..4 chars.

Opt-in laboratory only; C# supplies FULL-name normalization. First-token
prefix slicing is a Python diagnostic proxy, not BrazilianNameComponents parity.
No TEST, operational policy, SQL Server latency, FS/TF, FDR or promotion.
"""
import argparse
import csv
import importlib.util
import json
import math
import sqlite3
from pathlib import Path

SOURCE = Path(__file__).with_name("v9-sqlite-memory-cells.py")
spec = importlib.util.spec_from_file_location("v9_sql_memory_baseline", SOURCE)
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)

VERSION = "V9_PERSON_PREFIX_MOTHER_INITIAL_SWEEP_V1"
CAP_LIMIT = 1000000


def variants():
    # The PMD choices are also available when D is valid but name of mother
    # differs past a very short initial. P+D passes never require a mother.
    plans = {}
    for p in (1, 2, 3, 4):
        for calendar in ("DOB", "YEAR_MONTH"):
            plans[f"P{p}_{calendar}"] = (p, 0, calendar)
            plans[f"P{p}_M1_{calendar}"] = (p, 1, calendar)
    for p in (2, 3, 4):
        for m in (2, 3, 4):
            plans[f"P{p}_M{m}_YEAR_MONTH"] = (p, m, "YEAR_MONTH")
        plans[f"P{p}_M1_MONTH_DAY"] = (p, 1, "MONTH_DAY")
    assert len(plans) == 28
    return plans


def single_key(row, shape):
    p_length, m_length, calendar = shape
    if not (base.meaningful(row["nome_normalizado"]) and
            (base.meaningful(row["nome_mae_normalizado"]) or
             base.usable_birth(row["data_nascimento"]) is not None)):
        return None
    person = base.parts(row["nome_normalizado"])
    mother = (base.parts(row["nome_mae_normalizado"]) if
              base.meaningful(row["nome_mae_normalizado"]) else [])
    if (not person or len(person[0]) < p_length or
            (m_length and (not mother or len(mother[0]) < m_length))):
        return None
    date = base.usable_birth(row["data_nascimento"])
    if date is None:
        return None
    if calendar == "DOB":
        d = date.isoformat()
    elif calendar == "YEAR_MONTH":
        d = f"{date.year:04d}-{date.month:02d}"
    elif calendar == "MONTH_DAY":
        d = f"{date.month:02d}-{date.day:02d}"
    else:
        raise ValueError("unknown calendar hypothesis")
    return (base.key(person[0][:p_length], mother[0][:m_length], d)
            if m_length else base.key(person[0][:p_length], d))


def preflight(connection, part, cell):
    query = """
        SELECT composite, manager, count(*)
        FROM k JOIN o USING (oid)
        WHERE part=? AND cell=?
        GROUP BY composite, manager ORDER BY composite, manager
    """
    last, size, squared, total, hottest = None, 0, 0, 0, 0

    def flush(n, squares):
        return (n*n - squares)//2

    for composite, manager, n in connection.execute(query, (part, cell)):
        if last != composite:
            total += flush(size, squared)
            hottest = max(hottest, size)
            last, size, squared = composite, 0, 0
        size += n
        squared += n*n
    total += flush(size, squared)
    hottest = max(hottest, size)
    if total < 0:
        raise ValueError("invalid SQL cross-manager candidate count")
    return total, hottest


def evaluate(rows, cap=CAP_LIMIT):
    if cap < 1 or cap > CAP_LIMIT:
        raise ValueError("unapproved candidate cap")
    base.validate_rows(rows)
    configs = variants()
    con = sqlite3.connect(":memory:")
    try:
        con.executescript("""
            CREATE TABLE o(
                oid TEXT PRIMARY KEY COLLATE BINARY,
                manager TEXT NOT NULL COLLATE BINARY
            );
            CREATE TABLE k(
                cell TEXT NOT NULL COLLATE BINARY,
                part TEXT NOT NULL COLLATE BINARY,
                composite TEXT NOT NULL COLLATE BINARY,
                oid TEXT NOT NULL COLLATE BINARY,
                PRIMARY KEY(cell,part,composite,oid)
            ) WITHOUT ROWID;
        """)
        obs, keys = [], []
        for row in rows:
            if (row["particao"] not in ("TRAIN","VALIDATION")
                    or row["cpf"].strip()):  # deterministic CPF excluded
                continue
            obs.append((row["observacao_id"], row["gestor"]))
            for label, spec in configs.items():
                k = single_key(row, spec)
                if k:
                    keys.append((label, row["particao"], k, row["observacao_id"]))
        con.executemany("INSERT INTO o VALUES(?,?)", obs)
        con.executemany("INSERT INTO k VALUES(?,?,?,?)", keys)
        join = """
            SELECT a.oid,b.oid
            FROM k a JOIN k b ON a.cell=b.cell
              AND a.part=b.part AND a.composite=b.composite AND a.oid<b.oid
            JOIN o oa ON oa.oid=a.oid
            JOIN o ob ON ob.oid=b.oid
            WHERE a.cell=? AND a.part=? AND oa.manager<>ob.manager
            LIMIT ?
        """
        results, collected = {}, {}
        for part in ("TRAIN", "VALIDATION"):
            subset = [r for r in rows if r["particao"] == part]
            all_truth = base.truth_for(subset, False)
            eligible_truth = base.truth_for(subset, True)
            count = sum(not r["cpf"].strip() and
                        base.meaningful(r["nome_normalizado"]) and
                        (base.meaningful(r["nome_mae_normalizado"]) or
                         base.usable_birth(r["data_nascimento"]) is not None)
                        for r in subset)
            if not all_truth or not eligible_truth or not count:
                raise ValueError("no independent M or eligible records: " + part)
            singles = {}
            for cell in configs:
                estimated, hottest = preflight(con, part, cell)
                if estimated > cap:
                    singles[cell] = {
                        "status": "CAP_EXCEEDED_UNMEASURED",
                        "raw_cross_manager_pairs": estimated,
                        "max_index_key_size": hottest,
                        "M": None, "U": None, "incremental_M": None,
                        "incremental_U": None
                    }
                    continue
                pairs = set(con.execute(join, (cell, part, cap + 1)))
                if len(pairs) != estimated:
                    raise ValueError("SQL preflight vs actual pair count inconsistent")
                collected[part, cell] = pairs
                summary = base.measure(pairs, eligible_truth, count)
                summary.update({
                    "status": "MEASURED_INDEXED_SQLITE",
                    "raw_cross_manager_pairs": estimated,
                    "max_index_key_size": hottest,
                    "recall_on_all_M_without_cpf": summary["M"] / len(all_truth)
                })
                singles[cell] = summary

            anchor = collected.get((part, "P3_DOB"))
            if anchor is None:
                raise ValueError("P3_DOB reference exceeded cap; cannot compare")
            for cell, info in singles.items():
                if info["status"] != "MEASURED_INDEXED_SQLITE":
                    continue
                increment = collected[part, cell] - anchor
                union = collected[part, cell] | anchor
                info["incremental_M_vs_P3_DOB"] = len(increment & eligible_truth)
                info["incremental_U_vs_P3_DOB"] = len(increment - eligible_truth)
                info["union_M_with_P3_DOB"] = len(union & eligible_truth)
                info["union_U_with_P3_DOB"] = len(union - eligible_truth)
            results[part] = {
                "all_no_cpf_truth_M": len(all_truth),
                "name_eligible_truth_M": len(eligible_truth),
                "name_eligible_observations": count,
                "variants": singles,
            }
        return {
            "version": VERSION,
            "candidate_budget_cap": cap,
            "engine": "SQLITE_REAL_INDEXED_SINGLE_KEY_PER_CELL",
            "name_projection": "CSHARP_FULLNAME_NORMALIZED_PYTHON_FIRST_TOKEN_SLICE_PROXY",
            "split_selection": "NONE_HYPOTHESES_PRE_REGISTERED",
            "results": results,
            "TEST_evaluated": False, "fdr_measured": False,
            "sql_server_latency_measured": False,
            "city_population_scaled": False, "may_promote_policy": False,
            "baseline": "P3_DOB_ONLY_NOT_EXISTING_10_CELL_OPTIMIZED_UNION",
        }
    finally:
        con.close()


def run(snapshot, baseline_report, output, cap=CAP_LIMIT):
    snapshot, baseline_report, output = (Path(x).resolve() for x in
                                         (snapshot, baseline_report, output))
    if output.exists() or snapshot == output or snapshot in output.parents:
        raise ValueError("never overwrite frozen snapshot or prior report")
    manifest = json.loads((snapshot / "manifest.json").read_text(encoding="utf8"))
    if (manifest.get("frozen") is not True
        or manifest.get("projection_version") != base.PROJECTION
        or manifest.get("version") != "V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1"
        or manifest.get("test_used_for_frequency") is not False):
        raise ValueError("wrong frozen C# snapshot")
    for filename, name in (("canonical_observations.csv","frozen_source_sha256"),
                           ("train_name_frequencies.json","frequency_sha256"),
                           ("train_joint_name_frequencies.json","joint_frequency_sha256")):
        if base.digest(snapshot/filename) != manifest.get(name):
            raise ValueError("immutable source or TRAIN frequency SHA256 mismatch")
    compared = json.loads(baseline_report.read_text(encoding="utf8"))
    if (compared.get("canonical_observations_sha256") !=
        manifest["frozen_source_sha256"] or
        compared.get("TEST_evaluated") is not False or
        compared.get("selection_split") != "TRAIN_ONLY" or
        compared.get("diagnostic_only") is not True):
        raise ValueError("baseline evidence not on same source / split frame")
    with (snapshot/"canonical_observations.csv").open(encoding="utf-8-sig",newline="") as f:
        rows = list(csv.DictReader(f))
    report = evaluate(rows, cap)
    report["canonical_observations_sha256"] = manifest["frozen_source_sha256"]
    report["frozen_manifest_sha256"] = base.digest(snapshot/"manifest.json")
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,sort_keys=True,indent=2,ensure_ascii=False)+"\n",
                      encoding="utf8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--snapshot-dir", required=True)
    p.add_argument("--baseline-report", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--cap", type=int, default=CAP_LIMIT)
    a = p.parse_args()
    r = run(a.snapshot_dir,a.baseline_report,a.out,a.cap)
    print(json.dumps({"version":r["version"],
                      "sha256":r["canonical_observations_sha256"],
                      "measured":{part:sum(cell["status"] == "MEASURED_INDEXED_SQLITE"
                             for cell in val["variants"].values())
                                  for part,val in r["results"].items()}},
                     sort_keys=True))
