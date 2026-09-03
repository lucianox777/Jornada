using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class IdentityReplayInvariantTests
{
    [Test]
    public async Task Recompose_gold_person_is_idempotent_on_business_state()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await PrepareAsync(connection);
        var uuid = (Guid)(await ScalarAsync(connection, "SELECT TOP(1) pessoa_uuid FROM gold.pessoa ORDER BY pessoa_uuid")
            ?? throw new AssertionException("Seed deve produzir ao menos uma Pessoa Gold."));
        var before = await BusinessStateAsync(connection, uuid);
        await ExecRecomposeAsync(connection, uuid);
        var once = await BusinessStateAsync(connection, uuid);
        await ExecRecomposeAsync(connection, uuid);
        var twice = await BusinessStateAsync(connection, uuid);
        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EqualTo(before), "Primeira recomposição não pode alterar estado de negócio sem nova evidência.");
            Assert.That(twice, Is.EqualTo(once), "Replay da recomposição deve convergir ao mesmo estado de negócio.");
        });
    }

    [Test]
    public async Task Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await PrepareAsync(connection);
        var uuids = new List<Guid>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT pessoa_uuid FROM identidade.pessoa WHERE status='ATIVO' ORDER BY pessoa_uuid";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) uuids.Add(r.GetGuid(0));
        }
        foreach (var uuid in uuids) await ExecRecomposeAsync(connection, uuid);

        const string sql = """
            ;WITH chain AS(
                SELECT p.pessoa_uuid origem,p.pessoa_uuid atual,p.pessoa_uuid_sucessor proximo,
                       CAST('|' + CONVERT(varchar(36),p.pessoa_uuid) + '|' AS varchar(max)) caminho,0 profundidade
                FROM identidade.pessoa p
                UNION ALL
                SELECT c.origem,p.pessoa_uuid,p.pessoa_uuid_sucessor,
                       CAST(c.caminho + CONVERT(varchar(36),p.pessoa_uuid) + '|' AS varchar(max)),c.profundidade+1
                FROM chain c JOIN identidade.pessoa p ON p.pessoa_uuid=c.proximo
                WHERE c.proximo IS NOT NULL AND c.profundidade<128
                  AND c.caminho NOT LIKE '%|' + CONVERT(varchar(36),p.pessoa_uuid) + '|%'
            ), cycle_roots AS(
                SELECT DISTINCT c.origem
                FROM chain c
                WHERE c.proximo IS NOT NULL
                  AND c.caminho LIKE '%|' + CONVERT(varchar(36),c.proximo) + '|%'
            )
            SELECT
              (SELECT COUNT(*) FROM cycle_roots) ciclos,
              (SELECT COUNT(*) FROM identidade.identity_map im JOIN identidade.pessoa p ON p.pessoa_uuid=im.pessoa_uuid
                WHERE im.tipo='CPF' AND im.vigencia_fim IS NULL AND im.estado='ATIVO' AND p.status='FUNDIDO') cpf_ativo_em_fundido,
              (SELECT COUNT(*) FROM gold.pessoa g JOIN identidade.pessoa p ON p.pessoa_uuid=g.pessoa_uuid WHERE p.status<>'ATIVO') gold_nao_ativo,
              (SELECT COUNT(*) FROM serving.registro_integrado WHERE estado_atribuicao_identidade='ATRIBUIDA' AND pessoa_uuid IS NULL) fato_atribuido_sem_uuid,
              (SELECT COUNT(*) FROM serving.registro_integrado WHERE estado_atribuicao_identidade<>'ATRIBUIDA' AND pessoa_uuid IS NOT NULL) fato_pendente_com_uuid
            OPTION (MAXRECURSION 256);
            """;
        await using var q = connection.CreateCommand(); q.CommandText = sql;
        await using var reader = await q.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            for (var i=0;i<5;i++) Assert.That(reader.GetInt32(i), Is.Zero, $"invariante global índice {i}");
        });
    }

    private static async Task PrepareAsync(SqlConnection connection)
    {
        var dir=Path.Combine(AppContext.BaseDirectory,"database");
        await SqlBatchRunner.ExecuteFileAsync(connection,Path.Combine(dir,"Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,Path.Combine(dir,"Jornada_Seed_Dev.sql"));
    }
    private static async Task ExecRecomposeAsync(SqlConnection c, Guid uuid)
    {
        await using var cmd=c.CreateCommand(); cmd.CommandType=CommandType.StoredProcedure; cmd.CommandText="identidade.sp_recompor_gold_pessoa"; cmd.Parameters.AddWithValue("@pessoa_uuid",uuid); await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<string> BusinessStateAsync(SqlConnection c, Guid uuid)
    {
        await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT CONCAT(COALESCE(cpf,''),'|',status_cpf,'|',nome_completo,'|',CONVERT(char(10),data_nascimento,23),'|',nome_mae,'|',fontes_distintas,'|',estado_concordancia) FROM gold.pessoa WHERE pessoa_uuid=@u"; cmd.Parameters.AddWithValue("@u",uuid); return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
    private static async Task<object?> ScalarAsync(SqlConnection c,string sql){ await using var cmd=c.CreateCommand(); cmd.CommandText=sql; return await cmd.ExecuteScalarAsync(); }
    private static string RequireIntegrationConnection()
    {
        var cs=Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(cs)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var db=new SqlConnectionStringBuilder(cs).InitialCatalog ?? "";
        if(!db.Contains("test",StringComparison.OrdinalIgnoreCase)&&!db.Contains("dev",StringComparison.OrdinalIgnoreCase)&&!db.Contains("local",StringComparison.OrdinalIgnoreCase)) Assert.Fail("Banco de integração deve ser Test/Dev/Local.");
        return cs;
    }
}
