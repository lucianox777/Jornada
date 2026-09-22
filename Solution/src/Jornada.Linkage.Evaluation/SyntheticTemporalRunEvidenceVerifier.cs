using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>Verifica proveniencia SQL antes de abrir qualquer sidecar de truth.</summary>
public static class SyntheticTemporalRunEvidenceVerifier
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task VerifyAsync(
        SqlConnection connection, string generatedRoot, int timeout, CancellationToken ct)
    {
        var ingestion = Path.Combine(Path.GetFullPath(generatedRoot), "ingestion");
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(ingestion, "waves-manifest.json"), ct));
        var waves = manifest.RootElement.GetProperty("waves");
        if (waves.ValueKind != JsonValueKind.Array || waves.GetArrayLength() is < 2 or > 12)
            throw new InvalidDataException("Manifesto de ondas invalido.");

        for (var wave = 1; wave <= waves.GetArrayLength(); wave++)
        {
            var path = Path.Combine(ingestion,
                "wave-" + wave.ToString("D2", CultureInfo.InvariantCulture),
                "operational-snapshot.json");
            var snapshot = JsonSerializer.Deserialize<SyntheticTemporalTruthEvaluator.OperationalSnapshot>(
                await File.ReadAllTextAsync(path, ct), Json)
                ?? throw new InvalidDataException("Snapshot de onda ausente.");
            if (snapshot.SchemaVersion != "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V2"
                || snapshot.Wave != wave
                || snapshot.DecisionProvenance != "DETERMINISTIC_PLUS_MODEL_VALIDATION_SHADOW"
                || snapshot.LinkageRunStatus != "CONCLUIDO_SEM_PUBLICACAO"
                || !Guid.TryParse(snapshot.LinkageRunId, out var runId) || runId == Guid.Empty
                || !Guid.TryParse(snapshot.LinkageModelId, out var modelId) || modelId == Guid.Empty
                || snapshot.LinkageModelVersion is not > 0)
                throw new InvalidDataException("Snapshot sem run/modelo comprovavel.");

            long eligible;
            await using (var cmd = new SqlCommand(
                """
                SELECT r.modelo_id,r.modelo_versao,r.tipo_run,r.status,
                       r.registros_elegiveis,r.avaliados,r.publicado_em,m.status
                FROM identidade.linkage_run r
                JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
                WHERE r.linkage_run_id=@run_id;
                """, connection) { CommandTimeout = timeout })
            {
                cmd.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)
                    || reader.GetGuid(0) != modelId
                    || reader.GetInt32(1) != snapshot.LinkageModelVersion
                    || reader.GetString(2) != "MODEL_VALIDATION"
                    || reader.GetString(3) != "CONCLUIDO_SEM_PUBLICACAO"
                    || reader.GetInt64(4) != reader.GetInt64(5)
                    || reader.GetInt64(4) > snapshot.ObservationCount
                    || !reader.IsDBNull(6) || reader.GetString(7) != "RASCUNHO")
                    throw new InvalidDataException("Run nao concluido ou modelo publicado/nao RASCUNHO.");
                eligible = reader.GetInt64(4);
                if (await reader.ReadAsync(ct))
                    throw new InvalidDataException("Run SQL duplicado.");
            }

            var sourceCodes = snapshot.Sources.Select(s => s.SourceCode).ToHashSet(StringComparer.Ordinal);
            if (sourceCodes.Count != snapshot.Sources.Length)
                throw new InvalidDataException("Snapshot duplica fonte operacional.");
            var highWatermark = snapshot.Sources.Max(s => s.LatestObservationId);
            var results = new Dictionary<long, (string Status, string? Uuid)>();
            await using (var cmd = new SqlCommand(
                """
                SELECT i.pessoa_observacao_id,r.status,
                       CONVERT(nvarchar(36),r.pessoa_uuid_resolvido),
                       p.codigo_pessoa_origem,po.cpf,r.modelo_id,r.modelo_versao
                FROM identidade.linkage_run_item i
                JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id
                JOIN silver.pessoa_origem p ON p.pessoa_origem_id=po.pessoa_origem_id
                LEFT JOIN identidade.linkage_resultado r
                  ON r.linkage_run_id=i.linkage_run_id
                 AND r.pessoa_observacao_id=i.pessoa_observacao_id
                WHERE i.linkage_run_id=@run_id;
                """, connection) { CommandTimeout = timeout })
            {
                cmd.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(1) || reader.IsDBNull(5) || reader.IsDBNull(6)
                        || reader.GetGuid(5) != modelId
                        || reader.GetInt32(6) != snapshot.LinkageModelVersion
                        || !reader.GetString(3).StartsWith("SYNTH-", StringComparison.Ordinal)
                        || !sourceCodes.Contains(reader.GetString(3))
                        || !reader.IsDBNull(4) || reader.GetInt64(0) > highWatermark
                        || !results.TryAdd(reader.GetInt64(0),
                            (reader.GetString(1),
                             reader.IsDBNull(2) ? null : reader.GetString(2))))
                        throw new InvalidDataException("Item sem resultado ou duplicado.");
                }
            }

            await using (var cmd = new SqlCommand(
                """
                SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                WHERE linkage_run_id=@run_id;
                """, connection) { CommandTimeout = timeout })
            {
                cmd.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
                if (results.Count != eligible
                    || Convert.ToInt64(await cmd.ExecuteScalarAsync(ct),
                        CultureInfo.InvariantCulture) != eligible)
                    throw new InvalidDataException("Resultado real nao fecha o universo do run.");
            }

            var observedIds = new HashSet<long>();
            foreach (var source in snapshot.Sources)
            {
                if (!observedIds.Add(source.LatestObservationId))
                    throw new InvalidDataException("Snapshot duplica ultima observacao.");
                if (source.HasCpf)
                {
                    if (results.ContainsKey(source.LatestObservationId)
                        || source.ProposedStatus is not null || source.ProposedUuid is not null)
                        throw new InvalidDataException("Run probabilistico incluiu CPF.");
                }
                else if (!results.TryGetValue(source.LatestObservationId, out var raw)
                    || raw.Status != source.ProposedStatus
                    || !string.Equals(raw.Uuid, source.ProposedUuid,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Proposta congelada diverge do run real.");
            }
        }
    }
}
