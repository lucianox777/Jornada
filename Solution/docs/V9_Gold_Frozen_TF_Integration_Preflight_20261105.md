# V9 — TF congelado no Gold de diagnóstico: preflight de integração (05/11/2026)

**Escopo desta PR:** alteração mínima no construtor de variante Gold **experimental**, sem refit e sem publicação operacional. Não altera os modelos/snapshots históricos, não modifica os limites TF0 × ONE_MF das seeds 42–57, não aciona 30k e não desbloqueia DT-15.

## Estado comprovado pelo código

1. `V9FittedSnapshot.CreateGoldHarnessVariant()` anteriormente recusava quaisquer unidades TF ativas mesmo quando `V9_FITTED_SNAPSHOT_V3` já tinha suporte conjunto e todas as calibrações TF congeladas e validadas. A transformação de Gold altera somente `CandidateGrouping` e `GoldParentSnapshotSha256`, produzindo um **novo** SHA por conteúdo. Agora admite a mesma transformação sem apagar TF ativa.
2. `V9FittedSnapshot.FreezeAndValidate` continua verificando os parâmetros e a referência TF; a chamada recusa fit sem `JointSupport`, com schema incompatível, sem BLIND ou já convertido em GOLD. O contrato de publicação operacional não foi tocado.
3. O teste `Gold_diagnostic_variant_preserves_four_unit_tf_and_original_fit_bytes` constrói uma fixture V3 com TF positiva nas quatro unidades (prenome/sobrenome de pessoa/mãe), compara score/log-odds antes/depois, reconstrói o payload exatamente, verifica o SHA pai e rejeita V2 e conversão duplicada. **Esse teste é de contrato, não é ensaio físico Gold.**
4. `V9GoldRunnerVariantPreparation` e `V9GoldHarnessCommand` continuam exigindo vencedor e artefatos pré-selecionados, SHA de origem correto, escopo TRAIN/holdout autorizado e SQL LocalDB descartável. Nenhum vencedor/threshold/passe é escolhido por esta mudança.

## Separação obrigatória dos modelos já existentes

- O candidato Gold histórico `6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f` (oito passes, TF0, superior 0,8671875) permanece **imutável**. Há evidência física das três ordens, incluindo falsos vínculos/criações e diferenças por ordem. **Não foi substituído.**
- Os snapshots TF0/ONE_MF da campanha 42–57 pertencem a **outro ensaio** (bloqueio de três passes e margem primária 0,5). Seus ganhos **não** provam que o Gold de oito passes passou a ter TF ativa nem autorizam misturar pesos de um modelo com os passes do outro.
- A migração `20261009_Ref_Calibracao_Inicial_Sintetica.sql` cria `ref.calibracao_inicial_versao` e publicação imutável, mas isso não prova a existência de **uma linha publicada no banco DEV/HML** nem estabelece vínculo de runtime operacional. Não há nesta PR consulta à instância SQL real.

## Próximos gates, sem recalibração automática

1. **Leitura apenas** do banco de referência autorizado: verificar se há registros `PUBLICADA` em `ref.calibracao_inicial_versao`, qual seu `codigo`, `algoritmo_versao`, `modelo_id`, `corpus_sha256`, `resultados_sha256`, e se o snapshot V3 completo correspondente existe e valida SHA. Não confundir `ref.frequencia_nome_versao` (fonte IBGE) com a calibração do algoritmo; não utilizar `ATIVA` do dia como substituição de pin.
2. **Identidade exata do modelo de teste:** confirmar `BlockingPasses` (quantidade/ordem/atributos), `UnitTfCalibrations` (lado, unidade, peso, estado), m/u das 81 células, prior, upper/lower, agrupamento e normalização. Preservar todo esse envelope. Se não houver vencedor completo com TF ampliada, **não fabricar modelo híbrido** a partir do Gold TF0 e dos snapshots ONE_MF.
3. **Ligação operacional separada:** `SqlProbabilisticIdentityLinkage.LoadModelByIdAsync` atualmente lê `identidade.modelo_linkage`, `identidade.parametro_linkage` e TF histórico; não carrega `V9FittedSnapshot` completo de `ref`. `LinkageModelPolicy.RequireCertifiedOperationalAlgorithm` bloqueia V9 deliberadamente. Exigir vínculo verificado, certificado e autorização DT-15 **antes de habilitar produção**.
4. **Ensaio incremental físico de integração:** somente quando o modelo V9 correto estiver fixado, exercitar o mesmo Runner com quatro ondas e três ordens, cada corrida em LocalDB isolada, com gabarito externo só para auditoria. Medir **recall global, FDR/FP, fusões, falsas criações, pendências, fragmentação, B³, idempotência, custo/P95 e pior ordem**. Comparar com metas da V9, **não** com V8. Os números anteriores das seis corridas Gold são históricos, não uma nova validação do TF.
5. **CI e governança:** executar testes unitários/integração do SHA exato e classificador de impacto. Se código de scorer exigir prova física 30k manual, manter gate pendente até autorização/disparo legítimo; jamais ativar 30k em PR automática. **Sem merge ou DT-15 sem autorização expressa do responsável.**

## Consulta SQL somente leitura proposta para conferência de referência

```sql
SELECT TOP (50)
  calibracao_versao_id, codigo, estado, algoritmo_versao,
  modelo_id, corpus_sha256, resultados_sha256, publicado_em
FROM ref.calibracao_inicial_versao
ORDER BY calibracao_versao_id DESC;
```

Esta consulta deve ser executada **somente** na base autorizada após verificar o nome do banco e o ambiente; não atualiza, publica ou ativa qualquer modelo. Para a identidade do snapshot exato, o consumidor ainda deverá validar conteúdo, origem e SHA, não apenas a coluna `estado`.

**Critério de conclusão desta PR:** permitir e testar a transformação experimental de V3 BLIND com TF ativa para GOLD, preservando integralmente o fit. **Não alegar que a V9 operacional está liberada ou que o TF ampliado passou no Gold físico.**


## Comando de auditoria REF implementado nesta PR

O worker expõe um comando **opt-in, somente leitura** para confrontar um modelo V9 portátil congelado (bytes e SHA externos) com a referência `ref.calibracao_inicial_versao` **de uma instância SQL autorizada**:

```bash
ConnectionStrings__Jornada='<conexao-SQL-com-permissoes-somente-leitura>' \
dotnet Jornada.Linkage.Parameters.Worker.dll \
  --v9-audit-ref-frozen-snapshot \
  /evidencias/<sha>.fitted.json <sha256-do-fit> <codigo-exato-em-ref>
```

O comando valida o SHA/canonicalização do fit antes de acessar SQL e consulta, sem `INSERT`, `UPDATE`, `EXEC` ou carga automática: estado `PUBLICADA`, SHA **SQL UTF-16** de `resultados_json`, identidade/algoritmo de `identidade.modelo_linkage` vinculado e igualdade de `snapshot_referencia` com o SHA do fit solicitado. Os hashes SQL do JSON de resultados e o SHA UTF-8 do fit são objetos **distintos** e não devem ser identificados como iguais por dedução.

A saída JSON não inclui nomes, observações, `resultados_json` nem parâmetros sensíveis e sempre marca `OperationallyCertified=false` / `ActivationAllowed=false`. Registros não publicados, sem vínculo, com hashes divergentes ou sem modelo V9 correspondente são **não conformes** (exit code 2, se o registro existe). A confirmação de metadados é apenas pré-requisito: não demonstra que todo o envelope V9 está instalado, nem permite retirar `RequireCertifiedOperationalAlgorithm`.

Nenhuma instância SQL do usuário foi acessada nesta PR: o comando e seus testes unitários estão disponíveis para a execução sob autorização e credenciais somente leitura. O próprio usuário/operador deverá fornecer a string de conexão apropriada, em ambiente seguro.


## Inventário físico do fit fixado (sem SQL)

Antes de qualquer corrida, o comando de inspeção existente agora lista os **passes em ordem e seus campos** e as **quatro unidades de TF** (lado, unidade, estado, peso, modo, frame da referência). Ele diferencia `TfWeight=0` legado de uma configuração com unidades TF independentes **ativas**:

```bash
dotnet Jornada.Linkage.Parameters.Worker.dll \
  --v9-inspect-fitted-snapshot /evidencias/<sha>.fitted.json <sha256-do-fit>
```

Conferir `ContentSha256`, `blockingPasses`, `activeUnitTfCount`, `unitTf`, `jointCellCount=81`, `FitFingerprintSha256`, `Threshold`, `ConflictMargin`, `grouping` e `goldParentSnapshotSha256`. O comando SQL `--v9-audit-ref-frozen-snapshot` inclui o mesmo inventário em `frozenConfiguration` e, separadamente, o veredito de correspondência **apenas de metadados**. As flags de certificação e ativação são sempre falsas.

É **erro de interpretação** declarar TF0 somente porque `TfWeight=0` quando `UnitTfCalibrations` possui unidades ativas. Também é erro contar o número de passes de um snapshot diferente do que será usado no Runner incremental.


## Descoberta de referências já presentes no banco (somente leitura)

O novo comando `--v9-audit-ref-inventory` consulta os **50 registros mais recentes** de `ref.calibracao_inicial_versao` e seus modelos associados, sem necessitar do código de calibração ou do caminho do fit de antemão:

```bash
ConnectionStrings__Jornada='<conexao-SQL-com-login-somente-SELECT>' \
dotnet Jornada.Linkage.Parameters.Worker.dll --v9-audit-ref-inventory
```

A saída `V9_REF_CALIBRATION_INVENTORY_READONLY_V1` apresenta código, origem, estado, algoritmo, `modelo_id`, `corpus_sha256`, hash SQL conferido do JSON de resultados, e eventual hash declarado no modelo. **Não retorna `resultados_json` e não escolhe o modelo.** Um registro com metadados correspondentes é classificado como `V9_METADATA_PRESENT_FIT_SHA_NOT_YET_VERIFIED`, jamais certificado. Executar depois a comparação do fit por bytes/SHA fixados com `--v9-audit-ref-frozen-snapshot` e, em seguida, o ensaio físico incremental com os parâmetros inalterados. Caso não haja linhas, o inventário sinaliza ausência (exit code 2).

O código SQL só faz `SELECT`; `ApplicationIntent=ReadOnly` não substitui as **credenciais SQL com permissão somente leitura**, nem autoriza conexão a produção. Este incremento não publica calibração, não materializa modelo híbrido, não ativa DT-15 e não altera a definição de 30k manual.


## Preflight do TF congelado para o Gold experimental (sem nova calibração)

Novo comando `--v9-gold-frozen-tf-preflight` recebe **bytes originais do fit** e o **SHA-256 externo**:

```bash
dotnet Jornada.Linkage.Parameters.Worker.dll \
  --v9-gold-frozen-tf-preflight /evidencias/<sha>.fitted.json <sha256-original>
```

Ele exige **V3 + JointSupport + 81 células + origem BLIND + pelo menos uma TF de unidade ativa**; calcula a variante Gold **só em memória**, faz roundtrip SHA-256 e verifica que o payload é idêntico ao fit de origem **exceto agrupamento Gold e SHA do pai**. Não grava novo fit, não altera pesos, passes, upper/lower, não conecta ao SQL e não aceita um artefato TF0 como evidência positiva de TF. Se o fit não satisfaz as precondições, retorna `status` explicativo (exit code 2). A saída `V9_GOLD_FROZEN_TF_DIAGNOSTIC_PREFLIGHT_V1` marca `certified=false`, `operationallyAuthorized=false` e `physicalGoldExecuted=false`.

O **fit ONE_MF de origem**, `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f`, está documentado em `Solution/data/reference/v9-tf-seed43-frozen-transfer/` como parte do artefato Actions de `41900357031`; o run `41925355648` registrou a conferência física dos seis hashes antes do replay. Reutilize o mecanismo `gh run download` **já existente** no workflow `v9-tf-seed43-frozen-transfer.yml`, sem criar outro fetcher ou recalibrar.

**Este preflight não substitui o recibo de vencedor condicional do TRAIN** exigido por `V9GoldRunnerVariantPreparation` e `V9GoldHarnessCommand`; não cria tal recibo artificialmente. O Gold de 8 passes TF0 histórico não é convertido em ONE_MF por essa operação. Somente após fixar o modelo realmente escolhido e sua proveniência será possível interpretar o ensaio incremental de quatro ondas × três ordens.


## Resultado físico autenticado — suporte M/U original do ONE_MF (05/11/2026)

O run de Actions [41962785639](https://github.com/lucianox777/Jornada/actions/runs/41962785639) concluiu em `SUCCESS`. Foram conferidos os SHA do executor, `plan-tf.json`, `study-tf/study.json` e os **seis fits originais**, baixados do run de origem `41900357031`. A auditoria ampliada verificou também, **na entrada `FrozenConfigurations` do mesmo study físico**, os suportes observados das 81 células de TRAIN, sem inferir contagens da distribuição suavizada.

Resultado para a configuração ONE_MF (3 passes, margem 0,5):

- snapshot V2: `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f`;
- 81 células originais, 1 TF ativa (prenome da mãe, peso 1,0), sem `JointSupport` V3;
- **19.896 pares M TRAIN** e **26.378 pares U TRAIN** registrados na auditoria física original;
- suavização original `alpha=0.5`; SHA de contagens M/U normalizadas: `27a5b008ea8ead99447feaf4a5c5c778d9e44a7a06ebc2ea4df78141582054fd`;
- há **dados de suporte reutilizáveis**. Isso não demonstra independência entre identidades, não é base para certificado de FDR nem confere automaticamente contrato V3.

O SHA do `TrainInputSha256` do estudo e o `TrainCorpusSha256` do snapshot pertencem a **domínios diferentes**: o segundo incorpora o fingerprint do frame de blocking SQL. Analogamente, o `PlanSha256` do relatório C# não é necessariamente o SHA dos bytes de `plan-tf.json`. Cada evidência é autenticada pelo seu SHA original, sem forçar igualdade entre domínios distintos.

## Novo ensaio opcional de derivação V3 em TRAIN — outro modelo, nunca conversão in-place

O código agora oferece um caminho **experimental**, separado e com SHA novo, para reaproveitar as contagens M/U TRAIN que já existem, sem reexecutar 30k nem buscar de novo TF, pesos, passes ou limiares. **Isso é uma nova estimação das probabilidades conjuntas M/U, portanto altera o escore e exige nova validação independente**; não deve ser descrito como modelo original V2 intacto ou simples alteração de metadado.

Comando:

```bash
dotnet Jornada.Linkage.Parameters.Worker.dll \
  --v9-derive-joint-v3-from-frozen-train \
  /source/study-tf/<sha-v2>.fitted.json <sha-v2> \
  /source/study-tf/study.json <sha-study> \
  /policy/historical-v3-support-policy.json <sha-policy> \
  /saida-nova
```

- Verifica bytes originais e seus SHA externos, o protocolo físico do `study.json`, a identidade única do fit, a igualdade de suas **81 contagens observadas reconstruídas** e as do recibo físico, mais as contagens totais e o prior. Descarta evidência que não reconcilia.
- A política de suporte é entrada explícita com SHA próprio. Como **hipótese de diagnóstico**, o workflow `v9-frozen-train-joint-v3-trial.yml` usa a mesma política do Gold V3 histórico (`ClassMassCoverage=.99`, `MinimumM=5`, `MinimumU=5`, regra de cauda com suporte), mas **não a declara automaticamente ótima** no frame TF-001.
- A saída é **novo fit V3 experimental** com `JointCells`, `JointSupport` e fingerprint de FIT novos; preserva TF, referência de frequências, **os mesmos 3 passes**, limiar 0,95, margem 0,5 e prior. Rejeita falta de suporte mínimo do núcleo M/U. Não altera nem substitui o fit V2 original e não importa parâmetros do Gold TF0/8-passes.
- A Action derivadora roda **sem SQL, sem novo corpus e sem TEST**. Guarda o novo fit **somente em artefato Actions isolado e explicitamente não autorizado**, com recibo sanitizado separado; não o adiciona a `ref`, não habilita o loader operacional, não autoriza DT-15 nem faz merge.
- Mesmo que a Action conclua, ainda faltam escolha e validação independentes da configuração V3 em VALIDATION/holdout novo, auditabilidade por identidade-base e Gold incremental (4 ondas × 3 ordens em bancos independentes). Os ganhos das dez seeds do fit V2 **não são transferidos automaticamente ao novo V3**.

**Nenhuma execução automática de 30k é autorizada por esta frente.** O gate físico do PR #1277 continua manual e preso ao SHA exato. O incremento não define nem inicia certificação.


## Resultado da derivação real (05/11/2026) — êxito técnico, não certificação

A Action [41964404787](https://github.com/lucianox777/Jornada/actions/runs/41964404787) concluiu em `SUCCESS`, inclusive compilação do Worker, teste unitário focal `Frozen_V2_TRAIN_support_can_form_distinct_unapproved_V3_trial_without_TF_reoptimization`, download com SHA autenticado do estudo original, derivação TRAIN-only e **preflight positivo de Gold com TF** sobre o novo V3.

- Fonte imutável V2 (ONE_MF): `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f`.
- **Novo candidato experimental V3**, SHA `8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc`, com `FitFingerprintSha256` próprio `809ce7ef03fee2dcfd28369711bf088ad35e1ee55db5bc8eb8e1d89409de5d7c`.
- Política do ensaio imutável: SHA `3833d26a21691f2804fa9c52201a9c5e6f3603dc151d871453218debfc3dd319`, emprestada **somente como hipótese experimental** do Gold histórico V3 (cobertura 0,99, mínimos M/U 5, cauda com suporte).
- **19.896 M + 26.378 U TRAIN** recuperados da mesma fonte, 81 estados, `alpha=0,5`; mantém 3 passes e TF prenome da mãe peso 1, limiar 0,95 e margem 0,5. O **joint m/u foi reestimado**, então o resultado estatístico do modelo V2 **não se transfere automaticamente** ao modelo V3.
- O preflight verificou que a variante GOLD do **novo candidato V3** preserva integralmente os parâmetros do próprio V3 exceto agrupamento e SHA-pai, com 81 células, 3 passes e uma TF ativa. **Nenhuma corrida Gold incremental física ocorreu.**

Os artefatos estão no run acima:
- `V9-V3-TRAIN-TRIAL-NOT-AUTHORIZED-65cda6c063924ab75c5256a16142c5697fce3956` (artifact ID **13387205838**): apenas o fit derivado SHA-nomeado, **fora do banco operacional**, retenção de 90 dias.
- `v9-frozen-joint-v3-TRIAL-METADATA-65cda6c063924ab75c5256a16142c5697fce3956` (artifact ID **13386463554**): recibo e resultado do preflight, também com `certified=false`, `operationallyAuthorized=false`, `physicalGoldExecuted=false`.

**Próximo gate científico:** validar o **novo V3** sobre dados independentes e executar incremental Gold nas três ordens com SQL isolado, medindo FP/FDR, falsas criações, fragmentação, B³ e desempenho. O histórico 10/10 de TF é sobre V2 e não homologa V3. O inventário real da instância SQL `ref` ainda não foi executado — não confundir esta derivação experimental com a versão que a autoridade eventualmente autorizará.


## Protocolo preparado para validação independente V2 versus novo V3 (30k SOMENTE MANUAL)

A nova CLI `--v9-compare-frozen-v2-v3-independent` conecta-se **somente** a `127.0.0.1:1433/JornadaHarness` descartável. Reutiliza o avaliador SQL físico congelado `EvaluateFrozenHoldoutAsync` (nenhum segundo scorer) para comparar, em VALIDATION, o **ONE_MF V2 original** com o **novo candidato experimental V3**. Os dois fits são conferidos por SHA externo; uma verificação estrutural impede alterações de TF, blocking, prior, upper, margem e referência fora do novo `JointSupport`/probabilidades M/U/fingerprint. O recibo de derivação também precisa coincidir com SHA do `study`, política e família original.

**Seed independente declarada antes do ensaio: `20261105001`**, gerador clean, população 30.000, mesmo contrato sintético e referência IBGE; nada de TEST, nem reaproveitamento de seed 42/43 ou das confirmatórias 48–57. O workflow `v9-frozen-joint-v3-independent-physical.yml` contém **apenas `workflow_dispatch`** com verificação do SHA exato: nenhuma execução física 30k acontece por `push`, PR ou workflow automático. O executor gera o corpus no momento da autorização manual e só depois executa o SQL isolado. O resultado é um recibo agregado V2 × V3 com TP/FP, recall global, FDR descritiva, P95 e SHA do corpus, sem ids/nomes nem promoção de modelo.

**O protocolo é somente validação BLIND do novo V3.** Mesmo que o workflow manual termine favorável, ainda faltam a autorização de uma seleção válida do modelo V3 e o ensaio incremental **quatro ondas × três ordens** no Runner Gold, com B³, fragmentação, fusões, falsas criações, idempotência e custo. Não transformar o relatório de comparação em `finalist-validation.json` artificial. A PR #1277 permanece sem merge e não ativa a DT-15.


## Auditoria prévia V2/V3 real sem SQL (05/11/2026)

Antes da validação cara, foi implementado `--v9-preflight-frozen-v2-v3-pair`: aplica **a mesma função `VerifyPinnedPairAsync` usada pelo comparador físico** para autenticar o executor 41900357031, plano TRAIN, SHA dos dois fits, política, recibo de derivação, referência IBGE, identidade de snapshot, 81 células, um TF ativo, passes, limiar, margem e igualdade exata dos campos inalterados. É executado na Action `v9-frozen-train-joint-v3-trial` quando há alterações do comparador, usando os bytes reais já presentes nos arquivos do GitHub Actions. Em sucesso, retorna `independentSqlExecuted=false` e `goldIncrementalExecuted=false` e não gera novo corpus. A futura Action **manual** de 30k chama o mesmo preflight antes de produzir dados. Nenhum modelo é selecionado, publicado ou ativado por esse comando.


## Política de disparo dos workflows congelados (revisão 05/11/2026)

Após o 30k manual aprovado no SHA `4f34e66296821227a441db36bac8b42d5576763e`, foram revisados **somente** os gatilhos de dois workflows e adicionados dois testes de contrato (nenhum modelo, fit, SQL, calibrador, scorer ou TEST foi modificado):

- `.github/workflows/v9-tf-frozen-artifact-schema-audit.yml`: auditoria **sem refit/SQL** em eventos `pull_request` com filtro explícito dos arquivos relevantes; `workflow_dispatch` opcional exige `expected_sha` de 40 hex, o mesmo commit de checkout e a referência Git ao HEAD vivo da branch. Não há mais `push` condicionado ao nome da branch.
- `.github/workflows/v9-frozen-train-joint-v3-trial.yml`: derivação de candidato **TRAIN-only** apenas por `workflow_dispatch` explícito, igualmente travado no SHA exato e no HEAD vivo; nunca por `push` nem por abertura/atualização de PR. O resultado continua experimental, não é calibração automática operacional nem certificação.

Os dois workflows usam permissões `contents: read` e `actions: read`; nenhuma ação de publicação, DT-15 ou auto-merge foi habilitada. Os testes de contrato ficam em `Solution/tests/scripts/test_v9_tf_frozen_source_schema_audit.py`. A execução `workflow_dispatch` de workflows novos geralmente só fica disponível depois de eles existirem na branch padrão, sem dispensar aprovação do responsável para o merge; a auditoria via `pull_request` pode validar a mudança antes do merge.

**Controle de SHA obrigatório:** os commits desta revisão alteram o HEAD da #1277. O run manual `41994843778` permanece evidência física válida **somente** de `4f34e66296821227a441db36bac8b42d5576763e`, não do novo HEAD. O classificador de impacto deve avaliar a mudança integral: se exigir 30k, disparar novo `preflight` e `execute_30k` manuais no novo SHA, com pin IBGE autenticado, e aguardar probe/CI verdes. Não acrescentar exceções por workflow ou branch para escapar do gate. A validação física Gold 4 ondas × 3 ordens, a leitura do REF real, a certificação e a autorização DT-15 continuam pendentes.
