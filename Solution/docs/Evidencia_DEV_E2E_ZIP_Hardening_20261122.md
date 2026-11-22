# Evidência DEV — endurecimento da fronteira ZIP (22/11/2026)

## Contexto
Continuação da frente B (#1851), após PR #1867. A fronteira entre ZIP HTTP e gabarito externo já verifica digest, caminhos e duplicidades.

## Alteração
Rejeitar entradas ZIP criptografadas e links simbólicos Unix antes de ler conteúdos, mantendo o recibo sem identidades e o escopo `BOUNDARY_ONLY_NOT_LINKAGE_ORACLE`.

## Justificativa e impactos
Impede que conteúdo não regular atravesse o preflight como arquivo comum. Compatibilidade: ZIP sintético com arquivos regulares continua aceito. A verificação não substitui validação do contrato de ingestão nem oráculo independente de scores, abstenção e clusters.

## Verificação e limites
Testes Python adicionados para symlink e flag de criptografia. A execução CI deve ser verificada na PR; nenhum E2E HTTP/SQL físico foi executado nesta entrega. Sem TRAIN/TEST, promoção, HML ou PROD.

## Precedência e rastreabilidade
GOV-DOC-01 e `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md` prevalecem. Issue #1851; PR #1867; branch `test/e2e-truth-zip-hardening-20261122`.
