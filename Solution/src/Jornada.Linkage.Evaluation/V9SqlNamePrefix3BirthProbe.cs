using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public static class V9SqlNamePrefix3BirthProbe
{
    public sealed record Observation(int Id,string Base,string Partition,
        string Gestor,string? Prefix3,DateOnly? Birth,string? Cpf);

    public static async Task<V9SqlNamePrefix3BirthAudit.Report> RunAsync(
        SqlConnection sql,IReadOnlyList<Observation> observations,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension> oldPasses,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass> newPasses,
        IReadOnlyList<V9BlockingTruthLossAudit.Pair> truth,
        V9SqlElevenPassResidualAudit.Report eleven,int cap,
        CancellationToken ct=default)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development"
            || sql.Database!="JornadaHarness" || cap is < 1 or > 1_000_000)
            throw new InvalidOperationException("Prefixo3 exige JornadaHarness DEV.");
        var byId=observations.ToDictionary(x=>x.Id);
        if(byId.Count!=observations.Count)
            throw new InvalidDataException("Projeção duplicada.");
        await using(var q=new SqlCommand(
            """
            SELECT DISTINCT a.obs_id a,b.obs_id b,a.part
            INTO #v9prefix3 FROM #v9obs a JOIN #v9obs b
            ON a.part=b.part AND a.nome_p3=b.nome_p3
            AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
            WHERE a.part IN ('TRAIN','VALIDATION')
            AND a.nome_p3 IS NOT NULL AND b.nome_p3 IS NOT NULL
            AND a.nascimento IS NOT NULL AND b.nascimento IS NOT NULL
            AND a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
            AND NOT EXISTS (SELECT 1 FROM #v9pairs p
              WHERE p.a=a.obs_id AND p.b=b.obs_id);
            """,sql){CommandTimeout=600})await q.ExecuteNonQueryAsync(ct);
        long count;
        await using(var q=new SqlCommand("SELECT COUNT_BIG(*) FROM #v9prefix3",sql))
            count=(long)(await q.ExecuteScalarAsync(ct) ?? -1L);
        if(count<0 || count>cap)throw new InvalidDataException("Prefixo3 excedeu cap.");
        var added=new List<V9SqlNamePrefix3BirthAudit.Pair>();
        await using(var q=new SqlCommand("SELECT a,b,part FROM #v9prefix3 ORDER BY a,b",sql)
            {CommandTimeout=600})
        await using(var reader=await q.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct))
            {
                var a=byId[reader.GetInt32(0)];
                var b=byId[reader.GetInt32(1)];
                var p=reader.GetString(2);
                if(added.Count>=cap || p is not ("TRAIN" or "VALIDATION")
                   || a.Partition!=p || b.Partition!=p || a.Id>=b.Id
                   || a.Cpf is not null || b.Cpf is not null || a.Gestor==b.Gestor
                   || a.Prefix3 is null || a.Prefix3!=b.Prefix3
                   || !a.Birth.HasValue || a.Birth!=b.Birth)
                    throw new InvalidDataException("Par SQL prefixo3 inválido.");
                added.Add(new(a.Id,b.Id,p,a.Base==b.Base));
            }
        if(added.Count!=count)throw new InvalidDataException("Perda de pares SQL.");
        var prior=new List<V9SqlNamePrefix3BirthAudit.Pair>();
        prior.AddRange(baseline.Select(x=>new V9SqlNamePrefix3BirthAudit.Pair(
            x.A,x.B,x.Partition,x.IsReferenceMatch)));
        foreach(var pass in oldPasses)prior.AddRange(pass.ExtraPairs.Select(x=>
            new V9SqlNamePrefix3BirthAudit.Pair(x.A,x.B,x.Partition,x.IsReferenceMatch)));
        foreach(var pass in newPasses)prior.AddRange(pass.ExtraPairs.Select(x=>
            new V9SqlNamePrefix3BirthAudit.Pair(x.A,x.B,x.Partition,x.Match)));
        return V9SqlNamePrefix3BirthAudit.Analyze(
            observations.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9SqlNamePrefix3BirthAudit.Observation(
                    x.Id,x.Partition,x.Gestor,x.Cpf is null&&x.Birth.HasValue,
                    x.Prefix3,x.Birth)).ToArray(),
            prior,added,truth.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9SqlNamePrefix3BirthAudit.Pair(
                    x.LowerObservationId,x.HigherObservationId,x.Partition,true))
                .ToArray(),eleven.Splits,cap);
    }
}
