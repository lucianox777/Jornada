using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed class ProcessorSqlConnectionFactory
{
    private readonly string _connectionString;

    public ProcessorSqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Jornada")
            ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}

internal sealed class SqlIdentityMapRepository(ProcessorSqlConnectionFactory connections) : IIdentityMapRepository
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

        if (existingUuid.HasValue)
        {
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
                // Um CPF já mapeado não pode ser reutilizado sem um núcleo comparável.
                // Além de bloquear esta observação, o identificador inteiro entra em conflito para
                // que CPF->UUID não continue afirmando uma identidade que a plataforma não consegue sustentar.
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
                existingUuid.Value,
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
        await using (var insertMap = connection.CreateCommand())
        {
            insertMap.Transaction = transaction;
            insertMap.CommandText = """
                INSERT identidade.identity_map(
                    pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo,estado_em)
                OUTPUT INSERTED.identity_map_id
                VALUES(@uuid,'CPF',@cpf,SYSDATETIMEOFFSET(),@gestor_id,@source_record_id,'CPF_DETERMINISTICO','ATIVO','CPF_MAP_CRIADO',SYSDATETIMEOFFSET());
                """;
            insertMap.Parameters.AddWithValue("@uuid", created);
            insertMap.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            insertMap.Parameters.Add(new SqlParameter("@gestor_id", SqlDbType.BigInt) { Value = (object?)gestorId ?? DBNull.Value });
            insertMap.Parameters.Add(new SqlParameter("@source_record_id", SqlDbType.NVarChar, 255) { Value = (object?)sourceRecordId ?? DBNull.Value });
            var mapId = Convert.ToInt64(await insertMap.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            await using var eventInsert = connection.CreateCommand();
            eventInsert.Transaction = transaction;
            eventInsert.CommandText = """
                INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo)
                VALUES(@map_id,NULL,'ATIVO','CPF_MAP_CRIADO');
                """;
            eventInsert.Parameters.AddWithValue("@map_id", mapId);
            await eventInsert.ExecuteNonQueryAsync(ct);
        }
        return new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO,
            created,
            ResolutionMethod.CPF_DETERMINISTICO);
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

        // v3.45: conflito do identificador suspende apenas a atribuição canônica; os fatos permanecem Gold.
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

        // Fallback para bases migradas ou para um UUID constituído antes da primeira
        // materialização Gold. A observação recebida nesta transação ainda não possui
        // vínculo e, portanto, não participa desta consulta.
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
    private readonly ProcessorSqlConnectionFactory connections;
    private readonly RegistryQualityEngine quality;

    public SqlProcessorRepository(ProcessorSqlConnectionFactory connections, RegistryQualityEngine quality)
    {
        this.connections = connections;
        this.quality = quality;
    }
}
