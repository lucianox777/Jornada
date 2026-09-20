using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingPassNominalUSupport(
    string PassId,
    long SampleSize,
    IReadOnlyDictionary<string, long> NameStateSupport,
    IReadOnlyDictionary<string, long> MotherNameStateSupport);

public sealed record BlockingConditionedUnmatchedPairSample(
    IReadOnlyList<IdentityTrainingPair> Pairs,
    IReadOnlyDictionary<string, long> SemanticBirthPoolSupport,
    long CandidatePoolSize,
    IReadOnlyList<BlockingPassNominalUSupport> PassNominalSupport);

/// <summary>
/// Amostra não-vínculos diretamente do universo que sobreviveria ao ruleset vencedor.
/// Cada passe é materializado como uma assinatura determinística das suas chaves canônicas;
/// Pessoas distintas com a mesma assinatura formam pares candidatos. Além da amostra u,
/// percorre o pool candidato apenas com as datas para medir suporte semântico sem carregar
/// o universo inteiro em memória.
/// </summary>
public static class BlockingConditionedUnmatchedPairReader
{
    public static async Task<BlockingConditionedUnmatchedPairSample> ReadAsync(
        SqlConnection connection,
        string normalizationVersion,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        int sampleSize,
        int samplePoolSize,
        int decisionCalibrationSeed,
        int validationBasisPoints,
        int testBasisPoints,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplePoolSize, sampleSize);

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

        var stableFields = allFields
            .Where(field => BlockingFeatureTemporalCatalog.Get(field) == BlockingFeatureTemporalSemantics.StableIdentityDatum)
            .ToArray();
        var versionedFields = allFields
            .Where(field => BlockingFeatureTemporalCatalog.Get(field) == BlockingFeatureTemporalSemantics.VersionedAlias)
            .ToArray();

        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var ctes = new List<string>();
        var pairSources = new List<string>();
        var command = new SqlCommand { Connection = connection, CommandTimeout = Math.Max(30, commandTimeoutSeconds) };

        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;
        command.Parameters.Add("@pool_size", SqlDbType.Int).Value = samplePoolSize;
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = projection.SchemaVersion;
        command.Parameters.Add("@projection_fingerprint", SqlDbType.Char, 64).Value = projection.Fingerprint;
        command.Parameters.Add("@decision_seed", SqlDbType.Int).Value = decisionCalibrationSeed;
        command.Parameters.Add("@train_cut", SqlDbType.Int).Value = 10_000 - validationBasisPoints - testBasisPoints;

        var allFieldParameters = new List<string>();
        for (var index = 0; index < allFields.Length; index++)
        {
            var parameterName = $"@all_field_{index}";
            allFieldParameters.Add(parameterName);
            command.Parameters.Add(parameterName, SqlDbType.NVarChar, 80).Value = allFields[index];
        }

        var stableFieldParameters = new List<string>();
        for (var index = 0; index < stableFields.Length; index++)
        {
            var parameterName = $"@stable_field_{index}";
            stableFieldParameters.Add(parameterName);
            command.Parameters.Add(parameterName, SqlDbType.NVarChar, 80).Value = stableFields[index];
        }

        var versionedFieldParameters = new List<string>();
        for (var index = 0; index < versionedFields.Length; index++)
        {
            var parameterName = $"@versioned_field_{index}";
            versionedFieldParameters.Add(parameterName);
            command.Parameters.Add(parameterName, SqlDbType.NVarChar, 80).Value = versionedFields[index];
        }

        var temporalSemanticsPredicate = string.Join(
            " OR ",
            new[]
            {
                stableFieldParameters.Count == 0
                    ? null
                    : $"(k.atributo IN ({string.Join(",", stableFieldParameters)}) AND k.semantica_temporal=N'STABLE_IDENTITY_DATUM' AND k.vigencia_fim IS NULL)",
                versionedFieldParameters.Count == 0
                    ? null
                    : $"(k.atributo IN ({string.Join(",", versionedFieldParameters)}) AND k.semantica_temporal=N'VERSIONED_ALIAS')"
            }.Where(static clause => clause is not null));

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
                joinClauses.Add($"JOIN eligible_keys {alias} ON {alias}.pessoa_uuid=k0.pessoa_uuid AND {alias}.atributo={aliases[fieldIndex]}");
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
            var passIdParameter = $"@pass_id_{passIndex}";
            command.Parameters.Add(passIdParameter, SqlDbType.NVarChar, 80).Value = pass.PassId;
            pairSources.Add($"SELECT {passIdParameter} AS pass_id,a_uuid,b_uuid FROM {pairsCte}");
        }

        var commonCtes = $"""
WITH gold_sample AS (
    SELECT TOP (@pool_size)
        g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
    FROM gold.pessoa g
    WHERE g.estado_identidade=N'REFERENCIA'
      AND CONVERT(int,SUBSTRING(HASHBYTES('SHA2_256',CONVERT(varchar(100),CONCAT(@decision_seed,':',LOWER(CONVERT(varchar(36),g.pessoa_uuid))))),1,3)) % 10000 < @train_cut
      AND g.nome_completo IS NOT NULL
      AND g.data_nascimento IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM identidade.vinculo_fonte vf_val
        JOIN silver.pessoa_observacao po_val
          ON po_val.pessoa_observacao_id=vf_val.pessoa_observacao_id
        WHERE vf_val.pessoa_uuid=g.pessoa_uuid
          AND po_val.codigo_pessoa_origem LIKE N'SCALE-VAL-%'
    )
    ORDER BY g.pessoa_uuid
), eligible_keys AS (
    SELECT DISTINCT k.pessoa_uuid,k.atributo,k.valor_normalizado
    FROM identidade.blocking_chave k
    JOIN gold_sample gs ON gs.pessoa_uuid=k.pessoa_uuid
    WHERE k.normalizacao_versao=@normalizacao
      AND k.projection_schema_version=@projection_schema
      AND k.projection_fingerprint_sha256=@projection_fingerprint
      AND k.atributo IN ({string.Join(",", allFieldParameters)})
      AND ({temporalSemanticsPredicate})
),
{string.Join(",\n", ctes)},
pass_candidate_pairs AS (
    {string.Join("\n    UNION ALL\n    ", pairSources)}
)
""";

        command.CommandText = $"""
{commonCtes},
candidate_pairs AS (
    SELECT DISTINCT a_uuid,b_uuid
    FROM pass_candidate_pairs
), ordered_pairs AS (
    SELECT p.a_uuid,p.b_uuid,
           ROW_NUMBER() OVER (
             ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),p.a_uuid),':',CONVERT(nvarchar(36),p.b_uuid))),p.a_uuid,p.b_uuid) AS sample_rank
    FROM candidate_pairs p
)
SELECT
    CASE WHEN p.sample_rank<=@sample_size THEN a.nome_completo END AS a_nome,
    a.data_nascimento AS a_nascimento,
    CASE WHEN p.sample_rank<=@sample_size THEN a.nome_mae END AS a_mae,
    CASE WHEN p.sample_rank<=@sample_size THEN b.nome_completo END AS b_nome,
    b.data_nascimento AS b_nascimento,
    CASE WHEN p.sample_rank<=@sample_size THEN b.nome_mae END AS b_mae,
    p.sample_rank
FROM ordered_pairs p
JOIN gold_sample a ON a.pessoa_uuid=p.a_uuid
JOIN gold_sample b ON b.pessoa_uuid=p.b_uuid
ORDER BY p.sample_rank;

{commonCtes},
pass_ordered_pairs AS (
    SELECT p.pass_id,p.a_uuid,p.b_uuid,
           ROW_NUMBER() OVER (
             PARTITION BY p.pass_id
             ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),p.a_uuid),':',CONVERT(nvarchar(36),p.b_uuid))),p.a_uuid,p.b_uuid) AS pass_sample_rank
    FROM pass_candidate_pairs p
)
SELECT
    p.pass_id,
    a.nome_completo,
    a.nome_mae,
    b.nome_completo,
    b.nome_mae
FROM pass_ordered_pairs p
JOIN gold_sample a ON a.pessoa_uuid=p.a_uuid
JOIN gold_sample b ON b.pessoa_uuid=p.b_uuid
WHERE p.pass_sample_rank<=@sample_size
ORDER BY p.pass_id,p.pass_sample_rank;
""";

        var result = new List<IdentityTrainingPair>();
        var support = BirthDateSemanticEvidence.States.ToDictionary(state => state, _ => 0L, StringComparer.Ordinal);
        long candidatePoolSize = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidatePoolSize++;
            var leftBirth = DateOnly.FromDateTime(reader.GetDateTime(1));
            var rightBirth = DateOnly.FromDateTime(reader.GetDateTime(4));
            support[BirthDateSemanticEvidence.Classify(leftBirth, rightBirth)]++;

            var rank = reader.GetInt64(6);
            if (rank > sampleSize)
                continue;

            result.Add(new IdentityTrainingPair(
                reader.GetString(0),
                leftBirth,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                rightBirth,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        var passSupport = canonicalPasses.ToDictionary(
            static pass => pass.PassId,
            static pass => new PassSupportBuilder(
                pass.PassId,
                Enum.GetNames<NameComparisonState>().ToDictionary(static state => state, static _ => 0L, StringComparer.Ordinal),
                LinkageParameterCatalog.MotherNameStates.ToDictionary(static state => state, static _ => 0L, StringComparer.Ordinal)),
            StringComparer.Ordinal);

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var passId = reader.GetString(0);
                if (!passSupport.TryGetValue(passId, out var builder))
                    throw new InvalidOperationException($"Passe inesperado na amostra u condicionada: {passId}.");

                var nameState = IdentityComparison.CompareName(
                    reader.GetString(1),
                    reader.GetString(3),
                    NameComparisonContract.WholeNameJaroWinklerV1).ToString();
                builder.NameStateSupport[nameState]++;
                builder.SampleSize++;

                var motherState =
                    reader.IsDBNull(2) || reader.IsDBNull(4)
                        ? "MISSING"
                        : IdentityComparison.CompareName(
                            reader.GetString(2),
                            reader.GetString(4),
                            NameComparisonContract.WholeNameJaroWinklerV1).ToString();
                builder.MotherNameStateSupport[motherState]++;
            }
        }

        // candidate_pairs é construído exclusivamente dos CTEs de cada passe sobre
        // blocking_chave com a mesma semântica temporal do Runner. Reprojetar a Gold
        // corrente aqui seria incorreto: VERSIONED_ALIAS preserva valores históricos
        // deliberadamente recuperáveis, enquanto gold.pessoa expõe apenas o estado atual.
        if (candidatePoolSize < result.Count)
            throw new InvalidOperationException(
                "Invariante violada: amostra u excedeu o pool candidato materializado.");

        return new BlockingConditionedUnmatchedPairSample(
            result,
            support,
            candidatePoolSize,
            passSupport.Values
                .OrderBy(static item => item.PassId, StringComparer.Ordinal)
                .Select(static item => new BlockingPassNominalUSupport(
                    item.PassId,
                    item.SampleSize,
                    new Dictionary<string, long>(item.NameStateSupport, StringComparer.Ordinal),
                    new Dictionary<string, long>(item.MotherNameStateSupport, StringComparer.Ordinal)))
                .ToArray());
    }

    private sealed class PassSupportBuilder(
        string passId,
        Dictionary<string, long> nameStateSupport,
        Dictionary<string, long> motherNameStateSupport)
    {
        public string PassId { get; } = passId;
        public long SampleSize { get; set; }
        public Dictionary<string, long> NameStateSupport { get; } = nameStateSupport;
        public Dictionary<string, long> MotherNameStateSupport { get; } = motherNameStateSupport;
    }
}
