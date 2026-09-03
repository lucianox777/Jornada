using Jornada.Contracts;

namespace Jornada.Processor.Worker;

internal static class ConfidentialShelterAddressPolicy
{
    internal const string AttributeCode = "ENDERECO_CASA_ABRIGO_SIGILOSA";

    internal static void ValidateSource(ReservedBatch batch, string attributeCode)
    {
        if (!string.Equals(attributeCode, AttributeCode, StringComparison.OrdinalIgnoreCase))
            return;

        if (batch.Natureza != IntegrationNature.SERVICO
            || batch.TipoRegistroId is null
            || !batch.OriginaEnderecoCasaAbrigoSigilosa)
        {
            throw new InvalidDataException(
                $"pessoas.jsonl: {AttributeCode} só pode ser declarado em Entrega de Tipo de Serviço explicitamente cadastrado como casa-abrigo-sigilosa.");
        }
    }
}
