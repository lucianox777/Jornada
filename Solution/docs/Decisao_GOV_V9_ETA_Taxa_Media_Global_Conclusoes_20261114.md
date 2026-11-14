# GOV-V9-ETA-TAXA-MEDIA-GLOBAL-CONCLUSOES-20261114

**Autoridade:** Product Owner, 14/11/2026 (America/Sao_Paulo), esclarecimento expresso durante análise da campanha física #43631379628: “nem consideraria a média de 10. Consideraria a média geral. Só precisa ter o início do processamento e depois ver o timestamp de cada interação. Vão ser 324 hipóteses testadas. Considerar a média de velocidade do que for sendo colocado dentro desse array.”

**Estado:** **DECIDIDO (especificação operacional)**; registro documental em PR; **não constitui nova implementação, prova física de desempenho ou certificação**. GOV-DOC-01.

## Contexto e razão da decisão

A campanha V9 V3 TF4 avalia 324 hipóteses, com grupos SQL compartilhados, trabalho paralelo e replays individuais. O PO deseja uma medida simples da **velocidade global real de conclusão** a partir dos timestamps que acompanham as linhas `[V9 PROGRESS]`, sem média móvel das últimas 10/20 observações, nem novo cronômetro, nem trabalho de diagnóstico adicionado ao hot path. Não confundir velocidade de conclusões com duração isolada do replay/hipótese ou tempo do `RunAsync` compartilhado por grupo.

A decisão histórica de ETA móvel limitada a 20 eventos ([PR #1458](https://github.com/lucianox777/Jornada/pull/1458)) **não foi integrada**; não recuperá-la. As decisões GOV-V9-PROGRESS-PREVISAO-RISCO-TIMEOUT-20261114 ([PR #1506](https://github.com/lucianox777/Jornada/pull/1506)) e GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114 ([PR #1526](https://github.com/lucianox777/Jornada/pull/1526)) já estabeleceram a fórmula global e retiraram timeouts artificiais, respectivamente.

## Regra de cálculo aprovada

1. Registrar um **único instante inicial `t0` da execução física**; distinguir esse instante da preparação do workflow e dos trabalhos preliminares não pertencentes ao escopo da taxa.
2. Para cada hipótese concluída, considerar o timestamp UTC `ti` já associado ao registro individual `[V9 PROGRESS] i/324`. A sequência de até 324 conclusões (ou seus intervalos `Δi = ti − t(i−1)`, com `t(0)=t0`) forma o histórico acumulado; rejeições controladas são contadas se também são conclusões contadas no progresso.
3. **Média global desde o início**, sem janela: `media_seg_por_conclusao = (ti − t0) / i = (Σ Δk)/i`; `taxa_conclusoes_por_min = i / minutos(ti − t0)`.
4. **ETA operacional**: `eta_min = minutos(ti − t0) / i × (324 − i)` para `i>0`, e zero para `i=324`. Antes da primeira conclusão, ETA indisponível. A média inclui todas as conclusões observadas, nunca somente as últimas 10/20.
5. O array de timestamps permite auditoria, mas **não precisa ser relido em cada progresso**: o instante inicial, timestamp mais recente e contador produzem a mesma média por identidade telescópica. A escolha da estrutura não muda o resultado. Priorizar reaproveitamento dos timestamps já registrados e não acrescentar escrita por hipótese em disco ou impressão extra.
6. O cálculo expressa **vazão observada das conclusões**, não tempo individual de execução; o SQL compartilhado e a concorrência tornam a ETA heurística. Não usá-la para abortar, selecionar finalistas, controlar timeout ou interpretar P95 científico.

**Exemplo ilustrativo:** `t0=16:00:00`, conclusão `i=10` às `16:20:00` → média global `2 min/conclusão`, 314 pendentes e `ETA=628 min`. Este valor não é uma medição da campanha.

## Confronto com a versão atualmente em execução

Em `master` SHA `814238795abccf5c8c64f61a83c871bd0374cc7b`, `V9NestedCalibrationCoordinator.ForecastPhysicalProgress` já implementa matematicamente a média **global** `elapsedMinutes / completed × (total − completed)`; não há média móvel nessa versão. `ReportPhysicalCompletion` já captura `nowUtc` e utiliza `evaluation.RequestSha256` na mesma linha. Entretanto, `physicalStepStartUtc` pode vir da variável `V9_PHYSICAL_STEP_START_UTC` exportada no início do **step** do workflow, antes do wrapper Python, do worker e da preparação de grupos: conferir a semântica exata de `t0` antes de afirmar conformidade plena com “início do processamento”.

**Consequência operacional:** evitar introduzir array e um segundo relógio no caminho crítico quando a expressão O(1) é equivalente; avaliar somente eventual ajuste do marco `t0` e da apresentação. Nenhuma mudança de código nem disparo de 100k foi autorizada ou efetuada por este registro.

## Precedência, impactos e critérios de aceite

- **Prevalece** sobre a proposta de janela móvel de 10/20 amostras ou de projetar ETA a partir da duração individual pós-SQL; **não revoga** a previsão global já integrada em #1506, e respeita a retirada do deadline artificial de #1526.
- **Preserva** o plano 324 hipóteses/81 opções TF4, método científico FDR/CP/P95, SQL, `AUTO_0`, paralelismo, snapshots, dados, hashes, isolamento TRAIN/AUDIT/VALIDATION/TEST, TEST lacrado e uso manual owner-only.
- Testar, antes de mudar código: `i=0`, `i=1`, `i=10`, `i=324`, diferenças de timestamp e equivalência array/soma/expressão O(1); verificar que a origem `t0` pertence ao processamento físico acordado, que não existe janela móvel e que o log não cria saída adicional por hipótese.
- A campanha [#43631379628](https://github.com/lucianox777/Jornada/actions/runs/43631379628) é imutável em seu SHA; esta clarificação não altera sua execução. Sem A/B de runner e carga, não atribuir lentidão a timestamps, `Stopwatch` ou console. Sem nova autorização, não cancelar/reiniciar, nem consumir TEST.

**Rastreabilidade:** `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`; `.github/workflows/v9-v3-four-name-100k.yml`; GOV-V9-PROGRESS-324-20261111; GOV-V9-PROGRESS-PREVISAO-RISCO-TIMEOUT-20261114; GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114; `Solution/docs/Indice_Decisoes_Vigentes.md`; PR deste registro. GOV-DOC-01.
