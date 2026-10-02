using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Parquet;
using Parquet.Schema;

namespace Jornada.Linkage.Runner;

public sealed record Dt05CandidateStateSnapshot(
    string ManifestLogicalPath, string ManifestSha256, string PartitionSetSha256, long RowCount);

/// <summary>
/// Congela os atributos não reconstituíveis de gold.pessoa usados pelo scorer.
/// A publicação é create-only, content-addressed e validada contra a identidade
/// de candidatos já capturada de forma imutável para o run.
/// </summary>
public sealed class Dt05CandidateStateSnapshotPublisher(
    IOperationalSqlAdapter sql, string bronzeRoot)
{
    private const int ChunkSize = 10_000;

    public async Task<Dt05CandidateStateSnapshot> CaptureAsync(
        Guid runId, Dt05ReplayPreparation preparation, Dt05ReplayManifestIdentity identity, CancellationToken ct)
    {
        var root = Path.GetFullPath(Path.Combine(bronzeRoot, "linkage-snapshots", "v1"));
        Directory.CreateDirectory(Path.Combine(root, "tmp"));

        var rows = await ReadAndValidateAsync(preparation, ct);
        var partitions = new List<object>();
        for (var offset = 0; offset < rows.Count; offset += ChunkSize)
        {
            var chunk = rows.GetRange(offset, Math.Min(ChunkSize, rows.Count - offset));
            partitions.Add(await PublishPartitionAsync(root, chunk, ct));
        }

        var partitionSetSha = Sha256(Dt05ReplayManifestPublisher.Canonicalize(partitions));
        var document = new
        {
            schema_version = 2,
            run_id = runId.ToString("D"),
            snapshot_kind = "candidate-state",
            key_field = "candidate_uuid",
            versions = new
            {
                scorer_version = identity.ScorerVersion,
                ruleset_version = identity.RuleSetVersion,
                model_version = identity.ModelVersion,
                input_snapshot_id = identity.InputSnapshotId,
                normalization_version = identity.NormalizationVersion,
                resolution_catalog_version = identity.ResolutionCatalogVersion,
                projection_schema_version = identity.ProjectionSchemaVersion,
                projection_fingerprint_sha256 = identity.ProjectionFingerprintSha256
            },
            row_count = rows.Count,
            partitions,
            partition_set_sha256 = partitionSetSha
        };
        var manifestBytes = Dt05ReplayManifestPublisher.Canonicalize(document);
        var manifestSha = Sha256(manifestBytes);
        var logical = $"linkage-snapshots/v1/candidate-state/manifests/{runId:D}-{manifestSha}.json";
        await PublishCreateOnlyAsync(bronzeRoot, logical, manifestBytes, ct);
        return new Dt05CandidateStateSnapshot(logical, manifestSha, partitionSetSha, rows.Count);
    }

    private async Task<List<CandidateRow>> ReadAndValidateAsync(Dt05ReplayPreparation preparation, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var rows = new List<CandidateRow>();
            await using var command = new SqlCommand("""
                SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae,estado_identidade
                  FROM gold.pessoa WITH(HOLDLOCK)
                 WHERE estado_identidade=N'REFERENCIA'
                 ORDER BY pessoa_uuid;
                """, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0).ToString("D").ToLowerInvariant();
                rows.Add(new CandidateRow(
                    uuid,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    ReadDate(reader, 2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4)));
            }
            await reader.DisposeAsync();

            var canonical = string.Join('\n', rows.Select(x => x.CandidateUuid + "|" + x.EstadoIdentidade));
            var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(canonical))).ToLowerInvariant();
            if (rows.Count != preparation.CandidateReferenceCount
                || !string.Equals(actualSha, preparation.CandidateSetSha256, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "DT-05: estado candidato mudou após a captura de governança; snapshot Parquet recusado.");

            await transaction.CommitAsync(ct);
            return rows;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<object> PublishPartitionAsync(
        string root, IReadOnlyList<CandidateRow> rows, CancellationToken ct)
    {
        var logicalRows = rows.Select(x => new
        {
            candidate_uuid = x.CandidateUuid,
            nome_completo = x.NomeCompleto,
            data_nascimento = x.DataNascimento,
            nome_mae = x.NomeMae,
            estado_identidade = x.EstadoIdentidade
        }).ToArray();
        var logicalSha = Sha256(JoinCanonicalRows(logicalRows));

        var candidateUuid = new DataField<string>("candidate_uuid", false);
        var nomeCompleto = new DataField<string>("nome_completo", true);
        var dataNascimento = new DataField<string>("data_nascimento", true);
        var nomeMae = new DataField<string>("nome_mae", true);
        var estadoIdentidade = new DataField<string>("estado_identidade", false);
        var schema = new ParquetSchema(candidateUuid, nomeCompleto, dataNascimento, nomeMae, estadoIdentidade);
        var temp = Path.Combine(root, "tmp", ".candidate-" + Guid.NewGuid().ToString("N") + ".parquet");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = await ParquetWriter.CreateAsync(
                schema, stream, new ParquetOptions { CompressionMethod = CompressionMethod.Zstd }, cancellationToken: ct))
            using (var group = writer.CreateRowGroup())
            {
                await group.WriteAsync<string>(candidateUuid, rows.Select(x => x.CandidateUuid).ToArray(), cancellationToken: ct);
                await group.WriteAsync<string>(nomeCompleto, rows.Select(x => x.NomeCompleto).ToArray(), cancellationToken: ct);
                await group.WriteAsync<string>(dataNascimento, rows.Select(x => x.DataNascimento).ToArray(), cancellationToken: ct);
                await group.WriteAsync<string>(nomeMae, rows.Select(x => x.NomeMae).ToArray(), cancellationToken: ct);
                await group.WriteAsync<string>(estadoIdentidade, rows.Select(x => x.EstadoIdentidade).ToArray(), cancellationToken: ct);
                group.CompleteValidate();
            }

            var bytes = await File.ReadAllBytesAsync(temp, ct);
            var physicalSha = Sha256(bytes);
            var relative = $"objects/{physicalSha[..2]}/{physicalSha}.parquet";
            var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { File.Move(temp, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination))
            {
                var existingSha = Sha256(await File.ReadAllBytesAsync(destination, ct));
                if (!string.Equals(existingSha, physicalSha, StringComparison.Ordinal))
                    throw new InvalidDataException("DT-05: colisão/corrupção em objeto Parquet content-addressed.");
            }
            return new { path = relative, sha256 = physicalSha, bytes = bytes.LongLength, rows = rows.Count, logical_sha256 = logicalSha };
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static byte[] JoinCanonicalRows<T>(IReadOnlyList<T> rows)
    {
        using var output = new MemoryStream();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0) output.WriteByte((byte)'\n');
            var bytes = Dt05ReplayManifestPublisher.Canonicalize(rows[i]);
            output.Write(bytes);
        }
        return output.ToArray();
    }

    private static string? ReadDate(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return reader.GetValue(ordinal) switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTime dateTime => DateOnly.FromDateTime(dateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("DT-05: tipo inesperado em data_nascimento do estado candidato.")
        };
    }

    private static async Task PublishCreateOnlyAsync(string bronzeRoot, string logical, byte[] bytes, CancellationToken ct)
    {
        var root = Path.GetFullPath(bronzeRoot);
        var destination = Path.GetFullPath(Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("DT-05: caminho do candidate-state escapou da raiz Bronze.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            throw new InvalidOperationException("DT-05: manifesto candidate-state imutável já existe; publicação recusada.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record CandidateRow(
        string CandidateUuid, string? NomeCompleto, string? DataNascimento, string? NomeMae, string EstadoIdentidade);
}
