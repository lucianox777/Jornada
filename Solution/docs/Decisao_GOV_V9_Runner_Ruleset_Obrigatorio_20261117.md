# GOV-V9-RUNNER-RULESET-OBRIGATORIO-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** aprovado, implementação parcial proposta nesta PR; pendente CI/merge. **Política:** GOV-DOC-01.

## Contexto e justificativa
O produto permanece em desenvolvimento e não há obrigação de executar modelos legados sem ruleset. O leitor produtivo `LinkageRuleSetReader.TryLoadAsync` devolvia `null` quando não havia linha em `identidade.linkage_ruleset`, permitindo caminhos alternativos no seletor. Isso não garante compatibilidade da configuração do Calibrador com o seletor.

## Decisão e precedência
Exigir ruleset persistido no carregamento produtivo: ausência deve causar erro explícito antes da resolução, sem fallback legado. Manter as verificações já existentes de algoritmo, normalização, projeção e fingerprint. A decisão substitui a tolerância anterior de modelos sem ruleset, mas não dispensa controles de REPLAY, versões suportadas, integridade nem segurança. `test_database` permanece referência estatística opcional do PREPARE conforme GOV-V9-PREPARE-BRUTO-TRAIN-PLANOS-20261117.

## Impactos e limites desta PR
O leitor rejeita `null` e o log de snapshot deixa de registrar `LEGACY/NONE`. Consumidores ainda possuem ramos legados estruturalmente presentes, agora inalcançáveis pelo leitor produtivo; a remoção integral desses ramos e a prova de compatibilidade de ativação V9 permanecem pendentes. Testes integrados, CI e promoção requerem validação. Não executar TRAIN/TEST por esta alteração.

## Rastreabilidade
Origem: autorização expressa do PO em conversa de 17/11/2026. Frente correlata: PR #1639 (zero finalistas). Critério de aceite final: ausência de ruleset bloqueia o seletor, configuração V9 incompatível não é ativável, regressões aprovadas e índice sincronizado.
