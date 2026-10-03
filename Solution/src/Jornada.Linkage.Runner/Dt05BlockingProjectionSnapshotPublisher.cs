using System.Data;
using System.Security.Cryptography;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

public sealed record Dt05BlockingProjectionSnapshot(
    string ManifestLogicalPath, string ManifestSha256, string PartitionSetSha256, long RowCount);

/// <summary>Congela a projeção EAV efetivamente elegível para candidate generation do run.</summary>
public sealed class Dt05BlockingProjectionSnapshotPublisher(IOperationalSqlAdapter sql, string bronzeRoot)
{
    public async Task<Dt05BlockingProjectionSnapshot> CaptureAsync(
        Guid runId, Dt05ReplayManifestIdentity identity, CancellationToken ct)
    {
        PersonResolutionProjectionContract.ValidateSupported(
            identity.ProjectionSchemaVersion, identity.ProjectionFingerprintSha256);
        if (string.IsNullOrWhiteSpace(identity.ProjectionSchemaVersion))
            throw new InvalidOperationException("DT-05: replay exige identidade explícita da projeção de blocking.");

        var rows = await ReadAsync(identity, ct);
        var logicalRows = rows.Select(x => new {
            pessoa_uuid=x.PessoaUuid, normalizacao_versao=x.NormalizacaoVersao,
            atributo=x.Atributo, valor_normalizado=x.ValorNormalizado,
            semantica_temporal=x.SemanticaTemporal, vigente=x.Vigente
        }).ToArray();
        var payload = Dt05ReplayManifestPublisher.Canonicalize(logicalRows);
        var payloadSha = Sha256(payload);
        var relative=$"objects/{payloadSha[..2]}/{payloadSha}.json";
        await PublishCreateOnlyAsync($"linkage-snapshots/v1/{relative}", payload, ct);

        var partitions = new[] { new { path=relative, sha256=payloadSha, bytes=payload.LongLength, rows=rows.Count, logical_sha256=payloadSha } };
        var partitionSetSha=Sha256(Dt05ReplayManifestPublisher.Canonicalize(partitions));
        var manifest=new {
            schema_version=1, run_id=runId.ToString("D"), snapshot_kind="blocking-projection",
            normalization_version=identity.NormalizationVersion,
            projection_schema_version=identity.ProjectionSchemaVersion,
            projection_fingerprint_sha256=identity.ProjectionFingerprintSha256,
            row_count=rows.Count, partitions, partition_set_sha256=partitionSetSha
        };
        var bytes=Dt05ReplayManifestPublisher.Canonicalize(manifest);
        var sha=Sha256(bytes);
        var logical=$"linkage-snapshots/v1/blocking-projection/manifests/{runId:D}-{sha}.json";
        await PublishCreateOnlyAsync(logical, bytes, ct);
        return new(logical,sha,partitionSetSha,rows.Count);
    }

    private async Task<List<Row>> ReadAsync(Dt05ReplayManifestIdentity identity,CancellationToken ct)
    {
        await using var connection=await sql.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try {
            var rows=new List<Row>();
            await using var command=new SqlCommand("""
                SELECT pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal,
                       CASE WHEN vigencia_fim IS NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
                  FROM identidade.blocking_chave WITH(HOLDLOCK)
                 WHERE normalizacao_versao=@normalizacao
                   AND projection_schema_version=@schema
                   AND projection_fingerprint_sha256=@fingerprint
                 ORDER BY pessoa_uuid,atributo,valor_normalizado,vigencia_inicio;
                """,connection,transaction);
            command.Parameters.Add(new SqlParameter("@normalizacao",SqlDbType.NVarChar,80){Value=identity.NormalizationVersion});
            command.Parameters.Add(new SqlParameter("@schema",SqlDbType.NVarChar,120){Value=identity.ProjectionSchemaVersion!});
            command.Parameters.Add(new SqlParameter("@fingerprint",SqlDbType.Char,64){Value=identity.ProjectionFingerprintSha256!});
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
                rows.Add(new(reader.GetGuid(0).ToString("D").ToLowerInvariant(),reader.GetString(1),
                    reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetBoolean(5)));
            await transaction.CommitAsync(ct);
            return rows;
        } catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task PublishCreateOnlyAsync(string logical,byte[] bytes,CancellationToken ct)
    {
        var root=Path.GetFullPath(bronzeRoot);
        var destination=Path.GetFullPath(Path.Combine(root,logical.Replace('/',Path.DirectorySeparatorChar)));
        if(!destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new InvalidOperationException("DT-05: caminho de blocking escapou da raiz Bronze.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await File.WriteAllBytesAsync(temp,bytes,ct);
            try { File.Move(temp,destination,overwrite:false); }
            catch(IOException) when(File.Exists(destination)) {
                if(!string.Equals(Sha256(await File.ReadAllBytesAsync(destination,ct)),Sha256(bytes),StringComparison.Ordinal))
                    throw new InvalidDataException("DT-05: objeto blocking content-addressed divergente.");
            }
        } finally { try { if(File.Exists(temp)) File.Delete(temp); } catch {} }
    }
    private static string Sha256(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed record Row(string PessoaUuid,string NormalizacaoVersao,string Atributo,string ValorNormalizado,string SemanticaTemporal,bool Vigente);
}
