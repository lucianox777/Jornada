# GOV-V9-PERSISTENCIA-SNAPSHOTS-20261112 — retenção dos snapshots V9

**Autoridade e contexto:** Product Owner, 12/11/2026, determinou resolver a perda de snapshots da calibração 324 hipóteses, sem refazer a otimização e aproveitando os parâmetros já encontrados.

**Constatação:** a execução GitHub Actions #43105369099 concluiu a seleção TRAIN (324 avaliações; três finalistas), mas o artefato publicado reteve apenas referências SHA dos snapshots e do envelope. O workflow descartou o ambiente efêmero após terminar. A execução de VALIDATION independente não ocorreu. Não é possível afirmar que os bytes originais dos snapshots ainda existem; SHA e métricas não substituem o arquivo completo.

**Correção preventiva:** o workflow `.github/workflows/v9-v3-four-name-100k.yml` passa a publicar arquivos `*.fitted.json` e `*.selection.json` junto com os relatórios, mantendo a mesma retenção de 30 dias. Essa alteração **não recupera retroativamente** os arquivos da execução #43105369099; eventual recuperação exige origem verificável e conferência de SHA. Se não houver cópia, qualquer reconstrução exige todos os parâmetros e prova de equivalência, sem fingir que o SHA original foi preservado.

**Precedência e impactos:** preservados Pareto 2D, filtros, até três finalistas, preferência por menor FP entre elegíveis, não-refit, coorte independente e TEST reservado. A mudança aumenta armazenamento do artefato e requer verificação de capacidade/retention. Nenhuma certificação ou ativação é autorizada. Referências: execução #43105369099, GOV-DOC-01 e decisão de finalistas FP de 11/11/2026.

## Inspeção adicional do artefato histórico (12/11/2026)

O ZIP do artefato `13908090146` da execução `43105369099` contém `bound/plan.json`, `results/mu-statistical-analysis.json` e `bound/technical-audit.json`. O relatório de origens dos parâmetros inclui **parâmetros declarados** por opção (`perOptionParameterOrigins`), mas não os arrays M/U **efetivamente estimados** e serializados nos `.fitted.json`; logo não demonstra reconstrução byte a byte do snapshot. O relatório técnico registra para os finalistas, na mesma ordem do `selection-reference.json`: SHA `016c2f4cc777...` (TP 22862, FP 7, FN 748, recall 0,9683185091, FDR observado 0,0003060912); SHA `24f119ace834...` (TP 22857, FP 4, FN 753, recall 0,9681067344, FDR observado 0,0001749705); SHA `0ff16d72df6c...` (TP 22808, FP 2, FN 802, recall 0,9660313427, FDR observado 0,0000876808). Todos têm `rejections: []` na auditoria TRAIN. **FP 2 é o menor FP observado no TRAIN, não resultado da VALIDATION independente nem aprovação por seus gates.**

A recuperação histórica permanece não comprovada; uma execução de VALIDATION exige os snapshots completos e coorte nova. A PR preventiva não inicia validação nem autoriza refit.
