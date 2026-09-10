# Índice UML — Jornada do Cidadão

**Atualização:** 10/09/2026  
**Status:** documentação técnica versionada  
**Notação:** UML 2.x, fontes PlantUML (`.puml`)

Este índice organiza os diagramas normativos da Jornada e explicita a finalidade de cada visão. Como a solução ainda não foi publicada, as decisões arquiteturais internas foram incorporadas à especificação corrente `Arquitetura_Identidade_Linkage.md`; não se mantém uma camada separada de ADRs históricos antes da V1 pública.

## Regra de documentação

Diagramas novos ou materialmente alterados que representem componentes, classes, sequências, estados, atividades, implantação ou casos de uso devem utilizar o tipo UML adequado. A fonte textual PlantUML deve permanecer versionada no repositório sempre que tecnicamente possível.

Quando houver divergência entre um diagrama e um contrato executável vigente, o contrato executável, os requisitos normativos e a especificação arquitetural devem ser reconciliados no mesmo change-set; o diagrama não deve criar comportamento implícito.

## Catálogo atual

| Arquivo | Tipo UML | Finalidade |
|---|---|---|
| `uml/Jornada_Arquitetura_Componentes.puml` | Componentes | Visão lógica da Jornada, fronteiras entre API/Bronze, Processor, identidade, Gold/Serving, Linkage e IBGE. |
| `uml/Jornada_Implantacao.puml` | Implantação | Ambiente tecnológico, CI, runtime, bancos, storage e fontes externas. |
| `uml/Identidade_Progressiva_Estados.puml` | Máquina de estados | Ciclo de vida da identidade progressiva e correção governada. |
| `uml/Linkage_Calibrador_Avaliador_IBGE.puml` | Sequência | Relação entre Calibrador, snapshots IBGE, blocking e Avaliador versionado. |
| `uml/Linkage_Dynamic_Blocking_Sequence.puml` | Sequência | Execução do blocking dinâmico no runtime. |
| `diagrams/sequence/01_fato_identidade_resolvida.puml` | Sequência | Ingestão/publicação quando a identidade está resolvida. |
| `diagrams/sequence/02_cpf_em_conflito.puml` | Sequência | Tratamento de CPF em conflito. |
| `diagrams/sequence/03_sem_cpf_linkage.puml` | Sequência | Fato sem CPF e resolução probabilística posterior. |
| `diagrams/sequence/04_correcao_governada.puml` | Sequência | Correção governada sem reescrita silenciosa do fato declarado. |

## Visões normativas principais

### Arquitetura lógica

`Jornada_Arquitetura_Componentes.puml` registra as fronteiras que devem permanecer explícitas:

- `Jornada.Api` é porta de entrada e escreve Bronze/controle; não é escritora de Silver, Gold ou identidade;
- `Jornada.Processor.Worker` transforma o conteúdo aceito e participa da persistência operacional;
- Gold representa fatos publicados e não deve ser reescrita silenciosamente por uma decisão de identidade;
- identidade mantém âncoras, estado progressivo, histórico e composição governada;
- a projeção `blocking_chave` é derivada/reconstruível e não compete com Gold como fonte de verdade;
- Calibrador publica regras/parâmetros/evidências versionados e o Avaliador consome a versão exata sob teste;
- dados IBGE são estatística agregada auxiliar, não verdade individual.

### Estados da identidade progressiva

`Identidade_Progressiva_Estados.puml` apresenta a identidade como máquina de estados. A associação estável CPF → UUID, quando válida e confiável, não é trocada silenciosamente pelo Linkage. Correções são excepcionais, auditáveis, versionadas e preservam o histórico.

### Implantação e engenharia

`Jornada_Implantacao.puml` documenta a visão de implantação lógica e o RNF de ambiente reproduzível: C#/.NET, Git/GitHub, Docker, bancos suportados e Power BI Desktop/PBIP quando aplicável. O CI valida o change-set antes da integração e mantém gates adicionais pertinentes ao componente.

## Rastreabilidade de requisitos

Este conjunto materializa especialmente os requisitos de documentação/engenharia do adendo `Documentos/Requisitos/06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md`:

- **RNF34:** Integração Contínua;
- **RNF35:** documentação e diagramas UML;
- **RNF36:** ambiente tecnológico e reprodutibilidade de engenharia;
- **RF-052 a RF-057:** IBGE, snapshots, blocking e rulesets versionados.

A semântica arquitetural correspondente está consolidada em `Solution/docs/Arquitetura_Identidade_Linkage.md`.

## Convenções PlantUML

As fontes devem conter `@startuml`/`@enduml`, título legível e comentário inicial identificando o tipo UML. Alias devem representar conceitos estáveis do domínio, evitando nomes circunstanciais de host quando o diagrama for lógico. Diagramas de implantação podem registrar tecnologias concretas; diagramas de componentes devem priorizar responsabilidades e fronteiras.
