# GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116

**Autoridade:** Product Owner, 16/11/2026: após avaliação das alternativas A (gerar 100k dentro do TRAIN) e B (restaurar o workflow reutilizável de 15/11), aprovou explicitamente: "Concordo pode alterar". Esta decisão tem precedência **somente** sobre a persistência/reutilização da população sintética TRAIN 100k no workflow científico. GOV-DOC-01.

**Estado:** APROVADO PELO PO, **INTEGRADO À MASTER** em 16/11/2026 pela PR #1599. Os gates obrigatórios foram concluídos com sucesso; execução física das 324 hipóteses **não** é autorizada implicitamente por esta mudança.

## Contexto e justificativa

- A campanha [#43735943823](https://github.com/lucianox777/Jornada/actions/runs/43735943823) em 15/11 gerou localmente o corpus seed42 antes do TRAIN; 324/324 hipóteses, AUDIT e VALIDATION concluídos. A campanha [#43806078187](https://github.com/lucianox777/Jornada/actions/runs/43806078187) em 16/11 consumiu o ZIP persistido, também concluiu as 324 hipóteses, e foi aproximadamente 1h mais lenta no processamento físico.
- Os dados/planos e resultados científicos comparados são iguais, mas **não há comprovação de que a REF persistida causou** a diferença de desempenho; não usar promessa de aceleração como critério de aceite.
- O PO prioriza a simplicidade: gerar dados sintéticos automaticamente a cada TRAIN, manter recibos e resultados da campanha, sem buscar/publicar corpus sintético operacional na camada REF.

## Decisão substitutiva (escopo estrito)

1. Restaurar **apenas** o Step `Generate known-seed 100k clean TRAIN and bind new V3-only TF4 plan` do commit histórico `7de4f3d5966ab0ccf6fb9801d748893667949d0e` em `.github/workflows/v9-v3-four-name-100k.yml`: gerador `Jornada.Linkage.SyntheticCorpus`, perfil `clean`, 100.000 pessoas, `--seed 42`, saída efêmera `.local/v9-100k-technical/people-100k`. O gerador conclui antes da execução física das hipóteses.
2. O workflow científico principal não deve ler, gravar ou depender do ZIP/manifesto `Solution/data/reference/v9-synthetic-corpus-100000`, nem do commit experimental `9058c5dfe9b7dd6b8e77827fbaa55e5dc4faca50`. **Não apagar a evidência histórica**, os commits ou os artefatos já publicados.
3. Manter `v9-scale-100k-technical-plan.py`, manifesto, fingerprints, SHA-256 dos arquivos, binding do estudo e rejeição fail-closed para divergência; preservar proveniência e comparabilidade, com versão de gerador e parâmetros registrada. Qualquer futura mudança familiar que altere conteúdo exige nova proveniência, nunca sobregravar o estudo anterior.
4. Manter exatamente o plano TF4 V3 de 81 células/324 hipóteses, worker e SQL descartável, paralelismo AUTO, controles de RAM/CPU, `AUDIT` e `VALIDATION` em jobs independentes, handoffs intermediários essenciais, `TEST` científico bloqueado, segurança/autorizações e não publicação em produção.
5. Não mudar `.github/workflows/v9-tf4-execucao-testes.yml` porque o invocador já chama o reutilizável por `uses`; não restaurar código produtivo antigo nem eliminar artefatos de transferência inter-jobs. Os CSVs TRAIN temporários são descartados conforme workflow anterior, não publicados na REF.
6. Os workflows opt-in de prova de persistência e a PR #1567 **não** integram esta rota científica; permanecem como histórico/experimento não promovido e não autorizam reintrodução do consumo ou publicação de corpus sintético como REF na operação oficial.

## Precedência — o que muda e o que permanece

- **Supera parcialmente** `GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115` **apenas** quanto à exigência de persistir, compartilhar e reutilizar o corpus operacional sintético TRAIN 100k entre runs. **Permanecem vigentes** os requisitos científicos e de evolução familiar: obtenção de parâmetros familiares fundamentados, geração sintética de relações coerentes, distinção entre hipótese e referência oficial, padrão materno histórico de 10% até aprovação científica, e isolamento de gerador experimental.
- **Substitui operacionalmente** `GOV-V9-TRAIN-REF-PERSISTIDO-20261116` para o consumo do ZIP sintético no TRAIN. O histórico de sua implantação e as provas de integridade ficam preservados.
- **Não afeta** a REF estatística IBGE e a proveniência histórica seed42/30k, que continuam congeladas; não afeta VALIDATION independente nem TEST lacrado.

## Critérios de aceite e riscos

- Revisar diff do YAML para confirmar alteração **somente** do Step de preparação; teste automatizado garante geração local seed42 e ausência do consumidor REF no Job físico.
- CI obrigatória e guardrails completos sem relaxamento; comparar SHA do corpus regenerado ao baseline, se uma nova campanha física for autorizada; se divergente, bloquear comparação direta como mesmo estudo.
- Retenção dos recibos de geração, evidências de auditoria e relatórios finais. A regeneração pode custar algum tempo inicial e versões futuras do gerador podem alterar os bytes; não atribuir melhora de desempenho à reversão sem ensaio controlado.
- Nenhuma campanha física 100k ou TEST científico deve ser iniciada automaticamente por esta PR. TEST permanece não certificado, lacrado e sem publicação.

## Rastreabilidade

- Comparação: [workflow histórico de 15/11](https://github.com/lucianox777/Jornada/blob/7de4f3d5966ab0ccf6fb9801d748893667949d0e/.github/workflows/v9-v3-four-name-100k.yml) e [workflow com consumo REF de 16/11](https://github.com/lucianox777/Jornada/blob/7dcd2c70d5348be3f9e46e7487e6165b648d9c68/.github/workflows/v9-v3-four-name-100k.yml).
- Antecedentes: [decisão de corpus/REF familiar](Decisao_GOV_V9_Corpus_Persistente_REF_Familiar_20261115.md) e [integração técnica REF](Decisao_GOV_V9_TRAIN_REF_Persistido_20261116.md). Ambos preservados, com anotação de precedência.
- Integração documental e operacional concluída: PR [#1599](https://github.com/lucianox777/Jornada/pull/1599), squash [`cc844695bde37b3bf81546e58c899026964e7ad9`](https://github.com/lucianox777/Jornada/commit/cc844695bde37b3bf81546e58c899026964e7ad9), [CI obrigatória #43905763281](https://github.com/lucianox777/Jornada/actions/runs/43905763281) **SUCCESS**, [integração sintética TRAIN→AUDIT→VALIDATION #43905763347](https://github.com/lucianox777/Jornada/actions/runs/43905763347) **SUCCESS**, [prova de admissão #43905763346](https://github.com/lucianox777/Jornada/actions/runs/43905763346) **SUCCESS**. Nenhuma execução científica física 324 foi disparada por esta PR; TEST científico permanece bloqueado.

## Fechamento GOV-DOC-01 — 16/11/2026

A decisão, sua precedência parcial sobre a política de persistência sintética, os controles e o índice de decisões vigentes foram integrados junto ao workflow. O merge referido acima concluiu a reversão operacional **sem** autorizar ensaio físico nem revogar a fundamentação de geração familiar.
