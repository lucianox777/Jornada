using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt17RegressionMatrixContractTests
{
    private static readonly (string File, string Marker)[] ExistingProofs =
    [
        ("Solution/tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs", "DcId02_unanchored_decided_split_requires_two_new_canonical_uuids_and_makes_old_reference_historical"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs", "DcId02_cpf_anchor_keeps_holder_on_old_uuid_and_moves_only_other_person"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs", "DcId02_bare_initial_uuid_is_not_promoted_to_canonical_destination"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs", "Observation_core_identity_attributes_are_nullable_while_secretariat_contract_may_require_them"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs", "Initial_uuid_is_not_a_blocking_or_scoring_feature"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs", "Existing_identity_routes_keep_401_403_and_scope_regressions"),
        ("Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs", "Cpf_regressions_cover_valid_invalid_and_conflict_evidence_without_claiming_route_execution"),
        ("Solution/tests/Jornada.Tests/Unit/CpfRulesTests.cs", "Normalizes_valid_cpf"),
        ("Solution/tests/Jornada.Tests/Unit/CpfRulesTests.cs", "Rejects_invalid_cpf"),
        ("Solution/tests/Jornada.Tests/Unit/ProgressiveIdentityTests.cs", "InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation"),
        ("Solution/tests/Jornada.Tests/Unit/ProgressiveOriginApiTests.cs", "Origin_route_fails_closed_and_returns_only_owner_snapshot"),
        ("Solution/tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs", "Route_enforces_access_and_pre_response_audit"),
        ("Solution/tests/Jornada.Tests/Unit/AuthorizationMatrixContractTests.cs", "Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes"),
        ("Solution/tests/Jornada.Tests/Unit/IdentityCompositionTests.cs", "Fully_decided_unanchored_split_requires_new_or_already_admitted_destinations"),
        ("Solution/tests/Jornada.Tests/Unit/IdentityCompositionTests.cs", "Cpf_anchored_split_preserves_anchor_and_allocates_other_destination"),
        ("Solution/tests/Jornada.Tests/Unit/IdentityCompositionTests.cs", "Reusing_a_historical_alias_or_someone_elses_initial_uuid_is_rejected"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/CpfAnchorResolutionApiTests.cs", "Resolver_classifies_structurally_invalid_cpf_without_uuid"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/CpfAnchorResolutionApiTests.cs", "Resolver_uses_permanent_anchor_when_current_map_is_closed_or_in_conflict"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/CpfAnchorGovernedCorrectionTests.cs", "Governed_correction_cannot_transfer_permanent_cpf_anchor"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/IdentityReplayInvariantTests.cs", "Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/Dt05SemanticThreeWavesSqlServerTests.cs", "Four_waves_ignore_unchanged_and_score_only_retries_without_losing_raw_results"),
        ("Solution/tests/Jornada.Integration.Tests/Integration/Dt05PublicationGuardsSqlServerTests.cs", "Ledger_requires_transaction_and_executing_run_without_side_effects"),
        ("Solution/scripts/dt05-cpf-late-wave.ps1", "DT05_CPF_LATE_REAL_RUNNER_E2E")
    ];

    [Test]
    public void Matrix_references_real_proofs_and_preserves_scope_and_execution_semantics()
    {
        var root = RepositoryRoot();
        var matrixPath = Path.Combine(root.FullName, "Solution", "docs", "DT17_Matriz_Regressao.md");
        var matrix = File.ReadAllText(matrixPath);

        foreach (var (relativePath, marker) in ExistingProofs)
        {
            var proofPath = Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(proofPath), Is.True, "Prova inexistente: " + relativePath);
            Assert.That(File.ReadAllText(proofPath), Does.Contain(marker), "Método/marcador ausente: " + relativePath);
            Assert.That(matrix, Does.Contain("../" + relativePath["Solution/".Length..]), "Link documental ausente: " + relativePath);
            Assert.That(matrix, Does.Contain(marker), "Método/marcador não inventariado: " + marker);
        }

        foreach (var required in new[]
        {
            "SUPERADA por DC-LK-01",
            "Skipped não conta como prova",
            "NÃO EXECUTADA",
            "LACUNA: MARIA SOUZA / MARIA SOUZA LIMA / MARIA LIMA",
            "LACUNA: DT-05 replay histórico NAS",
            "DT-10 RESOLVIDA (01/10/2026)",
            "LACUNA: #539 / #378 compartilhamento institucional",
            "LACUNA: #506 / #31 validação externa e representatividade",
            "LACUNA: Ensaio integrado DT-17",
            "Nunca resetar `JornadaLocal`",
            "A separação da CI está excluída",
            "a próxima etapa é E2E-A/DT-17A"
        })
            Assert.That(matrix, Does.Contain(required));
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RELEASE_INFO.txt")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null, "A raiz do repositório deve ser encontrada pelo teste.");
        return root!;
    }
}
