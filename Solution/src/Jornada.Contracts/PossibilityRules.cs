using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace Jornada.Contracts;

/// <summary>
/// Operadores determinísticos aceitos pelo motor versionado de Possibilidades.
/// Regras institucionais não são embutidas no código: entram por catálogo aprovado.
/// </summary>
public enum PossibilityRuleOperator
{
    PRESENTE,
    AUSENTE,
    IGUAL,
    DIFERENTE,
    EM,
    NUMERO_MAIOR_IGUAL,
    NUMERO_MENOR_IGUAL,
    DATA_MAIOR_IGUAL,
    DATA_MENOR_IGUAL
}

public sealed record PossibilityRuleCondition(
    string Fact,
    PossibilityRuleOperator Operator,
    string? Expected = null,
    IReadOnlyList<string>? ExpectedAny = null);

public sealed record PossibilityRuleSet(
    string Natureza,
    string Codigo,
    int Versao,
    string ImplementacaoVersao,
    IReadOnlyList<PossibilityRuleCondition> AllOf,
    IReadOnlyList<PossibilityRuleCondition>? AnyOf = null,
    TimeSpan? ValidFor = null);

public sealed record PossibilityFactSnapshot(
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset ObservedAt);

public sealed record PossibilityImpactSummary(
    int Population,
    int CurrentCompatible,
    int CandidateCompatible,
    int Entered,
    int Exited,
    int CurrentNotEvaluable,
    int CandidateNotEvaluable);

public static class PossibilityRuleValidator
{
    public static void Validate(PossibilityRuleSet rule)
    {
        if (rule.Natureza is not ("BENEFICIO" or "SERVICO"))
            throw new InvalidDataException("Natureza da regra de Possibilidade deve ser BENEFICIO ou SERVICO.");
        if (rule.Codigo.Length != 4 || rule.Codigo.Any(c => !(c is >= 'A' and <= 'Z') && !char.IsAsciiDigit(c)))
            throw new InvalidDataException("Código da regra de Possibilidade deve conter exatamente 4 caracteres A-Z/0-9.");
        if (rule.Versao <= 0)
            throw new InvalidDataException("Versão da regra de Possibilidade deve ser positiva.");
        if (string.IsNullOrWhiteSpace(rule.ImplementacaoVersao) || rule.ImplementacaoVersao.Length > 80)
            throw new InvalidDataException("implementacaoVersao é obrigatória e deve ter até 80 caracteres.");
        if ((rule.AllOf?.Count ?? 0) == 0 && (rule.AnyOf?.Count ?? 0) == 0)
            throw new InvalidDataException("Uma regra publicada precisa conter ao menos uma condição.");
        if (rule.ValidFor is { } validFor && validFor <= TimeSpan.Zero)
            throw new InvalidDataException("Validade da avaliação deve ser positiva.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in (rule.AllOf ?? []).Concat(rule.AnyOf ?? []))
        {
            if (string.IsNullOrWhiteSpace(condition.Fact))
                throw new InvalidDataException("Toda condição deve informar fact.");
            var signature = $"{condition.Fact}|{condition.Operator}|{condition.Expected}|{string.Join(',', condition.ExpectedAny ?? [])}";
            if (!seen.Add(signature))
                throw new InvalidDataException($"Condição duplicada: {condition.Fact}/{condition.Operator}.");

            var requiresExpected = condition.Operator is not PossibilityRuleOperator.PRESENTE and not PossibilityRuleOperator.AUSENTE and not PossibilityRuleOperator.EM;
            if (requiresExpected && condition.Expected is null)
                throw new InvalidDataException($"{condition.Operator} exige expected.");
            if (condition.Operator == PossibilityRuleOperator.EM && (condition.ExpectedAny is null || condition.ExpectedAny.Count == 0))
                throw new InvalidDataException("Operador EM exige expectedAny não vazio.");
            if (condition.Operator != PossibilityRuleOperator.EM && condition.ExpectedAny is { Count: > 0 })
                throw new InvalidDataException("expectedAny só é permitido com operador EM.");
        }
    }
}

public static class PossibilityRuleEngine
{
    public static PossibilityEvaluation Evaluate(
        PossibilityRuleSet rule,
        PossibilityFactSnapshot snapshot,
        DateTimeOffset evaluatedAt)
    {
        PossibilityRuleValidator.Validate(rule);

        foreach (var condition in rule.AllOf ?? [])
        {
            var result = EvaluateCondition(condition, snapshot.Values);
            if (result == ConditionResult.Missing)
                return NotEvaluable(condition, evaluatedAt);
            if (result == ConditionResult.False)
                return NotCompatible(condition, evaluatedAt);
        }

        if (rule.AnyOf is { Count: > 0 })
        {
            var sawMissing = false;
            foreach (var condition in rule.AnyOf)
            {
                var result = EvaluateCondition(condition, snapshot.Values);
                if (result == ConditionResult.True)
                    return Compatible(rule, evaluatedAt);
                if (result == ConditionResult.Missing)
                    sawMissing = true;
            }
            if (sawMissing)
                return new PossibilityEvaluation(PossibilityResult.NAO_AVALIAVEL, "DADOS_INSUFICIENTES_ANYOF", evaluatedAt);
            return new PossibilityEvaluation(PossibilityResult.NAO_COMPATIVEL, "NENHUMA_CONDICAO_ANYOF_ATENDIDA", evaluatedAt);
        }

        return Compatible(rule, evaluatedAt);
    }

    private static PossibilityEvaluation Compatible(PossibilityRuleSet rule, DateTimeOffset at) =>
        new(PossibilityResult.COMPATIVEL, "REGRA_VERSIONADA_ATENDIDA", at, rule.ValidFor is { } ttl ? at + ttl : null);

    private static PossibilityEvaluation NotEvaluable(PossibilityRuleCondition c, DateTimeOffset at) =>
        new(PossibilityResult.NAO_AVALIAVEL, $"DADO_AUSENTE:{c.Fact}", at);

    private static PossibilityEvaluation NotCompatible(PossibilityRuleCondition c, DateTimeOffset at) =>
        new(PossibilityResult.NAO_COMPATIVEL, $"REGRA_NAO_ATENDIDA:{c.Fact}:{c.Operator}", at);

    private enum ConditionResult { True, False, Missing }

    private static ConditionResult EvaluateCondition(PossibilityRuleCondition c, IReadOnlyDictionary<string, string?> values)
    {
        var hasValue = values.TryGetValue(c.Fact, out var raw) && !string.IsNullOrWhiteSpace(raw);
        if (c.Operator == PossibilityRuleOperator.PRESENTE) return hasValue ? ConditionResult.True : ConditionResult.False;
        if (c.Operator == PossibilityRuleOperator.AUSENTE) return hasValue ? ConditionResult.False : ConditionResult.True;
        if (!hasValue) return ConditionResult.Missing;
        var value = raw!;

        return c.Operator switch
        {
            PossibilityRuleOperator.IGUAL => Bool(string.Equals(value, c.Expected, StringComparison.Ordinal)),
            PossibilityRuleOperator.DIFERENTE => Bool(!string.Equals(value, c.Expected, StringComparison.Ordinal)),
            PossibilityRuleOperator.EM => Bool((c.ExpectedAny ?? []).Contains(value, StringComparer.Ordinal)),
            PossibilityRuleOperator.NUMERO_MAIOR_IGUAL => CompareNumber(value, c.Expected!, static (a, b) => a >= b),
            PossibilityRuleOperator.NUMERO_MENOR_IGUAL => CompareNumber(value, c.Expected!, static (a, b) => a <= b),
            PossibilityRuleOperator.DATA_MAIOR_IGUAL => CompareDate(value, c.Expected!, static (a, b) => a >= b),
            PossibilityRuleOperator.DATA_MENOR_IGUAL => CompareDate(value, c.Expected!, static (a, b) => a <= b),
            _ => throw new InvalidDataException($"Operador de Possibilidade não suportado: {c.Operator}.")
        };
    }

    private static ConditionResult Bool(bool value) => value ? ConditionResult.True : ConditionResult.False;

    private static ConditionResult CompareNumber(string actual, string expected, Func<decimal, decimal, bool> compare)
    {
        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ||
            !decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var b))
            throw new InvalidDataException("Comparação numérica de Possibilidade exige decimal InvariantCulture.");
        return Bool(compare(a, b));
    }

    private static ConditionResult CompareDate(string actual, string expected, Func<DateOnly, DateOnly, bool> compare)
    {
        if (!DateOnly.TryParseExact(actual, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) ||
            !DateOnly.TryParseExact(expected, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            throw new InvalidDataException("Comparação de data de Possibilidade exige yyyy-MM-dd.");
        return Bool(compare(a, b));
    }
}

public static class PossibilityImpactSimulator
{
    public static PossibilityImpactSummary Compare(
        PossibilityRuleSet current,
        PossibilityRuleSet candidate,
        IReadOnlyDictionary<Guid, PossibilityFactSnapshot> population,
        DateTimeOffset evaluatedAt)
    {
        PossibilityRuleValidator.Validate(current);
        PossibilityRuleValidator.Validate(candidate);
        if (!string.Equals(current.Natureza, candidate.Natureza, StringComparison.Ordinal) ||
            !string.Equals(current.Codigo, candidate.Codigo, StringComparison.Ordinal))
            throw new InvalidDataException("Dry-run só compara versões da mesma Natureza/Código.");

        var currentCompatible = 0;
        var candidateCompatible = 0;
        var entered = 0;
        var exited = 0;
        var currentNotEvaluable = 0;
        var candidateNotEvaluable = 0;
        foreach (var snapshot in population.Values)
        {
            var before = PossibilityRuleEngine.Evaluate(current, snapshot, evaluatedAt).Resultado;
            var after = PossibilityRuleEngine.Evaluate(candidate, snapshot, evaluatedAt).Resultado;
            if (before == PossibilityResult.COMPATIVEL) currentCompatible++;
            if (after == PossibilityResult.COMPATIVEL) candidateCompatible++;
            if (before != PossibilityResult.COMPATIVEL && after == PossibilityResult.COMPATIVEL) entered++;
            if (before == PossibilityResult.COMPATIVEL && after != PossibilityResult.COMPATIVEL) exited++;
            if (before == PossibilityResult.NAO_AVALIAVEL) currentNotEvaluable++;
            if (after == PossibilityResult.NAO_AVALIAVEL) candidateNotEvaluable++;
        }
        return new(population.Count, currentCompatible, candidateCompatible, entered, exited, currentNotEvaluable, candidateNotEvaluable);
    }
}


public sealed record PossibilityRuleApproval(
    DateTimeOffset ApprovedAtUtc,
    string ApprovedBy,
    string EvidencePath,
    string EvidenceSha256);

public sealed record PossibilityRuleCatalog(
    string Status,
    string CatalogVersion,
    IReadOnlyList<PossibilityRuleSet> Rules,
    PossibilityRuleApproval? Approval);

public static class PossibilityRuleCatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PossibilityRuleCatalog Load(string json, bool requireApproved = true)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
            throw new InvalidDataException("Catálogo de Possibilidades exige schemaVersion=1.");
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (status is not ("PENDENTE" or "APROVADO"))
            throw new InvalidDataException("Status do catálogo de Possibilidades inválido.");
        if (requireApproved && status != "APROVADO")
            throw new InvalidDataException("Catálogo de Possibilidades não está aprovado para execução.");
        var version = root.TryGetProperty("catalogVersion", out var cv) ? cv.GetString() : null;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException("catalogVersion é obrigatório.");
        var rules = root.TryGetProperty("rules", out var r)
            ? JsonSerializer.Deserialize<List<PossibilityRuleSet>>(r.GetRawText(), Options) ?? []
            : [];
        foreach (var rule in rules) PossibilityRuleValidator.Validate(rule);

        var approval = ParseApproval(root);
        if (status == "PENDENTE")
        {
            if (rules.Count != 0)
                throw new InvalidDataException("Catálogo PENDENTE não pode distribuir regras como vigentes.");
            if (approval is not null)
                throw new InvalidDataException("Catálogo PENDENTE não pode declarar aprovação.");
        }
        if (status == "APROVADO")
        {
            if (rules.Count == 0)
                throw new InvalidDataException("Catálogo APROVADO precisa conter regras.");
            if (approval is null)
                throw new InvalidDataException("Catálogo APROVADO exige metadados de aprovação e evidência.");
        }
        return new(status!, version!, rules, approval);
    }

    public static PossibilityRuleCatalog LoadFromFile(string catalogPath, string evidenceRoot, bool requireApproved = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        var catalog = Load(File.ReadAllText(catalogPath), requireApproved);
        if (catalog.Approval is null) return catalog;

        var root = Path.GetFullPath(evidenceRoot);
        var evidence = Path.GetFullPath(Path.Combine(root, catalog.Approval.EvidencePath));
        var relative = Path.GetRelativePath(root, evidence);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException("approval.evidencePath escapa da raiz de evidências permitida.");
        if (!File.Exists(evidence))
            throw new InvalidDataException("Evidência de aprovação do catálogo de Possibilidades não foi localizada.");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidence))).ToLowerInvariant();
        if (!string.Equals(actual, catalog.Approval.EvidenceSha256, StringComparison.Ordinal))
            throw new InvalidDataException("SHA-256 da evidência de aprovação do catálogo de Possibilidades não confere.");
        return catalog;
    }

    private static PossibilityRuleApproval? ParseApproval(JsonElement root)
    {
        if (!root.TryGetProperty("approval", out var a) || a.ValueKind == JsonValueKind.Null) return null;
        if (a.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("approval deve ser objeto ou null.");
        var approvedAtRaw = RequiredString(a, "approvedAtUtc");
        if (!DateTimeOffset.TryParse(approvedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var approvedAt))
            throw new InvalidDataException("approval.approvedAtUtc deve ser data/hora ISO-8601 válida.");
        var approvedBy = RequiredString(a, "approvedBy");
        var evidencePath = RequiredString(a, "evidencePath");
        if (Path.IsPathRooted(evidencePath) || evidencePath.StartsWith('/') ||
            evidencePath.Contains('\\', StringComparison.Ordinal) || evidencePath.Contains(':', StringComparison.Ordinal) || evidencePath.Split('/').Any(x => x == ".."))
            throw new InvalidDataException("approval.evidencePath deve ser caminho relativo portável, sem traversal.");
        var evidenceSha = RequiredString(a, "evidenceSha256").ToLowerInvariant();
        if (evidenceSha.Length != 64 || evidenceSha.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("approval.evidenceSha256 deve conter 64 caracteres hexadecimais.");
        return new(approvedAt, approvedBy, evidencePath, evidenceSha);
    }

    private static string RequiredString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"approval.{name} é obrigatório.");
        return value.GetString()!;
    }
}

public sealed class ConfiguredPossibilityEvaluator(
    PossibilityRuleSet rule,
    Func<Guid, CancellationToken, Task<PossibilityFactSnapshot>> factsProvider,
    TimeProvider? timeProvider = null) : IPossibilityEvaluator
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public string Natureza => rule.Natureza;
    public string Codigo => rule.Codigo;
    public int Versao => rule.Versao;

    public async Task<PossibilityEvaluation> EvaluateAsync(Guid pessoaUuid, CancellationToken ct)
    {
        var facts = await factsProvider(pessoaUuid, ct);
        return PossibilityRuleEngine.Evaluate(rule, facts, clock.GetUtcNow());
    }
}
