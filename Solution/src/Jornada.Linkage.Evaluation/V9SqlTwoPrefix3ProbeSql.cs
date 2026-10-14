using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public static partial class V9SqlTwoPrefix3Probe
{
    private static async Task<IReadOnlyList<V9SqlNamePrefix3BirthAudit.Pair>>
        RunPassAsync(SqlConnection sql,string predicate,
            IReadOnlyDictionary<int,Observation> byId,int cap,CancellationToken ct)
    {
        if(predicate is not ("a.mae_p3=b.mae_p3 AND a.nascimento=b.nascimento"
            or "a.nome_p3=b.nome_p3 AND a.mae_p3=b.mae_p3"))
            throw new InvalidDataException("Predicado SQL não pré-registrado.");
        await using(var cmd=new SqlCommand($"""
            INSERT INTO #v9p3extra(a,b,part)
            SELECT DISTINCT a.obs_id,b.obs_id,a.part
            FROM #v9p3obs a JOIN #v9p3obs b
              ON a.part=b.part AND a.obs_id<b.obs_id
             AND {predicate}
            WHERE a.part IN ('TRAIN','VALIDATION')
              AND a.mae_p3 IS NOT NULL AND b.mae_p3 IS NOT NULL
              AND a.nascimento IS NOT NULL AND b.nascimento IS NOT NULL
              AND a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
              AND NOT EXISTS(SELECT 1 FROM #v9pairs x
                            WHERE x.a=a.obs_id AND x.b=b.obs_id);
            """,sql){CommandTimeout=600})
            await cmd.ExecuteNonQueryAsync(ct);
        long count;
        await using(var query=new SqlCommand(
            "SELECT COUNT_BIG(*) FROM #v9p3extra;",sql))
            count=(long)(await query.ExecuteScalarAsync(ct)??-1L);
        if(count<0 || count>cap)
            throw new InvalidDataException("Novos prefixos excedem limite de pares.");
        var actual=await ReadPairsAsync(sql,
            "SELECT a,b,part FROM #v9p3extra ORDER BY a,b;",
            byId,cap,ct);
        if(actual.Count!=count)
            throw new InvalidDataException("Contagem SQL divergiu de leitura.");
        await using(var clear=new SqlCommand("TRUNCATE TABLE #v9p3extra;",sql))
            await clear.ExecuteNonQueryAsync(ct);
        return actual;
    }

    private static async Task<List<V9SqlNamePrefix3BirthAudit.Pair>>
        ReadPairsAsync(SqlConnection sql,string query,
            IReadOnlyDictionary<int,Observation> byId,int cap,CancellationToken ct)
    {
        var result=new List<V9SqlNamePrefix3BirthAudit.Pair>();
        await using(var cmd=new SqlCommand(query,sql){CommandTimeout=600})
        await using(var reader=await cmd.ExecuteReaderAsync(ct))
        {
            while(await reader.ReadAsync(ct))
            {
                if(result.Count>=cap)throw new InvalidDataException("Cap na leitura SQL.");
                var aid=reader.GetInt32(0);var bid=reader.GetInt32(1);
                if(!byId.TryGetValue(aid,out var a)
                    || !byId.TryGetValue(bid,out var b))
                    throw new InvalidDataException("SQL referiu observação inexistente.");
                var part=reader.GetString(2);
                if(aid>=bid || part is not ("TRAIN" or "VALIDATION")
                    || a.Partition!=part || b.Partition!=part
                    || a.Gestor==b.Gestor || a.Cpf is not null
                    || b.Cpf is not null || !a.Birth.HasValue || !b.Birth.HasValue)
                    throw new InvalidDataException("SQL retornou TEST ou par fora do frame.");
                result.Add(new(aid,bid,part,a.Base==b.Base));
            }
        }
        return result;
    }
}
