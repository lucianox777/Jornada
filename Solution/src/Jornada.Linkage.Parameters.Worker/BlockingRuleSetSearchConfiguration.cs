using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Traduz configuração operacional explícita para os limites técnicos já suportados
/// pela busca bounded de blocking. A ausência de configuração preserva os defaults
/// históricos do algoritmo; nenhum valor metodológico novo é inferido aqui.
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
            MinimumTrueMatchRecall: configuration.GetValue($"{Prefix}:MinimumTrueMatchRecall", 0.95d));

        options.Validate();
        return options;
    }
}
