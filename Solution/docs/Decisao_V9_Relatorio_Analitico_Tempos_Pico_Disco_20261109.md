# Jornada V9 — observabilidade do relatório analítico: tempos por etapa e pico de disco

**Decisões:** DC-V9-REPORT-TIMINGS-20261109 e DC-V9-REPORT-DISK-PEAK-20261109  
**Data / autoridade:** 09/11/2026; Product Owner.  
**Política:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).  
**Estado dos dois requisitos:** **DECIDIDO / IMPLEMENTAÇÃO PENDENTE / MEDIÇÃO PENDENTE / VALIDAÇÃO PENDENTE / NÃO ATIVADO**.  
**Issues de implementação:** [#1362 — duração de cada etapa](https://github.com/lucianox777/Jornada/issues/1362) e [#1363 — pico de disco](https://github.com/lucianox777/Jornada/issues/1363).

## Contexto verificado na master e motivação

O Calibrador V9 em C# mantém um relatório analítico estatístico com duas saídas atuais: `mu-statistical-analysis.json` e `mu-statistical-analysis.md`, geradas por `V9NestedCalibrationCli.cs`. O relatório já apresenta diagnóstico de CPU/RAM e pico RSS do processo. O arquivo independente `runtime-diagnostics.json` possui duração total e divisão `physicalCalibration` / `evidenceExport`, e `memory-progress.jsonl` registra `elapsedMilliseconds` e RAM após lotes. Essas informações existentes não respondem, no próprio relatório analítico, **quanto durou cada etapa real da calibração** ou **qual foi o pico de uso de disco**.

A motivação do PO é tornar custo temporal e armazenamento legíveis no mesmo relatório estatístico, em paralelo ao pico de RAM, sem novo tipo de calibrador, sem novas opções científicas e sem modificar o que é calculado.

## DC-V9-REPORT-TIMINGS-20261109 — duração por etapa

**Decidido:** exibir no relatório estatístico o tempo gasto em **cada etapa efetivamente medida** da calibração, em formato legível e estruturado, além do tempo total.

- Aproveitar os marcos já disponíveis sem tratá-los como se fossem uma quebra completa do pipeline. Instrumentar as fases reais do fluxo e identificar estágios com ID/nome estáveis, duração em milissegundos e unidade legível, com início/fim e escopo declarado quando necessário.
- Se a decomposição permitir, explicitar preparo/validação de entradas, execução SQL e blocking, fit/avaliação/seleção e produção de artefatos. Não presumir fases ou granularidade não disponíveis; onde não houver medição confiável, indicar **NÃO MEDIDO**.
- Fazer a apresentação no `mu-statistical-analysis.md` e no `mu-statistical-analysis.json`, reconciliando-a com `runtime-diagnostics.json`. Não somar durações aninhadas ou paralelas para simular tempo de parede total. Registrar sobreposição e ambiente, SHA/corpus/seed quando disponíveis.
- A duração é **telemetria informativa**, não critério de desempate, otimização de FDR, ajuste de threshold ou seleção de modelos.

**Critério de aceite:** uma execução sintética reporta tempos das etapas instrumentadas e total verificável, com testes de consistência, casos sem medição e fases sobrepostas, sem alterar resultados, fingerprints ou hashes científicos.

## DC-V9-REPORT-DISK-PEAK-20261109 — pico de utilização de disco

**Decidido:** exibir no relatório estatístico o **pico observado de ocupação de disco atribuível à execução da calibração**, junto aos indicadores de RAM, com unidade, escopo e limites da coleta explícitos.

- Distinguir utilização de arquivos/temporários pertencentes à calibração, espaço total usado/livre no host e volume de I/O. O máximo do volume do host **não** pode ser apresentado como consumo do processo. Capturar baseline e máximo durante a execução, inclusive com remoção posterior de arquivos temporários.
- Instrumentar caminhos observáveis de saída e temporários por amostragem controlada, sem impactos indevidos na inferência. Definir precisão e cobertura de paths/volumes; áreas externas (p.ex., SQL/container) não visíveis não podem ser atribuídas à calibração sem medição identificável. Marcar `NÃO MEDIDO` onde não houver cobertura real, em vez de declarar `0`.
- Mostrar o pico em MiB/GiB no `mu-statistical-analysis.md` e campos estruturados no `mu-statistical-analysis.json`, lado a lado com RAM; manter todos os campos e a semântica anteriores de `ramCapacity`, `memory-progress.jsonl` e `runtime-diagnostics.json`.
- Falha de observabilidade deve ser diagnosticada com segurança, sem falsificar resultado; não introduzir *spill*, checkpoint em disco ou armazenamento alternativo como parte desta decisão.

**Critério de aceite:** testes que criem e removam temporários durante execução e verifiquem o máximo observado, bem como indisponibilidade, permissões e múltiplos volumes. A métrica é descritiva, não entrada para ranking/FDR/ativação.

## Impactos, precedência, limites e governança

1. As decisões acrescentam **dois requisitos de observabilidade ao relatório analítico**. Não ampliam a grade de hipóteses, não alteram plan.json científico, prior, M/U, TF, pesos, FDR, seleção, SOURCE/TEST/Gold ou política de execução física.
2. **Preservam** a decisão RAM-first/sem spill da [#1302](https://github.com/lucianox777/Jornada/issues/1302), os limites dos relatórios V9 e a decisão de não criar Pareto V4 ([#1264](https://github.com/lucianox777/Jornada/issues/1264)). Não reabrem o teste integral [#1345](https://github.com/lucianox777/Jornada/issues/1345), já encerrado: melhorias futuras terão seu próprio SHA/PR/gates.
3. Os artefatos históricos são imutáveis; novos campos de telemetria devem ser aditivos, não modificar um snapshot científico congelado. Nenhuma prova de novos campos pode ser afirmada por documentação isolada.
4. As duas issues são **autônomas** e podem ser implementadas separadamente; eventual medição compartilhada deve evitar duplicidade de fontes e conflitos de números. A precedência sobre o acervo é **complementar**, sem revogar decisões anteriores.
5. Esta PR de governança **somente registra as decisões**; implementação, medição e validação dos dois requisitos continuam **pendentes** até PRs específicas com testes/CI e evidência no SHA exato.

## Fontes

- [Código do Calibrador V9 — CLI](../src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs).
- [Estudo V9 de mistura U e diagnóstico de memória](V9_V3_U_Rare_Mixture_Experiment_20261106.md).
- [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md) e [Índice do acervo](Indice_Acervo_Documental.md).
- [Issue #1362](https://github.com/lucianox777/Jornada/issues/1362) e [issue #1363](https://github.com/lucianox777/Jornada/issues/1363).
