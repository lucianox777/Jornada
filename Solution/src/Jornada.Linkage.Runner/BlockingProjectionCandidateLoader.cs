using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

internal enum BlockingQueryDialect
{
    SqlServer,
    PostgreSql
}

/// <summary>
/// Executa um ruleset já verificado sobre a projeção indexada de blocking e materializa
/// os candidatos correntes da Gold. O conjunto de UUIDs é deduplicado pelos UNIONs do plano.
/// Nunca trunca silenciosamente: lê no máximo limite+1 e falha ao exceder o guard rail.
/// </summary>
internal static class BlockingProjectionCandidateLoader
{
    internal static async Task<IReadOnlyList<LinkageCandidate>> LoadAsync(
        DbConnection connection,
        LinkageDynamicRuleSet ruleSet,
        IdentityObservation observation,
        int maxCandidates,
        int commandTimeoutSeconds,
        BlockingQueryDialect dialect,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeoutSeconds);

        var passes = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);
        if (passes.Count == 0)
            return Array.Empty<LinkageCandidate>();

        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        var candidateUuidQuery = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
            command,
            passes,
            ruleSet.ProjectionSchemaVersion,
            ruleSet.ProjectionFingerprintSha256);
        Add(command, "@blocking_max_plus_one", DbType.Int32, maxCandidates + 1);

        command.CommandText = dialect switch
        {
            BlockingQueryDialect.SqlServer => $"""
                WITH candidate_uuid AS (
                    {candidateUuidQuery}
                )
                SELECT TOP (@blocking_max_plus_one)
                       g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
                  FROM candidate_uuid c
                  JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid
                 ORDER BY g.pessoa_uuid;
                """,
            BlockingQueryDialect.PostgreSql => $"""
                WITH candidate_uuid AS (
                    {candidateUuidQuery}
                )
                SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
                  FROM candidate_uuid c
                  JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid
                 ORDER BY g.pessoa_uuid
                 LIMIT @blocking_max_plus_one;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported blocking SQL dialect.")
        };

        var result = new List<LinkageCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new LinkageCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                ReadDateOnly(reader, 2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));

            if (result.Count > maxCandidates)
            {
                throw new InvalidOperationException(
                    $"Blocking dinâmico ruleset {ruleSet.RuleSetVersion} excedeu MaxCandidatesPerBlock={maxCandidates}; " +
                    "o run foi interrompido sem truncamento silencioso.");
            }
        }

        return result;
    }

    private static DateOnly ReadDateOnly(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => throw new InvalidOperationException(
                $"Provider retornou tipo inesperado para data de nascimento: {value.GetType().FullName}.")
        };
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
