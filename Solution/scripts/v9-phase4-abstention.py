"""Paired, nonpublishing DEV evaluation. replay() has no access to truth or SQL."""
from collections import Counter, defaultdict
from decimal import Decimal
from math import comb


VETO = "VETO_DEV_POS_R1"


def require(ok, check):
    if not ok:
        raise ValueError("V9_E2E_ORACLE_ABSTENTION_" + check)


def index(rows, field):
    mapped = {row[field]: row for row in rows}
    require(len(mapped) == len(rows), "DUPLICATE_KEY")
    return mapped


def pair_key(row):
    return row["Source"], row["Person"], row["Record"]


def verified_pairs(scores, pairs, graphs, prior, member):
    expected = {pair_key(s): (s["LogOdds"], s["Posterior"]) for s in scores}
    require(len(expected) == len(scores) == len(pairs) == member["secureRecords"] == member["goldRecordScores"], "PAIR_TOTAL")
    by_source = defaultdict(list)
    for row in pairs:
        src, dest, rec = pair_key(row)
        l, p = row["LogOdds"], row["Posterior"]
        require(src in graphs and dest in graphs[src]["candidates"] and rec in prior
                and (src, dest, rec) in expected and expected.pop((src, dest, rec)) == (l, p)
                and (l is None) == (p is None)
                and (l is None or (type(l) in (int, Decimal) and type(p) in (int, Decimal) and 0 <= p <= 1))
                and row["Birth"] in {"equal", "different", "missing"}
                and all(type(row[k]) is bool for k in
                        ("ExactFull", "SourceMotherPresent", "RecordMotherPresent", "MotherExactFull"))
                and (not row["MotherExactFull"] or (row["SourceMotherPresent"] and row["RecordMotherPresent"])), "PAIR_EVIDENCE")
        by_source[src].append(row)
    require(not expected, "PAIR_COVERAGE")
    return by_source


def pair_matches(row):
    return (row["ExactFull"] and row["Birth"] == "different" and row["SourceMotherPresent"]
            and not row["RecordMotherPresent"])


def replay(census, before, ranks, graphs, scores, pairs, member, protected_methods):
    """Select only vetoes of official new associations. Freeze the official map before applying them."""
    current = index(census, "observationId")
    prior = index(before, "Id")
    ranks = index(ranks, "Id")
    graphs = index(graphs, "observationId")
    eligible = {i for i, row in current.items() if not row["hasCpf"]}
    require(current.keys() == prior.keys() and ranks.keys() == graphs.keys() == eligible
            and len(eligible) == member["observations"] and member["status"] == "PASS"
            and member["independentR1DecisionVerified"] is True and member["actualGoldRecordScoresVerified"] is True, "UNIVERSE")
    records = verified_pairs(scores, pairs, graphs, prior, member)
    official = {i: row["currentUuid"] if row["currentStatus"] == "RESOLVIDO" else None for i, row in current.items()}
    protected = {i for i, pre in prior.items() if pre["Current"] is not None or pre["Canonical"] is not None
                 or pre["Status"] == "RESOLVIDO" or pre["State"] == "REFERENCIA" or pre["Method"] in protected_methods
                 or current[i]["currentMethod"] in protected_methods}
    new = {}
    propagated = {}
    reasons = {}
    near = Counter()
    candidate_total = 0
    for i in eligible:
        row, rank = current[i], ranks[i]
        candidates = graphs[i]["candidates"]
        evidence = records[i]
        valid = [r for r in evidence if r["LogOdds"] is not None]
        unscored = len(candidates) - len({r["Person"] for r in valid})
        require(rank["Status"] in {"RESOLVIDO", "NAO_RESOLVIDO", "CONFLITO"}
                and (rank["Resolved"] is not None) == (rank["Status"] == "RESOLVIDO")
                and len(set(candidates)) == len(candidates) == rank["CandidateCount"]
                and len(valid) == rank["ScoreableRecords"]
                and unscored == rank["PeopleWithoutScoreableEvidence"], "RANK")
        candidate_total += len(candidates)
        if i in protected:
            continue
        if row["runResult"] != "ASSOCIACAO_EXISTENTE":
            require(row["publicationReason"] != "ASSOCIACAO_EXISTENTE_LINKAGE", "PUBLICATION_REASON")
            continue
        if row["publicationReason"] == "REFERENCIA_PROGRESSIVA_PROPAGADA_NA_ORIGEM":
            require(row["currentMethod"] == "LINKAGE_PROBABILISTICO"
                    and official[i] is not None and row["publishedUuid"] == official[i], "PROPAGATED_LINK")
            propagated[i] = official[i]
            continue
        require(row["publicationReason"] == "ASSOCIACAO_EXISTENTE_LINKAGE", "EXISTING_REASON")
        dest = official[i]
        require(row["currentMethod"] == "LINKAGE_PROBABILISTICO"
                and row["runResult"] == "ASSOCIACAO_EXISTENTE" and dest is not None
                and dest == row["publishedUuid"] == rank["Resolved"] == rank["Best"]
                and dest in candidates and rank["Status"] == "RESOLVIDO" and unscored == 0, "LINK")
        new[i] = dest
        if len(candidates) != 1:
            near["multipleCandidates"] += 1
            continue
        highest = max(r["LogOdds"] for r in valid)
        top = [r for r in valid if r["LogOdds"] == highest]
        matches = sum(map(pair_matches, top))
        near["tiedMaxima"] += len(top) > 1
        if matches == len(top):
            reasons[i] = VETO
            near["unanimousTiedVeto"] += len(top) > 1
        elif matches:
            near["mixedMaximaKept"] += 1
        else:
            near["noMatchingMaximumKept"] += 1
            # Nonexclusive diagnostic counts, one count per decision per condition.
            near["maximumNameNotExact"] += any(not r["ExactFull"] for r in top)
            near["maximumBirthEqual"] += any(r["Birth"] == "equal" for r in top)
            near["maximumBirthMissing"] += any(r["Birth"] == "missing" for r in top)
            near["maximumSourceMotherMissing"] += any(not r["SourceMotherPresent"] for r in top)
            near["maximumRecordMotherPresent"] += any(r["RecordMotherPresent"] for r in top)
    require(candidate_total == member["candidatePeople"], "CANDIDATE_TOTAL")
    # The SQL batch preserves the leader's publication reason. Only the other
    # versions of its persistent origin can have this propagated reference.
    leaders = defaultdict(list)
    for i in eligible:
        row = current[i]
        if official[i] is not None and row["runResult"] in {"ASSOCIACAO_EXISTENTE", "NOVA_IDENTIDADE"} and row[
                "publicationReason"] in {"ASSOCIACAO_EXISTENTE_LINKAGE", "NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA",
                                         "REFERENCIA_PROGRESSIVA_CONFIRMADA", "REFERENCIA_PROGRESSIVA_PRESERVADA",
                                         "REFERENCIA_PROGRESSIVA_PRESERVADA_DESTINO_DIVERGENTE"}:
            leaders[prior[i]["Origin"]].append(i)
    founders = {}
    for i, dest in propagated.items():
        origin_leaders = leaders[prior[i]["Origin"]]
        require(len(origin_leaders) == 1, "PROPAGATED_ORIGIN")
        leader = origin_leaders[0]
        require(official[leader] == current[leader]["publishedUuid"] == dest, "PROPAGATED_DESTINATION")
        if current[leader]["runResult"] == "NOVA_IDENTIDADE":
            founders[i] = leader
    variant = official.copy()
    for i in reasons:
        variant[i] = None
    changed = {i for i in official if variant[i] != official[i]}
    require(changed == reasons.keys() and changed <= new.keys() and not changed & protected
            and all(official[i] is not None and variant[i] is None for i in changed)
            and all(dest is None or dest == official[i] for i, dest in variant.items())
            and all(i not in changed for i, rank in ranks.items() if rank["Status"] != "RESOLVIDO"), "SUBSET")
    conflict_groups = {graphs[i]["groupRoot"] for i, rank in ranks.items()
                       if rank.get("Reason") == "V9_R1_GRUPO_GOLD_CONFLITANTE"}
    conflict_versions = {i for i, graph in graphs.items() if graph["groupRoot"] in conflict_groups}
    require(not changed & conflict_versions, "R1_CONFLICT")
    return dict(official=official, variant=variant, new=new, reasons=reasons, records=records,
                propagated=propagated, founders=founders, ranks=ranks,
                r1ConflictGroups=len(conflict_groups), r1ConflictGroupVersions=len(conflict_versions),
                near=dict(near), protected=len(protected), postR1States=dict(Counter(r["Status"] for r in ranks.values())))


def partition_metrics(destinations, bases):
    require(destinations.keys() == bases.keys(), "TRUTH_UNIVERSE")
    sizes = Counter(bases.values())
    resolved = Counter()
    gold = Counter()
    joint = Counter()
    targets = defaultdict(set)
    members = defaultdict(set)
    for i, dest in destinations.items():
        if dest is not None:
            base = bases[i]
            resolved[base] += 1
            gold[dest] += 1
            joint[base, dest] += 1
            targets[base].add(dest)
            members[dest].add(base)
    true_pairs = sum(comb(n, 2) for n in sizes.values())
    predicted = sum(comb(n, 2) for n in gold.values())
    tp = sum(comb(n, 2) for n in joint.values())
    mixed = {dest for dest in gold if len(members[dest]) > 1}
    intact = sum(resolved[b] == sizes[b] and len(targets[b]) == 1
                 and len(members[next(iter(targets[b]))]) == 1 for b in sizes)
    return dict(versions=len(bases), basePeople=len(sizes), resolvedVersions=sum(gold.values()),
                unresolvedVersions=len(bases)-sum(gold.values()), fusedGold=len(mixed),
                affectedBases=len(set().union(*(members[d] for d in mixed))),
                affectedVersions=sum(gold[d] for d in mixed), intactBases=intact,
                splitBases=sum(len(t) > 1 for t in targets.values()),
                entirelyUnresolvedBases=sum(resolved[b] == 0 for b in sizes),
                tp=tp, fp=predicted-tp, fn=true_pairs-tp, truePairs=true_pairs, predictedPairs=predicted,
                recall=tp/true_pairs if true_pairs else None, fdr=(predicted-tp)/predicted if predicted else None)


def evidence_counts(ids, records, official):
    counts = Counter()
    unique = set()
    for i in ids:
        rows = [r for r in records[i] if r["Person"] == official[i]]
        valid = [r for r in rows if r["LogOdds"] is not None]
        require(valid, "WINNING_RECORD")
        winner = min(valid, key=lambda r: (-r["LogOdds"], r["Record"]))
        others = [r for r in rows if r is not winner]
        unique.update(r["Record"] for r in rows)
        counts["secureComparisons"] += len(rows)
        counts["scoreable"] += len(valid)
        counts["exactMaxima"] += sum(r["LogOdds"] == winner["LogOdds"] for r in valid)
        counts["otherRecordsWithMother"] += sum(r["RecordMotherPresent"] for r in others)
        counts["otherNonExactPresentMothers"] += sum(r["SourceMotherPresent"] and r["RecordMotherPresent"]
                                                     and not r["MotherExactFull"] for r in others)
        counts["otherDifferentBirthDates"] += sum(r["Birth"] == "different" for r in others)
    return dict(versions=len(ids), distinctSecureRecords=len(unique), **counts)


def evaluate(replay_result, truth, before, identity):
    # Truth enters only here, after both maps and all veto reasons have been frozen.
    bases = {i: t["base"] for i, t in truth.items()}
    official, variant = (replay_result[k] for k in ("official", "variant"))
    baseline, alternate = (partition_metrics(x, bases) for x in (official, variant))
    require(all(baseline[k] == identity["pairMetrics"][k] for k in
                ("tp", "fp", "fn", "truePairs", "predictedPairs", "recall", "fdr"))
            and baseline["fusedGold"] == identity["falseFusedGoldIdentities"]
            and baseline["splitBases"] == identity["splitBaseIdentities"]
            and baseline["resolvedVersions"] == identity["resolvedVersions"]
            and baseline["unresolvedVersions"] == identity["unresolvedVersions"], "BASELINE")
    anchors = defaultdict(set)
    for pre in before:
        if pre["Status"] == "RESOLVIDO":
            anchors[pre["Current"]].add(bases[pre["Id"]])
    counts = {arm:Counter(correct=0, wrong=0, unknown=0) for arm in ("baseline", "variant", "vetoed")}
    wrong_by_dest = defaultdict(list)
    for i, dest in replay_result["new"].items():
        anchor = anchors[dest]
        kind = "unknown" if len(anchor) != 1 else ("correct" if bases[i] in anchor else "wrong")
        counts["baseline"][kind] += 1
        counts["variant" if variant[i] is not None else "vetoed"][kind] += 1
        if kind == "wrong":
            wrong_by_dest[dest].append(i)
    require(sum(counts["vetoed"].values()) == len(replay_result["reasons"])
            and all(counts["baseline"][k] == counts["vetoed"][k] + counts["variant"][k] for k in counts["baseline"]), "ASSOCIATION_BALANCE")
    # Distinguish accepted score decisions from links copied within the same origin.
    # The latter stay fixed even if the counterfactual vetoes the leader.
    unchanged = Counter(correct=0, wrong=0, unknown=0)
    founder_counts = Counter(correct=0, wrong=0, unknown=0)
    for i, dest in replay_result["propagated"].items():
        require(variant[i] == official[i] == dest, "PROPAGATED_SUBSET")
        anchor = anchors[dest]
        kind = "unknown" if len(anchor) != 1 else ("correct" if bases[i] in anchor else "wrong")
        unchanged[kind] += 1
        if i in replay_result["founders"]:
            founder_counts["correct" if bases[i] == bases[replay_result["founders"][i]] else "wrong"] += 1
        if kind == "wrong":
            wrong_by_dest[dest].append(i)
    # Only count evidence for each anonymous class of errors. Never emit a record key.
    cases = []
    for dest, ids in wrong_by_dest.items():
        sizes = Counter(bases[i] for i, d in official.items() if d == dest)
        fp = comb(sum(sizes.values()), 2) - sum(comb(n, 2) for n in sizes.values())
        supported = [i for i in ids if replay_result["ranks"][i]["Resolved"] == dest]
        cases.append(dict(falsePairs=fp, vetoed=sum(i in replay_result["reasons"] for i in ids),
                          direct=sum(i in replay_result["new"] for i in ids),
                          propagated=sum(i in replay_result["propagated"] for i in ids),
                          withoutMatchingRawResolution=len(ids)-len(supported),
                          **evidence_counts(supported, replay_result["records"], official)))
    return dict(schema="V9_DEV_POST_R1_ABSTENTION_V1", experimentalReason=VETO, baseline=baseline,
                variant=alternate, newAssociations=counts, unchangedPropagatedAssociations=unchanged,
                propagatedFounderTruth=founder_counts, newAbstentions=len(replay_result["reasons"]),
                nearCases=replay_result["near"], postR1States=replay_result["postR1States"],
                unchangedR1ConflictGroups=replay_result["r1ConflictGroups"],
                unchangedR1ConflictGroupVersions=replay_result["r1ConflictGroupVersions"],
                protectedOrPreexisting=replay_result["protected"], subsetSafetyCheckedVersions=len(official),
                wrongAssociationEvidence=sorted(cases, key=lambda c: sorted(c.items())),
                vetoedEvidence=evidence_counts(replay_result["reasons"], replay_result["records"], official))
