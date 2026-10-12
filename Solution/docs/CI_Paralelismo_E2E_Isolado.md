# CI — Paralelização do E2E real em runners SQL isolados

**12/10/2026 — Escopo:** otimização de tempo decorrido (*wall-clock*) sem suprimir validação funcional, de dados ou de resiliência. Preserva os contratos do DT-10 de CI, os dez gates obrigatórios e os testes reais de cada suíte.

## Baseline observada

O `jornada-ci` tinha um único job `e2e` com duas aceitações independentes executadas em sequência. Em dois runs concluídos de referência, a aceitação Console ZIP HTTP→Gold consumiu cerca de 119–137 segundos e o cenário C3.2d de bootstrap SQL privado, APIs, workers, falhas e recuperação consumiu 189–235 segundos. Esses tempos se somavam no caminho mais lento, apesar de ambos usarem **bancos e containers efêmeros independentes**.

## Novo grafo

- `e2e-console` executa os testes Python DT-05, Chromium e o mesmo `jornada-runner.py --profile e2e --allow-reset e2e` da CI original, em runner GitHub efêmero. Preserva a evidência `local-e2e-evidence`, e o cleanup é limitado ao Compose deste runner.
- `e2e-private-sql` executa, **em outro runner**, o mesmo `e2e-private-sql-bootstrap-runtime.sh` e a matriz C3.2d/C3.3 de SQL/APIs/worker, crash/restart/RunOnce. Não acessa Docker do job Console; valida NuGet lockfiles da mesma execução e SDK `10.0.112`, e publica `e2e-private-sql-evidence` com `if-no-files-found: error`.
- `e2e` mantém o nome do **status check obrigatório** e só conclui com sucesso se **os dois jobs** estiverem `success`, seus artefatos forem baixáveis da mesma execução, a evidência Console existir e o `summary.json` do E2E privado indicar PASS. `always()` garante falha explícita se uma das duas suítes falhar, em vez de mascará-la com `skipped`.

O novo grafo não mexe em `unit`, `CodeQL`, `integration-sql`, `harness-smoke`, `DT10`, `deterministic-build`, `bronze-restore-drill`, `scale-harness`, nem nas exigências RC/release. `release-promotion` e `rc-evidence` continuam dependendo do gate `e2e`, agora agregador. O classificador conservador e os testes contra remoção de gates continuam ativos.

## Aceite/medição

Só integrar se cada novo job e o gate `e2e` passarem na HEAD exata, com evidência de ambas as suítes. O teste de contrato `test_ci_required_gates.py` exige o script privado, o runner Console, o double-success e o download dos artefatos. O cache dos 11 setups SDK da CI e o reusable DT10 permanecem fixados por lockfiles.

Comparar o resultado com o baseline: metas indicativas são reduzir aproximadamente 2–3 minutos da parte sequencial, mas **não** declarar benefício medido enquanto não houver execução integrada equivalente; diferenças de fila, provisionamento SQL, caches e carga dos runners podem alterar o resultado. Um runner adicional poderá elevar runner-minutes, embora reduza o tempo decorrido.

**Isolamento obrigatório:** bancos `JornadaE2E` descartáveis em dois runners independentes. Não tocar `JornadaLocal`, SQL original, referência IBGE, NODE1/NODE2, NAS, volumes reais, HML/PROD. O watchdog horário (#930) e o perfil (#931) permanecem separados.
