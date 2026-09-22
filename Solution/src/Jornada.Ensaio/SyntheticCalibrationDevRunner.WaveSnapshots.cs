using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Ensaio;

public sealed partial class SyntheticCalibrationDevRunner
{
    private static readonly JsonSerializerOptions WaveSnapshotJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Congela o estado operacional e as propostas brutas do Runner de cada onda.
    /// CPF, nomes, CNS e truth nunca entram no snapshot; nenhuma proposta e publicada.
    /// </summary>
    private async Task<string> WriteWaveOperationalSnapshotAsync(
        string directory, int wave, long expectedSources, long expectedObservations,
        IReadOnlyDictionary<string, bool> expectedCpf,
        SyntheticDraftModelEvidence model, SyntheticModelValidationEvidence run,
        CancellationToken cancellationToken)
    {
        if (run.ModelId != model.ModelId || run.ModelVersion != model.Version
            || run.Status != "CONCLUIDO_SEM_PUBLICACAO" || run.Published)
            throw new InvalidOperationException("Runner de onda sem proveniencia nao publicadora.");

        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.codigo_pessoa_origem,g.codigo,
                   o.pessoa_observacao_id,o.versao_interna,
                   CASE WHEN o.cpf IS NULL THEN 0 ELSE 1 END,
                   vc.status,CONVERT(nvarchar(36),vc.pessoa_uuid),vc.metodo_resolucao,
                   li.pessoa_observacao_id,
                   r.status,CONVERT(nvarchar(36),r.pessoa_uuid_resolvido)
            FROM silver.pessoa_origem p
            JOIN ref.sistema_origem s ON s.sistema_origem_id=p.sistema_origem_id
            JOIN ref.gestor g ON g.gestor_id=s.gestor_id
            CROSS APPLY (
                SELECT TOP(1) po.pessoa_observacao_id,po.versao_interna,po.cpf
                FROM silver.pessoa_observacao po
                WHERE po.pessoa_origem_id=p.pessoa_origem_id
                ORDER BY po.versao_interna DESC,po.pessoa_observacao_id DESC
            ) o
            LEFT JOIN identidade.v_vinculo_corrente vc
              ON vc.pessoa_observacao_id=o.pessoa_observacao_id
            LEFT JOIN identidade.linkage_run_item li
              ON li.linkage_run_id=@run_id AND li.pessoa_observacao_id=o.pessoa_observacao_id
            LEFT JOIN identidade.linkage_resultado r
              ON r.linkage_run_id=@run_id AND r.pessoa_observacao_id=o.pessoa_observacao_id
            WHERE p.codigo_pessoa_origem LIKE N'SYNTH-%'
            ORDER BY p.codigo_pessoa_origem;
            """;
        AddParameter(command, "@run_id", run.RunId);

        var sources = new List<SyntheticWaveObservedSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var sourceCode = reader.GetString(0);
                var observationId = reader.GetInt64(2);
                var hasCpf = reader.GetInt32(4) == 1;
                if (!seen.Add(sourceCode)
                    || !expectedCpf.TryGetValue(sourceCode, out var expected) || expected != hasCpf)
                    throw new InvalidDataException(
                        "Snapshot SQL divergente das fontes/CPFs observados nos pacotes.");

                // MODEL_VALIDATION so elege observacoes sem CPF. Toda ultima
                // versao sem CPF tem de estar materializada no run e ter resultado.
                if (hasCpf && (!reader.IsDBNull(8) || !reader.IsDBNull(9)))
                    throw new InvalidDataException("Runner incluiu indevidamente fonte com CPF.");
                if (!hasCpf && (reader.IsDBNull(8) || reader.GetInt64(8) != observationId
                    || reader.IsDBNull(9)))
                    throw new InvalidDataException(
                        "Ultima observacao sem CPF nao tem resultado no Runner desta onda.");

                var proposedStatus = reader.IsDBNull(9) ? null : reader.GetString(9);
                var proposedUuid = reader.IsDBNull(10) ? null : reader.GetString(10);
                if (proposedStatus == "RESOLVIDO" && proposedUuid is null
                    || proposedStatus != "RESOLVIDO" && proposedUuid is not null)
                    throw new InvalidDataException("Resultado bruto do Runner inconsistente.");

                sources.Add(new SyntheticWaveObservedSource(
                    sourceCode, reader.GetString(1), observationId, reader.GetInt32(3),
                    hasCpf, reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    proposedStatus, proposedUuid));
            }
        }

        if (sources.Count != expectedSources || sources.Count != expectedCpf.Count)
            throw new InvalidDataException("Snapshot SQL nao contem exatamente todas as origens da onda.");

        var payload = new
        {
            SchemaVersion = "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V2",
            Wave = wave,
            SourceCount = expectedSources,
            ObservationCount = expectedObservations,
            DecisionProvenance = "DETERMINISTIC_PLUS_MODEL_VALIDATION_SHADOW",
            LinkageRunId = run.RunId.ToString("D"),
            LinkageRunStatus = run.Status,
            LinkageModelId = model.ModelId.ToString("D"),
            LinkageModelVersion = model.Version,
            Sources = sources
        };
        var path = Path.Combine(directory, "operational-snapshot.json");
        // O diretorio de execucao e novo: jamais substituir checkpoint congelado.
        await using (var stream = new FileStream(
                         path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, payload,
                WaveSnapshotJsonOptions, cancellationToken);
        }
        await using var hashStream = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
        await File.WriteAllTextAsync(path + ".sha256",
            sha + "  operational-snapshot.json\n", new UTF8Encoding(false), cancellationToken);
        return sha;
    }

    private sealed record SyntheticWaveObservedSource(
        string SourceCode, string GestorCodigo, long LatestObservationId,
        int Version, bool HasCpf, string? CurrentStatus, string? CurrentUuid,
        string? CurrentMethod, string? ProposedStatus, string? ProposedUuid);
}
