# GOV-V9-P0-1734 — Diagnóstico de proveniência do gerador LOCKED (19/11/2026)

**Estado:** investigação aprovada pelo PO; diagnóstico implementado em PR; restauração física **pendente**.

## Contexto e evidência

A issue #1734 registra falhas de integridade do gerador. A PR #1721 alterou quatro arquivos sob `Solution/src/Jornada.Linkage.SyntheticCorpus`: `Program.cs`, `SyntheticCorpusGenerator.cs`, `SyntheticCorpusMaterializer.cs` e `SyntheticCorpusModels.cs`. O gate histórico `v9-tf-frozen-transfer-lock-gate.py` compara o digest de todos os arquivos rastreados e o SHA individual do contrato de partição com manifestos LOCKED. Alterar o código atual produz incompatibilidade de hashes mesmo que a configuração padrão mantenha 60/20/20. **Não há ainda prova de equivalência byte a byte do corpus.**

## Decisão operacional aprovada

O PO autorizou continuar a investigação P0 e restaurar integridade sem regenerar manifestos científicos por conveniência. Preservar contratos históricos e funcionalidades vigentes da V9; investigar primeiro, corrigir minimamente, executar gates físicos antes do merge de correção. Esta autorização **não** aprova `manifest-v2`, nova campanha TEST, promoção científica nem alteração de parâmetros.

## Entrega desta PR

O script `Solution/scripts/v9-generator-lock-provenance-diagnostic.py` compara, somente por leitura de objetos Git, o digest dos 25 arquivos do gerador na revisão histórica indicada em cada LOCK com a revisão HEAD, incluindo arquivos adicionados, removidos e modificados. Sinaliza separadamente se o histórico corresponde ao digest pré-registrado. Não gera dados, não altera os LOCKs, não usa TEST e não certifica corpus.

## Critérios pendentes

1. Executar o diagnóstico e preservar JSON, SHA do commit e logs.
2. Comparar os bytes de geração seed 42 e as referências originais em ambiente isolado.
3. Identificar quais gates vigentes devem validar o gerador atual sem reabrir campanhas históricas já concluídas.
4. Implementar correção mínima em PR própria, provar gates e atualizar a documentação definitiva antes de merge.
5. Reconciliar precedência com GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119: LOCK histórico é imutável; seu digest não é automaticamente um requisito para o gerador beta evolutivo atual.

**Rastreabilidade:** issue #1734; PR #1721; manifestos `V9_TF_SEEDS_44_47_LOCK.json` e `V9_TF_SEEDS_48_57_LOCK.json`; GOV-DOC-01.
