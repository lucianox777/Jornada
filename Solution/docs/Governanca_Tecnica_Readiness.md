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

## Readiness estatístico do linkage

`config/hml/linkage-statistical-readiness.json` é o contrato fail-closed que impede que a política HML legada de blocking/transportabilidade seja interpretada como fechamento estatístico da issue #31. O arquivo distribuído nasce `PENDENTE` e exige declaração explícita das evidências de corpus representativo, qualidade da verdade de referência, seleção/não resposta, avaliação independente, incerteza amostral, proveniência de pesos, dependência multievidência, concordância replicada quando aplicável e validação de escala.

Para os relatórios implementados nas fatias técnicas da #31, o gate fixa somente a identidade da versão do contrato (`LINKAGE_INDEPENDENT_RESOLUTION_EVALUATION_V1`, `LINKAGE_INDEPENDENT_RESOLUTION_SURVEY_V1`, `LINKAGE_INDEPENDENT_RESOLUTION_GOVERNED_SURVEY_V1`, `CANDIDATE_EVIDENCE_DEPENDENCY_DIAGNOSTIC_V1`, `CANDIDATE_REFERENCE_AGREEMENT_DIAGNOSTIC_V1` e `LINKAGE_SCALE_EVIDENCE_V1`) e exige fingerprint/evidência. Ele não cria valores mínimos, não escolhe fórmula de ponderação, não decide se seleção/não resposta existem e não define taxa aceitável de discordância, dependência ou desempenho.

A evidência `LINKAGE_SCALE_EVIDENCE_V1` preserva o SHA Git do código exercitado e o `runtimeScope` já congelado pelo próprio `linkage_run`, incluindo identidade do modelo e, quando o blocking usa `RULESET`, versão/fingerprint do ruleset e versão/fingerprint da projeção física. Isso torna a medição auditável sem transformar o harness sintético em prova de representatividade e sem criar um threshold de capacidade.

`NAO_APLICAVEL` só é aceito nos pontos em que a metodologia institucional pode legitimamente não exigir aquele mecanismo e sempre exige justificativa mais evidência. Mesmo quando o contrato chega a `APROVADO`, `productionActivationAuthorized` permanece obrigatoriamente `false`: a aprovação desse arquivo significa apenas que o conjunto de evidências da #31 foi formalmente atestado para HML, não autorização automática de Produção.

## Readiness HML

`hml-readiness-gate.sh/.ps1` agora exige, em conjunto:

1. ambiente mínimo;
2. governança técnica aprovada;
3. scheduler configurado;
4. parâmetros e calibrações HML vigentes;
5. baseline de desempenho;
6. avaliação legada de blocking/transportabilidade de linkage;
7. readiness estatístico/institucional da issue #31 aprovado e atestado;
8. evidência SQL de Query Store/waits/deadlocks;
9. evidência da API em lotes de 1, 10, 100 e 1000 UUIDs.

Um contrato `PENDENTE` é válido para distribuição e desenvolvimento, mas não satisfaz o modo estrito de homologação.
