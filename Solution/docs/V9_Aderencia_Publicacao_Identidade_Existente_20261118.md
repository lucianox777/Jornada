# Verificação de aderência V9 à publicação de identidade existente — 18/11/2026

**Natureza:** registro de engenharia e regressão; **nenhuma decisão nova**. Aplicam-se `Arquitetura_Identidade_Linkage.md`, `Decisoes_Canonicas_Identidade_Linkage_20260929.md` e a especificação técnica v5.00 candidata, sem alterar sua precedência.

## Fronteiras já existentes

- `silver.pessoa_observacao` preserva observações; `identidade.pessoa_origem_progressiva` conserva `initial_uuid`, `canonical_uuid` e estado da origem persistente.
- `Jornada_Identidade_Progressiva.sql` distingue `NOVA_IDENTIDADE` com estado `REFERENCIA` e `INDEFINIDA` sem `canonical_uuid`. Origem sem chave persistente não recebe `initial_uuid` artificial.
- `ProbabilisticLinkageBatchRunner.cs` possui a derivação da decisão de publicação e a guarda `THROW 51835` contra `SEM_CANDIDATO_*` contradito por candidatos persistidos. O motivo textual e UUIDs nulos **não comprovam** por si só a completude física da busca.
- `LinkagePublicationBatchSql.cs` já recompõe `gold.pessoa` e sincroniza Serving em lote; V9 deve reutilizar essa fronteira após certificação, não introduzir escrita paralela.
- `RunV9Async` continua bloqueado por `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`. A presente entrega não o desbloqueia.

## Evidência adicionada

`Solution/tests/Jornada.Tests/Unit/V9ExistingIdentityPublicationBoundaryTests.cs` verifica por contrato de fonte a permanência do bloqueio V9, a separação entre `NOVA_IDENTIDADE` e `INDEFINIDA`, a exigência de origem persistente, a proteção de candidatos contraditórios e o reaproveitamento da recomposição Gold/Serving.

**Limitação explícita:** estes testes são estruturais; não demonstram comportamento SQL em execução, completude física dos passes, nem certificação produtiva. O próximo gate é um teste físico descartável com ausência real de candidato, candidato incerto, origem ausente, execução truncada e atomicidade de publicação. Preservar as autorizações DT-10/DT-15 e o modelo V9 inativo até aprovação.

## Rastreabilidade

Continuidade de implementação solicitada pelo PO em 18/11/2026, sem decisão adicional; GOV-DOC-01. Referências: `Solution/docs/Arquitetura_Identidade_Linkage.md`, `Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md`, `Solution/database/Jornada_Identidade_Progressiva.sql`, `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`, `Solution/src/Jornada.Linkage.Runner/LinkagePublicationBatchSql.cs`.

## Verificação complementar da procedure DT-10 em lote (PR subsequente)

A migração `20261029_Linkage_NoCandidate_Literal_Guards.sql` foi examinada diretamente na definição da procedure `identidade.sp_publicar_resolucao_progressiva_linkage_lote`. O contrato SQL exige transação explícita e isolamento SERIALIZABLE, reconcilia avaliados/elegíveis/itens/resultados e confronta os universos de itens e resultados antes de emitir eventos. A decisão `NOVA_IDENTIDADE` exige motivo literal `SEM_CANDIDATO_`, `initial_uuid` e ausência de candidatos persistidos (51835); `INDEFINIDA` não admite destino publicado (51814). Há precedência por origem de vínculos determinísticos/governados.

O teste `V9Dt10BatchPublicationGuardsTests.cs` passa a proteger essas invariantes por inspeção estrutural do SQL instalado como migração, inclusive a ordem da guarda 51835 antes do primeiro INSERT de evento. **Não é teste físico SQL Server e não prova que o produtor V9 materializou todo o universo de candidatos.** Essa comprovação continua pendente; não liberar V9 para publicação com base nesta evidência.

## Inventário de ensaios físicos existentes e lacuna específica

A revisão dos testes de integração identificou evidências físicas já implementadas: `LinkageNoCandidateLiteralGuardsSqlServerTests.cs` cobre ausência declarada de candidato, contradições com candidatos persistidos, replay e rollback de lote misto; `LinkageProgressivePublicationSqlServerTests.cs` cobre promoção do UUID inicial e idempotência; `DT10PublicationEvidenceSqlServerTests.cs` cobre concorrência e falha injetada após INSERT do ledger. Esses testes **não equivalem** a uma prova end-to-end de busca física V9 nem a uma execução científica.

A presente alteração acrescenta um caso SQL Server descartável para **run incompleto**: após materializar um run sintético, `avaliados` é reduzido a zero, a publicação em lote deve lançar `51818`, e o estado progressivo e a contagem de eventos devem permanecer inalterados. O ensaio depende de `JORNADA_DT10_EVIDENCE=1` e de `JORNADA_TEST_SQL_CONNECTION` apontando exclusivamente para `JornadaSyntheticDev`; sua mera compilação não constitui execução física. Nenhuma decisão nova; GOV-DOC-01.
