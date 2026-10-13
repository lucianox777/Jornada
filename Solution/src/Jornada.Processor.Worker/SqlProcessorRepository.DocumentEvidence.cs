using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    /// <summary>
    /// Persistência documental append-only, na mesma transação de Pessoa. Nenhuma
    /// evidência recebida é marcada VALIDO nem promovida para Gold por esta rota.
    /// </summary>
    private static async Task PersistDocumentEvidenceAsync(
        SqlConnection connection, SqlTransaction tx, long observationId,
        IReadOnlyList<ParsedDocumentEvidence> evidences, string? sourceBase,
        CancellationToken ct)
    {
        if (evidences.Count == 0) return;
        if (string.IsNullOrWhiteSpace(sourceBase) || sourceBase.Length > 120)
            throw new InvalidDataException(
                "evidenciasDocumentais exige codigoBasePessoaOrigem estável (1 a 120 caracteres) no manifesto.");

        foreach (var evidence in evidences)
        {
            long modelId;
            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = tx;
                lookup.CommandText = """
                    SELECT m.modelo_documento_id
                    FROM ref.modelo_documento m WITH (HOLDLOCK)
                    JOIN ref.tipo_documento t ON t.tipo_documento_codigo=m.tipo_documento_codigo
                    WHERE m.codigo=@codigo AND m.versao=@versao
                      AND m.estado=N'PUBLICADO' AND t.ativo=1;
                    """;
                lookup.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = evidence.ModelCode });
                lookup.Parameters.AddWithValue("@versao", evidence.ModelVersion);
                var found = await lookup.ExecuteScalarAsync(ct);
                if (found is null or DBNull)
                    throw new InvalidDataException("Evidência documental exige modelo publicado e tipo ativo.");
                modelId = Convert.ToInt64(found, CultureInfo.InvariantCulture);
            }

            // Prevalidar TODOS os valores na própria transação para não aceitar
            // documento parcial ou qualquer atributo não coberto pelo catálogo.
            foreach (var item in evidence.Values)
            {
                await using var coverage = connection.CreateCommand();
                coverage.Transaction = tx;
                coverage.CommandText = """
                    SELECT COUNT_BIG(*) FROM ref.modelo_documento_atributo
                    WHERE modelo_documento_id=@modelo AND atributo_codigo=@atributo
                      AND comprovacao_admissivel=1;
                    """;
                coverage.Parameters.AddWithValue("@modelo", modelId);
                coverage.Parameters.Add(new SqlParameter("@atributo", SqlDbType.NVarChar, 80) { Value = item.AttributeCode });
                if (Convert.ToInt64(await coverage.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidDataException(
                        "Atributo da evidência não pertence à cobertura do modelo documental publicado.");
            }

            long documentId;
            await using (var document = connection.CreateCommand())
            {
                document.Transaction = tx;
                document.CommandText = """
                    INSERT silver.documento_evidencia_observacao(
                        pessoa_observacao_id,modelo_documento_id,base_origem_codigo,
                        ocorrencia_origem_codigo,classe_evidencia,data_evidencia,
                        data_atendimento,emissor_codigo,uf_emissor,status_validacao)
                    OUTPUT INSERTED.documento_evidencia_id
                    VALUES(@obs,@modelo,@base,@ocorrencia,@classe,@data,@atendimento,
                           @emissor,@uf,N'NAO_VALIDADO');
                    """;
                document.Parameters.AddWithValue("@obs", observationId);
                document.Parameters.AddWithValue("@modelo", modelId);
                document.Parameters.Add(new SqlParameter("@base", SqlDbType.NVarChar, 120) { Value = sourceBase });
                document.Parameters.Add(new SqlParameter("@ocorrencia", SqlDbType.NVarChar, 255) { Value = evidence.OccurrenceCode });
                document.Parameters.Add(new SqlParameter("@classe", SqlDbType.NVarChar, 20) { Value = evidence.EvidenceClass });
                document.Parameters.Add(new SqlParameter("@data", SqlDbType.Date)
                {
                    Value = evidence.EvidenceDate.HasValue
                        ? evidence.EvidenceDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
                });
                document.Parameters.Add(new SqlParameter("@atendimento", SqlDbType.DateTimeOffset)
                    { Value = (object?)evidence.AttendanceDate ?? DBNull.Value });
                document.Parameters.Add(new SqlParameter("@emissor", SqlDbType.NVarChar, 120)
                    { Value = (object?)evidence.IssuerCode ?? DBNull.Value });
                document.Parameters.Add(new SqlParameter("@uf", SqlDbType.Char, 2)
                    { Value = (object?)evidence.IssuerState ?? DBNull.Value });
                documentId = Convert.ToInt64(await document.ExecuteScalarAsync(ct),
                    CultureInfo.InvariantCulture);
            }

            foreach (var item in evidence.Values)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT silver.documento_evidencia_valor(
                        documento_evidencia_id,modelo_documento_id,atributo_codigo,valor_original)
                    VALUES(@documento,@modelo,@atributo,@valor);
                    """;
                command.Parameters.AddWithValue("@documento", documentId);
                command.Parameters.AddWithValue("@modelo", modelId);
                command.Parameters.Add(new SqlParameter("@atributo", SqlDbType.NVarChar, 80)
                    { Value = item.AttributeCode });
                command.Parameters.Add(new SqlParameter("@valor", SqlDbType.NVarChar, 2000)
                    { Value = item.OriginalValue });
                await command.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
