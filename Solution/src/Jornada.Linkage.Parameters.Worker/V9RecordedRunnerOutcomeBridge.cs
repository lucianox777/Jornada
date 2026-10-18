using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// One snapshot of a final decision actually supplied by the C# Runner,
/// accompanied by its complete candidate-union attribution. This type does
/// NOT create a new decision or claim that the caller authenticated a run.
/// Independent truth is supplied separately to Bind, never to the Runner.
/// </summary>
public sealed record V9RecordedRunnerCandidateBinding(
    Guid CandidatePersonUuid,
    string CandidateFingerprintSha256,
    decimal Posterior);

public sealed record V9RecordedRunnerObservation(
    string ObservationFingerprintSha256,
    string BaseIdentityFingerprintSha256,
    IReadOnlyList<V9RecordedRunnerCandidateBinding> Candidates,
    ProbabilisticLinkageDecision FinalDecision);

public sealed record V9RecordedRunnerOutcomeBinding(
    string Version,
    Guid RunId,
    Guid ModelId,
    IReadOnlyList<IndependentRecordedOutcome> RecordedOutcomes,
    IReadOnlyList<V9RecordedBaseObservation> BaseIdentityBindings,
    bool OperationallyCertified);

/// <summary>
/// Lossless bridge from C# ProbabilisticLinkageDecision (post-guards) into
/// EvaluateRecorded and V9RecordedClusterFdrValidation. It never reconstructs
/// decisions from scores or invents a truth label. Caller still must attest
/// that outcomes originate from a real pinned HML run and that the candidate
/// frame was the complete SQL union, not truncated TOP N.
/// </summary>
public static class V9RecordedRunnerOutcomeBridge
{
    public const string Version = "V9_RUNNER_FINAL_OUTCOME_PROVENANCE_BINDING_V1";

    public static V9RecordedRunnerOutcomeBinding Bind(
        Guid recordedRunId,
        Guid recordedModelId,
        IReadOnlyList<V9RecordedRunnerObservation> runnerObservations,
        IReadOnlyList<IndependentResolutionObservation> independentlyLabeledFrame,
        bool completeSqlCandidateUniverse)
    {
        ArgumentNullException.ThrowIfNull(runnerObservations);
        ArgumentNullException.ThrowIfNull(independentlyLabeledFrame);
        if (recordedRunId == Guid.Empty || recordedModelId == Guid.Empty
            || !completeSqlCandidateUniverse || runnerObservations.Count == 0
            || runnerObservations.Count != independentlyLabeledFrame.Count)
            throw new InvalidDataException(
                "Export Runner V9 exige run/model não vazios, todas observações e universo SQL completo.");

        var labeled = new Dictionary<string, IndependentResolutionObservation>(
            StringComparer.Ordinal);
        foreach (var row in independentlyLabeledFrame)
        {
            if (row is null || !Sha(row.ObservationFingerprintSha256)
                || row.Candidates is null
                || !labeled.TryAdd(row.ObservationFingerprintSha256, row))
                throw new InvalidDataException("Frame independente inválido, incompleto ou duplicado.");
        }

        var outcome = new List<IndependentRecordedOutcome>(runnerObservations.Count);
        var origin = new List<V9RecordedBaseObservation>(runnerObservations.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in runnerObservations)
        {
            if (record is null || !Sha(record.ObservationFingerprintSha256)
                || !Sha(record.BaseIdentityFingerprintSha256)
                || record.Candidates is null || record.FinalDecision is null
                || !seen.Add(record.ObservationFingerprintSha256)
                || !labeled.TryGetValue(record.ObservationFingerprintSha256, out var independent)
                || record.FinalDecision.ModeloId != recordedModelId)
                throw new InvalidDataException("Decisão gravada sem origem, modelo ou observação independente.");

            var names = new Dictionary<Guid, V9RecordedRunnerCandidateBinding>();
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in record.Candidates)
            {
                if (c is null || c.CandidatePersonUuid == Guid.Empty
                    || !Sha(c.CandidateFingerprintSha256)
                    || c.Posterior is < 0m or > 1m
                    || !names.TryAdd(c.CandidatePersonUuid, c)
                    || !fingerprints.Add(c.CandidateFingerprintSha256))
                    throw new InvalidDataException("Universo Runner contém UUID, SHA ou posterior inválido/duplicado.");
            }
            if (independent.Candidates is null
                || independent.Candidates.Count != record.Candidates.Count
                || independent.Candidates.Any(x => x is null
                    || !Sha(x.CandidateFingerprintSha256)
                    || !fingerprints.Contains(x.CandidateFingerprintSha256)
                    || !record.Candidates.Any(c =>
                        c.CandidateFingerprintSha256 == x.CandidateFingerprintSha256
                        && c.Posterior == x.Score))
                || independent.Candidates.Select(x => x.CandidateFingerprintSha256)
                    .Distinct(StringComparer.Ordinal).Count() != independent.Candidates.Count)
                throw new InvalidDataException(
                    "Universo SQL/posterior do Runner diverge da avaliação independentemente rotulada.");

            var decision = record.FinalDecision;
            string? Fingerprint(Guid? id) =>
                id is null ? null : names.TryGetValue(id.Value, out var c)
                    ? c.CandidateFingerprintSha256
                    : throw new InvalidDataException("Runner selecionou UUID fora da união candidata.");

            var best = Fingerprint(decision.MelhorCandidatoUuid);
            var second = Fingerprint(decision.SegundoCandidatoUuid);
            var resolved = Fingerprint(decision.PessoaUuidResolvido);
            if ((record.Candidates.Count == 0) != (best is null)
                || (record.Candidates.Count < 2) != (second is null)
                || best is not null && best == second
                || (best is not null &&
                    (decision.MelhorScore != names[decision.MelhorCandidatoUuid!.Value].Posterior
                     || record.Candidates.Any(c => c.Posterior > decision.MelhorScore)))
                || (second is not null &&
                    (decision.SegundoScore != names[decision.SegundoCandidatoUuid!.Value].Posterior
                     || record.Candidates.Count(c =>
                         c.Posterior > decision.SegundoScore!.Value) > 1))
                || (second is null && decision.SegundoScore is not null)
                || (record.Candidates.Count == 0 && decision.MelhorScore != 0m))
                throw new InvalidDataException("Melhor/segundo candidato gravado não coincide com a união e seus scores.");

            var status = decision.Status switch
            {
                ResolutionStatus.RESOLVIDO => "RESOLVIDO",
                ResolutionStatus.CONFLITO => "CONFLITO",
                ResolutionStatus.NAO_RESOLVIDO => "NAO_RESOLVIDO",
                _ => throw new InvalidDataException("Status do Runner não previsto no contrato de decisão.")
            };
            if ((status == "RESOLVIDO" && (resolved is null || resolved != best))
                || (status != "RESOLVIDO" && resolved is not null))
                throw new InvalidDataException("Resultado final do Runner contradiz status e candidato.");
            if (decision.Margem is < 0m)
                throw new InvalidDataException("Margem de decisão do Runner negativa.");

            outcome.Add(new IndependentRecordedOutcome(
                record.ObservationFingerprintSha256,
                status, best, resolved, decision.Motivo));
            origin.Add(new V9RecordedBaseObservation(
                record.ObservationFingerprintSha256,
                record.BaseIdentityFingerprintSha256));
        }
        return new V9RecordedRunnerOutcomeBinding(
            Version, recordedRunId, recordedModelId,
            Array.AsReadOnly(outcome.OrderBy(x => x.ObservationFingerprintSha256,
                StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(origin.OrderBy(x => x.ObservationFingerprintSha256,
                StringComparer.Ordinal).ToArray()),
            OperationallyCertified: false);
    }

    private static bool Sha(string? value) =>
        value is { Length: 64 } && value.All(static c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')
            || (c >= 'A' && c <= 'F'));
}
