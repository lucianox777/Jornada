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
    [Test]
    public void Controlled_rollback_preserves_hardened_no_candidate_publication()
    {
        var rollback = File.ReadAllText(Path.Combine(Root(), "Solution", "database",
            "rollback", "20261026_DT10_V9_Lower_Disable_Restore.sql"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(rollback, Does.Contain("SET habilitada=0"));
            Assert.That(rollback, Does.Contain("l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(rollback, Does.Contain("l.raw_motivo LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(rollback, Does.Contain("r.pessoa_observacao_id=l.leader_observacao_id"));
            Assert.That(rollback, Does.Contain("r.melhor_candidato_uuid IS NOT NULL"));
            Assert.That(rollback, Does.Contain("r.segundo_candidato_uuid IS NOT NULL"));
            Assert.That(rollback, Does.Contain("r.pessoa_uuid_resolvido IS NOT NULL"));
            Assert.That(rollback, Does.Contain("THROW 51835"));
            Assert.That(rollback, Does.Not.Contain("l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'"));
        }));
    }


    [Test]
    public void Controlled_rollback_restores_exact_hardened_pre_v9_batch_when_integrated()
    {
        // #1210 is integrated in this branch. Missing security migration
        // must FAIL the gate, never turn rollback verification into SKIP.
        var database = Path.Combine(Root(), "Solution", "database");
        var hardenedPath = Path.Combine(database, "migrations",
            "20261029_Linkage_NoCandidate_Literal_Guards.sql");
        Assert.That(File.Exists(hardenedPath), Is.True,
            "The integrated #1210 hardening migration is mandatory for safe V9 rollback.");

        var hardened = File.ReadAllText(hardenedPath);
        var rollback = File.ReadAllText(Path.Combine(database, "rollback",
            "20261026_DT10_V9_Lower_Disable_Restore.sql"));
        const string batch = "CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote";
        var expectedStart = hardened.IndexOf(batch, StringComparison.Ordinal);
        var actualStart = rollback.IndexOf(batch, StringComparison.Ordinal);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(expectedStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(actualStart, Is.GreaterThanOrEqualTo(0));
        }));
        Assert.That(rollback[actualStart..].Trim(), Is.EqualTo(hardened[expectedStart..].Trim()),
            "Disabling V9 must restore the entire #1210 hardened batch procedure, including every guard, not a hand-copied approximation.");
    }

    [Test]
    public void Governed_batch_must_be_last_redefinition_after_literal_no_candidate_hardening()
    {
        var database = Path.Combine(Root(), "Solution", "database");
        var manifest = File.ReadAllText(Path.Combine(database, "migrations", "manifest.txt"));
        var installer = File.ReadAllText(Path.Combine(database, "Jornada_Fase1_v3.70.sql"));
        const string hardened = "20261029_Linkage_NoCandidate_Literal_Guards.sql";
        const string governed = "DT10_V9_Lower_Governed_Off.sql";
        var hardenedFile = Path.Combine(database, "migrations", hardened);
        var hardenedInManifest = manifest.IndexOf(hardened, StringComparison.Ordinal);
        var governedInManifest = manifest.IndexOf(governed, StringComparison.Ordinal);
        var hardenedInInstaller = installer.IndexOf(hardened, StringComparison.Ordinal);
        var governedInInstaller = installer.IndexOf(governed, StringComparison.Ordinal);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(governedInManifest, Is.GreaterThanOrEqualTo(0));
            Assert.That(governedInInstaller, Is.GreaterThanOrEqualTo(0));
            // #1210 is already integrated. Missing migration or manifest entry
            // must FAIL instead of silently skipping the precedence assertions.
            Assert.That(File.Exists(hardenedFile), Is.True,
                "Integrated #1210 migration is required by both install paths.");
            Assert.That(hardenedInManifest, Is.GreaterThanOrEqualTo(0),
                "Security hardening cannot be omitted from upgrade order.");
            Assert.That(hardenedInInstaller, Is.GreaterThanOrEqualTo(0),
                "Security hardening cannot be omitted from clean installs.");
            Assert.That(governedInManifest, Is.GreaterThan(hardenedInManifest),
                "V9 must be the final batch procedure redefinition on upgrades.");
            Assert.That(governedInInstaller, Is.GreaterThan(hardenedInInstaller),
                "V9 must be the final batch procedure redefinition on clean installs.");
        }));
    }
    [Test]
    public void No_candidate_guard_checks_every_observation_of_the_origin_before_publication()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "Solution", "database", "migrations",
            "20261026_DT10_V9_Lower_Governed_Off.sql"));
        var guard = sql.IndexOf("THROW 51835", StringComparison.Ordinal);
        Assert.That(guard, Is.GreaterThan(0));
        var start = sql.LastIndexOf("IF EXISTS(", guard, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var clause = sql.Substring(start, guard - start);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(clause, Does.Contain("JOIN #dt10_rows origin_row"));
            Assert.That(clause, Does.Contain("origin_row.pessoa_origem_id=l.pessoa_origem_id"));
            Assert.That(clause, Does.Contain("r.pessoa_observacao_id=origin_row.pessoa_observacao_id"));
            Assert.That(clause, Does.Contain("l.replay_version IS NULL"));
            Assert.That(clause, Does.Contain("r.melhor_candidato_uuid IS NOT NULL"));
            Assert.That(clause, Does.Contain("r.segundo_candidato_uuid IS NOT NULL"));
            Assert.That(clause, Does.Contain("r.pessoa_uuid_resolvido IS NOT NULL"));
        }));
    }

    [Test]
    public void Dt06_verifier_fails_closed_when_dt10_governed_contract_is_missing()
    {
        var verifier = File.ReadAllText(Path.Combine(Root(), "Solution", "database",
            "tests", "DT06_Verificar_Schema_370.sql"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(verifier, Does.Contain("OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U')"));
            Assert.That(verifier, Does.Contain("OBJECT_ID(N'auditoria.v9_dt10_lower_policy_autorizacao',N'U')"));
            Assert.That(verifier, Does.Contain("OBJECT_ID(N'auditoria.tr_v9_dt10_lower_policy_autorizacao_append_only',N'TR')"));
            Assert.That(verifier, Does.Contain("OBJECT_ID(N'auditoria.sp_autorizar_v9_dt10_lower_policy',N'P')"));
            Assert.That(verifier, Does.Contain("COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'fit_fingerprint')"));
            Assert.That(verifier, Does.Contain("COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'snapshot_sha256')"));
            Assert.That(verifier, Does.Contain("sys.default_constraints"));
            Assert.That(verifier, Does.Contain("c.name=N'habilitada'"));
            Assert.That(verifier, Does.Contain("THROW 51373"));
            Assert.That(verifier, Does.Contain("FROM sys.triggers t"));
            Assert.That(verifier, Does.Contain("t.is_disabled=1"));
            Assert.That(verifier, Does.Contain("THROW 51374"));
        }));
    }

    [Test]
    public void Heavy_v9_30k_is_manual_or_scoped_integration_only_while_pr_preflight_remains()
    {
        var workflow = File.ReadAllText(Path.Combine(Root(), ".github", "workflows",
            "v9-physical-seed42-probe.yml"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(workflow, Does.Contain("pull_request:"),
                "PRs must still classify impact and archive the decision.");
            Assert.That(workflow, Does.Contain("workflow_dispatch:"),
                "The final physical gate must remain executable explicitly.");
            Assert.That(workflow, Does.Contain(
                "if: needs.impact.outputs.run_physical_30k == 'true' && (github.event_name == 'workflow_dispatch' || (github.event_name == 'pull_request' && github.event.pull_request.head.ref == 'integration/v9-dt10-joint-train-20261031'))"),
                "The expensive 30k probe must run only for an explicit dispatch or the exact V9 integration branch, when impact requires it.");
            Assert.That(workflow, Does.Contain("test \"$latest\" = \"$EXPECTED_HEAD_SHA\""),
                "The scoped integration run must reject a superseded pull request head.");
            Assert.That(workflow, Does.Contain("python3 Solution/scripts/v9_manual_probe_evidence.py"),
                "All other impacted PRs must still fail closed until exact-SHA physical evidence is available.");
            Assert.That(workflow, Does.Contain("name: V9 30k - classify impact"));
        }));
    }

}
