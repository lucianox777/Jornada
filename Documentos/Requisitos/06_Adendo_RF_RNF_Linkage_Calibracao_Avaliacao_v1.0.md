# Adendo de Requisitos — Linkage, Calibração, Avaliação e IBGE

**Versão:** 1.0  
**Data:** 09/09/2026  
**Escopo:** Jornada do Cidadão — Fase 1  
**Status:** HISTÓRICO — SUPERADO PELA CONSOLIDAÇÃO RF/RNF v1.1

> Este documento preserva o registro da formulação intermediária de 09/09/2026, mas **não é fonte normativa concorrente**. A numeração e a semântica vigentes estão em `02_Requisitos_Funcionais_Jornada_v1.1.md`, `03_Requisitos_Nao_Funcionais_Jornada_v1.1.md` e `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md`.

## Mapeamento histórico para os requisitos vigentes

| Identificador neste adendo | Requisito vigente |
|---|---|
| RNF34 — Integração Contínua | RNF34-B — Integração contínua obrigatória |
| RNF35 — Documentação e diagramas UML | RNF34-C — Diagramas em padrão UML |
| RNF36 — Ambiente tecnológico e reprodutibilidade | RNF34-D — Ambiente tecnológico e reprodutibilidade |
| RF-051 — Paralelismo | RF-051 — Executar calibrador e avaliador com paralelismo quando vantajoso |
| RF-052 — Dados IBGE semanticamente compatíveis | RF-052 e RF-054 — Uso de frequências oficiais e preservação de semântica |
| RF-053 — Evitar snapshot redundante | RF-056 — Evitar snapshots redundantes da base oficial do IBGE |
| RF-054 — Enriquecimento de blocking | RF-052 e RF-054 — Uso governado e semântica oficial do IBGE |
| RF-055 — Componentes de nome/mãe/nascimento | RF-053 — Componentes de nome e nascimento no blocking otimizado |
| RF-056 — Regras dinâmicas entre Calibrador/Avaliador | RF-055 — Reutilizar no avaliador a regra dinâmica versionada produzida pelo calibrador |
| RF-057 — Suporte físico indexado | requisito técnico realizado pela projeção `identidade.blocking_chave`; não cria novo RF nesta consolidação |

## Princípios preservados

A consolidação não altera o conteúdo técnico essencial que motivou este adendo: CI obrigatório; documentação normativa em UML; paralelismo somente quando mensuravelmente vantajoso; uso de dados oficiais agregados do IBGE apenas quando semanticamente compatíveis; preservação da grafia/semântica oficial; detecção de snapshots redundantes por validadores e fingerprint; blocking dinâmico versionado e reproduzível; e projeção indexada reconstruível para geração de candidatos.

**Microsoft SQL Server permanece a tecnologia relacional normativa da Jornada.** PostgreSQL pode existir como provider paralelo em escopos explicitamente suportados, sem substituir o baseline relacional normativo.

## UML vigente

As fontes UML correntes ficam em `Solution/docs/uml/`. Em particular, a consolidação v5.00 acrescenta:

- `Jornada_Identidade_Linkage_Classes.puml` — diagrama de classes UML;
- `Jornada_Resolucao_Identidade_Atividade.puml` — diagrama de atividade UML.

DER/DRE pode permanecer como visão física auxiliar de dados, mas não é classificado como UML nem substitui esses diagramas.
