using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Contracts;

/// <summary>
/// Contrato V1 de intercâmbio entre o gerador sintético e a avaliação independente.
/// Não substitui generation-manifest.json nem waves-manifest.json legados.
/// Não dá ao scorer acesso ao gabarito.
/// </summary>
public static class SyntheticEvaluationManifestContract
{
    public const int SchemaVersion = 1;
    public const string Version = "JORNADA_SYNTHETIC_EVALUATION_MANIFEST_V1";
    public const string PartitionMethod = "BY_BASE_PERSON_ID";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static string Serialize(SyntheticEvaluationManifest manifest)
    {
        Validate(manifest);
        return JsonSerializer.Serialize(manifest, Json) + "\n";
    }

    public static SyntheticEvaluationManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var manifest = JsonSerializer.Deserialize<SyntheticEvaluationManifest>(json, Json)
            ?? throw new InvalidDataException("Manifesto de avaliação sintética vazio.");
        Validate(manifest);
        return manifest;
    }

    public static void Validate(SyntheticEvaluationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != SchemaVersion || manifest.ContractVersion != Version)
            throw new InvalidDataException("Versão do contrato de avaliação sintética não suportada.");
        Required(manifest.GeneratorVersion, "generatorVersion");
        Required(manifest.RulesetVersion, "rulesetVersion");
        Required(manifest.RngVersion, "rngVersion");
        Required(manifest.ScenarioId, "scenarioId");
        HexSha(manifest.InputFingerprintSha256, "inputFingerprintSha256");
        if (manifest.PeopleCount <= 0 || manifest.ObservationCount <= 0)
            throw new InvalidDataException("O manifesto exige denominadores de pessoas e observações positivos.");
        if (manifest.PartitionMethod != PartitionMethod)
            throw new InvalidDataException("Particionar por observação permite vazamento de pessoas entre conjuntos.");
        if (manifest.PartitionState is not ("PROVISIONAL" or "FROZEN"))
            throw new InvalidDataException("partitionState deve ser PROVISIONAL ou FROZEN.");

        var partitions = manifest.Partitions
            ?? throw new InvalidDataException("Partições obrigatórias ausentes.");
        if (partitions.Count != 3
            || !new HashSet<string>(partitions.Select(x => x.Name), StringComparer.Ordinal)
                .SetEquals(new[] { "TRAIN", "VALIDATION", "TEST" })
            || partitions.Any(x => x.PeopleCount <= 0 || x.ObservationCount < 0)
            || partitions.Sum(x => (long)x.PeopleCount) != manifest.PeopleCount
            || partitions.Sum(x => (long)x.ObservationCount) != manifest.ObservationCount)
            throw new InvalidDataException("Partições TRAIN/VALIDATION/TEST inválidas ou denominadores divergentes.");

        var artifacts = manifest.Artifacts
            ?? throw new InvalidDataException("Hashes dos artefatos ausentes.");
        if (artifacts.Count < 3
            || artifacts.GroupBy(x => x.Role, StringComparer.Ordinal).Any(x => x.Count() != 1)
            || artifacts.GroupBy(x => x.Path, StringComparer.Ordinal).Any(x => x.Count() != 1)
            || !new HashSet<string>(artifacts.Select(x => x.Role), StringComparer.Ordinal)
                .IsSupersetOf(new[] { "PEOPLE", "OBSERVATIONS", "GROUND_TRUTH" }))
            throw new InvalidDataException("Artefatos PEOPLE, OBSERVATIONS e GROUND_TRUTH são obrigatórios e únicos.");
        foreach (var artifact in artifacts)
        {
            Required(artifact.Role, "artifacts.role");
            RelativePath(artifact.Path);
            HexSha(artifact.Sha256, "artifacts.sha256");
        }

        var rates = manifest.EffectiveRates
            ?? throw new InvalidDataException("Taxas efetivas ausentes.");
        if (rates.Count == 0
            || rates.GroupBy(x => (x.Profile, x.Gestor, x.CpfStratum, x.ErrorKind))
                .Any(x => x.Count() != 1))
            throw new InvalidDataException("As taxas devem ser informadas e ter escopo único.");
        foreach (var rate in rates)
        {
            Required(rate.Profile, "effectiveRates.profile");
            Required(rate.Gestor, "effectiveRates.gestor");
            Required(rate.ErrorKind, "effectiveRates.errorKind");
            if (rate.CpfStratum is not ("ALL" or "WITH_CPF" or "WITHOUT_CPF"))
                throw new InvalidDataException("Estrato CPF inválido.");
            ObservedRate(rate.NominalRate, rate.EligibleCount, rate.ObservedCount,
                rate.ObservedRate, "effectiveRates");
            if (rate.ParameterBasis == "EXTERNAL_EVIDENCE")
                Required(rate.ExternalSourceReference, "externalSourceReference");
            else if (rate.ParameterBasis != "EXPLORATORY_ASSUMPTION"
                     || rate.ExternalSourceReference is not null)
                throw new InvalidDataException("Proveniência da taxa inválida ou ambígua.");
        }

        var dependencies = manifest.ErrorDependencies
            ?? throw new InvalidDataException("errorDependencies deve existir (pode ser vazio).");
        if (dependencies.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new InvalidDataException("Identificadores de dependências repetidos.");
        foreach (var dependency in dependencies)
        {
            Required(dependency.Id, "errorDependencies.id");
            Required(dependency.FromErrorKind, "fromErrorKind");
            Required(dependency.ToErrorKind, "toErrorKind");
            Required(dependency.Scope, "errorDependencies.scope");
            Required(dependency.Mechanism, "errorDependencies.mechanism");
            ObservedRate(dependency.NominalJointRate, dependency.EligibleCount,
                dependency.ObservedJointCount, dependency.ObservedJointRate, "errorDependencies");
        }

        var truth = manifest.Truth
            ?? throw new InvalidDataException("Declaração de gabarito obrigatória ausente.");
        if (truth.PersonIdField != "base_person_id"
            || truth.ObservationIdField != "observacao_id"
            || truth.ObservationPersonIdField != "base_person_id"
            || truth.ArtifactRole != "GROUND_TRUTH"
            || truth.DerivedFromMotorDecision || truth.AvailableToScorer)
            throw new InvalidDataException("O gabarito deve ser independente e inacessível ao scorer.");

        var evidence = manifest.Evidence
            ?? throw new InvalidDataException("Declaração de evidências obrigatória ausente.");
        if (evidence.RealCadastreErrorRates != "UNAVAILABLE"
            || evidence.PopulationRepresentativeness != "NOT_CLAIMED"
            || evidence.RealPopulationValidationIssue != "#31")
            throw new InvalidDataException("Não declarar taxas reais ou representatividade sem outro contrato validado.");

        var reserved = manifest.ReservedFamily
            ?? throw new InvalidDataException("Declaração da família reservada obrigatória ausente.");
        if (!reserved.IsolatedFromMotorDevelopment || !reserved.TruthIndependent
            || reserved.UsedForThresholdSelection || reserved.UsedInTraining)
            throw new InvalidDataException("A família reservada não pode orientar treino, scorer ou thresholds.");
        if (reserved.State == "AWAITING_EXTERNAL_SPECIFICATION")
        {
            if (reserved.Seed is not null || reserved.SpecificationSha256 is not null
                || reserved.ExternalAuthorReference is not null || reserved.FamilyVersion is not null)
                throw new InvalidDataException("Família pendente não pode simular especificação externa.");
        }
        else if (reserved.State == "SEALED")
        {
            if (reserved.Seed is null)
                throw new InvalidDataException("Família selada requer seed externa explícita.");
            Required(reserved.ExternalAuthorReference, "reservedFamily.externalAuthorReference");
            Required(reserved.FamilyVersion, "reservedFamily.familyVersion");
            HexSha(reserved.SpecificationSha256, "reservedFamily.specificationSha256");
        }
        else throw new InvalidDataException("Estado da família reservada inválido.");
    }

    private static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Campo obrigatório ausente: {name}.");
    }

    private static void HexSha(string? value, string name)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{name} deve ser SHA-256 hexadecimal com 64 caracteres.");
    }

    private static void RelativePath(string? path)
    {
        Required(path, "artifacts.path");
        if (path!.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\')
            || path.Contains(':') || path.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("Artefato exige caminho relativo normalizado, sem travessia.");
    }

    private static void ObservedRate(double nominal, long eligible, long observed,
        double? reported, string field)
    {
        if (!double.IsFinite(nominal) || nominal < 0 || nominal > 1
            || eligible < 0 || observed < 0 || observed > eligible
            || (eligible == 0 && reported is not null)
            || (eligible > 0 && (reported is null || !double.IsFinite(reported.Value)
                || Math.Abs(reported.Value - observed / (double)eligible) > 1e-9)))
            throw new InvalidDataException($"Taxa nominal/observada ou denominador inválido: {field}.");
    }
}

public sealed record SyntheticEvaluationManifest(
    int SchemaVersion,
    string ContractVersion,
    string GeneratorVersion,
    string RulesetVersion,
    string RngVersion,
    ulong Seed,
    string ScenarioId,
    string PartitionState,
    string PartitionMethod,
    int PeopleCount,
    int ObservationCount,
    string InputFingerprintSha256,
    IReadOnlyList<SyntheticManifestPartition> Partitions,
    IReadOnlyList<SyntheticManifestArtifact> Artifacts,
    IReadOnlyList<SyntheticManifestEffectiveRate> EffectiveRates,
    IReadOnlyList<SyntheticManifestErrorDependency> ErrorDependencies,
    SyntheticManifestTruthDeclaration Truth,
    SyntheticManifestEvidenceDeclaration Evidence,
    SyntheticManifestReservedFamily ReservedFamily);

public sealed record SyntheticManifestPartition(string Name, int PeopleCount, int ObservationCount);

public sealed record SyntheticManifestArtifact(string Role, string Path, string Sha256);

/// <summary>Contagens observadas sempre carregam seu denominador e estrato explícito.</summary>
public sealed record SyntheticManifestEffectiveRate(
    string Profile, string Gestor, string CpfStratum, string ErrorKind,
    double NominalRate, long EligibleCount, long ObservedCount, double? ObservedRate,
    string ParameterBasis, string? ExternalSourceReference);

public sealed record SyntheticManifestErrorDependency(
    string Id, string FromErrorKind, string ToErrorKind, string Scope, string Mechanism,
    double NominalJointRate, long EligibleCount, long ObservedJointCount,
    double? ObservedJointRate);

public sealed record SyntheticManifestTruthDeclaration(
    string ArtifactRole, string PersonIdField, string ObservationIdField,
    string ObservationPersonIdField, bool DerivedFromMotorDecision, bool AvailableToScorer);

public sealed record SyntheticManifestEvidenceDeclaration(
    string RealCadastreErrorRates, string PopulationRepresentativeness,
    string RealPopulationValidationIssue);

/// <summary>
/// Apenas arcabouço. A equipe do motor não define conteúdo, seed nem especificação da família.
/// </summary>
public sealed record SyntheticManifestReservedFamily(
    string State, string? FamilyVersion, ulong? Seed, string? SpecificationSha256,
    string? ExternalAuthorReference, bool IsolatedFromMotorDevelopment, bool TruthIndependent,
    bool UsedInTraining, bool UsedForThresholdSelection);
