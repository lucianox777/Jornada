# GOV-V9-DESATIVAR-PROGRESSO-CONSOLE-20261112 — decisão aprovada pelo PO

## Contexto e evidências
Na campanha TF4 100k de 324 hipóteses, a execução #43228306208 (SHA `762fc8550736aea533e970a97146e5860bac9439`) falhou na etapa física após 222/324 hipóteses em 301,5 minutos, com limite de 305 minutos. A execução #43283688181 (SHA `d919fe0784d666c0e4097c0827369fcf0b766831`) mostrou tempos individuais iniciais de ~107–112 segundos, ainda acima do baseline de 75,8 segundos para a primeira hipótese da campanha #43159426682. Não há comprovação de causalidade entre logging e regressão; o custo de I/O síncrono é hipótese de investigação.

## Decisão e justificativa
Por orientação explícita do PO em 12/11/2026, desativar a emissão de `[V9 PROGRESS]` no console durante a calibração física 100k, para eliminar a escrita síncrona (`lock(Console.Out)`, `Console.WriteLine`, `Console.Out.Flush`) do caminho de execução **desta campanha**. Configurar `V9_PHYSICAL_PROGRESS_LOG='0'` no workflow `.github/workflows/v9-v3-four-name-100k.yml`. O código C# mantém o mecanismo opt-in para outros contextos, mas o callback retorna antes de imprimir quando desativado.

## Persistência e limites
A decisão anterior GOV-V9-LOG-HIPOTESE-CONSOLE-20261112 retirou os arquivos persistidos **por hipótese** (`hypothesis-progress.jsonl` e `hypotheses/hypothesis-*.json`). Logo, a desativação do console **não cria nem preserva um log detalhado persistente por hipótese**. Permanecem as evidências agregadas existentes (por exemplo, `batch-progress.jsonl`, `memory-progress.jsonl`, `startup-progress.jsonl`, resultados e diagnósticos quando produzidos) e o monitor externo Python `v9-100k-physical-memory-evidence.py`, que coleta métricas agregadas de memória/SQL/OOM em `physical-memory-evidence.json`. Esse monitor não registra a conclusão de cada hipótese. Não confundir evidência agregada com checkpoint recuperável por hipótese.

## Impactos e não objetivos
- Sem alteração de TF4, 324 hipóteses, governador `AUTO_0`, SQL, FDR, TRAIN, VALIDATION, TEST, critérios científicos, timeout ou política de segurança.
- O console deixa de fornecer andamento por hipótese e ETA; monitoramento da etapa e métricas de memória permanecem.
- Execuções já iniciadas conservam seu SHA e configuração; uma nova execução manual é necessária.
- O ganho de desempenho só pode ser comprovado por nova campanha física comparada às execuções anteriores.

## Precedência e rastreabilidade
Esta decisão **substitui apenas a exigência de imprimir `[V9 PROGRESS]` no console para a campanha 100k** de GOV-V9-PROGRESS-324-20261111, GOV-V9-LOG-HIPOTESE-CONSOLE-20261112 e GOV-V9-RESTAURAR-PROGRESSO-BASELINE-20261112. Preserva a proibição de arquivos detalhados por hipótese e as demais correções da PR #1460. Implementação: branch `fix/v9-disable-console-progress-20261112`, workflow `v9-v3-four-name-100k.yml`; comparação de referência: runs #43159426682, #43228306208 e #43283688181. Governança: GOV-DOC-01.
