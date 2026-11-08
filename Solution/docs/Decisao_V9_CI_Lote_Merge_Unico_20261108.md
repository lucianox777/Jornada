# DC-V9-CI-LOTE-20261108 — preparar PRs em lote, integrar uma vez e validar CI pós-merge

**Data / autoridade:** 08/11/2026, Product Owner (PO), esclarecimento de execução no Projeto Jornada.  
**Estado:** DECIDIDO quanto à ordem operacional; **NÃO IMPLEMENTADO / NÃO VALIDADO** quanto ao lote futuro. Este registro documental em branch não constitui merge, CI verde nem fechamento das frentes.  
**Escopo:** reordenação operacional do plano 0–9 mencionado pelo PO, apenas para a integração de correções V9; **não** altera o roteiro de ensaio DEV local e suas etapas numeradas.  
**Base consultada:** `master` `db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54`; [`jornada-ci`](../../.github/workflows/ci.yml), [PR fast](../../.github/workflows/jornada-pr-required-fast.yml), [30k técnico](../../.github/workflows/v9-physical-seed42-probe.yml).  
**Governança:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Contexto e justificativa

O PO quer corrigir, reconciliar e reunir os trabalhos pendentes **antes de integrar à `master`**, evitando CI completa redundante em cada PR intermediária. A decisão anterior de 06/11, confirmada em 07/11, já determina checks rápidos em PR e CI completa mais 30k técnico **após o merge**, em `push` da `master`. Esta decisão nova **mantém essa topologia**, mas consolida as mudanças em uma única integração final quando tecnicamente viável.

## Sequência vinculante para este lote

1. Corrigir o código, resolver conflitos, revisar dependências e manter as alterações nas branches/PRs de trabalho **sem merge à `master`**. Preservar rastreabilidade individual por PR, commit e issue.
2. Consolidar o conteúdo aprovado dessas frentes em uma **branch de integração** e expor o conjunto numa PR final direcionada à `master`. A integração entre branches de trabalho **não equivale** a merge na `master`; cada merge na `master` cria novo `push` e pode iniciar novas CI/30k.
3. Antes do merge final, cumprir obrigatoriamente os **checks rápidos requeridos** (`unit-runtime`, `deterministic-build`, `security-guards`) e impedir a integração de bloqueios graves conhecidos. A execução manual duplicada da **CI completa** não é etapa adicional obrigatória. Reconciliar a branch final com a `master` atual, evitando alterações novas que escapem aos checks.
4. Efetuar **um único merge final** da PR consolidada na `master`, desde que a integração seja elegível e os checks exigidos estejam verdes.
5. No `push` da `master`, observar a `jornada-ci` e o classificador/ensaio técnico 30k. A CI completa é iniciada automaticamente pelo push; seu classificador pode modular jobs segundo o impacto. O workflow 30k é iniciado automaticamente, mas o **ensaio físico** só é dispensável se o diff cumulativo for comprovadamente sem impacto; impacto no Calibrador/FS/TF/prior exige execução física. Não equivale a FDR certificada.
6. **Após a CI e o gate 30k aplicável concluídos**, executar a revisão consolidada das issues que o PO denomina **“item 8 do plano original”** e concluir/atualizar somente aquelas para as quais exista evidência verificável. A verificação final de issues fica pós-CI, mas a inspeção pré-merge de impedimentos críticos permanece obrigatória.
7. Não iniciar merges adicionais enquanto o SHA integrado estiver com CI/30k obrigatório falho, cancelado ou pendente. Se forem necessárias correções pós-merge, cada novo `push` gera novo ciclo: **“uma vez” significa uma execução por integração estável, e não dispensa absoluta de reexecuções**.

## PRs e distinção entre consolidar e fechar

A expressão do PO “fechar as PRs” significa encerrar as frentes somente após assegurar sua incorporação à PR consolidada, com evidência de commits/conteúdo. **Fechar uma PR sem merge não leva suas alterações para a `master`**. PRs originais podem ser mantidas como draft/abertas durante a consolidação e depois fechadas como substituídas, com referência cruzada à PR final e sem alegar `MERGED` em cada uma. Não descartar work-in-progress ou direitos de revisão.

## Precedência, limites e impactos

- **Complementa, não revoga:** [CI-V9-30K-PUSH-ONLY](Decisao_V9_Gate_Tecnico_30K_Somente_Push_Master_20261107.md) e a decisão de 06–07/11 (“PR fast, CI completa e regressão 30k no push da master”). Não mudar workflows neste registro.
- **Não elimina:** os checks rápidos de PR, testes físicos específicos exigidos por ciência/migration quando aplicáveis, revisão de segurança, bloqueios de merges ou documentação GOV-DOC-01; campanhas científicas/Gold/TEST e habilitação/DT-15 dependem de autorizações próprias.
- **Economia pretendida:** evitar execuções repetidas de CI completa por sucessivos merges individuais, sem alegar medição de custo ou desempenho.
- **Ponto de atenção:** se o plano “0–9” for outro documento, a renumeração exata depende da referência canônica; a sequência de CI e issues aqui é a intenção operacional aprovada, não uma alteração no roteiro local “Status do repositório” de 1–10.

## Critério de conclusão e rastreabilidade futura

Registrar a lista das PRs originais, commits efetivamente consolidados, SHA final da `master`, run `jornada-ci`, run 30k/classificação e resultado do item 8/issue por issue. Até essa evidência existir, conservar estado **DECIDIDO / NÃO EXECUTADO**. O registro documental deve chegar à `master` pelo fluxo de PR, nunca por escrita direta sem checks.
