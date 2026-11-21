# GOV-V9-LIBERACAO-POS-MERGE-20261121 — autorização do PO condicionada à integração

**Data:** 21/11/2026. **Autoridade:** Product Owner (PO), manifestação: “Depois desse merge aí, a liberação tá liberada.” **Estado:** AUTORIZAÇÃO EXPRESSA DO PO REGISTRADA; comprovação dos gates técnicos e científicos ainda pendente. **Política:** GOV-DOC-01.

## Contexto e alcance

Após o squash merge da PR #1817 (correção do E2E DT-05) na master, commit `cd20ace78d3af8b633a3d77007bed031783a0acc`, o PO manifestou concordância com a liberação subsequente. A PR #1817 teve quatro checks obrigatórios e o teste sintético Three Waves aprovados no HEAD da PR. A expressão “liberação” não especifica um comando de execução científica, ambiente ou campanha; por segurança, esta decisão registra autorização de prosseguir com o processo de liberação V9 **após o merge**, sem presumir autorização de executar TRAIN, VALIDATION, TEST, 100k, publicar ou ativar modelos automaticamente.

## Justificativa e efeitos

A correção DT-05 eliminou a falha de configuração do Runner no teste sintético, preservando a seleção explícita do modelo. A manifestação do PO remove a necessidade de solicitar novamente concordância genérica com o avanço da liberação após a integração, mas não equivale à comprovação dos critérios de aceite. Não declarar PASS ou liberar uso científico/produtivo sem as evidências correspondentes.

## Precedência, salvaguardas e pendências

Permanecem vigentes GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111, GOV-DOC-01, gates científicos LOCKED e proteção da master. Verificar no SHA integrado `jornada-ci` e `v9-physical-seed42-probe` segundo o classificador, digest independente do gerador LOCKED e critérios da issue P0-A #1734. A falta de visibilidade de runs pós-merge não constitui sucesso nem falha. Nenhum bypass de CI, execução científica, TEST/Gold, 100k ou ativação é autorizado por este registro. Se a intenção do PO for autorizar uma execução específica, registrar escopo e parâmetros antes da ação.

## Rastreabilidade

- PR #1817: https://github.com/lucianox777/Jornada/pull/1817
- Issue P0-A #1734: https://github.com/lucianox777/Jornada/issues/1734
- Runs aprovados na PR: required-fast #44867393618; Synthetic Bronze to Runner #44867393709.
- Commit de integração: `cd20ace78d3af8b633a3d77007bed031783a0acc`.
