# Auditoria incremental — issue #946 e política de PRs encerradas (21/11/2026)

## Regra operacional aprovada pelo PO
**PRs fechadas (merged ou closed sem merge) não serão reabertas.** Podem ser citadas como evidência histórica por número, estado e commit; não devem voltar à fila de implementação nem receber alterações de escopo. Analisar apenas issues abertas, PRs abertas e código atual da master; consultar diffs antigos somente quando indispensável à rastreabilidade de uma lacuna, sem reabrir PR. Não confundir issue aberta com PR fechada.

## Inventário revisado
- #1147, #1491, #1627, #1655, #1820 e #1822: entregas históricas já integradas; não reabrir.
- #1654: **PR fechada**; #1712 também fechada. Documento anterior `V9_Auditoria_Integracao_Runner_Beta_20261118.md` menciona #1654 como aberta, mas o estado atual consultado em 21/11 é **closed**. Não usar a afirmação histórica como status atual, nem reabrir #1654.
- #1836: PR documental aberta (draft), única PR modificada nesta auditoria.
- Issues #946, #1089, #1488, #1584, #1349, #1348, #1478 e #1575: abertas na verificação realizada; analisar separadamente seus critérios atuais.

## Próxima issue ainda não auditada em detalhe: #946
**Título:** P0 V9: substituir representação nominal TF da V8 por comparação composta calibrada com nome integral.

**Requisitos documentados:** nomes integrais de pessoa/mãe, um nível nominal composto mutuamente exclusivo por campo, m/u condicionado a blocking e dependências, TF somente por unidade estatística compatível, comparação independente C#×Splink, TRAIN/VALIDATION/TEST e gates DT-15. V8 fica como legado; V9 é arquitetura-alvo de novos modelos nominais.

**Código existente observado:**
- `Solution/src/Jornada.Contracts/BrazilianNameComponents.cs`: extrator de componentes nominais.
- `Solution/src/Jornada.Contracts/CompositeNominalComparisonV9.cs`: comparador nominal composto.
- `Solution/src/Jornada.Linkage.Core/V9NominalTfTermCoverage.cs`: cobertura de termos TF V9.
- `Solution/src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs`: scorer/Resolve de coorte V9 congelada (diagnóstico, não batch produtivo).

**Status rigoroso:** fase 1 marcada concluída no corpo da #946 e PR #947 histórica integrada; fases 2–5 permanecem desmarcadas no corpo da issue, cuja última atualização substantiva descreve estado de 12/10. Isso **não prova** que todo o trabalho posterior esteja ausente: código e PRs mais recentes avançaram o scorer e artefato, mas não certificam a totalidade dos critérios estatísticos ou DT-15. Não marcar fases 2–5 como concluídas sem mapa de evidências por critério e SHA.

**Separação de responsabilidade:** #946 define semântica e validação estatística V9; #1089 implementa Calibrador e exportação; #1488 contrato e seleção MASTER; #1584 batch Runner V9; #1349 escala multiparâmetros; #1478 gates TEST. Não duplicar adaptações do Runner na #946.

## Próxima verificação técnica
Mapear cada critério da #946 a classes, testes e evidência de integração no SHA atual; conferir a PR #1836 e integrá-la somente após checks. Depois, avançar à issue aberta #1349 para avaliar evidência física multiparâmetros, sem executar campanha sem autorização.
