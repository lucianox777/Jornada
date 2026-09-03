using System.Security.Cryptography;
using System.Text;

namespace Jornada.Api;

/// <summary>
/// Pseudonimiza o CPF de agente declarado pelo sistema finalístico. A Jornada não autentica o agente
/// nem valida a associação entre usuário e CPF; essa responsabilidade permanece integralmente com o Gestor.
/// </summary>
internal sealed class AgentCpfPseudonymizer
{
    private readonly byte[] _key;
    private readonly byte[] _domain;
    public short KeyVersion { get; }

    public AgentCpfPseudonymizer(IConfiguration configuration, IHostEnvironment environment, string repositoryRoot)
    {
        var configuredVersion = configuration.GetValue<int?>("AgentAudit:KeyVersion") ?? 1;
        if (configuredVersion is < 1 or > short.MaxValue)
            throw new InvalidOperationException("AgentAudit:KeyVersion deve estar entre 1 e 32767.");
        KeyVersion = (short)configuredVersion;
        _domain = Encoding.ASCII.GetBytes($"JORNADA:AGENTE:v{KeyVersion}:");

        var inline = configuration["AgentAudit:HmacKeyBase64"];
        string? encoded = string.IsNullOrWhiteSpace(inline) ? null : inline.Trim();

        if (encoded is null && environment.IsDevelopment())
        {
            var configured = configuration["AgentAudit:HmacKeyFile"];
            var path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(repositoryRoot, "config", "security", "test-agent-hmac-key.txt")
                : Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
            if (File.Exists(path)) encoded = File.ReadAllText(path).Trim();
        }

        if (encoded is null)
        {
            _key = Array.Empty<byte>();
            return;
        }

        try { _key = Convert.FromBase64String(encoded); }
        catch (FormatException ex) { throw new InvalidOperationException("AgentAudit:HmacKeyBase64/HmacKeyFile não contém Base64 válido.", ex); }
        if (_key.Length < 32) throw new InvalidOperationException("A chave HMAC de auditoria do agente deve ter ao menos 256 bits.");
    }

    public byte[] ComputeHash(string normalizedCpf)
    {
        if (_key.Length == 0) throw new InvalidOperationException("Chave HMAC da auditoria de agente não configurada.");
        var cpf = Encoding.ASCII.GetBytes(normalizedCpf);
        var message = new byte[_domain.Length + cpf.Length];
        Buffer.BlockCopy(_domain, 0, message, 0, _domain.Length);
        Buffer.BlockCopy(cpf, 0, message, _domain.Length, cpf.Length);
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(message);
    }
}
