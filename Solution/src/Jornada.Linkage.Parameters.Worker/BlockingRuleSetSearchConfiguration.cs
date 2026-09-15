using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Traduz configuração operacional explícita para os limites técnicos já suportados
/// pela busca bounded de blocking. O caminho de calibração exige por padrão suporte
/// observado de não-vínculos retidos, pois um ruleset com retenção u zero não permite
/// estimar os pesos no mesmo universo operacional que ele próprio produz.
/// </summary>
public static class BlockingRuleSetSearchConfiguration
{
    public const string Prefix = "LinkageParameters:BlockingSearch";

    public static BlockingRuleSetSearchOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new BlockingRuleSetSearchOptions(
            MaxFieldsPerPass: configuration.GetValue($"{Prefix}:MaxFieldsPerPass", 2),
            MaxPasses: configuration.GetValue($"{Prefix}:MaxPasses", 2),
            PrimitivePoolSize: configuration.GetValue($"{Prefix}:PrimitivePoolSize", 8),
            MinimumTrueMatchRecall: configuration.GetValue($"{Prefix}:MinimumTrueMatchRecall", 0.95d),
            RequireObservedNonMatchSupport: configuration.GetValue($"{Prefix}:RequireObservedNonMatchSupport", true));

        options.Validate();
        return options;
    }
}
