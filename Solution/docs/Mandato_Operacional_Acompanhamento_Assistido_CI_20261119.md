# MANDATO OPERACIONAL — Acompanhamento assistido de CI e correções rastreáveis

**Identificador:** GOV-MANDATO-CI-ASSISTIDA-20261119  
**Emissor:** Product Owner do Projeto Jornada do Cidadão  
**Data:** 19/11/2026  
**Status:** mandato solicitado expressamente pelo Product Owner nesta conversa; registrado em branch da PR #1450, **pendente de integração à master**.  
**Repositório:** `lucianox777/Jornada`

## 1. Objeto e finalidade

Formalizar o mandato para o assistente acompanhar, investigar e apoiar a correção de falhas de integração contínua no GitHub, de modo repetível, verificável e auditável. O método é descrito em [Procedimento Operacional — Acompanhamento Assistido da CI](Procedimento_Operacional_Acompanhamento_CI_Externo_20261119.md).

A CI **continua sendo executada pelo GitHub Actions**; o assistente consulta os resultados pela integração GitHub, sem constituir executor de CI paralelo. A atuação ocorre quando acionada pelo PO, não representa monitoramento ininterrupto em segundo plano.

## 2. Atribuições abrangidas

1. Consultar PR, branch, SHA e estado de merge; enumerar execuções, jobs, checks e respectivos resultados vinculados à revisão correta.
2. Ler logs, workflows, testes, código, artefatos disponíveis e documentação para separar causa primária de sintomas derivados.
3. Investigar divergências de contrato, dependências expiradas e erros de execução com evidência suficiente, mantendo a distinção entre hipótese e fato confirmado.
4. Aplicar **correções técnicas pontuais e reversíveis** em branches de trabalho, com commits identificáveis, quando não alterarem decisões científicas ou políticas reservadas ao PO.
5. Reexecutar/verificar a CI pelos mecanismos autorizados e conferir os gates efetivamente exigidos; não declarar aprovação com jobs pendentes nem aproveitar sucesso de SHA antigo como aprovação de SHA novo.
6. Documentar contexto, justificativa, precedência, impactos, SHA, runs, evidências e resultado; sincronizar o índice de decisões vigentes conforme GOV-DOC-01.
7. Informar o PO de avanços, falhas, bloqueios, riscos e necessidades de decisão explícita.

## 3. Limites de autoridade e matérias reservadas

Este mandato **não autoriza**, por si só:
- Fazer merge ou contornar branch protection, revisões, checks obrigatórios ou regras de segurança.
- Iniciar ou repetir campanhas científicas TRAIN/VALIDATION/TEST, consumir TEST one-use, alterar LOCKs, thresholds científicos, corpora congelados ou fingerprints.
- Certificar, promover ou ativar modelos; essas decisões continuam submetidas às autoridades e controles vigentes, inclusive Operador Master.
- Substituir artefatos históricos indisponíveis por corpora apenas aparentemente semelhantes.
- Publicar dados pessoais, segredos, mensagens integrais de exceções ou logs restritos.
- Encerrar incidentes ou afirmar que uma correção está validada antes de observar evidência do commit correspondente.

Mudanças de escopo, decisões técnicas/funcionais/científicas ou exceções de governança que ultrapassem os limites acima requerem aprovação específica do PO e registro oficial antes da conclusão.

## 4. Ciclo de atuação e critérios de conclusão

**Identificar SHA → consultar CI → priorizar bloqueios → examinar evidências → corrigir de forma mínima → registrar decisão/alteração → validar nova revisão → comunicar.**

Uma intervenção só pode ser reportada como **validada** quando os testes pertinentes concluírem com sucesso no SHA indicado. A PR só pode ser reportada como **apta à integração** após confirmação de todos os requisitos aplicáveis; `mergeable=true` não basta. Falhas de insumo ou de proveniência permanecem **fail-closed**.

## 5. Justificativa, impactos e rastreabilidade

**Contexto:** durante a PR #1450, o acompanhamento assistido permitiu identificar e corrigir falhas de CodeQL, Identity Foundation, TF4 e DT-05, confirmar aprovação física do DT-05 e explicitar a ausência de artefato histórico no Memory Batch. O preflight V2×V3 permaneceu sujeito à apuração da causa.

**Justificativa:** reduzir ciclos de diagnóstico, evitar correções especulativas, preservar provas por SHA e impedir que o histórico da conversa substitua os documentos oficiais.

**Impactos:** estabelece responsabilidade operacional e forma de prestação de contas; **não** altera critérios de CI, regras científicas, responsabilidades do Operador Master ou permissões de merge.

**Precedência:** GOV-DOC-01; políticas de segurança e de branch protection; decisões científicas/LOCKs V9; decisões do Operador Master. Em caso de conflito, prevalece a regra específica mais restritiva ou a decisão posterior expressamente aprovada pelo PO.

**Evidências de referência:** [PR #1450](https://github.com/lucianox777/Jornada/pull/1450), [DT-05 #44602944706](https://github.com/lucianox777/Jornada/actions/runs/44602944706), [Memory Batch #44602944087](https://github.com/lucianox777/Jornada/actions/runs/44602944087).

## 6. Vigência e revisão

Mandato aplicável ao acompanhamento assistido do projeto, observados os limites de cada tarefa e a disponibilidade das ferramentas autorizadas. Qualquer ampliação de autoridade, automação contínua ou dispensa de gate depende de nova decisão explícita e documentada do PO. A publicação nesta branch não equivale à integração na `master`.
