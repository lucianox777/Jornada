# Evidência DEV — validação de caminhos de diretórios no ZIP externo (22/11/2026)

## Contexto e justificativa
Frente B, issue #1851, plano aprovado de E2E integral V9 em DEV. O preflight `e2e-external-truth-boundary.py` já recusava travessia em nomes de arquivos ZIP, mas excluía entradas terminadas em `/` da validação. Isso permitia que uma entrada de diretório `../escape/` escapasse do controle de caminhos.

## Implementação e impactos
A mesma validação de caminhos passa a alcançar arquivos e diretórios, retirando apenas a barra final opcional antes da divisão em segmentos. Entradas `../escape/` são recusadas, enquanto `safe/` permanece permitida. Dois testes offline comprovam os casos negativo e positivo em `test_e2e_external_truth_boundary.py`. Nenhum ZIP real é extraído e nenhum dado pessoal é utilizado.

## Limites e precedência
Correção defensiva de implementação, não nova decisão do Product Owner. Não certifica oráculo de linkage, Runner V9 ou E2E físico; não autoriza TRAIN/TEST, HML/PROD ou alteração de campanha histórica. Precedência: GOV-DOC-01 e `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`.

## Rastreabilidade
Issue #1851; PR de correção `fix/e2e-external-truth-zip-directory-path-1851`. CI obrigatória e integração ao `master` ainda necessárias.
