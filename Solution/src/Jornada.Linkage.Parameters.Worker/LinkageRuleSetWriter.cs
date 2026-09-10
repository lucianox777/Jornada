using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Publica o ruleset calculado pelo Calibrador dentro da transação do modelo.
/// Um modelo recebe exatamente um ruleset; qualquer alteração exige nova versão/modelo.
/// </summary>
public static class LinkageRuleSetWriter
{
    public const string MethodVersion = "LINKAGE_RULESET_WRITER_V1";

    public static async Task WriteAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid modelId,
        LinkageDynamicRuleSet ruleSet,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(ruleSet);

        if (ruleSet.BlockingPasses.Count == 0)
            throw new InvalidOperationException(
                "Ruleset novo deve declarar BlockingPasses explicitamente; BlockingFields legado é somente compatibilidade de leitura.");

        string? modelStatus = null;
        string? modelAlgorithm = null;
        string? normalization = null;
        await using (var model = Command(connection, transaction, """
            SELECT status,algoritmo_versao,normalizacao_versao
              FROM identidade.modelo_linkage
             WHERE modelo_id=@modelo_id;
            """))
        {
            Add(model, "@modelo_id", DbType.Guid, modelId);
            await using var reader = await model.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                modelStatus = reader.GetString(0);
                modelAlgorithm = reader.GetString(1);
                normalization = reader.GetString(2);
            }
        }

        if (modelStatus is null)
            throw new InvalidOperationException($"Modelo {modelId} não encontrado para publicação de ruleset.");
        if (modelStatus is not ("GERANDO" or "RASCUNHO"))
            throw new InvalidOperationException($"Modelo {modelId} não está editável para publicação de ruleset: {modelStatus}.");
        if (!string.Equals(modelAlgorithm, ruleSet.AlgorithmVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Algoritmo do ruleset diverge do modelo {modelId}.");
        if (!string.Equals(normalization, IdentityComparison.NormalizationVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Normalização {normalization} do modelo não é suportada pelo ruleset atual.");

        // Um ruleset por modelo; reutilizar o modelo_id como ruleset_id torna a identidade estável e auditável.
        var ruleSetId = modelId;
        await using (var header = Command(connection, transaction, """
            INSERT INTO identidade.linkage_ruleset(
                ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256,
                ibge_source_versao,ibge_fingerprint_sha256)
            VALUES(@ruleset_id,@modelo_id,@versao,@algoritmo,@fingerprint,@ibge_versao,@ibge_fingerprint);
            """))
        {
            Add(header, "@ruleset_id", DbType.Guid, ruleSetId);
            Add(header, "@modelo_id", DbType.Guid, modelId);
            Add(header, "@versao", DbType.String, ruleSet.RuleSetVersion, 120);
            Add(header, "@algoritmo", DbType.String, ruleSet.AlgorithmVersion, 80);
            Add(header, "@fingerprint", DbType.AnsiStringFixedLength, ruleSet.FingerprintSha256, 64);
            Add(header, "@ibge_versao", DbType.String, ruleSet.IbgeSourceVersion, 200);
            Add(header, "@ibge_fingerprint", DbType.AnsiStringFixedLength, ruleSet.IbgeFingerprintSha256, 64);
            await header.ExecuteNonQueryAsync(ct);
        }

        for (var passIndex = 0; passIndex < ruleSet.BlockingPasses.Count; passIndex++)
        {
            var pass = ruleSet.BlockingPasses[passIndex];
            await using (var passCommand = Command(connection, transaction, """
                INSERT INTO identidade.linkage_ruleset_passe(ruleset_id,passe_ordem,passe_id)
                VALUES(@ruleset_id,@ordem,@passe_id);
                """))
            {
                Add(passCommand, "@ruleset_id", DbType.Guid, ruleSetId);
                Add(passCommand, "@ordem", DbType.Int32, passIndex);
                Add(passCommand, "@passe_id", DbType.String, pass.PassId, 120);
                await passCommand.ExecuteNonQueryAsync(ct);
            }

            for (var fieldIndex = 0; fieldIndex < pass.Fields.Count; fieldIndex++)
            {
                await using var fieldCommand = Command(connection, transaction, """
                    INSERT INTO identidade.linkage_ruleset_passe_campo(
                        ruleset_id,passe_ordem,campo_ordem,atributo)
                    VALUES(@ruleset_id,@passe_ordem,@campo_ordem,@atributo);
                    """);
                Add(fieldCommand, "@ruleset_id", DbType.Guid, ruleSetId);
                Add(fieldCommand, "@passe_ordem", DbType.Int32, passIndex);
                Add(fieldCommand, "@campo_ordem", DbType.Int32, fieldIndex);
                Add(fieldCommand, "@atributo", DbType.String, pass.Fields[fieldIndex], 80);
                await fieldCommand.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object? value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size.HasValue)
            parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
