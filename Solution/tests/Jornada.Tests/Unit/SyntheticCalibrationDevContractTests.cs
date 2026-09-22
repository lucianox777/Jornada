namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticCalibrationDevContractTests
{
    [Test]
    public void Synthetic_calibration_DEV_keeps_truth_out_of_calibration_and_evaluates_only_post_draft()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Ensaio",
            "Program.cs"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Ensaio",
            "SyntheticCalibrationDevRunner.cs"));
        var csproj = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Ensaio",
            "Jornada.Ensaio.csproj"));
        var evaluator = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Evaluation",
            "SyntheticEvaluationEngine.cs"));
        var evaluatorProgram = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Evaluation",
            "Program.cs"));
        var evidenceWriter = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Evaluation",
            "SyntheticEvaluationEvidenceWriter.cs"));
        var evidenceSql = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "database",
            "migrations",
            "20260922_Linkage_Synthetic_Evaluation_Evidence.sql"));
        var promotionSql = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "database",
            "migrations",
            "20260915_Linkage_Model_Promotion_Contract.sql"));
        var conferenceGovernanceSql = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "database",
            "migrations",
            "20260920_Linkage_Conference_Command_Governance.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain("SyntheticCalibrationDevRunner.Mode"));
            Assert.That(source, Does.Contain("SYNTHETIC_CALIBRATION_DEV"));
            Assert.That(source, Does.Contain("Jornada.EnvironmentProfile"));
            Assert.That(source, Does.Contain("RequiredEnvironment = \"Development\""));
            Assert.That(source, Does.Contain("Jornada.Api"));
            Assert.That(source, Does.Contain("Jornada.Processor.Worker"));
            Assert.That(source, Does.Contain("X-Jornada-Gestor"));
            Assert.That(source, Does.Contain("X-Jornada-Access-Key"));
            Assert.That(source, Does.Contain("LOAD_NAME_FREQUENCY_SNAPSHOT"));
            Assert.That(source, Does.Contain("GENERATE_DRAFT"));
            Assert.That(source, Does.Contain("--synthetic-evaluate-root"));
            Assert.That(source, Does.Contain("--synthetic-run-group-id"));
            Assert.That(source, Does.Contain("AppendOnlyPersisted: true"));
            Assert.That(source, Does.Contain("Jornada.Linkage.Evaluation"));
            Assert.That(source, Does.Contain("SyntheticTruthConsumed: false"));
            Assert.That(source, Does.Contain("PostDraftEvaluationTruthConsumed: true"));
            Assert.That(source, Does.Contain("ModelPromotionAttempted: false"));
            Assert.That(source, Does.Contain("SCALE-%"));
            Assert.That(source, Does.Not.Contain("bridge-truth.jsonl"));
            Assert.That(source, Does.Not.Contain("BasePersonId"));
            Assert.That(source, Does.Not.Contain("\"VALIDATE\""));
            Assert.That(source, Does.Not.Contain("\"ACTIVATE\""));
            Assert.That(csproj, Does.Not.Contain("Jornada.Linkage.SyntheticCorpus"),
                "O Ensaio deve executar o gerador como processo DEV separado, não acoplar seu assembly.");

            Assert.That(evaluatorProgram, Does.Contain("--synthetic-evaluate-root"));
            Assert.That(evaluatorProgram, Does.Contain("--synthetic-run-group-id"));
            Assert.That(evaluatorProgram, Does.Contain("SyntheticEvaluationEngine"));
            Assert.That(evaluatorProgram, Does.Contain("SyntheticEvaluationEvidenceWriter"));
            Assert.That(evaluator, Does.Contain("bridge-truth.jsonl"));
            Assert.That(evaluator, Does.Contain("BasePersonId"));
            Assert.That(evaluator, Does.Contain("BlockingProjectionKeyProjector"));
            Assert.That(evaluator, Does.Contain("CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION")
                .Or.Contain("LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics"));
            Assert.That(evaluator, Does.Contain("modelo RASCUNHO"));
            Assert.That(evaluator, Does.Contain("Jornada.EnvironmentProfile"));
            Assert.That(evaluator, Does.Contain("generation-manifest.json"));
            Assert.That(evaluator, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(evaluator, Does.Not.Contain("UPDATE identidade.modelo_linkage"));
            Assert.That(evaluator, Does.Not.Contain("\"VALIDATE\""));
            Assert.That(evaluator, Does.Not.Contain("\"ACTIVATE\""));

            Assert.That(evidenceWriter, Does.Contain("sp_registrar_avaliacao_sintetica_linkage"));
            Assert.That(evidenceWriter, Does.Contain("linkage_avaliacao_sintetica_metrica_tvp"));
            Assert.That(evidenceSql, Does.Contain("append-only"));
            Assert.That(evidenceSql, Does.Contain("NOT_ASSESSED_ISSUE_31"));
            Assert.That(evidenceSql, Does.Contain("promocao_autorizada"));
            Assert.That(evidenceSql, Does.Contain("Jornada.EnvironmentProfile"));
            Assert.That(evidenceSql, Does.Contain("Development"));
            Assert.That(evidenceSql, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(evidenceSql, Does.Not.Contain("base_person_id"));
            Assert.That(evidenceSql, Does.Not.Contain("observation_id"));
            Assert.That(evidenceSql, Does.Not.Contain("data_nascimento"));
            Assert.That(promotionSql, Does.Not.Contain("linkage_avaliacao_sintetica"));
            Assert.That(conferenceGovernanceSql, Does.Not.Contain("linkage_avaliacao_sintetica"));
        });
    }

    [Test]
    public void Local_database_provisioners_set_resident_Development_marker_outside_canonical_DDL()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "local-db.sh"));
        var powershell = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "local-db.ps1"));
        var ddl = File.ReadAllText(Path.Combine(root, "Solution", "database", "Jornada_Fase1.sql"));

        foreach (var script in new[] { shell, powershell })
        {
            Assert.Multiple(() =>
            {
                Assert.That(script, Does.Contain("Jornada.EnvironmentProfile"));
                Assert.That(script, Does.Contain("sp_updateextendedproperty"));
                Assert.That(script, Does.Contain("sp_addextendedproperty"));
                Assert.That(script, Does.Contain("Development"));
            });
        }

        Assert.That(ddl, Does.Not.Contain("Jornada.EnvironmentProfile"),
            "O perfil deve continuar sendo provisionado pelo ambiente, não pelo DDL canônico.");
    }

    [Test]
    public void Local_synthetic_calibration_wrappers_reset_without_SCALE_and_keep_HMAC_out_of_arguments()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "local-synthetic-calibration.sh"));
        var powershell = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "local-synthetic-calibration.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(shell, Does.Contain("reset --no-synthetic-corpus"));
            Assert.That(shell, Does.Contain("JORNADA_SYNTH_PSEUDONYMIZATION_KEY"));
            Assert.That(shell, Does.Contain("SYNTHETIC_CALIBRATION_DEV"));
            Assert.That(shell, Does.Contain("ConnectionStrings__Jornada"));
            Assert.That(shell, Does.Not.Contain("--pseudonymization-key "));

            Assert.That(powershell, Does.Contain("reset -NoSyntheticCorpus"));
            Assert.That(powershell, Does.Contain("JORNADA_SYNTH_PSEUDONYMIZATION_KEY"));
            Assert.That(powershell, Does.Contain("SYNTHETIC_CALIBRATION_DEV"));
            Assert.That(powershell, Does.Contain("ConnectionStrings__Jornada"));
            Assert.That(powershell, Does.Not.Contain("--pseudonymization-key "));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
