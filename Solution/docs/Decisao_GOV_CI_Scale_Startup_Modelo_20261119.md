# CI #44564338404 — compatibilidade do harness SCALE com seleção de modelo no startup (19/11/2026)

## Contexto e evidências
A execução manual `jornada-ci` #44564338404 na `master` SHA `356aec3527973c81a37cd48261e7e0d501dae675`, perfil `SCALE_PROFILE=smoke`, reprovou `scale-harness` com `RUNNER_MODEL_CONFIGURATION_MISSING: Linkage:SelectedModelVersion deve ser definido pelo Operador Master`. A exigência é intencional e foi aprovada em `GOV-RUNNER-STARTUP-MODELO-OPERADOR-MASTER-20261117`. O harness gerava e ativava um modelo DEV sintético, mas não o informava ao iniciar o Runner.

## Ajuste operacional proposto
Em `Solution/scripts/local-scale.sh`, fornecer `Linkage__SelectedModelVersion="$MODEL_VERSION"` apenas ao processo Runner, tanto na auditoria de blocking quanto em `MODEL_VALIDATION`. O valor vem da versão RASCUNHO gerada e validada no mesmo banco descartável de teste, previamente conferida como inteiro positivo. Preservar `--model-version "$MODEL_VERSION"` e sua validação de igualdade com a configuração. Não definir fallback em produção, não alterar o gate fail-closed nem o papel do Operador Master.

## Impactos, limites e precedência
Ajuste exclusivamente de infraestrutura de teste/CI. Não modifica pesos, TF, `U_ref`, plano 324, TRAIN/VALIDATION/TEST, modelo operacional produtivo ou publicação. Subordinado à decisão de startup de 17/11/2026 e GOV-DOC-01. Não equivale à aprovação de modelo produtivo. Os sete testes TF/U_ref reprovados no mesmo run ainda exigem reconciliação com as políticas de abstenção e imputação; esta PR não os altera. Aceitação pendente de CI rápida e execução física do `scale-harness` no SHA corrigido.

## Rastreabilidade
CI #44564338404, job `scale-harness`; `Solution/scripts/local-scale.sh`; `Solution/docs/Indice_Decisoes_Vigentes.md`; `Solution/docs/Decisao_GOV_Runner_Startup_Modelo_Operador_Master_20261117.md`.
