using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Jornada.Api;

/// <summary>
/// DT-15 development-only read model: no decisions, thresholds, candidate identities,
/// mutations or access to personal data. A LIVE active/draft snapshot is rechecked
/// before returning anything; a training-only delta is never a promotion dossier.
/// </summary>
internal sealed class ModelGovernanceReadOnlyService(IOperationalSqlAdapter sql)
{
    internal const string PartialMethod = "DT15_BLOCKING_TRAINING_PAIR_V1";

    public async Task<ModelGovernanceView> GetAsync(CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        var activeRows = await ReadModelsAsync(connection, "ATIVO", 2, ct);
        if (activeRows.Count > 1)
            throw new InvalidOperationException("DT-15: múltiplos modelos ATIVOS; leitura recusada.");
        var draftRows = await ReadModelsAsync(connection, "RASCUNHO", 1, ct);
        var active = activeRows.SingleOrDefault();
        var draft = draftRows.SingleOrDefault();

        var passes = await ReadPassesAsync(connection, active?.ModelId, draft?.ModelId, ct);
        if (active is not null) active = active with { Passes = passes.GetValueOrDefault(active.ModelId, []) };
        if (draft is not null) draft = draft with { Passes = passes.GetValueOrDefault(draft.ModelId, []) };

        var evidence = draft is null ? null
            : EvaluateEvidence(await ReadDraftStatisticsAsync(connection, draft.ModelId, ct), active);
        var history = await ReadRecentHistoryAsync(connection, ct);

        // Fail closed if another calibration/promotion changes either side while
        // the independent SQL reads above were being executed.
        await using (var check = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO'),
                (SELECT TOP(1) modelo_id FROM identidade.modelo_linkage
                  WHERE status=N'ATIVO' ORDER BY versao DESC),
                (SELECT TOP(1) modelo_id FROM identidade.modelo_linkage
                  WHERE status=N'RASCUNHO' ORDER BY versao DESC);
            """, connection) { CommandTimeout = 10 })
        await using (var reader = await check.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)
                || reader.GetInt64(0) > 1
                || (reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1)) != active?.ModelId
                || (reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2)) != draft?.ModelId)
                throw new InvalidOperationException(
                    "DT-15: o modelo ATIVO ou RASCUNHO mudou durante a leitura. Recarregue a comparação.");
        }

        return new ModelGovernanceView(
            "DT15_MASTER_READONLY_DEV_V1", "READ_ONLY_DEVELOPMENT",
            "NAO_HABILITADAS_SEM_IDP_LEDGER_DOSSIE_COMPLETO",
            active, draft, evidence, history,
            "As métricas disponíveis comparam apenas passes de blocking sobre o treino rotulado. " +
            "Não incluem replay de Fellegi-Sunter, custos SQL pareados nem aprovação humana.");
    }

    private static async Task<List<GovernanceModel>> ReadModelsAsync(
        SqlConnection connection, string status, int max, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (@max) m.modelo_id,m.versao,m.status,m.gerado_em,m.ativado_em,
                   r.ruleset_versao,r.fingerprint_sha256,r.projection_fingerprint_sha256
            FROM identidade.modelo_linkage m
            LEFT JOIN identidade.linkage_ruleset r ON r.modelo_id=m.modelo_id
            WHERE m.status=@status
            ORDER BY m.versao DESC;
            """, connection) { CommandTimeout = 10 };
        command.Parameters.Add("@max", SqlDbType.Int).Value = max;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = status;
        var output = new List<GovernanceModel>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            output.Add(new GovernanceModel(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                reader.IsDBNull(7) ? null : reader.GetString(7).Trim(), []));
        }
        return output;
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<GovernancePass>>> ReadPassesAsync(
        SqlConnection connection, Guid? active, Guid? draft, CancellationToken ct)
    {
        var output = new Dictionary<Guid, IReadOnlyList<GovernancePass>>();
        if (active is null && draft is null) return output;
        await using var command = new SqlCommand("""
            SELECT r.modelo_id,p.passe_ordem,p.passe_id,c.campo_ordem,c.atributo
            FROM identidade.linkage_ruleset r
            JOIN identidade.linkage_ruleset_passe p ON p.ruleset_id=r.ruleset_id
            LEFT JOIN identidade.linkage_ruleset_passe_campo c
              ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
            WHERE r.modelo_id=@active OR r.modelo_id=@draft
            ORDER BY r.modelo_id,p.passe_ordem,c.campo_ordem;
            """, connection) { CommandTimeout = 10 };
        command.Parameters.Add("@active", SqlDbType.UniqueIdentifier).Value = active ?? Guid.Empty;
        command.Parameters.Add("@draft", SqlDbType.UniqueIdentifier).Value = draft ?? Guid.Empty;
        var rows = new Dictionary<Guid, SortedDictionary<int, (string PassId, List<string> Fields)>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var model = reader.GetGuid(0);
            var order = reader.GetInt32(1);
            var passId = reader.GetString(2);
            if (!rows.TryGetValue(model, out var modelPasses))
                rows.Add(model, modelPasses = new());
            if (!modelPasses.TryGetValue(order, out var pass))
                modelPasses.Add(order, pass = (passId, []));
            else if (!string.Equals(pass.PassId, passId, StringComparison.Ordinal))
                throw new InvalidDataException("Passes duplicados com identificadores divergentes.");
            if (!reader.IsDBNull(4))
                pass.Fields.Add(reader.GetString(4));
        }
        foreach (var (id, modelPasses) in rows)
            output.Add(id, modelPasses.Select(x => new GovernancePass(
                x.Value.PassId, x.Value.Fields.ToArray())).ToArray());
        return output;
    }

    private static async Task<Dictionary<string, (decimal Value, string Method)>> ReadDraftStatisticsAsync(
        SqlConnection connection, Guid draft, CancellationToken ct)
    {
        var output = new Dictionary<string, (decimal, string)>(StringComparer.Ordinal);
        await using var command = new SqlCommand("""
            SELECT nome,valor,metodo
            FROM identidade.estatistica_linkage
            WHERE modelo_id=@model AND nome LIKE N'DT15[_]BLOCKING[_]%';
            """, connection) { CommandTimeout = 10 };
        command.Parameters.Add("@model", SqlDbType.UniqueIdentifier).Value = draft;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (!output.TryAdd(reader.GetString(0),
                (reader.GetDecimal(1), reader.GetString(2).Trim())))
                throw new InvalidDataException("Métrica DT-15 duplicada no RASCUNHO.");
        return output;
    }

    internal static GovernanceBlockingEvidence EvaluateEvidence(
        IReadOnlyDictionary<string, (decimal Value, string Method)> stats,
        GovernanceModel? active)
    {
        if (stats.Count == 0)
            return new("AGUARDA_EVIDENCIA", null, null, null, null, null, null, null,
                "RASCUNHO sem diagnóstico pareado persistido.");
        var methods = stats.Values.Select(static x => x.Method).Distinct(StringComparer.Ordinal).ToArray();
        if (methods.Length != 1 || !methods[0].StartsWith(PartialMethod + ":", StringComparison.Ordinal))
            return new("NAO_COMPARAVEL", null, null, null, null, null, null, null,
                "Método de comparação inconsistente ou desconhecido.");
        var source = methods[0][(PartialMethod.Length + 1)..];
        var baselineValid = active is null
            ? source == "SEM_ATIVO"
            : string.Equals(source, active.ModelId.ToString("N"), StringComparison.OrdinalIgnoreCase);
        if (!baselineValid)
            return new("EVIDENCIA_OBSOLETA", null, null, null, null, null, null, null,
                "O modelo ATIVO atual não corresponde ao ATIVO-base do RASCUNHO.");

        decimal? Metric(string name) => stats.TryGetValue(name, out var v) ? v.Value : null;
        var statusCode = Metric("DT15_BLOCKING_PAIR_STATUS_CODE");
        var comparable = Metric("DT15_BLOCKING_PAIR_COMPARABLE");
        var m = Metric("DT15_BLOCKING_PAIR_M_WEIGHT");
        var u = Metric("DT15_BLOCKING_PAIR_U_WEIGHT");
        var draftRecall = Metric("DT15_BLOCKING_DRAFT_RECALL");
        var draftReduction = Metric("DT15_BLOCKING_DRAFT_REDUCTION");
        if (statusCode is null || comparable is null || m is null || m <= 0m || u is null || u <= 0m
            || draftRecall is null || draftReduction is null)
            return new("INCOMPLETO", null, null, null, null, null, null, null,
                "Faltam denominadores ou métricas mínimas do diagnóstico.");

        if (statusCode != 1m || comparable != 1m)
            return new("NAO_COMPARAVEL", m, u, draftRecall, draftReduction, null, null, null,
                "Baseline ausente ou incompatível (código " + statusCode + ").");

        var baselineVersion = Metric("DT15_BLOCKING_BASE_VERSION");
        var activeRecall = Metric("DT15_BLOCKING_ACTIVE_RECALL");
        var activeReduction = Metric("DT15_BLOCKING_ACTIVE_REDUCTION");
        if (active is null || baselineVersion != active.Version ||
            activeRecall is null || activeReduction is null
            || Metric("DT15_BLOCKING_RECALL_DELTA") is null
            || Metric("DT15_BLOCKING_REDUCTION_DELTA") is null)
            return new("INCOMPLETO", m, u, draftRecall, draftReduction, null, null, null,
                "Métricas pareadas insuficientes ou versão do ATIVO-base divergente.");

        var recallDelta = Metric("DT15_BLOCKING_RECALL_DELTA")!.Value;
        var reductionDelta = Metric("DT15_BLOCKING_REDUCTION_DELTA")!.Value;
        if (Math.Abs((draftRecall.Value - activeRecall.Value) - recallDelta) > 0.000003m
            || Math.Abs((draftReduction.Value - activeReduction.Value) - reductionDelta) > 0.000003m)
            return new("NAO_COMPARAVEL", m, u, draftRecall, draftReduction, null, null, null,
                "Deltas persistidos não correspondem aos indicadores do mesmo treino.");

        return new("COMPARAVEL_APENAS_BLOCKING_TREINO",
            m, u, draftRecall, draftReduction, activeRecall, activeReduction,
            new GovernanceDelta(recallDelta, reductionDelta),
            "Evidência parcial sobre o mesmo treino; não constitui dossiê FS nem autorização.");
    }

    private static async Task<IReadOnlyList<GovernanceEvent>> ReadRecentHistoryAsync(
        SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (8) modelo_versao,operacao_codigo,status_novo,ocorrido_em
            FROM auditoria.modelo_linkage_estado_evento
            ORDER BY modelo_linkage_estado_evento_id DESC;
            """, connection) { CommandTimeout = 10 };
        var output = new List<GovernanceEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            output.Add(new GovernanceEvent(reader.GetInt32(0), reader.GetString(1),
                reader.GetString(2), reader.GetValue(3).ToString() ?? ""));
        return output;
    }
}

internal sealed record ModelGovernanceView(
    string MethodVersion, string AccessMode, string Actions,
    GovernanceModel? Active, GovernanceModel? Draft,
    GovernanceBlockingEvidence? Comparison,
    IReadOnlyList<GovernanceEvent> RecentHistory, string Limitation);
internal sealed record GovernanceModel(
    Guid ModelId, int Version, string Status, string? GeneratedAt,
    string? ActivatedAt, string? RuleSetVersion,
    string? RuleSetFingerprint, string? ProjectionFingerprint,
    IReadOnlyList<GovernancePass> Passes);
internal sealed record GovernancePass(string PassId, IReadOnlyList<string> Fields);
internal sealed record GovernanceDelta(decimal Recall, decimal Reduction);
internal sealed record GovernanceBlockingEvidence(
    string Status, decimal? MatchedPairWeight, decimal? NonMatchedPairWeight,
    decimal? DraftRecall, decimal? DraftReduction, decimal? ActiveRecall,
    decimal? ActiveReduction, GovernanceDelta? Delta, string Explanation);
internal sealed record GovernanceEvent(int ModelVersion, string Operation, string NewStatus, string At);
