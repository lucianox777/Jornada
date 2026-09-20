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
