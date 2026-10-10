using System.Data;
using Microsoft.Data.SqlClient;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Integration;

/// <summary>
/// DT-05: the semantic ledger must reject calls outside the publication
/// transaction and runs that have not entered EXECUTANDO.
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class Dt05PublicationGuardsSqlServerTests
{
    [Test]
    public async Task Ledger_requires_transaction_and_executing_run_without_side_effects()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            Assert.Ignore("JORNADA_TEST_SQL_CONNECTION required.");

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        var database = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, database);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "Jornada_Seed_Dev.sql"));

        // No transaction must fail before even checking whether the run exists.
        await using (var noTransaction = connection.CreateCommand())
        {
            noTransaction.CommandText = "EXEC identidade.sp_registrar_transicoes_linkage_run @run;";
            noTransaction.Parameters.AddWithValue("@run", Guid.NewGuid());
            var exception = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () => await noTransaction.ExecuteNonQueryAsync()));
            Assert.That(exception!.Number, Is.EqualTo(51940));
        }

        // An existing PREPARANDO run must also be rejected; an unknown run alone
        // would not detect a regression that accepts every persisted run.
        await using (var preparingTx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                await using var setup = connection.CreateCommand();
                setup.Transaction = preparingTx;
                setup.CommandText = """
                    DECLARE @model UNIQUEIDENTIFIER, @version INT;
                    SELECT TOP(1) @model=modelo_id,@version=versao
                    FROM identidade.modelo_linkage ORDER BY versao DESC;
                    INSERT identidade.linkage_run(
                      linkage_run_id,modelo_id,modelo_versao,tipo_run,status,
                      limite_solicitado,escopo_json,batch_size,max_parallelism,
                      registros_elegiveis,avaliados,resolvidos,nao_resolvidos,
                      conflitos,sem_candidato_no_bloco,solicitado_por,motivo,
                      correlation_id,iniciado_em)
                    VALUES(@run,@model,@version,N'ON_DEMAND',N'PREPARANDO',
                      1,N'{"test":"dt05-preparing-guard"}',1,1,
                      0,0,0,0,0,0,N'CI',N'DT05 PREPARANDO guard',NEWID(),SYSUTCDATETIME());
                    """;
                var runId = Guid.NewGuid();
                setup.Parameters.AddWithValue("@run", runId);
                await setup.ExecuteNonQueryAsync();

                await using var rejected = connection.CreateCommand();
                rejected.Transaction = preparingTx;
                rejected.CommandText = "EXEC identidade.sp_registrar_transicoes_linkage_run @run;";
                rejected.Parameters.AddWithValue("@run", runId);
                var exception = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () => await rejected.ExecuteNonQueryAsync()));
                Assert.That(exception!.Number, Is.EqualTo(51941));
            }
            finally
            {
                if (preparingTx.Connection is not null)
                    await preparingTx.RollbackAsync();
            }
        }

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // An unknown run is not in EXECUTANDO and must fail closed.
            await using var invalidRun = connection.CreateCommand();
            invalidRun.Transaction = tx;
            invalidRun.CommandText = "EXEC identidade.sp_registrar_transicoes_linkage_run @run;";
            invalidRun.Parameters.AddWithValue("@run", Guid.NewGuid());
            var exception = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () => await invalidRun.ExecuteNonQueryAsync()));
            Assert.That(exception!.Number, Is.EqualTo(51941));
        }
        finally
        {
            // XACT_ABORT can invalidate the transaction after THROW.
            if (tx.Connection is not null)
                await tx.RollbackAsync();
        }
    }
}
