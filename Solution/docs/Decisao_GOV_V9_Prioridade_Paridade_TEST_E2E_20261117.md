# GOV-V9-PRIORIDADE-PARIDADE-TEST-E2E-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** prioridade aprovada; implementação e execução pendentes. **Política:** GOV-DOC-01.

## Decisão e justificativa
Priorizar imediatamente a conclusão da paridade entre o vencedor V9 em arquivo operacional imutável e o mecanismo real de execução TEST/Runner, preservando isolamento, modelo congelado, integridade, reserva global inédita do TEST, ausência de publicação e compatibilidade legada. Depois de provas físicas e CI aprovadas no SHA exato, executar o workflow científico integral do calibrador (ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST condicionado a vencedor → CLOSEOUT) pela master.

## Contexto e precedência
O workflow canônico `.github/workflows/v9-standard-linear-100k.yml` já contém a cadeia manual; a presença dos jobs não comprova paridade ou execução E2E concluída. A PR #1655 possui prova física limitada de RunAsync em coorte sintética descartável, não certificação produtiva 100k. Os bloqueios explícitos ainda presentes no workflow legado `.github/workflows/v9-v3-four-name-100k.yml` não podem ser removidos por simples substituição de flags. Esta prioridade complementa GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA e GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE; não revoga os gates científicos de segurança.

## Aceite e impactos
(1) Adaptador produtivo que consuma exclusivamente o arquivo operacional validado, sem fallback para parâmetros científicos SQL; (2) prova física independente de paridade de decisões e grafo completo na mesma coorte, com falha fechada e nenhuma publicação; (3) teste de regressão e CI obrigatória verde no SHA exato; (4) verificação do estado da master e aprovação expressa de preflight de segurança para execução manual; (5) recibos auditáveis para cada fase, distinguindo sem finalistas, falha técnica e vencedor elegível. Não executar campanha cara nem consumir TEST antes desses gates; não afirmar certificação antecipada.

## Rastreabilidade
Pedido do PO em conversa de 17/11/2026: implementar paridade como prioridade e executar workflow integral. Dependências: PR #1655; issues #1488, #1478, #1584; workflows `v9-standard-linear-100k.yml` e `v9-v3-four-name-100k.yml`. Documento criado em branch de trabalho, ainda não vigente na master.

## Retificação expressa da precedência pelo PO — 17/11/2026

O PO esclareceu que o objetivo prioritário **não é certificar todo o Runner produtivo antes de concluir o calibrador**. A ordem vinculante passa a ser:

1. **P0 — concluir a PR #1655**, sem ampliar seu escopo nem relaxar verificações obrigatórias.
2. **P0 — desacoplar calibrador e seletor:** o calibrador entrega vencedor como pacote/arquivo imutável; o seletor consome exclusivamente esse pacote para parâmetros científicos V9, sem leitura/gravação de parâmetros científicos SQL como interface entre ambos. Preservar SQL operacional, metadados, compatibilidade legada e rollback; remoção física de colunas requer inventário e aprovação específica.
3. **P0 — concluir e executar o workflow científico E2E** ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST quando houver vencedor → CLOSEOUT, com evidências e SHA fixado.
4. **P1 — implementar somente a paridade física necessária para o TEST científico E2E**. Certificação produtiva geral do Runner não é pré-requisito automático: qualquer dependência deve ser demonstrada por contrato/teste específico.
5. **P2 — melhorias não bloqueantes** de robustez ficam fora do caminho crítico.

**Justificativa:** evitar trabalho paralelo e bloqueio circular entre certificação produtiva ampla e validação científica do calibrador. **Segurança:** preservar reserva one-use, isolamento, gates de TEST, ausência de publicação e falha fechada; nenhuma permissão de bypass. **Impacto:** substitui a exigência genérica de “adaptador produtivo” do item (1) de Aceite acima pelo contrato mínimo comprovável necessário ao TEST científico; demais controles de integridade e rastreabilidade permanecem. **Precedência:** esta retificação expressa do PO prevalece sobre a ordem de implementação descrita anteriormente neste documento. **Rastreabilidade:** decisão do PO na conversa de 17/11/2026; PR #1655; GOV-V9-DESATIVACAO-PARAMETROS-CIENTIFICOS-SQL-20261117; issues #1488/#1478/#1584.
