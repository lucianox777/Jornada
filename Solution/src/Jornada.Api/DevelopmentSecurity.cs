using Jornada.Operational.Sql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Api;

internal sealed record DevelopmentCredential(
    Guid CredentialId,
    string Type,
    string PublicCode,
    string GestorCodigo,
    string? TipoCodigo,
    string AccessKey,
    string[] Scopes,
    string[] AuthorizedResourceCodes);

internal sealed record DevelopmentKeyFile(
    string Environment,
    DateTimeOffset GeneratedAt,
    DevelopmentCredential[] Credentials);

internal sealed class DevelopmentAccessContextResolver : IAccessContextResolver
{
    private static readonly JsonSerializerOptions KeyFileJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, DevelopmentCredential> _credentials;

    public DevelopmentAccessContextResolver(string keyFilePath)
    {
        if (!File.Exists(keyFilePath))
            throw new InvalidOperationException(
                $"Arquivo de chaves de desenvolvimento não encontrado: {keyFilePath}. " +
                "O pacote deve conter config/security/test-access-keys.json; não existe gerador de chaves na API/Solution.");

        var model = JsonSerializer.Deserialize<DevelopmentKeyFile>(File.ReadAllText(keyFilePath), KeyFileJsonOptions) ?? throw new InvalidOperationException("Arquivo de chaves de desenvolvimento inválido.");

        if (!string.Equals(model.Environment, "Development", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O arquivo informado não é marcado como Development.");


        _credentials = model.Credentials.ToDictionary(
            c => Key(c.Type, c.PublicCode),
            c => c,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct)
    {
        if (!_credentials.TryGetValue(Key(credential.Type.ToString(), credential.PublicCode), out var stored))
            return Task.FromResult<AccessContext?>(null);

        if (!FixedTimeEquals(stored.AccessKey, credential.AccessKey))
            return Task.FromResult<AccessContext?>(null);

        if (!Enum.TryParse<AccessCredentialType>(stored.Type, true, out var type) || type != credential.Type)
            return Task.FromResult<AccessContext?>(null);

        return Task.FromResult<AccessContext?>(new AccessContext(
            stored.CredentialId,
            type,
            stored.PublicCode,
            stored.GestorCodigo,
            stored.TipoCodigo,
            stored.Scopes,
            stored.AuthorizedResourceCodes));
    }

    private static string Key(string type, string code) => $"{type}:{code}";

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

internal sealed class FileSystemContractResolver : IContractResolver
{
    private readonly string _repositoryRoot;

    public FileSystemContractResolver(string repositoryRoot) => _repositoryRoot = repositoryRoot;

    public Task<string> ResolvePersonSchemaAsync(AccessContext context, CancellationToken ct)
    {
        string baseDir = context.CredentialType switch
        {
            AccessCredentialType.GESTOR => Path.Combine(_repositoryRoot, "config", "contracts", "gestores", context.PublicCode, "pessoa"),
            AccessCredentialType.BENEFICIO => Path.Combine(_repositoryRoot, "config", "contracts", "registros", context.PublicCode),
            AccessCredentialType.SERVICO => Path.Combine(_repositoryRoot, "config", "contracts", "registros", context.PublicCode),
            _ => throw new InvalidOperationException("Tipo de credencial não suportado.")
        };

        if (!Directory.Exists(baseDir))
            throw new FileNotFoundException($"Contrato não encontrado para {context.CredentialType}:{context.PublicCode}.");

        var versionDir = Directory.EnumerateDirectories(baseDir, "v*")
            .Select(d => new { Dir = d, Version = ParseVersion(Path.GetFileName(d)) })
            .Where(x => x.Version > 0)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault()?.Dir
            ?? throw new FileNotFoundException($"Nenhuma versão de contrato encontrada em {baseDir}.");

        var schema = Path.Combine(versionDir, "pessoa.schema.json");
        if (!File.Exists(schema)) throw new FileNotFoundException("pessoa.schema.json não encontrado.", schema);
        return Task.FromResult(schema);
    }

    private static int ParseVersion(string? name) =>
        name is { Length: > 1 } && name[0] is 'v' or 'V' && int.TryParse(name[1..], out var v) ? v : -1;
}



internal sealed class CatalogBackedContractResolver(
    string repositoryRoot,
    IOperationalSqlAdapter connections) : IContractResolver
{
    public async Task<string> ResolvePersonSchemaAsync(AccessContext context, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = context.CredentialType == AccessCredentialType.GESTOR
            ? """
                SELECT TOP(1) gpv.pessoa_schema_ref,gpv.pessoa_schema_sha256
                FROM ref.gestor g
                JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.status='ATIVA'
                WHERE g.codigo=@codigo
                ORDER BY gpv.versao DESC;
                """
            : """
                SELECT TOP(1) trv.schema_pessoa_ref,trv.schema_pessoa_sha256
                FROM ref.tipo_registro tr
                JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.status='ATIVA'
                WHERE tr.codigo=@codigo
                ORDER BY trv.versao DESC;
                """;
        command.Parameters.AddWithValue("@codigo", context.PublicCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new FileNotFoundException($"Contrato ativo não encontrado no catálogo para {context.CredentialType}:{context.PublicCode}.");
        if (reader.IsDBNull(1))
            throw new InvalidOperationException($"Contrato ativo sem SHA-256 aprovado para {context.CredentialType}:{context.PublicCode}.");

        var relative = reader.GetString(0);
        var expected = (byte[])reader.GetValue(1);
        var full = Path.GetFullPath(Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Referência de contrato escapa do repositório configurado.");
        if (!File.Exists(full)) throw new FileNotFoundException("JSON Schema aprovado não encontrado.", full);
        var actual = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(full, ct));
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidOperationException($"JSON Schema diverge do SHA-256 aprovado no catálogo: {relative}.");
        return full;
    }
}

internal static class DevelopmentSecurityPaths
{
    public static string FindRepositoryRoot(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "config", "contracts"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Não foi possível localizar a raiz da Jornada a partir do ContentRoot.");
    }
}
