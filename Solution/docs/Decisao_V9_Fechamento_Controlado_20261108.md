# GOV-V9-FECHAMENTO-1345-20261108 — Fechamento definitivo e segregação de evolução

**Autoridade:** Product Owner, mandato explícito recebido em **08/11/2026 BRT**. **Estado normativo:** APROVADO. **Estado da execução:** EM VERIFICAÇÃO, **não homologado**. **Repositório:** `lucianox777/Jornada`. **Política:** GOV-DOC-01; decisão técnica, funcional, operacional e de governança que deve acompanhar o código na `master`.

## Decisão superveniente, contexto e precedência

O PO determinou encerrar **somente o mandato original da integração V9**, com evidência por SHA para cada item **a–m** da [#1345](https://github.com/lucianox777/Jornada/issues/1345), triagem de todas as issues, revisão nominal de 31 menções PRODAM na especificação v5.00, atestado de testemunha V2 e documentação de workflows. A classificação conclusiva deve ser **CONCLUÍDO**, **CONCLUÍDO COM RESSALVAS** ou **NÃO CONCLUÍDO** conforme provas, não segundo aparência de CI verde.

**Esta decisão é posterior e prevalece no que conflitar** com a [GOV-V9-FINAL-20261108-B](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md), sua AD01 (que dispensou o passo 8 de triagem de issues) e com a antiga meta de `zero PRs abertas`. A triagem foi **reinstalada expressamente** e a presença de **exatamente duas PRs de evolução posterior, preservadas em rascunho**, é agora requisito de governança, não defeito de integração. Ficam preservados os fatos e a rastreabilidade dos mandatos anteriores, sem apagar ou reescrever evidências históricas.

## Separação das frentes

**Frente A — fechamento:** somente defeitos impeditivos, verificação a–m, triagem, especificação e documentos de evidência. Não criar campanha adicional de 100k multiparâmetro V9, teste Gold/TEST novo, benchmark GPU, Terraform, novos modelos, novos critérios de seleção ou automação de pesquisa. **Provas históricas** podem ser usadas para contexto e análise, mas não elevadas a `PASS` de SHA mais novo.

**Frente B — evolução autorizada, congelada durante A:**

- [PR #1351](https://github.com/lucianox777/Jornada/pull/1351): rampa HTTP 10k, **DRAFT ABERTA**. Manter HEAD, commits, artefatos, testes, resultados existentes; não avançar implementação nem mesclar antes do fechamento A.
- [PR #1352](https://github.com/lucianox777/Jornada/pull/1352): calibrador exploratório **autorizado expressamente pelo PO depois do mandato original**, **DRAFT ABERTA**. Não classificar como trabalho ilegítimo; preservar plano aprovado, `REF`, documentação, commits e testes. Retomar apenas após fechamento A.
- Nenhuma dessas duas PRs será fechada, reescrita ou apagada sem autorização expressa. Nenhum agente ou rotina pode interpretar sua mera abertura como pendência do aceite da integração.

**Provas de escala sem extrapolação:** [#1347](https://github.com/lucianox777/Jornada/pull/1347) rodou 100k com **motor operacional V8** em SQL e não certifica V9 multiparâmetro 100k (lacuna [#1349](https://github.com/lucianox777/Jornada/issues/1349)). [#1350](https://github.com/lucianox777/Jornada/pull/1350) comprovou **1k HTTP→Serving**, não 10k/100k nem p95 multirremessa ([#1348](https://github.com/lucianox777/Jornada/issues/1348)).

## Regras de execução e de aceite

1. Conferir SHA integral atual da `master` antes de intervenção e correlacionar cada `PASS` a **run/job/artifact/commit**. É proibido importar `PASS` do SHA de estágio para a master final.
2. Cada item a–m registra requisito, código/manifesto correspondente, teste, SHA, execução, resultado **PASS/FAIL/NOT RUN/BLOCKED**, evidência, defeito, correção e situação final. Qualquer evidência obrigatória ausente impede `CONCLUÍDO`; não renomear `SKIPPED` como físico executado.
3. Manter `.github/workflows/v9-medium-scale-e2e-technical.yml` e `v9-http-volume-1k-e2e-technical.yml` com `workflow_dispatch` manual e guarda master-only. O gate `v9-physical-seed42-probe.yml` permanece **push-only master**, classificador de impacto acumulativo e físico 30k quando exigido. Não modificar política já aprovada.
4. Neutralizar **somente** vínculos de fornecedor nas 31 referências da especificação v5.00, preservando história, citações jurídicas, enum SQL e negativas de chamada externa; nenhum replace global. As pendências em oito PDFs/51 menções, já diferidas na [#1344](https://github.com/lucianox777/Jornada/issues/1344), continuam visíveis sem simular neutralização total.
5. A **V2 WITNESS+ANCHOR** no mesmo plano é obrigatória como comportamento/hashes/auditoria. Avaliar geração determinística, preservação do plano efetivo e hash por opção e vínculo com resultado. **Não exigir plano estático persistido antes da execução** se plano efetivamente executado e proveniência já forem imutáveis e auditáveis; se houver exigência explícita de versionamento, mantê-la no contrato.
6. Triar todas as issues em P0/P1/P2/resolvida/absorvida, com justificativa e referência; fechar apenas resolvidas/absorvidas com evidência, não usar volume de abertas como KPI. A classificação de risco atual é distinta de prioridade histórica de outras frentes.
7. Corrigir defeitos impeditivos encontrados, sem alterações da `REF`, do plano aprovado, defaults V2/V3, locks científicos, Git history, hashes, `DT-15`, TEST, HML/Produção ou segurança. Não introduzir Terraform ou compatibilidade retroativa desnecessária.
8. A proteção de `master` exige PR. Concentrar documentação e correções pertinentes nesta **única frente de fechamento**; somente mesclar com checks aplicáveis no HEAD exato e depois verificar CI/30k no SHA integrado. Revisão documental pós-merge deverá registrar o novo SHA, sem declarar validação que não ocorreu.
9. Escalar ao PO apenas conflito real de requisito, mudança de escopo ou risco importante sem orientação existente; não pedir autorização redundante para decisões já explicitadas.

## Efeitos, limites e documentação obrigatória

**Impacto:** reduz acoplamento de fornecedor em texto normativo, impede transposição indevida de SCALE V8 para V9, evita contaminar testes por PRs posteriores e torna explícitas lacunas do teste geral. A CI verde comprova testes que de fato executou; **não substitui** cluster/instalador/validação independente, comparação histórica final ou ensaio 160 configurações do OOM sem execução equivalente.

**Critério de conclusão:** relatório A–G do SHA final, com cada a–m validado, triagem completa, 31 ocorrências revisadas, V2 testemunha auditada, gates do SHA final e lista de pendências não impeditivas. **Estado atual: NÃO CONCLUÍDO enquanto faltar prova obrigatória.** Isso não reprova funcionalidade nunca executada; aplica `NOT RUN` ou `BLOCKED` conforme o caso.

**Registros oficiais integrantes:**

- [Relatório a–m e classificação A–G](Relatorio_V9_Fechamento_Integracao_20261108.md).
- [Triagem das 57 issues, com justificativa](Triagem_Issues_Fechamento_V9_20261108.md).
- [Matriz contextual de 31 referências PRODAM](Revisao_PRODAM_v5_00_20261108.md).
- [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md), [índice documental](Indice_Acervo_Documental.md), [issue central #1345](https://github.com/lucianox777/Jornada/issues/1345).

**Precedentes científicos preservados:** [decisão V2 testemunha](Decisao_V9_Multiparametro_V2_Testemunha_Permanente_20261107.md), [CI em lote](Decisao_V9_CI_Lote_Merge_Unico_20261108.md), [diferimento PDF](Decisao_V9_Diferimento_PDF_20261108.md), [E2E físico 1k](Resultado_V9_E2E_HTTP_1K_20261108.md) e [SCALE V8 100k](Resultado_V9_Scale_Medium_100k_20261108.md).
