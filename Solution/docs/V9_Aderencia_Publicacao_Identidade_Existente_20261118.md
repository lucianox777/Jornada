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
