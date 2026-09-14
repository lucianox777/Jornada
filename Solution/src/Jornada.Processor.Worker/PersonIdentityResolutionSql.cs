using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed record PersonSqlIdentityDecision(
    InternalIdentityResolution Resolution,
    bool InconsistenciaRetroalimentacao,
    Guid? UuidJornadaRecebido,
    Guid? UuidJornadaCanonico,
    bool UuidJornadaRedirecionado);

internal static class PersonIdentityResolutionSql
{
    public static async Task<PersonSqlIdentityDecision> ResolveAsync(
        SqlConnection connection,
        SqlTransaction tx,
        ParsedPerson person,
        long gestorId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(person);

        var identifiers = person.Identificadores ?? Array.Empty<ParsedPersonIdentifier>();
        var cpfIdentifiers = identifiers
            .Where(i => string.Equals(i.Tipo, "CPF", StringComparison.Ordinal))
            .Select(i => i.ValorNormalizado)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (cpfIdentifiers.Length > 1)
            throw new InvalidDataException("Observação contém mais de um CPF normalizado.");

        var cpfRaw = cpfIdentifiers.SingleOrDefault() ?? person.Cpf;
        string? cpf = null;
        if (!string.IsNullOrWhiteSpace(cpfRaw))
        {
            cpf = CpfRules.NormalizeAndValidate(cpfRaw);
            if (cpf is null)
            {
                return new PersonSqlIdentityDecision(
                    new InternalIdentityResolution(
                        ResolutionStatus.CONFLITO,
                        null,
                        ResolutionMethod.CPF_DETERMINISTICO,
                        Motivo: CpfRules.StructurallyInvalidReason),
                    false,
                    null,
                    null,
                    false);
            }
        }

        InternalIdentityResolution? cpfResolution = null;
        if (cpf is not null)
        {
            cpfResolution = await SqlIdentityMapRepository.ResolveOrCreateByCpfAsync(
                connection,
                tx,
                cpf,
                new IdentityCore(person.NomeCompleto, person.DataNascimento, person.NomeMae),
                gestorId,
                person.CodigoPessoaOrigem,
                ct);

            if (cpfResolution.Status != ResolutionStatus.RESOLVIDO || !cpfResolution.PessoaUuid.HasValue)
                throw new InvalidOperationException("CPF válido não retornou a âncora determinística esperada.");
        }

        var uuidIdentifiers = identifiers
            .Where(i => string.Equals(i.Tipo, "UUID_JORNADA", StringComparison.Ordinal))
            .Select(i => i.ValorNormalizado)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (uuidIdentifiers.Length > 1)
            throw new InvalidDataException("Observação contém mais de um UUID Jornada distinto.");

        JornadaUuidLookupResult? jornada = null;
        Guid? jornadaRequested = null;
        if (uuidIdentifiers.Length == 1)
        {
            if (!Guid.TryParse(uuidIdentifiers[0], out var requested) || requested == Guid.Empty)
                throw new InvalidDataException("UUID Jornada inválido após normalização.");
            jornadaRequested = requested;
            jornada = await JornadaUuidLookup.ResolveAsync(connection, tx, requested, ct)
                ?? throw new InvalidDataException("UUID Jornada não existe ou não possui referência autoritativa resolvida.");
        }

        var candidates = new List<ResolvedIdentifierCandidate>();
        if (cpfResolution?.PessoaUuid is Guid cpfUuid)
        {
            candidates.Add(new ResolvedIdentifierCandidate(
                "CPF",
                cpfUuid,
                cpfUuid,
                ElegivelDeterministico: true,
                PrioridadeExterna: 100,
                RetroalimentacaoJornada: false));
        }
        if (jornada is not null)
        {
            candidates.Add(new ResolvedIdentifierCandidate(
                "UUID_JORNADA",
                jornada.RequestedUuid,
                jornada.CanonicalUuid,
                ElegivelDeterministico: false,
                PrioridadeExterna: null,
                RetroalimentacaoJornada: true));
        }

        var decision = PersonIdentifierResolutionPolicy.Decide(candidates);
        if (cpfResolution is not null)
        {
            // A política nunca tem autoridade para substituir a âncora CPF. Mesmo em caso de
            // retroalimentação incompatível, a resolução determinística permanece a do CPF.
            if (decision.PessoaUuid.HasValue && decision.PessoaUuid.Value != cpfResolution.PessoaUuid)
                throw new InvalidOperationException("Política de identificadores tentou substituir a âncora CPF.");

            return new PersonSqlIdentityDecision(
                cpfResolution,
                decision.InconsistenciaRetroalimentacao,
                jornadaRequested,
                jornada?.CanonicalUuid,
                jornada?.Redirected ?? false);
        }

        if (jornada is not null && decision.PessoaUuid.HasValue)
        {
            return new PersonSqlIdentityDecision(
                new InternalIdentityResolution(
                    ResolutionStatus.RESOLVIDO,
                    decision.PessoaUuid,
                    ResolutionMethod.CORRECAO_GOVERNADA,
                    Motivo: jornada.Redirected ? "UUID_JORNADA_REDIRECIONADO" : "UUID_JORNADA_CONTINUIDADE"),
                decision.InconsistenciaRetroalimentacao,
                jornadaRequested,
                jornada.CanonicalUuid,
                jornada.Redirected);
        }

        return new PersonSqlIdentityDecision(
            new InternalIdentityResolution(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                ResolutionMethod.PENDENTE_PROBABILISTICO,
                Motivo: "AGUARDA_LINKAGE_SOB_DEMANDA"),
            false,
            null,
            null,
            false);
    }
}
