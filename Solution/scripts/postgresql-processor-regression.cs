using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;
using Npgsql;

// Isolated integration regression. The real Worker E2E runs first; these cases
// exercise the same repository with validated, canonically hashed model variants.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
var connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
    ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION não configurada.");
var database = new PostgreSqlOperationalAdapter(connectionString);
var leases = new PostgreSqlProcessorLeaseRepositoryAdapter(new PostgreSqlProcessorLeaseStore(database));
var quality = new RegistryQualityEngine(new IRegistryQualityEvaluator[]
{
    new PositiveGrantedValueRegistryQcEvaluator("AA01", 1)
});
var repository = new PostgreSqlProcessorRepository(database, leases, quality);
var ingestion = new PostgreSqlIngestionMetadataStore(database);
var fixture = Path.Combine(root, "tests", "fixtures", "ingestao", "AA01_v2");
var packagePath = Directory.GetFiles(Path.Combine(root, ".local", "postgresql-processor-runtime", "packages"), "*.zip").Single();
var packageBytes = await File.ReadAllBytesAsync(packagePath);
var packageSha = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
var objectKey = $"sha256/{packageSha[..2]}/{packageSha[2..4]}/{packageSha}.zip";
var sourcePersonJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "pessoas.jsonl")))!;
var sourceFactJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "registros.jsonl")))!;
var reference = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.FromHours(-3));
var runTag = Guid.NewGuid().ToString("N")[..12];
var cases = new List<string>();
var sequence = 0;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string SyntheticCpf()
{
    while (true)
    {
        var digits = new int[11];
        for (var i = 0; i < 9; i++) digits[i] = RandomNumberGenerator.GetInt32(10);
        for (var length = 9; length <= 10; length++)
        {
            var sum = 0;
            for (var i = 0; i < length; i++) sum += digits[i] * (length + 1 - i);
            var remainder = (sum * 10) % 11;
            digits[length] = remainder == 10 ? 0 : remainder;
        }
        var cpf = string.Concat(digits.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        if (CpfRules.NormalizeAndValidate(cpf) is not null) return cpf;
    }
}

static string PersonHash(JsonNode node) => CanonicalJsonHash.ComputePerson(JsonSerializer.SerializeToElement(node));
static string FactHash(JsonNode node) => CanonicalJsonHash.Compute(JsonSerializer.SerializeToElement(node), "codigoRegistroOrigem", "operacao");

async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
{
    await using var connection = await database.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
    return await command.ExecuteScalarAsync();
}

async Task<long> CountAsync(string sql, params (string Name, object? Value)[] parameters) =>
    Convert.ToInt64(await ScalarAsync(sql, parameters), CultureInfo.InvariantCulture);

async Task<string> TextAsync(string sql, params (string Name, object? Value)[] parameters) =>
    Convert.ToString(await ScalarAsync(sql, parameters), CultureInfo.InvariantCulture) ?? string.Empty;

async Task<(ReservedBatch Batch, ParsedPackage Package)> PrepareAsync()
{
    var key = $"pg-regression-{runTag}-{++sequence}";
    var receipt = await ingestion.RegisterAsync(new PostgreSqlIngestionMetadataRequest(
        "SEHAB", "SEHAB", 2, "BENEFICIO", "AA01", 1, key, packageSha, packageBytes.Length,
        reference, Path.GetFileName(packagePath), objectKey, packageSha, packageBytes.Length));
    Check(!receipt.RetransmissaoIdempotente, "A fixture deve criar uma Entrega independente.");
    var batch = await repository.ReserveNextAsync($"pg-regression-{runTag}", TimeSpan.FromMinutes(5), CancellationToken.None)
        ?? throw new InvalidOperationException("Entrega de regressão não foi reservada.");
    Check(batch.EntregaId == receipt.EntregaId, "A reserva não pertence à Entrega de regressão esperada.");
    var parser = new IngestionPackageParser(root, new ProcessorOptions());
    using var stream = new MemoryStream(packageBytes, writable: false);
    var parsed = parser.Parse(batch, stream);
    Check(parsed.Pessoas.Count == 1 && parsed.Registros.Count == 1, "Fixture base inesperada.");
    return (batch, parsed);
}

ParsedPerson Person(ParsedPackage template, string source, string cpf, int version = 1, bool conflicting = false)
{
    var node = sourcePersonJson.DeepClone();
    node["codigoPessoaOrigem"] = source;
    node["cpf"] = cpf;
    node["sourceTransactionId"] = $"{source}-TX-{version}";
    node["nomeCompleto"] = conflicting ? "Pessoa Sintética Divergente" : "Pessoa Sintética de Regressão";
    if (conflicting) node["dataNascimento"] = "1991-02-03";
    var attrs = node["atributosTransversais"]!.AsArray();
    attrs[0]!["sourceRecordId"] = $"{source}-END-{version}";
    if (version > 1) attrs[0]!["valor"] = $"CEP=01001000|COD_LOG=000001|NUMERO={100 + version}|COMPLEMENTO=";
    var original = template.Pessoas.Single();
    var originalAttribute = original.Atributos.Single();
    return original with
    {
        CodigoPessoaOrigem = source,
        Cpf = cpf,
        SourceTransactionId = $"{source}-TX-{version}",
        NomeCompleto = conflicting ? "Pessoa Sintética Divergente" : "Pessoa Sintética de Regressão",
        DataNascimento = conflicting ? new DateOnly(1991, 2, 3) : original.DataNascimento,
        ConteudoHash = PersonHash(node),
        Atributos = new[] { originalAttribute with
        {
            SourceRecordId = $"{source}-END-{version}",
            Valor = version > 1 ? $"CEP=01001000|COD_LOG=000001|NUMERO={100 + version}|COMPLEMENTO=" : originalAttribute.Valor
        }}
    };
}

ParsedFact Fact(ParsedPackage template, string source, string code, RegistroOperacao operation, decimal value)
{
    var node = sourceFactJson.DeepClone();
    node["codigoPessoaOrigem"] = source;
    node["codigoRegistroOrigem"] = code;
    node["operacao"] = operation.ToString();
    node["valorConcedido"] = value;
    return template.Registros.Single() with
    {
        CodigoPessoaOrigem = source,
        CodigoRegistroOrigem = code,
        Operacao = operation,
        ValorConcedido = value,
        ConteudoHash = FactHash(node)
    };
}

static ParsedPackage Package(ParsedPackage template, ParsedPerson person, params ParsedFact[] facts) =>
    template with { Pessoas = new[] { person }, Registros = facts };

async Task RunAsync(ReservedBatch batch, ParsedPackage package)
{
    await repository.PersistValidatedAsync(batch, package, CancellationToken.None);
    Check(await TextAsync("SELECT status FROM ingestao.entrega WHERE entrega_id=@id", ("@id", batch.EntregaId)) == "PROCESSADA",
        "A Entrega não foi finalizada após commit.");
}

async Task ExpectInvalidAsync(ReservedBatch batch, ParsedPackage package, string reason)
{
    try
    {
        await repository.PersistValidatedAsync(batch, package, CancellationToken.None);
        throw new InvalidOperationException("A operação inválida foi aceita: " + reason);
    }
    catch (InvalidDataException) { }
    Check(await TextAsync("SELECT status FROM ingestao.lote WHERE lote_id=@id", ("@id", batch.LoteId)) == "VALIDANDO",
        "Falha não reverteu o status PROCESSANDO do lote.");
    Check(await CountAsync("SELECT COUNT(*) FROM ingestao.item_processado WHERE lote_id=@id", ("@id", batch.LoteId)) == 0,
        "Falha deixou itens processados parcialmente publicados.");
    cases.Add(reason);
}

async Task CheckVersionsAsync(string recordCode, string expectedSilver, string expectedMaterialized, int expectedCurrent)
{
    const string origin = "SELECT registro_origem_id FROM silver.registro_origem WHERE codigo_registro_origem=@code";
    var originId = await ScalarAsync(origin, ("@code", recordCode));
    Check(originId is not null, "Origem factual ausente: " + recordCode);
    foreach (var (table, expected) in new[]
    {
        ("silver.registro_observacao", expectedSilver),
        ("gold.beneficio_concedido", expectedMaterialized),
        ("serving.registro_integrado", expectedMaterialized)
    })
    {
        var status = table.StartsWith("silver.", StringComparison.Ordinal) ? "operacao" : "status_analitico";
        var actual = await TextAsync($"SELECT string_agg(versao_interna::text || ':' || {status}, ',' ORDER BY versao_interna) FROM {table} WHERE registro_origem_id=@id", ("@id", originId));
        Check(actual == expected, $"Versionamento {table}: esperado [{expected}], obtido [{actual}].");
    }
    foreach (var table in new[] { "gold.beneficio_concedido", "serving.registro_integrado" })
    {
        Check(await CountAsync($"SELECT COUNT(*) FROM {table} WHERE registro_origem_id=@id AND status_analitico='VIGENTE'", ("@id", originId)) == expectedCurrent,
            "Quantidade de versões correntes incoerente em " + table);
        Check(await CountAsync($"SELECT COUNT(*) FROM {table} WHERE registro_origem_id=@id AND status_analitico<>'VIGENTE' AND vigencia_versao_fim IS NULL", ("@id", originId)) == 0,
            "Versão histórica sem término em " + table);
    }
}

var source = "PG-REG-" + runTag;
var record = "PG-AA-" + runTag;
var cpf = SyntheticCpf();
var first = await PrepareAsync();
var personV1 = Person(first.Package, source, cpf);
var factV1 = Fact(first.Package, source, record, RegistroOperacao.INCLUSAO, 600m);
await RunAsync(first.Batch, Package(first.Package, personV1, factV1));
await CheckVersionsAsync(record, "1:INCLUSAO", "1:VIGENTE", 1);
Check(await CountAsync("SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND estado='ATIVO' AND vigencia_fim IS NULL", ("@cpf", cpf)) == 1,
    "CPF determinístico não gerou mapa único.");
cases.Add("initial-inclusion");

var retransmit = await PrepareAsync();
await RunAsync(retransmit.Batch, Package(retransmit.Package, personV1, factV1));
await CheckVersionsAsync(record, "1:INCLUSAO", "1:VIGENTE", 1);
Check(await CountAsync("SELECT COUNT(*) FROM ingestao.item_processado WHERE lote_id=@id AND resultado='RETRANSMITIDO'", ("@id", retransmit.Batch.LoteId)) == 2,
    "Retransmissão idempotente não preservou ambas as versões.");
cases.Add("idempotent-retransmission");

var altered = await PrepareAsync();
var personV2 = Person(altered.Package, source, cpf, version: 2);
var factV2 = Fact(altered.Package, source, record, RegistroOperacao.ALTERACAO, 700m);
await RunAsync(altered.Batch, Package(altered.Package, personV2, factV2));
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO", "1:HISTORICO,2:VIGENTE", 1);
Check(await CountAsync("SELECT COUNT(*) FROM silver.pessoa_observacao WHERE codigo_pessoa_origem=@code", ("@code", source)) == 2,
    "Alteração cadastral não criou a segunda versão Silver.");
cases.Add("person-and-fact-versioning");

var repeatChange = await PrepareAsync();
await RunAsync(repeatChange.Batch, Package(repeatChange.Package, personV2, factV2));
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO", "1:HISTORICO,2:VIGENTE", 1);
cases.Add("unchanged-update-retransmission");

var invalidInclusion = await PrepareAsync();
await ExpectInvalidAsync(invalidInclusion.Batch, Package(invalidInclusion.Package, personV2,
    Fact(invalidInclusion.Package, source, record, RegistroOperacao.INCLUSAO, 750m)), "duplicate-inclusion-rollback");
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO", "1:HISTORICO,2:VIGENTE", 1);

var rectified = await PrepareAsync();
var factV3 = Fact(rectified.Package, source, record, RegistroOperacao.RETIFICACAO, 800m);
await RunAsync(rectified.Batch, Package(rectified.Package, personV2, factV3));
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO,3:RETIFICACAO", "1:HISTORICO,2:RETIFICADO,3:VIGENTE", 1);
cases.Add("rectification");

var excluded = await PrepareAsync();
var factV4 = factV3 with { Operacao = RegistroOperacao.EXCLUSAO };
await RunAsync(excluded.Batch, Package(excluded.Package, personV2, factV4));
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO,3:RETIFICACAO,4:EXCLUSAO", "1:HISTORICO,2:RETIFICADO,3:EXCLUIDO", 0);
cases.Add("exclusion-with-history");

var invalidReopen = await PrepareAsync();
await ExpectInvalidAsync(invalidReopen.Batch, Package(invalidReopen.Package, personV2,
    Fact(invalidReopen.Package, source, record, RegistroOperacao.ALTERACAO, 850m)), "excluded-record-requires-inclusion");

var reopened = await PrepareAsync();
var factV5 = Fact(reopened.Package, source, record, RegistroOperacao.INCLUSAO, 900m);
await RunAsync(reopened.Batch, Package(reopened.Package, personV2, factV5));
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO,3:RETIFICACAO,4:EXCLUSAO,5:INCLUSAO",
    "1:HISTORICO,2:RETIFICADO,3:EXCLUIDO,5:VIGENTE", 1);
Check(await CountAsync("SELECT COUNT(*) FROM ingestao.item_processado WHERE lote_id=@id AND resultado='REABERTO'", ("@id", reopened.Batch.LoteId)) == 1,
    "Reabertura não foi rastreada.");
cases.Add("reopening");

var failure = await PrepareAsync();
var failedSource = "PG-FAIL-" + runTag;
var failedCpf = SyntheticCpf();
var failedPerson = Person(failure.Package, failedSource, failedCpf) with
{
    Atributos = new[] { new ParsedTransversalAttribute("PG-UNKNOWN", "PG_UNKNOWN_ATTRIBUTE", "synthetic", "DECLARADO",
        null, null, null, null, null, null, null) }
};
await ExpectInvalidAsync(failure.Batch, Package(failure.Package, failedPerson), "late-person-persistence-rollback");
Check(await CountAsync("SELECT COUNT(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem=@code", ("@code", failedSource)) == 0,
    "Rollback deixou origem cadastral.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf", ("@cpf", failedCpf)) == 0,
    "Rollback deixou mapa CPF.");
Check(await CountAsync("SELECT COUNT(*) FROM gold.pessoa WHERE cpf=@cpf", ("@cpf", failedCpf)) == 0,
    "Rollback deixou Gold Pessoa.");

var stale = await PrepareAsync();
await using (var connection = await database.OpenAsync())
await using (var command = connection.CreateCommand())
{
    command.CommandText = "UPDATE ingestao.lote SET lease_id=@new WHERE lote_id=@id";
    foreach (var (name, value) in new[] { ("@new", (object)Guid.NewGuid()), ("@id", (object)stale.Batch.LoteId) })
    {
        var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p);
    }
    Check(await command.ExecuteNonQueryAsync() == 1, "Não foi possível simular perda do lease.");
}
try
{
    await repository.PersistValidatedAsync(stale.Batch, Package(stale.Package, personV2), CancellationToken.None);
    throw new InvalidOperationException("Lease antigo foi aceito.");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Lease perdido", StringComparison.Ordinal)) { }
Check(await CountAsync("SELECT COUNT(*) FROM ingestao.item_processado WHERE lote_id=@id", ("@id", stale.Batch.LoteId)) == 0,
    "Lease perdido deixou persistência parcial.");
cases.Add("stale-lease-rollback");

var conflict = await PrepareAsync();
var conflictingSource = "PG-CONFLICT-" + runTag;
var conflictingPerson = Person(conflict.Package, conflictingSource, cpf, conflicting: true);
await RunAsync(conflict.Batch, Package(conflict.Package, conflictingPerson));
Check(await TextAsync("SELECT estado FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL", ("@cpf", cpf)) == "EM_CONFLITO",
    "CPF incompatível não suspendeu o mapa determinístico.");
Check(await CountAsync("SELECT COUNT(*) FROM gold.beneficio_concedido WHERE codigo_registro_origem=@code AND status_analitico='VIGENTE' AND pessoa_uuid IS NULL AND estado_atribuicao_identidade='CONFLITO_IDENTIDADE'", ("@code", record)) == 1,
    "Conflito não suspendeu a atribuição do benefício corrente.");
Check(await CountAsync("SELECT COUNT(*) FROM serving.registro_integrado WHERE codigo_registro_origem=@code AND status_analitico='VIGENTE' AND pessoa_uuid IS NULL AND estado_atribuicao_identidade='CONFLITO_IDENTIDADE'", ("@code", record)) == 1,
    "Conflito não propagou a suspensão ao Serving.");
Check(await CountAsync("SELECT COUNT(*) FROM gold.pessoa WHERE cpf=@cpf", ("@cpf", cpf)) == 0,
    "Gold Pessoa permaneceu canônica após conflito.");
await CheckVersionsAsync(record, "1:INCLUSAO,2:ALTERACAO,3:RETIFICACAO,4:EXCLUSAO,5:INCLUSAO",
    "1:HISTORICO,2:RETIFICADO,3:EXCLUIDO,5:VIGENTE", 1);
cases.Add("deterministic-cpf-conflict-with-factual-preservation");

var concurrentSource = "PG-RACE-" + runTag;
var concurrentRecord = "PG-RACE-AA-" + runTag;
var concurrentCpf = SyntheticCpf();
var raceBase = await PrepareAsync();
var racePerson = Person(raceBase.Package, concurrentSource, concurrentCpf);
await RunAsync(raceBase.Batch, Package(raceBase.Package, racePerson,
    Fact(raceBase.Package, concurrentSource, concurrentRecord, RegistroOperacao.INCLUSAO, 600m)));
var raceA = await PrepareAsync();
var raceB = await PrepareAsync();
async Task<Exception?> AttemptAsync(ReservedBatch batch, ParsedPackage package)
{
    try { await RunAsync(batch, package); return null; }
    catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01") { return ex; }
}
var racePackageA = Package(raceA.Package, racePerson,
    Fact(raceA.Package, concurrentSource, concurrentRecord, RegistroOperacao.ALTERACAO, 700m));
var racePackageB = Package(raceB.Package, racePerson,
    Fact(raceB.Package, concurrentSource, concurrentRecord, RegistroOperacao.ALTERACAO, 800m));
var raceResults = await Task.WhenAll(AttemptAsync(raceA.Batch, racePackageA), AttemptAsync(raceB.Batch, racePackageB));
for (var i = 0; i < raceResults.Length; i++)
{
    if (raceResults[i] is not null)
        await RunAsync(i == 0 ? raceA.Batch : raceB.Batch, i == 0 ? racePackageA : racePackageB);
}
await CheckVersionsAsync(concurrentRecord, "1:INCLUSAO,2:ALTERACAO,3:ALTERACAO", "1:HISTORICO,2:HISTORICO,3:VIGENTE", 1);
Check(await CountAsync("SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND estado='ATIVO' AND vigencia_fim IS NULL", ("@cpf", concurrentCpf)) == 1,
    "Concorrência duplicou o mapa CPF.");
cases.Add("serializable-concurrent-versioning-and-retry");

var evidence = new
{
    status = "OK",
    generatedAtUtc = DateTimeOffset.UtcNow,
    provider = "PostgreSql",
    runTag,
    cases,
    caseCount = cases.Count,
    serializationRetries = raceResults.Count(x => x is not null)
};
var evidencePath = Path.Combine(root, ".local", "postgresql-processor-regression", "evidence.json");
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("POSTGRESQL PROCESSOR REGRESSION: OK");
