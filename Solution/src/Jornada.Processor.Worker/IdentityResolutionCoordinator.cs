using Jornada.Contracts;

namespace Jornada.Processor.Worker;

/// <summary>
/// Roteia a resolução de identidade na ingestão normal:
///  - CPF válido: resolução determinística via IDENTITY_MAP, sem score/modelo, mas
///    com trava de consistência do núcleo quando o CPF já pertence a um UUID;
///  - CPF informado porém inválido: CONFLITO/regularização;
///  - CPF ausente em hipótese admitida: fica pendente para uma execução probabilística
///    explícita, sob demanda. O pipeline normal nunca dispara score probabilístico sozinho.
/// </summary>
public sealed class IdentityResolutionCoordinator(IIdentityMapRepository identityMap)
{
    private static readonly HashSet<string> AllowedMissingCpfReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "SEM_CPF",
        "EM_REGULARIZACAO",
        "NAO_INFORMADO_ORIGEM"
    };

    public async Task<InternalIdentityResolution> ResolveAsync(
        IdentityObservation observation,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
        {
            var normalizedCpf = CpfValidator.NormalizeAndValidate(observation.Cpf);
            if (normalizedCpf is null)
            {
                return new InternalIdentityResolution(
                    ResolutionStatus.CONFLITO,
                    null,
                    ResolutionMethod.CPF_DETERMINISTICO,
                    Motivo: "CPF_INVALIDO");
            }

            return await identityMap.ResolveOrCreateByCpfAsync(normalizedCpf, observation, ct);
        }

        if (string.IsNullOrWhiteSpace(observation.CpfAusenteMotivo)
            || !AllowedMissingCpfReasons.Contains(observation.CpfAusenteMotivo))
        {
            return new InternalIdentityResolution(
                ResolutionStatus.CONFLITO,
                null,
                ResolutionMethod.PENDENTE_PROBABILISTICO,
                Motivo: "CPF_AUSENTE_SEM_MOTIVO_ADMITIDO");
        }

        return new InternalIdentityResolution(
            ResolutionStatus.NAO_RESOLVIDO,
            null,
            ResolutionMethod.PENDENTE_PROBABILISTICO,
            Motivo: "AGUARDA_LINKAGE_SOB_DEMANDA");
    }
}
