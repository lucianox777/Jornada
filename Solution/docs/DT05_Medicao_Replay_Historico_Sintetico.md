# DT-05 — medição verificável da duração do REPLAY histórico sintético

**Etapa incremental:** adiciona **uma amostra medida de latência real**
do processo `Jornada.Linkage.Runner --mode REPLAY --publish false`
no E2E SQL `JornadaSyntheticDev` do GitHub Actions. Este número
é o tempo de parede do **Runner real**, após a preparação do corpus
sintético; não é duração do job completo, estimativa de CPU,
benchmark comparável entre runners nem custo/latência de **NAS de
produção**.

## Implementação

- O `Stopwatch` no `scripts/dt05-cpf-late-wave.ps1` começa
  imediatamente antes de `dotnet run --no-build` no modo
  **REPLAY histórico** e para em `finally`, sem alterar
  materialização, assinatura, run de origem, candidatos ou
  política `publish=false`.
- Após validação do universo e da assinatura semântica, o arquivo
  `Solution/.local/e2e/dt05-historical-replay-evidence.json`
  mantém os campos anteriores e recebe `measurement`,
  `measurementIterations=1`,
  `replayElapsedMilliseconds` e
  `replayRowsPerSecond`, calculado sobre os
  `replayUniverse` itens realmente observados no SQL.
  `productionNasMeasured=false` e
  `productionSlaCertified=false` são **obrigatórios**.
- O workflow separado `DT-05 Real Runner CPF Late E2E`,
  restrito ao runner GitHub-hosted com SQL sintético, executa
  `scripts/verify-dt05-replay-latency-evidence.py` **depois**
  das três ondas/CPF tardio/replay, exigindo status PASS,
  universo idêntico, assinatura semântica, nenhuma publicação,
  proveniência `GITHUB_RUN_ID/GITHUB_SHA` e medição válida.
  Uma falha do validador faz falhar o job; o upload `if:always()`
  mantém a evidência mesmo com erro, sem declarar sucesso.

## Limites e próxima avaliação

A medição representa **somente uma execução** e não suporta SLA,
custo/mês, percentis p95/p99, capacidade de rede NAS, deduplicação
em massa ou retenção institucional. Para encerrar a
[DT-05 global](DT05_Snapshots_Parquet_NAS.md), continuam exigidas
amostras repetidas/comparáveis, carga controlada, orçamento de
armazenamento, segurança da retenção/GC e decisão externa de NAS.
Esse passo apenas fornece **proveniência mensurável** e impede
tratar uma avaliação sintética como medição de PROD.

**NÃO** invocar o ensaio sobre `JornadaLocal`, IBGE original,
NODE/Compose canônico, HML/PROD, containers/volumes persistentes
ou dados reais. Reutilizar somente o SQL de runner GitHub descartável
`JornadaSyntheticDev`, com credenciais efêmeras. A
[DT-22](DT22_Reavaliacao_Governada_Resolvidos.md) é **a última**,
não recebe implementação nesta etapa.
