using Jornada.Contracts;

namespace Jornada.Api;

/// <summary>
/// Política de borda da Jornada. A Pessoa compartilhada em âmbito municipal é comum a todas as credenciais autorizadas.
/// BENEFICIO/SERVICO continuam limitadas ao próprio recurso por scope/código, mas não por existência de fato prévio da Pessoa.
/// Restrições setoriais incidem somente na projeção retornada, por controle.restricao_projecao_jornada_versao.
/// </summary>
internal sealed class MunicipalAccessPolicyEngine : IPolicyEngine
{
    public Task<bool> IsAllowedAsync(
        AccessContext context,
        string permission,
        string? resourceCode,
        Guid? pessoaUuid,
        CancellationToken ct) =>
        Task.FromResult(BaseAllowed(context, permission, resourceCode));

    public Task<bool> ArePersonsAllowedAsync(
        AccessContext context,
        string permission,
        string? resourceCode,
        IReadOnlyCollection<Guid> pessoaUuids,
        CancellationToken ct)
    {
        if (pessoaUuids.Count == 0) return Task.FromResult(false);
        return Task.FromResult(BaseAllowed(context, permission, resourceCode));
    }

    private static bool BaseAllowed(AccessContext context, string permission, string? resourceCode)
    {
        if (!context.Scopes.Contains(permission, StringComparer.OrdinalIgnoreCase)) return false;

        var requiresResourceOwnership = context.CredentialType != AccessCredentialType.GESTOR
            || permission.StartsWith("jornada.ingestao", StringComparison.OrdinalIgnoreCase);
        if (requiresResourceOwnership && !string.IsNullOrWhiteSpace(resourceCode)
            && !context.AuthorizedResourceCodes.Contains(resourceCode, StringComparer.OrdinalIgnoreCase)) return false;

        return true;
    }
}
