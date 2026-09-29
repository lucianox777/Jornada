using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Conference;

/// <summary>
/// Confere oráculos LITERAIS obtidos por odds racionais e logaritmos documentados.
/// Nenhum resultado esperado é gerado pelo scorer ou pelos comparadores operacionais.
/// Não persiste evidência governada nem substitui a conferência por modelo em SQL.
/// </summary>
internal static class GoldenReferenceVectorVerifier
{
    internal const string ReferenceVersion = "JORNADA_CONFERENCE_GOLDEN_V1_2026-09-29";
    private const decimal MaxNumericDifference = 0.000000000001m;
    private static readonly Guid ModelId = Guid.Parse("a0100000-0000-4000-8000-000000000001");
    private static readonly Guid SingleCandidateId = Guid.Parse("a0100000-0000-4000-8000-000000000002");

    internal static GoldenReferenceVerification Verify(
        string vectorsPath,
        string toleranceConfigPath)
    {
        var config = ImplementationConferenceToleranceConfiguration.Load(toleranceConfigPath);
        var tolerance = config.ToContract();
        if (!tolerance.TryGetFrozen(out var maxLlrDifference, out var reason))
            throw new InvalidDataException($"GOLDEN_TOLERANCE_NOT_FROZEN:{reason}");
        Require(tolerance.Version == "V1_2026-09-26"
            && maxLlrDifference == 0.01m,
            "FROZEN_TOLERANCE_MISMATCH");

        var bytes = File.ReadAllBytes(vectorsPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1,
            "SCHEMA_VERSION");
        Require(Text(root, "referenceVersion") == ReferenceVersion,
            "REFERENCE_VERSION");
        Require(Text(root, "toleranceVersion") == tolerance.Version,
            "TOLERANCE_VERSION");
        Require(Text(root, "derivationMethod")
                == "ALGEBRA_RATIONAL_ODDS_FIXED_LN_CONSTANTS_NO_OPERATIONAL_SCORER",
            "DERIVATION_METHOD");

        var review = root.GetProperty("review");
        var reviewStatus = Text(review, "status");
        Require(reviewStatus is "PENDING_HUMAN_REVIEW" or "REVIEWED",
            "REVIEW_STATUS_INVALID");
        if (reviewStatus == "REVIEWED")
        {
            Require(!string.IsNullOrWhiteSpace(TextOrNull(review, "reviewer"))
                && !string.IsNullOrWhiteSpace(TextOrNull(review, "reviewedAtUtc"))
                && !string.IsNullOrWhiteSpace(TextOrNull(review, "reviewReference")),
                "HUMAN_REVIEW_METADATA_INCOMPLETE");
        }

        var scoreCount = 0;
        foreach (var vector in root.GetProperty("scoringVectors").EnumerateArray())
        {
            VerifyScoring(vector, tolerance);
            scoreCount++;
        }

        var nameCount = 0;
        foreach (var vector in root.GetProperty("nameVectors").EnumerateArray())
        {
            VerifyName(vector);
            nameCount++;
        }

        var birthCount = 0;
        foreach (var vector in root.GetProperty("birthVectors").EnumerateArray())
        {
            VerifyBirth(vector);
            birthCount++;
        }

        var policyCount = 0;
        foreach (var vector in root.GetProperty("policyVectors").EnumerateArray())
        {
            VerifyPolicy(vector, tolerance);
            policyCount++;
        }

        Require(scoreCount >= 7 && nameCount >= 9
            && birthCount == BirthDateSemanticEvidence.States.Count && policyCount >= 6,
            "REFERENCE_COVERAGE_INCOMPLETE");
        return new GoldenReferenceVerification(
            ReferenceVersion,
            tolerance.Version,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            scoreCount,
            nameCount,
            birthCount,
            policyCount,
            reviewStatus,
            "TECHNICAL_VECTORS_PASSED_NOT_SQL_EVIDENCE");
    }

    private static void VerifyScoring(
        JsonElement vector,
        ImplementationConferenceToleranceContract tolerance)
    {
        var id = Text(vector, "id");
        var v8 = Text(vector, "algorithm") switch
        {
            "V6" => false,
            "V8" => true,
            _ => throw new InvalidDataException($"GOLDEN_VECTOR_MISMATCH:{id}:ALGORITHM")
        };
        var p = BaselineParameters(v8, Number(vector, "prior"), Number(vector, "threshold"));
        if (vector.GetProperty("demographicGuard").GetBoolean())
            p[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
        ValidateDistributions(p, v8);

        var algorithm = v8
            ? LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion
            : LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion;
        var model = LinkageModelPolicy.Create(ModelId, v8 ? 8 : 6, algorithm, p);
        var name = ReadName(vector, "name");
        var mother = ReadName(vector, "mother");
        var leftBirth = ReadBirth(vector, "birthLeft");
        var rightBirth = ReadBirth(vector, "birthRight");
        var canonical = FellegiSunterScoring.CalculateWithBreakdown(
            p, name, mother, null, leftBirth, rightBirth);
        var expected = vector.GetProperty("expected");
        var evidence = vector.GetProperty("evidence").EnumerateArray().ToArray();
        Require(canonical.Contributions.Count == evidence.Length,
            $"{id}:EVIDENCE_COUNT");

        var expectedStates = new List<ImplementationConferenceEvidence>();
        for (var i = 0; i < evidence.Length; i++)
        {
            var row = evidence[i];
            var actual = canonical.Contributions[i];
            var field = Text(row, "field");
            var state = Text(row, "state");
            Require(actual.Evidence == field && actual.State == state,
                $"{id}:EVIDENCE_STATE:{field}");
            var m = OptionalNumber(row, "m");
            var u = OptionalNumber(row, "u");
            var ratio = OptionalNumber(row, "ratio");
            if (m is null || u is null)
            {
                Require(m is null && u is null && ratio is null
                    && state == "MISSING_NEUTRAL"
                    && actual.MProbability is null && actual.UProbability is null,
                    $"{id}:MISSING_NEUTRAL_SHAPE");
            }
            else
            {
                Require(m > 0m && m <= 1m && u > 0m && u <= 1m
                    && ratio is not null && ratio > 0m,
                    $"{id}:M_U_NOT_POSITIVE");
                Require(actual.MProbability == m && actual.UProbability == u
                    && Close(m.Value / u.Value, ratio.Value),
                    $"{id}:M_U_RATIO:{field}");
            }
            Require(Close(actual.LogLikelihoodRatio, Number(row, "expectedLlr")),
                $"{id}:TERM_LLR:{field}");
            expectedStates.Add(new ImplementationConferenceEvidence(field, state));
        }

        Require(Close(canonical.PriorLogOdds, Number(expected, "priorLogOdds"))
            && canonical.PriorProbability == Number(vector, "prior"),
            $"{id}:PRIOR");
        Require(Close(canonical.Contributions.Sum(static x => x.LogLikelihoodRatio),
                Number(expected, "totalLlr")),
            $"{id}:TOTAL_LLR");
        Require(canonical.Score.LogOdds == Number(expected, "logOdds")
            && canonical.Score.Posterior == Number(expected, "posterior"),
            $"{id}:ROUNDED_SCORE");

        var expectedStatus = Enum.Parse<ResolutionStatus>(Text(expected, "status"));
        var expectedReason = TextOrNull(expected, "reason");
        var expectedResolved = expectedStatus == ResolutionStatus.RESOLVIDO
            ? SingleCandidateId : (Guid?)null;
        var expectedDecision = new ImplementationConferenceDecision(
            expectedStatus, expectedResolved, SingleCandidateId, null, expectedReason);
        var collision = vector.GetProperty("collisionRisk").GetBoolean();
        var operational = ProbabilisticLinkageDecisions.ResolveRanked(
            model,
            [new CandidateScore(
                SingleCandidateId, canonical.Score.Posterior,
                canonical.Score.LogOdds, collision)],
            "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        Require(operational.Status == expectedStatus
            && operational.PessoaUuidResolvido == expectedResolved
            && operational.MelhorCandidatoUuid == SingleCandidateId
            && operational.SegundoCandidatoUuid is null
            && operational.Motivo == expectedReason,
            $"{id}:OPERATIONAL_DECISION");

        // O lado esperado vem SOMENTE do JSON versionado, inclusive score e decisão.
        var request = new ImplementationConferenceRequest(
            ModelId, model.Version, algorithm, p,
            [new ImplementationConferenceCandidate(
                SingleCandidateId, 1, expectedStates, collision,
                Number(expected, "totalLlr"),
                Number(expected, "logOdds"),
                Number(expected, "posterior"))],
            expectedDecision, tolerance);
        var independentlyChecked = IndependentImplementationConference.Evaluate(request);
        Require(independentlyChecked.Status == ImplementationConferenceStatus.CONFORME
            && independentlyChecked.SameFinalDecision
            && independentlyChecked.IndependentDecision == expectedDecision,
            $"{id}:INDEPENDENT_CONFERENCE");
    }

    private static void VerifyName(JsonElement vector)
    {
        var id = Text(vector, "id");
        var contract = Text(vector, "contract") switch
        {
            "V1" => NameComparisonContract.WholeNameJaroWinklerV1,
            "V2" => NameComparisonContract.PtBrContentTokenGuardV2,
            "V3" => NameComparisonContract.WholeNameJaroWinklerPrefixGatedV3,
            _ => throw new InvalidDataException($"GOLDEN_VECTOR_MISMATCH:{id}:NAME_CONTRACT")
        };
        var actual = IdentityComparison.CompareName(
            TextOrNull(vector, "left"), TextOrNull(vector, "right"), contract);
        Require(actual.ToString() == Text(vector, "expected"),
            $"{id}:NAME_STATE");
        if (id == "C07_PREFIX_GATED_V3")
        {
            // Jaro = (5/10 + 5/10 + 1)/3 = 2/3; V3 suprime o bônus.
            var gated = IdentityComparison.JaroWinklerPrefixGated(
                "AAAAABBBBB", "AAAAACCCCC");
            var original = IdentityComparison.JaroWinkler(
                "AAAAABBBBB", "AAAAACCCCC");
            Require(Math.Abs(gated - 2d / 3d) < 1e-12
                && Math.Abs(original - 0.8d) < 1e-12,
                $"{id}:PREFIX_GATE_ALGEBRA");
        }
    }

    private static void VerifyBirth(JsonElement vector)
    {
        var left = ReadBirth(vector, "left")!.Value;
        var right = ReadBirth(vector, "right")!.Value;
        Require(BirthDateSemanticEvidence.Classify(left, right)
            == Text(vector, "expected"),
            $"{Text(vector, "id")}:BIRTH_STATE");
    }

    private static void VerifyPolicy(
        JsonElement vector,
        ImplementationConferenceToleranceContract tolerance)
    {
        var id = Text(vector, "id");
        var p = BaselineParameters(
            false, Number(vector, "prior"), Number(vector, "threshold"));
        p[LinkageParameterCatalog.LogOddsConflictMargin] = Number(vector, "margin");
        if (vector.GetProperty("dualThreshold").GetBoolean())
            p[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        if (vector.GetProperty("floorV2").GetBoolean())
        {
            p[LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m;
            p[LinkageParameterCatalog.DualThresholdConflictFloor] =
                Number(vector, "floor");
        }
        if (vector.GetProperty("demographicGuard").GetBoolean())
            p[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
        ValidateDistributions(p, false);
        var algorithm = LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion;
        var model = LinkageModelPolicy.Create(ModelId, 6, algorithm, p);
        var scores = new List<CandidateScore>();
        var inputs = new List<ImplementationConferenceCandidate>();
        foreach (var candidate in vector.GetProperty("candidates").EnumerateArray())
        {
            var candidateId = Guid.Parse(Text(candidate, "id"));
            var name = ReadName(candidate, "name");
            var mother = ReadName(candidate, "mother");
            var leftBirth = ReadBirth(candidate, "birthLeft");
            var rightBirth = ReadBirth(candidate, "birthRight");
            var actual = FellegiSunterScoring.CalculateWithBreakdown(
                p, name, mother, null, leftBirth, rightBirth);
            var expLlr = Number(candidate, "expectedTotalLlr");
            var expLogOdds = Number(candidate, "expectedLogOdds");
            var expPosterior = Number(candidate, "expectedPosterior");
            Require(Close(actual.Contributions.Sum(static x => x.LogLikelihoodRatio), expLlr)
                && actual.Score.LogOdds == expLogOdds
                && actual.Score.Posterior == expPosterior
                && actual.Contributions[0].State == Text(candidate, "name")
                && actual.Contributions[1].State == Text(candidate, "mother")
                && actual.Contributions[2].State == BirthDateSemanticEvidence.Exact,
                $"{id}:CANDIDATE_SCORE:{candidateId}");
            var collision = candidate.GetProperty("collisionRisk").GetBoolean();
            scores.Add(new CandidateScore(
                candidateId, actual.Score.Posterior, actual.Score.LogOdds, collision));
            inputs.Add(new ImplementationConferenceCandidate(
                candidateId, inputs.Count + 1,
                [
                    new ImplementationConferenceEvidence("NOME", Text(candidate, "name")),
                    new ImplementationConferenceEvidence("NOME_MAE", Text(candidate, "mother")),
                    new ImplementationConferenceEvidence("NASCIMENTO_SEMANTICO", "EXACT")
                ],
                collision, expLlr, expLogOdds, expPosterior));
        }

        var ranked = scores
            .OrderByDescending(static x => x.LogOdds)
            .ThenBy(static x => x.PessoaUuid)
            .ToArray();
        Require(ranked.Select(static x => x.PessoaUuid)
            .SequenceEqual(inputs.Select(static x => x.CandidateId)),
            $"{id}:RANKING");
        var expected = vector.GetProperty("expected");
        var expectedDecision = new ImplementationConferenceDecision(
            Enum.Parse<ResolutionStatus>(Text(expected, "status")),
            ReadGuid(expected, "resolvedId"),
            ReadGuid(expected, "bestId"),
            ReadGuid(expected, "secondId"),
            TextOrNull(expected, "reason"));
        var operational = ProbabilisticLinkageDecisions.ResolveRanked(
            model, ranked, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        Require(operational.Status == expectedDecision.Status
            && operational.PessoaUuidResolvido == expectedDecision.ResolvedCandidateId
            && operational.MelhorCandidatoUuid == expectedDecision.BestCandidateId
            && operational.SegundoCandidatoUuid == expectedDecision.SecondCandidateId
            && operational.Motivo == expectedDecision.Reason,
            $"{id}:OPERATIONAL_POLICY");
        var request = new ImplementationConferenceRequest(
            ModelId, 6, algorithm, p, inputs, expectedDecision, tolerance);
        var report = IndependentImplementationConference.Evaluate(request);
        Require(report.Status == ImplementationConferenceStatus.CONFORME
            && report.SameFinalDecision
            && report.IndependentDecision == expectedDecision,
            $"{id}:INDEPENDENT_POLICY");
    }

    private static Dictionary<string, decimal> BaselineParameters(
        bool v8, decimal prior, decimal threshold)
    {
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = prior,
            [LinkageParameterCatalog.PriorBlockMin] = 0.000001m,
            [LinkageParameterCatalog.PriorBlockMax] = 0.25m,
            [LinkageParameterCatalog.Threshold] = threshold,
            [LinkageParameterCatalog.ConflictMargin] = 0.05m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = 0.05m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m
        };
        if (v8)
            p[LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m;

        AddDistribution(p, "NOME",
            [("EXACT", .8m, .2m), ("HIGH", .1m, .2m),
             ("MEDIUM", .05m, .2m), ("LOW", .05m, .4m)]);
        if (v8)
            AddDistribution(p, "NOME_MAE",
                [("EXACT", .5m, .1m), ("HIGH", .2m, .1m),
                 ("MEDIUM", .1m, .1m), ("LOW", .2m, .7m)]);
        else
            AddDistribution(p, "NOME_MAE",
                [("EXACT", .4m, .1m), ("HIGH", .2m, .1m),
                 ("MEDIUM", .1m, .1m), ("LOW", .2m, .5m),
                 ("MISSING", .1m, .2m)]);
        AddDistribution(p, "NASCIMENTO_SEMANTICO",
            [("EXACT", .4m, .1m), ("DAY_MONTH_SWAP", .1m, .1m),
             ("CENTURY_SHIFT", .1m, .1m), ("ONE_DIGIT_ERROR", .1m, .1m),
             ("TWO_DIGIT_ERROR", .1m, .2m),
             ("PARTIAL_COMPONENT_AGREEMENT", .1m, .2m),
             ("OTHER_DISAGREEMENT", .1m, .2m)]);
        return p;
    }

    private static void AddDistribution(
        Dictionary<string, decimal> parameters,
        string field,
        IReadOnlyList<(string State, decimal M, decimal U)> rows)
    {
        foreach (var row in rows)
        {
            parameters[$"M_{field}_{row.State}"] = row.M;
            parameters[$"U_{field}_{row.State}"] = row.U;
        }
    }

    private static void ValidateDistributions(
        IReadOnlyDictionary<string, decimal> parameters, bool v8)
    {
        foreach (var (field, states) in new[]
        {
            ("NOME", LinkageParameterCatalog.NameStates),
            ("NOME_MAE", v8
                ? LinkageParameterCatalog.NameStates
                : LinkageParameterCatalog.MotherNameStates),
            ("NASCIMENTO_SEMANTICO", BirthDateSemanticEvidence.States)
        })
        {
            var m = states.Sum(state => parameters[$"M_{field}_{state}"]);
            var u = states.Sum(state => parameters[$"U_{field}_{state}"]);
            Require(m == 1m && u == 1m
                && states.All(state => parameters[$"M_{field}_{state}"] > 0m
                    && parameters[$"U_{field}_{state}"] > 0m),
                $"INVALID_DISTRIBUTION:{field}");
        }
    }

    private static NameComparisonState? ReadName(JsonElement element, string field)
    {
        var value = TextOrNull(element, field);
        return value is null ? null : Enum.Parse<NameComparisonState>(value);
    }

    private static DateOnly? ReadBirth(JsonElement element, string field)
    {
        var value = TextOrNull(element, field);
        return value is null
            ? null
            : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static Guid? ReadGuid(JsonElement element, string field)
    {
        var value = TextOrNull(element, field);
        return value is null ? null : Guid.Parse(value);
    }

    private static decimal Number(JsonElement element, string field) =>
        element.GetProperty(field).GetDecimal();

    private static decimal? OptionalNumber(JsonElement element, string field)
    {
        var property = element.GetProperty(field);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetDecimal();
    }

    private static string Text(JsonElement element, string field) =>
        element.GetProperty(field).GetString()
            ?? throw new InvalidDataException($"GOLDEN_REQUIRED_TEXT:{field}");

    private static string? TextOrNull(JsonElement element, string field) =>
        element.GetProperty(field).ValueKind == JsonValueKind.Null
            ? null : element.GetProperty(field).GetString();

    private static bool Close(decimal actual, decimal expected) =>
        Math.Abs(actual - expected) <= MaxNumericDifference;

    private static void Require(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidDataException($"GOLDEN_VECTOR_MISMATCH:{detail}");
    }
}

internal sealed record GoldenReferenceVerification(
    string ReferenceVersion,
    string ToleranceVersion,
    string VectorsSha256,
    int ScoringVectors,
    int NameVectors,
    int BirthVectors,
    int PolicyVectors,
    string HumanReview,
    string Status);
