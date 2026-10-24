# V9 — estudo físico manual do limiar inferior sobre o Gold TRAIN

**Status (23/10/2026):** CLI integrada na `master` pela PR #1177; PR #1178
adapta o comparador/orquestrador a 3/6/9/12 corridas. A cadeia física
completa **ainda não foi executada** nem certificada.
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
O estudo **não executa** automaticamente a matriz Gold. O número de
corridas será definido por `k` finalistas reais: 3 × (1+k), de 3 a 12.

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

## Seleção descritiva por Pareto 2D e faixas (correção de 24/10/2026)

A versão **V6** de `V9LowerNonMatchTrainCalibrator` utiliza a fronteira
Pareto de **falsas novas referências × fontes pendentes** (ambos minimizados)
calculada no **mesmo TRAIN**. A sugestão primária continua sendo o ponto de
menor distância quadrática ao ideal, com normalização pelos extremos da
fronteira — denominado `DescriptiveSuggestedLowerThreshold`; não é
aprovação de política operacional.

As faixas de **taxa observada** de falsas criações entre todas as criações
são `0,5%, 1%, 2%, 5%, 100%`, e são apenas resumos descritivos da
fronteira, **não orçamentos prévios de duplicatas**. Para cada faixa
com ponto elegível, `DescriptiveBands` registra aquele que deixa menos
pendências, com desempate determinístico por menos falsas criações e
menor limiar. Faixas vazias continuam declaradas como vazias.

Os `DescriptiveFinalistThresholds` (máximo três valores distintos) são,
por ordem: sugestão Pareto 2D; representante da **faixa mais estrita não
vazia**; representante da **faixa mais ampla não vazia**. Empates são
deduplicados; se houver vaga, representantes de faixas intermediárias
distintos completam até três. Nenhuma alternativa é criada artificialmente
para preencher a matriz. **Não** substituir os representantes das faixas
pelos extremos absolutos de falsa criação/pendência sem relação com elas.
A escolha da fronteira não certifica nível de risco, e a matriz física
servirá para aferir estabilidade nas três ordens, não para escolher o
vencedor pela ordem mais favorável.

Os slots de matriz são `CURRENT`, `KNEE`, `FINALIST_2` e
`FINALIST_3` conforme efetivamente presentes; rótulos como
`CONSERVATIVE` e `LOWEST_PENDING` seriam enganosos quando uma faixa
intermediária é selecionada. O JSON de estudo contém a versão V6 e
fingerprint que inclui a versão do seletor.

**Não reescrever resultados já em andamento:** a execução física
[#40124876599](https://github.com/lucianox777/Jornada/actions/runs/40124876599)
foi iniciada em 24/10/2026 no commit `7870209ebab69c3dd565ad6cfe197717a59263d5`,
que ainda usa a seleção **V5** (joelho + extremos absolutos). Ela
permanece um ensaio TRAIN exploratório de protocolo V5; **não** alegar
que o arquivo `lower-train-pareto.json` dessa execução segue a V6.
Uma avaliação V6 exige novo run pré-declarado após integração, sem
contaminar o resultado histórico ou usar TEST.

## 3. Execução integrada no GitHub Actions

Para evitar depender do computador local, a PR #1178 também adiciona o
workflow manual `v9-gold-physical-train-matrix`, com os três inputs
`lower_thresholds`, `minimum_independent_identity_groups` e
`minimum_already_represented_sources`. A grade e os mínimos são definidos
manualmente **antes da avaliação**, sem usar TEST. O workflow confere SHA
do CSV sintético TRAIN histórico, da seleção descomprimida e dos snapshots
congelados; cria instâncias LocalDB novas para o estudo e cada tentativa,
e publica somente os agregados `lower-train-pareto.json` e
`gold-12-trial-matrix.json` (nome legado). Uma incompatibilidade de hash
interrompe a execução, sem fallback de corpus. O ensaio é longo e sujeito
ao limite de tempo do job; sucesso da verificação de `SqlLocalDB.exe info`
no Windows Actions não representa execução nem certificação da matriz.

A operação está descrita com os parâmetros e os cuidados de governança
em `Gold_Orquestracao_Matriz_12_LocalDB_TRAIN_20261021.md`.

## 4. Pendências para decisão operacional

A execução física usa `V9GoldDisposableLowerProcedurePatch` **apenas**
em LocalDB isolado. Antes de operar a política inferior fora do ensaio
será necessária uma **migration canônica DT-10**, novo teste completo
de publicação e autorização DT-15, preservando as salvaguardas
de composição e governança. Nenhuma migration ou ativação fazem parte
da geração de Pareto ou da matriz em TRAIN. A saída da matriz não é
certificado de FDR/duplicação, e TEST permanece lacrado.
