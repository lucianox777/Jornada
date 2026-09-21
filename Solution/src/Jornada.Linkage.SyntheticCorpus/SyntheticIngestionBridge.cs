using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Ingestion;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticIngestionRoute(
    string SyntheticGestor,
    string GestorCodigo,
    string CodigoSistemaOrigem);

public sealed record SyntheticIngestionBridgeOptions(
    int PessoaSchemaVersao,
    DateTimeOffset DataReferencia,
    string PseudonymizationKey,
    IReadOnlyList<SyntheticIngestionRoute>? Routes = null)
{
    public IReadOnlyList<SyntheticIngestionRoute> EffectiveRoutes =>
        Routes ?? SyntheticIngestionBridge.DefaultRoutes;

    public void Validate()
    {
        if (PessoaSchemaVersao < 4)
            throw new ArgumentOutOfRangeException(
                nameof(PessoaSchemaVersao),
                "A ponte sintética suporta contratos Pessoa v4+.");
        if (DataReferencia == default)
            throw new ArgumentException("dataReferencia deve ser explícita.", nameof(DataReferencia));
        if (string.IsNullOrWhiteSpace(PseudonymizationKey)
            || Encoding.UTF8.GetByteCount(PseudonymizationKey) < 16)
        {
            throw new ArgumentException(
                "A chave de pseudonimização deve possuir ao menos 16 bytes UTF-8.",
                nameof(PseudonymizationKey));
        }

        if (EffectiveRoutes.Count == 0)
            throw new ArgumentException("Ao menos uma rota sintética é obrigatória.", nameof(Routes));

        var duplicateSynthetic = EffectiveRoutes
            .GroupBy(x => x.SyntheticGestor, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateSynthetic is not null)
            throw new ArgumentException($"Gestor sintético duplicado: {duplicateSynthetic.Key}.", nameof(Routes));

        foreach (var route in EffectiveRoutes)
        {
            if (string.IsNullOrWhiteSpace(route.SyntheticGestor)
                || string.IsNullOrWhiteSpace(route.GestorCodigo)
                || string.IsNullOrWhiteSpace(route.CodigoSistemaOrigem))
            {
                throw new ArgumentException("Rota sintética contém código vazio.", nameof(Routes));
            }
        }
    }
}

public sealed record SyntheticIngestionTruthRow(
    string Status,
    string ObservationId,
    string BasePersonId,
    string Partition,
    string SyntheticGestor,
    string GestorCodigo,
    string CodigoSistemaOrigem,
    string? OpaquePersonId,
    string? PackageFileName,
    string? ExclusionReason);

public sealed record SyntheticIngestionPackage(
    string SyntheticGestor,
    string GestorCodigo,
    string CodigoSistemaOrigem,
    int PessoaSchemaVersao,
    string FileName,
    string Sha256,
    int PeopleCount,
    byte[] Bytes);

public sealed record SyntheticIngestionBridgeResult(
    IReadOnlyList<SyntheticIngestionPackage> Packages,
    IReadOnlyList<SyntheticIngestionTruthRow> TruthRows,
    IReadOnlyDictionary<string, SyntheticEmpiricalM> MaterializedEmpiricalM,
    int SourceObservationCount,
    int MaterializedObservationCount,
    int ExcludedObservationCount,
    string PseudonymizationKeySha256,
    string BridgeVersion);

public static class SyntheticIngestionBridge
{
    public const string BridgeVersion = "SYNTHETIC_INGESTION_BRIDGE_V1";
    public const string MissingBirthDateReason = "EXCLUIDA_CONTRATO_ATIVO_DATA_NASCIMENTO_AUSENTE";

    public static readonly IReadOnlyList<SyntheticIngestionRoute> DefaultRoutes =
    [
        new("G0", "SEHAB", "SEHAB"),
        new("G1", "SMADS", "ASSISTENCIA"),
        new("G2", "SMDET", "TRABALHO"),
        new("G3", "SMS", "SAUDE")
    ];

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static SyntheticIngestionBridgeResult Build(
        SyntheticCorpusGeneration generation,
        SyntheticIngestionBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var routeBySynthetic = options.EffectiveRoutes
            .ToDictionary(x => x.SyntheticGestor, StringComparer.Ordinal);
        var keyBytes = Encoding.UTF8.GetBytes(options.PseudonymizationKey);
        var keySha = Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();

        var truth = new List<SyntheticIngestionTruthRow>(generation.Observations.Count);
        var accepted = new List<(SyntheticObservation Observation, SyntheticIngestionRoute Route, string OpaqueId)>();
        var opaqueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var observation in generation.Observations)
        {
            if (!routeBySynthetic.TryGetValue(observation.Gestor, out var route))
                throw new InvalidDataException($"Sem rota de ingestão para gestor sintético {observation.Gestor}.");

            if (observation.BirthDate is null)
            {
                truth.Add(new SyntheticIngestionTruthRow(
                    "EXCLUIDA_CONTRATO_ATIVO",
                    observation.ObservationId,
                    observation.BasePersonId,
                    observation.Partition,
                    observation.Gestor,
                    route.GestorCodigo,
                    route.CodigoSistemaOrigem,
                    null,
                    null,
                    MissingBirthDateReason));
                continue;
            }

            if (string.IsNullOrWhiteSpace(observation.Name))
                throw new InvalidDataException(
                    $"Observação {observation.ObservationId} sem nome não é representável no contrato Pessoa ativo.");

            var opaque = ComputeOpaquePersonId(
                keyBytes,
                generation.Options.Seed,
                observation.ObservationId);
            if (!opaqueIds.Add(opaque))
                throw new InvalidDataException($"Colisão de pseudônimo operacional: {opaque}.");

            accepted.Add((observation, route, opaque));
        }

        var packages = new List<SyntheticIngestionPackage>();
        foreach (var group in accepted
                     .GroupBy(x => x.Route.SyntheticGestor, StringComparer.Ordinal)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var route = group.First().Route;
            var payloadRows = group
                .OrderBy(x => x.OpaqueId, StringComparer.Ordinal)
                .ToArray();

            var peopleBytes = BuildPeopleJsonl(payloadRows);
            var manifest = new IngestionPackageManifest(
                IngestionPackageInspector.CurrentFormatVersion,
                options.PessoaSchemaVersao,
                route.CodigoSistemaOrigem,
                null,
                null,
                null,
                options.DataReferencia);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var zipBytes = DeterministicIngestionZipWriter.Create(
                manifestBytes,
                peopleBytes,
                ReadOnlyMemory<byte>.Empty);
            var sha = IngestionPackageInspector.ComputeSha256(zipBytes);
            var context = new AccessContext(
                Guid.Empty,
                AccessCredentialType.GESTOR,
                route.GestorCodigo,
                route.GestorCodigo,
                null,
                [],
                []);
            var fileName = IngestionPackageInspector.GetCanonicalFileName(manifest, context, sha);

            packages.Add(new SyntheticIngestionPackage(
                route.SyntheticGestor,
                route.GestorCodigo,
                route.CodigoSistemaOrigem,
                options.PessoaSchemaVersao,
                fileName,
                sha,
                payloadRows.Length,
                zipBytes));

            foreach (var item in payloadRows)
            {
                truth.Add(new SyntheticIngestionTruthRow(
                    "MATERIALIZADA",
                    item.Observation.ObservationId,
                    item.Observation.BasePersonId,
                    item.Observation.Partition,
                    item.Observation.Gestor,
                    route.GestorCodigo,
                    route.CodigoSistemaOrigem,
                    item.OpaqueId,
                    fileName,
                    null));
            }
        }

        var materializedIds = truth
            .Where(x => x.Status == "MATERIALIZADA")
            .Select(x => x.ObservationId)
            .ToHashSet(StringComparer.Ordinal);
        var materializedObservations = generation.Observations
            .Where(x => materializedIds.Contains(x.ObservationId))
            .ToArray();

        return new SyntheticIngestionBridgeResult(
            packages,
            truth
                .OrderBy(x => x.ObservationId, StringComparer.Ordinal)
                .ToArray(),
            SyntheticCorpusGenerator.ComputeEmpiricalM(materializedObservations),
            generation.Observations.Count,
            materializedObservations.Length,
            generation.Observations.Count - materializedObservations.Length,
            keySha,
            BridgeVersion);
    }

    private static byte[] BuildPeopleJsonl(
        IReadOnlyList<(SyntheticObservation Observation, SyntheticIngestionRoute Route, string OpaqueId)> rows)
    {
        using var stream = new MemoryStream();
        foreach (var row in rows)
        {
            var identifiers = row.Observation.Cns is null
                ? null
                : new[]
                {
                    new SyntheticIngestionIdentifier(
                        "CNS",
                        "BR",
                        row.Observation.Cns,
                        "DECLARADO")
                };

            var person = new SyntheticIngestionPerson(
                row.OpaqueId,
                row.OpaqueId,
                row.Observation.Cpf,
                row.Observation.Cpf is null ? "NAO_INFORMADO_ORIGEM" : null,
                identifiers,
                row.Observation.Name!,
                row.Observation.BirthDate!.Value.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                row.Observation.MotherName);

            JsonSerializer.Serialize(stream, person, JsonOptions);
            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }

    private static string ComputeOpaquePersonId(
        byte[] key,
        ulong seed,
        string observationId)
    {
        var canonical = $"{BridgeVersion}|seed={seed}|observation={observationId}";
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        return "SYNTH-" + Convert.ToHexString(digest.AsSpan(0, 16));
    }

    private static JsonSerializerOptions CreateJsonOptions()
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private sealed record SyntheticIngestionIdentifier(
        string Tipo,
        string Namespace,
        string Valor,
        string StatusEvidencia);

    private sealed record SyntheticIngestionPerson(
        string IdPessoaEntrega,
        string CodigoPessoaOrigem,
        string? Cpf,
        string? CpfAusenteMotivo,
        IReadOnlyList<SyntheticIngestionIdentifier>? Identificadores,
        string NomeCompleto,
        string DataNascimento,
        string? NomeMae);
}
