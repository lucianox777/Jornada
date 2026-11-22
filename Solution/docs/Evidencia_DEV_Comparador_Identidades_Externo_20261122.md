# Evidência DEV — comparação independente de conjuntos de identidades (22/11/2026)

## Escopo
O script `Solution/scripts/e2e-independent-identity-oracle.py` compara **offline** as identidades esperadas de um JSON externo (`zipSha256`, `expectedIdentities`) com identidades observadas exportadas separadamente (`zipSha256`, `observedIdentities`). Exige SHA igual, identidade única e contrato estrito; produz recibo PASS/FAIL com ausências e excedentes. CI executa nove testes de contrato.

## Limitações explícitas
Não é integração com Runner/Gold, não extrai resultados reais do sistema, não prova qualidade de linkage probabilístico, não compara pares, não substitui TEST científico independente e não certifica V9 produtivo. O verificador de fronteira ZIP existente continua sendo requisito separado para assegurar que a verdade não entrou no payload HTTP. Nenhuma execução física 1k/10k, nenhum TRAIN/TEST, nenhuma alteração no calibrador/selector/scorer.

## Precedência e rastreabilidade
Plano DEV E2E integral aprovado, frente B; preparação sintética #1851; reversão TF4 integrada #1879; issue #1865 encerrada por calibração insatisfatória. O run #45002707113 permanece histórico, não é base de certificação para esta entrega. Esta evidência não cria decisão de PO (GOV-DOC-01).
