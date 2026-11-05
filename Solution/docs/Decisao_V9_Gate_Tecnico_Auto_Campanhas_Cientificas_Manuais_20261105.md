# DC-V9-GATE-TECNICO-AUTO-VS-CAMPANHA-CIENTIFICA — decisão de 05/11/2026

**Aprovada pelo responsável em 05/11/2026.** Revoga a exigência de disparo humano individual para o **gate técnico** da campanha histórica seed 42, mas não autoriza experimentos confirmatórios automáticos.

## Natureza distinta das operações

| Operação | Disparo | Conjunto e evidência | Limites |
|---|---|---|---|
| Gate técnico 30k seed 42, TRAIN, protocolo fixo | Automático quando uma PR do próprio repositório alterar superfície classificada como impactante; também disponível manualmente | SQL efêmero JornadaHarness, referências IBGE e corpus sintético clean pinados em manifesto imutável; SHA exato de branch; 45 braços/135 observações | **Regressão técnica não confirmatória**, jamais evidência de TEST ou autorização |
| Reprodução determinística V2 com plano e artefatos históricos imutáveis | Pode ser automática no **workflow oficial** sob o mesmo protocolo técnico | Comparação de hashes, medidas e snapshot fixado | Não alterar seed/plano nem abrir hipótese nova; não usar arquivo autodeclarado de "aprovação pelo dono" |
| Campanhas científicas com novas seeds ou coortes independentes | Manual, solicitação e protocolo LOCKED explícitos | Seeds/manifestos inéditos; hipótese, alfa, multiplicidade, métricas e orçamento pré-registrados; isolamento por identidade-base | Não consumir holdout automaticamente e não repetir procurando significância |
| Gold, TEST selado e persistência institucional | Manual e sujeito à governança apropriada | Evidência independente, invariantes de identidade e controle de acesso | Não publicar nem ativar automaticamente; DT-15 continua independente |
| Testes de regressão que reutilizam VALIDATION conhecido | CI automática permitida | Dados já conhecidos e finalidade de engenharia | Não equivale à nova avaliação independente |

A regra de **30.000 pessoas-base não é tamanho universal de calibração**. A exigência pertence exclusivamente ao probe histórico seed 42: estudos novos podem ter população declarada e SHA-autenticada conforme a decisão `DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105`.

## Salvaguardas obrigatórias

1. Sem autodeclaração de autorização em arquivo de branch. A permissão para CI técnico existe no código e nas regras auditáveis do workflow. `v9-v2-calibration-chat-approved-once.yml` e seu marcador de autorização não podem integrar a master.
2. O classificador de impacto falha fechado para mudança ambígua; só branches do próprio repositório; checkout e HEAD remoto coincidem; job físico completo e stable check devem ter sucesso **na mesma execução e no SHA exato**.
3. Referência IBGE, proveniência e manifestos de corpus precisam ter digest e conteúdo verificados antes da estimativa. SQL descartável, sem TEST, sem PII real e sem credenciais em evidências.
4. Uma execução `skipped`, `cancelled`, `in_progress`, sem o resultado físico positivo ou de outra branch/commit **não** é aprovação.
5. Resultados do gate técnico seed42 são **EVIDÊNCIA TÉCNICA DE REGRESSÃO — NÃO CONFIRMATÓRIA**. A execução repetida não cria amostra estatisticamente independente. Não usar esses resultados para proclamar FDR certificada nem validar hipótese escolhida após inspecioná-los.
6. Campanhas científicas novas permanecem com manifesto LOCKED, decisão de início específica, TEST/Gold controlados, pré-registro de hipótese/alfa e proteção contra reuso de holdout. Separar expressamente `EXECUTED`, `TECHNICALLY_APPROVED`, `STATISTICALLY_ELIGIBLE`, `CERTIFIED` e `OPERATIONALLY_AUTHORIZED`.
7. A seleção automática de parâmetros **dentro de um plano aprovado** não é ativação operacional. Autoridade competente continua decidindo DT-15.

**Implementação técnica:** workflow `.github/workflows/v9-physical-seed42-probe.yml` integrado pela PR #1285; orquestrador de evidência automática em PR #1286. A regra científica não autoriza alterar workflows independentes para incluir disparo por PR.

**Precedência:** substitui exclusivamente os portões antigos "30k seed42 apenas manual" em `V9_TF_DC_0_21_Rastreabilidade_20261101.md`, preservando parâmetros técnicos e controles experimentais. Complementa a decisão de não exclusividade de 30k.
