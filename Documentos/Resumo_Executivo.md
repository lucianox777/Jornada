# Jornada do Cidadão — Resumo Executivo

**Data:** 12/09/2026  
**Estado:** CANDIDATO TÉCNICO À CONSOLIDAÇÃO v5.00 — release/tag ainda não cortada  
**Base normativa vigente da release selada:** v3.64  
**Solution Engenharia selada:** v4.05  
**SolutionSchema da release selada:** v3.69  
**SolutionSchema alvo desta consolidação:** v3.70

## Fronteira de versão

`RELEASE_INFO.txt` continua sendo a fonte versionada da última release de engenharia selada: Base Normativa v3.64, Solution Engenharia v4.05, SolutionSchema v3.69 e tag `jornada-solution-v4.05`.

Esta branch prepara a consolidação técnica para SolutionSchema v3.70, mas **não declara a existência da release/tag v5.00**. O corte futuro deve ocorrer de forma atômica pelos mecanismos de release existentes; até lá, 3.70 é estado técnico candidato, não uma release publicada.

## Baseline documental corrente

A Especificação Técnica v3.62 permanece a base da família institucional de requisitos. A porta de entrada corrente é `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1`, que define leitura autossuficiente dos cinco documentos v1.1 e mantém os v1.0 apenas como baseline histórico, sem leitura cumulativa.

O modelo físico corrente é `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40`, derivado do schema canônico `Solution/database/Jornada_Fase1_v3.70.sql`. O inventário automatizado medido em 12/09/2026 no `master` mede **69 tabelas distintas** no schema operacional consolidado: 53 do baseline legado, 2 do núcleo progressivo e 14 tabelas distintas introduzidas por migrações. Entre as estruturas acrescentadas estão `ref.frequencia_nome`, `ref.frequencia_nome_cobertura` e `ref.frequencia_nome_versao`, que internalizam a referência versionada de frequências de nomes. As antigas contagens de 53, 64 e 66 tabelas não devem ser usadas como contagem do schema corrente.

A fonte Markdown do modelo físico já descreve a projeção semântica de nome em `gold.pessoa` e a separação entre chave estável e frequência versionada. Os correspondentes DOCX/PDF são artefatos derivados e devem ser regenerados antes de uma nova publicação de entrega; sua mera presença na árvore não substitui a fonte textual corrente.

## Estado técnico da consolidação

A consolidação 3.70 alinha readiness, gates, compatibilidade, observabilidade e caminho de upgrade ao schema corrente; preserva o upgrade fail-closed e a reentrada; e mantém Microsoft SQL Server como tecnologia relacional normativa.

A referência populacional de nomes é mantida internamente de forma versionada para permitir proveniência e replay. Modelos/execuções preservam a versão utilizada; `gold.pessoa` mantém apenas a chave semântica estável necessária para a consulta, e não uma frequência populacional única dependente do estado corrente da referência. Isso não implica, por si só, adoção de qualquer fórmula de raridade ou mudança metodológica no scoring.

Os requisitos consolidados v1.1 registram explicitamente que seu estado de incorporação é candidato técnico à Solution Engenharia v5.00. Essa documentação não antecipa aprovação institucional, implantação em HML/Produção, RIPD, calibração de Linkage nem qualquer decisão dependente de dados reais ou governança.

## Regra de leitura

Para identificar o estado vigente, use conjuntamente:

1. `RELEASE_INFO.txt` para a última release/tag efetivamente selada;
2. `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1` para o baseline institucional consolidado candidato;
3. `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.md` e `Solution/database/Jornada_Fase1_v3.70.sql` para o schema técnico candidato, regenerando os derivados DOCX/PDF antes de nova publicação de entrega;
4. `Documentos/README.md` para distinguir artefatos correntes de snapshots históricos preservados no repositório.
