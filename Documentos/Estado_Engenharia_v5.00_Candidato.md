# Estado de Engenharia — candidato Solution Engenharia v5.00

**Data de consolidação:** 18/09/2026  
**Status:** CANDIDATO TÉCNICO — RELEASE/TAG NÃO CORTADA  
**SolutionSchema corrente:** `3.70`

## 1. Relação com a última release selada

A última release selada continua sendo a **Solution Engenharia v4.05**, com `schema_solution=v3.69` e tag `jornada-solution-v4.05`, conforme `RELEASE_INFO.txt`.

Este documento descreve somente a candidata v5.00. O commit imutável da RC, quando existir, será registrado pelos metadados de candidato/release; este arquivo não congela antecipadamente um SHA mutável de `master`.

## 2. Runtime relacional da candidata

**Microsoft SQL Server é o único runtime relacional suportado pela Jornada candidata v5.00.**

O DDL canônico, o Processor, o Linkage, a coordenação transacional, os testes de integração, o instalador Windows e os gates de promoção usam o contrato Microsoft SQL exercitado em SQL Server 2022 Developer/Testcontainers no desenvolvimento e CI.

O suporte operacional paralelo a PostgreSQL foi retirado desta candidata: não há provider selecionável, adapter Npgsql, persistência de identidade PostgreSQL, calibrador PostgreSQL, DDL PostgreSQL nem gates de paridade PostgreSQL no produto corrente. O histórico Git preserva a implementação anterior para eventual migração a projeto independente; ele não constitui suporte runtime desta Jornada.

## 3. Microsoft Fabric

SQL Database in Microsoft Fabric **não é alvo operacional da candidata v5.00 e não é gate para o corte da RC/release**. Evidências Fabric anteriores permanecem como histórico de compatibilidade técnica.

Lakehouse e SQL Analytics Endpoint permanecem no escopo analítico/compatibilidade e não substituem o banco relacional operacional SQL Server.

Nenhuma hospedagem Fabric autoriza DDL alternativo, branch funcional ou segunda fonte de verdade operacional nesta candidata.

## 4. Linkage e calibração

O caminho operacional SQL Server usa `LinkageParametersWorker` e o modelo de decisão versionado da Jornada. A candidata já contém componentes normativos/experimentais do ADR do calibrador DF → Fellegi–Sunter — busca de thresholds, Pareto, intercâmbio Splink, planejamento de ground truth e política conservadora de agrupamento — porém **esses componentes ainda não formam um fluxo SQL Server ponta a ponta que produza e promova thresholds calibrados por Pareto**.

Portanto, a existência dessas bibliotecas e de seus testes unitários não equivale a implementação operacional integral do ADR.

A validação estatística representativa permanece gate externo. Corpus sintético, Monte Carlo e validação adversarial DEV são evidência de engenharia, não homologação populacional.

## 5. Proveniência de schema

A fonte canônica permanece `Solution/database/Jornada_Fase1_v3.70.sql`, com migrações versionadas e fingerprint estrutural controlado em `CANDIDATE_INFO.json`.

Antes do corte da RC, o tuple de proveniência deve corresponder ao HEAD exato escolhido e aos gates DDL executados para esse estado. Após o corte da RC, mudança estrutural exige novo checkpoint de RC.

## 6. Documentação e UML

Os documentos destinados à entrega permanecem em DOCX/PDF, com UML incorporada quando exigida. As fontes Markdown e scripts de geração são artefatos de engenharia e rastreabilidade.

Documentação histórica não deve ser usada para inferir arquitetura corrente quando divergir deste estado candidato, da Especificação/Requisitos correntes ou de `CANDIDATE_INFO.json`.

## 7. Pendências que bloqueiam o corte da v5.00-rc.1

1. concluir e integrar a extração do runtime PostgreSQL e confirmar CI completo no HEAD exato;
2. alinhar os documentos correntes e a numeração das ADRs ao estado SQL Server-only;
3. fechar a lacuna de orquestração ponta a ponta do ADR de calibração no caminho SQL Server, ou registrar explicitamente o recorte que ficará pós-RC sem alegar implementação inexistente;
4. manter verde o conjunto canônico de build, unitários, integração SQL, DDL/upgrade, E2E, segurança, harness e validação independente;
5. atualizar a proveniência da candidata para o commit imutável escolhido para a RC;
6. preservar como pendentes, sem fabricar aprovação, os gates externos/institucionais aplicáveis, inclusive validação estatística representativa do Linkage.

Homologação Fabric não integra essa lista.

## 8. O que este estado não autoriza

Este documento não autoriza:

- criar tag/release v5.00 ou v5.00-rc.1 antes dos gates do HEAD exato;
- reescrever `RELEASE_INFO.txt` da última release selada antes do novo corte;
- ativar modelo probabilístico por evidência sintética isolada;
- tratar código histórico PostgreSQL como runtime suportado;
- tratar evidência histórica Fabric como requisito ou homologação da candidata atual.

O objetivo do fechamento é manter **uma arquitetura operacional, um contrato relacional e uma cadeia de evidência reproduzível**, sem segunda persistência concorrente.
