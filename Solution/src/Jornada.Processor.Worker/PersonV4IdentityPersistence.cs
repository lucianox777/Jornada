using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    private sealed record PersonOriginState(
        long PessoaOrigemId,
        long BasePessoaOrigemId,
        string BaseCodigo,
        string BaseConfianca);

    private sealed record JornadaUuidState(
        Guid Recebido,
        Guid? Canonico);

    private static async Task<PersonOriginState?> ResolvePersonOriginV4Async(
        SqlConnection connection,
        SqlTransaction tx,
        long sistemaOrigemId,
        string? codigoPessoaOrigem,
        string? manifestBasePessoaOrigem,
        IReadOnlyList<ParsedPersonIdentifier> identifiers,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigoPessoaOrigem))
            return null;

        var explicitBase = identifiers
            .Where(i => i.Tipo == "CODIGO_BASE_ORIGEM" && !string.IsNullOrWhiteSpace(i.Namespace))
            .Select(i => i.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();

        var requestedBase = string.IsNullOrWhiteSpace(manifestBasePessoaOrigem)
            ? explicitBase
            : manifestBasePessoaOrigem.Trim();

        long baseId;
        string baseCode;
        string baseConfidence;
        await using (var resolveBase = connection.CreateCommand())
        {
            resolveBase.Transaction = tx;
            resolveBase.CommandText = """
                SELECT b.base_pessoa_origem_id,b.codigo,b.confianca_identidade
                FROM ref.sistema_origem_base_pessoa sb WITH(UPDLOCK,HOLDLOCK)
                JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=sb.base_pessoa_origem_id
                WHERE sb.sistema_origem_id=@sistema
                  AND sb.ativo=1
                  AND b.ativo=1
                  AND ((@base IS NULL AND sb.padrao=1) OR (@base IS NOT NULL AND b.codigo=@base));
                """;
            resolveBase.Parameters.AddWithValue("@sistema", sistemaOrigemId);
            resolveBase.Parameters.Add(new SqlParameter("@base", SqlDbType.NVarChar, 120)
            {
                Value = (object?)requestedBase ?? DBNull.Value
            });
            await using var reader = await resolveBase.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidDataException(
                    requestedBase is null
                        ? "Sistema de origem não possui Base de Pessoa padrão ativa/autorizada."
                        : $"Base de Pessoa {requestedBase} não está ativa/autorizada para o sistema de origem.");
            baseId = reader.GetInt64(0);
            baseCode = reader.GetString(1);
            baseConfidence = reader.GetString(2);
            if (await reader.ReadAsync(ct))
                throw new InvalidDataException("Governança de Base de Pessoa retornou mais de uma Base aplicável.");
        }

        long sourceId;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT pessoa_origem_id
                FROM silver.pessoa_origem WITH(UPDLOCK,HOLDLOCK)
                WHERE base_pessoa_origem_id=@base_id
                  AND codigo_pessoa_origem=@codigo;
                """;
            find.Parameters.AddWithValue("@base_id", baseId);
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigoPessoaOrigem });
            var value = await find.ExecuteScalarAsync(ct);
            if (value is null or DBNull)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
                    OUTPUT INSERTED.pessoa_origem_id
                    VALUES(@sistema,@codigo,@base_id);
                    """;
                insert.Parameters.AddWithValue("@sistema", sistemaOrigemId);
                insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigoPessoaOrigem });
                insert.Parameters.AddWithValue("@base_id", baseId);
                sourceId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                sourceId = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        await using (var usage = connection.CreateCommand())
        {
            usage.Transaction = tx;
            usage.CommandText = """
                MERGE silver.pessoa_origem_sistema WITH(HOLDLOCK) AS t
                USING(SELECT @pessoa_origem_id pessoa_origem_id,@sistema sistema_origem_id) AS s
                   ON t.pessoa_origem_id=s.pessoa_origem_id AND t.sistema_origem_id=s.sistema_origem_id
                WHEN MATCHED THEN
                    UPDATE SET ultima_observacao_em=SYSDATETIMEOFFSET()
                WHEN NOT MATCHED THEN
                    INSERT(pessoa_origem_id,sistema_origem_id)
                    VALUES(s.pessoa_origem_id,s.sistema_origem_id);
                """;
            usage.Parameters.AddWithValue("@pessoa_origem_id", sourceId);
            usage.Parameters.AddWithValue("@sistema", sistemaOrigemId);
            await usage.ExecuteNonQueryAsync(ct);
        }

        return new PersonOriginState(sourceId, baseId, baseCode, baseConfidence);
    }

    private static async Task<JornadaUuidState?> ResolveJornadaUuidAsync(
        SqlConnection connection,
        SqlTransaction tx,
        IReadOnlyList<ParsedPersonIdentifier> identifiers,
        CancellationToken ct)
    {
        var values = identifiers
            .Where(i => i.Tipo == "UUID_JORNADA")
            .Select(i => i.ValorNormalizado)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        if (values.Length == 0)
            return null;
        if (values.Length > 1)
            throw new InvalidDataException("Observação contém mais de um UUID Jornada distinto.");

        var received = Guid.Parse(values[0]);
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT CASE
                     WHEN EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@uuid)
                     THEN identidade.fn_pessoa_uuid_canonico(@uuid)
                     ELSE NULL
                   END;
            """;
        command.Parameters.AddWithValue("@uuid", received);
        var value = await command.ExecuteScalarAsync(ct);
        var canonical = value is null or DBNull ? (Guid?)null : (Guid)value;
        return new JornadaUuidState(received, canonical);
    }

    private static async Task PersistPersonIdentifiersAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long observationId,
        IReadOnlyList<ParsedPersonIdentifier> identifiers,
        PersonOriginState? origin,
        JornadaUuidState? jornadaUuid,
        CancellationToken ct)
    {
        foreach (var identifier in identifiers)
        {
            var namespaceCode = identifier.Tipo == "CODIGO_BASE_ORIGEM"
                ? origin?.BaseCodigo
                : identifier.Namespace;
            if (string.IsNullOrWhiteSpace(namespaceCode))
                throw new InvalidDataException($"Identificador {identifier.Tipo} sem namespace resolvido.");

            long? baseId = identifier.Tipo == "CODIGO_BASE_ORIGEM"
                ? origin?.BasePessoaOrigemId
                : null;
            if (identifier.Tipo == "CODIGO_BASE_ORIGEM" && !baseId.HasValue)
                throw new InvalidDataException("CODIGO_BASE_ORIGEM exige Pessoa de origem resolvida.");

            var validationStatus = identifier.Tipo switch
            {
                "CPF" => CpfRules.NormalizeAndValidate(identifier.ValorNormalizado) is null ? "INVALIDO" : "VALIDO",
                "NIS" => NisRules.NormalizeAndValidate(identifier.ValorNormalizado) is null
                    ? "INVALIDO"
                    : string.Equals(identifier.StatusEvidencia, "COMPROVADO", StringComparison.Ordinal)
                        ? "VALIDO"
                        : "NAO_VALIDADO",
                "CODIGO_BASE_ORIGEM" => string.Equals(origin?.BaseConfianca, "HOMOLOGADA_DETERMINISTICA", StringComparison.Ordinal)
                    ? "VALIDO" : "NAO_VALIDADO",
                "UUID_JORNADA" => jornadaUuid?.Canonico is not null ? "VALIDO" : "INVALIDO",
                _ => "NAO_VALIDADO"
            };

            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT silver.pessoa_identificador_observacao(
                    pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
                    valor_original,valor_normalizado,base_pessoa_origem_id,
                    emissor_codigo,uf_emissor,status_validacao,status_evidencia,
                    evidencia_tipo,verificado_em)
                VALUES(@obs,@tipo,@namespace,@original,@normalizado,@base,
                       @emissor,@uf,@validacao,@evidencia,@evidencia_tipo,@verificado);
                """;
            insert.Parameters.AddWithValue("@obs", observationId);
            insert.Parameters.Add(new SqlParameter("@tipo", SqlDbType.NVarChar, 40) { Value = identifier.Tipo });
            insert.Parameters.Add(new SqlParameter("@namespace", SqlDbType.NVarChar, 120) { Value = namespaceCode });
            insert.Parameters.Add(new SqlParameter("@original", SqlDbType.NVarChar, 255) { Value = identifier.ValorOriginal });
            insert.Parameters.Add(new SqlParameter("@normalizado", SqlDbType.NVarChar, 255) { Value = identifier.ValorNormalizado });
            insert.Parameters.Add(new SqlParameter("@base", SqlDbType.BigInt) { Value = (object?)baseId ?? DBNull.Value });
            insert.Parameters.Add(new SqlParameter("@emissor", SqlDbType.NVarChar, 120) { Value = (object?)identifier.Emissor ?? DBNull.Value });
            insert.Parameters.Add(new SqlParameter("@uf", SqlDbType.Char, 2) { Value = (object?)identifier.UfEmissor ?? DBNull.Value });
            insert.Parameters.Add(new SqlParameter("@validacao", SqlDbType.NVarChar, 20) { Value = validationStatus });
            insert.Parameters.Add(new SqlParameter("@evidencia", SqlDbType.NVarChar, 20) { Value = identifier.StatusEvidencia });
            insert.Parameters.Add(new SqlParameter("@evidencia_tipo", SqlDbType.NVarChar, 80) { Value = (object?)identifier.EvidenciaTipo ?? DBNull.Value });
            insert.Parameters.Add(new SqlParameter("@verificado", SqlDbType.DateTimeOffset) { Value = (object?)identifier.VerificadoEm ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static string ProcessingAuditCode(string? sourceCode, long observationId)
        => string.IsNullOrWhiteSpace(sourceCode) ? $"OBSERVACAO:{observationId}" : sourceCode;
}
