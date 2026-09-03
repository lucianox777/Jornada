using Jornada.Contracts;

namespace Jornada.Processor.Worker;

/// <summary>Compatibilidade interna: a regra canônica agora vive em Jornada.Contracts.CpfRules.</summary>
public static class CpfValidator
{
    public static string? NormalizeAndValidate(string? value) => CpfRules.NormalizeAndValidate(value);
}
