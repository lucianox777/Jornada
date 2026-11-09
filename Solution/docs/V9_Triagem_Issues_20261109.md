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

## Leitura individual — primeiro lote de P1 (corpos oficiais, 09/11/2026)

| Issue | Evidência documental confirmada | Classificação após leitura | Verificação pendente |
| --- | --- | --- | --- |
| #1345 | Exige teste geral E2E a–m no SHA exato da master; histórico após #1343 identificou 2 falhas de reflection em 2.155 testes e falha de source sanity. | **P1 — requisito explícito de fechamento** | Correlacionar remediação, CI, 30k e matriz final no SHA final. |
| #1302 | Incidente OOM físico run 42283035832 (exit 134); decisão RAM-first sem spill, sem mudar resultados. | **P1 — risco operacional histórico, bloqueio atual NÃO CONFIRMADO** | Conferir prova física após #1300, pico RAM e cardinalidade. |
| #1257 | Risco estatístico de comparação TF×TF0 sob `AbstainCandidate`; auditoria por slot não equivale a união por par. | **P1 — risco estatístico não certificado** | Confirmar auditoria de pares e sensibilidade, sem inferir FDR/recall. |
| #1089 | Decisão de Calibrador C# único, engenharia reversa, snapshot ref e bootstrap; nenhuma ativação sem autorização. | **P1 — contrato arquitetural; conclusão não demonstrada** | Mapear componentes C# e snapshot versionado; separar dívida futura. |
| #999 | Ensaio histórico sem TF reportou 787 FP/8.571 aceitos (9,1821%), 43,1581% recall; resultado não aceito para ativação. | **P1 — alerta histórico científico, não prova do estado atual** | Confrontar ensaio físico V9 posterior no mesmo frame e TEST cego autorizado. |
| #946 | V9 arquitetura-alvo de novos modelos nominais, níveis compostos e TF da mesma unidade; V8 permanece para replay. | **P1 — contrato estatístico vigente, conclusão não demonstrada** | Inspecionar níveis, m/u, dependência e cobertura do código integrado. |

**Não se concluiu que qualquer P1 esteja resolvida, duplicada ou seja P0 ativo apenas com o corpo da issue.** A leitura é evidência de escopo e risco, não resultado de teste.
