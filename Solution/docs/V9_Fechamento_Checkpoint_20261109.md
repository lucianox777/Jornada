# Jornada V9 — checkpoint auditável do fechamento (09/11/2026)

**Natureza:** inventário de evidências e lacunas; não é teste físico executado nesta etapa, nem atestado de aprovação. **Base observada da PR documental #1353:** `afbefd4c47aa217b83465364548499e400c8ed2a`. O SHA final da master deve ser verificado novamente antes do aceite.

## Verificações realizadas nesta etapa

- PR documental #1353 HEAD `43159811d3216df69b2c22796b1011b17337db4b`: `jornada-pr-required-fast` run **42547941042 SUCCESS**. **Não equivale a `jornada-ci` nem 30k da master.**
- Consulta GitHub de PRs abertas retornou **#1352 e #1353**. **#1351 não apareceu entre PRs abertas**; seu estado real deve ser verificado antes de afirmar que está em draft ou congelada. Nenhuma PR foi fechada/mesclada por este checkpoint.
- A especificação de teste geral em [Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md](Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md) exige provas por SHA, job, artifact e critérios próprios para a–m. Evidências de `c6451cf2` são históricas e não constituem aceite no SHA final.
- O índice já registra o ensaio HTTP 1k run 42541173969 e a prova SCALE 100k run 42534643000 **com motor operacional V8**. Não atribuir a última ao motor V9.

## Matriz a–m — checkpoint de evidências, não execução

| Item | Requisito | Estado deste checkpoint | Próxima prova exigida |
| --- | --- | --- | --- |
| a | CI completa | **NÃO VERIFICADO** no SHA final | `jornada-ci` completa, jobs obrigatórios e SHA |
| b | Bundle Windows | **NÃO VERIFICADO** no SHA final | workflow e artifact instalável |
| c | Cluster/topologia | **NÃO VERIFICADO** no SHA final | smoke real, logs e topologia |
| d | E2E API/Console/SQL | **NÃO VERIFICADO** no SHA final | runs E2E no SHA final |
| e | Ingestão, ledger, API, Policy | **PARCIAL HISTÓRICO**: HTTP 1k executado | matriz funcional positiva/negativa e contagens |
| f | Migração SQL/inventário | **NÃO VERIFICADO** no SHA final | upgrade, replay, inventário e hash |
| g | Validação independente | **NÃO VERIFICADO** no SHA final | workflow e contrato de validação |
| h | 30k físico seed42 | **NÃO VERIFICADO** no SHA final | run físico completo, SHA, artifact |
| i | V2/V3/TF/misturas | **NÃO VERIFICADO** no SHA final | relatórios técnicos e hashes |
| j | Comparação histórica | **NÃO VERIFICADO** no SHA final | comparação analítica do mesmo recorte ou NÃO COMPARÁVEL |
| k | V2 WITNESS+ANCHOR | **NÃO VERIFICADO** no plano oficial atual | parâmetros históricos explícitos, mesma amostra, CP só candidatas |
| l | Segurança/copyright/probe | **NÃO VERIFICADO** no SHA final | gates, licença e gatilho push-only |
| m | Documentação/fechamento | **PENDENTE**: #1353 draft; #1352 draft preservada | exceções aprovadas, inventário, relatório e status de PRs |

**Regra:** NÃO VERIFICADO não significa FAIL; tampouco pode ser transformado em PASS por inferência de run antigo.

## Pendências operacionais priorizadas

1. Determinar o SHA atual da master e cruzar `jornada-ci`, 30k e demais workflows por SHA; anexar evidências por item.
2. Confirmar estado real da #1351 antes de declarar congelamento formal; preservar #1352 em draft.
3. Fazer triagem das issues abertas com critérios P0/P1/P2/resolvida/duplicada, sem fechamento artificial.
4. Auditar contextualmente as 31 referências PRODAM da especificação v5.00; manter exceções históricas e diferimento editorial dos oito PDFs.
5. Conferir V2 WITNESS+ANCHOR no `plan.json` oficial com todos os parâmetros históricos; geração transitória sozinha não substitui decisão vigente.
6. Elaborar relatório final somente após as provas. **Estado atual: NÃO CONCLUÍDO.**

**Precedência:** [GOV-V9-FECHAMENTO-20261109](Decisao_V9_Fechamento_Mandato_20261109.md); [mandato a–m](Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md); [índice](Indice_Decisoes_Vigentes.md). GOV-DOC-01.

## Auditoria documental de V2 WITNESS+ANCHOR — segundo checkpoint

Consulta ao conteúdo indexado do repositório identificou:

- `Decisao_V9_Mandato_Operacional_Frentes_07_11_2026.md`: decisão explícita do PO exige V2 com **todos os parâmetros históricos exatos e explícitos** no `plan.json` oficial, papel WITNESS+ANCHOR, mesmo recorte físico, fora da seleção e da multiplicidade CP.
- `Decisao_V9_Compatibilidade_Opcional_Parametros_20261108.md`: contrato de compatibilidade determina `presente = valor do plan.json`, `ausente = default histórico`, `desconhecido = recusa`; não revoga V2 explícita em estudos J que a declarem. **A permissão de omissão para plano legado não prova conformidade do plano científico J.**
- `Indice_Decisoes_Vigentes.md`: descreve proposta #1337 com 24 braços e CP candidata 16 e registra expressamente que faltam valores V2 históricos, paridade no mesmo SQL e prova física.
- Busca textual indexada `path:plan.json WITNESS` não retornou resultados. **Ausência de resultado na busca não prova inexistência de arquivo**; é necessário localizar o plano por árvore/artefatos e conferir valores e SHA.

**Resultado de k:** **NÃO VERIFICADO**, não PASS. **Ação exigida:** extrair o `plan.json` científico efetivamente executado (arquivo e SHA), comparar campo a campo com builder V2 histórico autenticado, confirmar WITNESS+ANCHOR, frame idêntico e multiplicidade CP apenas de candidatas; publicar diferença e teste reprodutível.
