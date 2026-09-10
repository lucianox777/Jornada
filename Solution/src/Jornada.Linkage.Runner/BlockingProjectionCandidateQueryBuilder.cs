using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Constrói, de forma provider-neutral e totalmente parametrizada, a consulta de UUIDs candidatos
/// sobre identidade.blocking_chave. Cada passe usa INTERSECT entre seus atributos e os passes
/// são combinados por UNION. Aliases de nome históricos permanecem elegíveis; dados estáveis
/// usam somente a chave corrente.
/// </summary>
public static class BlockingProjectionCandidateQueryBuilder
{
    public const string MethodVersion = "BLOCKING_PROJECTION_CANDIDATE_QUERY_V1";

    public static string BuildCandidateUuidQuery(
        DbCommand command,
        IReadOnlyList<BlockingCandidatePassLookup> passes)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(passes);

        if (passes.Count == 0)
            return "SELECT pessoa_uuid FROM identidade.blocking_chave WHERE 1=0";

        Add(command, "@blocking_normalization", DbType.String, IdentityComparison.NormalizationVersion, 80);
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
                    $"WHERE normalizacao_versao=@blocking_normalization AND atributo={featureParameter} " +
                    $"AND valor_normalizado IN ({string.Join(",", valueParameters)}){currentOnly}");
            }

            passQueries.Add(
                $"SELECT pessoa_uuid FROM ({string.Join(" INTERSECT ", clauseQueries)}) AS pass_{passIndex}");
        }

        return passQueries.Count == 0
            ? "SELECT pessoa_uuid FROM identidade.blocking_chave WHERE 1=0"
            : string.Join(" UNION ", passQueries);
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
