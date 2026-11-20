# Instruções permanentes para agentes e colaboradores — Jornada

## Registro obrigatório de decisões (decisão do PO, 2026-11-07)

Toda decisão de produto, arquitetura, implementação, algoritmo/calibração, CI, segurança, operação, testes, escopo ou governança tomada ou confirmada nas discussões do projeto **deve ser documentada nos documentos versionados deste repositório GitHub**, mesmo quando a discussão ocorrer fora do GitHub (por exemplo, em chat). A conversa, por si só, **não é repositório canônico de decisões**.

**Antes de considerar concluído o trabalho decorrente da decisão**, registrar a decisão no documento temático pertinente em `Solution/docs/` ou `Documentos/`, atualizar `Solution/docs/Indice_Decisoes_Vigentes.md` quando afetar a precedência/vigência, e apontar a documentação na PR ou no relatório da atividade. Para decisão sem alteração de código, usar mudança exclusivamente documental. Não confiar apenas em descrição de PR, issue, commit ou histórico de chat.

A política detalhada e o formato mínimo estão em [Solution/docs/Politica_Registro_Decisoes_Discussoes.md](Solution/docs/Politica_Registro_Decisoes_Discussoes.md). Preservar a distinção entre **DECIDIDO / IMPLEMENTADO / MEDIDO / VALIDADO / ATIVADO**, a hierarquia documental e a imutabilidade de artefatos de release selados. Propostas, hipóteses e discussões sem aprovação explícita **não** devem ser lançadas como decisões aprovadas.

Esta instrução vale para toda futura atividade de desenvolvimento, documentação, revisão, merge e comunicação de status no Projeto Jornada.

## Correções, squash merge e separação de CI (PO, 2026-11-11)

Aplicar o protocolo [GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111](Solution/docs/Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md): diagnosticar e reproduzir falhas antes de corrigir; testes e checks rápidos no HEAD de PR; integrar por **squash merge**; executar instalador Windows e validação independente somente na master, com controles de SHA e CI/30k pós-merge. Nenhum 100k automático. Este é um apontador; consultar o documento canônico e a GOV-DOC-01, sem duplicar integralmente as regras.

## Execução de cada interação com IA e inventário acumulativo de CI (PO, 2026-11-19)

Aplicar o [aditivo GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119](Solution/docs/Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md): em **cada interação**, consultar estado vivo do GitHub, manter inventário deduplicado de runs/jobs por estado e SHA, investigar falhas individuais e comparar transições, desenvolver em paralelo, validar PR e pós-merge e documentar decisões segundo GOV-DOC-01. Se a ferramenta não fornecer listagem global paginada, declarar inventário **parcial**, nunca inventar totais. Não prometer vigilância entre mensagens sem automação confirmada; não contornar gates ou autorizações científicas.

**Mandato operacional específico do Jornada e retomada entre conversas:** [Mandato_IA_Ciclo_Desenvolvimento_Continuo.md](Solution/docs/Mandato_IA_Ciclo_Desenvolvimento_Continuo.md) operacionaliza o aditivo canônico de 13 etapas. Em toda conversa nova ou retomada, recuperar o estado real da `master`, índice de decisões, PR da frente de trabalho, checkpoint existente e CI antes de agir. Reutilizar PRs e workflows existentes; não criar monitor, persistência paralela ou PR substituta apenas por perda do contexto. O documento genérico anterior foi substituído nesta PR #1757, ainda pendente de integração. O mapa das 13 etapas no mandato é **não normativo**: executar o texto integral do aditivo canônico (inclusive estados, paginação, checkpoint, gates pós-merge e limites), não o resumo. Decisões aprovadas e integradas, com precedência documentada, devem ser respeitadas; documentos apenas em PR não são vigentes na `master`. Ler o mandato junto ao protocolo canônico e GOV-DOC-01.

## Recuperação pós-CI por Diagnostics — ratificação PO, 20/11/2026

Em cada interação operacional, após consultar o estado vivo, usar o `jornada-ci-diagnostics` correspondente ao `jornada-ci` e SHA integrado como entrada prioritária do pós-merge; confirmar `run_id`, `run_attempt`, SHA, jobs originais e demais gates exigíveis conforme [mandato Jornada](Solution/docs/Mandato_IA_Ciclo_Desenvolvimento_Continuo.md) e protocolo canônico de 13 etapas. Uma PR integrada já está fechada como `merged`; não aguardar Diagnostics para um segundo fechamento nem excluir branch automaticamente. Falha pós-CI gera incidente e correção em PR apropriada, seguida de novo squash. Não usar apenas o último diagnóstico, não criar controles paralelos e não presumir vigilância autônoma entre conversas. Decisão GOV-OPS-DIAGNOSTICS-RETOMADA-POSCI-20261120, documentação pendente de integração.

