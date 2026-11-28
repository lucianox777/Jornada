"""Executable synthetic specification. No product entry point or database access."""
from dataclasses import dataclass
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
import unittest

spec = spec_from_file_location("abstention", Path(__file__).resolve().parents[2] /
                               "scripts/v9-phase4-abstention.py")
abstention = module_from_spec(spec)
spec.loader.exec_module(abstention)


@dataclass(frozen=True)
class Publication:
    origin: str
    destination: str | None
    kind: str


def apply_origin_veto(publications, vetoed, roots):
    """roots lists the effective official grounds for each NEW propagation before publication.

    Each ground must be attested for this particular propagation. A shared UUID,
    raw score or common origin alone does not establish that relationship.
    """
    if any(i not in publications or publications[i].kind != "direct"
           or publications[i].destination is None for i in vetoed):
        raise ValueError("veto requires a new accepted direct association")
    followers = {i for i, row in publications.items() if row.kind == "propagated"}
    if roots.keys() != followers:
        raise ValueError("incomplete propagation provenance")
    result = {i: row.destination for i, row in publications.items()}
    for i in vetoed:
        result[i] = None
    for i, grounds in roots.items():
        follower = publications[i]
        if not grounds or follower.destination is None:
            raise ValueError("missing official ground or destination")
        for root in grounds:
            basis = publications.get(root)
            if (basis is None or basis.kind not in {"direct", "prior", "protected", "founder"}
                    or basis.origin != follower.origin or basis.destination != follower.destination):
                raise ValueError("invalid propagation provenance")
        if grounds <= vetoed:
            result[i] = None
    return result


class OriginVetoTests(unittest.TestCase):
    def setUp(self):
        self.rows = {
            "a1": Publication("origin-a", "gold", "prior"),
            "a2": Publication("origin-a", "gold", "protected"),
            "leader": Publication("origin-b", "gold", "direct"),
            "follower": Publication("origin-b", "gold", "propagated"),
        }
        self.roots = {"follower": {"leader"}}
        self.official = {i: row.destination for i, row in self.rows.items()}

    def test_wrong_leader_removes_residual_fusion(self):
        truth = {"a1": "a", "a2": "a", "leader": "b", "follower": "b"}
        direct_only = dict(self.official, leader=None)
        by_origin = apply_origin_veto(self.rows, {"leader"}, self.roots)
        for arm, expected in [(self.official, (2, 4, 1)), (direct_only, (1, 2, 1)),
                              (by_origin, (1, 0, 0))]:
            metrics = abstention.partition_metrics(arm, truth)
            self.assertEqual((metrics["tp"], metrics["fp"], metrics["fusedGold"]), expected)
        self.assertEqual(by_origin, dict(self.official, leader=None, follower=None))

    def test_correct_leader_cost_and_fixed_recall_denominator(self):
        truth = dict.fromkeys(self.rows, "a")
        direct_only = dict(self.official, leader=None)
        by_origin = apply_origin_veto(self.rows, {"leader"}, self.roots)
        for arm, expected in [(self.official, (6, 0)), (direct_only, (3, 3)), (by_origin, (1, 5))]:
            metrics = abstention.partition_metrics(arm, truth)
            self.assertEqual((metrics["tp"], metrics["fn"]), expected)
            self.assertEqual((metrics["truePairs"], metrics["fp"]), (6, 0))

    def test_only_exclusively_dependent_new_propagations_are_suppressed(self):
        self.rows["previous"] = Publication("origin-b", "gold", "prior")
        self.rows["second-follower"] = Publication("origin-b", "gold", "propagated")
        self.roots["second-follower"] = {"previous", "leader"}
        self.rows["other-direct"] = Publication("origin-c", "gold", "direct")
        self.rows["other-follower"] = Publication("origin-c", "gold", "propagated")
        self.roots["other-follower"] = {"other-direct"}
        self.rows["conflict"] = Publication("origin-d", None, "conflict")
        self.rows["unresolved"] = Publication("origin-e", None, "unresolved")
        expected = {i: row.destination for i, row in self.rows.items()}
        self.assertEqual(apply_origin_veto(self.rows, set(), self.roots), expected)
        expected.update(leader=None, follower=None)
        self.assertEqual(apply_origin_veto(self.rows, {"leader"}, self.roots), expected)

    def test_cost_for_zero_one_or_several_exclusive_followers(self):
        for follower_count in (0, 1, 3):
            with self.subTest(followers=follower_count):
                rows = {i: self.rows[i] for i in ("a1", "a2", "leader")}
                roots = {}
                for i in range(follower_count):
                    key = f"copy-{i}"
                    rows[key] = Publication("origin-b", "gold", "propagated")
                    roots[key] = {"leader"}
                result = apply_origin_veto(rows, {"leader"}, roots)
                k = 1 + follower_count
                truth = dict.fromkeys(rows, "a")
                official = {i: row.destination for i, row in rows.items()}
                baseline = abstention.partition_metrics(official, truth)
                alternate = abstention.partition_metrics(result, truth)
                self.assertEqual(baseline["resolvedVersions"] - alternate["resolvedVersions"], k)
                self.assertEqual(baseline["tp"] - alternate["tp"], 2*k + k*(k-1)//2)
                self.assertEqual(alternate["truePairs"], baseline["truePairs"])

    def test_prior_and_protected_links_cannot_be_vetoed(self):
        for i in ("a1", "a2", "follower"):
            with self.subTest(i=i), self.assertRaises(ValueError):
                apply_origin_veto(self.rows, {i}, self.roots)

    def test_incomplete_or_unrelated_provenance_invalidates_measurement(self):
        self.rows["wrong-target"] = Publication("origin-b", "another-gold", "prior")
        self.rows["raw-best"] = Publication("origin-b", "gold", "raw-score")
        for grounds in ({}, {"follower": set()}, {"follower": {"unknown"}},
                        {"follower": {"a1"}}, {"follower": {"wrong-target"}},
                        {"follower": {"raw-best"}}, {"follower": {"follower"}}):
            with self.subTest(grounds=grounds), self.assertRaises(ValueError):
                apply_origin_veto(self.rows, {"leader"}, grounds)
