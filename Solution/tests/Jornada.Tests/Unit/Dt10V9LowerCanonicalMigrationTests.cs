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
    public void Policy_issuer_requires_normal_immutable_dt15_digest_approval()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "Solution", "database",
            "migrations", "20261026_DT10_V9_Lower_Governed_Off.sql"));
        var start = sql.IndexOf("CREATE OR ALTER PROCEDURE auditoria.sp_autorizar_v9_dt10_lower_policy",
            StringComparison.Ordinal);
        var end = sql.IndexOf("CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote",
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        Assert.That(end, Is.GreaterThan(start));
        var issuer = sql[start..end];
        var publisher = sql[end..];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(issuer, Does.Contain("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE"));
            Assert.That(issuer, Does.Contain("a.acao=N'ACTIVATE'"));
            Assert.That(issuer, Does.Contain("d.origem_evidencia IN(N'REPRESENTATIVA_HML',N'REPRESENTATIVA_PROD')"));
            Assert.That(issuer, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(issuer, Does.Contain("HASHBYTES('SHA2_256',@bytes)"));
            Assert.That(issuer, Does.Contain("'$.FitFingerprintSha256'),2) IS NULL"));
            Assert.That(issuer, Does.Contain("'$.Threshold')) IS NULL"));
            Assert.That(issuer, Does.Contain("'$.GoldParentSnapshotSha256'),2) IS NULL"));
            Assert.That(issuer, Does.Contain("N'DT10_POLICY_SHA256='"));
            Assert.That(issuer, Does.Contain("a.motivo COLLATE Latin1_General_100_BIN2"));
            Assert.That(issuer, Does.Contain("THROW 51839"));
            Assert.That(issuer, Does.Contain("THROW 51840"));
            Assert.That(issuer, Does.Not.Contain("GRANT EXECUTE"));
            Assert.That(issuer, Does.Not.Contain("SET habilitada=1"));
            Assert.That(publisher, Does.Contain("CONVERT(NVARCHAR(64),pa.politica_sha256,2)"));
            Assert.That(publisher, Does.Contain("a.motivo COLLATE Latin1_General_100_BIN2"));
        }));
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
            // A claimed no-candidate branch cannot carry ANY retrieved
            // candidate: this protects the legacy promotion from forged reasons.
            Assert.That(migration, Does.Contain("l.raw_motivo LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(migration, Does.Contain("l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(migration, Does.Not.Contain("l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'"));
            Assert.That(migration, Does.Contain("r.melhor_candidato_uuid IS NOT NULL"));
            Assert.That(migration, Does.Contain("r.segundo_candidato_uuid IS NOT NULL"));
            Assert.That(migration, Does.Contain("THROW 51835"));
            Assert.That(migration, Does.Contain("r.pessoa_observacao_id=l.leader_observacao_id"));
            Assert.That(migration, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(migration, Does.Contain("auditoria.modelo_linkage_dossie_decisao"));
            Assert.That(migration, Does.Contain("d.valido_ate>SYSDATETIMEOFFSET()"));
            // A synthetic DEV dossier may exercise disposable SQL tests but
            // cannot satisfy the canonical publication authorization.
            Assert.That(migration, Does.Contain("d.origem_evidencia IN(N'REPRESENTATIVA_HML',N'REPRESENTATIVA_PROD')"));
            Assert.That(migration, Does.Not.Contain("d.origem_evidencia IN(N'SINTETICA_DEV'"));
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
