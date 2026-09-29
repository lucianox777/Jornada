using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Read-only SQL predicate for the GENERATE_DRAFT fail-closed gate.
/// Loading and reactivation belong exclusively to explicit environment bootstrap.</summary>
public static class ActiveNameFrequencyReferenceQuery
{
    public static async Task<bool> HasActiveAsync(IOperationalSqlAdapter operationalSql)
    {
        ArgumentNullException.ThrowIfNull(operationalSql);
        await using var connection = await operationalSql.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM ref.frequencia_nome_versao v
            WHERE v.status='ATIVA'
              AND DATALENGTH(v.conteudo_sha256)=32
              AND EXISTS (
                  SELECT 1 FROM ref.frequencia_nome n
                  WHERE n.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                    AND n.tipo='NOME')
              AND EXISTS (
                  SELECT 1 FROM ref.frequencia_nome s
                  WHERE s.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                    AND s.tipo='SOBRENOME');
            """;
        var value = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
