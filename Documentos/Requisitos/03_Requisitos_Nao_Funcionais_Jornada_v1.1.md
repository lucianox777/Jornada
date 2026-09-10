# Requisitos Não Funcionais - Jornada do Cidadão - Fase 1 - Aditivo v1.1

**Versão do aditivo:** 1.1  
**Data:** 09/09/2026  
**Base:** `03_Requisitos_Nao_Funcionais_Jornada_v1.0.md`  
**Status:** VIGENTE - COMPLEMENTO NORMATIVO

Este documento complementa, sem apagar nem renumerar, os 33 RNFs do baseline v1.0. Em caso de mudança técnica posterior a este aditivo, as regras abaixo são cumulativas com o baseline. Para eliminar ambiguidade, os RNFs históricos devem ser referenciados no formato `RNF-001` a `RNF-033`; os identificadores aditivos permanecem `RNF34-A` a `RNF34-D` até futura rebaseline integral.

## RNF12 - Testabilidade e regressão - complemento vigente

Além do requisito original de que os testes de integração exercitem DDL/seed reais de SQL Server quando aplicável, **toda mudança funcional, estatística, de contrato, persistência, integração, segurança ou regra de identidade deve possuir regressões automatizadas nos níveis unitário e de integração compatíveis com o risco alterado**.

Uma alteração não pode ser considerada pronta para merge quando houver comportamento alterado sem regressão unitária correspondente ou sem prova integrada do caminho afetado. Quando um nível não for tecnicamente aplicável, a exceção deve ser explícita e justificada no próprio change-set; ausência silenciosa de teste não é aceita.

Para algoritmos de identidade/Linkage, a regressão integrada deve provar também as propriedades de segurança pertinentes, inclusive ausência de publicação/ativação indevida, conservação de fatos e comportamento fail-closed quando esses invariantes fizerem parte do escopo.

## RNF34-A - Sincronização documental do change-set

Toda mudança que altere comportamento, contrato, configuração, operação, segurança, modelo estatístico, requisito ou evidência deve atualizar **no mesmo change-set** toda documentação diretamente afetada, incluindo, conforme aplicável: README do componente, runbook, requisitos e matriz de rastreabilidade, especificação técnica/arquitetura corrente, contrato de configuração/evidência e documentação de segurança/governança.

Documentação conhecida como obsoleta ou contraditória bloqueia o estado Ready. A revisão documental faz parte da definição de pronto; não é atividade posterior ao merge. ADR é exigido apenas quando o projeto optar por preservar histórico decisório pós-publicação; para a V1 ainda não publicada, decisões consolidadas podem ser incorporadas diretamente à especificação corrente.

## RNF34-B - Integração contínua obrigatória

Todo código e toda alteração de infraestrutura, banco, contrato, algoritmo ou documentação verificável do projeto devem passar por **CI automatizada** antes do merge. O CI deve executar os gates aplicáveis ao change-set, incluindo build, regressões unitárias, regressões de integração, verificações estáticas/segurança e validações documentais quando existirem.

Um PR não pode ser considerado pronto nem integrado quando um gate obrigatório do HEAD exato estiver ausente, falhando, cancelado de forma não justificada ou ainda em execução. Gates dispensados por condição explícita devem permanecer distinguíveis de gates executados com sucesso. Verificação manual não substitui gate automatizável de CI.

## RNF34-C - Diagramas em padrão UML

Diagramas técnicos e arquiteturais mantidos como documentação normativa ou de engenharia devem usar **notação UML compatível com o tipo de visão representada**, por exemplo: componente, sequência, atividade, estado, classes, implantação ou casos de uso.

DER/DRE e outras notações de modelagem de dados podem existir como anexos físicos auxiliares, mas **não são UML e não substituem o diagrama UML normativo**. A estrutura de identidade/linkage deve possuir diagrama de classes UML e o processo de resolução de identidade deve possuir diagrama de atividade UML, ambos com fonte versionada.

Diagramas informais podem existir como apoio visual, mas não substituem o diagrama UML quando o artefato documenta relações, fluxos, estados ou arquitetura usados para decisão técnica. Sempre que o comportamento ou arquitetura representada mudar, o diagrama UML correspondente deve ser atualizado no mesmo change-set conforme RNF34-A. A ferramenta de autoria pode variar, desde que o artefato preserve semântica UML e permaneça versionável/reproduzível quando possível.

## RNF34-D - Ambiente tecnológico e reprodutibilidade

O desenvolvimento, teste, integração e operação da Jornada devem adotar ambiente tecnológico explícito, versionado e reprodutível. A implementação de serviços e algoritmos deve usar **C#/.NET** conforme a versão suportada pelo repositório; **Git** é o sistema de controle de versão; **Docker** deve ser usado para ambientes descartáveis e testes integrados quando o componente possuir dependências containerizáveis; e artefatos de BI devem ser produzidos/validados em **Power BI Desktop** quando esse for o formato de entrega.

**Microsoft SQL Server é a tecnologia relacional normativa da Jornada.** PostgreSQL pode ser exercitado como provider operacional paralelo exclusivamente nos escopos explicitamente suportados e versionados, inclusive calibração/avaliação de Linkage, sem substituir a tecnologia relacional normativa. Microsoft Fabric permanece no escopo analítico/compatibilidade definido pela arquitetura e não é fonte de verdade operacional implícita.

Versões de SDK, imagens de container e demais dependências automatizáveis devem ser fixadas ou controladas de forma reproduzível no CI. Dependências exclusivamente locais, como Power BI Desktop quando não houver runner compatível, devem ter versão mínima/suportada documentada e procedimento de validação rastreável. Diferenças entre ambiente local e CI não podem alterar silenciosamente regras funcionais ou resultados estatísticos.

Os identificadores `RNF34-A`, `RNF34-B`, `RNF34-C` e `RNF34-D` são aditivos e passam a integrar o corpus vigente, embora preservem a forma alfanumérica até futura rebaseline integral.

## Aplicação ao Linkage

O calibrador e o avaliador devem usar o mesmo contrato versionado de **blocking dinâmico por observação**, atualmente materializado por `BirthBlockingPlan` e pelas políticas versionadas que vierem a selecionar seus passes/atributos. Nenhum deles pode manter uma regra paralela fixa que produza universo de candidatos semanticamente diferente sem versionamento, regressões unitárias/integradas e atualização documental conjunta.

A paralelização do calibrador e do avaliador deve ser limitada, configurável e condicionada a ganho mensurável, mantendo equivalência determinística/reprodutível com a execução serial. O uso de blocking dinâmico em calibração/avaliação não constitui, isoladamente, homologação estatística nem autorização de ativação probabilística. A issue #31 continua exigindo corpus representativo, avaliação independente, falsos vínculos/calibração e aprovação institucional.
