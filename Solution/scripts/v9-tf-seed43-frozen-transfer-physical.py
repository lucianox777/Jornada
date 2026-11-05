#!/usr/bin/env python3
"""Seed-43 frozen-transfer helpers: target bootstrap and paired post-hoc analysis.

`bootstrap` authenticates the NEW seed-43 corpus (via the committed preflight
module) and emits its TF bootstrap metadata. `analyze` reads the C#
transfer-results.json and computes descriptive and identity-clustered paired
statistics. Neither command reads TEST, names, dates or CSV rows of VALIDATION.
Nothing here certifies FDR, enables DT-15 or authorizes operation.
"""
import argparse
import importlib.util
import json
import math
from pathlib import Path

HERE = Path(__file__).resolve().parent
VERSION = "V9_TF_SEED43_FROZEN_TRANSFER_ANALYSIS_V1"
CONTROL = "T0.95_TF0"
TREATMENT = "TF_ONE_MF_W1_M0_A0.5_T0.95"
# Pre-declared in V9_TF_002_Statistical_Sample_Design_20261104.md: alpha 0.05
# split equally between the FDR family and ONE paired recall contrast.
PAIRED_ALPHA = 0.025
CAP = 4
TARGET_SEEDS = (43, 44, 45, 46, 47)


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, HERE / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bootstrap(corpus_root, registry_path, target_seed=43):
    preflight = _load("v9_seed43_preflight_for_bootstrap", "v9-tf-seed43-frozen-transfer-preflight.py")
    registry = preflight.pin_source.parse_json(registry_path)
    target, counts, _ = preflight.check_corpus(Path(corpus_root), registry, target_seed)
    if target["GeneratorSeed"] != target_seed or sum(counts.values()) != 30000:
        raise ValueError("Bootstrap must describe the new 30k target-seed corpus")
    return target


def _binomial_tail_ge(k, n):
    """P[Bin(n, 1/2) >= k], exact, via log-space summation."""
    if k <= 0:
        return 1.0
    if k > n:
        return 0.0
    logs = [math.lgamma(n + 1) - math.lgamma(i + 1) - math.lgamma(n - i + 1) - n * math.log(2)
            for i in range(k, n + 1)]
    top = max(logs)
    return min(1.0, math.exp(top) * sum(math.exp(x - top) for x in logs))


def arm_summary(arm):
    m = arm["Evaluation"]["Metric"]
    tp, fp = m["TrueAcceptedSourceObservations"], m["FalseAcceptedSourceObservations"]
    eligible = m["EligibleSourceObservationsBeforeBlocking"]
    outcomes = m.get("IdentityOutcomes") or []
    with_eligible = [o for o in outcomes if o["EligibleSources"] > 0]
    fragmented = sum(1 for o in with_eligible if o["TrueAccepted"] < o["EligibleSources"])
    fused = sum(1 for o in outcomes if o["FalseAccepted"] > 0)
    accepted = tp + fp
    return {
        "OptionId": arm["Arm"]["OptionId"], "ConflictMargin": arm["Arm"]["ConflictMargin"],
        "Role": arm["Arm"]["Role"], "Family": arm["Family"],
        "SnapshotSha256": arm["Arm"]["SnapshotSha256"],
        "EligibleSources": eligible, "TP": tp, "FP": fp, "FN": eligible - tp,
        "Precision": tp / accepted if accepted else None,
        "Recall": m["GlobalSourceRecall"], "BlockingRecall": m["BlockingSourceRecall"],
        "FdrPoint": fp / accepted if accepted else None,
        "FdrUpperConditionalClusterCp": m.get("ConditionalClusterCpFdrUpper"),
        "FdrUpperHoeffdingByIdentity": m.get("HoeffdingUpperBySourceBaseIdentity"),
        "ClopperPearsonUpperAnyFpByIdentity": m.get("ClopperPearsonUpperAnyFpByIdentity"),
        "BonferroniComparedConfigurations": m.get("BonferroniComparedConfigurations"),
        "Conflicts": m["Conflicts"], "Unresolved": m["Unresolved"],
        "ReasonsByResolveStatus": m.get("ReasonsByResolveStatus"),
        "BaseIdentities": m["PreBlockingBaseIdentities"],
        "IdentitiesWithEligibleSources": len(with_eligible),
        "FragmentedIdentities": fragmented,
        "FragmentationRate": fragmented / len(with_eligible) if with_eligible else None,
        "FusedIdentities": fused,
        "FusionRate": fused / len(outcomes) if outcomes else None,
        "P95Milliseconds": arm["Evaluation"].get("P95Milliseconds"),
        "LatencySamples": len(arm["Evaluation"].get("SamplesMilliseconds") or []),
        "FalseDegreeP99": arm["Evaluation"].get("FalseDegreeP99"),
        "Refitted": arm["Evaluation"].get("Refitted", False),
    }


def paired(control_arm, treatment_arm):
    """Identity-clustered paired contrast X_i = TP_treat,i - TP_ctrl,i, X_i in [-cap, cap]."""
    def by_identity(arm):
        rows = arm["Evaluation"]["Metric"].get("IdentityOutcomes")
        if not rows:
            raise ValueError("Paired analysis requires per-identity outcomes")
        table = {}
        for row in rows:
            if row["BaseIdentity"] in table:
                raise ValueError("Duplicate base identity in outcomes")
            table[row["BaseIdentity"]] = row
        return table
    c, t = by_identity(control_arm), by_identity(treatment_arm)
    if set(c) != set(t) or any(c[k]["EligibleSources"] != t[k]["EligibleSources"] for k in c):
        raise ValueError("Arms were not measured on the same identities and eligible sources")
    diffs = [t[k]["TrueAccepted"] - c[k]["TrueAccepted"] for k in sorted(c)]
    fp_diffs = [t[k]["FalseAccepted"] - c[k]["FalseAccepted"] for k in sorted(c)]
    if any(abs(x) > CAP for x in diffs + fp_diffs):
        raise ValueError("Per-identity difference exceeds the declared observation cap")
    n = len(diffs)
    eligible = sum(c[k]["EligibleSources"] for k in c)
    mean = sum(diffs) / n
    var = sum((x - mean) ** 2 for x in diffs) / (n - 1) if n > 1 else 0.0
    se_sum = math.sqrt(n * var)
    positive, negative = sum(1 for x in diffs if x > 0), sum(1 for x in diffs if x < 0)
    ties = n - positive - negative
    nonzero = positive + negative
    # Pre-declared one-sided Hoeffding test of E[X] <= 0, range width 2*cap.
    hoeffding_t = 2 * CAP * math.sqrt(math.log(1 / PAIRED_ALPHA) / (2 * n))
    return {
        "Identities": n, "EligibleSources": eligible,
        "DeltaTP": sum(diffs), "DeltaFP": sum(fp_diffs),
        "DeltaRecall": sum(diffs) / eligible,
        "IdentitiesTreatmentBetter": positive, "IdentitiesControlBetter": negative,
        "IdentitiesTied": ties,
        "MeanDifferencePerIdentity": mean,
        "ClusterRobustSeDeltaTP": se_sum,
        "ClusterRobust95CiDeltaRecall": [(sum(diffs) - 1.959964 * se_sum) / eligible,
                                         (sum(diffs) + 1.959964 * se_sum) / eligible],
        "SignTestOneSidedP": _binomial_tail_ge(positive, nonzero) if nonzero else None,
        "SignTestTwoSidedP": min(1.0, 2 * _binomial_tail_ge(max(positive, negative), nonzero)) if nonzero else None,
        "PredeclaredHoeffdingAlpha": PAIRED_ALPHA,
        "PredeclaredHoeffdingLowerBoundMeanPerIdentity": mean - hoeffding_t,
        "PredeclaredHoeffdingRejectsNoGain": mean - hoeffding_t > 0,
        "Assumption": "IDENTITIES_INDEPENDENT_NOT_VALIDATED_CANDIDATE_COMPETITION_MAY_INDUCE_DEPENDENCE",
    }


def analyze(results):
    if (results.get("Version") != "V9_TF_SEED43_FROZEN_TRANSFER_RESULT_V1" or results.get("TargetSeed") not in TARGET_SEEDS
            or results.get("Refitted") is not False or results.get("TestUsed") is not False
            or results.get("Certified") is not False or results.get("Activated") is not False):
        raise ValueError("Unexpected transfer result contract")
    arms = results["Arms"]
    if len(arms) != 6:
        raise ValueError("Expected six frozen arms")
    summaries = [arm_summary(a) for a in arms]
    contrasts = []
    for margin in sorted({a["Arm"]["ConflictMargin"] for a in arms}):
        pick = {a["Arm"]["OptionId"]: a for a in arms if a["Arm"]["ConflictMargin"] == margin}
        contrast = paired(pick[CONTROL], pick[TREATMENT])
        contrast["ConflictMargin"] = margin
        contrast["Family"] = pick[TREATMENT]["Family"]
        contrasts.append(contrast)
    primary = next(c for c in contrasts if c["Family"] == "PRIMARY_PREDECLARED")
    descriptive = primary["DeltaTP"] > 0 and primary["DeltaFP"] <= 0
    return {
        "Version": VERSION, "ResultVersion": results["Version"],
        "TargetSeed": results["TargetSeed"],
        "ExecutorPlanSha256": results["ExecutorPlanSha256"],
        "TargetObservationsSha256": results["TargetObservationsSha256"],
        "TargetManifestSha256": results["TargetManifestSha256"],
        "ValidationObservations": results["ValidationObservations"],
        "ValidationBaseIdentities": results["ValidationBaseIdentities"],
        "Arms": summaries, "PairedContrasts": contrasts,
        "DescriptiveStabilityCriterion": "PRIMARY_DELTA_TP_POSITIVE_AND_NO_OBSERVED_FP_INCREASE",
        "DescriptiveStabilityMet": descriptive,
        "InferentialGainEstablished": primary["PredeclaredHoeffdingRejectsNoGain"],
        "Certification": "NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT",
        "OperationalAuthorization": False, "TestUsed": False, "Refitted": False,
    }


def markdown(report):
    def pct(x):
        return "—" if x is None else f"{100 * x:.4f}%"
    lines = [f"# Transferência congelada seed 42 → seed {report['TargetSeed']} (resultado físico)", "",
             f"Status: **{report['Certification']}**. Sem refit, sem TEST, sem ativação.", "",
             f"VALIDATION seed {report['TargetSeed']}: {report['ValidationObservations']} observações, "
             f"{report['ValidationBaseIdentities']} identidades-base. "
             f"Corpus `{report['TargetObservationsSha256']}`.", "",
             "| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for a in report["Arms"]:
        p95 = "—" if a["P95Milliseconds"] is None else f"{a['P95Milliseconds']:.0f}"
        lines.append(f"| {a['OptionId']} | {a['ConflictMargin']} | {a['TP']} | {a['FP']} | {a['FN']} | "
                     f"{pct(a['Precision'])} | {pct(a['Recall'])} | {pct(a['FdrPoint'])} | "
                     f"{pct(a['FdrUpperConditionalClusterCp'])} | {a['Conflicts']} | {a['Unresolved']} | "
                     f"{pct(a['FragmentationRate'])} | {pct(a['FusionRate'])} | {p95} |")
    lines += ["", "| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |",
              "|---:|---|---:|---:|---:|---|---|---:|---|"]
    for c in report["PairedContrasts"]:
        lo, hi = c["ClusterRobust95CiDeltaRecall"]
        p = "—" if c["SignTestOneSidedP"] is None else f"{c['SignTestOneSidedP']:.4g}"
        lines.append(f"| {c['ConflictMargin']} | {c['Family']} | {c['DeltaTP']} | {c['DeltaFP']} | "
                     f"{pct(c['DeltaRecall'])} | [{pct(lo)}, {pct(hi)}] | "
                     f"{c['IdentitiesTreatmentBetter']}/{c['IdentitiesControlBetter']}/{c['IdentitiesTied']} | {p} | "
                     f"{'rejeita' if c['PredeclaredHoeffdingRejectsNoGain'] else 'não rejeita'} H0 |")
    lines += ["", f"Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **{'atendido' if report['DescriptiveStabilityMet'] else 'não atendido'}**.",
              f"Ganho inferencial pelo teste pré-declarado: **{'estabelecido' if report['InferentialGainEstablished'] else 'não estabelecido'}**.",
              "", "Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("bootstrap")
    b.add_argument("--corpus-root", type=Path, required=True)
    b.add_argument("--seed42-registry", type=Path, required=True)
    b.add_argument("--output", type=Path, required=True)
    b.add_argument("--target-seed", type=int, default=43, choices=TARGET_SEEDS)
    a = sub.add_parser("analyze")
    a.add_argument("--results", type=Path, required=True)
    a.add_argument("--output", type=Path, required=True)
    a.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "bootstrap":
        value = bootstrap(args.corpus_root, args.seed42_registry, args.target_seed)
        with args.output.open("x", encoding="utf-8") as out:
            json.dump(value, out, indent=2, allow_nan=False)
            out.write("\n")
        print("PASS: seed-43 bootstrap metadata only; no TEST or rows exported.")
        return
    report = analyze(json.loads(args.results.read_bytes()))
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(report, out, indent=2, allow_nan=False)
        out.write("\n")
    with args.markdown.open("x", encoding="utf-8") as out:
        out.write(markdown(report))
    print("PASS: paired analysis written; NOT a certification.")


if __name__ == "__main__":
    main()
