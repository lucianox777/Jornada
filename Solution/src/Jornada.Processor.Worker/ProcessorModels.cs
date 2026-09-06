using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Bronze.Storage;
using Jornada.Ingestion;

namespace Jornada.Processor.Worker;

public sealed record ProcessorOptions
{
    public int PollingMilliseconds { get; init; } = 1000;
    public int MaxPessoasPorEntrega { get; init; } = 10_000;
    public int MaxRegistrosPorEntrega { get; init; } = 10_000;
    public int LeaseDurationSeconds { get; init; } = 120;
    public int HeartbeatSeconds { get; init; } = 30;
    public int RecoveryScanSeconds { get; init; } = 30;
    public int MaxProcessingAttempts { get; init; } = 5;
    public int RetryBaseSeconds { get; init; } = 10;
    public int RetryMaxSeconds { get; init; } = 300;
}

internal sealed record ProcessorRuntimeIdentity(string WorkerId);

internal sealed record ReservedBatch(
    Guid LoteId,
    Guid EntregaId,
    Guid LeaseId,
    string LeaseOwner,
    int AttemptNumber,
    string GestorCodigo,
    long GestorId,
    long SistemaOrigemId,
    string CodigoSistemaOrigem,
    long GestorPessoaVersaoId,
    IntegrationNature? Natureza,
    long? TipoRegistroId,
    long? TipoRegistroVersaoId,
    string? CodigoTipo,
    int? TipoVersao,
    int PessoaSchemaVersao,
    DateTimeOffset DataReferencia,
    string PayloadSha256,
    string NomeArquivo,
    string ObjetoChave,
    long PayloadBytes,
    string PessoaSchemaRef,
    byte[] PessoaSchemaSha256,
    string? RegistroSchemaRef,
    byte[]? RegistroSchemaSha256,
    string? QcStatus,
    bool OriginaEnderecoCasaAbrigoSigilosa,
    DateOnly? DataInicioPermitidaConcessao,
    DateOnly? DataFimPermitidaConcessao,
    string? RegimeVigencia);

internal sealed record ParsedPackage(
    IngestionPackageManifest Manifest,
    IReadOnlyList<ParsedPerson> Pessoas,
    IReadOnlyList<ParsedFact> Registros);

internal sealed record ParsedPerson(
    string CodigoPessoaOrigem,
    string ConteudoHash,
    string? SourceTransactionId,
    string? Cpf,
    string? CpfAusenteMotivo,
    string NomeCompleto,
    DateOnly DataNascimento,
    string NomeMae,
    IReadOnlyList<ParsedTransversalAttribute> Atributos,
    IReadOnlyList<ParsedDocumentVerification> ConferenciasDocumentais);

internal sealed record ParsedDocumentVerification(
    string CampoCodigo,
    string EvidenciaTipo,
    string? ReferenciaEvidencia,
    DateTimeOffset VerificadoEm);

internal sealed record ParsedTransversalAttribute(
    string? SourceRecordId,
    string AtributoCodigo,
    string Valor,
    string StatusEvidencia,
    string? EvidenciaTipo,
    DateTimeOffset? ReferenciaEvidencia,
    DateTimeOffset? VerificadoEm,
    DateTimeOffset? AtualizadoEmOrigem,
    TerritorialReferenceNature? NaturezaReferenciaTerritorial,
    GeographicResolutionStatus? SituacaoGeografia,
    ReferenceGeography? Geografia);

internal sealed record ParsedFact(
    string CodigoPessoaOrigem,
    string CodigoRegistroOrigem,
    RegistroOperacao Operacao,
    string ConteudoHash,
    DateOnly? DataInicioConcessao,
    DateOnly? DataFimConcessao,
    DateOnly? DataEventoConcessao,
    DateTimeOffset? DataHoraServico,
    string? UnidadeServico,
    string? Situacao,
    string? SituacaoVigencia,
    DateOnly? SituacaoVigenciaDesde,
    string? MotivoEncerramento,
    decimal? ValorConcedido,
    decimal? Quantidade,
    string? Unidade);

internal sealed class IngestionPackageParser(string repositoryRoot, ProcessorOptions options)
{
    private readonly JsonSchemaValidatorCache schemaCache = new();
    public ParsedPackage Parse(ReservedBatch batch, Stream payloadStream)
    {
        if (!payloadStream.CanRead || !payloadStream.CanSeek)
            throw new InvalidDataException("Stream da Bronze deve ser legível e seekable.");
        var manifest = IngestionPackageInspector.ParseManifestForProcessing(payloadStream, batch.PayloadBytes);
        ValidateEnvelopeAgainstDatabase(batch, manifest);

        var actualSha = IngestionPackageInspector.ComputeSha256(payloadStream);
        if (!string.Equals(actualSha, batch.PayloadSha256, StringComparison.Ordinal))
            throw new BronzeObjectIntegrityException(batch.ObjetoChave, "SHA-256 físico diverge de ingestao.entrega.");

        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, batch.GestorCodigo, batch.GestorCodigo, null, [], []);
        IngestionPackageInspector.ValidateCanonicalFileName(batch.NomeArquivo, manifest, context, actualSha);

        var personSchemaPath = ResolveContractPath(batch.PessoaSchemaRef);
        var personValidator = schemaCache.Get(personSchemaPath, batch.PessoaSchemaSha256);
        JsonSchemaSubsetValidator? factValidator = batch.RegistroSchemaRef is null
            ? null
            : schemaCache.Get(ResolveContractPath(batch.RegistroSchemaRef),
                batch.RegistroSchemaSha256 ?? throw new InvalidDataException("Schema factual ativo sem SHA-256 aprovado."));

        payloadStream.Position = 0;
        using var zip = new ZipArchive(payloadStream, ZipArchiveMode.Read, leaveOpen: true);
        var budget = new DecompressedByteBudget(IngestionPackageInspector.MaxUncompressedBytes);
        ConsumeEntryForBudget(zip.GetEntry("manifest.json")!, budget);
        var pessoas = ParsePeople(batch, zip.GetEntry("pessoas.jsonl")!, personValidator, budget);
        var factEntry = zip.GetEntry("registros.jsonl")!;
        IReadOnlyList<ParsedFact> registros = Array.Empty<ParsedFact>();
        if (factEntry.Length > 0)
        {
            if (batch.Natureza is null || factValidator is null)
                throw new InvalidDataException("registros.jsonl contém fatos, mas a Entrega não possui contexto factual/schema resolvido.");
            registros = ParseFacts(factEntry, factValidator, batch.Natureza.Value, "registros.jsonl", budget);
            ValidateFactPersonReferences(pessoas, registros);
        }
        return new ParsedPackage(manifest, pessoas, registros);
    }

    private IReadOnlyList<ParsedPerson> ParsePeople(
        ReservedBatch batch,
        ZipArchiveEntry entry,
        JsonSchemaSubsetValidator validator,
        DecompressedByteBudget budget)
    {
        var result = new List<ParsedPerson>();
        var sourceCodes = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StreamReader(new DecompressedLimitStream(entry.Open(), budget));
        string? line;
        long lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException($"pessoas.jsonl: linha {lineNumber}: linha vazia não é permitida.");
            if (result.Count >= options.MaxPessoasPorEntrega)
                throw new InvalidDataException($"pessoas.jsonl excede o limite de {options.MaxPessoasPorEntrega} registros por Entrega.");

            var json = validator.ParseAndValidate(line, "pessoas.jsonl", lineNumber);
            var cpf = OptionalString(json, "cpf");
            var sourceCode = OptionalString(json, "codigoPessoaOrigem");
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                if (string.IsNullOrWhiteSpace(cpf))
                    throw new InvalidDataException("pessoas.jsonl: codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.");
                sourceCode = cpf;
            }
            if (!sourceCodes.Add(sourceCode))
                throw new InvalidDataException($"pessoas.jsonl: codigoPessoaOrigem duplicado: {sourceCode}.");

            var attributes = new List<ParsedTransversalAttribute>();
            if (json.TryGetProperty("atributosTransversais", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
            {
                foreach (var attr in attrs.EnumerateArray())
                {
                    var attributeCode = RequiredString(attr, "atributoCodigo");
                    ConfidentialShelterAddressPolicy.ValidateSource(batch, attributeCode);
                    TerritorialReferenceNature? referenceNature = null;
                    var referenceNatureRaw = OptionalString(attr, "naturezaReferenciaTerritorial");
                    if (string.Equals(attributeCode, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(referenceNatureRaw) || !Enum.TryParse<TerritorialReferenceNature>(referenceNatureRaw, true, out var parsedNature))
                            throw new InvalidDataException("pessoas.jsonl: REFERENCIA_TERRITORIAL exige naturezaReferenciaTerritorial válida.");
                        referenceNature = parsedNature;
                    }
                    else if (!string.IsNullOrWhiteSpace(referenceNatureRaw))
                    {
                        throw new InvalidDataException("pessoas.jsonl: naturezaReferenciaTerritorial só é permitida no atributo REFERENCIA_TERRITORIAL.");
                    }

                    var isTerritorialAttribute =
                        string.Equals(attributeCode, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attributeCode, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase);

                    GeographicResolutionStatus? geographyStatus = null;
                    var geographyStatusRaw = OptionalString(attr, "situacaoGeografia");
                    if (isTerritorialAttribute)
                    {
                        if (string.IsNullOrWhiteSpace(geographyStatusRaw)
                            || !Enum.TryParse<GeographicResolutionStatus>(geographyStatusRaw, ignoreCase: false, out var parsedStatus))
                            throw new InvalidDataException(
                                $"pessoas.jsonl: {attributeCode} exige situacaoGeografia explícita (RESOLVIDA, FORA_MUNICIPIO, SEM_ENDERECO_APTO ou NAO_RESOLVIDA_ORIGEM).");
                        geographyStatus = parsedStatus;
                    }
                    else if (!string.IsNullOrWhiteSpace(geographyStatusRaw))
                    {
                        throw new InvalidDataException("pessoas.jsonl: situacaoGeografia só é permitida em ENDERECO_RESIDENCIAL ou REFERENCIA_TERRITORIAL.");
                    }

                    ReferenceGeography? geography = null;
                    if (attr.TryGetProperty("geografia", out var geo) && geo.ValueKind == JsonValueKind.Object)
                    {
                        if (!isTerritorialAttribute)
                            throw new InvalidDataException("pessoas.jsonl: geografia só é permitida em ENDERECO_RESIDENCIAL ou REFERENCIA_TERRITORIAL.");
                        geography = new ReferenceGeography(
                            RequiredString(geo, "distritoCodigo"),
                            RequiredString(geo, "distritoNome"),
                            RequiredString(geo, "subprefeituraCodigo"),
                            RequiredString(geo, "subprefeituraNome"),
                            RequiredString(geo, "referenciaMalha"),
                            ReferenceGeographyOrigin.ORIGEM,
                            null);
                    }

                    if (geographyStatus == GeographicResolutionStatus.RESOLVIDA && geography is null)
                        throw new InvalidDataException($"pessoas.jsonl: {attributeCode} com situacaoGeografia=RESOLVIDA exige geografia completa e referenciaMalha.");
                    if (geographyStatus is not null && geographyStatus != GeographicResolutionStatus.RESOLVIDA && geography is not null)
                        throw new InvalidDataException($"pessoas.jsonl: {attributeCode} só pode enviar geografia quando situacaoGeografia=RESOLVIDA.");

                    var value = OptionalString(attr, "valor");
                    if (string.Equals(attributeCode, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (referenceNature == TerritorialReferenceNature.REFERENCIA_TERRITORIAL_DECLARADA && geography is null)
                            throw new InvalidDataException("pessoas.jsonl: REFERENCIA_TERRITORIAL_DECLARADA exige Distrito/Subprefeitura declarados pela fonte.");
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            if (geography is null)
                                throw new InvalidDataException("pessoas.jsonl: REFERENCIA_TERRITORIAL exige valor/endereço ou geografia declarada.");
                            value = $"DISTRITO={geography.DistritoCodigo}|SUBPREFEITURA={geography.SubprefeituraCodigo}";
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidDataException($"pessoas.jsonl: atributo {attributeCode} exige valor.");
                    }

                    attributes.Add(new ParsedTransversalAttribute(
                        OptionalString(attr, "sourceRecordId"),
                        attributeCode,
                        value!,
                        RequiredString(attr, "statusEvidencia"),
                        OptionalString(attr, "evidenciaTipo"),
                        OptionalDateTimeOffset(attr, "referenciaEvidencia"),
                        OptionalDateTimeOffset(attr, "verificadoEm"),
                        OptionalDateTimeOffset(attr, "atualizadoEmOrigem"),
                        referenceNature,
                        geographyStatus,
                        geography));
                }
            }

            var verifications = new List<ParsedDocumentVerification>();
            if (json.TryGetProperty("conferenciasDocumentais", out var confs) && confs.ValueKind == JsonValueKind.Array)
            {
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var conf in confs.EnumerateArray())
                {
                    var field = RequiredString(conf, "campoCodigo");
                    if (!fields.Add(field))
                        throw new InvalidDataException($"pessoas.jsonl: campo conferido duplicado: {field}.");
                    verifications.Add(new ParsedDocumentVerification(
                        field,
                        RequiredString(conf, "evidenciaTipo"),
                        OptionalString(conf, "referenciaEvidencia"),
                        RequiredDateTimeOffset(conf, "verificadoEm")));
                }
            }

            result.Add(new ParsedPerson(
                sourceCode,
                CanonicalJsonHash.ComputePerson(json),
                OptionalString(json, "sourceTransactionId"),
                cpf,
                OptionalString(json, "cpfAusenteMotivo"),
                RequiredString(json, "nomeCompleto"),
                RequiredDate(json, "dataNascimento"),
                RequiredString(json, "nomeMae"),
                attributes,
                verifications));
        }

        if (result.Count == 0)
            throw new InvalidDataException("pessoas.jsonl não contém registros.");
        return result;
    }

    private IReadOnlyList<ParsedFact> ParseFacts(
        ZipArchiveEntry entry,
        JsonSchemaSubsetValidator validator,
        IntegrationNature nature,
        string logicalFile,
        DecompressedByteBudget budget)
    {
        var result = new List<ParsedFact>();
        var recordCodes = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StreamReader(new DecompressedLimitStream(entry.Open(), budget));
        string? line;
        long lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException($"{logicalFile}: linha {lineNumber}: linha vazia não é permitida.");
            if (result.Count >= options.MaxRegistrosPorEntrega)
                throw new InvalidDataException($"{logicalFile} excede o limite de {options.MaxRegistrosPorEntrega} registros por Entrega.");

            var json = validator.ParseAndValidate(line, logicalFile, lineNumber);
            var sourceCode = RequiredString(json, "codigoPessoaOrigem");
            var recordCode = RequiredString(json, "codigoRegistroOrigem");
            if (!recordCodes.Add(recordCode))
                throw new InvalidDataException($"{logicalFile}: codigoRegistroOrigem duplicado na Entrega: {recordCode}.");
            var operationText = RequiredString(json, "operacao");
            if (!Enum.TryParse<RegistroOperacao>(operationText, ignoreCase: false, out var operation))
                throw new InvalidDataException($"{logicalFile}: operacao inválida: {operationText}.");
            var contentHash = CanonicalJsonHash.Compute(json, "codigoRegistroOrigem", "operacao");
            ParsedFact fact;
            if (nature == IntegrationNature.BENEFICIO)
            {
                fact = new ParsedFact(
                    sourceCode,
                    recordCode,
                    operation,
                    contentHash,
                    OptionalDate(json, "dataInicioConcessao"),
                    OptionalDate(json, "dataFimConcessao"),
                    OptionalDate(json, "dataEventoConcessao"),
                    null,
                    null,
                    null,
                    RequiredString(json, "situacaoVigencia"),
                    OptionalDate(json, "situacaoVigenciaDesde"),
                    OptionalString(json, "motivoEncerramento"),
                    OptionalDecimal(json, "valorConcedido"),
                    OptionalDecimal(json, "quantidade"),
                    OptionalString(json, "unidade"));
            }
            else
            {
                fact = new ParsedFact(
                    sourceCode,
                    recordCode,
                    operation,
                    contentHash,
                    null,
                    null,
                    null,
                    RequiredDateTimeOffset(json, "dataHoraServico"),
                    OptionalString(json, "unidadeServico"),
                    OptionalString(json, "situacao"),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }
            result.Add(fact);
        }
        return result;
    }

    private static void ConsumeEntryForBudget(ZipArchiveEntry entry, DecompressedByteBudget budget)
    {
        using var stream = new DecompressedLimitStream(entry.Open(), budget);
        Span<byte> buffer = stackalloc byte[16 * 1024];
        while (stream.Read(buffer) != 0) { }
    }

    private static void ValidateFactPersonReferences(IReadOnlyList<ParsedPerson> people, IReadOnlyList<ParsedFact> facts)
    {
        var known = people
            .Select(p => p.CodigoPessoaOrigem)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            if (!known.Contains(fact.CodigoPessoaOrigem))
                throw new InvalidDataException($"Registro referencia codigoPessoaOrigem ausente em pessoas.jsonl: {fact.CodigoPessoaOrigem}.");
        }
    }

    private static void ValidateEnvelopeAgainstDatabase(ReservedBatch batch, IngestionPackageManifest manifest)
    {
        if (manifest.PessoaSchemaVersao != batch.PessoaSchemaVersao
            || !string.Equals(manifest.CodigoSistemaOrigem, batch.CodigoSistemaOrigem, StringComparison.Ordinal)
            || manifest.Natureza != batch.Natureza
            || !string.Equals(manifest.CodigoTipo, batch.CodigoTipo, StringComparison.Ordinal)
            || manifest.TipoVersao != batch.TipoVersao
            || manifest.DataReferencia != batch.DataReferencia)
            throw new InvalidDataException("manifest.json diverge dos metadados persistidos da Entrega.");
    }

    private string ResolveContractPath(string reference)
    {
        var relative = reference.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException("Referência de contrato aponta para fora do repositório autorizado.");
        if (!File.Exists(full))
            throw new FileNotFoundException("Contrato referenciado pelo banco não encontrado.", full);
        return full;
    }

    private static string RequiredString(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString())
            ? v.GetString()!
            : throw new InvalidDataException($"Campo obrigatório inválido: {name}.");

    private static string? OptionalString(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateOnly RequiredDate(JsonElement json, string name) =>
        OptionalDate(json, name) ?? throw new InvalidDataException($"Campo obrigatório inválido: {name}.");

    private static DateOnly? OptionalDate(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Data inválida: {name}.");
        return date;
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement json, string name) =>
        OptionalDateTimeOffset(json, name) ?? throw new InvalidDataException($"Campo obrigatório inválido: {name}.");

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            throw new InvalidDataException($"Data/hora inválida: {name}.");
        return date;
    }

    private static decimal? OptionalDecimal(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var number))
            throw new InvalidDataException($"Número inválido: {name}.");
        return number;
    }
}
