using System.Data;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed class SqlIdentityMapRepository(IOperationalSqlAdapter connections) : IIdentityMapRepository
{
    public async Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
        string cpf,
        IdentityObservation observation,
        CancellationToken ct)
    {
        var normalized = CpfRules.NormalizeAndValidate(cpf)
            ?? throw new ArgumentException("CPF inválido.", nameof(cpf));
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await ResolveOrCreateByCpfAsync(
                connection, tx, normalized,
                new IdentityCore(observation.NomeCompleto, observation.DataNascimento, observation.NomeMae),
                gestorId: null, sourceRecordId: null, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal static async Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string cpf,
        IdentityCore incomingCore,
        long? gestorId,
        string? sourceRecordId,
        CancellationToken ct)
    {
        // A âncora é a autoridade permanente. O range lock é adquirido antes do identity_map
        // para que duas primeiras aparições simultâneas do mesmo CPF nunca constituam UUIDs concorrentes.
        var anchorUuid = await LockCpfAnchorAsync(connection, transaction, cpf, ct);

        Guid? existingUuid = null;
        long? existingMapId = null;
        string? existingState = null;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT TOP(1) identity_map_id,pessoa_uuid,estado
                FROM identidade.identity_map WITH (UPDLOCK,HOLDLOCK)
                WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL
                ORDER BY vigencia_inicio DESC,identity_map_id DESC;
                """;
            find.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                existingMapId = reader.GetInt64(0);
                existingUuid = reader.GetGuid(1);
                existingState = reader.GetString(2);
            }
        }

        if (anchorUuid.HasValue && existingUuid.HasValue && anchorUuid.Value != existingUuid.Value)
            throw new InvalidOperationException("CPF_ANCHOR_IDENTITY_MAP_DIVERGENCE: âncora permanente e mapa corrente apontam UUIDs diferentes.");

        if (existingUuid.HasValue)
        {
            // Bases constituídas por caminhos determinísticos antigos passam a reservar a âncora
            // no primeiro uso do writer V1. A reserva nunca transfere CPF nem UUID.
            if (!anchorUuid.HasValue)
            {
                anchorUuid = await ReserveCpfAnchorAsync(connection, transaction, cpf, existingUuid.Value, ct);
                if (anchorUuid.Value != existingUuid.Value)
                    throw new InvalidOperationException("CPF_ANCHOR_RESERVATION_DIVERGENCE: reserva retornou UUID distinto do mapa corrente.");
            }

            if (string.Equals(existingState, "EM_CONFLITO", StringComparison.Ordinal))
            {
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO,
                    null,
                    ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: CpfIdentityConsistency.IdentifierInConflictReason);
            }
            var existingCore = await LoadExistingCoreAsync(connection, transaction, existingUuid.Value, ct);
            if (existingCore is null)
            {
                await MarkCpfIdentifierConflictAsync(connection, transaction, existingMapId!.Value, CpfIdentityConsistency.ExistingCoreUnavailableReason, ct);
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO,
                    null,
                    ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: CpfIdentityConsistency.ExistingCoreUnavailableReason);
            }

            var assessment = CpfIdentityConsistency.Evaluate(existingCore, incomingCore);
            if (assessment.IsConflict)
            {
                await MarkCpfIdentifierConflictAsync(connection, transaction, existingMapId!.Value, assessment.Motivo!, ct);
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO,
                    null,
                    ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: assessment.Motivo);
            }

            return new InternalIdentityResolution(
                ResolutionStatus.RESOLVIDO,
                anchorUuid.Value,
                ResolutionMethod.CPF_DETERMINISTICO);
        }

        if (anchorUuid.HasValue)
        {
            // Uma âncora sobrevive ao fechamento/limpeza do mapa corrente. Na reaparição,
            // recupera-se o mesmo UUID e apenas se recompõe a projeção operacional identity_map.
            var historicalCore = await LoadExistingCoreAsync(connection, transaction, anchorUuid.Value, ct);
            if (historicalCore is not null)
            {
                var assessment = CpfIdentityConsistency.Evaluate(historicalCore, incomingCore);
                if (assessment.IsConflict)
                {
                    return new InternalIdentityResolution(
                        ResolutionStatus.CONFLITO,
                        null,
                        ResolutionMethod.CPF_DETERMINISTICO,
                        Motivo: assessment.Motivo);
                }
            }

            await InsertActiveCpfMapAsync(
                connection, transaction, anchorUuid.Value, cpf, gestorId, sourceRecordId,
                "CPF_MAP_RECUPERADO_ANCORA", ct);
            return new InternalIdentityResolution(
                ResolutionStatus.RESOLVIDO,
                anchorUuid.Value,
                ResolutionMethod.CPF_DETERMINISTICO);
        }

        var created = Guid.NewGuid();
        await using (var insertPerson = connection.CreateCommand())
        {
            insertPerson.Transaction = transaction;
            insertPerson.CommandText = "INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');";
            insertPerson.Parameters.AddWithValue("@uuid", created);
            await insertPerson.ExecuteNonQueryAsync(ct);
        }

        var reserved = await ReserveCpfAnchorAsync(connection, transaction, cpf, created, ct);
        if (reserved != created)
            throw new InvalidOperationException("CPF_ANCHOR_NEW_PERSON_DIVERGENCE: UUID recém-criado não corresponde à âncora reservada.");

        await InsertActiveCpfMapAsync(
            connection, transaction, created, cpf, gestorId, sourceRecordId,
            "CPF_MAP_CRIADO", ct);
        return new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO,
            created,
            ResolutionMethod.CPF_DETERMINISTICO);
    }

    private static async Task<Guid?> LockCpfAnchorAsync(
        SqlConnection connection, SqlTransaction transaction, string cpf, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pessoa_uuid
              FROM identidade.cpf_ancora WITH(UPDLOCK,HOLDLOCK)
             WHERE cpf=CONVERT(CHAR(11),@cpf) COLLATE Latin1_General_100_BIN2;
            """;
        command.Parameters.Add(new SqlParameter("@cpf", SqlDbType.NVarChar, 64) { Value = cpf });
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task<Guid> ReserveCpfAnchorAsync(
        SqlConnection connection, SqlTransaction transaction, string cpf, Guid uuid, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @resultado UNIQUEIDENTIFIER;
            EXEC identidade.sp_reservar_cpf_ancora @cpf=@cpf,@pessoa_uuid=@uuid,@uuid_resultado=@resultado OUTPUT;
            SELECT @resultado;
            """;
        command.Parameters.Add(new SqlParameter("@cpf", SqlDbType.NVarChar, 64) { Value = cpf });
        command.Parameters.AddWithValue("@uuid", uuid);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is not Guid reserved || reserved == Guid.Empty)
            throw new InvalidOperationException("Reserva da âncora CPF não retornou UUID válido.");
        return reserved;
    }

    private static async Task<long> InsertActiveCpfMapAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid uuid,
        string cpf,
        long? gestorId,
        string? sourceRecordId,
        string reason,
        CancellationToken ct)
    {
        long mapId;
        await using (var insertMap = connection.CreateCommand())
        {
            insertMap.Transaction = transaction;
            insertMap.CommandText = """
                INSERT identidade.identity_map(
                    pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo,estado_em)
                OUTPUT INSERTED.identity_map_id
                VALUES(@uuid,'CPF',@cpf,SYSDATETIMEOFFSET(),@gestor_id,@source_record_id,'CPF_DETERMINISTICO','ATIVO',@motivo,SYSDATETIMEOFFSET());
                """;
            insertMap.Parameters.AddWithValue("@uuid", uuid);
            insertMap.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            insertMap.Parameters.Add(new SqlParameter("@gestor_id", SqlDbType.BigInt) { Value = (object?)gestorId ?? DBNull.Value });
            insertMap.Parameters.Add(new SqlParameter("@source_record_id", SqlDbType.NVarChar, 255) { Value = (object?)sourceRecordId ?? DBNull.Value });
            insertMap.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = reason });
            mapId = Convert.ToInt64(await insertMap.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var eventInsert = connection.CreateCommand();
        eventInsert.Transaction = transaction;
        eventInsert.CommandText = """
            INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@map_id,NULL,'ATIVO',@motivo);
            """;
        eventInsert.Parameters.AddWithValue("@map_id", mapId);
        eventInsert.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = reason });
        await eventInsert.ExecuteNonQueryAsync(ct);
        return mapId;
    }

    private static async Task MarkCpfIdentifierConflictAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long identityMapId,
        string reason,
        CancellationToken ct)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE identidade.identity_map
               SET estado='EM_CONFLITO',estado_motivo=@motivo,estado_em=SYSDATETIMEOFFSET()
             WHERE identity_map_id=@id AND estado='ATIVO' AND vigencia_fim IS NULL;
            SELECT @@ROWCOUNT;
            """;
        update.Parameters.AddWithValue("@id", identityMapId);
        update.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = reason });
        var changed = Convert.ToInt32(await update.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        if (changed == 0) return;

        await using var history = connection.CreateCommand();
        history.Transaction = transaction;
        history.CommandText = """
            INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
            VALUES(@id,'ATIVO','EM_CONFLITO',@motivo);
            """;
        history.Parameters.AddWithValue("@id", identityMapId);
        history.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = reason });
        await history.ExecuteNonQueryAsync(ct);

        // Conflito do identificador suspende somente a atribuição canônica; os fatos permanecem Gold.
        await using var suspendFacts = connection.CreateCommand();
        suspendFacts.Transaction = transaction;
        suspendFacts.CommandText = """
            DECLARE @cpf CHAR(11),@uuid UNIQUEIDENTIFIER;
            SELECT @cpf=identificador,@uuid=pessoa_uuid FROM identidade.identity_map WHERE identity_map_id=@id;
            UPDATE gold.beneficio_concedido SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
             WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';
            UPDATE gold.servico_prestado SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
             WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';
            UPDATE serving.registro_integrado SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
             WHERE cpf_declarado=@cpf AND status_analitico='VIGENTE';
            UPDATE identidade.pessoa SET status='EM_CONFLITO' WHERE pessoa_uuid=@uuid AND status='ATIVO';
            DELETE FROM gold.pessoa WHERE pessoa_uuid=@uuid;
            """;
        suspendFacts.Parameters.AddWithValue("@id", identityMapId);
        await suspendFacts.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IdentityCore?> LoadExistingCoreAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        await using (var gold = connection.CreateCommand())
        {
            gold.Transaction = transaction;
            gold.CommandText = """
                SELECT nome_completo,data_nascimento,nome_mae
                FROM gold.pessoa
                WHERE pessoa_uuid=@uuid;
                """;
            gold.Parameters.AddWithValue("@uuid", pessoaUuid);
            await using var reader = await gold.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new IdentityCore(
                    reader.GetString(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    reader.GetString(2));
            }
        }

        await using var silver = connection.CreateCommand();
        silver.Transaction = transaction;
        silver.CommandText = """
            SELECT TOP(1) po.nome_completo,po.data_nascimento,po.nome_mae
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vf
              ON vf.pessoa_observacao_id=po.pessoa_observacao_id
             AND vf.status='RESOLVIDO'
             AND vf.pessoa_uuid=@uuid
            ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC;
            """;
        silver.Parameters.AddWithValue("@uuid", pessoaUuid);
        await using var silverReader = await silver.ExecuteReaderAsync(ct);
        if (!await silverReader.ReadAsync(ct)) return null;
        return new IdentityCore(
            silverReader.GetString(0),
            DateOnly.FromDateTime(silverReader.GetDateTime(1)),
            silverReader.GetString(2));
    }
}

internal sealed partial class SqlProcessorRepository
{
    private readonly IOperationalSqlAdapter connections;
    private readonly RegistryQualityEngine quality;

    public SqlProcessorRepository(IOperationalSqlAdapter connections, RegistryQualityEngine quality)
    {
        this.connections = connections;
        this.quality = quality;
    }
}
