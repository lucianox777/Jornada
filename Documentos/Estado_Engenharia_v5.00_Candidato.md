# Estado de Engenharia — candidato Solution Engenharia v5.00

**Data de consolidação:** 10/09/2026  
**Status:** CANDIDATO TÉCNICO — RELEASE/TAG NÃO CORTADA  
**Branch de fechamento:** `docs/fechamento-v5-fabric`  
**Base do trabalho:** `master` em `29bba9631c2184a22e035860cd15bdc0185eebf8`  
**SolutionSchema corrente:** `3.70`

## 1. Relação com a última release selada

A última release selada continua sendo a **Solution Engenharia v4.05**, com `schema_solution=v3.69` e tag `jornada-solution-v4.05`, conforme `RELEASE_INFO.txt`.

Este documento **não altera, substitui nem reescreve retroativamente** essa release. O estado 3.70 é tratado como candidato técnico à v5.00 até que todos os gates de fechamento estejam satisfeitos e a release seja cortada explicitamente.

## 2. Estado do master de referência

O `master` usado como base deste fechamento é:

```text
29bba9631c2184a22e035860cd15bdc0185eebf8
```

Nesse SHA, após a integração da correção de deadlock do polling de status de ingestão, os workflows de push observados incluem:

- `jornada-ci` — run 1651 — **success**;
- `jornada-windows-production-installer` — run 437 — **success**.

Essas execuções demonstram que existe CI verde no estado corrente de engenharia. Elas não substituem evidências externas/condicionais que dependem de ambiente institucional específico, como a homologação Fabric.

## 3. UML / RNF34-C

O requisito de possuir:

1. diagrama de classes UML para Identidade/Linkage; e
2. diagrama de atividade UML para resolução de identidade

está implementado no processo reprodutível de geração documental.

`Solution/scripts/generate-document-deliverables.py` gera as figuras:

- `Jornada_Identidade_Linkage_Classes.png`;
- `Jornada_Resolucao_Identidade_Atividade.png`.

O mesmo processo as incorpora ao `Anexo_Modelo_Fisico_Jornada_v1.40.docx` e falha se o DOCX final não contiver **exatamente duas figuras UML incorporadas**. Portanto, a ausência dessas duas visões como arquivos `.puml` em `Solution/docs/uml` não caracteriza, isoladamente, ausência dos artefatos UML exigidos pelo RNF34-C.

## 4. Fabric — distinção arquitetural

Para evitar a ambiguidade de tratar “Microsoft Fabric” como um único tipo de armazenamento, o fechamento v5.00 usa a seguinte distinção:

- **SQL Server 2022 Developer/Testcontainers:** baseline obrigatório de desenvolvimento, CI, DDL canônico e validação ordinária independente de ambiente;
- **SQL Database in Microsoft Fabric:** destino relacional operacional preferencial de HML/Produção, condicionado à homologação da release exata;
- **Lakehouse / SQL Analytics Endpoint:** escopo analítico, sem substituir implicitamente o banco relacional operacional.

A aplicação deve continuar usando o mesmo `OperationalSqlAdapter`/`Microsoft.Data.SqlClient` e o mesmo contrato funcional, sem bifurcação de regra de negócio por hospedagem.

## 5. Evidência Fabric disponível e limite da evidência

A execução de 03/09/2026 registrou **58/58 Integration PASS, 0 falhas e 0 skips** contra SQL Database in Microsoft Fabric real.

Essa evidência pertence à linha histórica v4.00 e permanece válida como antecedente de compatibilidade. Ela **não homologa automaticamente** o `master` atual, o SolutionSchema v3.70 ou o candidato v5.00.

Antes do corte da v5.00 deve existir nova evidência versionada executada contra o **HEAD exato candidato à release**, registrando ao menos SHA, SolutionSchema, data/hora, alvo Fabric, contagens de testes executados/aprovados/falhados/ignorados e referência ao TRX ou artefato equivalente.

## 6. Pendências que bloqueiam o corte v5.00

No estado deste documento, permanecem bloqueantes:

1. reexecutar o harness `FABRIC_SQL_DATABASE` contra o HEAD exato candidato e SolutionSchema v3.70;
2. versionar a nova evidência de homologação Fabric;
3. eliminar a ambiguidade remanescente em textos correntes que ainda usem “Fabric” genericamente como sinônimo apenas de ambiente analítico/compatibilidade, distinguindo SQL Database in Fabric de Lakehouse/SQL Analytics Endpoint;
4. confirmar os gates obrigatórios de CI no HEAD final após o change-set de fechamento;
5. somente então cortar a release/tag v5.00 e atualizar atomicamente os metadados de release.

## 7. O que este fechamento não autoriza

Este estado candidato não autoriza, por si só:

- criação da tag/release v5.00;
- alteração retroativa de `RELEASE_INFO.txt` da v4.05 antes do corte;
- ativação probabilística sem os gates de corpus/calibração/governança já definidos;
- tratar a evidência Fabric v4.00 como evidência da v5.00.

O objetivo é separar claramente **engenharia corrente validada** de **release formalmente selada** e impedir que documentação histórica seja confundida com evidência do HEAD atual.
