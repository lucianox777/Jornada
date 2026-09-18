using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

internal enum CanonicalNameFrequencyReferenceState
{
    MissingOrLoading = 0,
    AlreadyActive = 1,
    Reactivated = 2
}

internal static class NameFrequencyReferenceState
{
    public static async Task<CanonicalNameFrequencyReferenceState> EnsureCanonicalActiveAsync(
        IOperationalSqlAdapter operationalSql,
        string referenceCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceCode);

        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            long? versionId = null;
            string? status = null;
            byte[]? hash = null;

            await using (var readCanonical = new SqlCommand(
                """
                SELECT frequencia_nome_versao_id,status,conteudo_sha256
                FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK)
                WHERE codigo=@codigo;
                """,
                connection,
                transaction))
            {
                readCanonical.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = referenceCode;
                await using var reader = await readCanonical.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    versionId = reader.GetInt64(0);
                    status = reader.GetString(1);
                    hash = reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2);
                }
            }

            if (versionId is null || status == "CARREGANDO" || hash is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return CanonicalNameFrequencyReferenceState.MissingOrLoading;
            }

            if (status == "ATIVA")
            {
                await transaction.CommitAsync(cancellationToken);
                return CanonicalNameFrequencyReferenceState.AlreadyActive;
            }

            string? otherActiveCode;
            await using (var readActive = new SqlCommand(
                """
                SELECT TOP(1) codigo
                FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK)
                WHERE status='ATIVA';
                """,
                connection,
                transaction))
            {
                otherActiveCode = Convert.ToString(
                    await readActive.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(otherActiveCode))
            {
                throw new InvalidOperationException(
                    $"A referencia canonica {referenceCode} esta publicada como {status}, mas existe outra referencia ATIVA ({otherActiveCode}). A troca deve ser explicita.");
            }

            long nameRows;
            long surnameRows;
            await using (var validate = new SqlCommand(
                """
                SELECT
                    SUM(CASE WHEN tipo='NOME' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END),
                    SUM(CASE WHEN tipo='SOBRENOME' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END)
                FROM ref.frequencia_nome
                WHERE frequencia_nome_versao_id=@id;
                """,
                connection,
                transaction))
            {
                validate.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId.Value;
                await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Referencia canonica {referenceCode} nao possui linhas materializadas.");

                nameRows = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                surnameRows = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            }

            if (nameRows <= 0 || surnameRows <= 0)
            {
                throw new InvalidOperationException(
                    $"Referencia canonica {referenceCode} publicada esta incompleta: NOME={nameRows}; SOBRENOME={surnameRows}.");
            }

            await using (var reactivate = new SqlCommand(
                """
                UPDATE ref.frequencia_nome_versao
                SET status='ATIVA',
                    ativado_em=SYSDATETIMEOFFSET()
                WHERE frequencia_nome_versao_id=@id
                  AND status<>'ATIVA'
                  AND status<>'CARREGANDO'
                  AND conteudo_sha256 IS NOT NULL;
                """,
                connection,
                transaction))
            {
                reactivate.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId.Value;
                if (await reactivate.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Falha ao reativar a referencia canonica {referenceCode}.");
            }

            await transaction.CommitAsync(cancellationToken);
            return CanonicalNameFrequencyReferenceState.Reactivated;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
