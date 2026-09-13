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
        await using (var keyLock = Command(connection, tx,
                         "SELECT pg_advisory_xact_lock(hashtextextended('JORNADA:CPF_ANCORA:' || @cpf,0));"))
        {
            Add(keyLock, "@cpf", DbType.String, cpf, 11);
            await keyLock.ExecuteNonQueryAsync(ct);
        }

        var anchorUuid = await LockCpfAnchorAsync(connection, tx, cpf, ct);

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

        if (anchorUuid.HasValue && existingUuid.HasValue && anchorUuid.Value != existingUuid.Value)
            throw new InvalidOperationException("CPF_ANCHOR_IDENTITY_MAP_DIVERGENCE: âncora permanente e mapa corrente apontam UUIDs diferentes.");

        if (existingUuid.HasValue)
        {
            if (!anchorUuid.HasValue)
            {
                anchorUuid = await ReserveCpfAnchorAsync(connection, tx, cpf, existingUuid.Value, ct);
                if (anchorUuid.Value != existingUuid.Value)
                    throw new InvalidOperationException("CPF_ANCHOR_RESERVATION_DIVERGENCE: reserva retornou UUID distinto do mapa corrente.");
            }

            if (string.Equals(existingState, "EM_CONFLITO", StringComparison.Ordinal))
                return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, anchorUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);

            var existingCore = await LoadExistingCoreAsync(connection, tx, existingUuid.Value, ct);
            if (existingCore is null)
            {
                await MarkCpfIdentifierConflictAsync(connection, tx, existingMapId!.Value, CpfIdentityConsistency.ExistingCoreUnavailableReason, ct);
                return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, anchorUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);
            }

            var assessment = CpfIdentityConsistency.Evaluate(existingCore, incomingCore);
            if (assessment.IsConflict)
            {
                await MarkCpfIdentifierConflictAsync(connection, tx, existingMapId!.Value, assessment.Motivo!, ct);
                return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, anchorUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);
            }

            return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, anchorUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);
        }

        if (anchorUuid.HasValue)
        {
            var historicalCore = await LoadExistingCoreAsync(connection, tx, anchorUuid.Value, ct);
            var recoveredMapId = await InsertActiveCpfMapAsync(
                connection, tx, anchorUuid.Value, cpf, gestorId, sourceRecordId,
                "CPF_MAP_RECUPERADO_ANCORA", ct);

            if (historicalCore is not null)
            {
                var assessment = CpfIdentityConsistency.Evaluate(historicalCore, incomingCore);
                if (assessment.IsConflict)
                    await MarkCpfIdentifierConflictAsync(connection, tx, recoveredMapId, assessment.Motivo!, ct);
            }

            return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, anchorUuid.Value, ResolutionMethod.CPF_DETERMINISTICO);
        }

        var created = Guid.NewGuid();
        await using (var insertPerson = Command(connection, tx,
                         "INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');"))
        {
            Add(insertPerson, "@uuid", DbType.Guid, created);
            await insertPerson.ExecuteNonQueryAsync(ct);
        }

        var reserved = await ReserveCpfAnchorAsync(connection, tx, cpf, created, ct);
        if (reserved != created)
            throw new InvalidOperationException("CPF_ANCHOR_NEW_PERSON_DIVERGENCE: UUID recém-criado não corresponde à âncora reservada.");

        await InsertActiveCpfMapAsync(
            connection, tx, created, cpf, gestorId, sourceRecordId,
            "CPF_MAP_CRIADO", ct);
        return new InternalIdentityResolution(ResolutionStatus.RESOLVIDO, created, ResolutionMethod.CPF_DETERMINISTICO);
    }

    private static async Task<Guid?> LockCpfAnchorAsync(DbConnection connection, DbTransaction tx, string cpf, CancellationToken ct)
    {
        await using var command = Command(connection, tx, "SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf FOR UPDATE;");
        Add(command, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task<Guid> ReserveCpfAnchorAsync(DbConnection connection, DbTransaction tx, string cpf, Guid uuid, CancellationToken ct)
    {
        await using var command = Command(connection, tx, "SELECT identidade.fn_reservar_cpf_ancora(@cpf,@uuid);");
        Add(command, "@cpf", DbType.String, cpf, 11);
        Add(command, "@uuid", DbType.Guid, uuid);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is not Guid reserved || reserved == Guid.Empty)
            throw new InvalidOperationException("Reserva da âncora CPF não retornou UUID válido.");
        return reserved;
    }

    private static async Task<long> InsertActiveCpfMapAsync(
        DbConnection connection, DbTransaction tx, Guid uuid, string cpf, long? gestorId,
        string? sourceRecordId, string reason, CancellationToken ct)
    {
        long mapId;
        await using (var insertMap = Command(connection, tx, """
            INSERT INTO identidade.identity_map(
                pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,
                metodo_resolucao,estado,estado_motivo,estado_em)
            VALUES(@uuid,'CPF',@cpf,CURRENT_TIMESTAMP,@gestor_id,@source_record_id,
                   'CPF_DETERMINISTICO','ATIVO',@motivo,CURRENT_TIMESTAMP)
            RETURNING identity_map_id;
            """))
        {
            Add(insertMap, "@uuid", DbType.Guid, uuid);
            Add(insertMap, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
            Add(insertMap, "@gestor_id", DbType.Int64, gestorId);
            Add(insertMap, "@source_record_id", DbType.String, sourceRecordId, 255);
            Add(insertMap, "@motivo", DbType.String, reason, 120);
            mapId = Convert.ToInt64(await insertMap.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var stateEvent = Command(connection, tx, """
            INSERT INTO identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@map_id,NULL,'ATIVO',@motivo);
            """);
        Add(stateEvent, "@map_id", DbType.Int64, mapId);
        Add(stateEvent, "@motivo", DbType.String, reason, 120);
        await stateEvent.ExecuteNonQueryAsync(ct);
        return mapId;
    }

    private static async Task MarkCpfIdentifierConflictAsync(
        DbConnection connection, DbTransaction tx, long identityMapId, string reason, CancellationToken ct)
    {
        await using (var update = Command(connection, tx, """
            UPDATE identidade.identity_map
               SET estado='EM_CONFLITO',estado_motivo=@motivo,estado_em=CURRENT_TIMESTAMP
             WHERE identity_map_id=@id AND estado='ATIVO' AND vigencia_fim IS NULL
            RETURNING identity_map_id;
            """))
        {
            Add(update, "@id", DbType.Int64, identityMapId);
            Add(update, "@motivo", DbType.String, reason, 120);
            var changed = await update.ExecuteScalarAsync(ct);
            if (changed is null or DBNull) return;
        }

        await using var history = Command(connection, tx, """
            INSERT INTO identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@id,'ATIVO','EM_CONFLITO',@motivo);
            """);
        Add(history, "@id", DbType.Int64, identityMapId);
        Add(history, "@motivo", DbType.String, reason, 120);
        await history.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IdentityCore?> LoadExistingCoreAsync(DbConnection connection, DbTransaction tx, Guid uuid, CancellationToken ct)
    {
        await using (var gold = Command(connection, tx,
                         "SELECT nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE pessoa_uuid=@uuid;"))
        {
            Add(gold, "@uuid", DbType.Guid, uuid);
            await using var reader = await gold.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new IdentityCore(reader.GetString(0), ReadDate(reader, 1), reader.IsDBNull(2) ? null : reader.GetString(2));
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
        return new IdentityCore(silverReader.GetString(0), ReadDate(silverReader, 1), silverReader.IsDBNull(2) ? null : silverReader.GetString(2));
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