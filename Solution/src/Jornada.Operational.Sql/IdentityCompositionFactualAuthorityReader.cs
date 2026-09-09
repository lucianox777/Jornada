using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public interface IIdentityCompositionFactualAuthorityReader
{
    Task<ImmutableArray<IdentityCompositionFactualAttributionSnapshot>> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionRecompositionPlan recomposition,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fecha a autoridade factual corrente dos registros já incluídos no plano de recomposição.
/// A composição estrutural não é usada para escolher pessoa_uuid: o único UUID factual aceito vem
/// do vínculo_fonte ativo da observação de pessoa. Este leitor não altera vínculos nem projeções.
/// </summary>
public sealed class IdentityCompositionFactualAuthorityReader : IIdentityCompositionFactualAuthorityReader
{
    private readonly bool postgres;

    public IdentityCompositionFactualAuthorityReader(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<ImmutableArray<IdentityCompositionFactualAttributionSnapshot>> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionRecompositionPlan recomposition,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(recomposition);
        if (recomposition.DecisionId == Guid.Empty || recomposition.AffectedInitialUuids.IsDefault ||
            recomposition.RegistroObservacaoIds.IsDefault)
            throw new InvalidOperationException("Plano de recomposição inválido para leitura factual.");

        var affected = recomposition.AffectedInitialUuids.ToHashSet();
        if (affected.Count != recomposition.AffectedInitialUuids.Length || affected.Contains(Guid.Empty))
            throw new InvalidOperationException("UUIDs iniciais afetados inválidos ou duplicados.");
        var records = recomposition.RegistroObservacaoIds.Order().ToArray();
        if (records.Any(id => id <= 0) || records.Distinct().Count() != records.Length)
            throw new InvalidOperationException("Registros afetados inválidos ou duplicados.");

        var result = ImmutableArray.CreateBuilder<IdentityCompositionFactualAttributionSnapshot>(records.Length);
        foreach (var recordId in records)
        {
            var record = await LoadAndLockRecordAsync(connection, transaction, recordId, cancellationToken)
                ?? throw new InvalidOperationException("Registro afetado não existe mais no estado autoritativo.");
            if (!affected.Contains(record.InitialUuid))
                throw new InvalidOperationException("Registro afetado pertence a UUID inicial fora do plano de recomposição.");

            var link = await LoadAndLockActiveLinkAsync(
                connection, transaction, record.PessoaObservacaoId, cancellationToken);
            result.Add(new IdentityCompositionFactualAttributionSnapshot(
                record.InitialUuid,
                record.PessoaOrigemId,
                record.PessoaObservacaoId,
                recordId,
                link?.PessoaUuid,
                link?.Status));
        }

        if (result.Count != records.Length || result.Select(x => x.RegistroObservacaoId).Distinct().Count() != records.Length)
            throw new InvalidOperationException("Leitura factual não fechou exatamente os registros esperados.");
        return result.ToImmutable();
    }

    private async Task<RecordOwner?> LoadAndLockRecordAsync(
        DbConnection connection,
        DbTransaction transaction,
        long recordId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? """
              SELECT pop.initial_uuid,po.pessoa_origem_id,po.pessoa_observacao_id
              FROM silver.registro_observacao ro
              JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
              JOIN identidade.pessoa_origem_progressiva pop ON pop.pessoa_origem_id=po.pessoa_origem_id
              WHERE ro.registro_observacao_id=@record
              FOR UPDATE OF ro,po,pop;
              """
            : """
              SELECT pop.initial_uuid,po.pessoa_origem_id,po.pessoa_observacao_id
              FROM silver.registro_observacao ro WITH(UPDLOCK,HOLDLOCK)
              JOIN silver.pessoa_observacao po WITH(UPDLOCK,HOLDLOCK) ON po.pessoa_observacao_id=ro.pessoa_observacao_id
              JOIN identidade.pessoa_origem_progressiva pop WITH(UPDLOCK,HOLDLOCK) ON pop.pessoa_origem_id=po.pessoa_origem_id
              WHERE ro.registro_observacao_id=@record;
              """;
        Add(command, "@record", DbType.Int64, recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var initial = reader.GetGuid(0);
        var sourceId = reader.GetInt64(1);
        var observationId = reader.GetInt64(2);
        if (initial == Guid.Empty || sourceId <= 0 || observationId <= 0)
            throw new InvalidOperationException("Proveniência factual autoritativa inválida.");
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Registro factual possui mais de uma proveniência autoritativa.");
        return new RecordOwner(initial, sourceId, observationId);
    }

    private async Task<ActiveLink?> LoadAndLockActiveLinkAsync(
        DbConnection connection,
        DbTransaction transaction,
        long pessoaObservacaoId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? """
              SELECT pessoa_uuid,status
              FROM identidade.vinculo_fonte
              WHERE pessoa_observacao_id=@observation AND ativo
              ORDER BY vinculo_fonte_id
              FOR UPDATE;
              """
            : """
              SELECT pessoa_uuid,status
              FROM identidade.vinculo_fonte WITH(UPDLOCK,HOLDLOCK)
              WHERE pessoa_observacao_id=@observation AND ativo=1
              ORDER BY vinculo_fonte_id;
              """;
        Add(command, "@observation", DbType.Int64, pessoaObservacaoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var uuid = reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0);
        var status = reader.GetString(1);
        if (uuid == Guid.Empty || string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("Vínculo factual corrente inválido.");
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Observação possui mais de um vínculo factual ativo.");
        return new ActiveLink(uuid, status);
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Leitura factual exige transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record RecordOwner(Guid InitialUuid, long PessoaOrigemId, long PessoaObservacaoId);
    private sealed record ActiveLink(Guid? PessoaUuid, string Status);
}
