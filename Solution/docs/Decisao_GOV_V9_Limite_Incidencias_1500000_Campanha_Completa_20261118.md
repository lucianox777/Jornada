# GOV-V9-CAP-INCIDENCIAS-1500000-CAMPANHA-COMPLETA-20261118 — APROVADO PELO PO

## Decisão
Em 18/11/2026, após ponderação técnica, o Product Owner aprovou **definitivamente** elevar o limite operacional padrão de incidências de pares candidatos da V9 de **1.000.000 para 1.500.000** nas avaliações físicas de **TRAIN, VALIDATION e TEST independente/científico**, para liberar a execução do workflow completo de calibração. Não é exceção provisória nem autorização para elevar outros limites.

## Contexto e justificativa
O diagnóstico físico #44317521867 identificou 1.057.070 incidências entre gestores, 899.776 pares distintos e 157.294 incidências repetidas entre passes; o teto anterior bloqueou a avaliação antes do join. O diagnóstico posterior #44320234297 concluiu com o teto de 1.500.000 e 20 repetições físicas. Isso demonstra viabilidade para aquela coorte, **não** garante que toda configuração futura caiba no novo teto nem constitui certificação científica. O recall mínimo de 95% não estabelece teto matemático para incidências de candidatos. A prioridade aprovada é liberar a cadeia científica completa; otimizações de performance podem ser investigadas depois, preservando medições obrigatórias.

## Escopo e invariantes
- Alterar apenas o teto padrão de **incidências de pares candidatos** para 1.500.000, compartilhado entre TRAIN, VALIDATION e TEST. Preservar o teto separado de **assinaturas projetadas em 1.000.000**.
- Não modificar regras de blocking, TFs, hipóteses, recall, FDR, ranking, seleção de vencedor, populações, seeds, algoritmo de resolução ou requisitos de 20 repetições físicas.
- Rejeitar explicitamente avaliações que ultrapassem o teto; **não truncar pares**, não omitir rejeições nem apresentar uma hipótese não avaliada como cientificamente inferior. Registrar o limite efetivamente utilizado e a rejeição de capacidade.
- Preservar TEST independente com único vencedor congelado, nova coorte 100k de uso único, ausência de refit/publicação e SQL descartável.
- Utilizar o workflow manual existente `.github/workflows/v9-standard-linear-100k.yml` (ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST → CLOSEOUT); não criar segundo protocolo científico. Executar apenas após revisão, CI obrigatória e integração à master; verificar previamente admissão e recursos.

## Impactos, riscos e precedência
Aumenta o teto de incidências admitidas e pode elevar consumo de memória/CPU e tempo; a execução continua sujeita aos recursos reais do runner e aos demais guardrails. Substitui **somente quanto ao teto padrão científico** a restrição de 1.000.000 preservada em [GOV-V9-EXPERIMENTO-CAP-1500000-20261118](Decisao_GOV_V9_Experimento_Cap_1500000_20261118.md); mantém a natureza não científica do experimento histórico. Complementa [GOV-V9-DIAGNOSTICO-CAP-INCIDENCIAS](Decisao_GOV_V9_Diagnostico_Cap_Incidencias_20261118.md). Nenhuma execução científica ou certificação está presumida por esta decisão.

## Rastreabilidade
Autorização expressa do PO na conversa do projeto em 18/11/2026: aceitou a recomendação de estabelecer 1.500.000 como padrão e solicitou preparar a execução total. Implementação proposta na branch `feat/v9-cap-1500000-full-calibration-20261118`, sujeita a PR, CI, merge e execução manual posterior. Política GOV-DOC-01.
