using Microsoft.Data.SqlClient;
using System.Data;

namespace Jornada.Api;

/// <summary>
/// DT-15: proveniência de leitura dos três blocos LÓGICOS do modelo vigente.
/// Não é hash de bundle, parecer, autorização, evidência de recarga IBGE
/// ou contrato de publicação. Disponível somente pela API master DEV existente.
/// </summary>
internal static class GovernanceBundleProvenanceReader
{
    internal const string MissingGlobalFingerprint = "SEM_FINGERPRINT_GLOBAL_NAO_PROMOVIVEL";

    internal static async Task<IReadOnlyList<GovernanceBundleProvenance>> ReadAsync(
        SqlConnection connection, GovernanceModel? active, GovernanceModel? draft,
        CancellationToken ct)
    {
        if (active is null && draft is null)
            return [];

        await using var command = new SqlCommand("""
            SELECT m.modelo_id,m.versao,m.status,m.algoritmo_versao,m.normalizacao_versao,
                   r.ruleset_versao,r.fingerprint_sha256,r.projection_fingerprint_sha256,
                   v.codigo,CASE WHEN v.conteudo_sha256 IS NULL THEN NULL
                     ELSE CONVERT(VARCHAR(64),v.conteudo_sha256,2) END,
                   (SELECT COUNT_BIG(*) FROM identidade.parametro_linkage p
                     WHERE p.modelo_id=m.modelo_id) quantidade_parametros
              FROM identidade.modelo_linkage m
              LEFT JOIN identidade.linkage_ruleset r ON r.modelo_id=m.modelo_id
              LEFT JOIN ref.frequencia_nome_versao v
                ON v.frequencia_nome_versao_id=m.frequencia_nome_versao_id
             WHERE m.modelo_id=@active OR m.modelo_id=@draft;
            """, connection) { CommandTimeout = 10 };
        command.Parameters.Add("@active", SqlDbType.UniqueIdentifier).Value =
            active?.ModelId ?? Guid.Empty;
        command.Parameters.Add("@draft", SqlDbType.UniqueIdentifier).Value =
            draft?.ModelId ?? Guid.Empty;

        var output = new Dictionary<Guid, GovernanceBundleProvenance>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string? Str(int ordinal) => reader.IsDBNull(ordinal)
                ? null : reader.GetString(ordinal).Trim();
            var id = reader.GetGuid(0);
            var version = reader.GetInt32(1);
            var status = reader.GetString(2).Trim();
            var expected = active?.ModelId == id ? active :
                draft?.ModelId == id ? draft : null;
            if (expected is null || expected.Version != version ||
                !string.Equals(expected.Status, status, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "DT-15: proveniência incompatível com o modelo da leitura inicial.");

            var algorithm = Str(3);
            var normalization = Str(4);
            var ruleset = Str(5);
            var rulesetFingerprint = Str(6);
            var projectionFingerprint = Str(7);
            var referenceCode = Str(8);
            var referenceSha = Str(9);
            var parameterCount = reader.GetInt64(10);

            // Contagem e fingerprints parciais NÃO comprovam a cobertura integral
            // de m/u, thresholds, margem, guardas e comparadores do runtime.
            var partial = parameterCount > 0 &&
                !string.IsNullOrWhiteSpace(rulesetFingerprint) &&
                !string.IsNullOrWhiteSpace(algorithm) &&
                !string.IsNullOrWhiteSpace(normalization);
            var item = new GovernanceBundleProvenance(
                id, version, status,
                parameterCount, parameterCount > 0
                    ? "PARAMETROS_PRESENTES_SEM_HASH_GLOBAL"
                    : "PARAMETROS_AUSENTES",
                ruleset, rulesetFingerprint, projectionFingerprint,
                algorithm, normalization,
                "COMPARADORES_E_GUARDAS_NAO_VERIFICADOS_POR_ESTA_LEITURA",
                referenceCode, referenceSha,
                referenceCode is not null && referenceSha is not null
                    ? "REFERENCIA_ASSOCIADA_NAO_PROVA_RECARGA_NEM_ORIGEM_MU"
                    : "REFERENCIA_NAO_COMPROVADA_NAO_INFERIR_ORIGEM_MU",
                partial ? "PROVENIENCIA_PARCIAL" : "PROVENIENCIA_INCOMPLETA",
                MissingGlobalFingerprint);
            if (!output.TryAdd(id, item))
                throw new InvalidDataException(
                    "DT-15: mais de uma linha de proveniência para o mesmo modelo.");
        }

        if ((active is not null && !output.ContainsKey(active.ModelId)) ||
            (draft is not null && !output.ContainsKey(draft.ModelId)))
            throw new InvalidDataException(
                "DT-15: proveniência do ATIVO ou RASCUNHO não encontrada.");
        return output.Values.OrderBy(x => x.Version).ToArray();
    }
}

// Apenas metadados de modelos, nunca PII, m/u, thresholds ou guardas sensíveis.
// Blocos lógicos não implicam três arquivos físicos. O hash global ainda não existe
// nesta leitura; por contrato, ela nunca autoriza VALIDATE/ACTIVATE.
internal sealed record GovernanceBundleProvenance(
    Guid ModelId, int Version, string ModelStatus,
    long ParameterRowCount, string ParameterCoverage,
    string? RulesetVersion, string? RulesetFingerprint,
    string? ProjectionFingerprint,
    string? AlgorithmVersion, string? NormalizationVersion,
    string RuntimeComparatorAndGuardCoverage,
    string? ReferenceCode, string? ReferenceSha256,
    string ReferenceAssociationStatus,
    string CoverageStatus, string GlobalFingerprintStatus);
