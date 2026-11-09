# Pesquisa exploratória V9 — ramo isolado com parâmetros ampliados

**Decisão do Product Owner (09/11/2026):** criar ramo especial para pesquisa exploratória, preservando as decisões normais vigentes e permitindo parâmetros adicionais. **Estado:** ramo criado; ampliação técnica e validações ainda pendentes. **Governança:** GOV-DOC-01.

## Contrato e precedência

1. Este ramo deriva da master e **herda integralmente** as decisões científicas, estatísticas, funcionais, operacionais e de segurança vigentes. Não é fork de regras nem exceção a controles.
2. A pesquisa pode declarar e investigar **parâmetros adicionais** e espaços de busca mais amplos, em artefatos e código próprios do ramo; cada parâmetro precisa de nome, domínio, default neutro/compatível, semântica, proveniência, limites e teste. Os parâmetros históricos não são reescritos e a leitura de opcionais segue GOV-V9-J-COMPAT-20261108.
3. Não alterar REF, TEST, Gold, snapshots, plano operacional congelado, teto FDR aprovado, política CI/30k, elegibilidade ou ativação. Resultados exploratórios são diagnósticos, não confirmação estatística; reportar número de hipóteses, dependências, multiplicidade e incerteza.
4. O plano científico oficial de uma **campanha exploratória específica**, quando executada, deve ser único, versionado e auditável; não criar segundo motor/manifesto concorrente nem misturar resultados com a campanha de fechamento. Promoção a estudo confirmatório requer plano pré-declarado e autorização específica do PO.
5. O ramo não integra a master automaticamente. PR #1352 permanece draft e pode ser usada como referência de implementação; qualquer incorporação de commits deve passar por revisão de diferenças, testes e reconciliação, nunca merge cego.

## Contexto e justificativa

A decisão anterior autorizou ampliar parâmetros exploratórios, mas o PO esclareceu que essa pesquisa **deve ocorrer em ramo especial**, preservando as decisões normais. A separação reduz o risco de alterar o fechamento V9 ou misturar hipóteses exploratórias e evidências confirmatórias.

## Rastreabilidade e impactos

- Ramo: `research/v9-exploratoria-parametros-ampliados`, criado da `master` em 09/11/2026.
- Decisões complementadas: `GOV-V9-J-COMPAT-20261108` e complemento exploratório de `GOV-V9-FECHAMENTO-20261109`.
- Escopo inicial: isolamento arquitetural e documental. **Ainda não foi implementado nem executado um novo espaço paramétrico.**
- Próximos gates: inventário de parâmetros, testes de regressão dos defaults, rejeição fail-closed, proveniência por opção, execução exploratória isolada e relatório auditável.
