# Governança técnica e readiness

A Solution distribui contratos machine-readable para itens que dependem de decisão externa, sem converter defaults de Development em política de Produção.

## Schemas

`config/governance/schema-approvals.json` contém o inventário completo de `config/contracts/**/*.json` e o SHA-256 de cada arquivo. O estado distribuído é `PENDENTE`. Quando um schema for aprovado, sua aprovação deve registrar o SHA exato aprovado, data, responsável e evidência. Alterar um byte do schema invalida o inventário até que a nova versão seja tratada explicitamente.

## Retenção e DR

`config/governance/retention-dr-policy.json` mantém separados:

- retenção detalhada de `ingestao.item_processado`;
- retenção e GC do payload Bronze;
- RPO/RTO, frequência de backup e evidência de restore.

Nenhum valor é inferido. `governance-readiness-gate.py --require-approved` falha enquanto a política não estiver aprovada.

## Identidades que permanecem sem resolução

`config/governance/identity-pending-lifecycle.json` obriga a tratar explicitamente `PENDENTE_PROBABILISTICO`, `NAO_RESOLVIDO` e `CONFLITO`. O contrato não executa alteração automática (`autoMutate=false`): busca ativa, revisão ou encerramento permanecem decisões institucionais.

## Scheduler corporativo

`config/operations/scheduler-jobs.json` inventaria os workers e operações run-once/excepcionais da Fase 1. O arquivo não escolhe ferramenta nem cadência. O gate verifica projetos, operações, dependências e ausência de secrets; `--require-scheduled` exige owner, retry e configuração aprovada.

## Preflight de ambiente

`environment-preflight-gate.py` verifica perfis declarados em `config/release/environment-requirements.json`. O perfil `ci-linux` é usado no CI; `hml-runtime` verifica os pré-requisitos mínimos para executar o readiness HML; `powerbi-authoring` documenta as variáveis e ferramenta necessárias à validação externa do PBIP.

## Readiness HML

`hml-readiness-gate.sh/.ps1` agora exige, em conjunto:

1. ambiente mínimo;
2. governança técnica aprovada;
3. scheduler configurado;
4. parâmetros e calibrações HML vigentes;
5. baseline de desempenho;
6. avaliação de linkage;
7. evidência SQL de Query Store/waits/deadlocks;
8. evidência da API em lotes de 1, 10, 100 e 1000 UUIDs.

Um contrato `PENDENTE` é válido para distribuição e desenvolvimento, mas não satisfaz o modo estrito de homologação.
