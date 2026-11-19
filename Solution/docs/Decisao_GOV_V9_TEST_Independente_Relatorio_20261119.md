# GOV-V9-TEST-INDEPENDENTE-RELATORIO-20261119

**Decisão aprovada pelo Product Owner em 19/11/2026.** O TEST científico independente não aprova, rejeita, substitui ou invalida o vencedor científico da VALIDATION. O TEST executa sua avaliação e emite relatório próprio, inclusive resultados desfavoráveis ou falhas técnicas, preservando o vencedor e seu SHA. A autorização de produção permanece exclusivamente com o operador master (GOV-V9-OPERADOR-MASTER-AUTORIDADE-PRODUCAO-20261119).

## Contexto e justificativa
Na campanha V9 de 10 mil identidades a VALIDATION selecionou um vencedor e o TEST independente produziu medições. A lógica de encerramento `v9-tf4-e2e-closeout.py` exigia sucesso do TEST para considerar a cadeia concluída; isso confundia completude do relatório independente com validade da seleção científica.

## Contrato e impactos
TRAIN/AUDIT/VALIDATION continuam sujeitos aos próprios contratos. Quando há vencedor, o TEST permanece obrigatório conforme decisão vigente; o resultado do TEST (sucesso, falha, cancelamento, recibo ausente ou inválido) passa a ser informado separadamente e não revoga o SHA do vencedor. Um recibo TEST inválido nunca é tratado como medição verificada. A integridade, isolamento, não publicação, não refit e rastreabilidade dos recibos válidos continuam exigidos. Não se promove nem ativa modelo em produção, não se altera critério estatístico, TF4, grade de hipóteses ou coorte selada. Falha de infraestrutura do TEST não deve ser apresentada como sucesso do próprio TEST.

## Precedência e rastreabilidade
Complementa GOV-V9-TEST-OBRIGATORIO-SE-VENCEDOR-20261117, substituindo apenas a regra de que falha do TEST impede o encerramento científico com vencedor. Preserva GOV-DOC-01 e a competência do operador master. Implementação: `Solution/scripts/v9-tf4-e2e-closeout.py`; regressão: `Solution/tests/scripts/test_v9_independent_test_report_only.py`. Integração condicionada à CI e merge da PR.
