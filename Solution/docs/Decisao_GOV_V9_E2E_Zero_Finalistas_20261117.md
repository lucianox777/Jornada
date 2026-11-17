# GOV-V9-E2E-ZERO-FINALISTAS-20261117 — encerramento científico sem finalistas

**Autoridade:** Product Owner, 17/11/2026. **Estado:** aprovado; implementação proposta nesta PR, sujeita à CI e integração. **Política:** GOV-DOC-01.

## Contexto e justificativa
O AUDIT pode concluir corretamente sem identificar finalistas elegíveis. O workflow Standard Linear produzia o recibo `COMPLETED_WITHOUT_ELIGIBLE_FINALISTS`, mas em seguida executava `exit 1`, confundindo resultado científico negativo com falha operacional.

## Decisão
Quando PREPARE/TRAIN e AUDIT terminarem com sucesso e o AUDIT declarar explicitamente `has_finalists=false`, o workflow deve encerrar com sucesso, registrando `COMPLETED_WITHOUT_ELIGIBLE_FINALISTS`, zero finalistas, ausência de vencedor e ausência de certificação/ativação. VALIDATION e TEST permanecem **skipped**, sem geração nem exposição de TEST. Erros técnicos, ausência de evidência do AUDIT, estados desconhecidos ou cancelamentos continuam falhando em modo fechado.

## Implementação, impacto e precedência
Alterar somente o gate de AUDIT e o E2E CLOSEOUT do workflow `.github/workflows/v9-standard-linear-100k.yml`, com testes de regressão. Não afrouxar a exigência de VALIDATION quando houver finalistas. Esta decisão altera especificamente a classificação do resultado terminal sem finalistas e prevalece sobre a regra anterior de falha obrigatória nesse caso, inclusive no registro GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114. Não autoriza executar TRAIN/TEST nem promover modelos.

## Outras decisões do PO nesta mesma frente (implementação pendente)
O PO também aprovou retirar fallback de ruleset ausente do seletor, bloquear ativação de contratos V9 incompatíveis e substituir a semente TEST fixa por reserva global inédita, atômica e auditável. Essas alterações exigem PRs de implementação e testes próprios; **não são declaradas implementadas por esta PR**. O parâmetro `test_database` mantém a semântica de referência estatística opcional no PREPARE, segundo GOV-V9-PREPARE-BRUTO-TRAIN-PLANOS-20261117, sem ativar o adaptador externo.

## Rastreabilidade e aceite
Origem: aprovação expressa do PO em conversa de 17/11/2026. Evidências exigidas: teste de zero finalistas, teste de falha técnica, verificação de status do closeout e CI. Não declarar integração até merge na master.
