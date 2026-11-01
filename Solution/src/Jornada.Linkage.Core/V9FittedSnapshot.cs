using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>Portable fitted parameters, never an approval or activation credential.</summary>
public sealed record V9FittedSnapshotPayload(
    string SchemaVersion, string NominalContractVersion, string ProjectionSchemaVersion,
    string ProjectionFingerprintSha256, string FitFingerprintSha256,
    string TrainCorpusSha256, string ValidationCorpusSha256,
    string TrainIdentityGroupsSha256, string ValidationIdentityGroupsSha256,
    string AdapterVersion, string OptionId, decimal Threshold, decimal ConflictMargin,
    decimal TfWeight, IReadOnlyList<LinkageBlockingPass> BlockingPasses,
    IReadOnlyList<FellegiSunterJointNominalCellV9> JointCells,
    IReadOnlyDictionary<string, decimal> Parameters,
    V9SnapshotFirstNameTf? TfCalibration = null,
    string? BlockingFrameFingerprintSha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    V9SnapshotCandidateGrouping? CandidateGrouping = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    V9JointSupportProvenance? JointSupport = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GoldParentSnapshotSha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    V9SnapshotFirstNameTf? MotherTfCalibration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<V9SnapshotUnitTf>? UnitTfCalibrations = null);

public sealed record V9SnapshotCandidateGrouping(string CandidateGrouping,
    decimal MinimumPairPosterior, int MaximumObservationsPerIdentity);

public sealed record V9SnapshotFirstNameTf(
    CompositeNominalTfSideV9 Side, NominalFrequencyReferenceContractV9 Reference,
    IReadOnlyDictionary<string, decimal> Frequencies,
    IReadOnlyDictionary<string, decimal> TrainUnitUByValue,
    string TrainUnitFingerprintSha256, decimal MinimumFrequency,
    long MinimumIndependentSupport, string TrainCorpusFingerprintSha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    CompositeNominalTfAdjustmentModeV9 Mode = CompositeNominalTfAdjustmentModeV9.Symmetric,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Weight = null);

/// <summary>Explicit outcome of fitting a TF unit, including calibrated zero.</summary>
public enum V9SnapshotTfFitState
{
    Active,
    ZeroChosen,
    InsufficientSupport,
    NoSource
}

/// <summary>
/// One independent TF fit per (side, unit). A zero chosen by the calibrator
/// retains its frozen reference; missing evidence is never labelled zero.
/// </summary>
public sealed record V9SnapshotUnitTf(
    CompositeNominalTfSideV9 Side,
    NominalFrequencyUnitV9 Unit,
    V9SnapshotTfFitState State,
    V9SnapshotFirstNameTf? Calibration);

public sealed record V9SnapshotCandidate(
    Guid PersonUuid, string? FullName, DateOnly? BirthDate, string? MotherName);

/// <summary>
/// Immutable, content-addressed fitted snapshot. Loading validates bytes and contracts;
/// it never trains, publishes, selects an active reference or bypasses DT-15.
/// Positive TF includes the frozen first-name reference and conditional TRAIN U;
/// unknown terms fail closed rather than reverting to a neutral factor.
/// </summary>
public sealed class V9FittedSnapshot
{
    public const string SchemaVersion = "V9_FITTED_SNAPSHOT_V2";
    public const string SupportSchemaVersion = "V9_FITTED_SNAPSHOT_V3";
    public const int MaximumContentBytes = 67_108_864;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };
    private readonly byte[] utf8;
    private readonly FrozenFellegiSunterJointNominalV9 frozenNominal;
    private readonly IReadOnlyDictionary<string, double> numericParameters;
    public V9FittedSnapshotPayload Payload { get; }
    public string ContentSha256 { get; }

    private V9FittedSnapshot(V9FittedSnapshotPayload payload)
    {
        Payload = FreezeAndValidate(payload);
        frozenNominal = FrozenFellegiSunterJointNominalV9.Freeze(Payload.JointCells, Payload.NominalContractVersion);
        numericParameters = FellegiSunterScoring.ToDoubleParameters(Payload.Parameters);
        utf8 = JsonSerializer.SerializeToUtf8Bytes(Payload, JsonOptions);
        if (utf8.Length > MaximumContentBytes)
            throw new InvalidDataException("Snapshot V9 excede o limite permitido para carga e persistência.");
        ContentSha256 = Convert.ToHexString(SHA256.HashData(utf8)).ToLowerInvariant();
    }

    public static V9FittedSnapshot Create(V9FittedSnapshotPayload payload) => new(payload);
    public byte[] SerializeUtf8() => (byte[])utf8.Clone();

    /// <summary>
    /// Experimental Gold retrieval variant of the SAME fit, never an operational
    /// approval. Real Gold membership must be supplied by the incremental pipeline,
    /// not synthetic labels or the cohort complete-link builder.
    /// </summary>
    public V9FittedSnapshot CreateGoldHarnessVariant()
    {
        if (Payload.JointSupport is null || Payload.TfWeight != 0
            || Payload.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES")
            throw new InvalidDataException("Variante Gold exige fit V3 BLIND TF0 congelado.");
        return Create(Payload with { CandidateGrouping = new("GOLD", 0, 0), GoldParentSnapshotSha256 = ContentSha256 });
    }

    /// <summary>
    /// TRAIN-only frozen Gold scoring variant for an explicitly declared upper
    /// candidate. No m/u, TF, blocking, prior, fit fingerprint or gold grouping
    /// is refitted or changed. This immutable snapshot gets a distinct SHA per
    /// upper and must be executed on a fresh disposable Gold LocalDB.
    /// </summary>
    public V9FittedSnapshot CreateGoldHarnessVariantAtUpper(decimal upper)
    {
        if (upper is <= 0m or > 1m)
            throw new InvalidDataException("Joint Gold TRAIN upper must satisfy 0 < upper <= 1.");
        var baseVariant = CreateGoldHarnessVariant();
        return Create(baseVariant.Payload with { Threshold = upper });
    }

    /// <summary>The expected digest must come from the pinned reference, not the payload.</summary>
    public static V9FittedSnapshot Load(ReadOnlySpan<byte> content, string expectedSha256)
    {
        RequireSha(expectedSha256);
        if (content.Length <= 0 || content.Length > MaximumContentBytes
            || !Convert.ToHexString(SHA256.HashData(content)).Equals(expectedSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot V9 diverge do SHA-256 fixado ou excede o tamanho permitido.");
        V9FittedSnapshotPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<V9FittedSnapshotPayload>(content, JsonOptions)
                ?? throw new InvalidDataException("Snapshot V9 vazio.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Schema do snapshot V9 inválido.", error);
        }
        var snapshot = new V9FittedSnapshot(payload);
        // Require the canonical encoding, preventing multiple byte identities
        // for one fitted artifact and silent normalization at application startup.
        if (!content.SequenceEqual(snapshot.utf8))
            throw new InvalidDataException("Snapshot V9 não utiliza serialização canônica.");
        return snapshot;
    }

    public ProbabilisticLinkageDecision ResolvePreview(Guid diagnosticModelId,
        IdentityObservation observation, IReadOnlyList<V9SnapshotCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Any(c => c is null || c.PersonUuid == Guid.Empty))
            throw new InvalidDataException("Identidade candidata V9 inválida.");
        if (diagnosticModelId == Guid.Empty) throw new ArgumentException("ID do estudo V9 é obrigatório.", nameof(diagnosticModelId));
        return ResolveRankedPreview(diagnosticModelId, RankPreview(observation, candidates));
    }

    internal IReadOnlyList<CandidateScore> RankPreview(IdentityObservation observation,
        IReadOnlyList<V9SnapshotCandidate> candidates)
    {
        var converted = candidates.Select(c =>
            new LinkageCandidate(c.PersonUuid, c.FullName, c.BirthDate, c.MotherName)).ToArray();
        // A second side must never silently fall back to the legacy one-sided path.
        if (Payload.MotherTfCalibration is not null)
            return ProbabilisticLinkageDecisions.RankFrozenCompositeNominalV9BilateralPreview(
                Payload.Parameters, numericParameters, observation, converted, frozenNominal,
                (o, c) => CreateTf(o, c, Payload.TfCalibration!),
                (o, c) => CreateTf(o, c, Payload.MotherTfCalibration!),
                Payload.TfCalibration!.Reference.Unit,
                Payload.MotherTfCalibration!.Reference.Unit);
        return ProbabilisticLinkageDecisions.RankFrozenCompositeNominalV9Preview(
            Payload.Parameters, numericParameters, observation, converted, frozenNominal,
            Payload.TfCalibration is null ? null : (o, c) => CreateTf(o, c, Payload.TfCalibration!),
            Payload.TfCalibration?.Side);
    }

    internal ProbabilisticLinkageDecision ResolveRankedPreview(Guid diagnosticModelId,
        IReadOnlyList<CandidateScore> ranking) =>
        ProbabilisticLinkageDecisions.ResolveRankedCompositeNominalV9Preview(diagnosticModelId,
            Payload.Parameters, ranking, Payload.Threshold, Payload.ConflictMargin);

    private CompositeNominalTfPreviewV9? CreateTf(IdentityObservation observation, LinkageCandidate candidate,
        V9SnapshotFirstNameTf tf)
    {
        var left = tf.Side == CompositeNominalTfSideV9.Person ? observation.NomeCompleto : observation.NomeMae;
        var right = tf.Side == CompositeNominalTfSideV9.Person ? candidate.NomeCompleto : candidate.NomeMae;
        var level = CompositeNominalComparisonV9.Classify(left, right);
        var eligible = tf.Reference.Unit == NominalFrequencyUnitV9.LastContentSurname
            ? level == CompositeNominalLevelV9.ExactLast
            : level is CompositeNominalLevelV9.ExactFirst or CompositeNominalLevelV9.ExactFull;
        if (!eligible)
            return null;
        var term = tf.Reference.Unit == NominalFrequencyUnitV9.LastContentSurname
            ? BrazilianNameComponents.Project(left)?.LastContentSurname : FirstName(left);
        var rightTerm = tf.Reference.Unit == NominalFrequencyUnitV9.LastContentSurname
            ? BrazilianNameComponents.Project(right)?.LastContentSurname : FirstName(right);
        if (term is null || term != rightTerm
            || !tf.Frequencies.TryGetValue(term, out var frequency)
            || !tf.TrainUnitUByValue.TryGetValue(term, out var unitU))
            throw new InvalidDataException("TF positiva desconhecida: referência ou U TRAIN ausente no snapshot.");
        return new CompositeNominalTfPreviewV9(tf.Side, tf.Reference, term, frequency,
            unitU, tf.Weight ?? Payload.TfWeight, tf.MinimumFrequency, tf.MinimumIndependentSupport, tf.Mode);
    }

    private static string? FirstName(string? name)
    {
        var projection = BrazilianNameComponents.Project(name);
        if (projection is null) return null;
        var tokens = projection.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = projection.TitlePrefix is null ? 0 : 1;
        if (index >= tokens.Length) return null;
        var value = tokens[index].Trim('.', ',');
        return value.Length == 0 ? null : value;
    }

    private static V9FittedSnapshotPayload FreezeAndValidate(V9FittedSnapshotPayload p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.CandidateGrouping is { } grouping)
        {
            if (grouping.CandidateGrouping == "GOLD")
            {
                RequireSha(p.GoldParentSnapshotSha256!);
                if (p.JointSupport is null || grouping.MinimumPairPosterior != 0 || grouping.MaximumObservationsPerIdentity != 0)
                    throw new InvalidDataException("Gold usa entidades reais e proveniência do fit, sem guard de agrupamento BLIND.");
            }
            else if (grouping.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
                || grouping.MinimumPairPosterior is <= .5m or >= 1m || grouping.MaximumObservationsPerIdentity < 2)
                throw new InvalidDataException("Contrato de agrupamento do snapshot V9 inválido.");
        }
        if (p.GoldParentSnapshotSha256 is not null && p.CandidateGrouping?.CandidateGrouping != "GOLD")
            throw new InvalidDataException("Proveniência Gold exige variante Gold explícita.");
        if (p.SchemaVersion != (p.JointSupport is null ? SchemaVersion : SupportSchemaVersion) || p.NominalContractVersion != CompositeNominalComparisonV9.ContractVersion
            || string.IsNullOrWhiteSpace(p.ProjectionSchemaVersion)
            || string.IsNullOrWhiteSpace(p.AdapterVersion) || string.IsNullOrWhiteSpace(p.OptionId)
            || p.Threshold is <= 0m or > 1m || p.ConflictMargin is < 0m or > 1000m
            || p.TfWeight is < 0m or > 1m || (p.TfWeight == 0m) != (p.TfCalibration is null)
            || p.BlockingPasses is null || p.BlockingPasses.Count == 0
            || p.JointCells is null || p.Parameters is null)
            throw new InvalidDataException("Snapshot V9 incompleto, incompatível ou TF sem referência congelada.");
        foreach (var hash in new[] { p.ProjectionFingerprintSha256, p.FitFingerprintSha256,
                     p.TrainCorpusSha256, p.ValidationCorpusSha256,
                     p.TrainIdentityGroupsSha256, p.ValidationIdentityGroupsSha256 })
            RequireSha(hash);
        if (p.BlockingFrameFingerprintSha256 is not null)
            RequireSha(p.BlockingFrameFingerprintSha256);
        if (p.TrainCorpusSha256.Equals(p.ValidationCorpusSha256, StringComparison.OrdinalIgnoreCase)
            || p.TrainIdentityGroupsSha256.Equals(p.ValidationIdentityGroupsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot V9 mistura TRAIN e VALIDATION.");
        var passes = p.BlockingPasses.Select(pass =>
        {
            if (pass is null || string.IsNullOrWhiteSpace(pass.PassId) || pass.Fields is null
                || pass.Fields.Count == 0 || pass.Fields.Any(string.IsNullOrWhiteSpace)
                || pass.Fields.Distinct(StringComparer.Ordinal).Count() != pass.Fields.Count)
                throw new InvalidDataException("Passe de blocking V9 inválido.");
            return new LinkageBlockingPass(pass.PassId, Array.AsReadOnly(pass.Fields.ToArray()));
        }).ToArray();
        if (passes.Select(x => x.PassId).Distinct(StringComparer.Ordinal).Count() != passes.Length)
            throw new InvalidDataException("Passes de blocking V9 duplicados.");
        var cells = Array.AsReadOnly(p.JointCells.OrderBy(x => x?.Person).ThenBy(x => x?.Mother).ToArray());
        _ = FrozenFellegiSunterJointNominalV9.Freeze(cells, p.NominalContractVersion);
        var parameters = new ReadOnlyDictionary<string, decimal>(p.Parameters
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        if (!parameters.TryGetValue(LinkageParameterCatalog.PriorMatchProbability, out var prior)
            || prior is <= 0m or >= 1m
            || parameters.GetValueOrDefault(LinkageParameterCatalog.DecisionEvidenceScoring) != 1m
            || parameters.GetValueOrDefault(LinkageParameterCatalog.NeutralMissingEvidenceScoring) != 1m)
            throw new InvalidDataException("Prior e contrato de evidência V9 ausentes.");
        // Validate even before scoring an empty block. Unknown birth probabilities
        // must not become neutral factors merely because this record lacks DOB.
        if (parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var birth) && birth != 0m)
        {
            if (birth != 1m) throw new InvalidDataException("Estratégia de nascimento V9 inválida.");
            foreach (var kind in new[] { "M", "U" })
            {
                if (!parameters.TryGetValue(kind + "_DATA_NASCIMENTO_EXACT", out var exact)
                    || !parameters.TryGetValue(kind + "_DATA_NASCIMENTO_DIFF", out var diff)
                    || exact is <= 0m or >= 1m || diff is <= 0m or >= 1m
                    || Math.Abs(exact + diff - 1m) > 0.000001m)
                    throw new InvalidDataException("Distribuição de nascimento V9 incompleta ou não normalizada.");
            }
        }
        else
            // The portable V2 fit declares the calibrated binary birth strategy.
            // Individual records may omit DOB; an undeclared model strategy
            // would otherwise fall through to historical day/month/year scoring.
            throw new InvalidDataException("Snapshot V9 exige estratégia binária de nascimento calibrada; DOB de entrada continua opcional.");
        foreach (var unsupported in new[] { LinkageParameterCatalog.BirthSemanticEvidenceScoring,
                     LinkageParameterCatalog.BirthJointEvidenceScoring, LinkageParameterCatalog.BirthComponentScoring,
                     LinkageParameterCatalog.LegacyBirthComponentScoring })
            if (parameters.GetValueOrDefault(unsupported) != 0m)
                throw new InvalidDataException("Estratégia de nascimento não suportada pelo schema do snapshot.");
        // New multi-unit fitting metadata is validated independently of the
        // legacy two-slot execution path. Never accept duplicate or contradictory
        // state/reference combinations.
        if (p.UnitTfCalibrations is { } unitFits)
        {
            if (unitFits.Count > 4 || unitFits.Any(x => x is null
                || !Enum.IsDefined(x.Side) || !Enum.IsDefined(x.Unit)
                || !Enum.IsDefined(x.State))
                || unitFits.Select(x => (x.Side, x.Unit)).Distinct().Count() != unitFits.Count)
                throw new InvalidDataException("Configurações TF por lado/unidade inválidas ou duplicadas.");
            foreach (var fit in unitFits)
            {
                var hasFit = fit.State is V9SnapshotTfFitState.Active or V9SnapshotTfFitState.ZeroChosen;
                if (hasFit != (fit.Calibration is not null)
                    || (fit.Calibration is { } config
                        && (config.Side != fit.Side || config.Reference.Unit != fit.Unit
                            || (fit.State == V9SnapshotTfFitState.ZeroChosen && config.Weight != 0m)
                            || (fit.State == V9SnapshotTfFitState.Active && config.Weight is not > 0m))))
                    throw new InvalidDataException("Estado TF incompatível com referência e peso calibrados.");
            }
        }
        // The mother reference is separately fitted and must not reuse a person-side U.
        if (p.MotherTfCalibration is { } mother &&
            (p.TfCalibration?.Side != CompositeNominalTfSideV9.Person
             || mother.Side != CompositeNominalTfSideV9.Mother
             || mother.Weight is null || mother.Weight <= 0m || mother.Weight > 1m))
            throw new InvalidDataException("TF bilateral exige lados distintos e peso calibrado da mãe.");

        V9SnapshotFirstNameTf? FreezeTf(V9SnapshotFirstNameTf? tf)
        {
            if (tf is null) return null;
            RequireSha(tf.TrainUnitFingerprintSha256);
            RequireSha(tf.TrainCorpusFingerprintSha256);
            RequireSha(p.BlockingFrameFingerprintSha256!);
            if (!tf.TrainCorpusFingerprintSha256.Equals(p.TrainCorpusSha256, StringComparison.OrdinalIgnoreCase)
                || !Enum.IsDefined(tf.Side) || !Enum.IsDefined(tf.Mode) || tf.Reference is null
                || (tf.Weight is { } ownWeight && (ownWeight <= 0m || ownWeight > 1m))
                || !p.BlockingFrameFingerprintSha256!.Equals(tf.Reference.BlockingFrameFingerprint,
                    StringComparison.OrdinalIgnoreCase)
                || tf.MinimumFrequency is < 0m or > 1m || tf.MinimumIndependentSupport <= 0)
                throw new InvalidDataException("Snapshot TF incompatível com TRAIN ou configuração.");
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                tf.Reference.Unit == NominalFrequencyUnitV9.LastContentSurname
                    ? CompositeNominalLevelV9.ExactLast : CompositeNominalLevelV9.ExactFirst,
                tf.Reference, tf.MinimumIndependentSupport);
            if (tf.Reference.Source == NominalFrequencySourceV9.IbgePublishedMarginal
                && (!tf.Reference.PopulationFrameFingerprint.StartsWith("IBGE_PUBLISHED:", StringComparison.Ordinal)
                    || !tf.Reference.PopulationFrameFingerprint.EndsWith(
                        tf.Side == CompositeNominalTfSideV9.Person ? ":SP_3550308_TODOS" : ":BR_FEMININO",
                        StringComparison.Ordinal)))
                throw new InvalidDataException("Marginal IBGE incompatível com o lado nominal do snapshot TF.");
            static IReadOnlyDictionary<string, decimal> FreezeTerms(IReadOnlyDictionary<string, decimal> terms)
            {
                if (terms is null || terms.Count == 0 || terms.Any(x => string.IsNullOrWhiteSpace(x.Key)
                        || FirstName(x.Key) != x.Key || x.Value is <= 0m or > 1m))
                    throw new InvalidDataException("Tabela TF/U TRAIN inválida ou sem termos normalizados.");
                return new ReadOnlyDictionary<string, decimal>(terms.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
            }
            var frequencies = FreezeTerms(tf.Frequencies);
            var trainU = FreezeTerms(tf.TrainUnitUByValue);
            if (trainU.Values.Sum() > 1.000001m)
                throw new InvalidDataException("Distribuição condicional U TRAIN excede um.");
            return tf with { Frequencies = frequencies, TrainUnitUByValue = trainU };
        }
        var frozenTf = FreezeTf(p.TfCalibration);
        var frozenMotherTf = FreezeTf(p.MotherTfCalibration);
        // Reuse the proven prenome freeze/validation path for every unit.
        // A configured zero remains a fitted reference, not missing evidence.
        var frozenUnits = p.UnitTfCalibrations is null ? null
            : Array.AsReadOnly(p.UnitTfCalibrations.Select(fit =>
                fit with { Calibration = FreezeTf(fit.Calibration) })
                .OrderBy(fit => fit.Side).ThenBy(fit => fit.Unit).ToArray());
        return p with { BlockingPasses = Array.AsReadOnly(passes), JointCells = cells,
            Parameters = parameters, TfCalibration = frozenTf, MotherTfCalibration = frozenMotherTf,
            UnitTfCalibrations = frozenUnits,
            JointSupport = p.JointSupport is null ? null : V9JointSupportEstimation.VerifyAndFreeze(p.JointSupport, cells, p.TrainCorpusSha256) };
    }

    private static void RequireSha(string value)
    {
        if (value is not { Length: 64 } || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Snapshot V9 exige proveniência SHA-256 válida.");
    }
}
