#!/usr/bin/env python3
"""Private DEV trajectory inputs and independent aggregate evaluation of the six arms."""
import argparse
from collections import Counter, defaultdict
import hashlib
import importlib.util
import json
from math import comb
import os
from pathlib import Path
import re
import subprocess
import uuid


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


S = module("scale", "v9-phase4-scale.py")
A = module("abstention", "v9-phase4-abstention.py")
C = module("curve", "v9-publication-scale-curve.py")
I = S.I
require = I.require
SEED = 85229684
BEFORE = "a651d6fa2cf434bcce08187528fb0211dd4f0fac"
COUNTERS = ("candidatePeople", "silverEdges", "silverDirectionsCompared", "goldRecordScores")
SCHEDULES = {"repeat_full": (2, 2), "per_wave": (1, 2)}
MODES = ("None", "Direct", "Origin")


def admit(root):
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_bytes())
    owner = "lucianox777"
    pr = event["pull_request"]
    sha = os.environ["JORNADA_E2E_EXACT_SHA"]
    digest = hashlib.sha256(b"Jornada/DEV10k/seis-bracos/v1/7235636439").digest()
    require(SEED == 10000000 + int.from_bytes(digest[:4], "big") % 100000000
            and os.environ["JORNADA_V9_SIX_ARM"] == "7235636439"
            and os.environ["SCALE_SEED"] == str(SEED) and os.environ["SCALE_ERROR"] == "clean"
            and S.profile() == ("optimized10k", (10000, 355, "clean"))
            and os.environ["GITHUB_RUN_ATTEMPT"] == "1"
            and os.environ["GITHUB_ACTOR"] == os.environ["GITHUB_TRIGGERING_ACTOR"] == owner
            and event["action"] == "synchronize" and event["before"] == BEFORE and event["number"] == 1940
            and pr["user"]["login"] == owner and pr["head"]["repo"]["full_name"] == "lucianox777/Jornada"
            and re.fullmatch("[a-f0-9]{40}", sha) and event["after"] == pr["head"]["sha"] == sha
            and subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip() == sha
            and root == Path(os.environ["RUNNER_TEMP"]) / ("jornada-v9-e2e-" + str(uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"])))
            and root.is_dir() and not root.is_symlink(), "SIX_ARM_ADMISSION")
    for path, tree in (("src/Jornada.Linkage.SyntheticCorpus", "56cc0c102e1100dc43352211dfd5e0ba1b20ef3f"),
                       ("data/reference/ibge-nomes-2022", "99e29998a377f94872bd5a5a1b92e284dc6fee70"),
                       ("data/reference/synthetic-birth-sp", "23ae3b7b56076795e4d69228a4dcffbaff1f7958")):
        require(subprocess.check_output(["git", "rev-parse", "HEAD:Solution/" + path], text=True).strip() == tree,
                "SIX_ARM_GENERATOR_PIN")


def seals(root):
    fixture = root / "fixture"
    pre = S.read(root / "wave-preflight-receipt.json")
    paths = [root / "wave-preflight-receipt.json", fixture / "corpus" / "generation-manifest.json",
             fixture / "ingestion" / "waves-manifest.json"]
    inputs = S.read(fixture / "corpus" / "generation-manifest.json")
    paths += [fixture / "corpus" / name for name in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json")]
    for group in pre["packagesByWave"]:
        wave = fixture / "ingestion" / ("wave-%02d" % group["wave"]) / "delivery"
        paths += [wave / "bridge-manifest.json", wave / "bridge-truth.jsonl"]
        paths += [wave / p["fileName"] for p in group["packages"]]
    require(len(paths) == len(set(paths)) == 18 and inputs["seed"] == pre["seed"] == SEED, "SIX_ARM_SEALS")
    return {p.relative_to(root).as_posix(): hashlib.sha256(I.read(p)).hexdigest() for p in paths}


def ingest(root, staged):
    expected = S.read(root / "input-seals-private.json")
    require(seals(root) == expected, "SIX_ARM_INPUT_MUTATION")
    def after_wave(wave, count):
        require(wave in (1, 2) and count > 0, "SIX_ARM_WAVE")
        label = f"staged-{wave}"
        with (root / (label + ".log")).open("xb") as log:
            execution = subprocess.run(["dotnet", "run", "--no-build", "-c", "Release", "--no-launch-profile",
                                        "--project", "src/Jornada.Linkage.Runner", "--", "--v9-dev-full-10k", label],
                                       stdout=log, stderr=subprocess.STDOUT)
        require(execution.returncode == 0, "SIX_ARM_PHYSICAL_" + str(wave))
    S.H.ingest(root / "fixture", uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"]), population=10000,
               after_wave=after_wave if staged else None, receipt_dir=root / "staged-ingestion" if staged else None)


def pair_delta(before, after, bases):
    """Count gains and losses, including offsetting changes, without materializing pair lists."""
    require(set(before) <= set(after) == set(bases), "SIX_ARM_PAIR_UNIVERSE")
    overlap, true_overlap = Counter(), Counter()
    for i, left in before.items():
        right = after[i]
        if left is not None and right is not None:
            overlap[left, right] += 1
            true_overlap[left, right, bases[i]] += 1
    common = sum(comb(size, 2) for size in overlap.values())
    tp = sum(comb(size, 2) for size in true_overlap.values())
    left = A.partition_metrics(before, {i: bases[i] for i in before})
    right = A.partition_metrics(after, bases)
    return {"tpGained": right["tp"] - tp, "tpLost": left["tp"] - tp,
            "fpIntroduced": right["fp"] - (common - tp), "fpAvoided": left["fp"] - (common - tp)}


def partition(labels):
    members = defaultdict(list)
    for i, dest in labels.items():
        if dest is not None:
            members[dest].append(i)
    return {i: min(group) for group in members.values() for i in group}


def physical(root, census, truth, available_only=False):
    by_key = {I.version_key(r["manager"], r["package"], r["opaque"]): r["observationId"] for r in census}
    waves = []
    for n in (1, 2):
        d = root / "fixture" / "ingestion" / f"wave-{n:02d}" / "delivery"
        waves.append((S.read(d / "bridge-manifest.json"), I.read(d / "bridge-truth.jsonl")))
    results = {}
    for schedule, labels in (("repeat_full", ("", "repeat-2")), ("per_wave", ("staged-1", "staged-2"))):
        previous = None
        for number, label in enumerate(labels, 1):
            directory = root / label
            wave = SCHEDULES[schedule][number - 1]
            if available_only and not (directory / "runner-receipt.json").exists():
                continue
            rows = S.read(directory / "sql-census.json") if label else census
            receipt = S.read(directory / "runner-receipt.json")
            require(receipt.get("previousRunId") == previous and receipt["runId"] != previous, "SIX_ARM_PHYSICAL_CHAIN")
            previous = receipt["runId"]
            ids = {i for i, t in truth.items() if t["wave"] <= wave}
            identity = I.compare(waves[:wave], rows, receipt, required_identities=len({truth[i]["base"] for i in ids}))
            links = {by_key[I.version_key(r["manager"], r["package"], r["opaque"])]:
                     r["currentUuid"] if r["currentStatus"] == "RESOLVIDO" else None for r in rows}
            require(set(links) == ids, "SIX_ARM_PHYSICAL_KEYS")
            counts = receipt["counters"] if label else S.read(root / "membership-receipt.json")
            counts = {k: counts[k] for k in COUNTERS}
            extra = S.read(directory / "scale-full-extra-resource.json")
            counts["instanceBatchRequests"] = C.full10k_instance_batch_requests(extra)
            resource = S.read(directory / "matrix-full-physical-resource.json")
            local_to_common = {r["observationId"]: by_key[I.version_key(r["manager"], r["package"], r["opaque"])] for r in rows}
            mask = {local_to_common[i] for i in S.read(directory/"direct-mask-private.json")} if label else None
            results[schedule, number] = (links, mask, dict(runId=receipt["runId"], previousRunId=receipt.get("previousRunId"),
                counters=counts, fullMs=resource["fullElapsedMs"], runnerCpuMs=extra["runnerCpuMs"],
                identityCensusStatus=identity["status"], pairMetrics=identity["pairMetrics"]))
            path = Path(os.environ["RUNNER_TEMP"]) / f"v9-six-arm-control-{schedule}-{number}.json"
            summary = dict(results[schedule, number][2], schedule=schedule, round=number,
                           commit=os.environ["JORNADA_E2E_EXACT_SHA"], seed=SEED,
                           winnerSha256=S.P.WINNER, observations=len(ids), origins=identity["persistentOrigins"],
                           baseIdentities=identity["baseIdentities"], fusedGold=identity["falseFusedGoldIdentities"])
            if path.exists():
                require(S.read(path) == summary, "SIX_ARM_CONTROL_RECEIPT_DRIFT")
            else:
                S.write(path, summary)
    return results


def validate_state(mode, row, state, output):
    veto = set(row["EligibleLeaders"]) if mode != "None" else set()
    suppressed = veto | set(row["SuppressedFollowers"])
    require(all(output[i] is None for i in suppressed) and all(output[i] == state[i] for i in state.keys()-suppressed)
            and all(state[i] is not None for i in veto if mode == "Direct")
            and all(state[i] is None for i in suppressed if mode == "Origin"), "SIX_ARM_VETO_STATE")
    if mode == "None":
        require(state == output, "SIX_ARM_CONTROL_STATE")
    return veto, suppressed


def link_cost(row, truth, mode):
    veto = set(row["EligibleLeaders"]) if mode != "None" else set()
    followers = set(row["SuppressedFollowers"])
    costs = {"direct": Counter(), "propagated": Counter()}
    classifications = Counter()
    expected_followers = set()
    for link in row["NewLinks"]:
        leader = link["Leader"]
        group = set(link["Versions"])
        require(leader in group and len(group) == len(link["Versions"]) and
                len({truth[i]["origin"] for i in group}) == 1, "SIX_ARM_PUBLICATION_ORIGIN")
        records = link["SecureRecords"]
        anchor = {truth[i]["base"] for i in records}
        if link["Method"] == "FS_ASSOCIATION":
            require(bool(records), "SIX_ARM_SOURCE_GROUND")
            label = ("unknown" if len(anchor) != 1 else "correct" if truth[leader]["base"] in anchor else "wrong")
            classifications["direct_" + label] += 1
            classifications["propagated_" + label] += len(group) - 1
            if leader in veto:
                costs["direct"][label] += 1
                expected_followers.update(group - {leader})
                if mode == "Origin":
                    costs["propagated"][label] += len(group) - 1
    require(followers == (expected_followers if mode == "Origin" else set())
            and sum(costs["direct"].values()) == len(veto), "SIX_ARM_DEPENDENCY")
    return dict(costs), dict(classifications)


def evaluate(root):
    require(seals(root) == S.read(root / "input-seals-private.json"), "SIX_ARM_INPUT_MUTATION")
    census, _, truth = S.truth_frame(root, 10000)
    controls = physical(root, census, truth)
    member = S.read(root / "membership-receipt.json")
    scores = S.read_exact(root / "abstention-pairs-private.json")
    legacy = A.replay(census, S.read(root / "matrix-frame-private.json")["Rows"], S.read_exact(root / "scale-rank-private.json"),
                      S.read(root / "fn-private.json"), S.read_exact(root / "scale-gold-scores-private.json"),
                      scores["pairs"], member, I.PROTECTED)
    states, verdicts, reference = {}, [], {}
    with (root / "comparison-private.jsonl").open("rb") as stream:
        for line in stream:
            row = I.load(line)
            schedule, s4b, mode, number = (row[k] for k in ("schedule", "s4b", "mode", "round"))
            key = schedule, s4b, mode, number
            prev = states.get((schedule, s4b, mode, 1)) if number == 2 else None
            require(key not in states and schedule in SCHEDULES and s4b in ("i", "ii") and mode in MODES
                    and number in (1, 2) and row["wave"] == SCHEDULES[schedule][number-1]
                    and row["id"] == "/".join(map(str, key))
                    and row["previous"] == (prev["id"] if prev else None), "SIX_ARM_TRAJECTORY")
            bases = {i: t["base"] for i,t in truth.items() if t["wave"] <= row["wave"]}
            output = {r["Id"]: r["Person"] for r in row["rows"]}
            state = {int(i): dest for i,dest in row["State"].items()}
            require(state.keys() == output.keys() == bases.keys() and len(row["rows"]) == len(bases), "SIX_ARM_POPULATION")
            veto, suppressed = validate_state(mode, row, state, output)
            if prev:
                require(all(state[i] == dest for i,dest in prev["state"].items() if dest is not None), "SIX_ARM_PRIOR_LINK")
            if mode == "None":
                reference[schedule, s4b, number] = state
            official = reference[schedule, s4b, number]
            if mode == "Direct":
                require(state == official, "SIX_ARM_DIRECT_STATE")
            if (schedule, s4b, number) == ("repeat_full", "i", 1):
                require(set(row["EligibleLeaders"]) == set(legacy["reasons"]) and
                        (mode != "Direct" or output == legacy["variant"]), "SIX_ARM_FROZEN_DIRECT_VETO")
            cost, classifications = link_cost(row, truth, mode)
            metrics = A.partition_metrics(output, bases)
            ctrl, physical_mask, phys = controls[schedule, number]
            parity = None
            if mode in ("None", "Direct") and s4b == "i":
                # Staged HTTP ingestion receives independent UUIDs. Compare all resolved partitions and coverage.
                # For the repeat schedule both paths use the very same physical input UUIDs.
                same = state == ctrl if schedule == "repeat_full" else partition(state) == partition(ctrl)
                exact_mask = set(legacy["reasons"]) if physical_mask is None else physical_mask
                parity = same and exact_mask == set(row["EligibleLeaders"]) and all(row["counters"][k] == phys["counters"][k] for k in COUNTERS)
            grounds = {}
            for link in row["NewLinks"]:
                evidence = (link["Leader"], link["Destination"], link["Method"], tuple(sorted(link["SecureRecords"])))
                for i in link["Versions"]:
                    require(i not in grounds, "SIX_ARM_DUPLICATE_GROUND")
                    grounds[i] = evidence
            if prev:
                followup = Counter()
                for i in prev["veto"]:
                    if output[i] is None:
                        reason = "stillAbstained"
                    elif prev["state"][i] is not None:
                        reason = "reappearedViaOfficialPriorReference"
                    else:
                        require(i in grounds, "SIX_ARM_REAPPEARANCE_WITHOUT_GROUND")
                        leader, _, method, _ = grounds[i]
                        if leader in veto:
                            require(mode == "Direct" and leader != i, "SIX_ARM_VETOED_ONLY_GROUND")
                            reason = "propagatedFromVetoedLeaderInDiagnostic"
                        elif method != "FS_ASSOCIATION":
                            reason = "newFounderOrCollectivePublication"
                        elif grounds[i] == prev["grounds"][i]:
                            reason = "newAcceptedDecisionSameRecordedGround"
                        else:
                            reason = "newAcceptedDecisionChangedLeaderTargetOrEvidence"
                    followup[reason] += 1
                require(sum(followup.values()) == len(prev["veto"]), "SIX_ARM_FOLLOWUP_COVERAGE")
            else:
                followup = None
            official_resolved = {bases[i] for i in bases if official[i] is not None}
            arm_resolved = {bases[i] for i in bases if output[i] is not None}
            record = dict(id=row["id"], previous=row["previous"], throughWave=row["wave"], observations=len(bases),
                          origins=len({truth[i]["origin"] for i in bases}), metrics=metrics, proposedLinks=classifications,
                          lostLinksByGround=cost, additionalBasesFullyUnresolved=len(official_resolved-arm_resolved),
                          recoveredBases=len(arm_resolved-official_resolved), comparisonToNoVeto=pair_delta(official, output, bases),
                          previousRoundChange=pair_delta(prev["output"], output, bases) if prev else None,
                          vetoFollowup=followup, suppressedLeaders=len(veto), suppressedFollowers=len(suppressed-veto),
                          physicalStateParity=parity, control=phys if mode == "None" and s4b == "i" else None,
                          projectedCounters=row["counters"], projectedRoundMs=row["resources"]["roundMs"],
                          projectedCpuMs=row["cpuMs"], peakProcessBytes=row["resources"]["processPeakWorkingSetBytes"], sharedGraphMs=row["resources"]["graphSharedMs"])
            verdicts.append(record)
            states[key] = dict(id=row["id"], state=state, output=output, veto=suppressed, grounds={i: grounds[i] for i in suppressed})
    require(len(verdicts) == 24, "SIX_ARM_COVERAGE")
    exact = all(r["physicalStateParity"] is not False for r in verdicts)
    public = dict(schema="V9_DEV_SIX_ARMS_V1", integrity="VERIFIED" if exact else "PHYSICAL_PROJECTION_DIVERGENCE",
                  commit=os.environ["JORNADA_E2E_EXACT_SHA"], winnerSha256=S.P.WINNER, seed=SEED,
                  inputSealSha256=hashlib.sha256(I.read(root/"input-seals-private.json")).hexdigest(),
                  custody=S.read(root/"selection"/"winner-pin-receipt.json"), orderedInputSha256=list(seals(root).values()), sourcePopulation=10000, activated=False,
                  scientificTest=False, scaleApproval=False, reference4k=C.FULL10K_REFERENCES, lossLimitsApproved=False,
                  physicalScope="S4b(i) both schedules, both rounds", directState="official, per-round output mask",
                  alternativePublication="independent in-memory states; no SQL batch estimate", rounds=verdicts)
    S.write(Path(os.environ["RUNNER_TEMP"]) / "v9-six-arm-receipt.json", public)
    require(exact, "SIX_ARM_PHYSICAL_PROJECTION_DIVERGENCE")


def main():
    cli = argparse.ArgumentParser()
    cli.add_argument("action", choices=("admit", "preflight", "ingest", "ingest-staged", "controls", "evaluate"))
    cli.add_argument("--root", required=True, type=Path)
    args = cli.parse_args()
    root = args.root
    admit(root)
    if args.action == "preflight":
        S.preflight_inputs(root/"fixture", "six-arm-independent", 10000, SEED, "clean")
        (root/"wave-preflight-receipt.json").write_bytes(I.read(root/"fixture"/"delivery-preflight-receipt.json"))
        sealed = seals(root)
        S.write(root/"input-seals-private.json", sealed)
        pre = S.read(root/"wave-preflight-receipt.json")
        S.write(Path(os.environ["RUNNER_TEMP"])/"v9-six-arm-seal.json", dict(
            decisionComment=7235636439, seed=SEED, commit=os.environ["JORNADA_E2E_EXACT_SHA"],
            winnerSha256=S.P.WINNER, generatorTree="56cc0c102e1100dc43352211dfd5e0ba1b20ef3f",
            baseIdentities=pre["baseIdentities"], versions=pre["sourceVersions"], origins=pre["persistentOrigins"],
            preflightSha256=sealed["wave-preflight-receipt.json"], corpusFingerprint=pre["sourceFingerprint"],
            truthSha256=sealed["fixture/corpus/gabarito.json"],
            waves=[dict(wave=group["wave"],
                        truthSha256=sealed["fixture/ingestion/wave-%02d/delivery/bridge-truth.jsonl" % group["wave"]],
                        packageSha256={p["manager"]:p["zipSha256"] for p in group["packages"]})
                   for group in pre["packagesByWave"]]))
    elif args.action.startswith("ingest"):
        ingest(root, args.action == "ingest-staged")
    elif args.action == "controls":
        census, _, truth = S.truth_frame(root, 10000)
        physical(root, census, truth, available_only=True)
    elif args.action == "evaluate":
        evaluate(root)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        label = str(error)
        safe = label if re.fullmatch(r"V9_E2E_ORACLE_[A-Z0-9_]+", label) else type(error).__name__
        raise SystemExit("V9_SIX_ARM_FAILED " + safe) from None
