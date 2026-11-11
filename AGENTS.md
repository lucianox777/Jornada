# Instruções permanentes para agentes e colaboradores — Jornada

## Registro obrigatório de decisões (decisão do PO, 2026-11-07)

Toda decisão de produto, arquitetura, implementação, algoritmo/calibração, CI, segurança, operação, testes, escopo ou governança tomada ou confirmada nas discussões do projeto **deve ser documentada nos documentos versionados deste repositório GitHub**, mesmo quando a discussão ocorrer fora do GitHub (por exemplo, em chat). A conversa, por si só, **não é repositório canônico de decisões**.

**Antes de considerar concluído o trabalho decorrente da decisão**, registrar a decisão no documento temático pertinente em `Solution/docs/` ou `Documentos/`, atualizar `Solution/docs/Indice_Decisoes_Vigentes.md` quando afetar a precedência/vigência, e apontar a documentação na PR ou no relatório da atividade. Para decisão sem alteração de código, usar mudança exclusivamente documental. Não confiar apenas em descrição de PR, issue, commit ou histórico de chat.

A política detalhada e o formato mínimo estão em [Solution/docs/Politica_Registro_Decisoes_Discussoes.md](Solution/docs/Politica_Registro_Decisoes_Discussoes.md). Preservar a distinção entre **DECIDIDO / IMPLEMENTADO / MEDIDO / VALIDADO / ATIVADO**, a hierarquia documental e a imutabilidade de artefatos de release selados. Propostas, hipóteses e discussões sem aprovação explícita **não** devem ser lançadas como decisões aprovadas.

Esta instrução vale para toda futura atividade de desenvolvimento, documentação, revisão, merge e comunicação de status no Projeto Jornada.

## Correções, squash merge e separação de CI (PO, 2026-11-11)

Aplicar o protocolo [GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111](Solution/docs/Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md): diagnosticar e reproduzir falhas antes de corrigir; testes e checks rápidos no HEAD de PR; integrar por **squash merge**; executar instalador Windows e validação independente somente na master, com controles de SHA e CI/30k pós-merge. Nenhum 100k automático. Este é um apontador; consultar o documento canônico e a GOV-DOC-01, sem duplicar integralmente as regras.
