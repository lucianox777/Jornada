# GOV-V9-PERSISTENCIA-SNAPSHOTS-20261112 — retenção dos snapshots V9

**Autoridade e contexto:** Product Owner, 12/11/2026, determinou resolver a perda de snapshots da calibração 324 hipóteses, sem refazer a otimização e aproveitando os parâmetros já encontrados.

**Constatação:** a execução GitHub Actions #43105369099 concluiu a seleção TRAIN (324 avaliações; três finalistas), mas o artefato publicado reteve apenas referências SHA dos snapshots e do envelope. O workflow descartou o ambiente efêmero após terminar. A execução de VALIDATION independente não ocorreu. Não é possível afirmar que os bytes originais dos snapshots ainda existem; SHA e métricas não substituem o arquivo completo.

**Correção preventiva:** o workflow `.github/workflows/v9-v3-four-name-100k.yml` passa a publicar arquivos `*.fitted.json` e `*.selection.json` junto com os relatórios, mantendo a mesma retenção de 30 dias. Essa alteração **não recupera retroativamente** os arquivos da execução #43105369099; eventual recuperação exige origem verificável e conferência de SHA. Se não houver cópia, qualquer reconstrução exige todos os parâmetros e prova de equivalência, sem fingir que o SHA original foi preservado.

**Precedência e impactos:** preservados Pareto 2D, filtros, até três finalistas, preferência por menor FP entre elegíveis, não-refit, coorte independente e TEST reservado. A mudança aumenta armazenamento do artefato e requer verificação de capacidade/retention. Nenhuma certificação ou ativação é autorizada. Referências: execução #43105369099, GOV-DOC-01 e decisão de finalistas FP de 11/11/2026.
