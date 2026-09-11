using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Leitura provider-neutral do ruleset associado a um modelo.
/// Modelos legados podem não ter ruleset persistido e retornam null; quando existe,
/// o conteúdo é reconstruído canonicamente e seu fingerprint é obrigatoriamente revalidado.
/// Rulesets modernos também precisam carregar a identidade exata da projeção física.
/// </summary>
public static class LinkageRuleSetReader
{
    public const string MethodVersion = "LINKAGE_RULESET_READER_V2";

    public static async Task<LinkageDynamicRuleSet?> TryLoadAsync(
        DbConnection connection,
        Guid modelId,
        CancellationToken ct,
        DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string? ruleSetVersion = null;
        string? ruleSetAlgorithm = null;
        string? storedFingerprint = null;
        string? projectionSchemaVersion = null;
        string? projectionFingerprint = null;
        string? ibgeVersion = null;
        string? ibgeFingerprint = null;
        string? modelAlgorithm = null;
        string? normalization = null;
        string? modelStatus = null;

        await using (var header = Command(connection, transaction, """
            SELECT r.ruleset_versao,r.algoritmo_versao,r.fingerprint_sha256,
                   r.projection_schema_version,r.projection_fingerprint_sha256,
                   r.ibge_source_versao,r.ibge_fingerprint_sha256,
                   m.algoritmo_versao,m.normalizacao_versao,m.status
              FROM identidade.linkage_ruleset r
              JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
             WHERE r.modelo_id=@modelo_id;
            """))
        {
            Add(header, "@modelo_id", DbType.Guid, modelId);
            await using var reader = await header.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            ruleSetVersion = reader.GetString(0);
            ruleSetAlgorithm = reader.GetString(1);
            storedFingerprint = reader.GetString(2).Trim();
            projectionSchemaVersion = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
            projectionFingerprint = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
            ibgeVersion = reader.IsDBNull(5) ? null : reader.GetString(5);
            ibgeFingerprint = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
            modelAlgorithm = reader.GetString(7);
            normalization = reader.GetString(8);
            modelStatus = reader.GetString(9);
        }

        if (modelStatus is not ("VALIDADO" or "ATIVO" or "INATIVO"))
            throw new InvalidOperationException($"Ruleset do modelo {modelId} não está em estado consumível: {modelStatus}.");
        if (!string.Equals(ruleSetAlgorithm, modelAlgorithm, StringComparison.Ordinal))
            throw new InvalidOperationException($"Algoritmo do ruleset diverge do modelo {modelId}.");
        if (!string.Equals(normalization, IdentityComparison.NormalizationVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Normalização {normalization} do ruleset/modelo não é suportada pelo Runner.");

        PersonResolutionProjectionContract.ValidateSupported(projectionSchemaVersion, projectionFingerprint);

        var passFields = new SortedDictionary<int, (string PassId, List<(int Order, string Field)> Fields)>();
        await using (var passes = Command(connection, transaction, """
            SELECT p.passe_ordem,p.passe_id,c.campo_ordem,c.atributo
              FROM identidade.linkage_ruleset_passe p
              LEFT JOIN identidade.linkage_ruleset_passe_campo c
                ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
              JOIN identidade.linkage_ruleset r ON r.ruleset_id=p.ruleset_id
             WHERE r.modelo_id=@modelo_id
             ORDER BY p.passe_ordem,c.campo_ordem;
            """))
        {
            Add(passes, "@modelo_id", DbType.Guid, modelId);
            await using var reader = await passes.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var passOrder = reader.GetInt32(0);
                var passId = reader.GetString(1);
                if (!passFields.TryGetValue(passOrder, out var entry))
                {
                    entry = (passId, new List<(int Order, string Field)>());
                    passFields.Add(passOrder, entry);
                }
                else if (!string.Equals(entry.PassId, passId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Ruleset do modelo {modelId} possui passe inconsistente na ordem {passOrder}.");
                }

                if (!reader.IsDBNull(2))
                    entry.Fields.Add((reader.GetInt32(2), reader.GetString(3)));
            }
        }

        if (passFields.Count == 0 || passFields.Values.Any(static pass => pass.Fields.Count == 0))
            throw new InvalidOperationException($"Ruleset do modelo {modelId} não possui passes/campos completos.");

        var flattenedFields = passFields.Values
            .SelectMany(static pass => pass.Fields)
            .Select(static field => field.Field)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (projectionSchemaVersion is null && flattenedFields.Any(IsProjectionBoundFeature))
        {
            throw new InvalidOperationException(
                $"Ruleset do modelo {modelId} usa atributo dinâmico sem identidade de projeção física; execução recusada.");
        }

        var parameters = new List<KeyValuePair<string, decimal>>();
        await using (var parameterCommand = Command(connection, transaction, """
            SELECT nome,valor
              FROM identidade.parametro_linkage
             WHERE modelo_id=@modelo_id
             ORDER BY nome;
            """))
        {
            Add(parameterCommand, "@modelo_id", DbType.Guid, modelId);
            await using var reader = await parameterCommand.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                parameters.Add(new KeyValuePair<string, decimal>(reader.GetString(0), reader.GetDecimal(1)));
        }

        var canonicalPasses = passFields.Values
            .Select(static pass => LinkageBlockingPass.Create(
                pass.PassId,
                pass.Fields.OrderBy(static field => field.Order).Select(static field => field.Field)))
            .ToArray();

        var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
            ruleSetVersion!,
            ruleSetAlgorithm!,
            canonicalPasses,
            parameters,
            ibgeVersion,
            ibgeFingerprint) with
        {
            ProjectionSchemaVersion = projectionSchemaVersion,
            ProjectionFingerprintSha256 = projectionFingerprint?.ToLowerInvariant()
        };

        if (!string.Equals(ruleSet.FingerprintSha256, storedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Fingerprint do ruleset do modelo {modelId} diverge do conteúdo persistido; execução recusada.");

        return ruleSet;
    }

    private static bool IsProjectionBoundFeature(string feature) =>
        PersonResolutionContractCatalog.TryGetByBlockingFeature(feature, out var field)
        && field.CompatibilityProfile is null;

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
