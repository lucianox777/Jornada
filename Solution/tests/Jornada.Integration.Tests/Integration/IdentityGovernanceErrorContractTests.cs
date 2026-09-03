using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class IdentityGovernanceErrorContractTests
{
    [Test] public Task Invalid_groups_json_returns_51110() =>
        ExpectErrorAsync(51110, async (c, tx) =>
        {
            await CallApplyAsync(c, tx, "SMADS", Guid.NewGuid(), "{not-json", Guid.NewGuid());
        });

    [Test] public Task Inactive_or_unknown_manager_returns_51111() =>
        ExpectErrorAsync(51111, async (c, tx) =>
        {
            await CallApplyAsync(c, tx, "GESTOR_INEXISTENTE", Guid.NewGuid(), "[]", Guid.NewGuid());
        });

    [Test] public Task Missing_or_closed_case_returns_51112() =>
        ExpectErrorAsync(51112, async (c, tx) =>
        {
            await CallApplyAsync(c, tx, "SMADS", Guid.NewGuid(), "[]", Guid.NewGuid());
        });

    [Test] public Task Empty_group_set_returns_51113() =>
        ExpectErrorAsync(51113, async (c, tx) =>
        {
            var setup = await OpenCaseAsync(c, tx, "LINKAGE_INCORRETO");
            await CallApplyAsync(c, tx, "SMADS", setup.CaseId, "[]", Guid.NewGuid());
        });

    [Test] public Task Observation_in_more_than_one_group_returns_51114_not_pk_error() =>
        ExpectErrorAsync(51114, async (c, tx) =>
        {
            var setup = await OpenCaseAsync(c, tx, "LINKAGE_INCORRETO");
            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo="A", pessoaUuidDestino=setup.Uuids[0], pessoaObservacaoIds=new[] { setup.Observations[0] } },
                new { grupoCodigo="B", pessoaUuidDestino=setup.Uuids[1], pessoaObservacaoIds=new[] { setup.Observations[0], setup.Observations[1] } }
            });
            await CallApplyAsync(c, tx, "SMADS", setup.CaseId, groups, Guid.NewGuid());
        });

    [Test] public Task Not_all_case_observations_classified_returns_51115() =>
        ExpectErrorAsync(51115, async (c, tx) =>
        {
            var setup = await OpenCaseAsync(c, tx, "LINKAGE_INCORRETO");
            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo="A", pessoaUuidDestino=setup.Uuids[0], pessoaObservacaoIds=new[] { setup.Observations[0] } }
            });
            await CallApplyAsync(c, tx, "SMADS", setup.CaseId, groups, Guid.NewGuid());
        });

    [Test] public Task Group_without_observations_returns_51116() =>
        ExpectErrorAsync(51116, async (c, tx) =>
        {
            var setup = await OpenCaseAsync(c, tx, "LINKAGE_INCORRETO");
            var groups = JsonSerializer.Serialize(new object[]
            {
                new { grupoCodigo="A", pessoaUuidDestino=setup.Uuids[0], pessoaObservacaoIds=setup.Observations },
                new { grupoCodigo="B", pessoaUuidDestino=setup.Uuids[1], pessoaObservacaoIds=Array.Empty<long>() }
            });
            await CallApplyAsync(c, tx, "SMADS", setup.CaseId, groups, Guid.NewGuid());
        });

    [Test]
    public Task Historical_merge_rejects_invalid_or_cyclic_destination_chain_with_51117() =>
        ExpectErrorAsync(51117, async (c, tx) =>
        {
            var source = await PickSourceWithAllResolvedObservationsAsync(c, tx);
            var caseId = await OpenCaseForObservationsAsync(c, tx, "FUSAO_HISTORICA", source.Observations);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            await using (var cycle = c.CreateCommand())
            {
                cycle.Transaction = tx;
                cycle.CommandText = """
                    INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@a,'FUNDIDO'),(@b,'FUNDIDO');
                    UPDATE identidade.pessoa SET pessoa_uuid_sucessor=@b WHERE pessoa_uuid=@a;
                    UPDATE identidade.pessoa SET pessoa_uuid_sucessor=@a WHERE pessoa_uuid=@b;
                    """;
                cycle.Parameters.AddWithValue("@a", a);
                cycle.Parameters.AddWithValue("@b", b);
                await cycle.ExecuteNonQueryAsync();
            }
            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo="CANONICO", pessoaUuidDestino=a, pessoaObservacaoIds=source.Observations }
            });
            await CallApplyAsync(c, tx, "SMADS", caseId, groups, Guid.NewGuid());
        });

    [Test]
    public Task Historical_merge_rejects_divergent_active_cpfs_with_51118() =>
        ExpectErrorAsync(51118, async (c, tx) =>
        {
            Guid sourceUuid = Guid.Empty, destinationUuid = Guid.Empty;
            await using (var pick = c.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = """
                    SELECT TOP(1) src.pessoa_uuid,dst.pessoa_uuid
                    FROM identidade.identity_map src
                    JOIN identidade.identity_map dst ON dst.tipo='CPF' AND dst.vigencia_fim IS NULL
                         AND dst.estado IN('ATIVO','EM_CONFLITO') AND dst.identificador<>src.identificador
                    WHERE src.tipo='CPF' AND src.vigencia_fim IS NULL AND src.estado IN('ATIVO','EM_CONFLITO')
                      AND src.pessoa_uuid<>dst.pessoa_uuid
                      AND EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente v WHERE v.pessoa_uuid=src.pessoa_uuid AND v.status='RESOLVIDO')
                    ORDER BY src.identity_map_id,dst.identity_map_id;
                    """;
                await using var reader = await pick.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True, "Fixture DEV deve conter dois UUIDs com CPFs ativos divergentes para provar 51118.");
                sourceUuid = reader.GetGuid(0);
                destinationUuid = reader.GetGuid(1);
            }

            var observations = await LoadAllResolvedObservationsAsync(c, tx, sourceUuid);
            Assert.That(observations, Is.Not.Empty, "Fixture DEV deve possuir observações RESOLVIDAS para o UUID de origem do 51118.");
            var caseId = await OpenCaseForObservationsAsync(c, tx, "FUSAO_HISTORICA", observations);
            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo="CANONICO", pessoaUuidDestino=destinationUuid, pessoaObservacaoIds=observations }
            });
            await CallApplyAsync(c, tx, "SMADS", caseId, groups, Guid.NewGuid());
        });

    private static async Task ExpectErrorAsync(int expected, Func<SqlConnection, SqlTransaction, Task> action)
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ex = Assert.ThrowsAsync<SqlException>(async () => await action(connection, tx));
            Assert.That(ex!.Number, Is.EqualTo(expected));
        }
        finally
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
        }
    }

    private sealed record CaseSetup(Guid CaseId, long[] Observations, Guid[] Uuids);

    private static async Task<CaseSetup> OpenCaseAsync(SqlConnection connection, SqlTransaction tx, string reason)
    {
        var observations = new List<long>();
        var uuids = new List<Guid>();
        await using (var pick = connection.CreateCommand())
        {
            pick.Transaction = tx;
            pick.CommandText = """
                SELECT TOP(2) vc.pessoa_observacao_id,vc.pessoa_uuid
                FROM identidade.v_vinculo_corrente vc
                WHERE vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL
                ORDER BY vc.pessoa_observacao_id;
                """;
            await using var reader = await pick.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                observations.Add(reader.GetInt64(0));
                uuids.Add(reader.GetGuid(1));
            }
        }
        Assert.That(observations.Count, Is.GreaterThanOrEqualTo(2),
            "Fixture DEV deve conter ao menos duas observações RESOLVIDAS; ausência é regressão da fixture, não motivo para ignorar teste.");
        var caseId = await OpenCaseForObservationsAsync(connection, tx, reason, observations.Take(2).ToArray());
        return new CaseSetup(caseId, observations.Take(2).ToArray(), uuids.Take(2).ToArray());
    }

    private sealed record SourceSetup(Guid Uuid, long[] Observations);

    private static async Task<SourceSetup> PickSourceWithAllResolvedObservationsAsync(SqlConnection connection, SqlTransaction tx)
    {
        Guid uuid;
        await using (var pick = connection.CreateCommand())
        {
            pick.Transaction = tx;
            pick.CommandText = """
                SELECT TOP(1) pessoa_uuid
                FROM identidade.v_vinculo_corrente
                WHERE status='RESOLVIDO' AND pessoa_uuid IS NOT NULL
                GROUP BY pessoa_uuid
                ORDER BY COUNT(*) DESC,pessoa_uuid;
                """;
            var value = await pick.ExecuteScalarAsync();
            Assert.That(value, Is.Not.Null, "Fixture DEV deve conter UUID com observações RESOLVIDAS.");
            uuid = (Guid)value!;
        }
        var observations = await LoadAllResolvedObservationsAsync(connection, tx, uuid);
        Assert.That(observations, Is.Not.Empty);
        return new SourceSetup(uuid, observations);
    }

    private static async Task<long[]> LoadAllResolvedObservationsAsync(SqlConnection connection, SqlTransaction tx, Guid uuid)
    {
        var observations = new List<long>();
        await using var pick = connection.CreateCommand();
        pick.Transaction = tx;
        pick.CommandText = """
            SELECT pessoa_observacao_id
            FROM identidade.v_vinculo_corrente
            WHERE status='RESOLVIDO' AND pessoa_uuid=@uuid
            ORDER BY pessoa_observacao_id;
            """;
        pick.Parameters.AddWithValue("@uuid", uuid);
        await using var reader = await pick.ExecuteReaderAsync();
        while (await reader.ReadAsync()) observations.Add(reader.GetInt64(0));
        return observations.ToArray();
    }

    private static async Task<Guid> OpenCaseForObservationsAsync(
        SqlConnection connection, SqlTransaction tx, string reason, IReadOnlyCollection<long> observations)
    {
        await using var open = connection.CreateCommand();
        open.Transaction = tx;
        open.CommandType = CommandType.StoredProcedure;
        open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
        open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
        open.Parameters.AddWithValue("@motivo", reason);
        open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(observations));
        open.Parameters.AddWithValue("@ato_referencia", "TESTE-CONTRATO-511XX-V368");
        open.Parameters.AddWithValue("@justificativa", "Prova executável dos códigos de erro governados 51110-51118.");
        open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
        var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier);
        output.Direction = ParameterDirection.Output;
        await open.ExecuteNonQueryAsync();
        return (Guid)output.Value;
    }

    private static async Task CallApplyAsync(
        SqlConnection connection, SqlTransaction tx, string managerCode, Guid caseId, string groupsJson, Guid correlation)
    {
        await using var apply = connection.CreateCommand();
        apply.Transaction = tx;
        apply.CommandType = CommandType.StoredProcedure;
        apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
        apply.Parameters.AddWithValue("@gestor_codigo", managerCode);
        apply.Parameters.AddWithValue("@caso_id", caseId);
        apply.Parameters.AddWithValue("@grupos_json", groupsJson);
        apply.Parameters.AddWithValue("@correlation_id", correlation);
        await apply.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
