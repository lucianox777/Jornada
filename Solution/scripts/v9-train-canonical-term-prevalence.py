#!/usr/bin/env python3
"""TRAIN-only, aggregate term-incidence audit of C# V2 surname projections.

This is synthetic per-base-person prevalence, NOT published IBGE TF, a joint
distribution, a blocking candidate census, or an FS/LLR calibration.
"""
import argparse
import csv
import hashlib
import json
from collections import defaultdict
from pathlib import Path

VERSION = "V9_TRAIN_CANONICAL_TERM_PREVALENCE_V1"
PROJECTION = "V9_CANONICAL_CSV_EXPORTER_CSHARP_SURNAMES_V2"
ATTRIBUTES = (
    "NOME_PRENOME", "NOME_MAE_PRENOME",
    "NOME_ULTIMO_SOBRENOME", "NOME_MAE_ULTIMO_SOBRENOME",
)
REQUIRED = {
    "observacao_id", "base_person_id", "particao",
    "nome_normalizado", "nome_mae_normalizado",
    "nome_ultimo_sobrenome", "nome_sobrenomes_json",
    "nome_mae_ultimo_sobrenome", "nome_mae_sobrenomes_json",
}
SENTINELS = {"NAO INFORMADO", "NÃO INFORMADO", "DESCONHECIDO",
             "IGNORADO", "SEM INFORMACAO", "SEM INFORMAÇÃO",
             "NULO", "NULL", "N/A", "NAO CONSTA", "NÃO CONSTA"}


def percentile(sorted_values, percent):
    if not sorted_values:
        return None
    return sorted_values[(len(sorted_values) - 1) * percent // 100]


def first(value):
    value = " ".join(value.strip().split())
    return value.split()[0] if value and value not in SENTINELS else None


def surname(row, prefix):
    last = row[prefix + "_ultimo_sobrenome"]
    try:
        terms = json.loads(row[prefix + "_sobrenomes_json"])
    except (ValueError, TypeError) as exc:
        raise ValueError("malformed canonical C# surname list") from exc
    if (not isinstance(terms, list)
        or any(not isinstance(v, str) or not v.strip() or v != v.strip()
               for v in terms)
        or len(set(terms)) != len(terms)
        or bool(last) != bool(terms)
        or (terms and terms[0] != last)):
        raise ValueError("noncanonical C# last surname or surname list")
    return last or None


def assess(rows):
    rows = list(rows)
    if not rows:
        raise ValueError("empty TRAIN input")
    if any(not REQUIRED.issubset(row) for row in rows):
        raise ValueError("C# V2 canonical surname columns required; no proxy fallback")
    seen = set()
    identities = set()
    incidences = {attr: defaultdict(set) for attr in ATTRIBUTES}
    for row in rows:
        if row["particao"] != "TRAIN":
            raise ValueError("TRAIN-only input required; TEST and VALIDATION are sealed")
        oid, base = row["observacao_id"], row["base_person_id"]
        if not oid or oid in seen or not base:
            raise ValueError("invalid or duplicate observation/base identity")
        seen.add(oid)
        identities.add(base)
        values = (
            first(row["nome_normalizado"]),
            first(row["nome_mae_normalizado"]),
            surname(row, "nome"),
            surname(row, "nome_mae"),
        )
        for attribute, value in zip(ATTRIBUTES, values):
            if value:
                incidences[attribute][value].add(base)
    n = len(identities)
    results = {}
    for attribute, terms in incidences.items():
        counts = sorted(len(owners) for owners in terms.values())
        covered = len(set().union(*terms.values())) if terms else 0
        # Each term counted once per identity. A person may have several
        # observed variants, so the incidence sum is NOT a probability mass.
        results[attribute] = {
            "distinct_terms": len(counts),
            "identities_with_term": covered,
            "identity_coverage": covered / n,
            "term_identity_incidences": sum(counts),
            "singleton_terms": sum(c == 1 for c in counts),
            "p50_identities_per_term": percentile(counts, 50),
            "p95_identities_per_term": percentile(counts, 95),
            "p99_identities_per_term": percentile(counts, 99),
            "max_identities_per_term": counts[-1] if counts else None,
            "term_collision_mass_sum_p_squared":
                sum((c / n) ** 2 for c in counts),
            "published_ibge_frequency_verified": False,
        }
    return {
        "version": VERSION,
        "canonical_projection_version": PROJECTION,
        "partition": "TRAIN",
        "distinct_base_persons": n,
        "observations": len(rows),
        "synthetic_prevalence_only": True,
        "test_evaluated": False,
        "llr_weight_or_joint_tf_estimated": False,
        "warning": "Synthetic TRAIN base-person term incidence, not IBGE publication, joint TF, deduplicated candidate U, or calibrated LLR. No TF for missing terms.",
        "attributes": results,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--canonical-csv", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    source = Path(args.canonical_csv)
    target = Path(args.out)
    if source.resolve() == target.resolve():
        raise ValueError("cannot overwrite canonical source")
    with source.open(newline="", encoding="utf-8-sig") as handle:
        result = assess(csv.DictReader(handle))
    result["source_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                      encoding="utf-8")


if __name__ == "__main__":
    main()
