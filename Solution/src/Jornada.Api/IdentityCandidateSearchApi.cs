using System.Data;
using Jornada.Access.Security;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

public interface IIdentityCandidateSearchService
{
    Task<IdentityCandidateSearchResponse> SearchAsync(
        AccessContext context, IdentityCandidateSearchRequest request, CancellationToken ct);
}

public sealed class CandidateSearchUnavailableException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Consulta somente-leitura. Usa o modelo ATIVO, o ruleset persistido com fingerprint
/// verificado, o blocking dinâmico e o MESMO rank C# FS do Runner, sem criar linkage_run.
/// </summary>
public sealed class SqlIdentityCandidateSearchService(
    IOperationalSqlAdapter sql,
    IConfiguration configuration) : IIdentityCandidateSearchService
{
    public async Task<IdentityCandidateSearchResponse> SearchAsync(
        AccessContext context, IdentityCandidateSearchRequest request, CancellationToken ct)
    {
        if (!context.Scopes.Contains(IdentityCandidateSearchApi.Permission, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Busca de candidatos não autorizada.");
        IdentityCandidateSearchApi.ValidateRequest(request);

        await using var connection = await sql.OpenAsync(ct);
        var (modelId, algorithm, parameters) = await ReadActiveModelAsync(connection, ct);
        var ruleset = await LinkageRuleSetReader.TryLoadAsync(connection, modelId, ct);
        if (ruleset is null)
            throw new CandidateSearchUnavailableException("RULESET_ATIVO_INDISPONIVEL");
        if (!string.Equals(ruleset.AlgorithmVersion, algorithm, StringComparison.Ordinal))
            throw new CandidateSearchUnavailableException("MODELO_RULESET_DIVERGENTE");

        var observation = new IdentityObservation(
            null, "BUSCA_AD_HOC_SEM_CPF", request.NomeCompleto,
            request.DataNascimento, request.NomeMae);
        var passes = IdentityCandidateBlockingPlanner.Plan(ruleset, observation);
        if (passes.Count == 0)
            return new IdentityCandidateSearchResponse([], true);

        var maxCandidates = Math.Clamp(
            configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000), 1000, 1000000);
        var timeoutSeconds = Math.Clamp(
            configuration.GetValue("CandidateSearch:CommandTimeoutSeconds", 30), 1, 60);
        var snapshots = await LoadCandidateSnapshotsAsync(
            connection, ruleset, passes, maxCandidates, timeoutSeconds, ct);

        // A lista completa é pontuada antes de selecionar as cinco sugestões.
        // Nunca truncar o blocking previamente: isso alteraria o prior e o top 5.
        var chosen = SemiblindCandidateSelector.Select(algorithm, parameters, observation, snapshots);
        var candidates = chosen.Select(static person => new IdentityCandidateDto(
            person.PessoaUuid, person.NomeCompleto, person.DataNascimento, person.NomeMae)).ToArray();
        return new IdentityCandidateSearchResponse(candidates, candidates.Length == 0);
    }

    private static async Task<(Guid ModelId, string Algorithm, IReadOnlyDictionary<string, decimal> Parameters)>
        ReadActiveModelAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT m.modelo_id,m.algoritmo_versao,p.nome,p.valor
              FROM identidade.modelo_linkage m
              JOIN identidade.parametro_linkage p ON p.modelo_id=m.modelo_id
             WHERE m.status='ATIVO'
             ORDER BY m.modelo_id,p.nome;
            """;
        Guid? modelId = null;
        string? algorithm = null;
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (modelId is not null && modelId != id)
                throw new CandidateSearchUnavailableException("MULTIPLOS_MODELOS_ATIVOS");
            modelId = id;
            algorithm = reader.GetString(1);
            if (!parameters.TryAdd(reader.GetString(2), reader.GetDecimal(3)))
                throw new CandidateSearchUnavailableException("PARAMETRO_MODELO_DUPLICADO");
        }

        if (modelId is null || algorithm is null)
            throw new CandidateSearchUnavailableException("MODELO_ATIVO_INDISPONIVEL");
        return (modelId.Value, algorithm, parameters);
    }

    private static async Task<IReadOnlyList<SemiblindCandidateSnapshot>> LoadCandidateSnapshotsAsync(
        SqlConnection connection,
        LinkageDynamicRuleSet ruleset,
        IReadOnlyList<BlockingCandidatePassLookup> passes,
        int maxCandidates,
        int timeoutSeconds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = timeoutSeconds;
        var uuidSql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
            command, passes, ruleset.ProjectionSchemaVersion, ruleset.ProjectionFingerprintSha256);
        command.Parameters.Add(new SqlParameter("@max_plus_one", SqlDbType.Int) { Value = maxCandidates + 1 });
        command.CommandText = $"""
            WITH candidate_uuid AS (
                {uuidSql}
            )
            SELECT DISTINCT TOP (@max_plus_one)
                   g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
              FROM candidate_uuid c
              JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid
             WHERE g.estado_identidade='REFERENCIA'
             ORDER BY g.pessoa_uuid;
            """;

        var candidates = new List<SemiblindCandidateSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            DateOnly? birth = null;
            if (!reader.IsDBNull(2))
            {
                birth = reader.GetValue(2) switch
                {
                    DateOnly date => date,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    _ => throw new CandidateSearchUnavailableException("DATA_NASCIMENTO_INVALIDA")
                };
            }
            candidates.Add(new SemiblindCandidateSnapshot(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                birth,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
            if (candidates.Count > maxCandidates)
                throw new CandidateSearchUnavailableException("BLOCKING_EXCEDE_LIMITE");
        }
        return candidates;
    }
}

/// <summary>UNION aditivo entre ruleset publicado e passes combinados quando nascimento está disponível.</summary>
public static class IdentityCandidateBlockingPlanner
{
    public static IReadOnlyList<BlockingCandidatePassLookup> Plan(
        LinkageDynamicRuleSet ruleset, IdentityObservation observation)
    {
        var dynamicPasses = BlockingRuleSetCandidatePlanner.Plan(ruleset, observation);
        var combinedPasses = CombinedIdentityCandidatePlanner.Plan(observation);
        return dynamicPasses.Concat(combinedPasses)
            .GroupBy(static pass => pass.PassId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }
}

public static class IdentityCandidateSearchApi
{
    public const string Route = "/api/v1/identidade/candidatos";
    public const string Permission = "jornada.identidade.candidatos.read";

    public static bool TryValidateRequest(IdentityCandidateSearchRequest? request) =>
        request is not null
        && !string.IsNullOrWhiteSpace(request.NomeCompleto)
        && request.NomeCompleto.Length is >= 2 and <= 255
        && !request.NomeCompleto.Any(char.IsControl)
        && (request.NomeMae is null ||
            (request.NomeMae.Length <= 255 && !request.NomeMae.Any(char.IsControl)))
        && (request.DataNascimento is null ||
            (request.DataNascimento.Value.Year >= 1850
             && request.DataNascimento.Value <= DateOnly.FromDateTime(DateTime.UtcNow)));

    public static void ValidateRequest(IdentityCandidateSearchRequest request)
    {
        if (!TryValidateRequest(request))
            throw new ArgumentException("Dados de busca inválidos.", nameof(request));
    }
}
