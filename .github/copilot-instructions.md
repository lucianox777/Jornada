- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

- @Jornada Regra obrigatória de decisões - Toda decisão tomada ou confirmada em discussões do Projeto Jornada, inclusive conversas de chat, deve ser registrada em documento versionado no GitHub antes de declarar o trabalho concluído. Aplicar a política GOV-DOC-01 em `Solution/docs/Politica_Registro_Decisoes_Discussoes.md` e as instruções em `AGENTS.md`; atualizar o documento temático e os índices pertinentes. Não tratar conversa/PR/issue como substitutos, nem confundir decisão com implementação, validação ou ativação.

- @Jornada Protocolo de correção e CI - Aplicar `Solution/docs/Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md`: evidência -> reprodução -> correção -> regressão -> checks rápidos PR -> **squash merge** -> CI/30k master e evidência. Não executar bundle Windows ou validação independente em PR/branch. 100k físico somente manual owner-only. GOV-DOC-01 permanece obrigatória.
