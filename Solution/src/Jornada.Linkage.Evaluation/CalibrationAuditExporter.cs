using Jornada.Linkage.Parameters.Worker;
using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public sealed class CalibrationAuditExporter(SqlConnection connection, int commandTimeoutSeconds)
{
    /// <summary>
    /// Extensão de referência pública do exportador EXISTENTE. Não lê Gold,
    /// Silver nem tabela de observações. O banco deve ser o Development
    /// JornadaSyntheticDev e a referência ativa deve ser o snapshot IBGE esperado.
    /// A saída é replay sintético, NÃO documento de auditoria de modelo real.
    /// </summary>
    public async Task<SplinkIbgeReplayDocument> ExportIbgeSyntheticReplayAsync(
        int seed, int pairCount, string firstNameSex, CancellationToken ct = default)
    {
        if (firstNameSex is not ("TODOS" or "FEMININO"))
            throw new ArgumentOutOfRangeException(nameof(firstNameSex));
        if (pairCount is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(pairCount));

        await using (var profile = new SqlCommand(
            """
            SELECT DB_NAME(),
                   CONVERT(nvarchar(32),
                     (SELECT value FROM sys.extended_properties
                      WHERE class=0 AND name=N'Jornada.EnvironmentProfile'));
            """, connection) { CommandTimeout = commandTimeoutSeconds })
        await using (var reader = await profile.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) ||
                !string.Equals(reader.GetString(0), "JornadaSyntheticDev", StringComparison.Ordinal) ||
                reader.IsDBNull(1) ||
                !string.Equals(reader.GetString(1), "Development", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Replay Splink exige banco JornadaSyntheticDev e Jornada.EnvironmentProfile=Development.");
        }

        var reference = await IbgeNominalUReferenceReader.ReadActiveReferenceAsync(connection, ct);
        if (reference.Code != "CENSO2022_NOMES_BRASIL_V1" ||
            reference.ContentSha256.Length != 64)
            throw new InvalidDataException("Replay exige snapshot público IBGE CENSO2022_NOMES_BRASIL_V1.");
        var published = await IbgeNominalUReferenceReader.ReadBrazilPublishedMarginalsAsync(
            connection, reference.Id, firstNameSex, ct);
        var opts = new IbgeNominalUBootstrapOptions(seed, pairCount);
        var replay = IbgeNominalUBootstrapEstimator.ReplayPairs(published, opts);
        var estimate = IbgeNominalUBootstrapEstimator.Estimate(published, opts);
        if (estimate.States.Any(state =>
                replay.Count(pair => pair.CSharpState == state.State) != state.Support))
            throw new InvalidDataException(
                "Replay divergiu dos suportes do estimador IBGE: exportação recusada.");

        var document = new SplinkIbgeReplayDocument(
            SplinkIbgeReplayContract.InputSchema,
            reference.Code, reference.ContentSha256,
            firstNameSex, "TODOS",
            estimate.MethodVersion, estimate.JointConstructionVersion,
            estimate.ObservationChannelVersion,
            SplinkIbgeReplayContract.ComparisonV1,
            seed, pairCount,
            estimate.FirstNamePublishedOccurrences,
            estimate.SurnamePublishedOccurrences,
            estimate.AnalyticExactSyntheticFullNameProbability,
            replay.Select(x => new SplinkIbgeReplayPair(
                x.Index, x.LeftName, x.RightName, x.CSharpState)).ToArray());
        SplinkIbgeReplayContract.ValidateInput(document);
        return document;
    }

    public async Task<LinkageCalibrationAuditDocument> ExportAsync(
        Guid? requestedModelId,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadModelAsync(requestedModelId, cancellationToken)
            ?? throw new InvalidOperationException(requestedModelId is null
                ? "Não há modelo de linkage ATIVO para exportação."
                : $"Modelo de linkage {requestedModelId} não encontrado.");

        LinkageCalibrationAuditExchangePolicy.EnsureExportableModelStatus(model.ModelId, model.Status);

        var parameters = await LoadParametersAsync(model.ModelId, cancellationToken);
        var statistics = await LoadStatisticsAsync(model.ModelId, cancellationToken);
        var rulesets = await LoadRuleSetsAsync(model.ModelId, cancellationToken);
        var passes = await LoadPassesAsync(model.ModelId, cancellationToken);
        var frequencyVersion = await LoadFrequencyVersionAsync(model.NameFrequencyVersionId, cancellationToken);
        var frequencyCoverage = await LoadFrequencyCoverageAsync(model.NameFrequencyVersionId, cancellationToken);
        var persistedTfRows = await ScalarLongAsync(
            "SELECT COUNT_BIG(*) FROM identidade.frequencia_linkage WHERE modelo_id=@model_id;",
            model.ModelId,
            cancellationToken);

        return new LinkageCalibrationAuditDocument(
            SchemaVersion: LinkageCalibrationAuditExchangePolicy.SchemaVersion,
            Nature: LinkageCalibrationAuditExchangePolicy.Nature,
            Purpose: LinkageCalibrationAuditExchangePolicy.Purpose,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Safeguards:
            [
                "read-only SELECTs only",
                "does not create or activate a model",
                "does not create linkage_run",
                "does not write identity_map/vinculo_fonte or Gold",
                "does not enable term frequency in the operational scorer"
            ],
            Model: model,
            Parameters: parameters,
            Statistics: statistics,
            InterchangeContract: new LinkageCalibrationAuditInterchangeContract(
                StatusAtExport: model.Status,
                UProbabilitySemantics: LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                NominalNameUSource: LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(
                    parameters, motherName: false),
                NominalMotherNameUSource: LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(
                    parameters, motherName: true),
                SplinkDefaultRandomPairUEquivalent: false,
                ComparisonStateMapping: new LinkageCalibrationAuditComparisonMapping(
                    Complete: false,
                    UnmappedOrNonBijectiveStates:
                        LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates,
                    Rule: LinkageCalibrationAuditExchangePolicy.ComparisonStateMappingRule)),
            Blocking: new LinkageCalibrationAuditBlocking(rulesets, passes),
            TermFrequency: new LinkageCalibrationAuditTermFrequency(
                RuntimeEnabled: false,
                AlgorithmVersion: SplinkCompatibleTermFrequency.AlgorithmVersion,
                PersistedModelFrequencyRows: persistedTfRows,
                ReferenceSnapshot: frequencyVersion,
                ReferenceCoverage: frequencyCoverage,
                ConformanceVectors: TermFrequencyConformanceVectors(),
                Interpretation:
                    "Vetores sintéticos permitem conferir a matemática TF sem afirmar que TF está habilitada no scorer. A ativação continua condicionada à calibração/homologação da issue #31."));
    }

    private async Task<LinkageCalibrationAuditModel?> LoadModelAsync(Guid? requestedModelId, CancellationToken ct)
    {
        var sql = requestedModelId is null
            ? """
              SELECT TOP(1)
                     modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                     deduplicacao_metodo,base_referencia,snapshot_referencia,
                     registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                     snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                     amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                     frequencia_nome_versao_id
              FROM identidade.modelo_linkage
              WHERE status=N'ATIVO'
              ORDER BY versao DESC;
              """
            : """
              SELECT TOP(1)
                     modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                     deduplicacao_metodo,base_referencia,snapshot_referencia,
                     registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                     snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                     amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                     frequencia_nome_versao_id
              FROM identidade.modelo_linkage
              WHERE modelo_id=@model_id;
              """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = commandTimeoutSeconds };
        if (requestedModelId is Guid id)
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = id;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new LinkageCalibrationAuditModel(
            ModelId: reader.GetGuid(0),
            Version: reader.GetInt32(1),
            Status: reader.GetString(2),
            AlgorithmVersion: reader.GetString(3),
            NormalizationVersion: reader.GetString(4),
            DeduplicationMethod: reader.GetString(5),
            BaseReference: reader.GetString(6),
            SnapshotReference: reader.IsDBNull(7) ? null : reader.GetString(7),
            RecordsRead: reader.IsDBNull(8) ? null : reader.GetInt64(8),
            UniquePeople: reader.IsDBNull(9) ? null : reader.GetInt64(9),
            GeneratedAt: reader.GetFieldValue<DateTimeOffset>(10),
            ActivatedAt: reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            SnapshotCapturedAt: reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            SampleMethod: reader.IsDBNull(13) ? null : reader.GetString(13),
            SamplePoolSize: reader.IsDBNull(14) ? null : reader.GetInt32(14),
            SampleMSize: reader.IsDBNull(15) ? null : reader.GetInt32(15),
            SampleUSize: reader.IsDBNull(16) ? null : reader.GetInt32(16),
            FailureSummary: reader.IsDBNull(17) ? null : reader.GetString(17),
            NameFrequencyVersionId: reader.IsDBNull(18) ? null : reader.GetInt64(18));
    }

    private async Task<IReadOnlyList<LinkageCalibrationAuditParameter>> LoadParametersAsync(
        Guid modelId,
        CancellationToken ct)
    {
        await using var command = ModelCommand(
            "SELECT nome,valor FROM identidade.parametro_linkage WHERE modelo_id=@model_id ORDER BY nome;",
            modelId);
        var rows = new List<LinkageCalibrationAuditParameter>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetDecimal(1)));
        return rows;
    }

    private async Task<IReadOnlyList<LinkageCalibrationAuditStatistic>> LoadStatisticsAsync(
        Guid modelId,
        CancellationToken ct)
    {
        await using var command = ModelCommand(
            "SELECT nome,valor,metodo FROM identidade.estatistica_linkage WHERE modelo_id=@model_id ORDER BY nome;",
            modelId);
        var rows = new List<LinkageCalibrationAuditStatistic>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetDecimal(1), reader.GetString(2)));
        return rows;
    }

    private async Task<IReadOnlyList<LinkageCalibrationAuditRuleSet>> LoadRuleSetsAsync(
        Guid modelId,
        CancellationToken ct)
    {
        await using var command = ModelCommand(
            """
            SELECT ruleset_id,ruleset_versao,algoritmo_versao,fingerprint_sha256,
                   ibge_source_versao,ibge_fingerprint_sha256,criado_em
            FROM identidade.linkage_ruleset
            WHERE modelo_id=@model_id
            ORDER BY criado_em,ruleset_id;
            """,
            modelId);
        var rows = new List<LinkageCalibrationAuditRuleSet>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new(
                RuleSetId: reader.GetGuid(0),
                RuleSetVersion: reader.GetString(1),
                AlgorithmVersion: reader.GetString(2),
                FingerprintSha256: reader.GetString(3),
                IbgeSourceVersion: reader.IsDBNull(4) ? null : reader.GetString(4),
                IbgeFingerprintSha256: reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return rows;
    }

    private async Task<IReadOnlyList<LinkageCalibrationAuditPass>> LoadPassesAsync(
        Guid modelId,
        CancellationToken ct)
    {
        await using var command = ModelCommand(
            """
            SELECT p.ruleset_id,p.passe_ordem,p.passe_id,
                   STRING_AGG(c.atributo,',') WITHIN GROUP (ORDER BY c.campo_ordem) atributos
            FROM identidade.linkage_ruleset r
            JOIN identidade.linkage_ruleset_passe p ON p.ruleset_id=r.ruleset_id
            LEFT JOIN identidade.linkage_ruleset_passe_campo c
              ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
            WHERE r.modelo_id=@model_id
            GROUP BY p.ruleset_id,p.passe_ordem,p.passe_id
            ORDER BY p.passe_ordem;
            """,
            modelId);
        var rows = new List<LinkageCalibrationAuditPass>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var attributes = reader.IsDBNull(3)
                ? Array.Empty<string>()
                : reader.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            rows.Add(new(
                RuleSetId: reader.GetGuid(0),
                Order: reader.GetInt32(1),
                PassId: reader.GetString(2),
                Attributes: attributes));
        }
        return rows;
    }

    private async Task<LinkageCalibrationAuditFrequencyReference?> LoadFrequencyVersionAsync(
        long? versionId,
        CancellationToken ct)
    {
        if (versionId is null)
            return null;

        await using var command = new SqlCommand(
            """
            SELECT frequencia_nome_versao_id,codigo,fonte,edicao,data_referencia,publicado_em,
                   status,
                   CASE WHEN conteudo_sha256 IS NULL THEN NULL ELSE CONVERT(VARCHAR(64),conteudo_sha256,2) END,
                   criado_em,ativado_em
            FROM ref.frequencia_nome_versao
            WHERE frequencia_nome_versao_id=@id;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId.Value;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new LinkageCalibrationAuditFrequencyReference(
            VersionId: reader.GetInt64(0),
            Code: reader.GetString(1),
            Source: reader.GetString(2),
            Edition: reader.GetString(3),
            ReferenceDate: DateOnly.FromDateTime(reader.GetDateTime(4)),
            PublishedAt: reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
            Status: reader.GetString(6),
            ContentSha256: reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(8),
            ActivatedAt: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
    }

    private async Task<IReadOnlyList<LinkageCalibrationAuditFrequencyCoverage>> LoadFrequencyCoverageAsync(
        long? versionId,
        CancellationToken ct)
    {
        if (versionId is null)
            return [];

        await using var command = new SqlCommand(
            """
            SELECT tipo,escopo_geografico,sexo,periodo_nascimento,
                   COUNT_BIG(*) registros,
                   SUM(CONVERT(decimal(38,0),frequencia)) soma_frequencia
            FROM ref.frequencia_nome
            WHERE frequencia_nome_versao_id=@id
            GROUP BY tipo,escopo_geografico,sexo,periodo_nascimento
            ORDER BY tipo,escopo_geografico,sexo,periodo_nascimento;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId.Value;

        var rows = new List<LinkageCalibrationAuditFrequencyCoverage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new(
                Type: reader.GetString(0),
                GeographicScope: reader.GetString(1),
                Sex: reader.GetString(2),
                BirthPeriod: reader.GetString(3),
                Records: reader.GetInt64(4),
                FrequencySum: reader.GetDecimal(5)));
        }
        return rows;
    }

    private SqlCommand ModelCommand(string sql, Guid modelId)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
        return command;
    }

    private async Task<long> ScalarLongAsync(string sql, Guid modelId, CancellationToken ct)
    {
        await using var command = ModelCommand(sql, modelId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(ct),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<LinkageCalibrationAuditTfVector> TermFrequencyConformanceVectors() =>
    [
        Vector("common-common", 0.20m, 0.20m, 0.05m, 1m, 0.000001m),
        Vector("rare-rare", 0.001m, 0.001m, 0.05m, 1m, 0.000001m),
        Vector("rare-common-conservative", 0.001m, 0.20m, 0.05m, 1m, 0.000001m),
        Vector("floor-limited", 0.00000001m, 0.00000002m, 0.05m, 1m, 0.000001m),
        Vector("disabled-weight", 0.001m, 0.001m, 0.05m, 0m, 0.000001m)
    ];

    private static LinkageCalibrationAuditTfVector Vector(
        string name,
        decimal left,
        decimal right,
        decimal referenceU,
        decimal weight,
        decimal minimumU)
    {
        var effective = SplinkCompatibleTermFrequency.EffectiveFrequency(left, right, minimumU);
        var adjustment = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            left,
            right,
            referenceU,
            weight,
            minimumU);
        return new(
            Name: name,
            LeftFrequency: left,
            RightFrequency: right,
            ReferenceUProbability: referenceU,
            Weight: weight,
            MinimumUValue: minimumU,
            EffectiveFrequency: effective,
            LogBayesAdjustmentNatural: adjustment);
    }
}
