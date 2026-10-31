using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

/// <summary>
/// Disposable-only SQL contract test. Synthetic ACTIVATE/dossier records here
/// are deliberately rolled back and are NOT institutional DT-15 approvals,
/// representative evidence, or authorization to enable production policy.
/// </summary>
[TestFixture, Category("Integration"), Category("DT10V9"), NonParallelizable]
public sealed class Dt10V9LowerAuthorizationIssuerLocalDbTests
{
    [Test]
    public async Task Synthetic_dt15_digest_seals_then_governed_lower_publishes_once_and_replays()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_DT10_V9_HARNESS_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            Assert.Ignore("Requires an explicitly provisioned disposable LocalDB JornadaHarness.");

        var builder = new SqlConnectionStringBuilder(cs!);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(builder.InitialCatalog, Is.EqualTo("JornadaHarness").IgnoreCase);
            Assert.That(builder.DataSource, Does.StartWith("(localdb)\\").IgnoreCase);
            Assert.That(builder.IntegratedSecurity, Is.True);
            Assert.That(builder.AttachDBFilename, Is.Empty);
        }));

        await using var sql = new SqlConnection(cs);
        await sql.OpenAsync();
        await using (var guard = sql.CreateCommand())
        {
            guard.CommandText = """
                SELECT CASE WHEN DB_NAME()=N'JornadaHarness'
                  AND COALESCE(TRY_CONVERT(INT,SERVERPROPERTY(N'IsLocalDB')),0)=1
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                      AND CONVERT(NVARCHAR(200),value)=N'Development')
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness'
                      AND CONVERT(NVARCHAR(200),value)=N'PINNED_FIT_PREVIEW_NO_ACTIVATION')
                THEN 1 ELSE 0 END;
                """;
            Assert.That(Convert.ToInt32(await guard.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1),
                "Never run synthetic ACTIVATE proof against a non-disposable database.");
        }

        // The disposable harness is intentionally unseeded. Provision the
        // synthetic development model only after the LocalDB ownership guard.
        // This seed is test data, not an institutional model activation.
        var seedPath = Path.Combine(AppContext.BaseDirectory, "database", "Jornada_Seed_Dev.sql");
        Assert.That(File.Exists(seedPath), Is.True, "Disposable model seed must be available.");
        await SqlBatchRunner.ExecuteFileAsync(sql, seedPath);
        await using (var modelCheck = sql.CreateCommand())
        {
            modelCheck.CommandText = "SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO';";
            Assert.That(Convert.ToInt64(await modelCheck.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture),
                Is.GreaterThan(0), "The positive DT-15 fixture requires an active synthetic model.");
        }

        // Ensure an unused synthetic CPF-free source exists in the owned harness.
        // Only the disposable LocalDB is seeded; this is not operational Gold.
        await using (var countScale = sql.CreateCommand())
        {
            countScale.CommandText = "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';";
            if (Convert.ToInt64(await countScale.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0)
                await SqlBatchRunner.ExecuteFileWithSqlCmdVariablesAsync(
                    sql, Path.Combine(AppContext.BaseDirectory, "database", "Jornada_Dev_SyntheticScale.sql"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["SCALE_PEOPLE"] = "1000", ["SCALE_PAIRED"] = "2",
                        ["SCALE_PENDING"] = "1000", ["SCALE_SEED"] = "991",
                        ["SCALE_COLLISION_MODULO"] = "37",
                        ["SCALE_BIRTH_SHIFT_MODULO"] = "29"
                    });
        }

        // The outer transaction is rolled back even after successful issuer
        // execution. The SQL procedure's own COMMIT only closes its nested
        // transaction and cannot persist this synthetic approval.
        await using var cmd = sql.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRY
              BEGIN TRANSACTION;
              DECLARE @model UNIQUEIDENTIFIER, @version INT, @fingerprint BINARY(32),
                      @fit BINARY(32), @parent BINARY(32), @snapshot BINARY(32),
                      @bytes VARBINARY(MAX), @dossier BINARY(32), @digest BINARY(32),
                      @approval UNIQUEIDENTIFIER=NEWID(),
                      @policy NVARCHAR(120)=N'V9_DT10_LOWER_CI_POSITIVE',
                      @lower DECIMAL(18,8)=0.25000000,
                      @upper DECIMAL(18,8)=0.85000000,
                      @json NVARCHAR(MAX);
              SELECT TOP(1) @model=modelo_id,@version=versao
                FROM identidade.modelo_linkage WITH(UPDLOCK,HOLDLOCK)
                WHERE status=N'ATIVO' ORDER BY modelo_id;
              IF @model IS NULL THROW 51901, 'DT-10 positive fixture requires an active model.', 1;

              -- LocalDB only: temporarily align the synthetic model contract.
              -- Rollback restores the original algorithm and fingerprint.
              UPDATE identidade.modelo_linkage
                SET algoritmo_versao=N'FELLEGI_SUNTER_DECISION_EVIDENCE_COMPOSITE_NOMINAL_V9'
                WHERE modelo_id=@model;
              EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
                   @modelo_id=@model,@fingerprint=@fingerprint OUTPUT;
              IF @fingerprint IS NULL THROW 51902, 'DT-10 fixture fingerprint missing.', 1;

              SET @fit=HASHBYTES('SHA2_256',N'DT10_CI_SYNTHETIC_FIT');
              SET @parent=HASHBYTES('SHA2_256',N'DT10_CI_SYNTHETIC_PARENT');
              SET @json=CONCAT(N'{"SchemaVersion":"V9_FITTED_SNAPSHOT_V3",',
                N'"CandidateGrouping":{"CandidateGrouping":"GOLD"},',
                N'"FitFingerprintSha256":"',CONVERT(VARCHAR(64),@fit,2),N'",',
                N'"GoldParentSnapshotSha256":"',CONVERT(VARCHAR(64),@parent,2),N'",',
                N'"Threshold":0.85}');
              SET @bytes=CONVERT(VARBINARY(MAX),
                CONVERT(VARCHAR(MAX),@json COLLATE Latin1_General_100_CI_AS_SC_UTF8));
              SET @snapshot=HASHBYTES('SHA2_256',@bytes);
              SET @dossier=HASHBYTES('SHA2_256',N'DT10_CI_SYNTHETIC_DOSSIER');
              SET @digest=HASHBYTES('SHA2_256',CONCAT(
                N'JORNADA_DT10_V9_POLICY_V1|',@policy,N'|',
                CONVERT(NVARCHAR(36),@model),N'|',CONVERT(NVARCHAR(20),@version),N'|',
                CONVERT(VARCHAR(64),@fingerprint,2),N'|',
                CONVERT(VARCHAR(64),@fit,2),N'|',
                CONVERT(VARCHAR(64),@snapshot,2),N'|',
                CONVERT(NVARCHAR(40),@lower),N'|',CONVERT(NVARCHAR(40),@upper)));

              INSERT auditoria.modelo_linkage_dossie_decisao
                (modelo_id,modelo_snapshot_sha256,dossie_sha256,contrato_versao,
                 estado,origem_evidencia,valido_ate,registrado_por)
              VALUES(@model,@fingerprint,@dossier,N'DT10_CI_SYNTHETIC',
                     N'COMPLETO',N'REPRESENTATIVA_HML',
                     DATEADD(DAY,1,SYSDATETIMEOFFSET()),N'DT10_DISPOSABLE_CI_ONLY');

              INSERT auditoria.modelo_linkage_aprovacao
                (aprovacao_id,modelo_id,acao,modelo_snapshot_sha256,dossie_sha256,
                 decisor,motivo)
              VALUES(@approval,@model,N'ACTIVATE',@fingerprint,@dossier,
                     N'DT10_DISPOSABLE_CI_ONLY',
                     CONCAT(N'DT10_POLICY_SHA256=',CONVERT(NVARCHAR(64),@digest,2)));

              INSERT identidade.v9_dt10_lower_policy
                (politica_versao,modelo_id,modelo_versao,modelo_fingerprint,
                 fit_fingerprint,snapshot_sha256,snapshot_bytes,
                 limiar_inferior,limiar_superior,aprovacao_id)
              VALUES(@policy,@model,@version,@fingerprint,@fit,@snapshot,@bytes,
                     @lower,@upper,@approval);

              EXEC auditoria.sp_autorizar_v9_dt10_lower_policy
                   @politica_versao=@policy,@aprovacao_id=@approval;
              IF (SELECT COUNT_BIG(*) FROM auditoria.v9_dt10_lower_policy_autorizacao
                  WHERE aprovacao_id=@approval AND politica_versao=@policy
                    AND politica_sha256=@digest)<>1
                THROW 51903, 'Issuer did not seal the exact policy digest.', 1;
              IF (SELECT habilitada FROM identidade.v9_dt10_lower_policy
                  WHERE politica_versao=@policy)<>0
                THROW 51904, 'Issuer must never enable the lower policy.', 1;

              -- The issuer itself must never enable the policy. This one
              -- synthetic publication is enabled exclusively within the
              -- never-committed LocalDB test transaction below.
              UPDATE identidade.v9_dt10_lower_policy SET habilitada=1
                WHERE politica_versao=@policy;
              DECLARE @source BIGINT, @obs BIGINT,
                      @initial UNIQUEIDENTIFIER, @candidate UNIQUEIDENTIFIER,
                      @run UNIQUEIDENTIFIER=NEWID();
              SELECT TOP(1) @source=o.pessoa_origem_id,
                            @obs=po.pessoa_observacao_id
                FROM silver.pessoa_origem o
                JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
                LEFT JOIN identidade.pessoa_origem_progressiva p
                  ON p.pessoa_origem_id=o.pessoa_origem_id
                WHERE o.codigo_pessoa_origem LIKE N'SCALE-%'
                  AND po.cpf IS NULL
                  AND (p.estado=N'PROVISORIA' OR p.pessoa_origem_id IS NULL)
                ORDER BY po.pessoa_observacao_id DESC;
              IF @source IS NULL OR @obs IS NULL
                THROW 51905, 'No synthetic source available for governed LOWER.', 1;
              EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
              SELECT @initial=initial_uuid FROM identidade.pessoa_origem_progressiva
                WHERE pessoa_origem_id=@source;
              SELECT TOP(1) @candidate=pessoa_uuid FROM identidade.pessoa
                WHERE pessoa_uuid<>@initial ORDER BY pessoa_uuid;
              IF @initial IS NULL OR @candidate IS NULL
                THROW 51906, 'A distinct ranked candidate is required.', 1;

              INSERT identidade.linkage_run(
                linkage_run_id,modelo_id,modelo_versao,tipo_run,status,limite_solicitado,
                escopo_json,batch_size,max_parallelism,pessoa_observacao_id_high_watermark,
                registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
              VALUES(@run,@model,@version,N'FULL',N'EXECUTANDO',1,
                N'{"test":"dt10-v9-disposable-positive"}',1,1,@obs,1,1,0,1,0,0,
                N'CI',N'Owned synthetic LocalDB only',NEWID(),SYSUTCDATETIME());
              INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                VALUES(@run,@obs);
              INSERT identidade.linkage_resultado(
                linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
              VALUES(@run,@model,@version,@obs,NULL,@candidate,
                CONVERT(DECIMAL(9,6),0.100000),NULL,NULL,NULL,
                N'NAO_RESOLVIDO',N'LIMIAR_INFERIOR_V9_GOVERNADO',
                SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',
                N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO',@source,
                @policy,CONCAT(N'V9_SNAPSHOT_SHA256:',CONVERT(VARCHAR(64),@snapshot,2)),
                SYSUTCDATETIME());

              EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;
              IF (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento
                  WHERE linkage_run_id=@run)<>1
                THROW 51907, 'LOWER requires one ledger event.', 1;
              IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva
                  WHERE pessoa_origem_id=@source AND estado=N'REFERENCIA'
                    AND initial_uuid=@initial AND canonical_uuid=@initial)
                THROW 51908, 'LOWER must promote only initial_uuid.', 1;
              EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;
              IF (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento
                  WHERE linkage_run_id=@run)<>1
                THROW 51909, 'LOWER replay must be idempotent.', 1;
              IF (SELECT COUNT_BIG(*) FROM auditoria.v9_dt10_lower_policy_autorizacao
                  WHERE aprovacao_id=@approval AND politica_sha256=@digest)<>1
                THROW 51910, 'Published policy must match sealed DT-15 digest.', 1;

              ROLLBACK TRANSACTION;
            END TRY
            BEGIN CATCH
              IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
              THROW;
            END CATCH;
            """;
        await cmd.ExecuteNonQueryAsync();

        await using var noLeak = sql.CreateCommand();
        noLeak.CommandText = """
            SELECT COUNT_BIG(*) FROM identidade.v9_dt10_lower_policy
             WHERE politica_versao=N'V9_DT10_LOWER_CI_POSITIVE';
            """;
        Assert.That(Convert.ToInt64(await noLeak.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.Zero,
            "Synthetic DT-15 evidence must not survive the test transaction.");
    }
}
