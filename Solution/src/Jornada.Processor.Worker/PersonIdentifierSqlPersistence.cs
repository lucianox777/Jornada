using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    private static async Task PersistPersonIdentifiersAsync(
        SqlConnection connection,
        SqlTransaction tx,
        ReservedBatch batch,
        long observationId,
        IReadOnlyList<ParsedPersonIdentifier>? identifiers,
        CancellationToken ct)
    {
        if (identifiers is null || identifiers.Count == 0)
            return;

        foreach (var identifier in identifiers)
        {
            long? basePessoaOrigemId = null;
            var namespaceCodigo = identifier.Namespace;
            var statusValidacao = "NAO_VALIDADO";

            switch (identifier.Tipo)
            {
                case "CPF":
                    statusValidacao = CpfRules.NormalizeAndValidate(identifier.ValorNormalizado) is null
                        ? "INVALIDO"
                        : "VALIDO";
                    break;
                case "UUID_JORNADA":
                    statusValidacao = Guid.TryParse(identifier.ValorNormalizado, out _)
                        ? "VALIDO"
                        : "INVALIDO";
                    break;
                case "CODIGO_BASE_ORIGEM":
                {
                    var resolved = await ResolveAuthorizedPersonBaseAsync(
                        connection, tx, batch.SistemaOrigemId, namespaceCodigo, ct);
                    basePessoaOrigemId = resolved.BasePessoaOrigemId;
                    namespaceCodigo = resolved.Codigo;
                    statusValidacao = resolved.ConfiancaIdentidade == "HOMOLOGADA_DETERMINISTICA"
                        ? "VALIDO"
                        : "NAO_VALIDADO";
                    break;
                }
                case "CNS":
                case "RG":
                case "OUTRO":
                    statusValidacao = "NAO_VALIDADO";
                    break;
                default:
                    throw new InvalidDataException($"Tipo de identificador não suportado para persistência: {identifier.Tipo}.");
            }

            if (string.IsNullOrWhiteSpace(namespaceCodigo))
                throw new InvalidDataException($"Identificador {identifier.Tipo} sem namespace resolvido.");

            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT silver.pessoa_identificador_observacao(
                    pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
                    valor_original,valor_normalizado,base_pessoa_origem_id,emissor_codigo,uf_emissor,
                    status_validacao,status_evidencia,evidencia_tipo,verificado_em)
                VALUES(@obs,@tipo,@namespace,@original,@normalizado,@base,@emissor,@uf,
                       @validacao,@evidencia,@evidencia_tipo,@verificado);
                """;
            insert.Parameters.AddWithValue("@obs", observationId);
            insert.Parameters.Add(new SqlParameter("@tipo", SqlDbType.NVarChar, 40) { Value = identifier.Tipo });
            insert.Parameters.Add(new SqlParameter("@namespace", SqlDbType.NVarChar, 120) { Value = namespaceCodigo });
            insert.Parameters.Add(new SqlParameter("@original", SqlDbType.NVarChar, 255) { Value = identifier.ValorOriginal });
            insert.Parameters.Add(new SqlParameter("@normalizado", SqlDbType.NVarChar, 255) { Value = identifier.ValorNormalizado });
            insert.Parameters.Add(new SqlParameter("@base", SqlDbType.BigInt) { Value = (object?)basePessoaOrigemId ?? DBNull.Value });
            AddNullable(insert, "@emissor", SqlDbType.NVarChar, 120, identifier.Emissor);
            AddNullable(insert, "@uf", SqlDbType.Char, 2, identifier.UfEmissor);
            insert.Parameters.Add(new SqlParameter("@validacao", SqlDbType.NVarChar, 20) { Value = statusValidacao });
            insert.Parameters.Add(new SqlParameter("@evidencia", SqlDbType.NVarChar, 20) { Value = identifier.StatusEvidencia });
            AddNullable(insert, "@evidencia_tipo", SqlDbType.NVarChar, 80, identifier.EvidenciaTipo);
            AddNullableDto(insert, "@verificado", identifier.VerificadoEm);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<AuthorizedPersonBase> ResolveAuthorizedPersonBaseAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long sistemaOrigemId,
        string? requestedCode,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = string.IsNullOrWhiteSpace(requestedCode)
            ? """
                SELECT TOP(1) b.base_pessoa_origem_id,b.codigo,b.confianca_identidade
                FROM ref.sistema_origem_base_pessoa sb WITH (UPDLOCK,HOLDLOCK)
                JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=sb.base_pessoa_origem_id
                WHERE sb.sistema_origem_id=@sistema AND sb.ativo=1 AND sb.padrao=1 AND b.ativo=1;
                """
            : """
                SELECT TOP(1) b.base_pessoa_origem_id,b.codigo,b.confianca_identidade
                FROM ref.sistema_origem_base_pessoa sb WITH (UPDLOCK,HOLDLOCK)
                JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=sb.base_pessoa_origem_id
                WHERE sb.sistema_origem_id=@sistema AND sb.ativo=1 AND b.ativo=1
                  AND b.codigo=@codigo COLLATE Latin1_General_100_BIN2
                  AND DATALENGTH(b.codigo)=DATALENGTH(@codigo);
                """;
        command.Parameters.AddWithValue("@sistema", sistemaOrigemId);
        if (!string.IsNullOrWhiteSpace(requestedCode))
            command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 120) { Value = requestedCode });

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(requestedCode))
                throw new InvalidDataException("Sistema de origem não possui Base de Pessoa padrão autorizada.");
            throw new InvalidDataException($"Base de Pessoa {requestedCode} não está autorizada para o sistema de origem.");
        }

        return new AuthorizedPersonBase(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    private sealed record AuthorizedPersonBase(long BasePessoaOrigemId, string Codigo, string ConfiancaIdentidade);
}
