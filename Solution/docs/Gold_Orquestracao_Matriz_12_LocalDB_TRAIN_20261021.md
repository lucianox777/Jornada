# V9 Gold — orquestração física das doze corridas (Windows/LocalDB)

**Situação:** script de orquestração preparado; **as doze execuções físicas não
foram realizadas** por esta alteração. A ferramenta depende de o Runner C# e o
comparador da PR #1174 estarem compilados e aprovados. É um ensaio de TRAIN;
não abre TEST, não certifica identidade e não ativa modelo.

## Pré-condições

- Windows com `SqlLocalDB.exe` instalado e acessível no PATH; SQL Server
  LocalDB descartável (não SQL Server operacional, não `JornadaLocal`).
- `Jornada.Linkage.Runner.dll` de build Release **que já contenha** a integração
  `LOWER_NONMATCH` e o comparador `--v9-gold-compare-matrix` (#1174).
- `Solution/database` com todas as migrations canônicas, envelope descomprimido
  `<SHA>.selection.json`, `finalist-validation.json`, variante Gold
  `<SHA>.fitted.json`, CSV sintético TRAIN original imutável (correspondente
  à assinatura de seleção) e estudo diagnóstico do
  `V9LowerNonMatchTrainCalibrator.Search` gerado por comando **manual**.
- O estudo deve conter exatamente **três** candidatos distintos no campo
  `DescriptiveFinalistThresholds` (joelho, conservador, menor pendência), em
  `0 < T_inferior < T_superior`, e a sugestão do joelho no primeiro índice.
  Se o Pareto resultar em menos de três valores distintos, **não inventar**
  valores para satisfazer a matriz 3×4: o script recusa a execução.
- A origem do estudo TRAIN/independência de rótulos deve ser auditada
  previamente por responsável; o JSON e seus SHAs são verificações
  de consistência, **não são atestação independente** de proveniência.

## Execução manual

Em PowerShell 7.2+ no Windows, usando **diretório de saída inexistente**:

```powershell
pwsh -NoProfile -File Solution/scripts/v9-gold-12-trial-localdb.ps1 `
  -RunnerDll "Solution/src/Jornada.Linkage.Runner/bin/Release/net10.0/Jornada.Linkage.Runner.dll" `
  -DatabaseSchema "Solution/database" `
  -TrainSelectionEnvelope "C:/V9-evidence/<SHA>.selection.json" `
  -FinalistValidation "C:/V9-evidence/finalist-validation.json" `
  -GoldFittedSnapshot "C:/V9-evidence/<SHA-GOLD>.fitted.json" `
  -OriginalTrainCsv "C:/V9-evidence/observacoes.csv" `
  -LowerTrainStudy "C:/V9-evidence/lower-train-pareto.json" `
  -OutputRoot "C:/V9-gold-trials/<NOVA_EXECUCAO>"
```

O script invoca a CLI `--v9-gold-harness-create` para **cada uma** das
doze execuções, cria LocalDB independente e executa na ordem:
CURRENT (controle), KNEE, CONSERVATIVE, LOWEST_PENDING; em cada política,
baseline, reversa e intercalada. Não reutiliza banco entre ordens ou
políticas; usa o mesmo fit e corpus. Preserva pastas, fontes SQL e evidências
dos ensaios, mesmo se alguma execução falhar. **Não realiza limpeza**.

As doze saídas `gold-runner-study.json` são validadas pelo comparador
C# existente, que exige a mesma massa sintética/fit/política por trio, SHA
de procedure experimental, contagens e 12 fingerprints de runs distintos.
A saída final é `gold-12-trial-matrix.json` com diagnósticos agregados:
cobertura, pares, B-cubed, fusões, fragmentação, FDR, pendências por motivo,
falsas criações, latência e partição. Em particular, **diminuir pendências
não demonstra segurança de criar identidades**.

O workflow `v9-gold-matrix-script-parse` somente valida a sintaxe
PowerShell e invariantes estáticas do script. **Ele não executa LocalDB, não
consome o corpus sintético e não constitui evidência física.** A matriz
deverá ser efetivamente executada e auditada em máquina Windows descartável,
com avaliação posterior de sensibilidade à ordem e erros por identidade.
A simples execução do script não altera a governança de DT-15 nem aprova
publicação operacional.
