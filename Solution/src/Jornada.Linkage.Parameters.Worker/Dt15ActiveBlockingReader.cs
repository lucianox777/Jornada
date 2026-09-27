using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Read-only ACTIVE blocking metadata for the DT-15 diagnostic. The reconstructed
/// ruleset must match the stored fingerprint; no fallback to an unverifiable
/// algorithm or to a different active model is allowed.
/// </summary>
internal static class Dt15ActiveBlockingReader
{
    internal static async Task<Dt15ActiveBlockingSnapshot?> ReadAsync(
        SqlConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Guid modelId;
        int version;
        string algorithm;
        string normalization;
        string? ruleVersion;
        string? ruleAlgorithm;
        string? fingerprint;
        string? projectionSchema;
        string? projectionFingerprint;
        string? ibgeVersion;
        string? ibgeFingerprint;

        await using (var header = new SqlCommand("""
            SELECT m.modelo_id,m.versao,m.algoritmo_versao,m.normalizacao_versao,
                   r.ruleset_versao,r.algoritmo_versao,r.fingerprint_sha256,
                   r.projection_schema_version,r.projection_fingerprint_sha256,
                   r.ibge_source_versao,r.ibge_fingerprint_sha256
              FROM identidade.modelo_linkage m
              LEFT JOIN identidade.linkage_ruleset r ON r.modelo_id=m.modelo_id
             WHERE m.status=N'ATIVO';
            """, connection))
        await using (var reader = await header.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;
            modelId = reader.GetGuid(0);
            version = reader.GetInt32(1);
            algorithm = reader.GetString(2);
            normalization = reader.GetString(3);
            ruleVersion = reader.IsDBNull(4) ? null : reader.GetString(4);
            ruleAlgorithm = reader.IsDBNull(5) ? null : reader.GetString(5);
            fingerprint = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
            projectionSchema = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            projectionFingerprint = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
            ibgeVersion = reader.IsDBNull(9) ? null : reader.GetString(9);
            ibgeFingerprint = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (await reader.ReadAsync(ct))
                throw new InvalidOperationException("DT-15 exige no máximo um modelo ATIVO.");
        }

        if (ruleVersion is null)
        {
            return new Dt15ActiveBlockingSnapshot(
                modelId, version, algorithm, normalization,
                null, null, null, null);
        }
        if (!string.Equals(ruleAlgorithm, algorithm, StringComparison.Ordinal))
            throw new InvalidDataException("DT-15: algoritmo do ruleset ATIVO diverge do cabeçalho.");

        var ordered = new SortedDictionary<int, (string PassId, List<(int Order, string Field)> Fields)>();
        await using (var command = new SqlCommand("""
            SELECT p.passe_ordem,p.passe_id,c.campo_ordem,c.atributo
              FROM identidade.linkage_ruleset_passe p
              LEFT JOIN identidade.linkage_ruleset_passe_campo c
                ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
              JOIN identidade.linkage_ruleset r ON r.ruleset_id=p.ruleset_id
             WHERE r.modelo_id=@model_id
             ORDER BY p.passe_ordem,c.campo_ordem;
            """, connection))
        {
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var order = reader.GetInt32(0);
                var passId = reader.GetString(1);
                if (!ordered.TryGetValue(order, out var entry))
                {
                    entry = (passId, new List<(int Order, string Field)>());
                    ordered.Add(order, entry);
                }
                else if (!string.Equals(entry.PassId, passId, StringComparison.Ordinal))
                    throw new InvalidDataException("DT-15: passe ATIVO tem identidade inconsistente.");
                if (!reader.IsDBNull(2))
                    entry.Fields.Add((reader.GetInt32(2), reader.GetString(3)));
            }
        }

        if (ordered.Count == 0)
            return new Dt15ActiveBlockingSnapshot(modelId, version, algorithm, normalization,
                fingerprint, projectionSchema, projectionFingerprint, null);
        if (ordered.Values.Any(static x => x.Fields.Count == 0))
            throw new InvalidDataException("DT-15: ruleset ATIVO contém passe sem campos.");

        var passes = ordered.Values
            .Select(static p => LinkageBlockingPass.Create(
                p.PassId, p.Fields.OrderBy(static field => field.Order).Select(static field => field.Field)))
            .ToArray();

        var parameters = new List<KeyValuePair<string, decimal>>();
        await using (var command = new SqlCommand("""
            SELECT nome,valor FROM identidade.parametro_linkage
             WHERE modelo_id=@model_id ORDER BY nome;
            """, connection))
        {
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                parameters.Add(new KeyValuePair<string, decimal>(reader.GetString(0), reader.GetDecimal(1)));
        }

        var rebuilt = LinkageDynamicRuleSet.CreateWithPasses(
            ruleVersion, ruleAlgorithm!, passes, parameters, ibgeVersion, ibgeFingerprint);
        if (!string.Equals(rebuilt.FingerprintSha256, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DT-15: fingerprint do ruleset ATIVO não confere com os parâmetros persistidos.");

        return new Dt15ActiveBlockingSnapshot(modelId, version, algorithm, normalization,
            fingerprint, projectionSchema, projectionFingerprint, passes);
    }
}
