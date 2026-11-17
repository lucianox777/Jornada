# GOV-V9-PREPARE-BRUTO-TRAIN-PLANOS-20261117

**Aprovação:** Product Owner, 17/11/2026. **Situação:** decisão aprovada; fase 1 em implementação na branch, ainda não integrada na master.

## Contexto e justificativa
O workflow V9 Standard Linear demonstrou geração de 100 mil registros e transferência íntegra PREPARE → TRAIN na execução #43989180083. O PO decidiu tornar PREPARE homogêneo, exclusivamente responsável pela geração do corpus sintético bruto. Os planos técnico e TF4 passam a ser preparados no TRAIN. O custo temporal adicional esperado é pequeno, mas deve ser medido.

## Decisão
**Fase 1 (implementação autorizada agora):** PREPARE conserva a geração sintética 100k atual, seed 42, demographic-primary e clean, e entrega corpus bruto com integridade/proveniência. TRAIN valida o handoff, baixa o artifact histórico pinado, executa os scripts existentes `v9-scale-100k-technical-plan.py` e `v9-v3-four-name-100k-plan.py`, valida planos e recibos, inicia SQL, reidrata IBGE e executa as 324 hipóteses. AUDIT, VALIDATION, TEST opcional, CLOSEOUT, segurança e retenção permanecem preservados.

**Fase 2 (direção aprovada; implementação posterior):** PREPARE usará `test_database`, quando preenchido, como referência/seed estatística adicional para gerar os mesmos 100 mil registros sintéticos brutos. Na ausência do parâmetro, mantém o gerador atual. `test_required` controla separadamente o TEST científico. A fase 2 depende de detalhar acesso somente leitura, privacidade, distribuição estatística, isolamento TRAIN/TEST e contrato de entrada. Não copiar registros pessoais reais para artifacts.

## Precedência e impactos
Revisa parcialmente `Decisao_GOV_V9_Fluxo_Padrao_Provisorio_20261116.md` apenas quanto à localização da construção dos planos: PREPARE deixa de construí-los, TRAIN passa a fazê-lo. Preserva a decisão de separar jobs e o contrato fail-closed de integridade, ajustando a allowlist do pacote para corpus bruto. TEST científico permanece bloqueado por `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`; nenhuma autorização para execução física automática.

## Aceite e rastreabilidade
Origem: aprovação expressa do PO em 17/11/2026. Evidência de referência: run #43989180083, SHA 37c90820969577c912e5a5c48db9aeaabd98c4b1. Exigir CI de contrato verde, smoke entre runners, parâmetros do gerador preservados, geração dos planos somente após validação do corpus, e execução física controlada autorizada antes de declarar implementação integral comprovada. A fase 2 não integra esta primeira PR.
