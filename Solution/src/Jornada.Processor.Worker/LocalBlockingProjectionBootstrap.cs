using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

/// <summary>
/// Reconstrói somente a projeção física ausente da massa SCALE usada pelo ambiente Docker local.
/// A massa sintética é inserida diretamente em Gold para acelerar o bootstrap e, por isso, não
/// percorre o Processor normal que materializa identidade.blocking_chave. Esta operação reutiliza
/// o projetor canônico C# e é deliberadamente restrita ao perfil Development/Test.
/// </summary>
internal static class LocalBlockingProjectionBootstrap
{
    internal sealed record Result(int SyntheticPersons, int RebuiltPersons, int ProjectedKeys);

    internal static async Task<Result> RebuildMissingSqlServerAsync(
        string connectionString,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var syntheticPersons = await CountSyntheticPersonsAsync(connection, ct);
        if (syntheticPersons == 0)
            return new Result(0, 0, 0);

        var missing = await ReadMissingPersonsAsync(connection, ct);
        if (missing.Count == 0)
            return new Result(syntheticPersons, 0, 0);

        var table = CreateProjectionTable();
        foreach (var person in missing)
        {
            foreach (var key in BlockingProjectionKeyProjector
                         .Project(person.Name, person.MotherName, person.BirthDate)
                         .Distinct())
            {
                var semantics = BlockingFeatureTemporalCatalog.Get(key.Feature)
                    == BlockingFeatureTemporalSemantics.StableIdentityDatum
                    ? "STABLE_IDENTITY_DATUM"
                    : "VERSIONED_ALIAS";

                table.Rows.Add(
                    person.PersonUuid,
                    IdentityComparison.NormalizationVersion,
                    key.Feature,
                    key.Value,
                    semantics,
                    person.UpdatedAt,
                    DBNull.Value);
            }
        }

        if (table.Rows.Count == 0)
            throw new InvalidOperationException(
                "A projeção canônica não produziu chaves para a massa sintética local.");

        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                         IsolationLevel.ReadCommitted, ct))
        {
            try
            {
                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
                {
                    DestinationTableName = "identidade.blocking_chave",
                    BatchSize = Math.Min(10_000, table.Rows.Count),
                    BulkCopyTimeout = 900
                };
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                await bulk.WriteToServerAsync(table, ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        var remaining = await CountMissingPersonsAsync(connection, ct);
        if (remaining != 0)
            throw new InvalidOperationException(
                $"Reconstrução local de blocking incompleta: ainda existem {remaining} Pessoas SCALE sem projeção corrente.");

        return new Result(syntheticPersons, missing.Count, table.Rows.Count);
    }

    private static async Task<int> CountSyntheticPersonsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT vc.pessoa_uuid)
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vc
              ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.codigo_pessoa_origem LIKE N'SCALE-%'
              AND vc.status='RESOLVIDO'
              AND vc.pessoa_uuid IS NOT NULL;
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountMissingPersonsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = CreateMissingCommand(connection, countOnly: true);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<List<SyntheticPerson>> ReadMissingPersonsAsync(
        SqlConnection connection,
        CancellationToken ct)
    {
        await using var command = CreateMissingCommand(connection, countOnly: false);
        var result = new List<SyntheticPerson>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new SyntheticPerson(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTimeOffset(4)));
        }
        return result;
    }

    private static SqlCommand CreateMissingCommand(SqlConnection connection, bool countOnly)
    {
        var select = countOnly
            ? "SELECT COUNT(*) FROM synthetic_person"
            : "SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae,atualizado_em FROM synthetic_person ORDER BY pessoa_uuid";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH synthetic_person AS (
                SELECT DISTINCT
                       g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae,g.atualizado_em
                FROM gold.pessoa g
                WHERE EXISTS (
                    SELECT 1
                    FROM silver.pessoa_observacao po
                    JOIN identidade.v_vinculo_corrente vc
                      ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                    WHERE vc.pessoa_uuid=g.pessoa_uuid
                      AND vc.status='RESOLVIDO'
                      AND po.codigo_pessoa_origem LIKE N'SCALE-%'
                )
                  AND NOT EXISTS (
                    SELECT 1
                    FROM identidade.blocking_chave bc
                    WHERE bc.pessoa_uuid=g.pessoa_uuid
                      AND bc.normalizacao_versao=@normalizacao
                      AND bc.projection_schema_version=@projection_schema
                      AND bc.projection_fingerprint_sha256=@projection_fingerprint
                )
            )
            {select};
            """;
        command.Parameters.Add(new SqlParameter("@normalizacao", SqlDbType.NVarChar, 80)
            { Value = IdentityComparison.NormalizationVersion });
        command.Parameters.Add(new SqlParameter("@projection_schema", SqlDbType.NVarChar, 80)
            { Value = PersonResolutionProjectionContract.SchemaVersion });
        command.Parameters.Add(new SqlParameter("@projection_fingerprint", SqlDbType.Char, 64)
            { Value = PersonResolutionProjectionContract.FingerprintSha256 });
        command.CommandTimeout = 900;
        return command;
    }

    private static DataTable CreateProjectionTable()
    {
        var table = new DataTable();
        table.Columns.Add("pessoa_uuid", typeof(Guid));
        table.Columns.Add("normalizacao_versao", typeof(string));
        table.Columns.Add("atributo", typeof(string));
        table.Columns.Add("valor_normalizado", typeof(string));
        table.Columns.Add("semantica_temporal", typeof(string));
        table.Columns.Add("vigencia_inicio", typeof(DateTimeOffset));
        table.Columns.Add("vigencia_fim", typeof(DateTimeOffset));
        return table;
    }

    private sealed record SyntheticPerson(
        Guid PersonUuid,
        string Name,
        DateOnly BirthDate,
        string? MotherName,
        DateTimeOffset UpdatedAt);
}
