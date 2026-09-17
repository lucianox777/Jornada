namespace Jornada.Processor.Worker;

internal sealed record ResolvedIdentifierCandidate(
    string Tipo,
    Guid PessoaUuid,
    Guid CanonicalPessoaUuid,
    bool ElegivelDeterministico,
    int? PrioridadeExterna,
    bool RetroalimentacaoJornada);

internal sealed record PersonIdentifierResolutionDecision(
    Guid? PessoaUuid,
    string Estado,
    string? TipoIdentificadorDeterminante,
    bool InconsistenciaRetroalimentacao,
    Guid? UuidJornadaRecebido,
    string? Motivo);

internal static class PersonIdentifierResolutionPolicy
{
    public static PersonIdentifierResolutionDecision Decide(
        IReadOnlyCollection<ResolvedIdentifierCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var feedback = candidates
            .Where(c => c.RetroalimentacaoJornada)
            .ToArray();
        var external = candidates
            .Where(c => !c.RetroalimentacaoJornada && c.ElegivelDeterministico && c.PrioridadeExterna.HasValue)
            .OrderByDescending(c => c.PrioridadeExterna)
            .ToArray();

        var cpf = external.Where(c => string.Equals(c.Tipo, "CPF", StringComparison.Ordinal)).ToArray();
        if (cpf.Select(c => c.CanonicalPessoaUuid).Distinct().Skip(1).Any())
            throw new InvalidOperationException("Invariante violado: o mesmo contexto de resolução contém CPF determinístico apontando para mais de um UUID canônico.");

        var selected = cpf.FirstOrDefault() ?? external.FirstOrDefault();
        if (selected is null)
        {
            if (feedback.Length == 0)
            {
                return new PersonIdentifierResolutionDecision(
                    null,
                    "SEM_IDENTIFICADOR_DETERMINISTICO",
                    null,
                    false,
                    null,
                    "AGUARDA_LINKAGE");
            }

            var feedbackTargets = feedback.Select(c => c.CanonicalPessoaUuid).Distinct().ToArray();
            if (feedbackTargets.Length != 1)
            {
                return new PersonIdentifierResolutionDecision(
                    null,
                    "RETROALIMENTACAO_INCONSISTENTE",
                    null,
                    true,
                    feedback.First().PessoaUuid,
                    "MULTIPLOS_UUID_JORNADA_INCOMPATIVEIS");
            }

            return new PersonIdentifierResolutionDecision(
                feedbackTargets[0],
                "CONTINUIDADE_JORNADA",
                "UUID_JORNADA",
                false,
                feedback.First().PessoaUuid,
                null);
        }

        if (feedback.Length == 0)
        {
            return new PersonIdentifierResolutionDecision(
                selected.CanonicalPessoaUuid,
                "RESOLVIDA_DETERMINISTICA",
                selected.Tipo,
                false,
                null,
                null);
        }

        var incompatibleFeedback = feedback
            .FirstOrDefault(c => c.CanonicalPessoaUuid != selected.CanonicalPessoaUuid);
        if (incompatibleFeedback is not null)
        {
            return new PersonIdentifierResolutionDecision(
                selected.CanonicalPessoaUuid,
                "RESOLVIDA_DETERMINISTICA",
                selected.Tipo,
                true,
                incompatibleFeedback.PessoaUuid,
                "INCONSISTENCIA_RETROALIMENTACAO");
        }

        return new PersonIdentifierResolutionDecision(
            selected.CanonicalPessoaUuid,
            "RESOLVIDA_DETERMINISTICA",
            selected.Tipo,
            false,
            feedback.First().PessoaUuid,
            null);
    }
}
