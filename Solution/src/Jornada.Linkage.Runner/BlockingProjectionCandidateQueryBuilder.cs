using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Constrói, de forma provider-neutral e totalmente parametrizada, a consulta de UUIDs candidatos
/// sobre identidade.blocking_chave. Cada passe usa INTERSECT entre seus atributos e os passes
/// são combinados por UNION. Aliases de nome históricos permanecem elegíveis; dados estáveis
/// usam somente a chave corrente. Rulesets modernos filtram também a identidade exata da projeção física.
/// </summary>
public static class BlockingProjectionCandidateQueryBuilder
{
    public const string MethodVersion = "BLOCKING_PROJECTION_CANDIDATE_QUERY_V3";
    public const int DefaultMaxParameters = 1800;

    public static string BuildCandidateUuidQuery(
        DbCommand command,
        IReadOnlyList<BlockingCandidatePassLookup> passes,
        string? projectionSchemaVersion = null,
        string? projectionFingerprintSha256 = null,
        int maxParameters = DefaultMaxParameters)
        => BuildCandidateUuidQueryCore(command, passes, projectionSchemaVersion,
            projectionFingerprintSha256, maxParameters, dynamicPassCount: null);

    /// <summary>
    /// Read-only audit: preserve D/C provenance for exact overlap counting in a single
    /// SQL statement. Reuses every filtering, projection and parameter-limit contract
    /// of BuildCandidateUuidQuery; never changes the Runner's production SQL.
    /// </summary>
    public static string BuildTaggedCandidateUuidQuery(
        DbCommand command,
        IReadOnlyList<BlockingCandidatePassLookup> dynamicPasses,
        IReadOnlyList<BlockingCandidatePassLookup> combinedPasses,
        string? projectionSchemaVersion = null,
        string? projectionFingerprintSha256 = null,
        int maxParameters = DefaultMaxParameters)
    {
        ArgumentNullException.ThrowIfNull(dynamicPasses);
        ArgumentNullException.ThrowIfNull(combinedPasses);
        var all = dynamicPasses.Concat(combinedPasses).ToArray();
        return BuildCandidateUuidQueryCore(command, all, projectionSchemaVersion,
            projectionFingerprintSha256, maxParameters, dynamicPasses.Count);
    }

    private static string BuildCandidateUuidQueryCore(
        DbCommand command,
        IReadOnlyList<BlockingCandidatePassLookup> passes,
        string? projectionSchemaVersion,
        string? projectionFingerprintSha256,
        int maxParameters,
        int? dynamicPassCount)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParameters);
        PersonResolutionProjectionContract.ValidateSupported(projectionSchemaVersion, projectionFingerprintSha256);

        if (passes.Count == 0)
            return dynamicPassCount is null
                ? "SELECT pessoa_uuid FROM identidade.blocking_chave WHERE 1=0"
                : "SELECT pessoa_uuid, CAST(0 AS int) AS in_dynamic, " +
                  "CAST(0 AS int) AS in_combined FROM identidade.blocking_chave WHERE 1=0";

        var projectionBound = projectionSchemaVersion is not null;
        var requiredParameters = 1 + (projectionBound ? 2 : 0) + passes.Sum(static pass =>
            pass.Clauses.Sum(static clause => 1 + clause.Values.Count));
        if (requiredParameters > maxParameters)
        {
            throw new InvalidOperationException(
                $"Plano de blocking exige {requiredParameters} parâmetros, acima do limite seguro {maxParameters}; " +
                "o run foi recusado sem truncar valores ou passes.");
        }

        Add(command, "@blocking_normalization", DbType.String, IdentityComparison.NormalizationVersion, 80);
        var projectionFilter = string.Empty;
        if (projectionBound)
        {
            Add(command, "@blocking_projection_schema", DbType.String, projectionSchemaVersion!, 120);
            Add(command, "@blocking_projection_fingerprint", DbType.AnsiStringFixedLength, projectionFingerprintSha256!.ToLowerInvariant(), 64);
            projectionFilter =
                " AND projection_schema_version=@blocking_projection_schema" +
                " AND projection_fingerprint_sha256=@blocking_projection_fingerprint";
        }

        var passQueries = new List<string>(passes.Count);

        for (var passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            var pass = passes[passIndex];
            if (pass.Clauses.Count == 0)
                continue;

            var clauseQueries = new List<string>(pass.Clauses.Count);
            for (var clauseIndex = 0; clauseIndex < pass.Clauses.Count; clauseIndex++)
            {
                var clause = pass.Clauses[clauseIndex];
                if (clause.Values.Count == 0)
                    throw new InvalidOperationException($"Blocking pass {pass.PassId} contém cláusula sem valores.");

                var featureParameter = $"@bp_{passIndex}_{clauseIndex}_feature";
                Add(command, featureParameter, DbType.String, clause.Feature, 80);

                var valueParameters = new List<string>(clause.Values.Count);
                for (var valueIndex = 0; valueIndex < clause.Values.Count; valueIndex++)
                {
                    var valueParameter = $"@bp_{passIndex}_{clauseIndex}_v{valueIndex}";
                    Add(command, valueParameter, DbType.String, clause.Values[valueIndex], 500);
                    valueParameters.Add(valueParameter);
                }

                var currentOnly = BlockingFeatureTemporalCatalog.Get(clause.Feature)
                    == BlockingFeatureTemporalSemantics.StableIdentityDatum
                    ? " AND vigencia_fim IS NULL"
                    : string.Empty;

                clauseQueries.Add(
                    "SELECT pessoa_uuid FROM identidade.blocking_chave " +
                    $"WHERE normalizacao_versao=@blocking_normalization{projectionFilter} AND atributo={featureParameter} " +
                    $"AND valor_normalizado IN ({string.Join(",", valueParameters)}){currentOnly}");
            }

            var passSource = $"({string.Join(" INTERSECT ", clauseQueries)}) AS pass_{passIndex}";
            passQueries.Add(dynamicPassCount is null
                ? $"SELECT pessoa_uuid FROM {passSource}"
                : $"SELECT pessoa_uuid, " +
                  $"CAST({(passIndex < dynamicPassCount.Value ? 1 : 0)} AS int) AS in_dynamic, " +
                  $"CAST({(passIndex >= dynamicPassCount.Value ? 1 : 0)} AS int) AS in_combined " +
                  $"FROM {passSource}");
        }

        return passQueries.Count == 0
            ? dynamicPassCount is null
                ? "SELECT pessoa_uuid FROM identidade.blocking_chave WHERE 1=0"
                : "SELECT pessoa_uuid, CAST(0 AS int) AS in_dynamic, " +
                  "CAST(0 AS int) AS in_combined FROM identidade.blocking_chave WHERE 1=0"
            : string.Join(dynamicPassCount is null ? " UNION " : " UNION ALL ", passQueries);
    }

    private static void Add(DbCommand command, string name, DbType type, object value, int size)
    {
        if (command.Parameters.Contains(name))
            return;

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Size = size;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
