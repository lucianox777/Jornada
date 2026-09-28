using NUnit.Framework;

namespace Jornada.Tests.Unit;

/// <summary>
/// DT-17 is an inventory of actual tests plus explicit gaps, not evidence that
/// every E2E, institutional or statistical acceptance criterion has passed.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class Dt17RegressionMatrixContractTests
{
    private static readonly (string File, string Marker)[] ExistingProofs =
    [
        ("Solution/tests/Jornada.Tests/Unit/CpfRulesTests.cs", "Rejects_invalid_cpf"),
        ("Solution/tests/Jornada.Tests/Unit/ProgressiveIdentityTests.cs", "InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation"),
        ("Solution/tests/Jornada.Tests/Unit/SyntheticIngestionWaveTests.cs", "Waves_reuse_source_code_but_rotate_delivery_ids_and_reveal_cpf"),
        ("Solution/tests/Jornada.Tests/Unit/ProbabilisticLinkageIncrementalEligibilityTests.cs", "Incremental_reconsiders_unresolved_and_conflict_when_candidate_side_changes"),
        ("Solution/tests/Jornada.Tests/Unit/Dt05SemanticTransitionContractTests.cs", "V1_signature_field_order_and_documentation_are_frozen"),
        ("Solution/tests/Jornada.Tests/Unit/ProbabilisticV6TieOrderingTests.cs", "Exact_log_odds_tie_is_conflict_and_uuid_only_stabilizes_display_order"),
        ("Solution/tests/Jornada.Tests/Unit/SemiblindIdentitySearchTests.cs", "Search_exposes_at_most_five_minimized_candidates"),
        ("Solution/tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs", "Route_enforces_access_and_pre_response_audit"),
        ("Solution/tests/Jornada.Tests/Unit/BlockingParallelCandidateDiagnosticTests.cs", "Analyze_ComparesComplementaryAndSharedPairsInExactlyTheSameUniverse"),
        ("Solution/tests/Jornada.Tests/Unit/BlockingParallelSqlAuditMetricsTests.cs", "Summarize_ComparesD_CAndUnionOverEveryLabeledObservation"),
        ("Solution/tests/Jornada.Tests/Unit/AuthorizationMatrixContractTests.cs", "Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes"),
        ("Solution/tests/Jornada.Tests/Unit/LinkageDecisionBoundaryTests.cs", "V6_threshold_is_inclusive_at_exact_best_score_and_rejects_immediately_above"),
        ("Solution/tests/Jornada.Tests/Unit/LinkagePromotionConferenceGateContractTests.cs", "Validate_and_activate_share_the_same_governed_parameters_and_sql_assertion"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/CpfAnchorGovernedCorrectionTests.cs", "Governed_correction_cannot_transfer_permanent_cpf_anchor"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/IdentityReplayInvariantTests.cs", "Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/ProbabilisticLinkageIncrementalEligibilitySqlServerTests.cs", "Incremental_marks_prior_probabilistic_conflict_eligible_after_candidate_universe_changes"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/Dt05SemanticThreeWavesSqlServerTests.cs", "Four_waves_ignore_unchanged_and_score_only_retries_without_losing_raw_results"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/Dt05PublicationGuardsSqlServerTests.cs", "Ledger_requires_transaction_and_executing_run_without_side_effects"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/BlockingParallelSqlAuditQueryTests.cs", "TaggedQuery_ExecutesWithEmptyAndCombinedPassesWithoutExposingCandidateIds"),
        ("Solution/scripts/dt05-cpf-late-wave.ps1", "DT05_CPF_LATE_REAL_RUNNER_E2E")
    ];

    [Test]
    public void Baseline_references_existing_test_methods_and_keeps_open_gaps_explicit()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RELEASE_INFO.txt")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null, "A raiz do repositório deve ser encontrada pelo teste.");

        var matrixPath = Path.Combine(root!.FullName, "Solution", "docs", "DT17_Matriz_Regressao.md");
        Assert.That(File.Exists(matrixPath), Is.True);
        var matrix = File.ReadAllText(matrixPath);
        foreach (var (relativePath, marker) in ExistingProofs)
        {
            var proofPath = Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(proofPath), Is.True, "Prova inexistente: " + relativePath);
            Assert.That(File.ReadAllText(proofPath), Does.Contain(marker), "Método/marcador ausente: " + relativePath);
            Assert.That(matrix, Does.Contain("../" + relativePath["Solution/".Length..]), "Link documental ausente: " + relativePath);
            Assert.That(matrix, Does.Contain(marker), "Método/marcador não inventariado: " + marker);
        }

        string[] requiredGaps =
        [
            "LACUNA: MARIA SOUZA / MARIA SOUZA LIMA / MARIA LIMA",
            "LACUNA: DT-05 replay histórico NAS",
            "LACUNA: DT-10 concorrência adversarial e rollback parcial",
            "LACUNA: #539 / #378 compartilhamento institucional",
            "LACUNA: #506 / #31 validação externa e representatividade",
            "LACUNA: Ensaio integrado DT-17"
        ];
        foreach (var gap in requiredGaps)
            Assert.That(matrix, Does.Contain(gap), "Lacuna não deve desaparecer sem nova evidência: " + gap);
        Assert.Multiple(() =>
        {
            Assert.That(matrix, Does.Contain("Nunca resetar `JornadaLocal`"));
            Assert.That(matrix, Does.Contain("DT-10 permanece última"));
            Assert.That(matrix, Does.Contain("A separação da CI está excluída"));
            Assert.That(matrix, Does.Contain("não** aceite integral"));
        });
    }
}
