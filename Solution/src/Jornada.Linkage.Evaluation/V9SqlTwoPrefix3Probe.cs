using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Runs two complementary physical SQL equality joins over materialized
/// first-three-character person/mother keys. Existing rules remain untouched.
/// </summary>
public static partial class V9SqlTwoPrefix3Probe
{
    public sealed record Observation(int Id,string Base,string Partition,
        string Gestor,string? Person3,string? Mother3,DateOnly? Birth,string? Cpf,
        string? PersonFull=null,string? MotherFull=null,
        string? PersonPhonetic=null,string? MotherPhonetic=null);

    public static async Task<V9SqlTwoPrefix3Audit.Report> RunAsync(
        SqlConnection sql,IReadOnlyList<Observation> observations,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension> sixPasses,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass> fivePasses,
        IReadOnlyList<V9BlockingTruthLossAudit.Pair> truth,
        V9SqlNamePrefix3BirthAudit.Report twelve,int cap,CancellationToken ct)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development"
            || sql.Database!="JornadaHarness" || cap is < 1 or > 1_000_000)
            throw new InvalidOperationException("Prefixo3 exige JornadaHarness DEV.");
        var byId=observations.ToDictionary(x=>x.Id);
        if(byId.Count!=observations.Count)
            throw new InvalidDataException("Observações SQL duplicadas.");

        await using(var q=new SqlCommand(
            """
            SELECT obs_id,part,gestor,cpf,nascimento,nome_p3,
              CASE WHEN LEN(mae)>=3 THEN LEFT(mae,3)
                   ELSE NULL END AS mae_p3
            INTO #v9p3obs FROM #v9obs;
            CREATE INDEX IX_v9_p3_mother_birth
                ON #v9p3obs(part,mae_p3,nascimento);
            CREATE INDEX IX_v9_p3_person_mother
                ON #v9p3obs(part,nome_p3,mae_p3);
            CREATE TABLE #v9p3extra(
                a INT NOT NULL,b INT NOT NULL,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                PRIMARY KEY CLUSTERED(a,b));
            """,sql){CommandTimeout=600})
            await q.ExecuteNonQueryAsync(ct);

        var twelveEdges=new List<V9SqlNamePrefix3BirthAudit.Pair>();
        twelveEdges.AddRange(baseline.Select(x=>new V9SqlNamePrefix3BirthAudit.Pair(
            x.A,x.B,x.Partition,x.IsReferenceMatch)));
        foreach(var pass in sixPasses)
            twelveEdges.AddRange(pass.ExtraPairs.Select(x=>
                new V9SqlNamePrefix3BirthAudit.Pair(
                    x.A,x.B,x.Partition,x.IsReferenceMatch)));
        foreach(var pass in fivePasses)
            twelveEdges.AddRange(pass.ExtraPairs.Select(x=>
                new V9SqlNamePrefix3BirthAudit.Pair(
                    x.A,x.B,x.Partition,x.Match)));
        twelveEdges.AddRange(await ReadPairsAsync(sql,
            "SELECT a,b,part FROM #v9prefix3 ORDER BY a,b;",
            byId,cap,ct));

        var passes=new List<V9SqlTwoPrefix3Audit.Pass>();
        foreach(var (name,predicate) in new[]{
            (V9SqlTwoPrefix3Audit.MotherDate,
                "a.mae_p3=b.mae_p3 AND a.nascimento=b.nascimento"),
            (V9SqlTwoPrefix3Audit.PersonMother,
                "a.nome_p3=b.nome_p3 AND a.mae_p3=b.mae_p3")})
        {
            var edges=await RunPassAsync(sql,predicate,byId,cap,ct);
            passes.Add(new V9SqlTwoPrefix3Audit.Pass(name,edges));
        }
        var fourteen = V9SqlTwoPrefix3Audit.Analyze(
            observations.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9SqlTwoPrefix3Audit.Observation(
                    x.Id,x.Partition,x.Gestor,x.Cpf is null && x.Birth.HasValue,
                    x.Person3,x.Mother3,x.Birth)).ToArray(),
            twelveEdges,
            truth.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9SqlNamePrefix3BirthAudit.Pair(
                    x.LowerObservationId,x.HigherObservationId,x.Partition,true))
                .ToArray(),passes,twelve,cap);

        // The physical union is already materialized by SQL; this parallel
        // diagnostic reuses that EXACT membership, but independently enumerates
        // all no-CPF M truth, including observations without DOB. TEST stays out.
        var twoField = V9TwoFieldSqlCoverageAudit.Analyze(
            observations.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9TwoFieldSqlCoverageAudit.Observation(
                    x.Id,x.Base,x.Partition,x.Gestor,
                    x.PersonFull,x.MotherFull,x.Birth,x.Cpf)).ToArray(),
            twelveEdges.Concat(passes.SelectMany(x=>x.Edges)),cap);
        foreach (var split in fourteen.Splits)
        {
            var comparison = twoField.Splits.Single(x=>x.Partition==split.Partition);
            if (split.FourteenM!=comparison.LegacySqlM
                || split.FourteenU!=comparison.LegacySqlU)
                throw new InvalidDataException(
                    "Censo de duas evidências diverge da união SQL física de 14 passes.");
        }
        return fourteen with { TwoFieldSqlCoverage = twoField };
    }
}
