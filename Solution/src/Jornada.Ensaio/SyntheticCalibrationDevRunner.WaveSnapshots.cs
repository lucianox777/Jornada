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
    /// Congela o estado operacional de cada onda antes da onda seguinte. Não abre
    /// sidecar de truth nem exporta CPF, nome, CNS ou atributos pessoais.
    /// A execução do Runner por onda ainda não existe; não simular resultados.
    /// </summary>
    private async Task<string> WriteWaveOperationalSnapshotAsync(
        string directory, int wave, long expectedSources, long expectedObservations,
        IReadOnlyDictionary<string, bool> expectedCpf, CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.codigo_pessoa_origem, g.codigo,
                   o.pessoa_observacao_id, o.versao_interna,
                   CASE WHEN o.cpf IS NULL THEN 0 ELSE 1 END,
                   vc.status, CONVERT(nvarchar(36),vc.pessoa_uuid), vc.metodo_resolucao
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
            WHERE p.codigo_pessoa_origem LIKE N'SYNTH-%'
            ORDER BY p.codigo_pessoa_origem;
            """;
        var sources = new List<SyntheticWaveObservedSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var source = reader.GetString(0);
                var hasCpf = reader.GetInt32(4) == 1;
                if (!seen.Add(source)
                    || !expectedCpf.TryGetValue(source, out var expected) || expected != hasCpf)
                    throw new InvalidDataException(
                        "Snapshot SQL divergente das fontes/CPFs observados nos pacotes.");
                sources.Add(new SyntheticWaveObservedSource(
                    source, reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3),
                    hasCpf, reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        if (sources.Count != expectedSources || sources.Count != expectedCpf.Count)
            throw new InvalidDataException("Snapshot SQL não contém exatamente todas as origens da onda.");

        var payload = new
        {
            SchemaVersion = "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V1",
            Wave = wave,
            SourceCount = expectedSources,
            ObservationCount = expectedObservations,
            DecisionProvenance = "CURRENT_IDENTITY_NO_RUN",
            LinkageRunId = (string?)null,
            LinkageRunStatus = "NAO_EXECUTADO",
            Sources = sources
        };
        var path = Path.Combine(directory, "operational-snapshot.json");
        // O diretório de execução é novo: jamais substituir o checkpoint congelado.
        await using (var stream = new FileStream(
                         path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, payload,
                WaveSnapshotJsonOptions,
                cancellationToken);
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
        string? CurrentMethod);
}
