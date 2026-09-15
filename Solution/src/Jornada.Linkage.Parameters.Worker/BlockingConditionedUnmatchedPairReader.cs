using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Amostra não-vínculos diretamente do universo que sobreviveria ao ruleset vencedor.
/// Cada passe é materializado como uma assinatura determinística das suas chaves canônicas;
/// Pessoas distintas com a mesma assinatura formam pares candidatos. Isso evita inferir
/// a interseção de um passe a partir de pares previamente amostrados por apenas uma chave.
/// </summary>
public static class BlockingConditionedUnmatchedPairReader
{
    public static async Task<IReadOnlyList<IdentityTrainingPair>> ReadAsync(
        SqlConnection connection,
        string normalizationVersion,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        int sampleSize,
        int samplePoolSize,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));
        if (sampleSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleSize));
        if (samplePoolSize < sampleSize)
            throw new ArgumentOutOfRangeException(nameof(samplePoolSize));

        var canonicalPasses = passes
            .Select(pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();
        var allowed = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates.ToHashSet(StringComparer.Ordinal);
        var allFields = canonicalPasses
            .SelectMany(pass => pass.Fields)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
        if (allFields.Any(field => !allowed.Contains(field)))
            throw new InvalidOperationException("Ruleset contém feature fora do catálogo canônico do calibrador.");

        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var ctes = new List<string>();
        var pairSources = new List<string>();
        var command = new SqlCommand { Connection = connection, CommandTimeout = Math.Max(30, commandTimeoutSeconds) };

        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;
        command.Parameters.Add("@pool_size", SqlDbType.Int).Value = samplePoolSize;
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = projection.SchemaVersion;
        command.Parameters.Add("@projection_fingerprint", SqlDbType.Char, 64).Value = projection.Fingerprint;

        var allFieldParameters = new List<string>();
        for (var index = 0; index < allFields.Length; index++)
        {
            var parameterName = $"@all_field_{index}";
            allFieldParameters.Add(parameterName);
            command.Parameters.Add(parameterName, SqlDbType.NVarChar, 80).Value = allFields[index];
        }

        for (var passIndex = 0; passIndex < canonicalPasses.Length; passIndex++)
        {
            var pass = canonicalPasses[passIndex];
            var aliases = new List<string>();
            for (var fieldIndex = 0; fieldIndex < pass.Fields.Count; fieldIndex++)
            {
                var parameterName = $"@p{passIndex}_f{fieldIndex}";
                command.Parameters.Add(parameterName, SqlDbType.NVarChar, 80).Value = pass.Fields[fieldIndex];
                aliases.Add(parameterName);
            }

            var joinClauses = new List<string>();
            var valueParts = new List<string>();
            for (var fieldIndex = 0; fieldIndex < aliases.Count; fieldIndex++)
            {
                var alias = $"k{fieldIndex}";
                if (fieldIndex == 0)
                {
                    valueParts.Add($"{alias}.valor_normalizado");
                    continue;
                }

                joinClauses.Add(
                    $"JOIN eligible_keys {alias} ON {alias}.pessoa_uuid=k0.pessoa_uuid AND {alias}.atributo={aliases[fieldIndex]}");
                valueParts.Add($"{alias}.valor_normalizado");
            }

            var signatureExpression = valueParts.Count == 1
                ? "HASHBYTES('SHA2_256',CONVERT(varbinary(max),k0.valor_normalizado))"
                : $"HASHBYTES('SHA2_256',CONVERT(varbinary(max),CONCAT({string.Join(",NCHAR(31),", valueParts)})))";
            var keysCte = $"pass_{passIndex}_keys";
            var rankedCte = $"pass_{passIndex}_ranked";
            var pairsCte = $"pass_{passIndex}_pairs";

            ctes.Add($"""
{keysCte} AS (
    SELECT DISTINCT k0.pessoa_uuid,{signatureExpression} AS assinatura
    FROM eligible_keys k0
    {string.Join("\n    ", joinClauses)}
    WHERE k0.atributo={aliases[0]}
)
""");
            ctes.Add($"""
{rankedCte} AS (
    SELECT pessoa_uuid,assinatura,
           ROW_NUMBER() OVER (
               PARTITION BY assinatura
               ORDER BY HASHBYTES('SHA2_256',CONVERT(nvarchar(36),pessoa_uuid)),pessoa_uuid) AS rn
    FROM {keysCte}
)
""");
            ctes.Add($"""
{pairsCte} AS (
    SELECT a.pessoa_uuid AS a_uuid,b.pessoa_uuid AS b_uuid
    FROM {rankedCte} a
    JOIN {rankedCte} b ON b.assinatura=a.assinatura AND b.rn=a.rn+1
    WHERE a.rn % 2=1 AND a.pessoa_uuid<>b.pessoa_uuid
)
""");
            pairSources.Add($"SELECT a_uuid,b_uuid FROM {pairsCte}");
        }

        command.CommandText = $"""
WITH gold_sample AS (
    SELECT TOP (@pool_size)
        pessoa_uuid,nome_completo,data_nascimento,nome_mae
    FROM gold.pessoa
    ORDER BY pessoa_uuid
), eligible_keys AS (
    SELECT DISTINCT
        k.pessoa_uuid,k.atributo,k.valor_normalizado
    FROM identidade.blocking_chave k
    JOIN gold_sample gs ON gs.pessoa_uuid=k.pessoa_uuid
    WHERE k.normalizacao_versao=@normalizacao
      AND k.projection_schema_version=@projection_schema
      AND k.projection_fingerprint_sha256=@projection_fingerprint
      AND k.atributo IN ({string.Join(",", allFieldParameters)})
      AND (k.semantica_temporal<>'STABLE_IDENTITY_DATUM' OR k.vigencia_fim IS NULL)
),
{string.Join(",\n", ctes)},
candidate_pairs AS (
    SELECT DISTINCT a_uuid,b_uuid
    FROM (
        {string.Join("\n        UNION ALL\n        ", pairSources)}
    ) p
)
SELECT TOP (@sample_size)
    a.nome_completo,a.data_nascimento,a.nome_mae,
    b.nome_completo,b.data_nascimento,b.nome_mae
FROM candidate_pairs p
JOIN gold_sample a ON a.pessoa_uuid=p.a_uuid
JOIN gold_sample b ON b.pessoa_uuid=p.b_uuid
ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),p.a_uuid),':',CONVERT(nvarchar(36),p.b_uuid))),p.a_uuid,p.b_uuid;
""";

        var result = new List<IdentityTrainingPair>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new IdentityTrainingPair(
                reader.GetString(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        var verified = BlockingConditionedTrainingPairFilter.Retain(result, canonicalPasses);
        if (verified.Count != result.Count)
            throw new InvalidOperationException(
                $"Invariante violada: {result.Count - verified.Count} pares u amostrados não sobreviveram ao ruleset consultado.");
        return result;
    }
}
