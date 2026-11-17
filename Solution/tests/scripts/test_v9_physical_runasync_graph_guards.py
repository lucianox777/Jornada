"""Fail-closed source guards for the physical SQL graph in real V9 RunAsync.

These guards are NOT a physical integration test or TEST admission evidence.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src/Jornada.Linkage.Runner"
SCORER = ROOT / "V9PinnedSyntheticGraphCohortRunScorer.cs"
RUNNER = ROOT / "ProbabilisticLinkageBatchRunner.cs"


class V9PhysicalRunAsyncGraphGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scorer = SCORER.read_text(encoding="utf-8")
        cls.runner = RUNNER.read_text(encoding="utf-8")

    def test_sql_graph_mode_does_not_accept_supplied_edges(self):
        s = self.scorer
        self.assertIn("readPhysicalSqlGraph = true;", s)
        self.assertIn("Array.Empty<(long A, long B)>()", s)
        self.assertIn("V9FrozenCohortSqlCandidateGraph.ReadAsync(", s)
        self.assertIn("V9OperationalBlockingChaveCohortParity.VerifyAsync(", s)
        self.assertLess(s.index("V9FrozenCohortSqlCandidateGraph.ReadAsync("),
                        s.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("))
        self.assertLess(s.index("V9OperationalBlockingChaveCohortParity.VerifyAsync("),
                        s.index("var global = await operationalLinkage.ResolvePinnedV9FrozenTestCohortAsync("))

    def test_exact_graph_parity_required_before_global_scoring(self):
        s = self.scorer
        self.assertIn("operational.UndirectedSqlEdges != physical.Edges.Count", s)
        self.assertIn("operational.CanonicalEdgesSha256", s)
        self.assertIn("physical.CanonicalEdgesSha256", s)
        self.assertIn("edges = physical.Edges.ToList();", s)
        self.assertLess(s.index("edges = physical.Edges.ToList();"),
                        s.index("var global = await operationalLinkage.ResolvePinnedV9FrozenTestCohortAsync("))
        self.assertIn("global.SnapshotSha256 != pinnedWinnerSha", s)
        self.assertIn("global.Decisions.Count != cohort.Length", s)

    def test_runasync_requires_full_frozen_universe_before_persistence(self):
        s = self.runner
        self.assertIn("v9Grouped.PrepareAsync(runId, model, frozen, workCt)", s)
        self.assertIn("await PersistBatchAsync(", s)
        self.assertLess(s.index("v9Grouped.PrepareAsync(runId, model, frozen, workCt)"),
                        s.index("await PersistBatchAsync("))
        self.assertIn("request.Publish || request.Mode != LinkageRunType.MODEL_VALIDATION", s)


    def test_physical_sql_graph_rejects_capacity_excess_and_invalid_edges(self):
        graph = (ROOT / "V9FrozenCohortSqlCandidateGraph.cs").read_text(encoding="utf-8")
        for guard in ("if (signatures.Count >= MaximumSignatures)",
                      "if (++incidences > MaximumPairIncidences)",
                      "if (edges.Count >= MaximumPairIncidences)",
                      "if (a < 1 || a >= b || b > frozenRows.Count)",
                      "SELECT COUNT_BIG(*) FROM #v9_run_sig;",
                      "materializedSignatures != signatures.Count",
                      "CREATE UNIQUE INDEX UX_v9_run_sig",
                      "gestor nvarchar(120) COLLATE Latin1_General_100_BIN2",
                      "pass_id nvarchar(120) COLLATE Latin1_General_100_BIN2",
                      "assinatura char(64) COLLATE Latin1_General_100_BIN2",
                      "manager.Length > 120",
                      "manager.EndsWith(' ')",
                      "pass.PassId.Length > 120",
                      "pass.PassId.EndsWith(' ')",
                      "expectedSignatures.SetEquals(observedSignatures)",
                      "CanonicalSignaturesSha256: signaturesDigest",
                      "OrderBy(s => s.Obs)",
                      "SELECT obs_id,gestor,pass_id,assinatura FROM #v9_run_sig;",
                      "SELECT COUNT_BIG(*) FROM (",
                      "a.obs_id AS source_obs_id,b.obs_id AS target_obs_id",
                      "if (edges.Count != expectedEdges)",
                      "var expectedPairs = new HashSet<(int A, int B)>()",
                      "expectedPairs.SetEquals(edges)",
                      "expectedPairs.Count > MaximumPairIncidences",
                      "edges[i - 1].B >= edges[i].B"):
            self.assertIn(guard, graph)

    def test_direct_runasync_v9_fails_before_sql_and_pipeline_lock(self):
        s = self.runner
        gate = s.index("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED")
        start = s.index("public async Task<ProbabilisticLinkageRunSummary> RunAsync(")
        self.assertLess(start, gate)
        self.assertLess(gate, s.index("AcquireExclusiveJobAsync(", start))
        self.assertLess(gate, s.index("await linkage.GetModelByVersionAsync(", start))
        self.assertIn('configuration["Linkage:V9SelectionFile"]', s[start:gate])
        self.assertIn('configuration.GetValue<int?>("Linkage:SelectedModelVersion") == 9', s[start:gate])
        self.assertIn("request.Mode != LinkageRunType.REPLAY", s[start:gate])

    def test_verified_runtime_boundary_is_fail_closed_and_file_only(self):
        boundary = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        program = (ROOT / "Program.cs").read_text(encoding="utf-8")
        self.assertIn("V9OperationalPinnedCohortRuntime Runtime", boundary)
        self.assertIn("V9OperationalRunBoundary(V9OperationalPinnedCohortRuntime runtime)", boundary)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", boundary)
        self.assertNotIn("GetModelByVersionAsync", boundary)
        self.assertNotIn("LoadPinnedAsync(", boundary)
        self.assertIn("new V9OperationalRunBoundary(frozenV9)", program)
        self.assertIn("v9Boundary.EnsureProductiveCertified();", program)
        self.assertLess(
            program.index("V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile)"),
            program.index("new V9OperationalRunBoundary(frozenV9)"))
        self.assertLess(
            program.index("v9Boundary.EnsureProductiveCertified();"),
            program.index("pinnedLinkage.GetModelByVersionAsync(configuredVersion.Value)"))

    def test_v9_file_selection_is_separate_from_legacy_sql_parameters(self):
        program = (ROOT / "Program.cs").read_text(encoding="utf-8")
        loader = (ROOT / "V9ConfiguredArtifactLoader.cs").read_text(encoding="utf-8")
        self.assertIn("V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile)", program)
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", program)
        self.assertIn("RUNNER_V9_SELECTION_MISSING", program)
        self.assertLess(
            program.index("V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile)"),
            program.index("pinnedLinkage.GetModelByVersionAsync(configuredVersion.Value"))
        self.assertIn("options.Mode != LinkageRunType.REPLAY", program)
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(artifact, ct)", loader)
        self.assertIn("FromVerifiedFileSnapshot(loaded.Snapshot)", loader)
        self.assertNotIn("SqlConnection", loader)

    def test_operational_graph_checks_full_manifest_and_bidirectional_edges(self):
        parity = (ROOT / "V9OperationalBlockingChaveCohortParity.cs").read_text(encoding="utf-8")
        for guard in ("expectedManifest.SetEquals(seen)",
                      "goldCount != frozenRows.Count",
                      "directions.Values.Any(value => value != 3)",
                      "expected.SetEquals(directions.Keys)",
                      "BuildCandidateUuidQuery(cmd,"):
            self.assertIn(guard, parity)


if __name__ == "__main__":
    unittest.main()
