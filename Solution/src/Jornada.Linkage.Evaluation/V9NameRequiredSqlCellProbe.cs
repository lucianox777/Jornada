using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Index-backed SQL experiment exclusively in ephemeral JornadaHarness #tables.
/// No DDL of identidade.blocking_chave, published passes or operational model.
/// </summary>
public static class V9NameRequiredSqlCellProbe
{
    public static async Task<V9NameRequiredSqlCellAudit.Report> RunAsync(
        SqlConnection connection,IReadOnlyList<V9NameRequiredSqlCellAudit.Observation> observations,
        IEnumerable<V9SqlNamePrefix3BirthAudit.Pair> existingFourteen,int cap,CancellationToken ct)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development"
           || connection.Database!="JornadaHarness" || cap is <1 or >1_000_000)
            throw new InvalidOperationException("Células de blocking só podem executar na JornadaHarness DEV.");
        var seen=new HashSet<int>();
        foreach(var x in observations)
            if(x.Partition is not ("TRAIN" or "VALIDATION") || !seen.Add(x.Id))
                throw new InvalidDataException("Duplicidade ou TEST em células SQL.");
        await using(var create=new SqlCommand("""
            CREATE TABLE #v9cellobs(
                obs_id INT NOT NULL PRIMARY KEY,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                gestor NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
                pfull NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                mfull NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                pfon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                plast NVARCHAR(120) COLLATE Latin1_General_100_BIN2 NULL,
                mfon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                mlast NVARCHAR(120) COLLATE Latin1_General_100_BIN2 NULL,
                birthyear INT NULL);
            CREATE TABLE #v9cellmother(
                obs_id INT NOT NULL,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                surname NVARCHAR(120) COLLATE Latin1_General_100_BIN2 NOT NULL,
                PRIMARY KEY CLUSTERED(obs_id,surname));
            """,connection){CommandTimeout=600})
            await create.ExecuteNonQueryAsync(ct);

        var obsTable=new DataTable();
        foreach(var (name,kind) in new (string,Type)[]{
            ("obs_id",typeof(int)),("part",typeof(string)),("gestor",typeof(string)),
            ("pfull",typeof(string)),("mfull",typeof(string)),
            ("pfon",typeof(string)),("plast",typeof(string)),("mfon",typeof(string)),
            ("mlast",typeof(string)),("birthyear",typeof(int))})
            obsTable.Columns.Add(name,kind);
        var motherTable=new DataTable();
        foreach(var (name,kind) in new (string,Type)[]{
            ("obs_id",typeof(int)),("part",typeof(string)),("surname",typeof(string))})
            motherTable.Columns.Add(name,kind);
        var reference=new Dictionary<int,V9NameRequiredSqlCellAudit.Observation>();
        foreach(var x in observations.Where(V9NameRequiredSqlCellAudit.Eligible))
        {
            reference.Add(x.Id,x);
            var pfon=MetaphoneBr.Encode(x.Person);
            var plast=V9NameRequiredSqlCellAudit.Last(x.Person);
            var mfon=MetaphoneBr.Encode(x.Mother);
            var mlast=V9NameRequiredSqlCellAudit.Last(x.Mother);
            var year=x.Birth.HasValue &&
                V9MinimumEvidenceEligibility.HasBirth(x.Birth.Value.ToDateTime(TimeOnly.MinValue))
                ? x.Birth.Value.Year : (int?)null;
            if(pfon is null || pfon.Length>150 || (mfon?.Length??0)>150
                || (plast?.Length??0)>120 || (mlast?.Length??0)>120)
                throw new InvalidDataException("Comprimento de chave SQL inválido; não truncar.");
            obsTable.Rows.Add(x.Id,x.Partition,x.Gestor,
                (object?)IdentityComparison.NormalizeText(x.Person)??DBNull.Value,
                (object?)IdentityComparison.NormalizeText(x.Mother)??DBNull.Value,
                (object?)pfon??DBNull.Value,
                (object?)plast??DBNull.Value,(object?)mfon??DBNull.Value,
                (object?)mlast??DBNull.Value,(object?)year??DBNull.Value);
            foreach(var surname in V9NameRequiredSqlCellAudit.AnySurname(x.Mother))
            {
                if(surname.Length>120)
                    throw new InvalidDataException("Sobrenome não cabe na chave indexada.");
                motherTable.Rows.Add(x.Id,x.Partition,surname);
            }
        }
        using(var copy=new SqlBulkCopy(connection){
            DestinationTableName="#v9cellobs",BatchSize=15000,BulkCopyTimeout=600})
            await copy.WriteToServerAsync(obsTable,ct);
        using(var copy=new SqlBulkCopy(connection){
            DestinationTableName="#v9cellmother",BatchSize=15000,BulkCopyTimeout=600})
            await copy.WriteToServerAsync(motherTable,ct);
        obsTable.Dispose();motherTable.Dispose();

        // The original #v9obs was populated using BlockingProjectionKeyProjector:
        // strict BIN2 parity between the earlier MetaphoneBR projection and
        // the NEW physical index's exact values, before measuring any recall.
        await using(var parity=new SqlCommand("""
            SELECT COUNT_BIG(*) FROM #v9cellobs c
            LEFT JOIN #v9obs o ON c.obs_id=o.obs_id
            WHERE o.obs_id IS NULL
               OR ISNULL(c.pfull,N'~')<>ISNULL(o.nome,N'~')
               OR ISNULL(c.mfull,N'~')<>ISNULL(o.mae,N'~')
               OR ISNULL(c.pfon,N'~')<>ISNULL(o.nome_fon,N'~')
               OR ISNULL(c.mfon,N'~')<>ISNULL(o.mae_fon,N'~');
            """,connection))
        {
            var mismatches=Convert.ToInt64(await parity.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            if(mismatches!=0)throw new InvalidDataException(
                "Índice normalizado/MetaphoneBR difere das chaves canônicas C# existentes.");
        }
        await using(var ix=new SqlCommand("""
            CREATE INDEX IX_v9cell_pfon_mlast ON #v9cellobs(part,pfon,mlast);
            CREATE INDEX IX_v9cell_pfon_year ON #v9cellobs(part,pfon,birthyear);
            CREATE INDEX IX_v9cell_plast_mfon ON #v9cellobs(part,plast,mfon);
            CREATE INDEX IX_v9cell_pfon ON #v9cellobs(part,pfon);
            CREATE INDEX IX_v9cell_mother_presence ON #v9cellmother(part,surname,obs_id);
            """,connection){CommandTimeout=600})
            await ix.ExecuteNonQueryAsync(ct);

        var predicates=new[]{
            (V9NameRequiredSqlCellAudit.PersonPhoneticMotherLast,
                "a.pfon=b.pfon AND a.mlast=b.mlast AND a.mlast IS NOT NULL"),
            (V9NameRequiredSqlCellAudit.PersonPhoneticYear,
                "a.pfon=b.pfon AND a.birthyear=b.birthyear AND a.birthyear IS NOT NULL"),
            (V9NameRequiredSqlCellAudit.PersonLastMotherPhonetic,
                "a.plast=b.plast AND a.mfon=b.mfon AND a.plast IS NOT NULL AND a.mfon IS NOT NULL"),
            (V9NameRequiredSqlCellAudit.PersonPhoneticMotherAny,
                "a.pfon=b.pfon AND EXISTS (SELECT 1 FROM #v9cellmother p JOIN #v9cellmother q ON p.part=q.part AND p.surname=q.surname WHERE p.obs_id=a.obs_id AND q.obs_id=b.obs_id)")
        };
        var passes=new List<V9NameRequiredSqlCellAudit.Pass>(predicates.Length);
        foreach(var (name,predicate) in predicates)
        {
            // Whitelisted SQL predicates only; no value interpolation or user SQL.
            var sql=$"""
                SELECT DISTINCT a.obs_id,b.obs_id,a.part
                FROM #v9cellobs a JOIN #v9cellobs b
                  ON a.part=b.part AND a.obs_id<b.obs_id
                 AND a.pfon IS NOT NULL
                 AND {predicate}
                WHERE a.gestor<>b.gestor
                ORDER BY a.obs_id,b.obs_id;
                """;
            var pairs=new List<V9SqlNamePrefix3BirthAudit.Pair>();
            await using(var cmd=new SqlCommand(sql,connection){CommandTimeout=600})
            await using(var reader=await cmd.ExecuteReaderAsync(ct))
            {
                while(await reader.ReadAsync(ct))
                {
                    if(pairs.Count>=cap)
                        throw new InvalidDataException("Passe SQL excede 1 milhão; nunca truncar.");
                    var a=reader.GetInt32(0);
                    var b=reader.GetInt32(1);
                    var partition=reader.GetString(2);
                    if(!reference.TryGetValue(a,out var ra) ||
                       !reference.TryGetValue(b,out var rb) ||
                       ra.Partition!=partition || rb.Partition!=partition ||
                       ra.Gestor==rb.Gestor || a>=b)
                        throw new InvalidDataException("SQL retornou par inelegível/TEST.");
                    pairs.Add(new(a,b,partition,ra.Base==rb.Base));
                }
            }
            passes.Add(new V9NameRequiredSqlCellAudit.Pass(name,pairs));
        }
        return V9NameRequiredSqlCellAudit.Analyze(observations,
            existingFourteen,passes,cap);
    }
}
