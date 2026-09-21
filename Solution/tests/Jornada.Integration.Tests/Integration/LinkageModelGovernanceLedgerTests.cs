using Jornada.Api;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class LinkageModelGovernanceLedgerTests
{
    [Test]
    public async Task Model_state_transitions_are_append_only_and_visible_in_monitor_governance()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var modelId = Guid.NewGuid();
        var version = 900000 + Random.Shared.Next(1, 90000);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,gerado_em,amostra_metodo)
                VALUES(
                    @id,@versao,'RASCUNHO','TEST_GOVERNANCE_V1','TEST_NORMALIZATION_V1',
                    'TEST_ONLY','gold.pessoa',SYSDATETIMEOFFSET(),'TEST_ONLY');
                """;
            insert.Parameters.AddWithValue("@id", modelId);
            insert.Parameters.AddWithValue("@versao", version);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var blockingSupport = connection.CreateCommand())
        {
            blockingSupport.CommandText = """
                INSERT identidade.linkage_ruleset(
                    ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256)
                VALUES(
                    @id,@id,N'TEST_RULESET_V1',N'TEST_GOVERNANCE_V1',
                    REPLICATE('a',64));

                INSERT identidade.linkage_ruleset_passe(
                    ruleset_id,passe_ordem,passe_id)
                VALUES(@id,0,N'P001');

                INSERT identidade.parametro_linkage(modelo_id,nome,valor)
                VALUES
                    (@id,N'NOMINAL_U_MIN_CONDITIONED_PAIRS_PER_PASS',1000),
                    (@id,N'BLOCKING_PASS_U_01_SAMPLE_SIZE',1500),
                    (@id,N'BLOCKING_PASS_U_01_NOME_MAE_EXACT',400),
                    (@id,N'BLOCKING_PASS_U_01_NOME_MAE_HIGH',300),
                    (@id,N'BLOCKING_PASS_U_01_NOME_MAE_MEDIUM',200),
                    (@id,N'BLOCKING_PASS_U_01_NOME_MAE_LOW',150);
                """;
            blockingSupport.Parameters.AddWithValue("@id", modelId);
            await blockingSupport.ExecuteNonQueryAsync();
        }

        await using (var conference = connection.CreateCommand())
        {
            conference.CommandText = """
                EXEC sys.sp_set_session_context
                    @key=N'Jornada.SourceRevision',
                    @value=N'TEST_MONITOR_CONFERENCE_V1';

                DECLARE @e UNIQUEIDENTIFIER;
                DECLARE @request_sha256 BINARY(32)=HASHBYTES(
                    'SHA2_256',
                    CONCAT(N'monitor-request-',CONVERT(NVARCHAR(36),@id)));
                DECLARE @report_sha256 BINARY(32)=HASHBYTES(
                    'SHA2_256',
                    CONCAT(N'monitor-report-',CONVERT(NVARCHAR(36),@id)));
                EXEC auditoria.sp_registrar_conferencia_linkage
                    @modelo_id=@id,
                    @modelo_versao=@versao,
                    @metodo_versao=N'JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1',
                    @escopo=N'SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE',
                    @tolerancia_versao=N'TEST_ONLY_FROZEN_MONITOR_V1',
                    @max_llr_par_permitido=0.000001,
                    @status=N'CONFORME',
                    @candidatos_avaliados=208,
                    @max_llr_par_observado=0.0000001,
                    @max_log_odds_observado=0.0000001,
                    @mesma_decisao_final=1,
                    @mesmo_top1=1,
                    @spearman=1,
                    @motivo=NULL,
                    @validacao_estatistica=N'NOT_ASSESSED_ISSUE_31',
                    @request_sha256=@request_sha256,
                    @report_sha256=@report_sha256,
                    @evidencia_id=@e OUTPUT;
                """;
            conference.Parameters.AddWithValue("@id", modelId);
            conference.Parameters.AddWithValue("@versao", version);
            await conference.ExecuteNonQueryAsync();
        }

        await using (var validate = connection.CreateCommand())
        {
            validate.CommandText = "UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@id;";
            validate.Parameters.AddWithValue("@id", modelId);
            await validate.ExecuteNonQueryAsync();
        }

        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await using var activate = connection.CreateCommand();
            activate.Transaction = tx;
            activate.CommandText = """
                UPDATE identidade.modelo_linkage SET status='INATIVO' WHERE status='ATIVO' AND modelo_id<>@id;
                UPDATE identidade.modelo_linkage SET status='ATIVO',ativado_em=SYSDATETIMEOFFSET() WHERE modelo_id=@id;
                """;
            activate.Parameters.AddWithValue("@id", modelId);
            await activate.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var events = connection.CreateCommand())
        {
            events.CommandText = """
                SELECT status_anterior,status_novo,operacao_codigo
                FROM auditoria.modelo_linkage_estado_evento
                WHERE modelo_id=@id
                ORDER BY modelo_linkage_estado_evento_id;
                """;
            events.Parameters.AddWithValue("@id", modelId);
            await using var reader = await events.ExecuteReaderAsync();
            var rows = new List<(string? Previous, string Current, string Operation)>();
            while (await reader.ReadAsync())
                rows.Add((
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));

            Assert.That(rows, Is.EqualTo(new[]
            {
                ((string?)null, "RASCUNHO", "INSERT_EXISTING_STATE"),
                ("RASCUNHO", "VALIDADO", "VALIDATE"),
                ("VALIDADO", "ATIVO", "ACTIVATE")
            }));
        }

        await using (var immutable = connection.CreateCommand())
        {
            immutable.CommandText = """
                UPDATE auditoria.modelo_linkage_estado_evento
                SET motivo=N'alteração proibida'
                WHERE modelo_id=@id;
                """;
            immutable.Parameters.AddWithValue("@id", modelId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await immutable.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51961));
        }

        var monitor = await new OperationalMonitorService(new OperationalSqlAdapter(connectionString))
            .GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(monitor.LinkageModelGovernance.Status, Is.EqualTo("OK"));
            Assert.That(monitor.LinkageModelGovernance.ActiveModelCount, Is.EqualTo(1));
            Assert.That(monitor.LinkageModelGovernance.ModelId, Is.EqualTo(modelId));
            Assert.That(monitor.LinkageModelGovernance.ModelVersion, Is.EqualTo(version));
            Assert.That(monitor.LinkageModelGovernance.AlgorithmVersion, Is.EqualTo("TEST_GOVERNANCE_V1"));
            Assert.That(monitor.LinkageModelGovernance.StatisticalValidation, Is.EqualTo("PENDENTE_ISSUE_31"));
            Assert.That(monitor.LinkageConferenceGovernance.Status, Is.EqualTo("CONFORME"));
            Assert.That(monitor.LinkageConferenceGovernance.ModelVersion, Is.EqualTo(version));
            Assert.That(monitor.LinkageConferenceGovernance.MethodVersion,
                Is.EqualTo("JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1"));
            Assert.That(monitor.LinkageConferenceGovernance.ToleranceVersion,
                Is.EqualTo("TEST_ONLY_FROZEN_MONITOR_V1"));
            Assert.That(monitor.LinkageConferenceGovernance.CandidatesEvaluated, Is.EqualTo(208));
            Assert.That(monitor.LinkageConferenceGovernance.SameFinalDecision, Is.True);
            Assert.That(monitor.LinkageConferenceGovernance.SameTop1, Is.True);
            Assert.That(monitor.LinkageConferenceGovernance.SnapshotCurrent, Is.True);
            Assert.That(monitor.LinkageConferenceGovernance.StatisticalValidation,
                Is.EqualTo("PENDENTE_ISSUE_31"));
            Assert.That(monitor.LinkageConferenceGovernance.RoundTripMethod,
                Is.EqualTo("JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1"));
            Assert.That(monitor.LinkageConferenceGovernance.RoundTripStatus,
                Is.EqualTo("OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO"));
            Assert.That(monitor.LinkageBlockingPassSupport, Has.Count.EqualTo(1));
            Assert.That(monitor.LinkageBlockingPassSupport[0].PassOrder, Is.EqualTo(1));
            Assert.That(monitor.LinkageBlockingPassSupport[0].PassId, Is.EqualTo("P001"));
            Assert.That(monitor.LinkageBlockingPassSupport[0].SampleSize, Is.EqualTo(1500));
            Assert.That(monitor.LinkageBlockingPassSupport[0].MotherNamePresentSupport, Is.EqualTo(1050));
            Assert.That(monitor.LinkageBlockingPassSupport[0].MinimumRequiredPerPass, Is.EqualTo(1000));
            Assert.That(monitor.LinkageBlockingPassSupport[0].NameSufficient, Is.True);
            Assert.That(monitor.LinkageBlockingPassSupport[0].MotherNameSufficient, Is.True);
            Assert.That(monitor.LinkageModelTransitions.Any(x =>
                x.ModelId == modelId && x.Operation == "ACTIVATE" && x.NewStatus == "ATIVO"), Is.True);
        });
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
}
