namespace Jornada.Ensaio;

public enum EnsaioEtapaTipo { Checkpoint, Calibrador, Ingestao, ValidacaoModelo, LinkageValidacao }

public sealed record EnsaioEtapa(string Codigo,string Descricao,EnsaioEtapaTipo Tipo,string? PrefixoArquivo=null,string? Gestor=null);

public static class EnsaioPlan
{
    public static IReadOnlyList<EnsaioEtapa> Padrao =>
    [
        new("00-ambiente", "Ambiente provisionado, antes das cargas do ensaio", EnsaioEtapaTipo.Checkpoint),
        new("01-ingestao-aa", "SEHAB — Auxílio Aluguel (pacotes AA*)", EnsaioEtapaTipo.Ingestao, "AA", "SEHAB"),
        new("02-pos-aa", "Estado após primeira carga", EnsaioEtapaTipo.Checkpoint),
        new("03-calibrador-aa", "Calibrador com uma única fonte real (espera-se fail-closed por ausência de pares inter-Gestor)", EnsaioEtapaTipo.Calibrador),
        new("04-pos-calibrador-aa", "Estado após tentativa de calibração com fonte única", EnsaioEtapaTipo.Checkpoint),
        new("05-ingestao-ae", "SEHAB — Auxílio Emergencial (pacotes AE*)", EnsaioEtapaTipo.Ingestao, "AE", "SEHAB"),
        new("06-pos-ae", "Estado após segunda carga do mesmo Gestor", EnsaioEtapaTipo.Checkpoint),
        new("07-calibrador-ae", "Calibrador com duas bases do mesmo Gestor (ainda se espera ausência de pares inter-Gestor)", EnsaioEtapaTipo.Calibrador),
        new("08-pos-calibrador-ae", "Estado após tentativa de calibração intra-Gestor", EnsaioEtapaTipo.Checkpoint),
        new("09-ingestao-sa", "SMADS — Serviços de Abordagem (pacotes SA*)", EnsaioEtapaTipo.Ingestao, "SA", "SMADS"),
        new("10-pos-sa", "Estado após carga de Gestor distinto", EnsaioEtapaTipo.Checkpoint),
        new("11-calibrador-sa", "Calibrador com Gestores distintos; sucesso depende de suficiência estatística, não apenas da existência de pares", EnsaioEtapaTipo.Calibrador),
        new("12-validar-modelo", "Validação explícita do RASCUNHO recém-calibrado, sem ativá-lo", EnsaioEtapaTipo.ValidacaoModelo),
        new("13-linkage-validacao", "Runner em MODEL_VALIDATION sobre a mesma versão, sem publicar vínculos correntes", EnsaioEtapaTipo.LinkageValidacao),
        new("14-final", "Estado final do ensaio", EnsaioEtapaTipo.Checkpoint),
    ];
}
