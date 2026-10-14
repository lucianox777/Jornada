#!/usr/bin/env python3
"""Aggregate V2 synthetic corruption impact, without emitting identities or PII.

Labels denote ATTEMPTED/effective observable field edits; the common Bernoulli
draw is NOT logged and cannot be reconstructed from simultaneous labels.
Measures all pairwise true co-identity cross-gestor comparisons, independently
of candidate blocking, and reports all/no-CPF denominators separately.
"""
import argparse
import csv
import hashlib
import itertools
import json
from collections import Counter, defaultdict
from pathlib import Path

VERSION = "JORNADA_V2_SYNTHETIC_CORRUPTION_OBSERVED_AUDIT_V1"
REQUIRED = {"observacao_id","base_person_id","particao","gestor","cpf",
            "nome","nome_mae","data_nascimento","corrupcoes"}
STAGES = ("TRAIN","VALIDATION","TEST")
FIELDS = ("name_changed", "mother_changed", "dob_changed",
          "mother_missing", "dob_missing")
PAIR_FLAGS = ("any_observation_triple_changed",
              "any_observation_dob_changed_or_missing",
              "dob_disagrees_or_missing",
              "dob_disagrees_with_both_present",
              "person_names_disagree",
              "mother_names_disagree_or_missing")

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def flags(row):
    tokens = [t for t in row["corrupcoes"].split("|") if t]
    allowed=("NOME_DROP_CHAR","NOME_SWAP_ADJACENT","NOME_DOUBLE_CHAR",
             "NOME_TRUNCATE","MAE_DROP_CHAR","MAE_SWAP_ADJACENT",
             "MAE_DOUBLE_CHAR","MAE_TRUNCATE","MAE_MISSING",
             "DATE_YEAR","DATE_DAY","DATE_HEAPING","DATE_TRANSPOSE",
             "DATE_MISSING")
    if any(t not in allowed for t in tokens):
        raise ValueError("unknown V2 corruption label; new overlay needs separate audit")
    return {
        "name_changed":any(t.startswith("NOME_") for t in tokens),
        "mother_changed":any(t.startswith("MAE_") and t!="MAE_MISSING" for t in tokens),
        "dob_changed":any(t.startswith("DATE_") and t!="DATE_MISSING" for t in tokens),
        "mother_missing":"MAE_MISSING" in tokens,
        "dob_missing":"DATE_MISSING" in tokens,
    }

def analyze(rows):
    rows = list(rows)
    if not rows:
        raise ValueError("empty corpus")
    owner={}
    seen=set()
    groups=defaultdict(list)
    observations={field:0 for field in FIELDS}
    triple=0
    per_split=Counter()
    for r in rows:
        if not REQUIRED.issubset(r) or any(r[c] is None for c in REQUIRED):
            raise ValueError("missing required raw generator columns")
        oid,identity,split,manager=(r[x] for x in ("observacao_id","base_person_id",
                                                    "particao","gestor"))
        if not oid or not identity or not manager or split not in STAGES or oid in seen:
            raise ValueError("duplicate/invalid observation identity")
        seen.add(oid)
        if identity in owner and owner[identity]!=split:
            raise ValueError("identity leaked between splits")
        owner[identity]=split
        f=flags(r)
        for field in FIELDS:observations[field]+=int(f[field])
        triple+=int(all(f[c] for c in FIELDS[:3]))
        per_split[split]+=1
        groups[(split,identity)].append((r,f))
    pair_counts={"ALL":Counter(),"NO_CPF":Counter()}
    pair_denominators=Counter()
    for group in groups.values():
        for (a,fa),(b,fb) in itertools.combinations(group,2):
            if a["gestor"]==b["gestor"]:
                continue
            flags_pair={
                "any_observation_triple_changed":all(fa[c] for c in FIELDS[:3]) or all(fb[c] for c in FIELDS[:3]),
                "any_observation_dob_changed_or_missing":fa["dob_changed"] or fa["dob_missing"] or fb["dob_changed"] or fb["dob_missing"],
                "dob_disagrees_or_missing":not a["data_nascimento"] or not b["data_nascimento"] or a["data_nascimento"]!=b["data_nascimento"],
                "dob_disagrees_with_both_present":bool(a["data_nascimento"] and b["data_nascimento"] and a["data_nascimento"]!=b["data_nascimento"]),
                "person_names_disagree":a["nome"]!=b["nome"],
                "mother_names_disagree_or_missing":not a["nome_mae"] or not b["nome_mae"] or a["nome_mae"]!=b["nome_mae"]
            }
            for subset in ("ALL","NO_CPF"):
                if subset=="NO_CPF" and (a["cpf"].strip() or b["cpf"].strip()):
                    continue
                pair_denominators[subset]+=1
                for k,v in flags_pair.items(): pair_counts[subset][k]+=int(v)

    def count_pct(n,d):
        return {"count":n,"rate":(n/d if d else None)}
    n=len(rows)
    return {
        "version":VERSION,"observations":n,
        "observations_by_split":dict(sorted(per_split.items())),
        "effective_field_changes_per_observation":{
            k:count_pct(observations[k],n) for k in FIELDS},
        "triple_observable_corruption_per_observation":count_pct(triple,n),
        "true_pairs_across_managers":{
            k:{"pairs":pair_denominators[k],
               "rates":{flag:count_pct(pair_counts[k][flag],pair_denominators[k])
                        for flag in PAIR_FLAGS}}
            for k in ("ALL","NO_CPF")},
        "not_the_common_bernoulli_draw":True,
        "no_candidate_blocking_or_FDR":True,
        "test_used_to_select_parameters":False,
        "test_metrics_reported_for_data_quality_audit_only":True,
        "may_promote_v9_or_recalibrate":False
    }

def run(source,output,manifest=None):
    source,output=Path(source).resolve(),Path(output).resolve()
    if output.exists() or output==source:
        raise ValueError("refuse to overwrite source or existing evidence")
    profile=None
    if manifest is not None:
        m=json.loads(Path(manifest).read_text(encoding="utf8"))
        if m.get("ruleset_version")!="JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1":
            raise ValueError("unknown C# synthetic rules")
        outputs={x["path"]:x["sha256"].lower() for x in m.get("outputs",[])}
        if outputs.get("observacoes.csv")!=digest(source):
            raise ValueError("source SHA does not match frozen manifest")
        profile=m.get("error_profile")
    with source.open(encoding="utf-8-sig",newline="") as stream:
        report=analyze(csv.DictReader(stream))
    report.update({
        "observations_sha256":digest(source),
        "frozen_manifest_sha256":digest(manifest) if manifest is not None else None,
        "manifest_recorded_error_profile":profile,
        "profile_source_verified":profile is not None,
        "legacy_missing_profile_in_manifest":profile is None,
    })
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,sort_keys=True,indent=2,ensure_ascii=False)+"\n",
                      encoding="utf8")
    return report

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--observations",required=True)
    p.add_argument("--manifest")
    p.add_argument("--out",required=True)
    a=p.parse_args()
    r=run(a.observations,a.out,manifest=a.manifest)
    print(json.dumps({"version":r["version"],"observations":r["observations"],
                      "legacy_missing_profile_in_manifest":r["legacy_missing_profile_in_manifest"],
                      "pairs":r["true_pairs_across_managers"]["ALL"]["pairs"]},
                     sort_keys=True))
