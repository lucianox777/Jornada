namespace Jornada.Contracts;

/// <summary>
/// Identidade imutável da transformação física usada para gerar as chaves de blocking de Pessoa.
/// É deliberadamente separada de IdentityComparison.NormalizationVersion: normalização descreve
/// a forma textual; este contrato identifica o conjunto exato de fontes, elegibilidade, algoritmos,
/// saídas e materializações que produziram identidade.blocking_chave.
/// </summary>
public static class PersonResolutionProjectionContract
{
    public const string SchemaVersion = "PERSON_RESOLUTION_PROJECTION_V2";
    public const string FingerprintSha256 = "d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4";

    public static void ValidateSupported(string? schemaVersion, string? fingerprintSha256)
    {
        var hasSchema = !string.IsNullOrWhiteSpace(schemaVersion);
        var hasFingerprint = !string.IsNullOrWhiteSpace(fingerprintSha256);
        if (hasSchema != hasFingerprint)
            throw new InvalidOperationException("Identidade da projeção deve informar versão e fingerprint em conjunto.");
        if (!hasSchema)
            return;

        var fingerprint = fingerprintSha256!.Trim();
        if (fingerprint.Length != 64 || fingerprint.Any(static c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Fingerprint da projeção deve ser SHA-256 hexadecimal de 64 caracteres.");
        if (!string.Equals(schemaVersion!.Trim(), SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(fingerprint, FingerprintSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Projeção física não suportada pelo runtime: {schemaVersion}/{fingerprintSha256}.");
        }
    }
}
