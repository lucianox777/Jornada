#!/usr/bin/env python3
"""JORNADA_SYNTH_CORPUS_V2.

Benchmark sintético controlado para o Calibrador. Não é dado operacional e não
substitui o benchmark nominal IBGE. O gabarito de m é EMPÍRICO, calculado depois
da materialização das observações.
"""

import argparse, csv, gzip, hashlib, itertools, json, os, random
from bisect import bisect
from collections import defaultdict
from datetime import date, timedelta

SURNAME_PARTICLES = ["da", "de", "do", "dos", "das"]


def load_vocab(path, tipo, min_freq):
    out = []
    h = hashlib.sha256()
    with gzip.open(path, "rb") as raw:
        for line in raw:
            h.update(line)
            row = json.loads(line.decode("utf-8"))
            if row.get("tipo") == tipo and int(row["frequencia"]) >= min_freq:
                out.append((row["valor"], int(row["frequencia"])))
    if not out:
        raise SystemExit(f"vocabulário vazio: {tipo}")
    return out, h.hexdigest()


class FreqSampler:
    def __init__(self, pairs, tail_boost, tail_quantile, rng):
        self.rng = rng
        pairs = sorted(pairs, key=lambda x: -x[1])
        cut = pairs[int(len(pairs) * (1 - tail_quantile))][1] if tail_boost != 1 else 0
        self.values, self.cum, self.correction = [], [], {}
        acc = 0.0
        for value, freq in pairs:
            boost = tail_boost if tail_boost != 1 and freq <= cut else 1.0
            acc += freq * boost
            self.values.append(value)
            self.cum.append(acc)
            self.correction[value] = 1.0 / boost
        self.total = acc

    def draw(self):
        return self.values[bisect(self.cum, self.rng.random() * self.total)]

    def weight(self, value):
        return self.correction.get(value, 1.0)


def cpf_valid(rng):
    while True:
        d = [rng.randrange(10) for _ in range(9)]
        if len(set(d)) == 1:
            continue
        s = sum(x * w for x, w in zip(d, range(10, 1, -1)))
        dv1 = 0 if 11 - (s % 11) >= 10 else 11 - (s % 11)
        s = sum(x * w for x, w in zip(d + [dv1], range(11, 1, -1)))
        dv2 = 0 if 11 - (s % 11) >= 10 else 11 - (s % 11)
        return "".join(map(str, d + [dv1, dv2]))


def cns_valid(rng):
    # Forma provisória 7/8/9: 15 dígitos e soma ponderada 15..1 divisível por 11.
    prefix = [rng.choice([7, 8, 9])] + [rng.randrange(10) for _ in range(13)]
    partial = sum(x * w for x, w in zip(prefix, range(15, 1, -1)))
    last = (-partial) % 11
    if last > 9:
        return cns_valid(rng)
    return "".join(map(str, prefix + [last]))


def invalidate_check_digit(value):
    if not value:
        return value
    return value[:-1] + str((int(value[-1]) + 1) % 10)


def corrupt_text(s, rng):
    if len(s) < 4:
        return s, None
    op = rng.choice(["DROP_CHAR", "SWAP_ADJACENT", "DOUBLE_CHAR", "TRUNCATE"])
    if op == "DROP_CHAR":
        i = rng.randrange(1, len(s) - 1); return s[:i] + s[i + 1:], op
    if op == "SWAP_ADJACENT" and len(s) >= 5:
        i = rng.randrange(1, len(s) - 2); return s[:i] + s[i+1] + s[i] + s[i+2:], op
    if op == "DOUBLE_CHAR":
        i = rng.randrange(1, len(s) - 1); return s[:i] + s[i] + s[i:], op
    if len(s) >= 8:
        return s[:rng.randrange(5, len(s) - 1)], op
    return s, None


def corrupt_date(d, rng):
    op = rng.choice(["DATE_TRANSPOSE", "DATE_YEAR", "DATE_DAY", "DATE_HEAPING"])
    try:
        if op == "DATE_TRANSPOSE": return date(d.year, d.day, d.month), op
        if op == "DATE_YEAR": return date(d.year + rng.choice([-10, -1, 1, 10]), d.month, d.day), op
        if op == "DATE_DAY": return date(d.year, d.month, min(28, max(1, d.day + rng.choice([-1, 1])))), op
        return date(d.year, d.month, rng.choice([1, 15])), op
    except ValueError:
        return d, None


PROFILES = {
    "clean": dict(p_name=.06, p_mother=.10, p_date=.04, p_common=.00, p_missing_mother=.05, p_missing_date=.01),
    "independent": dict(p_name=.22, p_mother=.30, p_date=.15, p_common=.00, p_missing_mother=.18, p_missing_date=.05),
    "correlated": dict(p_name=.12, p_mother=.16, p_date=.08, p_common=.18, p_missing_mother=.18, p_missing_date=.05),
    "field": dict(p_name=.35, p_mother=.45, p_date=.28, p_common=.22, p_missing_mother=.40, p_missing_date=.12),
}


def make_person(pid, first, surname, rng, cpf_prev, cns_prev):
    s = [surname.draw() for _ in range(rng.choices([1,2,3],[35,45,20])[0])]
    if len(s) > 1 and rng.random() < .45: s.insert(1, rng.choice(SURNAME_PARTICLES))
    f = first.draw(); f2 = None
    if rng.random() < .18: f2 = first.draw(); f += " " + f2
    mf = first.draw(); mother_extra = surname.draw() if rng.random() < .4 else None
    mother = mf + (" " + mother_extra if mother_extra else "") + " " + s[-1]
    y = rng.randint(1935, 2020); dob = date(y,1,1) + timedelta(days=rng.randrange(365))
    components = [first.weight(x) for x in f.split()] + [surname.weight(x) for x in s if x not in SURNAME_PARTICLES]
    components += [first.weight(mf), surname.weight(s[-1])]
    return dict(base_person_id=pid, nome=f+" "+" ".join(s), nome_mae=mother, data_nascimento=dob,
                sexo=rng.choice(["M","F"]), cpf=cpf_valid(rng) if rng.random()<cpf_prev else None,
                cns=cns_valid(rng) if rng.random()<cns_prev else None,
                evaluation_weight=round(__import__('math').prod(components), 8))


def observe(p, rng, cfg, gestor, seq, cpf_retention, cns_retention):
    o = {k:p[k] for k in ("base_person_id","nome","nome_mae","data_nascimento","sexo","cpf","cns","evaluation_weight")}
    o.update(observacao_id=f"{p['base_person_id']}-{gestor}-{seq}", gestor=gestor)
    labels=[]; common = rng.random() < cfg["p_common"]
    for key, prob, prefix in (("nome",cfg["p_name"],"NOME"),("nome_mae",cfg["p_mother"],"MAE")):
        if common or rng.random() < prob:
            v, lab = corrupt_text(o[key], rng)
            if lab and v != o[key]: o[key]=v; labels.append(prefix+"_"+lab)
    if common or rng.random() < cfg["p_date"]:
        v, lab = corrupt_date(o["data_nascimento"], rng)
        if lab and v != o["data_nascimento"]: o["data_nascimento"]=v; labels.append(lab)
    if rng.random() < cfg["p_missing_mother"]: o["nome_mae"]=None; labels.append("MAE_MISSING")
    if rng.random() < cfg["p_missing_date"]: o["data_nascimento"]=None; labels.append("DATE_MISSING")
    if o["cpf"] and rng.random() > cpf_retention: o["cpf"]=None
    if o["cns"] and rng.random() > cns_retention: o["cns"]=None
    o["corrupcoes"]="|".join(labels)
    return o


def empirical_m(obs):
    by = defaultdict(list)
    for o in obs: by[o["base_person_id"]].append(o)
    mapping = {"NOME":"nome","NOME_MAE":"nome_mae","NASCIMENTO":"data_nascimento"}
    result={}
    for label, field in mapping.items():
        eligible=exact=0; weighted_den=weighted_num=0.0
        for rows in by.values():
            for a,b in itertools.combinations(rows,2):
                if a[field] in (None,"") or b[field] in (None,""): continue
                eligible += 1
                w=(float(a["evaluation_weight"])+float(b["evaluation_weight"]))/2
                weighted_den += w
                if a[field] == b[field]: exact += 1; weighted_num += w
        result[label] = {"eligible_pairs":eligible,"exact_pairs":exact,
                         "m_exact_empirical": round(exact/eligible,6) if eligible else None,
                         "m_exact_empirical_reweighted": round(weighted_num/weighted_den,6) if weighted_den else None}
    return result


def apply_cns_scenarios(people, rng, invalid_rate, reuse_rate, dob_conflict_rate):
    with_cns=[p for p in people if p["cns"]]
    for p in rng.sample(with_cns, min(len(with_cns), round(len(with_cns)*invalid_rate))):
        p["cns"]=invalidate_check_digit(p["cns"]); p["cns_scenario"]="INVALID_CHECK_DIGIT"
    valid=[p for p in with_cns if p.get("cns_scenario") is None]
    n_reuse=min(len(valid)//2, round(len(valid)*reuse_rate))
    for i in range(n_reuse):
        src,dst=valid[2*i],valid[2*i+1]; dst["cns"]=src["cns"]; dst["cns_scenario"]="REUSED"; src["cns_scenario"]="REUSED"
    remaining=[p for p in valid[2*n_reuse:] if p.get("cns_scenario") is None]
    n_conf=min(len(remaining)//2, round(len(remaining)*dob_conflict_rate))
    used=set()
    for a in remaining:
        if len(used)//2 >= n_conf: break
        candidates=[b for b in remaining if b is not a and b["base_person_id"] not in used and abs((a["data_nascimento"]-b["data_nascimento"]).days)>=3650]
        if not candidates: continue
        b=rng.choice(candidates); b["cns"]=a["cns"]; a["cns_scenario"]=b["cns_scenario"]="DOB_CONFLICT_REUSE"; used|={a["base_person_id"],b["base_person_id"]}
    for p in people: p.setdefault("cns_scenario","CLEAN" if p["cns"] else "ABSENT")


def write_csv(path, rows, cols):
    with open(path,"w",newline="",encoding="utf-8") as fh:
        w=csv.DictWriter(fh,fieldnames=cols,extrasaction="ignore"); w.writeheader()
        for r in rows:
            x=dict(r)
            for k,v in list(x.items()):
                if isinstance(v,date): x[k]=v.isoformat()
            w.writerow(x)


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--ibge-source", required=True, help="frequencia-brasil.ndjson.gz")
    ap.add_argument("--out", default="./corpus-v2"); ap.add_argument("--people",type=int,default=20000); ap.add_argument("--seed",type=int,default=42)
    ap.add_argument("--error-profile",choices=PROFILES,default="correlated"); ap.add_argument("--min-freq",type=int,default=20); ap.add_argument("--tail-oversample",type=float,default=1.0)
    ap.add_argument("--cpf-base-prevalence",type=float,default=.22); ap.add_argument("--cns-base-prevalence",type=float,default=.55)
    ap.add_argument("--cpf-observation-retention",type=float,default=.55); ap.add_argument("--cns-observation-retention",type=float,default=.70)
    ap.add_argument("--cns-invalid-rate",type=float,default=.01); ap.add_argument("--cns-reuse-rate",type=float,default=.01); ap.add_argument("--cns-dob-conflict-rate",type=float,default=.01)
    ap.add_argument("--gestores",type=int,default=4)
    a=ap.parse_args(); rng=random.Random(a.seed); os.makedirs(a.out,exist_ok=True)
    first,fp1=load_vocab(a.ibge_source,"NOME",a.min_freq); sur,fp2=load_vocab(a.ibge_source,"SOBRENOME",a.min_freq)
    fs=FreqSampler(first,a.tail_oversample,.25,rng); ss=FreqSampler(sur,a.tail_oversample,.25,rng); cfg=PROFILES[a.error_profile]
    people=[]
    for i in range(a.people):
        p=make_person(f"P{i:07d}",fs,ss,rng,a.cpf_base_prevalence,a.cns_base_prevalence)
        r=rng.random(); p["particao"]="TRAIN" if r<.6 else ("VALIDATION" if r<.8 else "TEST"); people.append(p)
    apply_cns_scenarios(people,rng,a.cns_invalid_rate,a.cns_reuse_rate,a.cns_dob_conflict_rate)
    obs=[]
    for p in people:
        n=rng.choices([1,2,3,4],[40,34,18,8])[0]; gestores=rng.sample(range(a.gestores),min(n,a.gestores))
        for k in range(n):
            o=observe(p,rng,cfg,f"G{gestores[k%len(gestores)]}",k,a.cpf_observation_retention,a.cns_observation_retention); o["particao"]=p["particao"]; obs.append(o)
    write_csv(os.path.join(a.out,"pessoas_verdade.csv"),people,["base_person_id","particao","nome","nome_mae","data_nascimento","sexo","cpf","cns","cns_scenario","evaluation_weight"])
    write_csv(os.path.join(a.out,"observacoes.csv"),obs,["observacao_id","base_person_id","particao","gestor","nome","nome_mae","data_nascimento","sexo","cpf","cns","corrupcoes","evaluation_weight"])
    truth={"generator_version":"JORNADA_SYNTH_CORPUS_V2","seed":a.seed,"people":len(people),"observations":len(obs),"error_profile":a.error_profile,
           "ibge":{"source_sha256_name_pass":fp1,"source_sha256_surname_pass":fp2,"min_freq":a.min_freq,"tail_oversample":a.tail_oversample},
           "identifier_model":{"cpf_base_prevalence":a.cpf_base_prevalence,"cns_base_prevalence":a.cns_base_prevalence,"cpf_observation_retention":a.cpf_observation_retention,"cns_observation_retention":a.cns_observation_retention,
                               "cns_invalid_rate":a.cns_invalid_rate,"cns_reuse_rate":a.cns_reuse_rate,"cns_dob_conflict_rate":a.cns_dob_conflict_rate},
           "declared_corruption_rates":cfg,"empirical_m_exact":empirical_m(obs),
           "invariants":["base_person_id is truth-only; forbidden in blocking/scoring","missing pairs excluded from field m denominator","CPF/CNS label source and derivatives forbidden from candidate generation and scoring","CNS scenarios are benchmark evidence only and never authorize UUID resolution"]}
    with open(os.path.join(a.out,"gabarito.json"),"w",encoding="utf-8") as fh: json.dump(truth,fh,indent=2,ensure_ascii=False)
    print(json.dumps(truth["empirical_m_exact"],indent=2,ensure_ascii=False))

if __name__ == "__main__": main()
