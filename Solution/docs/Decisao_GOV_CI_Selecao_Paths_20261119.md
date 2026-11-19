# GOV-CI-PATHS-20261119 — Execução seletiva de SCALE e Cluster

**Decisão aprovada pelo Product Owner em 19/11/2026.** Conforme GOV-DOC-01, workflows pesados de SCALE e Cluster Topology devem ser executados somente quando arquivos relevantes ao seu contrato operacional forem modificados; mudanças exclusivamente documentais em Markdown não devem, por si sós, dispará-los. Alterações mistas preservam os testes pertinentes aos arquivos executáveis. As verificações obrigatórias de proteção da master não podem ser enfraquecidas: qualquer ajuste de gatilhos precisa manter um status obrigatório satisfatório, inclusive quando o teste pesado for dispensado, e preservar `workflow_dispatch` quando já existente.

## Contexto e constatação

Na revisão dos workflows vigentes na master, `.github/workflows/scale-observability-contract.yml` e `.github/workflows/cluster-topology.yml` **já possuem filtros `paths`** em `pull_request` e `push`; ambos excluem Markdown genérico. Portanto, a hipótese inicial de disparo irrestrito por documentação **não se confirmou**. A PR #1740 altera `Solution/scripts/local-scale.sh` (gatilho SCALE) e `Solution/tests/scripts/test_runner_startup_model_gate.py` (gatilho Cluster): **ambos os testes são pertinentes**, mesmo que a PR também altere documentação. Não se deve suprimir esses gatilhos para acelerar esta PR.

## Aplicação e limites

A decisão fica registrada como regra de manutenção da CI; **nenhuma mudança de YAML é necessária nesta PR**, porque o comportamento de seleção solicitado já está implementado para SCALE e Cluster. Uma otimização futura só poderá remover ou restringir caminhos após análise de cobertura e da proteção de branch, com regressões e evidência de não redução dos gates obrigatórios. Documentação Markdown pode ser verificada por controles leves próprios, sem substituir gates obrigatórios. Não altera ciência, parâmetros, campanhas, TRAIN, TEST ou baseline.

## Precedência, impactos e rastreabilidade

Complementa GOV-DOC-01 e a decisão de correção SCALE `Decisao_GOV_CI_Scale_Startup_Modelo_20261119.md`; não substitui os contratos de inicialização do Runner. Arquivos verificados: `.github/workflows/scale-observability-contract.yml`, `.github/workflows/cluster-topology.yml` (master, 19/11/2026). PR #1740: `Solution/scripts/local-scale.sh` e `Solution/tests/scripts/test_runner_startup_model_gate.py`. Impacto imediato: explicitar que as execuções de SCALE/Cluster na PR #1740 são necessárias por alterações executáveis, não pelos arquivos Markdown.
