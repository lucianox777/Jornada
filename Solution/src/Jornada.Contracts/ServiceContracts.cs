namespace Jornada.Contracts;

public interface IAccessContextResolver
{
    /// <summary>Valida código + chave e deriva o Gestor/Tipo. A chave nunca deve ser persistida ou logada.</summary>
    Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct);
}

public interface IPolicyEngine
{
    /// <summary>Valida scope e propriedade do recurso. Nenhuma credencial autorizada exige vínculo prévio com a Pessoa.</summary>
    Task<bool> IsAllowedAsync(
        AccessContext context,
        string permission,
        string? resourceCode,
        Guid? pessoaUuid,
        CancellationToken ct);

    /// <summary>Versão em lote para evitar N consultas de autorização em POST /pessoas/consulta.</summary>
    Task<bool> ArePersonsAllowedAsync(
        AccessContext context,
        string permission,
        string? resourceCode,
        IReadOnlyCollection<Guid> pessoaUuids,
        CancellationToken ct);
}

public interface IContractResolver
{
    /// <summary>Resolve o pessoa.schema.json autorizado para a credencial e versão vigente.</summary>
    Task<string> ResolvePersonSchemaAsync(AccessContext context, CancellationToken ct);
}

public interface IIdentityResolutionService
{
    Task<IdentityResolutionResponse> ResolveAsync(AccessContext context, IdentityResolutionRequest request, CancellationToken ct);
}


public interface IIdentityCorrectionService
{
    Task<IdentityConflictDetailResponse?> GetConflictAsync(AccessContext context, IdentityConflictDetailRequest request, CancellationToken ct);
    Task<IdentityCorrectionResponse> ApplyAsync(AccessContext context, IdentityCorrectionRequest request, Guid? correlationId, CancellationToken ct);
    Task<IdentityGovernedCaseOpenResponse> OpenCaseAsync(AccessContext context, IdentityGovernedCaseOpenRequest request, Guid? correlationId, CancellationToken ct);
    Task<IdentityGovernedCaseApplyResponse> ApplyCaseAsync(AccessContext context, Guid caseId, IdentityGovernedCaseApplyRequest request, Guid? correlationId, CancellationToken ct);
    Task<IReadOnlyList<IdentityDivergenceDto>> ListDivergencesAsync(AccessContext context, int limit, CancellationToken ct);
    Task ResolveDivergenceAsync(AccessContext context, long divergenceId, IdentityDivergenceDispositionRequest request, Guid? correlationId, CancellationToken ct);
}

public interface IIngestionService
{
    Task<IngestionReceipt> ReceivePackageAsync(
        AccessContext context,
        string idempotencyKey,
        IngestionPackageManifest manifest,
        string packageFileName,
        Stream packageStream,
        long packageBytes,
        string payloadSha256,
        CancellationToken ct);
    Task<IngestionStatusResponse?> GetStatusAsync(AccessContext context, Guid entregaId, CancellationToken ct);
}

public interface IPersonProjectionService
{
    Task<PersonProjectionResponse?> GetAsync(AccessContext context, Guid pessoaUuid, CancellationToken ct);
    Task<IReadOnlyList<PersonProjectionResponse>> GetManyAsync(AccessContext context, IReadOnlyList<Guid> pessoaUuids, CancellationToken ct);
}

public interface IRegistrosQueryService
{
    /// <summary>Histórico comum da Jornada: somente atributos semanticamente compartilhados por Benefícios Concedidos e Serviços Prestados.</summary>
    Task<IReadOnlyList<RegistroJornadaDto>> GetRegistrosAsync(
        AccessContext context,
        Guid pessoaUuid,
        string? natureza,
        string? codigo,
        DateOnly? desde,
        DateOnly? ate,
        CancellationToken ct);

    /// <summary>Detalhes próprios de Benefícios Concedidos na Fase 1.</summary>
    Task<IReadOnlyList<BeneficioConcedidoPessoaDto>> GetBeneficiosConcedidosAsync(
        AccessContext context, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate, CancellationToken ct);

    /// <summary>Detalhes próprios de Serviços Prestados na Fase 1. Não existe entidade técnica genérica Atendimento.</summary>
    Task<IReadOnlyList<ServicoPrestadoPessoaDto>> GetServicosPrestadosAsync(
        AccessContext context, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate, CancellationToken ct);
}

public interface IPossibilidadesQueryService
{
    Task<IReadOnlyList<PossibilidadeCompativelDto>> GetCompativeisAsync(
        AccessContext context,
        Guid pessoaUuid,
        string? natureza,
        string? codigo,
        CancellationToken ct);
}

public interface IPossibilityEvaluator
{
    string Natureza { get; }
    string Codigo { get; }
    int Versao { get; }
    Task<PossibilityEvaluation> EvaluateAsync(Guid pessoaUuid, CancellationToken ct);
}

public sealed record PossibilityEvaluation(PossibilityResult Resultado, string Motivo, DateTimeOffset AvaliadoEm, DateTimeOffset? ValidadeAte = null);
