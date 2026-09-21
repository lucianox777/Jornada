namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IdentityDecisionEvidenceContractTests
{
    [Test]
    public void Migration_makes_human_evidence_explicit_and_keeps_legacy_backfill_write_closed()
    {
        var root = FindSolutionRoot();
        var sql = File.ReadAllText(Path.Combine(
            root, "database", "migrations",
            "20260921_Identidade_Decisao_Evidencia_Estruturada.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("DOCUMENTO_VERIFICADO"));
            Assert.That(sql, Does.Contain("CONFIRMACAO_SEM_DOCUMENTO"));
            Assert.That(sql, Does.Contain("DECISAO_PREVIA_APLICADA"));
            Assert.That(sql, Does.Contain("LEGADO_NAO_CLASSIFICADO"));
            Assert.That(sql, Does.Contain("LEGADO_NAO_CLASSIFICADO é reservado ao backfill histórico"));
            Assert.That(sql, Does.Contain("documento_tipo_codigo"));
            Assert.That(sql, Does.Contain("elegivel_referencia_estrato_dificil"));
            Assert.That(sql, Does.Contain("CONFIRMACAO_SEM_DOCUMENTO' THEN 1 ELSE 0"));
        });
    }

    [Test]
    public void Api_requires_human_evidence_but_application_of_prior_case_is_not_new_evidence()
    {
        var root = FindRepositoryRoot();
        var contracts = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Contracts", "ApiContracts.cs"));
        var service = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Api", "IdentityCorrectionService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(contracts, Does.Contain("string EvidenciaTipo, string? DocumentoTipoCodigo"));
            Assert.That(service, Does.Contain("NormalizeDecisionEvidence"));
            Assert.That(service, Does.Contain("DOCUMENTO_VERIFICADO exige DocumentoTipoCodigo"));
            Assert.That(service, Does.Contain("CONFIRMACAO_SEM_DOCUMENTO não admite DocumentoTipoCodigo"));
            Assert.That(service, Does.Contain("new DecisionEvidence(\"DECISAO_PREVIA_APLICADA\", null)"));
            Assert.That(service, Does.Not.Contain("LEGADO_NAO_CLASSIFICADO"));
        });
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln"))
                && Directory.Exists(Path.Combine(directory.FullName, "database")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }

    private static string FindRepositoryRoot()
    {
        var solution = new DirectoryInfo(FindSolutionRoot());
        return solution.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
