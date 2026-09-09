using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>
/// Resolve a autoridade CPF de cada origem já carregada. O contrato recebe somente UUIDs e IDs
/// internos; implementações não devem registrar CPF em logs nem inferir titularidade apenas porque
/// várias origens compartilham o mesmo canonical_uuid.
/// </summary>
public interface IIdentityCompositionCpfAuthorityReader
{
    Task<IReadOnlyDictionary<Guid, Guid?>> LoadAnchorUuidsAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyCollection<IdentityCompositionOriginSnapshot> origins,
        CancellationToken cancellationToken = default);
}

public sealed record IdentityCompositionOriginSnapshot(
    long SourceId,
    Guid InitialUuid,
    Guid? CanonicalUuid,
    ProgressiveIdentityStatus Status,
    long Version);

/// <summary>
/// Leitor do componente progressivo corrente. Expande a partir das origens declaradas e de todos
/// os destinos existentes envolvidos até incluir todos os membros das referências afetadas.
/// Não escreve dados. A autoridade CPF é delegada a um componente separado para evitar inferência
/// por mera coincidência de canonical_uuid.
/// </summary>
public sealed class IdentityCompositionAuthoritativeReader : IIdentityCompositionAuthoritativeReader
{
    private readonly bool postgres;
    private readonly IIdentityCompositionCpfAuthorityReader cpfAuthority;

    public IdentityCompositionAuthoritativeReader(
        IOperationalDatabaseAdapter database,
        IIdentityCompositionCpfAuthorityReader cpfAuthority)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.cpfAuthority = cpfAuthority ?? throw new ArgumentNullException(nameof(cpfAuthority));
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<IdentityCompositionReadSet> LoadClosedReadSetAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionDecision decision,
        IReadOnlyList<Guid> reservedUuids,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(reservedUuids);
        if (decision.DecisionId == Guid.Empty || decision.Assignments.IsDefaultOrEmpty)
            throw new InvalidOperationException("Decisão de composição incompleta.");
        if (reservedUuids.Any(id => id == Guid.Empty) || reservedUuids.Distinct().Count() != reservedUuids.Count)
            throw new InvalidOperationException("Reservas autoritativas inválidas ou duplicadas.");

        var seedIds = decision.Assignments.Select(a => a.InitialUuid).Order().ToArray();
        if (seedIds.Any(id => id == Guid.Empty) || seedIds.Distinct().Count() != seedIds.Length)
            throw new InvalidOperationException("Origens declaradas inválidas ou duplicadas.");

        var origins = new Dictionary<Guid, IdentityCompositionOriginSnapshot>();
        foreach (var id in seedIds)
        {
            var origin = await LoadByInitialUuidAsync(connection, transaction, id, cancellationToken)
                ?? throw new InvalidOperationException("Origem progressiva declarada não existe no estado autoritativo.");
            if (!origins.TryAdd(origin.InitialUuid, origin))
                throw new InvalidOperationException("Origem progressiva duplicada no estado autoritativo.");
        }

        var reserved = reservedUuids.ToHashSet();
        var references = origins.Values.Where(x => x.CanonicalUuid is not null)
            .Select(x => x.CanonicalUuid!.Value)
            .Concat(decision.Assignments.Where(a => a.TargetUuid is not null && !reserved.Contains(a.TargetUuid.Value))
                .Select(a => a.TargetUuid!.Value))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        // A expansão é monotônica. Novos membros podem introduzir apenas a mesma referência corrente,
        // mas o laço mantém o contrato correto caso o armazenamento futuro acrescente outra aresta
        // autoritativa que exija expansão adicional.
        var scannedReferences = new HashSet<Guid>();
        while (true)
        {
            var pending = references.Except(scannedReferences).Order().ToArray();
            if (pending.Length == 0) break;
            foreach (var reference in pending)
            {
                await LockReferenceAsync(connection, transaction, decision.DecisionId, reference, cancellationToken);
                var referenceMembers = await LoadByCanonicalUuidAsync(connection, transaction, reference, cancellationToken);
                foreach (var member in referenceMembers)
                {
                    if (origins.TryGetValue(member.InitialUuid, out var existing) && existing != member)
                        throw new InvalidOperationException("Leitura autoritativa retornou estados divergentes para a mesma origem.");
                    origins[member.InitialUuid] = member;
                    if (member.CanonicalUuid is { } current) references.Add(current);
                }
                scannedReferences.Add(reference);
            }
        }

        if (decision.Assignments.Any(a => !origins.ContainsKey(a.InitialUuid)))
            throw new InvalidOperationException("Componente autoritativo perdeu origem declarada durante a leitura.");

        var ordered = origins.Values.OrderBy(x => x.InitialUuid).ToArray();
        var anchors = await cpfAuthority.LoadAnchorUuidsAsync(
            connection, transaction, ordered, cancellationToken);
        if (anchors.Count != ordered.Length || ordered.Any(x => !anchors.ContainsKey(x.InitialUuid)))
            throw new InvalidOperationException("Autoridade CPF não respondeu por todas as origens carregadas.");
        foreach (var pair in anchors)
            if (pair.Key == Guid.Empty || pair.Value == Guid.Empty || !origins.ContainsKey(pair.Key))
                throw new InvalidOperationException("Autoridade CPF retornou referência inválida ou origem desconhecida.");

        var compositionMembers = ordered.Select(x => new IdentityCompositionMember(
            x.InitialUuid,
            x.CanonicalUuid,
            x.Status,
            x.Version,
            anchors[x.InitialUuid])).ToImmutableArray();

        // O histórico efetivado ainda não existe como armazenamento nesta fatia. Passar vazio é
        // explícito: HistoryToAppend do PREPARADA continua proposta e não é promovido a fato.
        return new IdentityCompositionReadSet(
            compositionMembers,
            reservedUuids.Order().ToImmutableArray(),
            ImmutableArray<IdentityCompositionHistory>.Empty);
    }

    private async Task<IdentityCompositionOriginSnapshot?> LoadByInitialUuidAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid initialUuid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_origem_id,initial_uuid,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WHERE initial_uuid=@uuid FOR UPDATE;"
            : "SELECT pessoa_origem_id,initial_uuid,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK) WHERE initial_uuid=@uuid;";
        Add(command, "@uuid", DbType.Guid, initialUuid);
        return await ReadSingleAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<IdentityCompositionOriginSnapshot>> LoadByCanonicalUuidAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid canonicalUuid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_origem_id,initial_uuid,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WHERE canonical_uuid=@uuid ORDER BY initial_uuid FOR UPDATE;"
            : "SELECT pessoa_origem_id,initial_uuid,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK) WHERE canonical_uuid=@uuid ORDER BY initial_uuid;";
        Add(command, "@uuid", DbType.Guid, canonicalUuid);
        var result = new List<IdentityCompositionOriginSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private async Task LockReferenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid decisionId,
        Guid reference,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (postgres)
        {
            command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@resource,0));";
            Add(command, "@resource", DbType.String, $"JORNADA:COMPOSICAO:REF:{reference:D}");
        }
        else
        {
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @r<0 THROW 51440,'Não foi possível travar referência de composição.',1;";
            Add(command, "@resource", DbType.String, $"JORNADA:COMPOSICAO:REF:{reference:D}");
        }
        _ = decisionId; // decision_id permanece no contrato para futura hierarquia de locks da publicação.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IdentityCompositionOriginSnapshot?> ReadSingleAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var value = Read(reader);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("UUID inicial possui mais de uma origem progressiva.");
        return value;
    }

    private static IdentityCompositionOriginSnapshot Read(DbDataReader reader)
    {
        var sourceId = reader.GetInt64(0);
        var initial = reader.GetGuid(1);
        var canonical = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var stateText = reader.GetString(3);
        if (sourceId <= 0 || initial == Guid.Empty || canonical == Guid.Empty ||
            !Enum.TryParse<ProgressiveIdentityStatus>(stateText, false, out var status) || !Enum.IsDefined(status))
            throw new InvalidOperationException("Estado progressivo autoritativo inválido.");
        var version = reader.GetInt64(4);
        if (version < 0 ||
            (status == ProgressiveIdentityStatus.REFERENCIA) != (canonical is not null) ||
            (status == ProgressiveIdentityStatus.PROVISORIA && version != 0) ||
            (status != ProgressiveIdentityStatus.PROVISORIA && version == 0))
            throw new InvalidOperationException("Versão ou referência progressiva autoritativa inválida.");
        return new IdentityCompositionOriginSnapshot(sourceId, initial, canonical, status, version);
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Leitura autoritativa exige transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
