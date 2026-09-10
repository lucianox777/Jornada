# Adendo de Requisitos — Linkage, Calibração, Avaliação e IBGE

**Versão:** 1.0  
**Data original:** 09/09/2026  
**Escopo:** Jornada do Cidadão — Fase 1  
**Status:** **SUPERADO / HISTÓRICO — NÃO NORMATIVO**

Este arquivo é preservado apenas para rastreabilidade da consolidação ocorrida em 09/09/2026. Seus identificadores temporários **não devem ser usados como fonte normativa**, pois foram incorporados e renumerados nos documentos vigentes:

- `02_Requisitos_Funcionais_Jornada_v1.1.md`;
- `03_Requisitos_Nao_Funcionais_Jornada_v1.1.md`;
- `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md`.

Em caso de divergência, prevalece a numeração dos documentos vigentes acima.

## Mapeamento canônico

| Conceito originalmente registrado neste adendo | Identificador canônico vigente |
|---|---|
| Paralelismo seguro do Calibrador/Avaliador | RF-051 |
| Uso governado de frequências oficiais IBGE no blocking | RF-052 |
| Componentes de nome/nome da mãe/nascimento no blocking | RF-053 |
| Preservação da semântica oficial dos nomes IBGE | RF-054 |
| Mesma política dinâmica versionada no Calibrador/Avaliador | RF-055 |
| Evitar snapshots IBGE redundantes | RF-056 |
| Suporte físico indexado/reconstruível para blocking dinâmico | requisito técnico associado ao RF-053/RF-055 e RT-057; não cria RF-057 paralelo |
| Integração Contínua e gates | RNF34-A / RNF34-B |
| UML normativo | RNF34-C |
| Ambiente tecnológico reprodutível | RNF34-D |

## Regras preservadas da consolidação

1. Paralelismo é otimização subordinada a determinismo, correção e medição reproduzível.
2. Estatística IBGE é evidência agregada auxiliar, nunca identidade individual.
3. Atributos sem correspondência IBGE continuam elegíveis para calibração com evidência da Jornada.
4. `Content-Length` é pré-verificação barata; identidade de snapshot depende de validadores confiáveis e/ou SHA-256.
5. Blocking dinâmico usa regras versionadas; o Avaliador consome exatamente a política produzida pelo Calibrador.
6. A projeção `identidade.blocking_chave` é derivada/reconstruível e não compete com a Gold como fonte de verdade.
7. Nomes podem preservar aliases históricos conforme política; CPF e data de nascimento têm semântica estável e correções são excepcionais/auditáveis.

## UML

O fluxo normativo permanece representado em `Solution/docs/uml/Linkage_Calibrador_Avaliador_IBGE.puml` e nos demais diagramas referenciados pelo índice UML vigente.

> Nota de governança: este arquivo não deve ser contado por ferramentas de auditoria de requisitos como corpus normativo vigente.
