using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LinkageParametersBootstrapProgressTests
{
    [Test]
    public void Program_EmitsHeartbeatWhileCanonicalNameSnapshotIsLoading()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Parameters.Worker", "Program.cs"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(program, Does.Contain("RunHostWithHeartbeatAsync"));
            Assert.That(program, Does.Contain("TimeSpan.FromSeconds(15)"));
            Assert.That(program, Does.Contain("processo ativo, aguarde"));
            Assert.That(program, Does.Contain("milhoes de linhas e pode levar alguns minutos"));
            Assert.That(program, Does.Contain("ensureBuilder.Build(),"));
            Assert.That(program, Does.Contain("operation == NameFrequencySnapshotLoader.Operation"));
        }));
    }

    [Test]
    public void GenerateDraft_RequiresPersistedBootstrapWithoutReactivation()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "Program.cs"));
        var draftStart = program.IndexOf("if (operation == \"GENERATE_DRAFT\"", StringComparison.Ordinal);
        var draftEnd = program.IndexOf("builder.Services.AddSingleton<IOperationalSqlAdapter>", draftStart, StringComparison.Ordinal);
        Assert.That(draftStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(draftEnd, Is.GreaterThan(draftStart));
        var draft = program[draftStart..draftEnd];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(draft, Does.Contain("PersistedIbgeBootstrapReferenceQuery.RequireAsync"));
            Assert.That(draft, Does.Not.Contain("ActiveNameFrequencyReferenceQuery.HasActiveAsync"));
            Assert.That(draft, Does.Not.Contain("EnsureCanonicalActiveAsync"));
            Assert.That(draft, Does.Not.Contain("NameFrequencySnapshotLoader"));
        }));
    }

    [Test]
    public void PersistedBootstrapQuery_RequiresCanonicalReadyPersonAndMotherDerivatives()
    {
        var query = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "PersistedIbgeBootstrapReferenceQuery.cs"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(query, Does.Contain("CENSO2022_NOMES_BRASIL_V1"));
            Assert.That(query, Does.Contain("u.status=N'PRONTA'"));
            Assert.That(query, Does.Contain("u.recorte_prenome=N'TODOS'"));
            Assert.That(query, Does.Contain("u.recorte_prenome=N'FEMININO'"));
            Assert.That(query, Does.Contain("u.conteudo_origem_sha256=v.conteudo_sha256"));
            Assert.That(query, Does.Contain("IbgeNominalUBootstrapOptions.MethodVersion"));
            Assert.That(query, Does.Not.Contain("v.status='ATIVA'"));
            Assert.That(query, Does.Contain("rascunhos posteriores não exigem referência IBGE ATIVA"));
        }));
    }

    [Test]
    public void CanonicalIbgeReference_ExplicitEnsureCanActivateAndReusePublishedRows()
    {
        var root = FindRepositoryRoot();
        var state = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Parameters.Worker", "NameFrequencyReferenceState.cs"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(state, Does.Contain("status == \"ATIVA\""));
            Assert.That(state, Does.Contain("status == \"CARREGANDO\""));
            Assert.That(state, Does.Contain("conteudo_sha256"));
            Assert.That(state, Does.Contain("tipo='NOME'"));
            Assert.That(state, Does.Contain("tipo='SOBRENOME'"));
            Assert.That(state, Does.Contain("SET status='ATIVA'"));
            Assert.That(state, Does.Contain("IsolationLevel.Serializable"));
        }));
    }

    [Test]
    public void GenerationTrigger_RequiresExplicitPersistedBootstrapNotActiveStatus()
    {
        var migration = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Solution", "database",
            "migrations", "20260929_RF052_Bootstrap_Persistido_Geracao.sql"));
        var worker = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "LinkageParametersWorker.cs"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(migration, Does.Contain("referência explícita do bootstrap IBGE inicial persistido"));
            Assert.That(migration, Does.Contain("u.status=N'PRONTA'"));
            Assert.That(migration, Does.Not.Contain("WHERE status='ATIVA'"));
            Assert.That(worker, Does.Contain("frequencia_nome_versao_id,model_config_bundle_version,model_config_bundle_fingerprint_sha256)"));
            Assert.That(worker, Does.Contain("@ibge_ref"));
            Assert.That(worker, Does.Contain("PersistedIbgeBootstrapReferenceQuery.RequireAsync"));
            Assert.That(worker, Does.Not.Contain("IbgeNominalUReferenceReader.ReadActiveReferenceAsync(connection, workCt)"));
        }));
    }

    [Test]
    public void CanonicalIbgeReference_IsPreloadedAsEnvironmentBootstrap_NotPerCalibration()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Parameters.Worker", "Program.cs"));
        var compose = File.ReadAllText(Path.Combine(root, "Solution", "docker-compose.yml"));
        var calibration = File.ReadAllText(Path.Combine(
            root, "Solution", "install", "windows-production", "Invoke-JornadaLinkageCalibration.ps1"));

        const string ensureOperation = "ENSURE_NAME_FREQUENCY_SNAPSHOT";
        const string canonicalReference = "CENSO2022_NOMES_BRASIL_V1";
        var bootstrapServiceIndex = compose.IndexOf("jornada-reference-bootstrap:", StringComparison.Ordinal);
        var node1Index = compose.IndexOf("jornada-node1:", StringComparison.Ordinal);
        var node2Index = compose.IndexOf("jornada-node2:", StringComparison.Ordinal);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(program, Does.Contain($"const string EnsureNameFrequencySnapshotOperation = \"{ensureOperation}\""));
            Assert.That(program, Does.Contain($"const string CanonicalNameFrequencyReferenceCode = \"{canonicalReference}\""));
            Assert.That(program, Does.Contain("NameFrequencyReferenceState.EnsureCanonicalActiveAsync"));

            Assert.That(bootstrapServiceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bootstrapServiceIndex, Is.LessThan(node1Index));
            Assert.That(bootstrapServiceIndex, Is.LessThan(node2Index));
            Assert.That(compose, Does.Contain("jornada-reference-bootstrap:\n      condition: service_completed_successfully"));
            Assert.That(compose, Does.Contain($"LinkageParameters__Operation: {ensureOperation}"));
            Assert.That(compose, Does.Contain("/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll"));

            Assert.That(calibration, Does.Not.Contain($"Invoke-Parameters '{ensureOperation}'"));
            Assert.That(calibration, Does.Not.Contain("Invoke-Parameters 'ENSURE_IBGE_NOMINAL_U_REFERENCE'"));
            Assert.That(calibration, Does.Contain("GENERATE_DRAFT"));
            Assert.That(calibration, Does.Contain("não carrega, reativa nem recalcula"));
        }));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
                return current.FullName;
            current = current.Parent;
        }
        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}
