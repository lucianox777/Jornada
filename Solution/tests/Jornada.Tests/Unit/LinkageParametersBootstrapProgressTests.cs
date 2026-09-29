using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LinkageParametersBootstrapProgressTests
{
    [Test]
    public void Program_EmitsHeartbeatWhileCanonicalNameSnapshotIsLoading()
    {
        var root = FindRepositoryRoot();
        var programPath = Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain("RunHostWithHeartbeatAsync"));
            Assert.That(program, Does.Contain("TimeSpan.FromSeconds(15)"));
            Assert.That(program, Does.Contain("processo ativo, aguarde"));
            Assert.That(program, Does.Contain("milhoes de linhas e pode levar alguns minutos"));
            Assert.That(program, Does.Contain("ensureBuilder.Build(),"));
            Assert.That(program, Does.Contain("operation == NameFrequencySnapshotLoader.Operation"));
        });
    }

    [Test]
    public void GenerateDraft_FailsClosedWithoutExplicitIbgeLoad()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "Program.cs"));
        var draftStart = program.IndexOf("if (operation == \"GENERATE_DRAFT\"", StringComparison.Ordinal);
        var draftEnd = program.IndexOf("builder.Services.AddSingleton<IOperationalSqlAdapter>", draftStart, StringComparison.Ordinal);
        Assert.That(draftStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(draftEnd, Is.GreaterThan(draftStart));
        var draft = program[draftStart..draftEnd];
        Assert.Multiple(() =>
        {
            Assert.That(draft, Does.Contain("GenerateDraftIbgePrecondition.RequireActiveAsync"));
            Assert.That(draft, Does.Contain("ActiveNameFrequencyReferenceQuery.HasActiveAsync"));
            Assert.That(draft, Does.Not.Contain("EnsureCanonicalActiveAsync"));
            Assert.That(draft, Does.Not.Contain("NameFrequencySnapshotLoader"));
        });
    }

    [Test]
    public void GenerateDraft_RejectsMissingIbgeBeforeStartingWorker()
    {
        var checkedReference = false;
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Jornada.Linkage.Parameters.Worker.GenerateDraftIbgePrecondition.RequireActiveAsync(() =>
            {
                checkedReference = true;
                return Task.FromResult(false);
            }));
        Assert.Multiple(() =>
        {
            Assert.That(checkedReference, Is.True);
            Assert.That(exception!.Message, Does.Contain("referência IBGE ATIVA"));
        });
    }

    [Test]
    public async Task GenerateDraft_AllowsPreviouslyActiveIbgeWithoutLoading()
    {
        var checks = 0;
        await Jornada.Linkage.Parameters.Worker.GenerateDraftIbgePrecondition.RequireActiveAsync(() =>
        {
            checks++;
            return Task.FromResult(true);
        });
        Assert.That(checks, Is.EqualTo(1));
    }

    [Test]
    public void CanonicalIbgeReference_RequiresActiveStateAndCanReusePublishedRows()
    {
        var root = FindRepositoryRoot();
        var state = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "NameFrequencyReferenceState.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(state, Does.Contain("status == \"ATIVA\""));
            Assert.That(state, Does.Contain("status == \"CARREGANDO\""));
            Assert.That(state, Does.Contain("conteudo_sha256"));
            Assert.That(state, Does.Contain("tipo='NOME'"));
            Assert.That(state, Does.Contain("tipo='SOBRENOME'"));
            Assert.That(state, Does.Contain("SET status='ATIVA'"));
            Assert.That(state, Does.Contain("IsolationLevel.Serializable"));
        });
    }

    [Test]
    public void DraftBootstrap_DoesNotAcceptActiveReferenceWithMissingMarginals()
    {
        var workerDir = Path.Combine(FindRepositoryRoot(), "Solution", "src",
            "Jornada.Linkage.Parameters.Worker");
        var program = File.ReadAllText(Path.Combine(workerDir, "Program.cs"));
        var query = File.ReadAllText(Path.Combine(workerDir, "ActiveNameFrequencyReferenceQuery.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain("ActiveNameFrequencyReferenceQuery.HasActiveAsync"));
            Assert.That(query, Does.Contain("DATALENGTH(v.conteudo_sha256)=32"));
            Assert.That(query, Does.Contain("n.tipo='NOME'"));
            Assert.That(query, Does.Contain("s.tipo='SOBRENOME'"));
        });
    }

    [Test]
    public void CanonicalIbgeReference_IsPreloadedAsEnvironmentBootstrap()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Program.cs"));
        var compose = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "docker-compose.yml"));
        var calibration = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "install",
            "windows-production",
            "Invoke-JornadaLinkageCalibration.ps1"));

        const string ensureOperation = "ENSURE_NAME_FREQUENCY_SNAPSHOT";
        const string canonicalReference = "CENSO2022_NOMES_BRASIL_V1";

        var bootstrapServiceIndex = compose.IndexOf("jornada-reference-bootstrap:", StringComparison.Ordinal);
        var node1Index = compose.IndexOf("jornada-node1:", StringComparison.Ordinal);
        var node2Index = compose.IndexOf("jornada-node2:", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain($"const string EnsureNameFrequencySnapshotOperation = \"{ensureOperation}\""));
            Assert.That(program, Does.Contain($"const string CanonicalNameFrequencyReferenceCode = \"{canonicalReference}\""));
            Assert.That(program, Does.Contain("NameFrequencyReferenceState.EnsureCanonicalActiveAsync"));
            Assert.That(program, Does.Contain("CanonicalNameFrequencyReferenceState.Reactivated"));
            Assert.That(program, Does.Contain("reativada sem recarga"));
            Assert.That(program, Does.Not.Contain("HasPublishedNameFrequencyReferenceAsync"));

            Assert.That(bootstrapServiceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bootstrapServiceIndex, Is.LessThan(node1Index));
            Assert.That(bootstrapServiceIndex, Is.LessThan(node2Index));
            Assert.That(compose, Does.Contain("jornada-reference-bootstrap:\n      condition: service_completed_successfully"));
            Assert.That(compose, Does.Contain($"LinkageParameters__Operation: {ensureOperation}"));
            Assert.That(compose, Does.Contain("/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll"));

            Assert.That(calibration, Does.Contain($"Invoke-Parameters '{ensureOperation}'"));
            Assert.That(calibration, Does.Not.Contain("Invoke-Parameters 'LOAD_NAME_FREQUENCY_SNAPSHOT'"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}
