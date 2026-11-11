# GOV-V9-100K-12-DE-24-20261111 — diagnóstico físico, correção de testes e auditoria incompleta

**Data:** 11/11/2026 (America/Sao_Paulo). **Origem:** mandato do PO de prosseguir na correção do otimizador V9 após novo erro, sem alterar os parâmetros científicos. **Política:** GOV-DOC-01. **Estado:** prova da falha recuperada; correção técnica de testes e relatório proposta em PR; **campanha 24/24 NÃO CERTIFICADA**.

## Evidência verificável

- [Campanha manual 100k run #42939482401](https://github.com/lucianox777/Jornada/actions/runs/42939482401), SHA `196ee9743446f144d19f3b7c3ea7bfbd923c73a5`, terminou `FAILURE` no job **Audit all V2 witness, V3/TF/MU fits, TP/FP/FN, FDR and resolution P95**.
- O Calibrador C# terminou sem erro de processo (`worker_exit=0`) e produziu relatório `results/mu-statistical-analysis.json`. Artefato imutável do run: **#13820548707**, digest `sha256:022b75439f680fb963ef7bfcb50500dd280e0454bbe04018e140cabb24e34c74`.
- **Contadores distintos do relatório:** `evaluations=24`, `measured=12`, `publishedTfCoverageInsufficient=12`, `scientificallyNonEvaluable=12`, `rejectedOrIneligible=20`. Todas as **12 hipóteses não medidas** têm `outcome=REJECTED_NOT_MEASURED` e `Rejections=[PUBLISHED_TF_COVERAGE_INSUFFICIENT]`; as **12 medidas** são controles TF0. **20 inelegíveis** inclui opções medidas mas diagnósticas/rejeitadas: não equivale a 20 hipóteses sem medição.
- O auditor Python falhou deliberadamente com `V9_100K_AUDIT_FAIL_CLOSED: 24/24 physical measurements incomplete`. Sua recusa ao PASS é **correta**; 24 solicitações físicas não são 24 fits mensurados. Logo, **não** resolver relaxando o gate, preenchendo `measured=24`, usando TF0 como substituto ou inventando frequência marginal IBGE.
- Os nomes não publicados podem ser reais/novos pós-2022, suprimidos, não observados ou afetados por grafia. O agregado não identifica qual causa vale para cada ocorrência. **Ausência da publicação IBGE não é evidência de erro/invalidade nominal.**

## Incidente de CI paralelo, independente da recusa científica

- `jornada-ci` da master em [#42939390649](https://github.com/lucianox777/Jornada/actions/runs/42939390649) e [#42950898371](https://github.com/lucianox777/Jornada/actions/runs/42950898371) falhou em `unit-runtime`. **Três testes** de `V9FsAdaptiveFirstNameTfTrainValidationTests.cs` ainda exigiam `InvalidDataException`, enquanto `RequireCompleteCoverage` hoje lança `PublishedCoverageInsufficientException : IOException` para permitir classificação segura da não-avaliabilidade. A falta do relatório OpenAPI é consequência do abort anterior do job; não identificar como quarta causa independente.
- Correção específica nesta PR: ajustar **somente as três asserções** para exigir o tipo real, mantendo contagem, sigilo, cenário de nome válido e checkpoint; esclarecer comentário da classe da exceção. **Não** converter o comportamento científico em PASS.

## Correção observacional proposta, sem mudar política científica

- `v9-scale-100k-technical-audit.py` continua falhando no caso **12/24**, mas passa a registrar `technical-audit.json` e `technical-audit.md` agregados com `INCOMPLETE_NOT_CERTIFIED`, medidas, não medidas e não-avaliáveis por TF, SHA e run ID, sem PII. Esses registros são para investigação e **não** correspondem ao artefato `V9_100K_MULTIPARAM_TECHNICAL_AUDIT_V1` de PASS.
- Testes offline no gate rápido PR cobrem a contagem 12/24, o erro fail-closed, a não divulgação de nome de exemplo, a ausência de falsa certificação e a distinção entre `evaluations` e `measured`.
- O modelo, scorer, `UnpublishedTermPolicy=RejectOperation` do plano técnico histórico, 81 células, M/U, TF0, limites FDR, snapshots e corpus permanecem intocados.

## Pendência científica bloqueante e precedência

**Falta decisão estatística aprovada** sobre o tratamento de termos válidos sem frequência publicada. A política existente `AbstainCandidate` é alternativa explícita para avaliação, mas implica deixar candidatos pendentes, possivelmente afetando recall e a comparação TF×TF0. Não selecioná-la silenciosamente em `plan.json`, nem aplicar piso imputado, neutralizar TF, mudar pesos ou declarar que o IBGE tem cobertura exaustiva. Avaliar alternativas com dados de treinamento adequados, split e FDR sob dependência antes de produção.

**Próximos gates:** regressões unitárias e auditor offline no HEAD da PR; squash merge após checks rápidos; CI e 30k impactado na `master`; 100k manual somente pelo proprietário e somente após desbloqueio científico, SHA e preflight. Sem TEST/Gold/ativação.

**Precedência:** complementa [GOV-V9-BASE-REAL-100K-BOOTSTRAP-20261110](Decisao_V9_Base_Inicial_Real_100k_Bootstrap_Configuracoes_20261110.md), [GOV-V9-100K-VALID-NAME-REPRO-20261110](Evidencia_V9_100k_20261110_Repro_Cobertura_TF_Nomes_Novos.md), [GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md) e GOV-DOC-01, sem revogar qualquer gate.
