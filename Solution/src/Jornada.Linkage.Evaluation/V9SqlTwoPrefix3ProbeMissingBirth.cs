using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public static partial class V9SqlTwoPrefix3Probe
{
    /// <summary>
    /// Fifteenth physical TEMP-table pass, after the immutable 14-pass union:
    /// P3 AND M3 with at least one missing DOB. Does not promote any policy.
    /// </summary>
    private static async Task<V9NameMotherMissingBirthSqlAudit.Report>
        RunMissingBirthAsync(SqlConnection sql,
            IReadOnlyList<Observation> observations,
            V9TwoFieldSqlCoverageAudit.Report baseline,
            int hardPairCap,CancellationToken ct)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development"
            || sql.Database!="JornadaHarness"
            || hardPairCap is < 1 or > 1_000_000)
            throw new InvalidOperationException("Passe sem DOB exige JornadaHarness DEV.");
        var byId=observations.ToDictionary(x=>x.Id);
        if(byId.Count!=observations.Count)
            throw new InvalidDataException("Observações duplicadas no passe sem DOB.");

        await using(var create=new SqlCommand(
            """
            SELECT DISTINCT a.obs_id a,b.obs_id b,a.part
            INTO #v9p3missing
            FROM #v9p3obs a JOIN #v9p3obs b
              ON a.part=b.part AND a.obs_id<b.obs_id
             AND a.nome_p3=b.nome_p3 AND a.mae_p3=b.mae_p3
            WHERE a.part IN ('TRAIN','VALIDATION')
              AND a.nome_p3 IS NOT NULL AND b.nome_p3 IS NOT NULL
              AND a.mae_p3 IS NOT NULL AND b.mae_p3 IS NOT NULL
              AND (a.nascimento IS NULL OR b.nascimento IS NULL)
              AND a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor;
            CREATE UNIQUE CLUSTERED INDEX IX_v9p3missing ON #v9p3missing(a,b);
            """,sql){CommandTimeout=600})
            await create.ExecuteNonQueryAsync(ct);

        long physicalCount;
        await using(var count=new SqlCommand(
            "SELECT COUNT_BIG(*) FROM #v9p3missing;",sql){CommandTimeout=600})
            physicalCount=(long)(await count.ExecuteScalarAsync(ct)??-1L);
        if(physicalCount<0 || physicalCount>hardPairCap)
            throw new InvalidDataException("Passe P3+M3 sem DOB excede cap de 1 milhão.");

        var edges=new List<V9SqlNamePrefix3BirthAudit.Pair>();
        await using(var command=new SqlCommand(
            "SELECT a,b,part FROM #v9p3missing ORDER BY a,b;",sql)
            {CommandTimeout=600})
        await using(var reader=await command.ExecuteReaderAsync(ct))
        {
            while(await reader.ReadAsync(ct))
            {
                if(edges.Count>=hardPairCap)
                    throw new InvalidDataException("Cap da leitura P3+M3 sem DOB.");
                var aId=reader.GetInt32(0);
                var bId=reader.GetInt32(1);
                var part=reader.GetString(2);
                if(!byId.TryGetValue(aId,out var a)
                    || !byId.TryGetValue(bId,out var b)
                    || aId>=bId || part is not ("TRAIN" or "VALIDATION")
                    || a.Partition!=part || b.Partition!=part
                    || a.Gestor==b.Gestor || a.Cpf is not null || b.Cpf is not null
                    || (a.Birth.HasValue && b.Birth.HasValue)
                    || a.Person3 is null || a.Person3!=b.Person3
                    || a.Mother3 is null || a.Mother3!=b.Mother3)
                    throw new InvalidDataException("SQL P3+M3 sem DOB fora do frame.");
                edges.Add(new(aId,bId,part,a.Base==b.Base));
            }
        }
        if(edges.Count!=physicalCount)
            throw new InvalidDataException("Leitura incompleta do passe sem DOB.");

        return V9NameMotherMissingBirthSqlAudit.Analyze(
            observations.Where(x=>x.Partition is "TRAIN" or "VALIDATION").ToArray(),
            edges,baseline,hardPairCap);
    }
}
