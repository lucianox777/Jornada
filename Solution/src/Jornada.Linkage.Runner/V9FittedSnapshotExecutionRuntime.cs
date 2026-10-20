using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public sealed record V9RetrievedSnapshotCandidateFrame(
    string SnapshotSha256, string ProjectionSchemaVersion, string ProjectionFingerprintSha256,
    bool CompleteCandidateUniverse, IReadOnlyList<V9SnapshotCandidate> Candidates);

/// <summary>
/// Opt-in execution contract for a pinned portable fit. Reuses the existing key
/// projectors, SQL query builder and FS/Resolve kernel. It has no production DI,
/// SQL connection, persistence, publication or activation capability.
/// </summary>
public sealed class V9FittedSnapshotExecutionRuntime
{
    private readonly V9FittedSnapshot snapshot;
    private readonly HashSet<string>? v9Features;
    public string SnapshotSha256 => snapshot.ContentSha256;
    public string ProjectionSchemaVersion => snapshot.Payload.ProjectionSchemaVersion;
    public string ProjectionFingerprintSha256 => snapshot.Payload.ProjectionFingerprintSha256;
    public int? MaximumPrefixLength { get; }

    private V9FittedSnapshotExecutionRuntime(V9FittedSnapshot fit)
    {
        snapshot = fit;
        if (fit.Payload.ProjectionSchemaVersion == V9CalibrationBlockingProjection.SchemaVersion)
        {
            var matches = Enumerable.Range(1, V9CalibrationBlockingProjection.MaximumSupportedPrefixLength)
                .Where(k => string.Equals(V9CalibrationBlockingProjection.ComputeSchemaFingerprint(k), ProjectionFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Fingerprint da projeção V9 não corresponde a um contrato de prefixos executável.");
            MaximumPrefixLength = matches[0];
            v9Features = V9CalibrationBlockingProjection.SupportedFeatures(matches[0]).ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            PersonResolutionProjectionContract.ValidateSupported(ProjectionSchemaVersion, ProjectionFingerprintSha256);
        }
        foreach (var feature in fit.Payload.BlockingPasses.SelectMany(p => p.Fields))
            _ = TemporalSemantics(feature);
    }

    public static V9FittedSnapshotExecutionRuntime Load(ReadOnlySpan<byte> content, string expectedSnapshotSha256) =>
        new(V9FittedSnapshot.Load(content, expectedSnapshotSha256));

    /// <summary>Produces the exact keys of the schema bound into the immutable fit.</summary>
    public IReadOnlyList<BlockingProjectionKey> Project(IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("Execução probabilística V9 é exclusiva para observação sem CPF.");
        return MaximumPrefixLength is { } prefix
            ? V9CalibrationBlockingProjection.Project(observation.NomeCompleto, observation.NomeMae, observation.DataNascimento, prefix)
            : BlockingProjectionKeyProjector.Project(observation.NomeCompleto, observation.NomeMae, observation.DataNascimento)
                .Concat(PersonResolutionBlockingProjector.Project(observation.ResolutionAttributes)).Distinct().ToArray();
    }

    /// <summary>Preserves frozen pass order; an unavailable field makes its pass ineligible.</summary>
    public IReadOnlyList<BlockingCandidatePassLookup> Plan(IdentityObservation observation)
    {
        var values = Project(observation).GroupBy(k => k.Feature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(k => k.Value)
                .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return snapshot.Payload.BlockingPasses.Where(p => p.Fields.All(values.ContainsKey))
            .Select(p => new BlockingCandidatePassLookup(p.PassId,
                p.Fields.Select(f => new BlockingCandidateClause(f, values[f])).ToArray())).ToArray();
    }

    /// <summary>
    /// Caller supplies the identity read from the materialization manifest. Legacy indexes
    /// cannot satisfy a V9 prefix fit. This method only builds parameterized SQL.
    /// </summary>
    public string BuildCandidateUuidQuery(DbCommand command, IdentityObservation observation,
        string materializedProjectionSchemaVersion, string materializedProjectionFingerprintSha256,
        int maximumParameters = BlockingProjectionCandidateQueryBuilder.DefaultMaxParameters)
    {
        ValidateMaterializedProjection(materializedProjectionSchemaVersion, materializedProjectionFingerprintSha256);
        return BlockingProjectionCandidateQueryBuilder.BuildVerifiedSnapshotCandidateUuidQuery(command, Plan(observation), this, maximumParameters);
    }

    /// <summary>
    /// Candidates must come from the complete, untruncated union under this materialization.
    /// The caller remains responsible for authenticating that provenance. No scorer is refit.
    /// </summary>
    public ProbabilisticLinkageDecision ResolveRetrievedCandidatesPreview(Guid diagnosticModelId,
        IdentityObservation observation, V9RetrievedSnapshotCandidateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Candidates);
        ValidateMaterializedProjection(frame.ProjectionSchemaVersion, frame.ProjectionFingerprintSha256);
        if (!string.Equals(frame.SnapshotSha256, SnapshotSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("União candidata não pertence ao conteúdo integral do snapshot fixado.");
        if (!frame.CompleteCandidateUniverse)
            throw new InvalidDataException("Resolve V9 exige união candidata completa, sem TOP N/truncamento.");
        if (snapshot.Payload.CandidateGrouping is { CandidateGrouping: not "GOLD" })
            throw new InvalidDataException("Snapshot agrupado exige replay da coorte e das arestas; Resolve isolado não pode ignorar agrupamento.");
        if (Plan(observation).Count == 0)
        {
            if (frame.Candidates.Count != 0)
                throw new InvalidDataException("Candidatos fornecidos sem passe de blocking elegível para a observação.");
            if (diagnosticModelId == Guid.Empty) throw new ArgumentException("Modelo de preview obrigatório.", nameof(diagnosticModelId));
            // An ineligible search is not an exhaustive search with zero matches:
            // progressive publication must not mint a reference from missing evidence.
            return new(ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null,
                diagnosticModelId, "EVIDENCIA_INSUFICIENTE_PARA_BLOCKING");
        }
        return snapshot.ResolvePreview(diagnosticModelId, observation, frame.Candidates);
    }

    internal void ValidateMaterializedProjection(string schemaVersion, string fingerprintSha256)
    {
        if (schemaVersion != ProjectionSchemaVersion
            || !string.Equals(fingerprintSha256, ProjectionFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Projeção materializada diverge do snapshot fixado; fallback ou resultado vazio implícito proibidos.");
    }

    internal BlockingFeatureTemporalSemantics TemporalSemantics(string feature)
    {
        if (v9Features is not null)
        {
            if (!v9Features.Contains(feature))
                throw new InvalidDataException($"Feature de blocking não pertence à projeção V9 fixada: {feature}.");
            if (feature.StartsWith("name_prefix_", StringComparison.Ordinal)
                || feature.StartsWith("mother_name_prefix_", StringComparison.Ordinal))
                return BlockingFeatureTemporalSemantics.VersionedAlias;
        }
        return BlockingFeatureTemporalCatalog.Get(feature);
    }
}
