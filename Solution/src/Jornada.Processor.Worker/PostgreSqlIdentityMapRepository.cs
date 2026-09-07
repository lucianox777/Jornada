using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Processor.Worker;

internal sealed class PostgreSqlIdentityMapRepository(IOperationalDatabaseAdapter database) : IIdentityMapRepository
{
    private readonly IOperationalDatabaseAdapter database = Validate(database);

    public async Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
        string cpf, IdentityObservation observation, CancellationToken ct)
    {
        var normalized = CpfRules.NormalizeAndValidate(cpf)
            ?? throw new ArgumentException("CPF inválido.", nameof(cpf));
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await PostgreSqlIdentityPersistence.ResolveOrCreateByCpfAsync(
                connection, tx, normalized,
                new IdentityCore(observation.NomeCompleto, observation.DataNascimento, observation.NomeMae),
                gestorId: null, sourceRecordId: null, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await PostgreSqlPersistenceSupport.RollbackPreservingOriginalAsync(tx);
            throw;
        }
    }

    private static IOperationalDatabaseAdapter Validate(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!string.Equals(database.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("PostgreSqlIdentityMapRepository exige provider PostgreSql.", nameof(database));
        return database;
    }
}

internal static class PostgreSqlIdentityPersistence
{
    internal static async Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
        DbConnection connection,
        DbTransaction tx,
        string cpf,
        IdentityCore incomingCore,
        long? gestorId,
        string? sourceRecordId,
        CancellationToken ct)
    {
        Guid? existingUuid = null;
        long? existingMapId = null;
        string? existingState = null;
        await using (var find = Command(connection, tx, """
            SELECT identity_map_id,pessoa_uuid,estado
              FROM identidade.identity_map
             WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL
             ORDER BY vigencia_inicio DESC,identity_map_id DESC
             LIMIT 1
             FOR UPDATE;
            """))
        {
            Add(find, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                existingMapId = reader.GetInt64(0);
                existingUuid = reader.GetFieldValue<Guid>(1);
                existingState = reader.GetString(2);
            }
        }

        if (existingUuid.HasValue)
        {
            if (string.Equals(existingState, "EM_CONFLITO", StringComparison.Ordinal))
            {
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: CpfIdentityConsistency.IdentifierInConflictReason);
            }

            var existingCore = await LoadExistingCoreAsync(connection, tx, existingUuid.Value, ct);
            if (existingCore is null)
            {
                await MarkCpfIdentifierConflictAsync(
                    connection, tx, existingMapId!.Value, CpfIdentityConsistency.ExistingCoreUnavailableReason, ct);
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: CpfIdentityConsistency.ExistingCoreUnavailableReason);
            }

            var assessment = CpfIdentityConsistency.Evaluate(existingCore, incomingCore);
            if (assessment.IsConflict)
            {
                await MarkCpfIdentifierConflictAsync(connection, tx, existingMapId!.Value, assessment.Motivo!, ct);
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO, Motivo: assessment.Motivo);
            }

            return new InternalIdentityResolution(
                ResolutionStatus.RESOLVIDO, existingUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);
        }

        var created = Guid.NewGuid();
        await using (var insertPerson = Command(connection, tx,
                         "INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');"))
        {
            Add(insertPerson, "@uuid", DbType.Guid, created);
            await insertPerson.ExecuteNonQueryAsync(ct);
        }

        long mapId;
        await using (var insertMap = Command(connection, tx, """
            INSERT INTO identidade.identity_map(
                pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,
                metodo_resolucao,estado,estado_motivo,estado_em)
            VALUES(@uuid,'CPF',@cpf,CURRENT_TIMESTAMP,@gestor_id,@source_record_id,
                   'CPF_DETERMINISTICO','ATIVO','CPF_MAP_CRIADO',CURRENT_TIMESTAMP)
            RETURNING identity_map_id;
            """))
        {
            Add(insertMap, "@uuid", DbType.Guid, created);
            Add(insertMap, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
            Add(insertMap, "@gestor_id", DbType.Int64, gestorId);
            Add(insertMap, "@source_record_id", DbType.String, sourceRecordId, 255);
            mapId = Convert.ToInt64(await insertMap.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using (var stateEvent = Command(connection, tx, """
            INSERT INTO identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@map_id,NULL,'ATIVO','CPF_MAP_CRIADO');
            """))
        {
            Add(stateEvent, "@map_id", DbType.Int64, mapId);
            await stateEvent.ExecuteNonQueryAsync(ct);
        }

        return new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO, created, ResolutionMethod.CPF_DETERMINISTICO);
    }

    private static async Task MarkCpfIdentifierConflictAsync(
        DbConnection connection, DbTransaction tx, long identityMapId, string reason, CancellationToken ct)
    {
        Guid? uuid = null;
        string? cpf = null;
        await using (var update = Command(connection, tx, """
            UPDATE identidade.identity_map
               SET estado='EM_CONFLITO',estado_motivo=@motivo,estado_em=CURRENT_TIMESTAMP
             WHERE identity_map_id=@id AND estado='ATIVO' AND vigencia_fim IS NULL
            RETURNING pessoa_uuid,identificador;
            """))
        {
            Add(update, "@id", DbType.Int64, identityMapId);
            Add(update, "@motivo", DbType.String, reason, 120);
            await using var reader = await update.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return;
            uuid = reader.GetFieldValue<Guid>(0);
            cpf = reader.GetString(1);
        }

        await using (var history = Command(connection, tx, """
            INSERT INTO identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@id,'ATIVO','EM_CONFLITO',@motivo);
            """))
        {
            Add(history, "@id", DbType.Int64, identityMapId);
            Add(history, "@motivo", DbType.String, reason, 120);
            await history.ExecuteNonQueryAsync(ct);
        }

        foreach (var sql in new[]
                 {
                     "UPDATE gold.beneficio_concedido SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=CURRENT_TIMESTAMP WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';",
                     "UPDATE gold.servico_prestado SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=CURRENT_TIMESTAMP WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';",
                     "UPDATE serving.registro_integrado SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=CURRENT_TIMESTAMP WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';"
                 })
        {
            await using var suspend = Command(connection, tx, sql);
            Add(suspend, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
            await suspend.ExecuteNonQueryAsync(ct);
        }

        await using (var person = Command(connection, tx,
                         "UPDATE identidade.pessoa SET status='EM_CONFLITO',atualizado_em=CURRENT_TIMESTAMP WHERE pessoa_uuid=@uuid AND status='ATIVO';"))
        {
            Add(person, "@uuid", DbType.Guid, uuid);
            await person.ExecuteNonQueryAsync(ct);
        }
        await using (var goldPerson = Command(connection, tx, "DELETE FROM gold.pessoa WHERE pessoa_uuid=@uuid;"))
        {
            Add(goldPerson, "@uuid", DbType.Guid, uuid);
            await goldPerson.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<IdentityCore?> LoadExistingCoreAsync(
        DbConnection connection, DbTransaction tx, Guid uuid, CancellationToken ct)
    {
        await using (var gold = Command(connection, tx, """
            SELECT nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE pessoa_uuid=@uuid;
            """))
        {
            Add(gold, "@uuid", DbType.Guid, uuid);
            await using var reader = await gold.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new IdentityCore(reader.GetString(0), ReadDate(reader, 1), reader.GetString(2));
        }

        await using var silver = Command(connection, tx, """
            SELECT po.nome_completo,po.data_nascimento,po.nome_mae
              FROM silver.pessoa_observacao po
              JOIN identidade.v_vinculo_corrente vf
                ON vf.pessoa_observacao_id=po.pessoa_observacao_id
               AND vf.status='RESOLVIDO' AND vf.pessoa_uuid=@uuid
             ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
             LIMIT 1;
            """);
        Add(silver, "@uuid", DbType.Guid, uuid);
        await using var silverReader = await silver.ExecuteReaderAsync(ct);
        if (!await silverReader.ReadAsync(ct)) return null;
        return new IdentityCore(silverReader.GetString(0), ReadDate(silverReader, 1), silverReader.GetString(2));
    }

    private static DateOnly ReadDate(DbDataReader reader, int ordinal)
    {
        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => throw new InvalidCastException($"Data PostgreSQL inesperada: {raw.GetType().FullName}.")
        };
    }

    private static DbCommand Command(DbConnection connection, DbTransaction tx, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object? value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
