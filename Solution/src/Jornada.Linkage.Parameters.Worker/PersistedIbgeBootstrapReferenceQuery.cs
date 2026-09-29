using Jornada.Contracts;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Resolve a fonte do bootstrap IBGE inicial já materializado. A fonte pode ter
/// deixado de ser ATIVA depois da carga: GENERATE_DRAFT consome os derivados
/// imutáveis persistidos e nunca reativa/carrega a referência implicitamente.
/// </summary>
public static class PersistedIbgeBootstrapReferenceQuery
{
    public const string CanonicalReferenceCode = "CENSO2022_NOMES_BRASIL_V1";

    public static async Task<IbgeNominalUReferenceInfo> RequireAsync(
        SqlConnection connection,
        int seed,
        int pairCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pairCount);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.frequencia_nome_versao_id,v.codigo,v.fonte,v.conteudo_sha256
            FROM ref.frequencia_nome_versao v
            WHERE v.codigo=@codigo
              AND DATALENGTH(v.conteudo_sha256)=32
              AND EXISTS (
                  SELECT 1
                  FROM ref.ibge_u_referencia u
                  WHERE u.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                    AND u.conteudo_origem_sha256=v.conteudo_sha256
                    AND u.status=N'PRONTA'
                    AND u.metodo_versao=@metodo
                    AND u.construcao_versao=@construcao
                    AND u.canal_versao=@canal
                    AND u.comparador_versao=@comparador
                    AND u.recorte_prenome=N'TODOS'
                    AND u.seed=@seed_pessoa
                    AND u.pares=@pares)
              AND EXISTS (
                  SELECT 1
                  FROM ref.ibge_u_referencia u
                  WHERE u.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                    AND u.conteudo_origem_sha256=v.conteudo_sha256
                    AND u.status=N'PRONTA'
                    AND u.metodo_versao=@metodo
                    AND u.construcao_versao=@construcao
                    AND u.canal_versao=@canal
                    AND u.comparador_versao=@comparador
                    AND u.recorte_prenome=N'FEMININO'
                    AND u.seed=@seed_mae
                    AND u.pares=@pares)
            ORDER BY v.frequencia_nome_versao_id DESC;
            """;
        command.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = CanonicalReferenceCode;
        command.Parameters.Add("@metodo", SqlDbType.NVarChar, 80).Value = IbgeNominalUBootstrapOptions.MethodVersion;
        command.Parameters.Add("@construcao", SqlDbType.NVarChar, 100).Value = IbgeNominalUBootstrapOptions.JointConstructionVersion;
        command.Parameters.Add("@canal", SqlDbType.NVarChar, 100).Value = IbgeNominalUBootstrapOptions.ObservationChannelVersion;
        command.Parameters.Add("@comparador", SqlDbType.NVarChar, 60).Value = NameComparisonContract.WholeNameJaroWinklerV1.ToString();
        command.Parameters.Add("@seed_pessoa", SqlDbType.Int).Value = seed;
        command.Parameters.Add("@seed_mae", SqlDbType.Int).Value = unchecked(seed + 1);
        command.Parameters.Add("@pares", SqlDbType.Int).Value = pairCount;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "GENERATE_DRAFT exige bootstrap IBGE inicial persistido e íntegro. " +
                "Execute ENSURE_NAME_FREQUENCY_SNAPSHOT e ENSURE_IBGE_NOMINAL_U_REFERENCE uma vez na preparação do ambiente; " +
                "rascunhos posteriores não exigem referência IBGE ATIVA.");

        var result = new IbgeNominalUReferenceInfo(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToHexString((byte[])reader.GetValue(3)).ToLowerInvariant());

        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "Mais de um bootstrap IBGE canônico persistido satisfaz a chave corrente; seleção implícita recusada.");

        return result;
    }
}
