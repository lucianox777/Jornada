using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed record PersonOriginResolution(
    long PessoaOrigemId,
    long BasePessoaOrigemId,
    string BaseCodigo,
    string CodigoPessoaOrigem);

internal static class PersonOriginSqlPersistence
{
    public static async Task<PersonOriginResolution?> ResolveOrCreateAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long sistemaOrigemId,
        string? codigoPessoaOrigem,
        string? codigoBasePessoaOrigem,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigoPessoaOrigem))
            return null;

        var baseResolution = await ResolveAuthorizedBaseAsync(
            connection,
            tx,
            sistemaOrigemId,
            codigoBasePessoaOrigem,
            ct);

        var codigo = codigoPessoaOrigem.Trim();
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT pessoa_origem_id
                FROM silver.pessoa_origem WITH (UPDLOCK,HOLDLOCK)
                WHERE base_pessoa_origem_id=@base AND codigo_pessoa_origem=@codigo;
                """;
            find.Parameters.AddWithValue("@base", baseResolution.BasePessoaOrigemId);
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
            {
                var pessoaOrigemId = Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture);
                await RegisterSystemUseAsync(connection, tx, pessoaOrigemId, sistemaOrigemId, ct);
                return new PersonOriginResolution(pessoaOrigemId, baseResolution.BasePessoaOrigemId, baseResolution.BaseCodigo, codigo);
            }
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
            OUTPUT INSERTED.pessoa_origem_id
            VALUES(@sistema,@codigo,@base);
            """;
        insert.Parameters.AddWithValue("@sistema", sistemaOrigemId);
        insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
        insert.Parameters.AddWithValue("@base", baseResolution.BasePessoaOrigemId);
        var createdId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        await RegisterSystemUseAsync(connection, tx, createdId, sistemaOrigemId, ct);
        return new PersonOriginResolution(createdId, baseResolution.BasePessoaOrigemId, baseResolution.BaseCodigo, codigo);
    }

    private static async Task<(long BasePessoaOrigemId, string BaseCodigo)> ResolveAuthorizedBaseAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long sistemaOrigemId,
        string? codigoBasePessoaOrigem,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = string.IsNullOrWhiteSpace(codigoBasePessoaOrigem)
            ? """
                SELECT b.base_pessoa_origem_id,b.codigo
                FROM ref.sistema_origem_base_pessoa sb
                JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=sb.base_pessoa_origem_id
                WHERE sb.sistema_origem_id=@sistema AND sb.padrao=1 AND sb.ativo=1 AND b.ativo=1;
                """
            : """
                SELECT b.base_pessoa_origem_id,b.codigo
                FROM ref.sistema_origem_base_pessoa sb
                JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=sb.base_pessoa_origem_id
                WHERE sb.sistema_origem_id=@sistema AND sb.ativo=1 AND b.ativo=1
                  AND b.codigo=@base_codigo;
                """;
        command.Parameters.AddWithValue("@sistema", sistemaOrigemId);
        if (!string.IsNullOrWhiteSpace(codigoBasePessoaOrigem))
            command.Parameters.Add(new SqlParameter("@base_codigo", SqlDbType.NVarChar, 120) { Value = codigoBasePessoaOrigem.Trim() });

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidDataException("Base de Pessoa de origem não autorizada para o sistema transmissor.");
        var result = (reader.GetInt64(0), reader.GetString(1));
        if (await reader.ReadAsync(ct))
            throw new InvalidDataException("Sistema possui mais de uma base padrão ativa de Pessoa.");
        return result;
    }

    private static async Task RegisterSystemUseAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long pessoaOrigemId,
        long sistemaOrigemId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            MERGE silver.pessoa_origem_sistema AS t
            USING (SELECT @pessoa AS pessoa_origem_id,@sistema AS sistema_origem_id) AS s
            ON t.pessoa_origem_id=s.pessoa_origem_id AND t.sistema_origem_id=s.sistema_origem_id
            WHEN MATCHED THEN UPDATE SET ultima_observacao_em=SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN
              INSERT(pessoa_origem_id,sistema_origem_id,primeira_observacao_em,ultima_observacao_em)
              VALUES(s.pessoa_origem_id,s.sistema_origem_id,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@pessoa", pessoaOrigemId);
        command.Parameters.AddWithValue("@sistema", sistemaOrigemId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
