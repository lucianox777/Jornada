# GOV-OPS-V9-01 — vigília e validação horária da implementação V9

**Decisão do Product Owner:** 07/11/2026  
**Origem:** solicitação expressa do PO em conversa do Projeto Jornada, para reativar a validação horária e prosseguir na implementação.  
**Estado:** **DECIDIDO** (governança operacional) e **AUTOMAÇÃO REATIVADA** na plataforma de agendamentos em 07/11/2026; **NÃO** é evidência de que cada ciclo já executou correções, testes, merges ou validação física.  
**Âmbito:** repositório `lucianox777/Jornada`, Jornada V9 e dependências pertinentes.  
**Regra documental:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## 1. Contexto e justificativa

O PO solicitou que a validação horária voltasse a acompanhar a implementação, diante de frentes V9 abertas e de trabalhos paralelos. A intenção é detectar bloqueios e efetuar correções seguras sem depender de solicitação repetida do PO a cada verificação. Há risco de automações e conversas diferentes atuarem sobre as mesmas PRs ou de um acompanhamento desatualizado tomar decisões com base em gates antigos. A alternativa de reativar várias vigílias sobrepostas foi preterida em favor de **uma vigília horária coordenadora**; ações pontuais e demais frentes permanecem independentes, sujeitas à verificação de não duplicação.

A dúvida do PO sobre quantos *chats* permanecem abertos não equivale a um inventário de sessões: o contador de **automações habilitadas** é verificável pela plataforma de agendamentos, mas não demonstra quantas conversas ou agentes estão abertos ou executando em outras superfícies.

## 2. Deliberação aprovada

1. Reativar **uma** tarefa agendada de vigília do GitHub a cada hora no fuso `America/Sao_Paulo`, com objetivo de verificar e, quando possível, dar continuidade às frentes da Jornada V9.
2. Em cada ciclo, consultar o estado **atual** de `master`, PRs abertas, HEAD SHA, conflitos, workflows, testes, logs e decisões documentadas; corrigir falhas efetivas em branch/PR, sem duplicar alterações de outras frentes.
3. Notificar o PO somente sobre avanço comprovado, bloqueio material ou intervenção necessária. Ciclos sem mudança relevante não geram relatório.
4. Manter isoladas as autorizações: vigília técnica **não** autoriza por si só novas campanhas científicas, Gold/TEST, DT-15, publicação ou ativação de modelo.

## 3. Restrições e precedência

- Prevalece a decisão superveniente de **06/11/2026, confirmada em 07/11/2026**, registrada no [índice de decisões vigentes](Indice_Decisoes_Vigentes.md): a PR executa checks rápidos; `jornada-ci` completa e regressão física técnica 30k/seed42 são disparadas **após o merge**, por `push` na `master`, no SHA integrado. Não exigir falsamente gate 30k pré-merge nesta topologia, nem tratar falha pós-merge como êxito.
- Só integrar PR autorizada para o escopo, reconciliada e sem conflito, com checks **pré-merge aplicáveis** no SHA exato aprovados, revisão de diff, exigências documentais satisfeitas e merge protegido por `expected_head_sha`. Após cada merge, verificar CI/30k pós-merge; se houver falha obrigatória, **bloquear novos merges** até correção ou reversão.
- Proibidos commits diretos em `master`, reclassificação/dispensa de gates para forçar aprovação, relaxamento estatístico para obtenção de resultado e interpretação de regressão sintética 30k como validação independente.
- Preservar corpus técnico seed42 congelado, `TRAIN/VALIDATION/TEST`, referências pinadas, FDR versionado no `plan.json` e o fluxo de autorização próprio para experimentos e ativação.
- Para qualquer nova decisão do PO durante a execução, aplicar [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md) no documento canônico e nos índices. O agendamento não substitui a documentação nem confere autonomia decisória normativa ao agente.

## 4. Situação, medição e rastreabilidade

A ferramenta de agendamentos confirmou, em **07/11/2026**, o sucesso da reativação da tarefa **“Jornada — vigília Git horária”** com recorrência `FREQ=HOURLY` e fuso `America/Sao_Paulo`. No recorte consultado havia **uma automação habilitada** para esse acompanhamento. Isso **não certifica** que exista apenas um chat aberto ou que uma execução esteja em andamento neste exato momento.

O repositório consultado no mesmo dia apresentava `master` `65659c681eb02620e49acd96196c5a53db54be9a`, com múltiplas PRs abertas, incluindo frentes V9. Esse SHA é uma **fotografia**, não um pin normativo nem uma afirmação de que CI/30k do SHA esteja verde.

**IMPLEMENTADO:** recorrência horária ativada na plataforma de automações.  
**MEDIDO/VALIDADO no GitHub:** cada intervenção futura exige evidência de PR, job, run e SHA próprio.  
**ATIVADO operacionalmente:** não há promoção de modelo nem implantação aprovada por esta decisão.  
**Pendente documental:** integração deste registro e sincronização dos índices por PR, respeitando os gates vigentes.

**Precedência:** esta decisão reabre o monitoramento periódico sem revogar regras anteriores de segurança, CI ou ciência; versões de prompts e vigílias antigas são históricas, não uma nova autorização de ações irrestritas.
