# GOV-V9-DIAGNOSTICO-SQL-REPLAY-100K-ISOLADO-20261116

**Autoridade:** Product Owner, 16/11/2026: “Executar” o ensaio independente para identificar onde o processamento físico SQL/blocking/replay C# perde desempenho. **Política:** GOV-DOC-01. **Estado:** APROVAÇÃO DA INVESTIGAÇÃO; IMPLEMENTAÇÃO E ENSAIO FÍSICO PENDENTES. Esta nota documenta a autorização sem declarar prova causal, novo código testado ou desempenho recuperado.

## Contexto e evidências imutáveis

| Campanha | Git SHA | Corpus | SQL/blocking/fit/replays, duração composta | P95 médio dos 324 replays |
| --- | --- | --- | ---: | ---: |
| 15/11/2026, run [#43735943823](https://github.com/lucianox777/Jornada/actions/runs/43735943823) | `7de4f3d` | C# local, clean seed 42 | 16.914.097 ms (4h41m54s) | 6.870,3 ms |
| 16/11/2026, run [#43806078187](https://github.com/lucianox777/Jornada/actions/runs/43806078187) | `7dcd2c7` | ZIP persistido em REF | 20.704.691 ms (5h45m05s) | 8.352,4 ms |
| 16/11/2026, run [#43908715448](https://github.com/lucianox777/Jornada/actions/runs/43908715448) | `a47263d` | C# local restaurado | 17.936.036 ms (4h58m56s) | 7.637,8 ms |

Nos três recibos, corpus SHA `6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1`, plano, 324 hipóteses, e resultados de TP/FP/FN, recall/FDR e SHA de fit coincidem. A terceira campanha recuperou 46m09s contra a segunda e ainda ficou 17m02s atrás da primeira. Em 16/11, TRAIN, AUDIT, VALIDATION e closeout da terceira campanha passaram; TEST ficou SKIPPED, `certified=false`, `activated=false`, sem publicação. Seleção da VALIDATION: `24f119ace8340d212dd567674b66da9b89d8ba8f28776cfc1a632d470ead9208`.

**Interpretação limitada:** substituir a fonte do corpus por si só não demonstra causalidade; as definições de `#v9_cal_obs`, `#v9_cal_sig`, `#v9_cal_pairs` e respectivos índices permaneceram idênticas. Não há plano SQL real histórico para comprovar troca de Index Seek/Scan ou algoritmo de JOIN. O intervalo agregado atual não identifica o tempo exclusivo de SQL versus C#.

## Decisão diagnóstica aprovada — escopo restrito

Preparar e executar **somente em ambiente separado**, com workflow próprio, banco efêmero `JornadaHarness`, SQL Server de imagem/digest pinados e corpus sintético 100k clean seed 42 validado por SHA. Usar uma configuração física representativa do plano TF4 congelado e um único modelo fixado, sem novo ajuste M/U e sem abrir TEST. Não tocar no workflow científico de 324 hipóteses em curso, nas fontes de produção, na `master` científica ou na coorte TEST one-use.

Instrumentar separadamente (sem logs a cada replay): projeção/assinaturas C#, criação das tabelas temporárias e índices, `SqlBulkCopy`, auto-JOIN+`SELECT DISTINCT`, criação do `IX_v9_cal_pairs`, leitura de pares, scoring FS C#, agrupamento/Resolve. Coletar contadores de CPU/leitura SQL, plano **real** de execução com operadores/índices e spills, sem armazenar XML bruto; P95 diagnóstico de pelo menos 20 repetições por modo. Comparar 1 versus 3 workers com ordem A-B-A, registrando hashes de pares e decisões para impedir alteração de semântica.

O P95 diagnóstico **não** substitui o P95 científico do TRAIN ou os gates do TEST. Comparações entre runners em dias distintos continuam sujeitas a warmup, planos, recursos de VM, estatísticas de `tempdb` e interferência não controlada. Falha ou divergência fecha o ensaio, sem ativar modelo, publicar dados, alterar índices em produção, relaxar segurança ou modificar parâmetros TF4/81, 324 hipóteses, 20 replays, FDR/CP ou seleção.

## Implementação, critérios de aceite e precedência

Estado em 16/11: **não executado o novo ensaio independente**; não há plano SQL real, divisão dos tempos por fase ou prova causal. Preparação de código/workflow e execução física requerem PR separada com testes, recibo do GitHub Actions e validação por SHA; atualizar este documento e ambos os índices na integração. Sem esse recibo, não declarar diagnóstico realizado.

Precedência: GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116, GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114, GOV-DOC-01 e o bloqueio científico TEST da issue [#1584](https://github.com/lucianox777/Jornada/issues/1584). A autorização aqui não revoga nem supera nenhuma dessas decisões. Não inferir que o novo grafo de TEST seja chamado pelo TRAIN.
