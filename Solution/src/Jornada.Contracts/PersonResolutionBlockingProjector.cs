namespace Jornada.Contracts;

/// <summary>
/// Projeta somente atributos transversais explicitamente elegíveis no contrato compartilhado.
/// Atributos desconhecidos/inelegíveis são ignorados; não há inferência por nome, sufixo ou formato.
/// </summary>
public static class PersonResolutionBlockingProjector
{
    public const string MethodVersion = "PERSON_RESOLUTION_BLOCKING_PROJECTOR_V1";

    public static IReadOnlyList<BlockingProjectionKey> Project(
        IEnumerable<IdentityResolutionAttributeValue>? attributes)
    {
        if (attributes is null)
            return Array.Empty<BlockingProjectionKey>();

        var keys = new HashSet<BlockingProjectionKey>();
        foreach (var item in attributes)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.AttributeCode) || item.Value is null)
                continue;
            if (!PersonResolutionContractCatalog.TryGet(item.AttributeCode, out var contract) ||
                !contract.EligibleForResolution ||
                contract.CompatibilityProfile is not null)
                continue;

            try
            {
                switch (contract.Semantic)
                {
                    case PersonResolutionSemantic.PersonName:
                        AddPersonName(keys, item.Value, contract);
                        break;
                    case PersonResolutionSemantic.Phone:
                        AddIfDeclared(
                            keys,
                            contract,
                            PersonResolutionContractCatalog.ContactPhoneCanonicalFeature,
                            ContactCanonicalization.NormalizeBrazilianPhoneV2(item.Value));
                        break;
                    case PersonResolutionSemantic.Email:
                        AddIfDeclared(
                            keys,
                            contract,
                            PersonResolutionContractCatalog.ContactEmailCanonicalFeature,
                            ContactCanonicalization.NormalizeEmailV2(item.Value));
                        break;
                }
            }
            catch (InvalidDataException)
            {
                // Valor fora do contrato não ganha chave aproximada.
            }
        }

        return keys
            .OrderBy(static key => key.Feature, StringComparer.Ordinal)
            .ThenBy(static key => key.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddPersonName(
        ISet<BlockingProjectionKey> keys,
        string value,
        PersonResolutionAttributeContract contract)
    {
        var basic = PersonNameBasicNormalization.Project(value);
        if (basic is not null)
        {
            AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameUpperFeature, basic.Upper);
            AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameUpperNoDiacriticsFeature, basic.UpperNoDiacritics);
            AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameWithoutParticlesFeature, basic.WithoutPortugueseParticles);
        }

        var normalized = IdentityComparison.NormalizeText(value);
        if (normalized is not null)
        {
            AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameNormalizedFeature, normalized);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameFirstFeature, tokens[0]);
                AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameLastFeature, tokens[^1]);
                for (var index = 1; index < tokens.Length; index++)
                    AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNameSurnamesFeature, tokens[index]);
            }
        }

        var phonetic = MetaphoneBr.Encode(value);
        if (phonetic is not null)
            AddIfDeclared(keys, contract, PersonResolutionContractCatalog.SocialNamePhoneticFeature, phonetic);
    }

    private static void AddIfDeclared(
        ISet<BlockingProjectionKey> keys,
        PersonResolutionAttributeContract contract,
        string feature,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !contract.BlockingFeatures.Contains(feature, StringComparer.Ordinal))
            return;
        keys.Add(new BlockingProjectionKey(feature, value));
    }
}
