# Revisão transversal do acervo — 09/10/2026

**Objetivo:** registrar o que foi atualizado e o que foi deliberadamente
preservado, evitando que o estado atual do sistema seja inferido de
snapshots desatualizados ou que um aceite DEV/CI seja confundido com
aprovação normativa/HML/PROD.

**Ponto técnico de partida:** `master` no commit
`b4f74cb34e4346191ed58010b3d6b5cbb2db7e65`,
incluindo os merges #847–#859; o estado de cada funcionalidade é
verificável no código e nas Actions da respectiva HEAD. A presente
revisão é **editorial**, não efetua deploy, build, testes operacionais
ou ações sobre banco/containers.

## Cobertura desta consolidação

| Grupo documental | Atualização / fonte principal | Regra |
|---|---|---|
| **Manual central, estado e navegação** | [Manual técnico integrado](Manual_Sistema_Consolidado_20261009.md), [Estado atual](Estado_Atual_Projeto.md), [README do acervo](README.md), [Índice vivo](Indice_Acervo_Documental.md), `Solution/README.md` | Estado implementado diferente de candidato normativo e evidência histórica |
| **Módulos, arquitetura e contratos** | [Mapa de módulos](Mapa_Modulos_Produto_Andaime.md), [API](API.md), [Integração Gestor](Manual_Integracao_Gestor.md), [decisões de identidade/linkage](Indice_Decisoes_Vigentes.md) | 20 `.csproj` de `Solution/src`, divisão entre produto, operação e andaime |
| **Workers e Console DEV** | [Guia atual da Console](Console_DEV_Supervisao_Atual.md), [DT-18](DT18_Servicos_Independentes_Console_DEV.md), [DT-19](DT19_Console_Acoes_Workers.md), [DT-20](DT20_Supervisao_Opt_In_Workers.md), [DT-21](DT21_Testes_Resiliencia_Workers.md), [Plano de 08/10](Plano_Console_Workers_Independentes_2026-10-08.md), [Compose efêmero](../install/console-dev-e2e/README.md) | Não confundir histórico “PENDENTE em 08/10” com merges #845–#856; cancelamento confirmado segue pendente |
| **CI, qualidade, release e testes** | [DT10 extração](DT10_CI_Extracao_Reutilizavel.md), [Runbook de testes](Runbook_Testes_Tecnicos.md), [Índice de testes](Testes_Operacao_Indice.md) | Dez identidades de gates; DT10 condicional reusable, builds repetidos ainda pendentes |
| **Operação, governança e segurança** | [Runbook operacional](Runbook_Operacao.md), [Desenvolvimento local](Runbook_Desenvolvimento_Local.md), [Monitor](Monitor_Operacional.md), [Readiness](Governanca_Tecnica_Readiness.md) | CI sintética não homologa HML/PROD nem autoriza reset de bancos existentes |
| **Planejamento e dívidas** | [Plano](Plano_Desenvolvimento.md), [Dívidas técnicas](Dividas_Tecnicas.md), [DT-22 de RESOLVIDOS](DT22_Reavaliacao_Governada_Resolvidos.md), [Contrato de cancelamento](C3_3b3_Confirmacao_Cancelamento_RunOnce.md) | Trilha 4 encerrada como **proposta contínua**; replay extraordinário postergado, não entregue; pendências reais separadas de funcionalidades já mergeadas |
| **Diagramas / visão institucional** | [Índice UML](UML_Arquitetura_Indice.md) e documentos publicados em `Documentos/` | Diagramas datados não são atualizados silenciosamente nem documentos selados reeditados |

## O que significa “todo o sistema”

A cobertura transversal inclui **escopo e objetivos; arquitetura;
20 projetos C#; SQL e fluxos Bronze/Silver/Gold/Serving; identidade
e linkage; API/integração; Console; operações; segurança; ambientes;
testes e CI; release/governança; backlog; navegação e histórico**.
Documentos específicos de decisão, avaliação estatística, evidências
de E2E, `Documentos/ADR`, anexos DOCX/PDF e históricos de release
preservam seus próprios tempos e conteúdo. Eles continuam acessíveis
pelo [índice vivo](Indice_Acervo_Documental.md), com sua classe
de vigência explícita; reescrever arquivos selados para “atualizar tudo”
seria apagar evidência histórica. Esta revisão não afirma ter
revalidado individualmente cada hipótese de estudo/experimento antigo.

## Divergências reconciliadas

1. O estado anterior “DT-18–21 pendente” era anterior às PRs
   #845–#856. Passa a ser interpretado como decisão/critério
   histórico; implantação **DEV/CI efêmera** está documentada,
   preservando pendências de cancelamento confirmado e HML/PROD.
2. `SolutionSchema` corrente da candidata é **3.70**, não
   3.69 quando se fala de readiness atual. Menções a 3.69
   ligadas a versões de engenharia anteriores continuam históricas.
3. A CI não consiste mais em “oito gates em toda PR”:
   há dez identidades, classificador de impacto e gate DT10
   **condicional** transferido para workflow reutilizável.
4. O RunOnce dos três workers e a parada individual são funções
   distintas; a sobrevivência do RunOnce à desconexão HTTP
   **não** implementa um cancelamento confirmado.
5. A documentação de desenvolvimento local não é permissão
   para executar scripts que reiniciem ou eliminem
   `JornadaLocal`, acervo IBGE, Compose/volumes do usuário ou
   HML/PROD. A cadeia de evidências C3 usa ambiente GitHub-hosted
   isolado `JornadaE2E`.

## Decisão posterior incorporada — 09/10: Trilha 4 → DT-22

A proposta de uma **Trilha 4 periódica/contínua** foi
**encerrada pelo responsável**. Seu resultado funcional futuro
de reavaliar `RESOLVIDOS` com novas evidências/candidatos ou
modelo recalibrado é uma **dívida técnica separada e postergada**:
[DT-22](DT22_Reavaliacao_Governada_Resolvidos.md). Não há
comando novo, scheduler ou replay implementado nesta revisão.
O `INCREMENTAL` seleciona faltantes, `NAO_RESOLVIDO`,
`CONFLITO` e `PENDENTE_PROBABILISTICO`, não abrange
genericamente `RESOLVIDOS` indiretamente afetados. O
`REPLAY` já implementado é reprodução do modelo histórico;
não se pode declará-lo reavaliação com modelo novo. A ativação
e o replay extraordinário são operações **separadas**.
A limitação é expressamente conhecida, não autocorreção já
comprovada; manter critério de risco e aprovação próprios
no Ensaio. Atualizados: Plano, Dívidas, Manual, Estado, índices,
DT-05, DT-15, documentos de blocking e o plano datado de 08/10,
sem reescrever a normativa publicada.

## Checklist editorial para as próximas PRs

- Conferir código, HEAD, workflows de **cada HEAD** e artefatos
  antes de escrever “implementado”, “testado” ou “concluído”.
- Explicitar **escopo de ambiente**, dados usados (sintéticos vs
  representativos), versão de schema/norma e limites do aceite.
- Alterar, no **mesmo PR**, o manual atual, o documento de domínio,
  o Estado atual, o Plano/Dívidas e o runbook quando houver
  mudança de comportamento, segurança ou CI. Quando não aplicável,
  registrar o motivo.
- Não editar `RELEASE_INFO.txt`, anexos de release selados ou
  fotografias históricas apenas para ajustar a narrativa.
- Para código de segurança e lifecycle, mostrar a diferença entre
  **estado do processo** e **estado recuperado do lote**.
- Respeitar governança institucional: não elevar “verde em CI”
  a aceitação de Secretaria, modelo estatístico ou HML/PROD.

## Limites deste esforço

Esta revisão **não** altera a Especificação Técnica publicada 3.62,
o candidato v5.00 nem seus anexos formais em `Documentos/`. Ela
também não implanta a UI, não expõe um endpoint a PROD,
não conclui cancelamento explícito de RunOnce nem torna reutilizáveis
todos os builds .NET; essas pendências exigem implementação,
aceites específicos e/ou tramitação institucional própria.

**Fronteira de segurança:** `JornadaLocal`, IBGE original, banco de
usuário, NODE canônico, containers/volumes comuns, HML/PROD e dados
reais não são objetos de manipulação de uma revisão documental.
