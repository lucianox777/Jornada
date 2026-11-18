# GOV-V9-TEST — Projeção congelada no TEST independente (2026-11-18)

**Estado:** correção proposta em PR; CI e revisão pendentes. **Política:** GOV-DOC-01.

## Evidência e contexto
A execução manual 44235715333, SHA d9b751212ce4af0502c6827ae783067142a4b8f7, concluiu ADMISSION, PREPARE, TRAIN (324), AUDIT e VALIDATION. O TEST reservou seed 897934382746562240 e validou a vinculação ao manifesto, mas falhou em `V9PhysicalSqlFrozenHoldout.EvaluateFrozenPartitionAsync` com `Snapshot e projeção do protocolo devem coincidir.`. A seed já exposta não é reutilizável.

## Diagnóstico técnico
O TEST construía `V9PhysicalAdaptiveExecution` sem `MaximumPrefixLength`, adotando zero, que impõe fingerprint da projeção padrão. O vencedor congelado pode carregar contrato de projeção V9 com comprimento de prefixo explícito. A comparação fail-closed rejeitou a divergência; não há evidência de falha da seed.

## Correção proposta, justificativa e impacto
O TEST resolve o comprimento de prefixo exclusivamente do snapshot imutável SHA-pinned, por `V9FittedSnapshotExecutionRuntime.Load`, que valida o fingerprint e exige correspondência única para projeções V9. O contrato padrão continua representado por zero. O avaliador mantém a comparação obrigatória de fingerprints. Não há refit, alteração do vencedor, seleção, relaxamento do protocolo, publicação ou ativação.

## Precedência e rastreabilidade
Complementa a decisão de seed reservada de 2026-11-17; não revoga as restrições de isolamento e imutabilidade do TEST. Código: `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs`. Regressão: `Solution/tests/scripts/test_v9_tf4_standalone_test.py`. Execução e logs: https://github.com/lucianox777/Jornada/actions/runs/44235715333/job/134694520640 . **Pendente:** CI, revisão, integração e nova campanha científica com seed inédita. TEST anterior inconclusivo, sem certificação.
