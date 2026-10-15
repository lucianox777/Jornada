using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>Diagnostic-only eligibility for probabilistic V9 evaluation; does not resolve identity.</summary>
public static class V9MinimumEvidenceEligibility
{
    public const string Version = "V9_MINIMUM_TWO_FIELDS_DIAGNOSTIC_V1";

    public sealed record Result(bool Eligible, int ValidFields, string Combination, bool MustAbstain);

    // Candidate policy only: the person's name must be usable, together
    // with either the mother's name or date of birth. MD alone abstains.
    public const string PersonRequiredVersion =
        "V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1";

    public static Result EvaluatePersonRequired(
        string? personName, string? motherName, DateTime? birthDate)
    {
        var original = Evaluate(personName, motherName, birthDate);
        var allowed = original.Combination is "PM" or "PD" or "PMD";
        return original with { Eligible = allowed, MustAbstain = !allowed };
    }

    public static Result Evaluate(string? personName, string? motherName, DateTime? birthDate)
    {
        var person = HasName(personName);
        var mother = HasName(motherName);
        var birth = HasBirth(birthDate);
        var count = (person ? 1 : 0) + (mother ? 1 : 0) + (birth ? 1 : 0);
        var combination = string.Concat(person ? "P" : "", mother ? "M" : "", birth ? "D" : "");
        return new Result(count >= 2, count, combination.Length == 0 ? "NONE" : combination, count < 2);
    }

    public static bool HasName(string? value) =>
        PersonResolutionMissingValuePolicy.HasUsableName(value);

    public static bool HasBirth(DateTime? date) =>
        PersonResolutionMissingValuePolicy.HasUsableBirthDate(date);
}
