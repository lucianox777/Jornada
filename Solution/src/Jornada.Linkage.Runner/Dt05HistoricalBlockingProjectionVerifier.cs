using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

public sealed record Dt05HistoricalBlockingProjectionRow(
    Guid PessoaUuid,string NormalizacaoVersao,string Atributo,string ValorNormalizado,string SemanticaTemporal,bool Vigente);
public sealed record Dt05VerifiedBlockingProjection(
    string ManifestSha256,string PartitionSetSha256,long RowCount,IReadOnlyList<Dt05HistoricalBlockingProjectionRow> Rows);

public sealed class Dt05HistoricalBlockingProjectionVerifier(string bronzeRoot)
{
    public async Task<Dt05VerifiedBlockingProjection> VerifyAsync(Dt05HistoricalBlockingProjectionBinding binding,CancellationToken ct)
    {
        var manifestPath=Resolve(binding.ManifestLogicalPath);
        var bytes=await File.ReadAllBytesAsync(manifestPath,ct);
        if(Sha(bytes)!=binding.ManifestSha256) throw new InvalidDataException("DT-05: SHA do manifesto blocking-projection diverge do binding SQL.");
        using var doc=JsonDocument.Parse(bytes); var root=doc.RootElement;
        if(root.GetProperty("run_id").GetString()!=binding.SourceRunId.ToString("D")
           || root.GetProperty("snapshot_kind").GetString()!="blocking-projection"
           || root.GetProperty("normalization_version").GetString()!=binding.NormalizationVersion
           || root.GetProperty("projection_schema_version").GetString()!=binding.ProjectionSchemaVersion
           || root.GetProperty("projection_fingerprint_sha256").GetString()!=binding.ProjectionFingerprintSha256
           || root.GetProperty("partition_set_sha256").GetString()!=binding.PartitionSetSha256)
            throw new InvalidDataException("DT-05: identidade do manifesto blocking-projection diverge do binding SQL.");
        var rows=new List<Dt05HistoricalBlockingProjectionRow>(); long declared=0;
        foreach(var p in root.GetProperty("partitions").EnumerateArray())
        {
            var path=p.GetProperty("path").GetString()??throw new InvalidDataException();
            var payload=await File.ReadAllBytesAsync(Resolve("linkage-snapshots/v1/"+path),ct);
            var sha=Sha(payload);
            if(sha!=p.GetProperty("sha256").GetString() || payload.LongLength!=p.GetProperty("bytes").GetInt64())
                throw new InvalidDataException("DT-05: partição blocking-projection diverge do manifesto.");
            using var part=JsonDocument.Parse(payload);
            foreach(var x in part.RootElement.EnumerateArray())
                rows.Add(new(Guid.Parse(x.GetProperty("pessoa_uuid").GetString()!),
                    x.GetProperty("normalizacao_versao").GetString()!,x.GetProperty("atributo").GetString()!,
                    x.GetProperty("valor_normalizado").GetString()!,x.GetProperty("semantica_temporal").GetString()!,
                    x.GetProperty("vigente").GetBoolean()));
            declared+=p.GetProperty("rows").GetInt64();
        }
        if(rows.Count!=declared || declared!=root.GetProperty("row_count").GetInt64())
            throw new InvalidDataException("DT-05: contagem blocking-projection diverge do manifesto.");
        return new(binding.ManifestSha256,binding.PartitionSetSha256,declared,rows);
    }
    private string Resolve(string logical)
    {
        var root=Path.GetFullPath(bronzeRoot); var p=Path.GetFullPath(Path.Combine(root,logical.Replace('/',Path.DirectorySeparatorChar)));
        if(!p.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)) throw new InvalidDataException("DT-05: caminho blocking-projection escapou da raiz Bronze.");
        return p;
    }
    private static string Sha(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
