# V9 — Correção de execução TF4 para termos IBGE não publicados (2026-11-19)

## Contexto e evidência
A campanha 10K GitHub Actions run 44455634678, SHA 624a564e4c79abb7808071db0bdfe88d30537fe2, concluiu ADMISSION, PREPARE, TRAIN (324/324), AUDIT e VALIDATION. O TEST falhou no passo "Apply one frozen winner to TEST" com `System.IO.InvalidDataException: TF positiva sem frequência IBGE publicada para unidade ativa.`, em `V9FittedSnapshot.CreateTf`. O closeout publicou recibo de execução incompleta, não TEST científico concluído.

## Correção e justificativa
A política já existente `AbstainCandidate=1` evita pontuar ou aceitar candidato com frequência IBGE não publicada, sem inventar frequência e preservando o denominador original de candidatos. A geração de plano TF4, quando recebia a configuração unificada `unpublishedTermPolicy=0`, propagava indevidamente `RejectOperation=0` às 80 opções TF4 ativas, embora o gerador sem configuração já usasse abstenção. A correção aplica `AbstainCandidate=1` às opções TF4 e preserva o controle histórico TF0 com `RejectOperation=0`, os parâmetros de população, pesos, 324 hipóteses e critérios científicos existentes. O auditor reconstrói a grade por meio do mesmo gerador e rejeita divergências. Teste de regressão cobre ambas as políticas.

## Limites, precedência e rastreabilidade
Não alterar retrospectivamente o SHA ou o vencedor congelado da campanha 10K, nem afirmar que seu TEST foi medido. A execução 100K #44458551807 já iniciada também não recebe alterações retroativas. Esta correção só vale para campanhas futuras que utilizem um novo SHA após integração e aprovação dos gates. A integridade do TEST e a proibição de promoção automática permanecem inalteradas. Implementação: `Solution/scripts/v9-v3-four-name-100k-plan.py`; teste: `Solution/tests/scripts/test_v9_tf4_unpublished_policy.py`. Referências: GOV-DOC-01 e contratos V9 de abstenção por candidato.
