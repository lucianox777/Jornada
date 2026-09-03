namespace Jornada.Processor.Worker;

internal sealed record RegistryQcEvaluation(string Resultado, string? RegraCodigo = null, string? Motivo = null);

internal interface IRegistryQualityEvaluator
{
    string Codigo { get; }
    int Versao { get; }
    RegistryQcEvaluation Evaluate(ParsedFact fact);
}

/// <summary>
/// Regras sintéticas de referência coerentes com o seed de Development. Não representam regra de elegibilidade
/// nem decisão de direito: apenas validam que o valor monetário informado é positivo.
/// </summary>
internal sealed class PositiveGrantedValueRegistryQcEvaluator(string codigo, int versao) : IRegistryQualityEvaluator
{
    public string Codigo { get; } = codigo;
    public int Versao { get; } = versao;

    public RegistryQcEvaluation Evaluate(ParsedFact fact) =>
        fact.ValorConcedido is > 0m
            ? new RegistryQcEvaluation("VALIDO")
            : new RegistryQcEvaluation("DIVERGENTE", "VALOR_CONCEDIDO_POSITIVO_V1", "Valor concedido nulo, zero ou negativo.");
}

internal sealed class RegistryQualityEngine(IEnumerable<IRegistryQualityEvaluator> evaluators)
{
    private readonly IReadOnlyDictionary<string, IRegistryQualityEvaluator> _evaluators = evaluators
        .ToDictionary(x => Key(x.Codigo, x.Versao), StringComparer.OrdinalIgnoreCase);

    public RegistryQcEvaluation? Evaluate(ReservedBatch batch, ParsedFact fact)
    {
        var contract = EvaluateBenefitContract(batch, fact);
        if (contract is not null) return contract;

        if (!string.Equals(batch.QcStatus, "IMPLEMENTADO", StringComparison.OrdinalIgnoreCase))
            return null;

        var key = Key(batch.CodigoTipo ?? string.Empty, batch.TipoVersao ?? 0);
        if (!_evaluators.TryGetValue(key, out var evaluator))
            throw new InvalidDataException($"QC marcado como IMPLEMENTADO, mas não existe avaliador executável para {batch.CodigoTipo}/v{batch.TipoVersao}.");

        return evaluator.Evaluate(fact);
    }

    private static RegistryQcEvaluation? EvaluateBenefitContract(ReservedBatch batch, ParsedFact fact)
    {
        if (batch.Natureza != Jornada.Contracts.IntegrationNature.BENEFICIO) return null;

        if (string.IsNullOrWhiteSpace(batch.RegimeVigencia))
            return new RegistryQcEvaluation("NAO_VERIFICAVEL", "REGIME_VIGENCIA_NAO_CONFIGURADO_V1",
                "Versão do Tipo de Benefício sem regime_vigencia configurado.");

        var janelaConfigurada = batch.DataInicioPermitidaConcessao is not null || batch.DataFimPermitidaConcessao is not null;
        if (janelaConfigurada && fact.DataInicioConcessao is null)
            return new RegistryQcEvaluation("NAO_VERIFICAVEL", "CONCESSAO_JANELA_NAO_VERIFICAVEL_V1",
                "Janela permitida de concessão configurada, mas dataInicioConcessao não foi informada.");

        if (fact.DataInicioConcessao is { } inicio)
        {
            if (batch.DataInicioPermitidaConcessao is { } limiteInicial && inicio < limiteInicial)
                return new RegistryQcEvaluation("DIVERGENTE", "CONCESSAO_FORA_JANELA_V1",
                    $"Data de início da concessão {inicio:yyyy-MM-dd} anterior ao limite permitido {limiteInicial:yyyy-MM-dd}.");
            if (batch.DataFimPermitidaConcessao is { } limiteFinal && inicio > limiteFinal)
                return new RegistryQcEvaluation("DIVERGENTE", "CONCESSAO_FORA_JANELA_V1",
                    $"Data de início da concessão {inicio:yyyy-MM-dd} posterior ao limite permitido {limiteFinal:yyyy-MM-dd}.");
        }

        if (fact.DataInicioConcessao is { } inicioVigencia && fact.DataFimConcessao is { } fimVigencia && fimVigencia < inicioVigencia)
            return new RegistryQcEvaluation("DIVERGENTE", "VIGENCIA_CONCESSAO_INVERTIDA_V1",
                "Data de fim da concessão é anterior à data de início da concessão.");

        if (string.Equals(batch.RegimeVigencia, "PRAZO_DETERMINADO", StringComparison.OrdinalIgnoreCase) && fact.DataFimConcessao is null)
            return new RegistryQcEvaluation("DIVERGENTE", "DATA_FIM_CONCESSAO_OBRIGATORIA_V1",
                "Tipo com prazo determinado exige dataFimConcessao.");

        return null;
    }

    private static string Key(string code, int version) => $"{code}:v{version}";
}
