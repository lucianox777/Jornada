# V9 — estudo físico manual do limiar inferior sobre o Gold TRAIN

**Status:** implementação em PR, sem execução das 12 corridas e sem certificação.
A fonte física do Pareto é a execução da política **CURRENT**, em quatro
ondas da ordem `BASELINE_G0_G1_G2_G3`, no **mesmo C# Runner** congelado.
A geração do estudo é manual; nenhum modelo é promovido, TEST é lacrado
e nenhuma procedure de produção é modificada.

## 1. Proveniência que não pode ser fabricada

`--v9-lower-nonmatch-study` reside no executável isolado `Jornada.Linkage.Runner`
e executa `V9GoldHarnessCommand.RunTrainStudyAsync`, que passa pelas
mesmas travas de LocalDB do ensaio Gold: nome reservado da instância,
`JornadaHarness`, `SERVERPROPERTY('IsLocalDB')`, marcador residente,
schema intacto e banco vazio. Não usa outro banco por fallback.

O scorer V9 recupera a união SQL completa, sem TOP ou paginação,
constrói `VerifiedRetrieval` e registra em memória a decisão **ORIGINAL**
de `Rank→Resolve` *antes* de a onda ser publicada. Após a publicação,
o harness associa a decisão ao ID da fonte do CSV congelado e à
verdade sintética independente do score: `GoldAlreadyContainsTrueIdentity`
é calculado contra a composição Gold **anterior à onda**, não com a
partição final. Homônimos idênticos podem partilhar o mesmo score e frame,
mas cada observação continua sendo contada pelo seu ID único. Divergência
de score para duas observações idênticas da mesma onda é erro.

Depois das quatro ondas e reavaliação final, somente em memória,
o Runner converte a evidência em `TrainingSource` e invoca
`V9LowerNonMatchTrainCalibrator.Search` implementado uma única vez
no assembly compartilhado `Jornada.Linkage.Core`.
O coordenador adaptativo do Worker também usa esse único método Core.
O SHA da evidência física original, os hashes de fonte/grupo e a
grade declarada compõem a proveniência do estudo. O relatório físico
agregado `gold-runner-study.json` e o agregado
`lower-train-pareto.json` são preservados; **nenhum arquivo com UUIDs de
candidatos, IDs individuais, labels por fonte ou decisões por fonte é
exportado**. Os labels sintéticos foram declarados no corpus TRAIN:
não são uma certificação independente de dados municipais reais.

## 2. Execução Windows manual em LocalDB exclusivo

Pré-requisitos: Windows + LocalDB + PowerShell; build Release do
`Jornada.Linkage.Runner` com o calibrador compartilhado em `Jornada.Linkage.Core`,
Solution/database canônica, envelope de seleção, validação de finalista,
variante GOLD fitted, seu snapshot-pai na mesma pasta do envelope, e
CSV original correspondente ao SHA daquele envelope.

Primeiro criar **uma instância nova** com Runner:

```powershell
dotnet Solution/src/Jornada.Linkage.Runner/bin/Release/net10.0/Jornada.Linkage.Runner.dll --v9-gold-harness-create C:/V9-train/study-db-UNICO Solution/database
```

Ler `C:/V9-train/study-db-UNICO/schema-ready.json` e configurar
`ConnectionStrings__Jornada` com a instância gerada. Por exemplo:

```powershell
$ready = Get-Content C:/V9-train/study-db-UNICO/schema-ready.json -Raw | ConvertFrom-Json
if ($ready.completed -ne $true -or $ready.database -ne 'JornadaHarness' -or
    $ready.instance -cnotmatch '^JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$') { throw 'LocalDB inválido' }
$env:ConnectionStrings__Jornada = "Server=(localdb)\$($ready.instance);Database=JornadaHarness;Integrated Security=true;Encrypt=Optional"
```

Criar `C:/V9-train/grade-pisos.json` **antes da execução**, declarando
`CandidateLowerThresholds` (grade exploratória dentro de `0 < piso <
Threshold` do fit, com valores únicos), `MinimumIndependentIdentityGroups`,
`MinimumAlreadyRepresentedSources`, `ManualCalibrationRequested=true`,
`TestUsed=false`. Os limites de suporte e a grade precisam ser decididos
explicitamente pelo operador: não vêm de TEST e não são parâmetros do
modelo em produção. O CLI recusa JSON sem contrato ou campos extras. Exemplo **ilustrativo**
de estrutura de arquivo (os números devem ser declarados segundo o
intervalo permitido pelo fit, não escolhidos como parâmetros definitivos):

```json
{
  "CandidateLowerThresholds": [0.1, 0.2, 0.3, 0.4, 0.5],
  "MinimumIndependentIdentityGroups": 2,
  "MinimumAlreadyRepresentedSources": 1,
  "ManualCalibrationRequested": true,
  "TestUsed": false
}
```

```powershell
dotnet Solution/src/Jornada.Linkage.Runner/bin/Release/net10.0/Jornada.Linkage.Runner.dll `
  --v9-lower-nonmatch-study `
  C:/V9-evidence/SELECTION_SHA.selection.json `
  C:/V9-evidence/finalist-validation.json `
  C:/V9-evidence/GOLD_SHA.fitted.json `
  C:/V9-evidence/observacoes.csv `
  C:/V9-train/grade-pisos.json `
  C:/V9-train/study-report-UNICO
```

Saídas: `study-report-UNICO/gold-runner-study.json` (controle físico)
e `study-report-UNICO/lower-train-pareto.json` (Pareto TRAIN).
A pasta deve ser inexistente antes da execução. Não usar dados reais.
O estudo **não executa** automaticamente as 12 corridas.

A segunda etapa segue
`Gold_Orquestracao_Matriz_12_LocalDB_TRAIN_20261021.md`:
forneça `-LowerTrainStudy` apontando ao `lower-train-pareto.json`.
O orquestrador aceita **k = 0, 1, 2 ou 3 finalistas distintos**;
executa exatamente `3 × (1 + k)` tentativas em LocalDB exclusivos,
sempre incluindo as três ordens do controle CURRENT. A fronteira pode
legitimamente retornar um ou dois, e o contrato também aceita zero
explicitamente declarado, sem inventar valores. O JSON da matriz contém
`FinalistCount` e `ExecutedTrialCount`; o nome histórico do arquivo
continua `gold-12-trial-matrix.json`, mesmo quando executa 3/6/9 corridas.
O calibrador atual, enquanto tiver grade não vazia, normalmente sugere
pelo menos um limiar; portanto `k=0` é compatibilidade com a hipótese
futura de nenhuma alternativa, não uma saída usual da implementação.

## 3. Pendências para decisão operacional

A execução física usa `V9GoldDisposableLowerProcedurePatch` **apenas**
em LocalDB isolado. Antes de operar a política inferior fora do ensaio
será necessária uma **migration canônica DT-10**, novo teste completo
de publicação e autorização DT-15, preservando as salvaguardas
de composição e governança. Nenhuma migration ou ativação fazem parte
da geração de Pareto ou da matriz em TRAIN. A saída da matriz não é
certificado de FDR/duplicação, e TEST permanece lacrado.
