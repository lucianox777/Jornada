# GOV-V9-DIAGNOSTICO-SQL-REPLAY-FASES-ISOLADO-20261116

**Autoridade:** Product Owner, 16/11/2026: "Executar" o ensaio SQL descartável e independente proposto na investigação de regressão TF4/324. **Estado:** implementação em branch/PR para avaliação; a execução física somente se considera realizada mediante run GitHub concluído e artefato verificável. **Política:** GOV-DOC-01. **Não é aprovação de alteração científica, otimização SQL, liberação de TEST ou alteração da campanha em andamento.**

## Contexto e evidências anteriores

- Referência [TRAIN 15/11 #43735943823](https://github.com/lucianox777/Jornada/actions/runs/43735943823), SHA `7de4f3d5966ab0ccf6fb9801d748893667949d0e`, operação física aproximadamente 4h42m; referência [TRAIN 16/11 #43806078187](https://github.com/lucianox777/Jornada/actions/runs/43806078187), SHA `7dcd2c70d5348be3f9e46e7487e6165b648d9c68`, operação física aproximadamente 5h45m.
- Os hashes dos candidatos, os resultados e a ciência coincidiram nas 324 hipóteses; aproximadamente 322 tiveram P95 maior. Há regressão de throughput, não comprovação de mudança no recall.
- A rotina SQL, índices `IX_v9_cal_sig` e `IX_v9_cal_pairs`, streaming e algoritmo do TRAIN foram comparados byte a byte, sem alteração entre campanhas. Entretanto não há planos reais registrados nem latências SQL/C# isoladas; `MaterializePairsMilliseconds` existente soma indevidamente parte do BulkSignatures, não devendo ser somado como métrica disjunta.
- A campanha em curso [#43908715448](https://github.com/lucianox777/Jornada/actions/runs/43908715448) está em SHA anterior ao diagnóstico; **não modificar, cancelar, reiniciar ou aproveitar SQL/runner dessa campanha**.

## Decisão de implementação diagnóstica autorizada

1. Criar **somente** um entrypoint explícito `--v9-diagnose-sql-replay` no Worker e workflow segregado `.github/workflows/v9-sql-replay-diagnostic.yml`, com job próprio no GitHub-hosted Ubuntu 24.04 e SQL Server descartável `JornadaHarness`. Nenhum caminho `--v9-calibrate`, AUDIT, VALIDATION oficial, TEST, publicação, Gold/Serving ou migração operacional é chamado pelo diagnóstico.
2. Gerar localmente o corpus `clean, demographic-primary, 100000, seed 42`, e **exigir** SHA256 de `observacoes.csv` `6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1` da execução 15/11. Consumir plano histórico com SHA256 `82f348b7eafdd82cbcaa6055832838089d1adaf0c3be8d7fe7624d5104914555` e um único snapshot finalista pinado `24f119ace8340d212dd567674b66da9b89d8ba8f28776cfc1a632d470ead9208`, ambos oriundos dos artefatos do run #43735943823. Não consumir TEST nem produzir novo fit.
3. Reutilizar a mesma imagem SQL `2022-CU26` imutavelmente fixada pelo digest do script `v9-tf-start-disposable-sql.sh`, com credencial descartável e mascarada. O diagnóstico não executa os preparativos históricos de REF/IBGE porque o replay de VALIDATION usa as frequências TF já congeladas no fit. A verificação da imagem é contratual; não presumir equivalência de capacidade do runner.
4. Executar **20 replays de uma única hipótese finalista** em uma conexão, cronometrando geração das assinaturas, criação de tabelas/índice inicial, bulk de observações e assinaturas, `JOIN + SELECT DISTINCT + índice de pares`, leitura de arestas e scoring/agrupamento C#. Comparar com **três conexões simultâneas**, 20 repetições por conexão (60); cada amostra reconstrói o quadro SQL e executa o scorer congelado. Contagens de pares e SHA de decisões devem permanecer invariantes.
5. Fazer uma **execução adicional fora das amostras P95** de `SELECT DISTINCT` com `SET STATISTICS XML/IO/TIME ON`, recolher o plano real, operadores e índices observados, e medir separadamente `CREATE UNIQUE CLUSTERED INDEX` de pares. Ela divide as instruções SQL apenas para observabilidade e **não** se apresenta como idêntica em custo ao comando combinado da rotina científica.
6. Exportar **somente um JSON agregado sanitizado**, incluindo SHA do plano, índices observados, CPU, leituras, temporizações e throughput. Proibir publicação de corpus, snapshot, nomes, hashes de pessoas, senhas, linhas por pessoa, XML integral com parâmetros, ou resultados científicos. O artefato é **DIAGNÓSTICO NÃO CERTIFICADO**. O P95 desse ensaio mede preparação + scoring sem a composição completa de métricas da rotina oficial; não substitui o P95 científico de 324 hipóteses.
7. Executar por PR em branch próprio para obter evidência física no GitHub e preservar o master e a campanha científica durante o ensaio. Falha de CI ou ausência de plano real significa **experimento ainda não concluído**, nunca PASS presumido. Nenhuma mudança SQL/índice/perfil de paralelismo científico será promovida apenas com esses dados.

## Critérios de evidência e limites

- Provas fortes: SHA completo do corpus e plano e do fit, contagem idêntica de pares, digest estável de replays, plano real do `SELECT DISTINCT`, utilização de índice, CPU e I/O do SQL, P95 separado da materialização e do scorer, vazão comparada entre 1 e 3 conexões.
- Limites: um finalista e um conjunto VALIDATION são **amostra representativa**, não as 324 combinações; quatro grupos do calibrador são diferentes de três cópias da mesma hipótese. Um runner novo também pode variar; sem execução pareada no mesmo hardware, este experimento **localiza o custo**, mas sozinho não prova por que o run 16/11 foi 22% mais lento do que o 15/11.
- Se o plano SQL variar ou os tempos SQL dominarem, preparar hipótese posterior de índices/estatísticas com prova física. Se CPU de scoring/grouping dominar ou 3 conexões reduzirem throughput, investigar concorrência em C#. **Nenhuma otimização ou mudança de estratégia foi aprovada aqui.**
- O fechamento documental requer acrescentar PR, run, resultado físico, artefato e conclusão **após** a execução real. Até lá, permanece "preparado/aguardando execução". A implementação é diagnóstico opt-in, não uma mudança do algoritmo científico.

## Precedência e rastreabilidade

Complementa `Investigacao_V9_TF4_324_Desempenho_Rollback_20261115.md`, `Baseline_Tempo_Decorrido_V9_100k_20261112.md` e `Plano_Otimizacao_V9_Holdout_SQL_20261112.md`. Não altera GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116, o histórico REF, GOV-DOC-01, os 324 ajustes, limites do runner, IBGE congelado, AUDIT, VALIDATION independente ou os gates `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`. A conclusão da investigação não altera o estatuto do TEST 100k.


## Recibos físicos verificados — 16/11/2026, dois runners independentes

**PR de implementação:** [#1604](https://github.com/lucianox777/Jornada/pull/1604), branch `diagnostic/v9-sql-replay-phases-20261116`. **Dois ensaios físicos concluídos em SUCCESS**, sem TRAIN 324, TEST, publicação ou alteração da campanha #43908715448:

1. [run #43939364948](https://github.com/lucianox777/Jornada/actions/runs/43939364948), commit `885674c1df721cc2d84d7717c62dc8eb64d94235`, artefato `v9-sql-replay-diagnostic-43939364948-fbfebd146e8103cc743c4ddf45b4d1d3c6ab395b`, ID **14262787248**.
2. [run #43940312442](https://github.com/lucianox777/Jornada/actions/runs/43940312442), commit `713e5697f791861685c84bc23aa1e103c69332c4`, artefato `v9-sql-replay-diagnostic-43940312442-bbd7846af14062481c3942e54f6b891d5e045999`, ID **14262767977**. Entre esses commits, **nenhum arquivo C# mudou**: só workflow de aquisição do mesmo snapshot e teste de contrato; blob do executor `V9SqlReplayDiagnostic.cs` permaneceu `d8fbcd28253033cfb01630d26faec4371a83a04b`.

Ambos os recibos declararam:
- `schema=V9_SQL_REPLAY_PHASE_DIAGNOSTIC_V1`, `evidence_only=true`, `scientific_p95_certified=false`, `test_opened=false`, `publication=false`;
- 100k gerado localmente e **corpus SHA** `6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1`; **plano SHA** `82f348b7eafdd82cbcaa6055832838089d1adaf0c3be8d7fe7624d5104914555`; **snapshot SHA** `24f119ace8340d212dd567674b66da9b89d8ba8f28776cfc1a632d470ead9208`;
- mesmo digest da imagem SQL Server 2022-CU26, 34.448 observações VALIDATION, 55.055 pares em cada replay, hash canônico de decisões `1257288fdff26288f6acaed96be01774c27e8296a361829ff24124ca7db8e387`, preservado nos **80 replays de cada run** (20 isolados + 60 com 3 conexões).

| Medida observada | #43939364948 (runner A) | #43940312442 (runner B) |
| --- | ---: | ---: |
| P95 completo do replay diagnóstico (1 conexão) | 6.189,116 ms | 4.396,277 ms |
| P95 do comando JOIN/DISTINCT/índice (1 conexão) | 376,208 ms | 294,867 ms |
| P95 de replay C# + serialização SHA diagnóstica (1 conexão) | 3.885,003 ms | 2.672,435 ms |
| Tempo médio de geração das assinaturas (1 conexão) | 1.330,524 ms | 900,225 ms |
| Tempo médio de SqlBulkCopy assinaturas (1 conexão) | 438,740 ms | 356,907 ms |
| P95 completo (3 conexões) | 11.310,043 ms | 7.647,130 ms |
| P95 comando JOIN/DISTINCT/índice (3 conexões) | 1.100,912 ms | 728,388 ms |
| P95 replay C# + serialização SHA (3 conexões) | 7.544,448 ms | 4.837,512 ms |
| Throughput global em 1 conexão | 0,1621 replay/s | 0,2362 replay/s |
| Throughput global em 3 conexões | 0,2887 replay/s | 0,4247 replay/s |
| CREATE UNIQUE CLUSTERED INDEX separado, fora das amostras | 115,762 ms | 93,842 ms |
| Leituras lógicas na consulta SQL instrumental | 4.166 | 4.166 |
| Leituras físicas na consulta SQL instrumental | 0 | 0 |
| CPU SQL do plano instrumental | 1.089 ms | 804 ms |
| Duração SQL do plano instrumental | 614 ms | 439 ms |

**Planos reais:** os dois mostram as mesmas classes de operadores `Clustered Index Scan`, `Hash Match`, `Parallelism`, `Table Insert`, `Table Scan`. Índice referenciado no trecho da consulta capturado: apenas PK temporária de observações, com nome volátil `PK__#v9_cal___...`; o índice `IX_v9_cal_sig` está **criado** mas **não consta na lista referenciada do plano da consulta capturada**. Isso **não é evidência de índice removido ou incorreto**, nem demonstra que outro plano seria melhor; hash do XML de plano difere por metadados temporários e não significa necessariamente mudança estratégica.

**Interpretação observacional:** com SQL e C# idênticos, a latência isolada do runner A foi **40,8% maior** que no runner B (`6.189/4.396-1`), e ambos os replays C# e SQL variaram na mesma direção; ambas as campanhas mantiveram iguais entradas, números de pares e decisões. A maior parcela absoluta do replay medido ficou no C# (incluindo serialização do recibo diagnóstico), e **não** na consulta `SELECT DISTINCT` isolada. Sob três conexões, o P95 individual cresce (~83% no runner A, ~74% no B), enquanto o **throughput agregado também melhora ~78–80%**: concorrência é um compromisso entre latência e vazão, não prova de throughput pior. Os P95 de fases não são aditivos e não correspondem ao P95 certificado do calibrador 324.

**Conclusão limitada:** comprovada sensibilidade forte do custo físico à capacidade/estado do GitHub-hosted runner, *sem mudança da lógica científica ou dos dados*. Essa prova é consistente com parte da regressão histórica 15→16/11, mas **não fornece planos históricos** daqueles dois runners nem isola a causa raiz dos 63 minutos. Não autoriza ajuste de índices, mudança de `AUTO_0`, otimização do scorer, rollback da REF, classificação de recall como gargalo ou desbloqueio de TEST. O experimento é considerado **executado com evidência diagnóstica real**, porém sua PR ainda requer integração e recibo de merge antes do encerramento formal GOV-DOC-01.
