using System;
using System.IO;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldDisposableLowerProcedurePatchTests
{
    private const string CanonicalGuard = "l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'";
    private const string CanonicalDefinition = """
        CREATE PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote
            @linkage_run_id UNIQUEIDENTIFIER
        AS
        BEGIN
            IF (l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%')
                THROW 51810, 'DT-10: motivo inválido.', 1;
        END
        """;

    [Test]
    public void Recognizes_current_literal_prefix_and_preserves_disposable_only_gates()
    {
        var patched = V9GoldDisposableLowerProcedurePatch.Apply(CanonicalDefinition);
        Assert.Multiple(() =>
        {
            Assert.That(patched, Does.StartWith("ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote"));
            Assert.That(patched, Does.Contain("l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(patched, Does.Contain("LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL"));
            Assert.That(patched, Does.Contain("DB_NAME()=N'JornadaHarness'"));
            Assert.That(patched, Does.Contain("SERVERPROPERTY(N'IsLocalDB')"));
            Assert.That(patched, Does.Contain("Jornada.V9.ExperimentalHarness"));
            Assert.That(patched, Does.Not.Contain("NOT LIKE N'SEM_CANDIDATO_%'"));
        });
    }

    [Test]
    public void Accepts_sql_server_normalized_declaration_without_relaxing_guard()
    {
        var normalized = CanonicalDefinition.Replace("CREATE PROCEDURE", " CREATE   PROCEDURE", StringComparison.Ordinal);
        Assert.That(V9GoldDisposableLowerProcedurePatch.Apply(normalized),
            Does.Contain("ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote"));
    }

    [Test]
    public void Rejects_unrecognized_guard_declaration_duplicate_or_missing_throw()
    {
        var invalid = new[]
        {
            CanonicalDefinition.Replace(CanonicalGuard, "l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'", StringComparison.Ordinal),
            CanonicalDefinition.Replace(CanonicalGuard, "l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO%'", StringComparison.Ordinal),
            CanonicalDefinition.Replace("identidade.sp_publicar_resolucao_progressiva_linkage_lote", "identidade.sp_outra_procedure", StringComparison.Ordinal),
            CanonicalDefinition.Replace("THROW 51810", "THROW 51811", StringComparison.Ordinal),
            CanonicalDefinition.Replace(CanonicalGuard, CanonicalGuard + " AND " + CanonicalGuard, StringComparison.Ordinal),
            CanonicalDefinition + " LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL"
        };
        foreach (var definition in invalid)
            Assert.Throws<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(definition));
    }
}
