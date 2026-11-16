# GOV-V9-SQL-INDICE-ASSINATURAS-APOS-BULK-20261116 — índice de assinaturas após carga física

**Autoridade:** Product Owner, 16/11/2026 (America/Sao_Paulo): após avaliação de custo de recriar `IX_v9_cal_sig` e esclarecimento de que se trata de mudar sua ordem relativamente ao `SqlBulkCopy`, aprovou: **«Pode implantar esta alteração»**. **Política:** GOV-DOC-01.

**Estado:** decisão APROVADA; implementação proposta na branch `perf/v9-sql-signature-index-after-bulk-20261116`; **não declarar integrada à master, validada em SQL físico ou acelerada sem evidência**. PR, CI, squash e medições devem ser anexados a este registro quando disponíveis.

## Contexto e evidência

- Na rotina V9, `CreateTablesAsync` criava a tabela temporária `#v9_cal_sig` **e** seu índice `IX_v9_cal_sig(part,pass_id,assinatura,obs_id)` antes de inserir qualquer assinatura. `BulkSignaturesAsync` inseria as assinaturas por `SqlBulkCopy` com `BatchSize=25_000`.
- `EvaluateFrozenHoldoutAsync` executa `EndToEndRepetitions` (20 no plano TF4 vigente) replays completos por hipótese **elegível**, recriando tabelas, assinaturas, índices, união de pares e o scoring/Resolve em cada repetição. O máximo teórico das 324 hipóteses é 6.480 criações no holdout, além da preparação de grupos; rejeições anteriores ao P95 reduzem a contagem real. Uma criação por carga de assinaturas, **não por batch de 25 mil**.
- A degradação retrospectiva de throughput entre campanhas com geração local e corpus sintético persistido REF é observacional, não uma prova de custo do índice; não atribuir causalidade nem prometer recuperação de ~22%. A campanha [#43908715448](https://github.com/lucianox777/Jornada/actions/runs/43908715448) foi lançada na master anterior à mudança e **não é alterada nem cancelada por esta PR**.

## Decisão técnica e escopo

1. Retirar `CREATE INDEX IX_v9_cal_sig` de `CreateTablesAsync`, que deve criar as tabelas temporárias sem esse índice.
2. Em `BulkSignaturesAsync`, após **todos** os lotes do `SqlBulkCopy` terem sido inseridos e o bulk ter sido descartado, executar o mesmo `CREATE INDEX IX_v9_cal_sig ON #v9_cal_sig(part,pass_id,assinatura,obs_id)` antes de retornar. A mesma rotina serve ao censo do catálogo, ao probe inicial, ao holdout e às repetições do probe que constroem novas assinaturas.
3. Não alterar a chave, unicidade (índice continua não único), collation, nomes de tabelas, algoritmo de JOIN, `SELECT DISTINCT`, `IX_v9_cal_pairs`, batch size, contagem de hipóteses, recursos SQL, ordens científicas, consultas ou fonte de corpus/REF.
4. Preservar integralmente todos os replays elegíveis; cada P95 físico deve continuar incluindo **a criação do índice**, apenas em posição posterior à carga. Não cachear pares, reduzir 20 repetições, substituir SQL por memória ou mudar protocolo de latência.
5. Atenção às fases do timing: antes o índice de assinaturas estava em `CreateTablesMilliseconds`; depois passará a integrar `BulkSignaturesMilliseconds` por estar dentro de `BulkSignaturesAsync`. Comparar sempre o **tempo total e o P95 físico**, não uma fase isolada sem normalização.
6. Testar regressões estruturais dos caminhos SQL, compilação e gates de CI obrigatórios. A paridade física de contagem/SHA de pares e do P95 exige benchmark/ensaio controlado independente com SQL Server descartável; esta mudança não autoriza iniciar um novo TRAIN/TEST científico.

## Justificativa e riscos

A criação pós-carga pode evitar manutenção incremental de um índice durante milhares de inserções, dando ao otimizador o índice completo antes do auto-JOIN. Em contrapartida, a construção em bloco pode exigir maior memória, ordenação/tempdb ou ser mais lenta dependendo do tamanho e da distribuição das assinaturas. **O número de recriações é preservado** e não há ganho quantificado sem medição.

O risco de alteração do plano de execução devido à atualização das estatísticas é relevante: verificar pares e hashes iguais, além de tempo, leituras, CPU, tempdb, perfil de runner e eventuais regressões. Segurança de conexão descartável `JornadaHarness`, falha fechada, isolamento de TEST, snapshots, auditoria e procedimentos de governança mantidos.

## Rastreabilidade, precedência e aceite

- Código: `Solution/src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlAdaptiveCalibrationProbe.cs`; todos os consumidores de `BulkSignaturesAsync` entram em materialização com `IX_v9_cal_sig` construído.
- Testes: `Solution/tests/scripts/test_v9_sql_signature_index_after_bulk.py`; protegem ordem estrutural e manutenção de replays, **não** substituem equivalência SQL física.
- Investigação anterior: [V9 TF4/324, desempenho e rollback 15/11](Investigacao_V9_TF4_324_Desempenho_Rollback_20261115.md); [plano de possível reutilização física do holdout](Plano_Otimizacao_V9_Holdout_SQL_20261112.md), que **permanece proposta separada e não foi aprovada por esta decisão**.
- Política superior: GOV-DOC-01; decisões TF4 324 hipóteses, SQL físico íntegro, P95 ponta a ponta, TRAIN→AUDIT→VALIDATION, TEST one-use/fail-closed e autorização manual de campanhas continuam vigentes.
- Aceite de integração: revisão do diff restrito, CI de segurança/unidade/build e merge squash após checks; aceite de **desempenho** futuro somente com medições físicas reproduzíveis do mesmo corpus/SQL/runner, sem atalhos de ciência.
