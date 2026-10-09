# Índice vivo atualizado — 09/10/2026

**Trilha editorial desta atualização:** [Revisão transversal de 09/10/2026](Revisao_Documental_Integral_20261009.md), sem reclassificação retroativa de releases/artefatos selados.

A **porta de entrada técnica transversal** agora é o [Manual integrado do sistema](Manual_Sistema_Consolidado_20261009.md). Acompanhe o [estado de implementação](Estado_Atual_Projeto.md), a [Console DEV implementada](Console_DEV_Supervisao_Atual.md), a [extração DT-10](DT10_CI_Extracao_Reutilizavel.md), as [dívidas revisadas](Dividas_Tecnicas.md), o [plano](Plano_Desenvolvimento.md) e as [decisões canônicas](Indice_Decisoes_Vigentes.md). **Esta revisão editorial não substitui a Especificação Técnica publicada nem reclassifica releases seladas.** Para provar merges/testes, conferir HEAD e Actions, nunca só o texto datado.

**Classificação de histórico:** anotações de 08/10 que dizem “DT-18–21 pendente” documentam o *estado anterior* à série #847–#856. Elas não devem ser usadas para afirmar que o código continua ausente; os itens comprovados são discriminados no manual atual. Por outro lado, o cancelamento **confirmado** de um RunOnce ativo e a homologação HML/PROD **não** foram entregues por essa série. O [contrato de cancelamento](C3_3b3_Confirmacao_Cancelamento_RunOnce.md) é uma especificação futura.

---

# Índice vivo do acervo — precedência e leitura mínima

> **Organização do monorepo (06/10/2026):** a antiga solução embarcada `Solution/ApoioSecretarias/` foi removida do repositório principal. Referências abaixo a esse caminho descrevem evidência/histórico anterior à remoção. O produto não compila nem distribui esse transmissor/preparador; apenas schemas SEHAB estritamente sintéticos necessários à regressão permanecem em `Solution/tests/fixtures/external-contracts/gestores/SEHAB/`.

**Revisão editorial:** 06/10/2026. Este arquivo é a porta de entrada **corrente** para o acervo e deve ser mantido sem tentar reclassificar, a cada mudança, todos os documentos históricos. O [Catálogo de vigência de 29/09](Catalogo_Vigencia_Documental_20260929.md) é um snapshot datado do HEAD `fc43cd3e...`; não representa arquivos criados ou alterados depois daquele corte.

O estado técnico real não é deduzido apenas da documentação: para afirmar implementação, confrontar este índice com `master`, Actions e issues/PRs. Documentos históricos, evidências e contratos selados são preservados por rastreabilidade e não ganham vigência só por permanecerem no repositório.

| Pergunta | Fonte primária | Como interpretar |
|---|---|---|
| Qual foi a última release selada? | `../../RELEASE_INFO.txt` + `../../Documentos/README.md` | Fato histórico de release; não é o estado candidato atual. |
| Qual é a última Especificação Técnica publicada? | `../../Documentos/Especificacao_Tecnica_Jornada_v3.62.docx/.pdf` | Último texto normativo materializado; não inventar uma v3.64 ausente. |
| Qual é a candidata normativa seguinte? | `../../Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md` + Requisitos v1.1 | Candidata, sem efeito de publicação até aprovação/corte formal. |
| Qual é o estado técnico corrente? | [Estado atual](Estado_Atual_Projeto.md) + `master` + Actions | O documento é fotografia; HEAD e CI confirmam a execução. |
| Quais decisões de identidade/linkage prevalecem? | [Decisões canônicas 29/09](Decisoes_Canonicas_Identidade_Linkage_20260929.md) + [índice de decisões](Indice_Decisoes_Vigentes.md) | Data da decisão não significa que o código daquele dia seja o estado atual. |
| O que desenvolver? | [Plano](Plano_Desenvolvimento.md) + [Dívidas técnicas](Dividas_Tecnicas.md) + issues abertas | Plano define prioridade; issues/PRs e CI comprovam execução/fechamento. |
| Como operar/testar? | `Runbook_*.md`, [Testes e operação](Testes_Operacao_Indice.md), workflows e scripts versionados | Procedimento deve ser compatível com o código corrente; divergência vira achado, não é resolvida por inferência. |
| Como executar Ensaio/HML? | [Ensaio único](Ensaio_Unico_Paridade_HML.md) + runbooks HML | CI/DEV sintético não equivale a homologação HML/Produção. |
| Onde está o histórico? | `archive/releases/`, `Documentos/Estado_Engenharia_v*.md`, `Evidencia_Runtime_*`, snapshots datados | Evidência e contexto; não usar como instrução corrente quando houver fonte posterior. |

## Classes de documento

- **Normativo publicado:** release selada e Especificação Técnica efetivamente publicada. Preservar bytes e proveniência.
- **Candidato normativo:** v5.00 candidata e Requisitos v1.1; podem consolidar o estado comprovável, mas não se promovem sozinhos a norma.
- **Decisão canônica:** decisões explícitas que governam novas mudanças. Quando houver descompasso, a decisão não autoriza afirmar que o código já foi adequado.
- **Estado/plano corrente:** Estado atual, Plano, Dívidas e este índice. São mantidos editorialmente e precisam ser confrontados com HEAD/CI/issues.
- **Operação e implementação:** runbooks, arquitetura, contratos, gates e documentos especializados. Podem conter seções cronológicas; ler o marcador de estado antes de reutilizar uma afirmação.
- **Evidência/histórico:** snapshots, notas de engenharia, releases antigas, relatórios de execução e documentos datados. Preservam auditoria sem autoridade decisória futura.

## Pontos de atenção confirmados em 06/10/2026

1. **Runtime .NET:** a Solution corrente está em `net10.0`/SDK 10.0.112, mas o instalador Windows de produção ainda verifica/instala .NET 8. O drift está rastreado na issue #790; até correção técnica e regressão do instalador, documentação de produção não deve ser usada para declarar o runtime alinhado.
2. **SEHAB:** `Solution/ApoioSecretarias/` é a fronteira de titularidade/solução. As cópias JSON em `Solution/config/contracts/gestores/SEHAB/` são cópias runtime deliberadas e devem permanecer byte-idênticas às fontes do apoio conforme o Gate 6; duplicação aqui não é, por si só, lixo.
3. **V6/V7 e guard histórico:** testes/documentos legados continuam úteis para replay e regressão histórica. As decisões DC-LK tornam V8 o destino e retiram o guard demográfico fixo para novos V8 com TF; não usar provas V6/V7 como aceite de uma V8 nova.
4. **Catálogo de 29/09:** permanece válido somente para o corte que declara. Não usar a expressão “catálogo de todo o acervo” para inferir cobertura de documentos posteriores.
5. **Arquivos sem referência automática:** ausência de chamada em workflow/código identifica candidato de revisão, não prova ausência de uso operacional/manual. Scripts e runbooks precisam ser avaliados no fluxo real antes de exclusão.

## Catálogo por assunto

- **Identidade e Gold:** `Identidade_*.md`, `Identity_*.md`, `Gold_Pessoa_Universo_CPF.md`, `Arquitetura_Identidade_Linkage.md`.
- **Linkage, calibração e diagnósticos:** `Linkage_*.md`, `Calibrador_*.md`, `DT05_*.md`, `DT15_*.md`, `DT17_*.md`.
- **Ensaios e testes:** `Ensaio_*.md`, `Runbook_Testes_Tecnicos.md`, `Aceite_OpenAPI_BlackBox.md`, `evidence/`.
- **Operação, HML, segurança e release:** `Runbook_*.md`, `HML_*.md`, `Governanca_*.md`, `Release_Evidence.md`.
- **Norma e histórico institucional:** `../../Documentos/README.md`, `../../Documentos/Requisitos/`, `../../Documentos/Estado_Engenharia_v*.md`.

## Regra de precedência e manutenção

Quando duas fontes divergirem, não “fundir” silenciosamente os textos. Identificar primeiro a classe de cada documento. Release selada e evidência histórica preservam o que ocorreu; decisões canônicas orientam mudanças futuras; o estado corrente precisa ser comprovado no HEAD; plano/backlog não prova entrega.

Decisão nova deve entrar na fonte canônica adequada; estado comprovado no Estado atual; prioridade no Plano; critério técnico em Dívidas; execução em issues/PRs; procedimento em runbook; evidência junto à execução. O histórico deve ser arquivado/classificado, não reescrito para parecer atual.

**Existe um único Ensaio** técnico e operacional antes de HML; HML muda a massa para a preparada pelas Secretarias, preservando contratos e controles. Não transformar documentos auxiliares de ensaio em fases concorrentes.
