# GOV-V9-HANDOFF-ARQUIVO-AUDITORIA-SQL-20261118 — implementação diagnóstica em PR

## Contexto e justificativa

A master contém o runtime V9 imutável de arquivo, a auditoria do grafo SQL e o replay reconciliado (#1712), mas o Runner não possuía um método que transportasse a fronteira tipada do arquivo diretamente para a auditoria operacional sem reler parâmetros científicos do SQL. Este incremento conecta componentes existentes para demonstrar o contrato sem ativar produção.

## Escopo e precedência

O método `RehearseV9PinnedFileNonPublishingAsync` recebe `V9OperationalRunBoundary`, coorte TEST sintética e grafo SQL físico independente. Encaminha `boundary.Runtime` à auditoria SQL em `JornadaHarness`, verifica o recibo e retorna status `CONCLUIDO_SEM_PUBLICACAO` sem criar run ou gravar decisões. Mantém precedência das decisões de fonte única em arquivo e gate produtivo fail-closed. Não altera o workflow científico, não habilita `RunV9Async`, não faz fallback SQL nem constitui prova E2E produtiva.

## Aceite e impactos

Exigir CI, compilação, teste estrutural e ensaio SQL descartável antes de integrar. Paridade de coorte física, persistência operacional e certificação de beta permanecem pendentes. Rastreabilidade: #1712, #1659, #1658 e GOV-DOC-01.
