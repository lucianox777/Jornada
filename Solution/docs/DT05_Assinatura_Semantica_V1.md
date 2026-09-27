# DT-05 — Contrato da assinatura semântica V1

**Estado:** contrato técnico congelado para a implementação V1 existente; não constitui aceite de replay histórico nem de CPF tardio ponta a ponta. **Implementação canônica:** `database/migrations/20260927_Linkage_Transicao_Semantica_DT05.sql`, procedure `identidade.sp_registrar_transicoes_linkage_run`. Mudança nos campos, ordem, sentinelas ou codificação exige nova versão de assinatura e migração/ensaio explícitos; não alterar silenciosamente V1.

## Identidade e conteúdo

A chave lógica da sequência é `pessoa_observacao_id`. Para cada `linkage_resultado` do run em publicação, a procedure calcula `SHA2_256(CONVERT(VARBINARY(MAX), CONCAT(...)))` com prefixo `DT05_V1|`, campos na ordem exata abaixo, separados por `|`. Os campos nulos são representados por `<NULL>`. GUIDs são convertidos em `NVARCHAR(36)` quando indicado. A conversão SQL para `VARBINARY(MAX)` faz parte do contrato (não assumir que o hash de UTF-8 em outra linguagem seja equivalente).

| Ordem | Campo do resultado bruto | Conversão |
|---|---|---|
| 1 | `modelo_id` | `NVARCHAR(36)` |
| 2 | `modelo_versao` | conversão implícita de `CONCAT` |
| 3 | `status` | valor ou sentinela |
| 4 | `motivo` | valor ou sentinela |
| 5 | `resultado_publicacao` | valor ou sentinela |
| 6 | `pessoa_uuid_publicado` | `NVARCHAR(36)` ou sentinela |
| 7 | `status_publicacao` | valor ou sentinela |
| 8 | `motivo_publicacao` | valor ou sentinela |
| 9 | `melhor_candidato_uuid` | `NVARCHAR(36)` ou sentinela |
| 10 | `segundo_candidato_uuid` | `NVARCHAR(36)` ou sentinela |
| 11 | `politica_publicacao_versao` | valor ou sentinela |
| 12 | `pessoa_origem_id_publicado` | `NVARCHAR(36)` ou sentinela, conforme SQL atual |

**Excluídos intencionalmente:** `linkage_run_id`, timestamps, scores numéricos (inclusive arredondamento), `universo_referencia`, `calculado_em`, `publicado_em`. O resultado bruto de **cada run** permanece em `identidade.linkage_resultado`; o ledger não substitui o histórico bruto. Como modelo e motivo fazem parte da assinatura, mudanças nesses campos são transições mesmo que o UUID publicado permaneça igual. Uma alteração de CPF que não altere nenhum dos 12 campos **não** gera evento V1; o CPF bruto não integra a assinatura.

## Emissão e idempotência

Sob a transação SERIALIZABLE de publicação e lock do Runner, compara-se a assinatura do resultado com a **última transição registrada para a mesma observação**. Se não existe anterior: `INICIAL`; se o hash difere: `ALTERACAO_SEMANTICA` com `assinatura_anterior_sha256` igual ao hash da última transição; se é idêntico: não inserir evento. `UQ_linkage_transicao_resultado` impede duplicação do mesmo resultado em retry. A chave de ordenação do ledger é `transicao_id`, não o timestamp do run.

## Evidência e pendências

O teste SQL `Dt05SemanticThreeWavesSqlServerTests`, integrado no PR #524, verifica três runs, retry por onda, encadeamento de hashes e preservação dos resultados brutos. Na terceira onda ele altera **`motivo_publicacao`** para um marcador de evidência de CPF tardio: isso valida o ledger, **não** a ingestão real do CPF. Para fechar o Marco A ainda é necessário executar a cadeia real Bronze → Silver (nova versão append-only) → Runner → publicação, comparar identidade progressiva e verificar se os campos V1 mudam ou não, com resultado esperado declarado antes do ensaio. O Marco B, manifesto NAS ligado ao run e replay histórico determinístico, tem aceite independente. `LinkageReplay:CaptureBronzeSources` continua desligado por padrão.
