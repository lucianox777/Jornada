using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Consulta leve e somente leitura para o gate fail-closed de GENERATE_DRAFT.
/// Carga, validação integral e reativação pertencem exclusivamente ao bootstrap explícito.</summary>
public static class ActiveNameFrequencyReferenceQuery
{
    public static async Task<bool> HasActiveAsync(IOperationalSqlAdapter operationalSql, string normalizationVersion)
    {
        ArgumentNullException.ThrowIfNull(operationalSql);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        await using var connection = await operationalSql.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM ref.frequencia_nome_versao v
            WHERE v.status='ATIVA'
              AND DATALENGTH(v.conteudo_sha256)=32
              AND v.normalizacao_versao=@normalizacao
              AND v.manifest_schema_version=1
              AND EXISTS (SELECT 1 FROM ref.frequencia_nome n
                          WHERE n.frequencia_nome_versao_id=v.frequencia_nome_versao_id AND n.tipo='NOME')
              AND EXISTS (SELECT 1 FROM ref.frequencia_nome s
                          WHERE s.frequencia_nome_versao_id=v.frequencia_nome_versao_id AND s.tipo='SOBRENOME');
            """;
        var parameter=command.CreateParameter();
        parameter.ParameterName="@normalizacao";
        parameter.Value=normalizationVersion;
        command.Parameters.Add(parameter);
        var value=await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture)==1;
    }
}
