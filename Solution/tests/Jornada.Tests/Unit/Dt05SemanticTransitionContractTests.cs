using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class Dt05SemanticTransitionContractTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void V1_signature_field_order_and_documentation_are_frozen()
    {
        var root = Root();
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "20260927_Linkage_Transicao_Semantica_DT05.sql"));
        var contract = File.ReadAllText(Path.Combine(root, "docs", "DT05_Assinatura_Semantica_V1.md"));
        var signatureStart = sql.IndexOf("N'DT05_V1|'", StringComparison.Ordinal);
        var signatureEnd = sql.IndexOf("))) AS assinatura", signatureStart, StringComparison.Ordinal);
        Assert.That(signatureStart, Is.GreaterThan(0));
        Assert.That(signatureEnd, Is.GreaterThan(signatureStart));
        var signature = sql[signatureStart..signatureEnd];
        var fields = new[]
        {
            "r.modelo_id", "r.modelo_versao", "r.status", "r.motivo",
            "r.resultado_publicacao", "r.pessoa_uuid_publicado", "r.status_publicacao",
            "r.motivo_publicacao", "r.melhor_candidato_uuid", "r.segundo_candidato_uuid",
            "r.politica_publicacao_versao", "r.pessoa_origem_id_publicado"
        };
        var previous = -1;
        foreach (var field in fields)
        {
            var index = signature.IndexOf(field, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThan(previous), $"DT-05 V1 signature field missing or reordered: {field}");
            previous = index;
            Assert.That(contract, Does.Contain($"`{field[2..]}`"),
                $"V1 normative contract must list {field}");
        }
        Assert.Multiple((Action)(() =>
        {
            Assert.That(signature, Does.Contain("COALESCE("));
            Assert.That(signature, Does.Contain("N'<NULL>'"));
            Assert.That(signature, Does.Not.Contain("r.linkage_run_id"));
            Assert.That(signature, Does.Not.Contain("r.score_melhor"));
            Assert.That(sql, Does.Contain("anterior.assinatura_sha256<>s.assinatura"));
        }));
    }

    [Test]
    public void Ledger_is_additive_and_registered_inside_publication_transaction()
    {
        var root = Root();
        var runner = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "20260927_Linkage_Transicao_Semantica_DT05.sql"));
        var publish = runner.IndexOf("private async Task<LinkageRunStatus> PublishAsync(", StringComparison.Ordinal);
        var progressive = runner.IndexOf("{ProgressivePublicationSql()}", publish, StringComparison.Ordinal);
        var ledger = runner.IndexOf("EXEC identidade.sp_registrar_transicoes_linkage_run", progressive, StringComparison.Ordinal);
        var published = runner.IndexOf("SET status='PUBLICADO'", ledger, StringComparison.Ordinal);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(progressive, Is.GreaterThan(publish));
            Assert.That(ledger, Is.GreaterThan(progressive));
            Assert.That(published, Is.GreaterThan(ledger));
            Assert.That(migration, Does.Contain("CREATE OR ALTER PROCEDURE identidade.sp_registrar_transicoes_linkage_run"));
            Assert.That(migration, Does.Contain("OUTER APPLY"));
            Assert.That(migration, Does.Contain("anterior.assinatura_sha256<>s.assinatura"));
            Assert.That(migration, Does.Contain("UQ_linkage_transicao_resultado"));
            Assert.That(migration, Does.Not.Contain("DELETE FROM identidade.linkage_resultado"));
        }));
    }
}
