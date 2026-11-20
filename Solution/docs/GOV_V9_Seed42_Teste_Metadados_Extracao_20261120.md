# GOV-V9-SEED42-TESTE-METADADOS-20261120 — proposta em PR

## Contexto
A PR #1751 (Draft) continha um teste adicional de rejeição de alteração apenas de metadados do manifesto. A correção do gerador e as evidências físicas foram integradas separadamente pela PR #1752. A PR #1749 sincronizou o índice histórico. A cobertura específica de metadados não estava presente no teste da master no momento desta extração.

## Escopo e justificativa
Extrair somente o teste `test_metadata_only_manifest_change_remains_blocked` para preservar o comportamento fail-closed do gate imutável: uma alteração no hash do manifesto deve ser recusada mesmo com hashes do corpus inalterados e com o bootstrap do pin atualizado. O teste usa bytes sintéticos em memória e não executa geração física, SQL, TRAIN ou TEST científico.

## Impactos, precedência e rastreabilidade
Precedência: GOV-DOC-01, registro imutável seed42/30k, decisões de proveniência e manifesto histórico integradas via PRs #1750 e #1752. Nenhum código de produção, referência, LOCK, workflow, modelo ou critério científico é modificado. A PR #1751 não deve ser encerrada até que a cobertura residual seja integrada e sua substituição rastreada.

## Estado
Proposta técnica em PR separada, sujeita à CI e à autorização explícita do PO para merge. Este registro não constitui aprovação científica.
