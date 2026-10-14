"""Query-side adaptive 19-cell plans from frozen TRAIN key frequencies.

For each identity/observation, rank *canonical indexed SQL cells* by the
rarest observed TRAIN distinct-base identity frequency of its keys. Evaluate
the chosen per-source cells against the exact real SQL pair sets. A pair can
be recovered if EITHER endpoint's plan queried one shared indexed cell.
This is observational diagnostic replay, not a measured SQL latency model.
"""
from collections import Counter,defaultdict

VERSION="V9_ADAPTIVE_CSHARP_19_CELL_SQL_EDGE_REPLAY_V1"
K_CHOICES=(3,5,8,12,19)


def plan_and_replay(rows,base,physical):
    # Identity-level presence means multiple observations of the SAME person
    # do not inflate the frequency of a nominal candidate index key.
    train_ident=defaultdict(set)
    cache={}
    ids={}
    for r in rows:
        if r["particao"] not in ("TRAIN","VALIDATION") or r["cpf"].strip():
            continue
        if (not base.meaningful(r["nome_normalizado"])
            or not (base.meaningful(r["nome_mae_normalizado"]) or
                    base.usable_birth(r["data_nascimento"]) is not None)):
            continue
        oid=r["observacao_id"]
        if oid in cache:
            raise ValueError("duplicate original canonical observation ID")
        keys=base.keys(r)
        cache[oid]=keys
        ids[oid]=(r["particao"],r["base_person_id"])
        if r["particao"]=="TRAIN":
            for name in base.EXPANDED:
                for value in keys[name]:
                    train_ident[r["base_person_id"]].add((name,value))
    counter=Counter(token for tokens in train_ident.values() for token in tokens)
    by_split={}
    for split in ("TRAIN","VALIDATION"):
        plans={}
        for oid,keys in cache.items():
            if ids[oid][0]!=split:
                continue
            frequencies=[]
            for cell in base.EXPANDED:
                values=keys[cell]
                if values:
                    frequencies.append((min(counter.get((cell,key),0)
                                            for key in values),cell))
            # Don't reinterpret the zero frequency as impossible: keys unseen
            # in TRAIN are still queried, subject to actual physical index.
            ordered=[name for _,name in sorted(frequencies,
                              key=lambda x:(x[0],x[1]))]
            plans[oid]=ordered
        result={k:set() for k in K_CHOICES}
        original=set()
        for cell in base.EXPANDED:
            key=(split,"COMPOSE_"+cell)
            if key not in physical:
                raise ValueError("original SQL pair set missing for "+cell)
            for a,b in physical[key]:
                original.add((a,b))
                if a not in plans or b not in plans:
                    raise ValueError("SQL candidate outside eligible key plan")
                for k in K_CHOICES:
                    if cell in plans[a][:k] or cell in plans[b][:k]:
                        result[k].add((a,b))
        if result[19]!=original:
            raise ValueError("adaptive full 19-cell plan must equal physical SQL union")
        for k0,k1 in zip(K_CHOICES,K_CHOICES[1:]):
            if not result[k0].issubset(result[k1]):
                raise ValueError("larger per-source k lost indexed candidate pairs")
        by_split[split]=result
    return by_split
