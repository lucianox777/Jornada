# CI-V9-30K-PUSH-ONLY — gate técnico 30k sem disparo manual

**Decisão do PO:** 07/11/2026. **Estado:** DECIDIDO; implementação proposta na frente E, sujeita aos checks rápidos do HEAD da PR e à CI completa/30k no push da master após merge. **Escopo:** gate técnico regressivo conhecido `v9-physical-seed42-probe.yml`. **Rastreabilidade:** mandato A–L de 07/11, política [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Contexto, precedência e justificativa

O corpus técnico seed42/30k é a regressão sintética conhecida, não um experimento científico novo. Em 06/11 (confirmado 07/11), o PO estabeleceu checks rápidos na PR e CI completa + 30k físico técnico no `push` da `master` integrada, com classificador de impacto cumulativo desde a última prova física real. Preservar no mesmo workflow o modo manual `workflow_dispatch` (`preflight` / `execute_30k`) permitiria confundir execução técnica ordinária com autorização manual e acrescentaria caminhos de entrada e parâmetros paralelos. A decisão **mais nova de 07/11** elimina o disparo manual **somente do gate técnico**.

## Contrato vinculante

1. O workflow do gate técnico de 30k inicia somente em `push` para `master`; não recebe `workflow_dispatch`, `pull_request`, `inputs.expected_sha` ou `inputs.pinned_ibge_json`.
2. O commit `github.sha` e a ref `refs/heads/master` são conferidos antes de SQL; comparações de impacto usam a última baseline autenticada física cumulativa. Ausência de baseline, erro de classificação, falha na geração/pin do corpus IBGE ou execução física obrigatória não podem ser convertidos em `skipped` silencioso.
3. Mudança com efeito no calibrador/FS/TF/prior exige 30k real no SHA integrado; diferença documental comprovadamente sem impacto pode ser `NOT_REQUIRED_FOR_THIS_DIFF` pelo classificador. Toda falha obrigatória bloqueia novos merges e exige correção/reversão.
4. Execuções de campanhas científicas, controle Gold, TEST, VALIDATION independente, autorização DT-15 e publicação de modelo seguem **fora** deste workflow, com protocolos e aprovações próprias. Não remover gatilhos manuais de outros workflows científicos com este ato.
5. Não alterar seed42, 30k, hashes, critérios de FDR, `plan.json` científico nem resultados congelados para aprovar gate. CI PR continua rápida; `jornada-ci` completa e 30k no evento `push` da master.

## Implementação, verificação e impactos

A frente E retira o caminho manual e seu job somente de preflight, preserva o caminho auto de SQL descartável, o classificador cumulativo e a prova de corpus/IBGE, e atualiza testes estáticos de contrato para exigir exclusivamente `push` em master. A retirada do acionamento manual é uma **mudança operacional deliberada**: histórico antigo de runs e recibos não é reescrito. A PR só pode mesclar com checks aplicáveis verdes e merge serial; verificar no SHA final que o gate físico realmente executa no `push` pós-merge. Isso **não** é FDR certificada, validação independente, TEST, Gold ou ativação.

**Rastreabilidade:** PR da frente E, a vincular após abertura; [índice de decisões vigentes](Indice_Decisoes_Vigentes.md).
