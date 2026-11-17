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
