using Jornada.Contracts;

namespace Jornada.Processor.Worker;

/// <summary>
/// Roteia a resolução de identidade na ingestão normal:
///  - CPF válido: resolução determinística pela âncora CPF -> UUID; inconsistência entre
///    núcleos pode sinalizar o identificador globalmente, mas não suspende essa atribuição;
///  - CPF informado porém estruturalmente inválido: CONFLITO_IDENTIDADE, sem UUID;
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
                    Motivo: CpfRules.StructurallyInvalidReason);
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
