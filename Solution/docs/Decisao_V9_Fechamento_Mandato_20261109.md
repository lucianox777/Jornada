# GOV-V9-FECHAMENTO-20261109 — Execução do mandato final sem expansão

**Autoridade:** Product Owner, confirmação expressa de execução em 09/11/2026 (conversa do projeto). **Estado:** DECIDIDO; execução e provas ainda pendentes. **Política:** GOV-DOC-01.

## Contexto e precedência

Após integração #1343 e provas #1347 (100k operacional V8, não motor multiparâmetro V9) e #1350 (HTTP 1k), o PO aprovou o fechamento do mandato original antes de novas expansões. Esta determinação é superveniente à dispensa histórica da triagem do passo 8 em GOV-V9-FINAL-20261108-B-AD01: **a triagem volta a ser obrigatória no fechamento**, sem fechamento indiscriminado de issues. Não reinterpreta medições, gates ou autorização científica anteriores. O trabalho posterior de calibrador exploratório #1352 foi autorizado expressamente pelo PO, mas fica fora do caminho crítico.

## Decisões vinculantes

1. **#1345:** fechar matriz de testes gerais a–m no SHA final, com PASS/FAIL/NOT RUN/BLOCKED, runs, evidências e lacunas; ausência de prova não é PASS.
2. **#1351:** congelar desenvolvimento da rampa HTTP em draft, preservar branch e evidências, sem merge no fechamento.
3. **#1352:** preservar em draft, sem merge; opção exploratória autorizada, mas não integra a entrega de fechamento; nenhuma alteração da REF/plano operacional.
4. **Workflows manuais:** manter escala média e HTTP 1k como `workflow_dispatch`, documentando finalidade e sem alterar a política de 30k físico.
5. **Issues:** triagem com justificativas, separando impedimentos, requisitos de fechamento, melhorias e duplicidades; não fechar por meta numérica.
6. **PRODAM:** revisar as 31 menções informadas na especificação v5.00, neutralizando dependências indevidas e preservando contexto histórico/contratual justificável. Não confundir com a remediação editorial dos oito PDFs, diferida anteriormente.
7. **V2 testemunha:** verificar requisito vigente de V2 WITNESS+ANCHOR com todos os parâmetros históricos explícitos no plano oficial, mesmo recorte, não elegível e fora do CP. Geração determinística com hash é evidência útil, **mas não substitui por si só** o requisito textual expresso de plano oficial com parâmetros completos.
8. **Relatório final:** consolidar SHA, matriz a–m, triagem, referências PRODAM, V2, gates, limitações e conclusão CONCLUÍDO / COM RESSALVAS / NÃO CONCLUÍDO.

## Controles de execução

- Não abrir campanhas 10k/100k HTTP ou 100k V9 só por expansão; registrar a lacuna #1349 separadamente.
- Não inventar resultados, dispensar checks ou confundir sucesso em SHA anterior com prova no SHA final.
- PRs de correção somente para defeitos impeditivos, sem escopo paralelo; preservar a integridade dos bancos DEV e do TEST congelado.
- Fechamento somente após documentação integrada à master, gates aplicáveis no SHA final e evidências auditáveis.

## Inventário inicial de lacunas (não é atestado de execução)

| Frente | Estado na abertura | Evidência necessária |
| --- | --- | --- |
| Teste geral #1345 | Aberta | Matriz a–m completa no SHA final |
| Escala V9 100k #1349 | Lacuna declarada | Não usar prova V8 como V9 |
| HTTP #1351 | Draft congelada por decisão | Sem merge no fechamento |
| Exploratório #1352 | Draft preservada | Sem merge ou ativação |
| Triagem | Pendente | Classificação rastreável de issues |
| PRODAM v5.00 | Pendente | Revisão contextual de 31 menções |
| V2 WITNESS+ANCHOR | Verificação pendente | Plano oficial completo e hashes |
| Relatório final | Pendente | Resultado baseado em runs do SHA final |

**Referências:** [mandato anterior](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md), [pré-auditoria a–m](Teste_Geral_V9_a_m_Preflight_20261108.md), [índice vigente](Indice_Decisoes_Vigentes.md), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).
