using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class IbgeNominalUDerivedReferenceSqlTests
{
    [Test]
    public async Task Published_derived_reference_is_complete_unique_and_immutable()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar o teste SQL Server.");
        var database = new SqlConnectionStringBuilder(connectionString!).InitialCatalog ?? string.Empty;
        if (!new[] { "test", "dev", "local" }
            .Any(x => database.Contains(x, StringComparison.OrdinalIgnoreCase)))
            Assert.Fail("Teste de mutação somente em banco Test/Dev/Local.");

        await using var connection = new SqlConnection(connectionString!);
        await connection.OpenAsync();
        var dbDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(dbDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(
            dbDir, "migrations", "20260912_Frequencia_Nomes_Referencia.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(
            dbDir, "migrations", "20260926_Ibge_Nominal_U_Derived_371.sql"));

        var marker = Guid.NewGuid().ToString("N")[..12];
        var sourceHash = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        var resultHash = Enumerable.Repeat((byte)0x7B, 32).ToArray();

        await using var create = new SqlCommand(
            """
            INSERT ref.frequencia_nome_versao(
                codigo,fonte,edicao,data_referencia,publicado_em,status)
            VALUES(@code,N'IBGE - test reference',N'test edition',
                '2022-08-01','2025-11-04',N'CARREGANDO');
            DECLARE @src BIGINT=CONVERT(BIGINT,SCOPE_IDENTITY());
            INSERT ref.ibge_u_referencia(
                frequencia_nome_versao_id,conteudo_origem_sha256,metodo_versao,
                construcao_versao,canal_versao,comparador_versao,
                recorte_prenome,seed,pares,vocabulario_prenomes,vocabulario_sobrenomes,
                ocorrencias_prenomes,ocorrencias_sobrenomes,
                colisoes_prenome,colisoes_sobrenome,colisoes_nome_completo,status)
            VALUES(@src,@sha,N'TEST_METHOD_V1',N'TEST_JOINT_V1',N'TEST_CHANNEL_V1',
                N'WholeNameJaroWinklerV1',N'TODOS',17,100,2,2,200,200,
                0.2,0.3,0.06,N'CARREGANDO');
            SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
            """, connection);
        create.Parameters.Add("@code", SqlDbType.NVarChar, 80).Value = "TEST_U_" + marker;
        create.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = sourceHash;
        var id = Convert.ToInt64(await create.ExecuteScalarAsync());

        await using (var incomplete = new SqlCommand(
            """
            UPDATE ref.ibge_u_referencia
            SET status=N'PRONTA',resultado_sha256=@hash,
                publicado_em=SYSDATETIMEOFFSET()
            WHERE ibge_u_referencia_id=@id;
            """, connection))
        {
            incomplete.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            incomplete.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = resultHash;
            var failure = Assert.ThrowsAsync<SqlException>(
                async () => await incomplete.ExecuteNonQueryAsync());
            Assert.That(failure!.Number, Is.EqualTo(52085),
                "A publicação sem os quatro estados deve falhar.");
        }

        await using (var states = new SqlCommand(
            """
            INSERT ref.ibge_u_referencia_estado(
                ibge_u_referencia_id,estado,suporte,probabilidade,erro_padrao)
            VALUES
                (@id,N'EXACT',100,1.0,0.0),
                (@id,N'HIGH',0,0.0,0.0),
                (@id,N'MEDIUM',0,0.0,0.0),
                (@id,N'LOW',0,0.0,0.0);
            """, connection))
        {
            states.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            Assert.That(await states.ExecuteNonQueryAsync(), Is.EqualTo(4));
        }

        await using (var publish = new SqlCommand(
            """
            UPDATE ref.ibge_u_referencia
            SET status=N'PRONTA',resultado_sha256=@hash,
                publicado_em=SYSDATETIMEOFFSET()
            WHERE ibge_u_referencia_id=@id;
            """, connection))
        {
            publish.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            publish.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = resultHash;
            Assert.That(await publish.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        await using (var immutableState = new SqlCommand(
            "UPDATE ref.ibge_u_referencia_estado SET probabilidade=0.9 WHERE ibge_u_referencia_id=@id AND estado=N'EXACT';",
            connection))
        {
            immutableState.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            var ex = Assert.ThrowsAsync<SqlException>(
                async () => await immutableState.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(52082));
        }
        await using (var immutableParent = new SqlCommand(
            "DELETE ref.ibge_u_referencia WHERE ibge_u_referencia_id=@id;",
            connection))
        {
            immutableParent.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            var ex = Assert.ThrowsAsync<SqlException>(
                async () => await immutableParent.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(52083));
        }

        await using (var verify = new SqlCommand(
            """
            SELECT COUNT(*) FROM ref.ibge_u_referencia u
            WHERE u.ibge_u_referencia_id=@id
              AND u.status=N'PRONTA' AND u.resultado_sha256=@hash
              AND (SELECT COUNT(*) FROM ref.ibge_u_referencia_estado e
                   WHERE e.ibge_u_referencia_id=u.ibge_u_referencia_id)=4;
            """, connection))
        {
            verify.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            verify.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = resultHash;
            Assert.That(Convert.ToInt32(await verify.ExecuteScalarAsync()), Is.EqualTo(1));
        }
    }
}
