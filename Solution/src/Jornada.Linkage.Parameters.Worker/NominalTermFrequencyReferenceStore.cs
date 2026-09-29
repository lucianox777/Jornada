using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record PreparedNominalTermFrequency(
    NominalTermFrequencySnapshot Snapshot,
    IReadOnlyList<NominalTermFrequencyEntry> Entries,
    long ReferenceVersionId);

/// <summary>
/// Materializa, para o modelo, apenas frequências publicadas e governadas.
/// Pessoa usa NOME/TODOS do município de São Paulo 3550308; mãe usa
/// NOME/FEMININO Brasil. Não consulta versão ATIVA e não faz fallback geográfico.
/// </summary>
public static class NominalTermFrequencyReferenceStore
{
    public const string MethodVersion = "NOMINAL_TF_PUBLISHED_FIRST_NAME_V1";

    public static async Task<PreparedNominalTermFrequency> PrepareAsync(
        SqlConnection connection,
        long referenceVersionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (referenceVersionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceVersionId));

        await RequireCoverageAsync(connection, referenceVersionId, cancellationToken);

        var entries = new List<NominalTermFrequencyEntry>();
        await using var command = new SqlCommand(
            """
            WITH domain_rows AS (
                SELECT
                    N'NOME_PRENOME' AS atributo,
                    valor_normalizado,
                    SUM(CONVERT(bigint,frequencia)) AS ocorrencias
                FROM ref.frequencia_nome
                WHERE frequencia_nome_versao_id=@ref
                  AND tipo=N'NOME'
                  AND sexo=N'TODOS'
                  AND periodo_nascimento=N'TODOS'
                  AND escopo_geografico=N'MUNICIPIO'
                  AND uf_codigo='35'
                  AND municipio_codigo='3550308'
                GROUP BY valor_normalizado
                UNION ALL
                SELECT
                    N'NOME_MAE_PRENOME' AS atributo,
                    valor_normalizado,
                    SUM(CONVERT(bigint,frequencia)) AS ocorrencias
                FROM ref.frequencia_nome
                WHERE frequencia_nome_versao_id=@ref
                  AND tipo=N'NOME'
                  AND sexo=N'FEMININO'
                  AND periodo_nascimento=N'TODOS'
                  AND escopo_geografico=N'BRASIL'
                  AND uf_codigo='00'
                  AND municipio_codigo='0000000'
                GROUP BY valor_normalizado
            ),
            totals AS (
                SELECT atributo,SUM(ocorrencias) AS populacao_referencia
                FROM domain_rows
                GROUP BY atributo
            )
            SELECT d.atributo,d.valor_normalizado,d.ocorrencias,t.populacao_referencia
            FROM domain_rows d
            JOIN totals t ON t.atributo=d.atributo
            ORDER BY d.atributo,d.valor_normalizado;
            """,
            connection);
        command.Parameters.Add("@ref", SqlDbType.BigInt).Value = referenceVersionId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurrences = reader.GetInt64(2);
            var population = reader.GetInt64(3);
            if (population <= 0 || occurrences <= 0 || occurrences > population)
                throw new InvalidDataException("Contagem TF publicada inválida.");
            var frequency = Math.Round(
                decimal.Divide(occurrences, population),
                12,
                MidpointRounding.ToEven);
            entries.Add(new NominalTermFrequencyEntry(
                reader.GetString(0),
                reader.GetString(1),
                occurrences,
                population,
                frequency));
        }

        if (!entries.Any(x => x.Attribute == NominalTermFrequencySnapshot.PersonFirstNameAttribute)
            || !entries.Any(x => x.Attribute == NominalTermFrequencySnapshot.MotherFirstNameAttribute))
            throw new InvalidOperationException(
                "TF V8 exige frequências publicadas de prenome para pessoa SP 3550308 e mãe Brasil/FEMININO.");

        return new PreparedNominalTermFrequency(
            NominalTermFrequencySnapshot.Create(entries),
            entries,
            referenceVersionId);
    }

    public static async Task WriteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid modelId,
        PreparedNominalTermFrequency prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(prepared);

        foreach (var entry in prepared.Entries)
        {
            await using var command = new SqlCommand(
                """
                INSERT identidade.frequencia_linkage(
                    modelo_id,atributo,valor_normalizado,ocorrencias,
                    populacao_referencia,frequencia)
                VALUES(@model,@attribute,@value,@occurrences,@population,@frequency);
                """,
                connection,
                transaction);
            command.Parameters.Add("@model", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@attribute", SqlDbType.NVarChar, 80).Value = entry.Attribute;
            command.Parameters.Add("@value", SqlDbType.NVarChar, 500).Value = entry.ValueNormalized;
            command.Parameters.Add("@occurrences", SqlDbType.BigInt).Value = entry.Occurrences;
            command.Parameters.Add("@population", SqlDbType.BigInt).Value = entry.PopulationReference;
            var frequency = command.Parameters.Add("@frequency", SqlDbType.Decimal);
            frequency.Precision = 20;
            frequency.Scale = 12;
            frequency.Value = entry.Frequency;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task RequireCoverageAsync(
        SqlConnection connection,
        long referenceVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT
                SUM(CASE WHEN tipo=N'NOME' AND escopo_geografico=N'MUNICIPIO'
                              AND inclui_sexo=0 AND inclui_periodo_nascimento=0
                              AND cobertura=N'COMPLETA' THEN 1 ELSE 0 END),
                SUM(CASE WHEN tipo=N'NOME' AND escopo_geografico=N'BRASIL'
                              AND inclui_sexo=1 AND inclui_periodo_nascimento=0
                              AND cobertura=N'COMPLETA' THEN 1 ELSE 0 END)
            FROM ref.frequencia_nome_cobertura
            WHERE frequencia_nome_versao_id=@ref;
            """,
            connection);
        command.Parameters.Add("@ref", SqlDbType.BigInt).Value = referenceVersionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.IsDBNull(0) || reader.GetInt32(0) < 1
            || reader.IsDBNull(1) || reader.GetInt32(1) < 1)
            throw new InvalidOperationException(
                "TF V8 exige cobertura COMPLETA de NOME/MUNICIPIO e NOME/BRASIL com sexo na referência fixada.");
    }
}
