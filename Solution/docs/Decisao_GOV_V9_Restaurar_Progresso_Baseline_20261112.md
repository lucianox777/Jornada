# GOV-V9-RESTAURAR-PROGRESSO-BASELINE-20261112 — reversão seletiva da instrumentação física

**Autoridade:** Product Owner, 12/11/2026. **Status:** aprovada; implementação submetida a PR, integração e validação pendentes.

## Contexto e evidência
A execução manual #43159426682 (commit 32ac1845c10bfddb2a80f0be5e91fc99950588f4) concluiu 324/324 hipóteses físicas em 303,1 minutos, mas falhou posteriormente em VALIDATION por leitura de envelope >2 GiB. A execução #43228306208 (commit 762fc8550736aea533e970a97146e5860bac9439) apresentou 212/324 em 289,7 minutos e tempos individuais maiores. O diff introduziu `physicalProgressLock`, fila de até 20 conclusões, `searchClock` e ETA móvel; o bloqueio abrangia `Console.WriteLine` e `Flush`. Contenção é hipótese plausível, **não causa comprovada**; diferenças de recursos/SQL também são possíveis.

## Decisão aprovada
Restaurar **somente** o bloco de progresso físico de `V9NestedCalibrationCoordinator.cs` ao conteúdo exato do commit 32ac1845c10bfddb2a80f0be5e91fc99950588f4: contador `Interlocked.Increment`, ETA cumulativo, mensagem `[V9 PROGRESS]` por hipótese e `Flush` originais; remover `physicalProgressLock`, `recentCompletions`, `searchClock`, ETA móvel e campos adicionais de preparo/total. Preservar os cronômetros e a lógica científica já presentes no baseline.

## Limites, impactos e precedência
Preservar a retirada dos arquivos por hipótese do CLI, leitura em fluxo do envelope de VALIDATION (>2 GiB), anúncio inicial e correções de diretório/limpeza do workflow, 324 hipóteses TF4, `AUTO_0`, SQL, snapshots, critérios científicos, timeout de 305 minutos, TEST reservado e execução manual pelo PO. Esta decisão **substitui** a instrumentação de ETA móvel proposta na PR #1458; essa PR não deve ser integrada sobre esta reversão sem nova decisão do PO. Não afirma equivalência de desempenho antes de build/testes e nova execução física. Não iniciar workflow automaticamente.

## Rastreabilidade e aceite
Baseline #43159426682 / 32ac1845c10bfddb2a80f0be5e91fc99950588f4; regressão #43228306208 / 762fc8550736aea533e970a97146e5860bac9439; arquivo `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`; branch `fix/v9-restore-physical-progress-baseline-20261112`; índice de decisões vigentes; política GOV-DOC-01. Aceite: revisão do diff, compilação/testes e campanha manual comparável, com diagnóstico de recursos se lentidão persistir.
