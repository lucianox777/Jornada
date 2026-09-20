using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

internal sealed class CalibrationAuditExporter(SqlConnection connection, int commandTimeoutSeconds)
{
    public async Task<object> ExportAsync(Guid? requestedModelId, CancellationToken cancellationToken = default)
    {
        var model = await LoadModelAsync(requestedModelId, cancellationToken)
            ?? throw new InvalidOperationException(requestedModelId is null
                ? "Não há modelo de linkage ATIVO para exportação."
                : $"Modelo de linkage {requestedModelId} não encontrado.");

        var modelId = (Guid)model["modelo_id"]!;
        var parameters = await QueryRowsAsync(
            "SELECT nome,valor FROM identidade.parametro_linkage WHERE modelo_id=@model_id ORDER BY nome;",
            modelId, cancellationToken);
        var statistics = await QueryRowsAsync(
            "SELECT nome,valor,metodo FROM identidade.estatistica_linkage WHERE modelo_id=@model_id ORDER BY nome;",
            modelId, cancellationToken);
        var rulesets = await QueryRowsAsync(
            """
            SELECT ruleset_id,ruleset_versao,algoritmo_versao,fingerprint_sha256,
                   projection_schema_version,projection_fingerprint_sha256,criado_em
            FROM identidade.linkage_ruleset
            WHERE modelo_id=@model_id
            ORDER BY criado_em,ruleset_id;
            """, modelId, cancellationToken);
        var passes = await QueryRowsAsync(
            """
            SELECT p.ruleset_id,p.passe_ordem,p.passe_id,p.ativo,
                   STRING_AGG(c.campo,',') WITHIN GROUP (ORDER BY c.campo_ordem) campos
            FROM identidade.linkage_ruleset r
            JOIN identidade.linkage_ruleset_passe p ON p.ruleset_id=r.ruleset_id
            LEFT JOIN identidade.linkage_ruleset_passe_campo c
              ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
            WHERE r.modelo_id=@model_id
            GROUP BY p.ruleset_id,p.passe_ordem,p.passe_id,p.ativo
            ORDER BY p.passe_ordem;
            """, modelId, cancellationToken);

        var frequencyVersion = await LoadFrequencyVersionAsync(model, cancellationToken);
        var frequencyCoverage = await LoadFrequencyCoverageAsync(model, cancellationToken);
        var persistedTfRows = await ScalarLongAsync(
            "SELECT COUNT_BIG(*) FROM identidade.frequencia_linkage WHERE modelo_id=@model_id;",
            modelId, cancellationToken);

        return new
        {
            schemaVersion = 1,
            nature = "LINKAGE_CALIBRATION_AUDIT_EXPORT",
            purpose = "EXTERNAL_REPRODUCIBILITY_READ_ONLY",
            generatedAtUtc = DateTimeOffset.UtcNow,
            safeguards = new[]
            {
                "read-only SELECTs only",
                "does not create or activate a model",
                "does not create linkage_run",
                "does not write identity_map/vinculo_fonte or Gold",
                "does not enable term frequency in the operational scorer"
            },
            model,
            parameters,
            statistics,
            blocking = new { rulesets, passes },
            termFrequency = new
            {
                runtimeEnabled = false,
                algorithmVersion = SplinkCompatibleTermFrequency.AlgorithmVersion,
                persistedModelFrequencyRows = persistedTfRows,
                referenceSnapshot = frequencyVersion,
                referenceCoverage = frequencyCoverage,
                conformanceVectors = TermFrequencyConformanceVectors(),
                interpretation = "Vetores sintéticos permitem conferir a matemática TF sem afirmar que TF está habilitada no scorer. A ativação continua condicionada à calibração/homologação da issue #31."
            }
        };
    }

    private async Task<Dictionary<string, object?>?> LoadModelAsync(Guid? requestedModelId, CancellationToken ct)
    {
        var sql = requestedModelId is null
            ? "SELECT TOP(1) * FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;"
            : "SELECT TOP(1) * FROM identidade.modelo_linkage WHERE modelo_id=@model_id;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = commandTimeoutSeconds };
        if (requestedModelId is Guid id)
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = id;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadRow(reader);
    }

    private async Task<Dictionary<string, object?>?> LoadFrequencyVersionAsync(
        IReadOnlyDictionary<string, object?> model, CancellationToken ct)
    {
        if (!model.TryGetValue("frequencia_nome_versao_id", out var value) || value is null || value is DBNull)
            return null;
        var id = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        await using var command = new SqlCommand(
            """
            SELECT frequencia_nome_versao_id,codigo,fonte,edicao,data_referencia,publicado_em,
                   status,conteudo_sha256,criado_em,ativado_em
            FROM ref.frequencia_nome_versao
            WHERE frequencia_nome_versao_id=@id;
            """, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRow(reader) : null;
    }

    private async Task<IReadOnlyList<Dictionary<string, object?>>> LoadFrequencyCoverageAsync(
        IReadOnlyDictionary<string, object?> model, CancellationToken ct)
    {
        if (!model.TryGetValue("frequencia_nome_versao_id", out var value) || value is null || value is DBNull)
            return [];
        var id = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        await using var command = new SqlCommand(
            """
            SELECT tipo,escopo_geografico,sexo,periodo_nascimento,
                   COUNT_BIG(*) registros,
                   SUM(CONVERT(decimal(38,0),frequencia)) soma_frequencia
            FROM ref.frequencia_nome
            WHERE frequencia_nome_versao_id=@id
            GROUP BY tipo,escopo_geografico,sexo,periodo_nascimento
            ORDER BY tipo,escopo_geografico,sexo,periodo_nascimento;
            """, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        return await ReadRowsAsync(command, ct);
    }

    private async Task<IReadOnlyList<Dictionary<string, object?>>> QueryRowsAsync(
        string sql, Guid modelId, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
        return await ReadRowsAsync(command, ct);
    }

    private async Task<long> ScalarLongAsync(string sql, Guid modelId, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<Dictionary<string, object?>>> ReadRowsAsync(SqlCommand command, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static Dictionary<string, object?> ReadRow(SqlDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        return row;
    }

    private static object[] TermFrequencyConformanceVectors()
    {
        return
        [
            Vector("common-common", 0.20m, 0.20m, 0.05m, 1m, 0.000001m),
            Vector("rare-rare", 0.001m, 0.001m, 0.05m, 1m, 0.000001m),
            Vector("rare-common-conservative", 0.001m, 0.20m, 0.05m, 1m, 0.000001m),
            Vector("floor-limited", 0.00000001m, 0.00000002m, 0.05m, 1m, 0.000001m),
            Vector("disabled-weight", 0.001m, 0.001m, 0.05m, 0m, 0.000001m)
        ];
    }

    private static object Vector(
        string name, decimal left, decimal right, decimal referenceU, decimal weight, decimal minimumU)
    {
        var effective = SplinkCompatibleTermFrequency.EffectiveFrequency(left, right, minimumU);
        var adjustment = SplinkCompatibleTermFrequency.LogBayesAdjustment(left, right, referenceU, weight, minimumU);
        return new
        {
            name,
            leftFrequency = left,
            rightFrequency = right,
            referenceUProbability = referenceU,
            weight,
            minimumUValue = minimumU,
            effectiveFrequency = effective,
            logBayesAdjustmentNatural = adjustment
        };
    }
}
