using System;
using System.IO;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt10V9LowerCanonicalMigrationTests
{
    private static string Root()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution", "database")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Jornada root not found.");
    }

    [Test]
    public void Canonical_batch_preserves_every_existing_statement_outside_new_guard()
    {
        var directory = Path.Combine(Root(), "Solution", "database", "migrations");
        var before = File.ReadAllText(Path.Combine(directory,
            "20260926_Linkage_Publicacao_Progressiva_Lote.sql"));
        var after = File.ReadAllText(Path.Combine(directory,
            "20261026_DT10_V9_Lower_Governed_Off.sql"));
        var bodyStart = after.IndexOf("-- DT-10: publicação progressiva em lote", StringComparison.Ordinal);
        Assert.That(bodyStart, Is.GreaterThan(0));
        var body = after[bodyStart..];
        var newGuardStart = body.IndexOf("    -- DT-10 V9: the lower nonmatch", StringComparison.Ordinal);
        const string nextGuard = "    IF EXISTS(\n        SELECT 1 FROM #dt10_leaders l\n        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'\n          AND (l.universo_referencia";
        var newGuardEnd = body.IndexOf(nextGuard, newGuardStart, StringComparison.Ordinal);
        const string oldGuard = "    IF EXISTS(\n        SELECT 1 FROM #dt10_leaders l\n        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'\n          AND (l.raw_status";
        var oldStart = before.IndexOf(oldGuard, StringComparison.Ordinal);
        var oldEnd = before.IndexOf(nextGuard, oldStart, StringComparison.Ordinal);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(newGuardStart, Is.GreaterThan(0));
            Assert.That(newGuardEnd, Is.GreaterThan(newGuardStart));
            Assert.That(oldStart, Is.GreaterThan(0));
            Assert.That(oldEnd, Is.GreaterThan(oldStart));
        }));
        var reconstructed = body[..newGuardStart] + before[oldStart..oldEnd] + body[newGuardEnd..];
        Assert.That(reconstructed, Is.EqualTo(before),
            "The migration must not alter serializable locking, precedence, ledger, or replay.");
    }

    [Test]
    public void V9_gate_is_disabled_by_default_and_requires_independent_governance()
    {
        var root = Root();
        var migration = File.ReadAllText(Path.Combine(root, "Solution", "database",
            "migrations", "20261026_DT10_V9_Lower_Governed_Off.sql"));
        var manifest = File.ReadAllText(Path.Combine(root, "Solution", "database",
            "migrations", "manifest.txt"));
        var installer = File.ReadAllText(Path.Combine(root, "Solution", "database",
            "Jornada_Fase1_v3.70.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "Solution", "database",
            "rollback", "20261026_DT10_V9_Lower_Disable_Restore.sql"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(migration, Does.Contain("DEFAULT(0)"));
            Assert.That(migration, Does.Contain("habilitada=1"));
            Assert.That(migration, Does.Contain("N'LIMIAR_INFERIOR_V9_GOVERNADO'"));
            Assert.That(migration, Does.Contain("N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO'"));
            Assert.That(migration, Does.Contain("a.acao<>N'ACTIVATE'"));
            Assert.That(migration, Does.Contain("m.status<>N'ATIVO'"));
            // O motivo operacional, isoladamente, não autoriza criação:
            // o score real do melhor candidato deve estar abaixo do piso.
            Assert.That(migration, Does.Contain("r.melhor_candidato_uuid IS NULL"));
            Assert.That(migration, Does.Contain("r.score_melhor>=p.limiar_inferior"));
            Assert.That(migration, Does.Contain("r.pessoa_observacao_id=l.leader_observacao_id"));
            Assert.That(migration, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(migration, Does.Contain("auditoria.modelo_linkage_dossie_decisao"));
            Assert.That(migration, Does.Contain("d.valido_ate>SYSDATETIMEOFFSET()"));
            Assert.That(migration, Does.Contain("HASHBYTES('SHA2_256',p.snapshot_bytes)"));
            Assert.That(migration, Does.Contain("THROW 51830"));
            Assert.That(migration, Does.Contain("THROW 51810"));
            Assert.That(migration, Does.Not.Contain("LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL"));
            Assert.That(migration, Does.Not.Contain("SERVERPROPERTY(N'IsLocalDB')"));
            Assert.That(manifest, Does.Contain("migrations/20261026_DT10_V9_Lower_Governed_Off.sql"));
            Assert.That(installer, Does.Contain(":r database/migrations/20261026_DT10_V9_Lower_Governed_Off.sql"));
            Assert.That(manifest, Does.Not.Contain("rollback/20261026_DT10_V9_Lower_Disable_Restore.sql"));
            Assert.That(rollback, Does.Contain("SET habilitada=0"));
            Assert.That(rollback, Does.Contain("THROW 51810"));
        }));
    }
}
