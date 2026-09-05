using System.Text.Json;

namespace Jornada.Contracts;

public enum ResolutionStatus { RESOLVIDO, NAO_RESOLVIDO, CONFLITO }
public enum ResolutionMethod { CPF_DETERMINISTICO, PENDENTE_PROBABILISTICO, LINKAGE_PROBABILISTICO, CORRECAO_GOVERNADA }
public enum IntegrationNature { BENEFICIO, SERVICO }
public enum RegistroOperacao { INCLUSAO, ALTERACAO, RETIFICACAO, EXCLUSAO }
public enum PossibilityResult { COMPATIVEL, NAO_COMPATIVEL, NAO_AVALIAVEL }
public enum AccessCredentialType { GESTOR, BENEFICIO, SERVICO }

public sealed record IdentityResolutionRequest(
    string? Cpf,
    string? NomeCompleto = null,
    DateOnly? DataNascimento = null,
    string? NomeMae = null,
    string? CpfAusenteMotivo = null);

/// <summary>Resposta pública da resolução. Score, candidatos, T_LINKAGE e versão do modelo não são expostos.</summary>
public sealed record IdentityResolutionResponse(
    ResolutionStatus Status,
    Guid? PessoaUuid,
    ResolutionMethod? MetodoResolucao,
    string? Motivo = null);


public sealed record IdentityConflictDetailRequest(string Cpf);
public sealed record IdentityConflictCoreDto(
    long PessoaObservacaoId, Guid? PessoaUuidAtual, string GestorCodigo, string CodigoPessoaOrigem,
    string NomeCompleto, DateOnly DataNascimento, string NomeMae, string VinculoStatus, string? Motivo);
public sealed record IdentityConflictDetailResponse(
    string CpfEstado, string? Motivo, Guid? PessoaUuidAnteriormenteAssociada, IReadOnlyList<IdentityConflictCoreDto> Nucleos);
public sealed record IdentityCorrectionGroupRequest(
    string GrupoCodigo, Guid? PessoaUuidDestino, IReadOnlyList<long> PessoaObservacaoIds);
public sealed record IdentityCorrectionRequest(
    string Cpf, string GrupoTitularCpf, IReadOnlyList<IdentityCorrectionGroupRequest> Grupos,
    string AtoReferencia, string Justificativa);
public sealed record IdentityCorrectionResponse(
    Guid CorrecaoId, Guid PessoaUuidTitular, IReadOnlyDictionary<string, Guid> GrupoPessoaUuids, string Status);

// v3.44/v3.45 - caso governado geral, independente de CPF.
public sealed record IdentityGovernedCaseOpenRequest(
    string Motivo, IReadOnlyList<long> PessoaObservacaoIds, string AtoReferencia, string Justificativa);
public sealed record IdentityGovernedCaseOpenResponse(Guid CasoId, string Status);
public sealed record IdentityGovernedCaseApplyRequest(
    IReadOnlyList<IdentityCorrectionGroupRequest> Grupos);
public sealed record IdentityGovernedCaseApplyResponse(Guid CasoId, string Status);

// Retorno ativo de divergências ao Gestor finalístico; não contém CPF nem conteúdo do fato.
public sealed record IdentityDivergenceDto(
    long DivergenciaId, string Tipo, string Motivo, long? PessoaObservacaoId, long? RegistroObservacaoId,
    string? CodigoPessoaOrigem, Guid? CorrelationId, DateTimeOffset AbertaEm);
public sealed record IdentityDivergenceDispositionRequest(string Status, string Desfecho, string? Observacao = null);

/// <summary>Credencial apresentada transitoriamente pela borda HTTP. AccessKey nunca é persistida nem logada.</summary>
public sealed record PresentedAccessCredential(
    AccessCredentialType Type,
    string PublicCode,
    string AccessKey);

/// <summary>Contexto institucional já autenticado. Para BENEFICIO/SERVICO, GestorCodigo é derivado do Tipo cadastrado.</summary>
public sealed record AccessContext(
    Guid CredentialId,
    AccessCredentialType CredentialType,
    string PublicCode,
    string GestorCodigo,
    string? TipoCodigo,
    IReadOnlyCollection<string> Scopes,
    IReadOnlyCollection<string> AuthorizedResourceCodes);

/// <summary>
/// Manifesto obrigatório dentro do ZIP de ingestão. O envelope é único: manifest.json + pessoas.jsonl + registros.jsonl.
/// Toda Entrega declara codigoSistemaOrigem. O contexto factual (Natureza/CodigoTipo/TipoVersao) é opcional quando registros.jsonl está vazio
/// e obrigatório quando houver Benefícios Concedidos ou Serviços Prestados. A finalística nunca envia número de versão do fato.
/// </summary>
public sealed record IngestionPackageManifest(
    int FormatoVersao,
    int PessoaSchemaVersao,
    string CodigoSistemaOrigem,
    IntegrationNature? Natureza,
    string? CodigoTipo,
    int? TipoVersao,
    DateTimeOffset DataReferencia);

public sealed record IngestionReceipt(
    Guid EntregaId,
    string Status,
    string PayloadSha256,
    long BytesRecebidos,
    DateTimeOffset RecebidoEm);

public sealed record IngestionStatusResponse(
    Guid EntregaId,
    string Status,
    DateTimeOffset RecebidoEm,
    DateTimeOffset UltimaAtualizacao,
    string? Erro = null);

public sealed record PersonBatchQueryRequest(IReadOnlyList<Guid> PessoaUuids);

/// <summary>Envelope estável; Dados é a projeção variável e deve validar contra o schema selecionado pela credencial.</summary>
public sealed record PersonProjectionMetadata(
    int FontesDistintas,
    string EstadoConcordancia,
    DateTimeOffset AtualizadoEm,
    Guid? PessoaUuidSolicitado = null,
    bool RedirecionadoPorFusao = false);

public sealed record PersonProjectionResponse(
    Guid PessoaUuid,
    JsonElement Dados,
    string SchemaRef,
    PersonProjectionMetadata Metadados);

public sealed record RegistroJornadaDto(
    string RegistroId,
    Guid PessoaUuid,
    string Natureza,
    string Codigo,
    string Nome,
    string Gestor,
    DateTimeOffset DataReferencia,
    DateTimeOffset OcorridoEm,
    string? Situacao);

/// <summary>Projeção especializada de Benefício Concedido. A Natureza de catálogo continua BENEFICIO.</summary>
public sealed record BeneficioConcedidoPessoaDto(
    long BeneficioConcedidoId,
    Guid PessoaUuid,
    string Gestor,
    string CodigoBeneficio,
    string NomeBeneficio,
    int VersaoTipo,
    int VersaoInternaRegistro,
    string Operacao,
    string StatusAnalitico,
    string CodigoRegistroOrigem,
    string TipoMedida,
    DateOnly? DataInicioConcessao,
    DateOnly? DataFimConcessao,
    DateOnly? DataEventoConcessao,
    string SituacaoVigencia,
    DateOnly? SituacaoVigenciaDesde,
    string? MotivoEncerramento,
    decimal? ValorConcedido,
    decimal? Quantidade,
    string? UnidadeMedida,
    DateTimeOffset DataReferencia,
    string? QcResultado,
    bool QcEspecificoImplementado);

/// <summary>Projeção especializada de Serviço Prestado. A Natureza de catálogo continua SERVICO.</summary>
public sealed record ServicoPrestadoPessoaDto(
    long ServicoPrestadoId,
    Guid PessoaUuid,
    string Gestor,
    string CodigoServico,
    string NomeServico,
    int VersaoTipo,
    int VersaoInternaRegistro,
    string Operacao,
    string StatusAnalitico,
    string CodigoRegistroOrigem,
    DateTimeOffset DataHoraServico,
    string? UnidadeServico,
    string? Situacao,
    DateTimeOffset DataReferencia);

public sealed record PossibilidadeCompativelDto(
    Guid PessoaUuid,
    string Natureza,
    string Codigo,
    string Nome,
    string Gestor,
    string RegraVersao,
    DateTimeOffset AvaliadoEm,
    DateTimeOffset? ValidadeAte,
    string Motivo,
    bool SujeitoAnaliseGestor = true);
