# GOV-V9-PREPARE-BRUTO-TRAIN-PLANOS-20261117

**Aprovação:** Product Owner, 17/11/2026. **Situação:** decisão vigente; fase 1 integrada via PR #1621 (squash `80d95b6a9beca4dba8c9aa72eb6d8839935c6140`), CI de contrato e smoke aprovadas; execução física 100k posterior ainda não comprovada.

## Contexto e justificativa
O workflow V9 Standard Linear demonstrou geração de 100 mil registros e transferência íntegra PREPARE → TRAIN na execução #43989180083. O PO decidiu tornar PREPARE homogêneo, exclusivamente responsável pela geração do corpus sintético bruto. Os planos técnico e TF4 passam a ser preparados no TRAIN. O custo temporal adicional esperado é pequeno, mas deve ser medido.

## Decisão
**Fase 1 (implementação autorizada agora):** PREPARE conserva a geração sintética 100k atual, seed 42, demographic-primary e clean, e entrega corpus bruto com integridade/proveniência. TRAIN valida o handoff, baixa o artifact histórico pinado, executa os scripts existentes `v9-scale-100k-technical-plan.py` e `v9-v3-four-name-100k-plan.py`, valida planos e recibos, inicia SQL, reidrata IBGE e executa as 324 hipóteses. AUDIT, VALIDATION, TEST opcional, CLOSEOUT, segurança e retenção permanecem preservados.

**Fase 2 (direção aprovada; implementação posterior):** PREPARE usará `test_database`, quando preenchido, como referência/seed estatística adicional para gerar os mesmos 100 mil registros sintéticos brutos. Na ausência do parâmetro, mantém o gerador atual. `test_required` controla separadamente o TEST científico. A fase 2 depende de detalhar acesso somente leitura, privacidade, distribuição estatística, isolamento TRAIN/TEST e contrato de entrada. Não copiar registros pessoais reais para artifacts.

## Revogação expressa de decisão anterior (PO, 17/11/2026)
A decisão [GOV-V9-TEST-DATABASE-EXISTENTE-OPCIONAL-20261114](Decisao_GOV_V9_TEST_Database_Existente_Opcional_20261114.md) foi declarada **obsoleta** pelo PO. A semântica anterior de `test_database` como seleção de banco existente para diagnóstico pós-VALIDATION **não permanece vigente, nem como exceção do workflow legado**. Prevalece o uso no PREPARE como referência estatística opcional para geração sintética 100k, mantendo `test_required` independente. Remover/adequar rotas legadas incompatíveis em implementação posterior, com testes de regressão e sem habilitar TEST científico. Esta atualização é de governança, não declara migração do código concluída.

## Precedência e impactos
Revisa parcialmente `Decisao_GOV_V9_Fluxo_Padrao_Provisorio_20261116.md` apenas quanto à localização da construção dos planos: PREPARE deixa de construí-los, TRAIN passa a fazê-lo. Preserva a decisão de separar jobs e o contrato fail-closed de integridade, ajustando a allowlist do pacote para corpus bruto. TEST científico permanece bloqueado por `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`; nenhuma autorização para execução física automática.

## Aceite e rastreabilidade
Origem: aprovação expressa do PO em 17/11/2026. Evidência de referência: run #43989180083, SHA 37c90820969577c912e5a5c48db9aeaabd98c4b1. Exigir CI de contrato verde, smoke entre runners, parâmetros do gerador preservados, geração dos planos somente após validação do corpus, e execução física controlada autorizada antes de declarar implementação integral comprovada. A fase 2 não integra esta primeira PR.

## Complemento operacional — workflow padrão e parâmetro (PO, 17/11/2026)
O PO confirmou que o **workflow V9 Standard Linear atualmente utilizado** é o padrão único para esta evolução, com entrada opcional `test_database` no `workflow_dispatch`. Não reutilizar a semântica obsoleta de TEST de 14/11 nem manter exceção decisória para o legado. O campo vazio mantém o gerador IBGE atual. O campo preenchido destina-se exclusivamente à referência estatística externa no PREPARE, **independentemente de `test_required`**.

**Implantação incremental e segura:** a primeira PR desta etapa expõe a entrada e aplica validação fail-closed antes de qualquer geração quando não vazia, com `V9_PREPARE_EXTERNAL_STATISTICAL_ADAPTER_NOT_IMPLEMENTED`. Isso **não** implementa a extração de estatísticas nem habilita o caminho externo; a futura implementação exige runner privado com acesso SELECT-only, contrato de agregados sem PII, proveniência/fingerprint, verificação de suporte e independência TRAIN/TEST. O caminho vazio permanece inalterado. O bloqueio do TEST científico continua vigente. A documentação não deve declarar o modo externo operacional antes de CI e prova física específicas.
