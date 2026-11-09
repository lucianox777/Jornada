# Triagem inicial das issues abertas — Jornada V9 (09/11/2026)

**Escopo:** classificação preliminar por título e pertinência ao mandato, não verificação de implementação. **Não houve fechamento de issues.** Consulta GitHub retornou **55 issues abertas**, sem contar PRs; o número é uma fotografia, não meta de redução. **Revisão individual de corpo, comentários e commits permanece pendente.**

**P1 = analisar antes do fechamento** (não significa que todas sejam defeitos impeditivos): #1345, #1344, #1302, #1292, #1257, #1253, #1228, #1089, #999, #946, #944, #1180.

**P2 = backlog/execução posterior, salvo evidência de bloqueio**: #1349, #1348, #1301, #1275, #1260, #1209, #1200, #1194, #1186, #940, #903, #861, #613, #612, #539, #506, #503, #501, #500, #497, #496, #494, #449, #434, #424, #416, #414, #413, #411, #409, #408, #407, #405, #404, #402, #401, #393, #392, #379, #378, #93, #79, #31.

## Critérios de triagem

- **P0 confirmado** somente após evidência de defeito impeditivo em requisito obrigatório no SHA final. O prefixo `P0` no título não comprova que o defeito persiste.
- **P1:** relação direta com teste a–m, segurança, integridade, V2 testemunha, regressão ou documentação necessária ao fechamento.
- **P2:** escala adicional, HML/produção, otimização, novas funcionalidades e backlog não impeditivo, sujeitos à reclassificação após leitura individual.
- **Resolvida/duplicada:** somente com vínculo verificável para merge/commit/teste; não presumir pelo título.
- **#1344:** oito PDFs/51 ocorrências têm diferimento editorial anterior do PO; não converter automaticamente em bloqueio técnico.
- **#1348/#1349:** lacunas de 10k/100k HTTP e motor V9 100k; não confundir com ensaios concluídos em 1k HTTP ou 100k V8.
- **#378:** referência institucional PRODAM em HML/PRD requer contexto; não remover sem avaliar obrigação externa.
- **#1302, #1257, #1089, #999, #946, #944:** títulos críticos, mas situação real exige inspeção e prova; não declarar resolvidas ou falhas.

## Próxima etapa obrigatória

Ler corpo/comentários das P1, cruzar com SHA master e evidências, classificar P0/P1/P2/resolvida/duplicada definitivamente, registrar links e justificativas, e só então considerar encerramento individual. Esta triagem preliminar **não satisfaz sozinha** o requisito de triagem completa do mandato.

**Referência:** [GOV-V9-FECHAMENTO-20261109](Decisao_V9_Fechamento_Mandato_20261109.md), [matriz a–m](V9_Fechamento_Checkpoint_20261109.md).
