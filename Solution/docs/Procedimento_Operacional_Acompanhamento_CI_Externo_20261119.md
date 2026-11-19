# Procedimento operacional — acompanhamento assistido da CI pelo GitHub (19/11/2026)

**Status:** procedimento observado e documentado a pedido do Product Owner; não substitui os gates oficiais nem institui nova política de aprovação. **Escopo:** PR #1450, extensível a outras PRs mediante conferência dos identificadores.

## O que é diferente

O assistente **não executa uma segunda CI fora do GitHub**. A compilação, os testes, os jobs, os artefatos e os status continuam sendo produzidos pelo **GitHub Actions**. O diferencial é a **orquestração de investigação externa aos jobs**: consultas autenticadas à API do GitHub via integração GitHub, leitura de estados e logs, correlação por commit, diagnóstico, correções pontuais em branch e documentação de evidências. Não há monitoramento autônomo contínuo entre mensagens; cada solicitação de continuidade dispara novas consultas. Uma consulta pontual não comprova conclusão futura.

## Procedimento reproduzível

1. **Fixar identidade da revisão.** Consultar a PR (número, branch, `head_sha`, situação de merge). Não misturar resultados de SHAs diferentes.
2. **Enumerar workflows do commit.** Consultar as execuções associadas ao `head_sha`, agrupar por `queued`, `pending`, `in_progress`, `completed/success`, `completed/failure`. Registrar total e momento da consulta. Não tratar `queued` como sucesso.
3. **Priorizar os gates.** Inspecionar `jornada-pr-required-fast` e workflows relacionados à mudança (neste caso TF4, DT-05, Identity Foundation, V2×V3 e Memory Batch). Conferir exigências de branch protection/rulesets antes de considerar merge.
4. **Investigar falhas concretas.** Buscar jobs e etapas com `failure`, depois logs do job; separar erro primário de falhas derivadas (por exemplo, upload de relatório inexistente após falha no download).
5. **Confrontar código e proveniência.** Ler workflow, testes, implementação, documentação e origem dos artefatos. Não inferir equivalência entre corpora apenas por seed/população. Validar fingerprints, hashes e contratos quando necessários.
6. **Corrigir o mínimo necessário.** Alterar a branch da PR com commit rastreável, preservar regras científicas, segurança e controles do Operador Master. Não iniciar TRAIN/TEST científico, promover modelo nem fazer merge sem autorização e gates.
7. **Registrar a decisão e evidências.** Atualizar documentos oficiais pertinentes e, quando houver decisão nova/aprovada, o `Indice_Decisoes_Vigentes.md`, conforme GOV-DOC-01. Diferenciar observação técnica de decisão do PO.
8. **Reconsultar a nova revisão.** Todo commit pode iniciar uma nova rodada de CI; sucesso de SHA anterior é evidência histórica, não aprovação automática da revisão atual. Reportar explicitamente pendências e links das execuções.

## Evidências da PR #1450

- DT-05: [run #44602944706](https://github.com/lucianox777/Jornada/actions/runs/44602944706) concluiu `success` para as três ondas sintéticas após fixar `Linkage__SelectedModelVersion` no contexto do teste. Evidência limitada à revisão testada.
- Memory Batch: [run #44602944087](https://github.com/lucianox777/Jornada/actions/runs/44602944087) confirmou `validate-runner=success` e `frozen-original-sqlite=failure` por artefato expirado do [run histórico #38593005691](https://github.com/lucianox777/Jornada/actions/runs/38593005691). Foi incluído diagnóstico explícito e **fail-closed**, sem substituir o corpus.
- V2×V3: [run #44600128868](https://github.com/lucianox777/Jornada/actions/runs/44600128868) apresentou `INVALID_DATA:UNCLASSIFIED` após compilação; a causa material não está demonstrada.
- TF4: correção de fixture sintética implementada; aprovação depende do workflow completo no SHA pertinente.
- A PR e os estados atualizados devem ser consultados em [PR #1450](https://github.com/lucianox777/Jornada/pull/1450).

## Limites e salvaguardas

- O assistente não substitui GitHub Actions, revisores, políticas de branch protection, aprovação do PO ou operador master.
- Não há polling contínuo ou execução em segundo plano neste procedimento; cada atualização depende de nova interação.
- Logs públicos devem preservar sanitização de erros e dados pessoais; diagnósticos DEV/HML completos só em destinos restritos autorizados.
- Status de PR `mergeable` isolado não significa que os checks obrigatórios passaram.
- Artefato ausente/expirado exige recuperação com identidade física comprovada; jamais trocar silenciosamente massa histórica.
- A documentação deve registrar SHA, run/job, evidência, hipótese versus fato, correção, impacto e resultado no novo SHA.

## Rastreabilidade e precedência

**Motivo:** tornar repetível e auditável o fluxo de investigação que o PO solicitou documentar. **Impacto:** clareza operacional, menos inferências e melhor triagem de CI; nenhuma mudança de critério científico ou de aprovação. **Precedência:** GOV-DOC-01; decisões de LOCK V9, Operador Master e segurança de diagnósticos prevalecem. **Natureza:** documentação de prática existente, sem nova autorização de execução científica.
