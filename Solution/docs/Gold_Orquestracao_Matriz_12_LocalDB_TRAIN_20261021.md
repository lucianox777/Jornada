# V9 Gold — orquestração física das 3–12 corridas (Windows/LocalDB)

**Situação (23/10/2026):** #1173, #1174, #1175, #1176 e #1177 já integram a master.
A PR #1178 adapta tanto o script quanto o comparador C# a k = 0–3 finalistas.
**Nenhuma matriz física 3/6/9/12 foi executada por esta mudança**.
O teste Windows comprovou apenas que `SqlLocalDB.exe info` funciona no runner
hospedado (GitHub Actions #39892149332); não comprovou provisionamento ou
publicação Gold no Windows. Ensaio exclusivamente TRAIN: TEST lacrado,
sem certificação, migration operacional ou ativação.

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
- O estudo declara **zero a três** candidatos distintos no campo
  `DescriptiveFinalistThresholds`, em `0 < T_inferior < T_superior`.
  Se k > 0, o primeiro coincide com `DescriptiveSuggestedLowerThreshold`
  (sugestão Pareto 2D). Na seleção V6, as outras alternativas provêm
  dos representantes **das faixas descritivas de falsa criação**
  (0,5%/1%/2%/5%/100%) — preferência às faixas não vazias mais estrita
  e mais ampla, com preenchimento por faixas intermediárias distintas.
  Em qualquer k, os slots seguintes são `FINALIST_2`/`FINALIST_3`;
  não se presume `CONSERVATIVE` ou `LOWEST_PENDING` como ótimos absolutos.
  Com k = 0, o joelho deve ser nulo.
  Quando o Pareto fornecer menos de três valores distintos, **não inventar**
  finalistas: executar três ordens × (CURRENT + k finalistas), com k entre 0 e 3;
  o arquivo agregado grava `FinalistCount` e `ExecutedTrialCount`. O calibrador
  atual normalmente fornece pelo menos um finalista; zero é contrato para
  ausência explícita de sugestão, não fallback de JSON inválido.
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
3–12 execuções, cria LocalDB independente e executa na ordem:
CURRENT (controle) e k finalistas distintos, na ordem exportada pelo Pareto; em cada política,
baseline, reversa e intercalada. Não reutiliza banco entre ordens ou
políticas; usa o mesmo fit e corpus. Preserva pastas, fontes SQL e evidências
dos ensaios, mesmo se alguma execução falhar. **Não realiza limpeza**.

As 3–12 saídas `gold-runner-study.json` são validadas pelo comparador
C# existente, que exige a mesma massa sintética/fit/política por trio, SHA
de procedure experimental, contagens e um fingerprint distinto para cada tentativa de runs distintos.
A saída final é `gold-12-trial-matrix.json` (nome histórico preservado mesmo para 3/6/9 corridas) com diagnósticos agregados:
cobertura, pares, B-cubed, fusões, fragmentação, FDR, pendências por motivo,
falsas criações, latência e partição. Em particular, **diminuir pendências
não demonstra segurança de criar identidades**.

**Compatibilidade da evidência:** a execução [#40124876599](https://github.com/lucianox777/Jornada/actions/runs/40124876599), iniciada em branch experimental de 24/10,
foi compilada com o seletor **V5** (joelho + extremos absolutos) e não deve
ser confundida com a V6 proposta nesta correção (joelho + representantes
das faixas). Preservar os artefatos antigos, não trocar limiares durante
um run e nunca fazer merge do gatilho temporário `push` para a master.

## Workflow manual Windows no GitHub Actions (PR #1178)

O job automático `v9-gold-matrix-script-parse` verifica a sintaxe, os
invariantes estáticos e a disponibilidade real de `SqlLocalDB.exe` no
runner Windows. **Não** cria bancos nem executa o ensaio físico. No run
#39892149332, localizou `C:\\Program Files\\Microsoft SQL Server\\170\\Tools\\Binn\\SqlLocalDB.exe`
e recebeu `MSSQLLocalDB` como resposta do comando `info`.

Após a integração da PR #1178, abrir **GitHub → Actions →
v9-gold-physical-train-matrix → Run workflow**, na `master`, informando
uma grade **pré-declarada manualmente**. Os três parâmetros são:

- `lower_thresholds`: valores decimais separados por vírgulas, todos
  distintos no intervalo `0 < x < 0.8671875`. Exemplo **apenas de
  sintaxe**: `0.1,0.2,0.3` (não são limiares recomendados/definitivos).
- `minimum_independent_identity_groups`: suporte mínimo de grupos
  independentes de identidade, inteiro maior ou igual a 2.
- `minimum_already_represented_sources`: suporte mínimo de fontes
  já presentes no Gold anterior à onda, inteiro maior ou igual a 1.

**Localização do executável no GitHub Windows.** A instalação do
`SqlLocalDB.exe` foi confirmada na imagem hospedada, mas a pasta
`Microsoft SQL Server/170/Tools/Binn` não deve ser presumida no
`PATH`. O workflow pesquisa exclusivamente a instalação do LocalDB
no host Windows, falha se o executável não existir, publica a pasta
encontrada em `GITHUB_PATH` e verifica novamente a visibilidade de
`SqlLocalDB.exe info` **no passo seguinte**. Sem isso, um sucesso
isolado do preflight não asseguraria que o orquestrador em outro
processo PowerShell pudesse localizar o comando; jamais substituir
esse executável por um servidor SQL operacional.

O workflow `workflow_dispatch`, nunca automático, regenera o corpus
sintético histórico clean seed42 de 30 mil identidades e **confere seu
SHA** contra `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`.
Descompacta o envelope de seleção de ~951 MB, confere o SHA
`b98121f9b4854e0db56026df1ddaef30e0f8c32717baa88a68549bd0ad08d75a`,
usa os snapshots congelados e a validação pré-existente, **sem reabrir
VALIDATION/TEST**. Se qualquer verificação falhar, o ensaio aborta; não
substituir o CSV ou o fit para forçar um resultado.

O workflow cria um LocalDB exclusivo para o estudo `CURRENT` →
`lower-train-pareto.json`, depois novos LocalDB para as três ordens de
cada política (3 × (1+k) corridas, k entre 0 e 3). Publica somente os
agregados `lower-train-pareto.json`, `gold-runner-study.json`
e `gold-12-trial-matrix.json`, se produzidos. O nome da matriz é legado,
mas o conteúdo V2 deve trazer `FinalistCount` e `ExecutedTrialCount`.
Sucesso do workflow é evidência da execução naquele runner, **não prova
independência estatística** nem autoriza promover a política.

**Após o resultado:** analisar falsa criação física segundo o ledger
#1176, pendências por motivo, falsos pares/associações, B-cubed,
fragmentação, sensibilidade à ordem e intervalos de incerteza. Seguir
a sequência governada: matriz TRAIN → congelamento de política →
validação em massa **nova** no Gold incremental → TEST lacrado com
autorização → governança/DT-15. Se aprovada uma mudança operacional,
são exigidas migration canônica DT-10 e novos testes; nenhuma migration
experimental é transferida automaticamente.


## Primeiro ensaio físico — bloqueio DT-10 e correção direcionada (24/10/2026)

No [run #40124876599](https://github.com/lucianox777/Jornada/actions/runs/40124876599)
o estudo Pareto TRAIN V5 e as três ordens CURRENT foram concluídos e
publicados como **artefatos parciais**, mas a primeira tentativa
experimental KNEE abortou em
`V9GoldDisposableLowerProcedurePatch.Apply`, antes de qualquer
publicação experimental. Não existe `gold-12-trial-matrix.json` desse
run; resultados de prévia não podem ser confundidos com métricas Gold
físicas da política inferior. Ver [issue #1180](https://github.com/lucianox777/Jornada/issues/1180).

O [diagnóstico físico isolado #40132062400](https://github.com/lucianox777/Jornada/actions/runs/40132062400)
confirmou a causa específica: o SQL Server 17 devolve
`OBJECT_DEFINITION` começando por
` CREATE   PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote`,
embora a migration-fonte use `CREATE OR ALTER PROCEDURE`.
O predicado de motivo `l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'`
continuava presente **uma vez**, a regra `THROW 51810` existia e
`LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL` **não** estava instalado.
A comparação literal do cabeçalho rejeitou uma normalização legítima
do motor, e a trava falhou fechado.

A correção limita-se ao reconhecimento do cabeçalho SQL Server,
aceitando a forma canônica e a normalização de `OR ALTER` com
espaçamento variável **da mesma procedure**, preservando as checagens
da ocorrência única do guard original, da regra `THROW 51810` e da
ausência do motivo experimental. Não altera DT-10, schema canônico,
a política Gold operacional ou os hashes da massa sintética. O patch
continua restrito ao banco `JornadaHarness` LocalDB próprio, recém
criado e vazio. Um novo run físico requer código integrado, grade e
versão registrados antes do novo disparo.

