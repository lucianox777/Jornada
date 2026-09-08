using Jornada.Api;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class CpfAnchorResolutionApiTests
{
    private const string Cpf = "11144477735";

    [Test]
    public async Task Resolver_uses_permanent_anchor_when_current_map_is_closed_or_in_conflict()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260907_Cpf_Ancora.sql"));

        Guid anchorUuid;
        long mapId;
        string originalState;
        string? originalReason;
        DateTimeOffset? originalStateAt;
        DateTimeOffset? originalEnd;
        string originalMethod;

        await using (var fixture = connection.CreateCommand())
        {
            fixture.CommandText = """
                SELECT a.pessoa_uuid,m.identity_map_id,m.estado,m.estado_motivo,m.estado_em,m.vigencia_fim,m.metodo_resolucao
                FROM identidade.cpf_ancora a
                JOIN identidade.identity_map m ON m.tipo='CPF' AND m.identificador COLLATE Latin1_General_100_BIN2=a.cpf AND m.vigencia_fim IS NULL
                WHERE a.cpf=@cpf;
                """;
            fixture.Parameters.AddWithValue("@cpf", Cpf);
            await using var reader = await fixture.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Fixture DEV deve conter CPF com âncora e mapa corrente.");
            anchorUuid = reader.GetGuid(0);
            mapId = reader.GetInt64(1);
            originalState = reader.GetString(2);
            originalReason = reader.IsDBNull(3) ? null : reader.GetString(3);
            originalStateAt = reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4);
            originalEnd = reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5);
            originalMethod = reader.GetString(6);
        }

        var service = new SqlIdentityResolutionService(new OperationalSqlAdapter(connectionString));
        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);
        var request = new IdentityResolutionRequest(Cpf);

        try
        {
            var active = await service.ResolveAsync(context, request, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(active.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
                Assert.That(active.PessoaUuid, Is.EqualTo(anchorUuid));
                Assert.That(active.MetodoResolucao, Is.EqualTo(Enum.Parse<ResolutionMethod>(originalMethod)));
                Assert.That(active.Motivo, Is.Null);
            });

            await using (var close = connection.CreateCommand())
            {
                close.CommandText = """
                    UPDATE identidade.identity_map
                    SET vigencia_fim=SYSDATETIMEOFFSET(),estado='ENCERRADO',estado_motivo='TESTE_API_ANCORA_SEM_MAPA',estado_em=SYSDATETIMEOFFSET()
                    WHERE identity_map_id=@id;
                    """;
                close.Parameters.AddWithValue("@id", mapId);
                Assert.That(await close.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }

            var withoutCurrentMap = await service.ResolveAsync(context, request, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(withoutCurrentMap.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
                Assert.That(withoutCurrentMap.PessoaUuid, Is.EqualTo(anchorUuid), "Encerrar projeção identity_map não apaga a referência CPF→UUID.");
                Assert.That(withoutCurrentMap.MetodoResolucao, Is.EqualTo(ResolutionMethod.CPF_DETERMINISTICO));
                Assert.That(withoutCurrentMap.Motivo, Is.EqualTo("CPF_ANCORA_SEM_MAPA_CORRENTE"));
            });

            await RestoreMapAsync(connection, mapId, originalState, originalReason, originalStateAt, originalEnd);

            await using (var conflict = connection.CreateCommand())
            {
                conflict.CommandText = """
                    UPDATE identidade.identity_map
                    SET estado='EM_CONFLITO',estado_motivo=@motivo,estado_em=SYSDATETIMEOFFSET(),vigencia_fim=NULL
                    WHERE identity_map_id=@id;
                    """;
                conflict.Parameters.AddWithValue("@id", mapId);
                conflict.Parameters.AddWithValue("@motivo", CpfIdentityConsistency.IdentifierInConflictReason);
                Assert.That(await conflict.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }

            var conflicted = await service.ResolveAsync(context, request, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(conflicted.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
                Assert.That(conflicted.PessoaUuid, Is.EqualTo(anchorUuid), "O conflito suspende atribuição factual, não a âncora permanente.");
                Assert.That(conflicted.MetodoResolucao, Is.EqualTo(ResolutionMethod.CPF_DETERMINISTICO));
                Assert.That(conflicted.Motivo, Is.EqualTo(CpfIdentityConsistency.IdentifierInConflictReason));
            });
        }
        finally
        {
            await RestoreMapAsync(connection, mapId, originalState, originalReason, originalStateAt, originalEnd);
        }
    }

    private static async Task RestoreMapAsync(
        SqlConnection connection,
        long mapId,
        string state,
        string? reason,
        DateTimeOffset? stateAt,
        DateTimeOffset? end)
    {
        await using var restore = connection.CreateCommand();
        restore.CommandText = """
            UPDATE identidade.identity_map
            SET estado=@estado,estado_motivo=@motivo,estado_em=@estado_em,vigencia_fim=@fim
            WHERE identity_map_id=@id;
            """;
        restore.Parameters.AddWithValue("@estado", state);
        restore.Parameters.AddWithValue("@motivo", (object?)reason ?? DBNull.Value);
        restore.Parameters.AddWithValue("@estado_em", (object?)stateAt ?? DBNull.Value);
        restore.Parameters.AddWithValue("@fim", (object?)end ?? DBNull.Value);
        restore.Parameters.AddWithValue("@id", mapId);
        Assert.That(await restore.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
