# Gold V9/V6 — resultado físico, candidato congelado para validação e lacunas até operação

**Registro técnico de 25/10/2026.** Não constitui ativação operacional, decisão final de política, certificação, execução de holdout novo ou release. É uma fotografia verificável do TRAIN sintético e do candidato de próxima avaliação. `TEST` permanece lacrado. Fonte original: [issue #1180, resultado físico auditado](https://github.com/lucianox777/Jornada/issues/1180#issuecomment-6394721695).

## 1. Evidência física integral, imutável

- GitHub Actions [run #40156139352](https://github.com/lucianox777/Jornada/actions/runs/40156139352), **completed/success**, 25/10/2026, 12 corridas físicas (3 ordens × CURRENT/KNEE 0,05/FINALIST_2 0,5/FINALIST_3 0,7) com SQL Server LocalDB Windows exclusivamente descartável, ~4 h 03 min.
- Execução na branch experimental `experiment/v9-gold-v6-train-20261024`, HEAD `080cb6437ae8130562ddb0573802f6c58bfdc430`, proveniente de `master 5c86a374faff9932dff8b4e4207ab616a33b2b0c` (única diferença no gatilho YAML, **não** no FS, corpus ou procedure).
- [Artifact agregado #12590550181](https://github.com/lucianox777/Jornada/actions/runs/40156139352/artifacts/12590550181): SHA-256 ZIP `79b2b1a19dc60344a33069ca5a4b4d4d539a06b92058bff61087dfa89699056c`; `v9-gold-study-report/lower-train-pareto.json` SHA-256 `e0f6350687620026dc389667926c81b332139587a3aa6124f07966d5ec2f8f51`; `v9-gold-matrix-evidence/gold-12-trial-matrix.json` SHA-256 `147ce1a0efe111d93465b9c375308284cbe8e9678b98df911a8546ff7f1fbb77`. Os 12 `gold-runner-study.json` individuais constam do mesmo ZIP.
- Estudo `V9_TRAIN_SELECTION_LOWER_NONMATCH_PARETO_V6`, matriz `GOLD_TRAIN_LOWER_NONMATCH_MATRIX_V2`. CSV original clean seed42 SHA-256 `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`. Fit Gold congelado SHA-256 `6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f`. `TrainingEvidenceSha256=ae74ab19fc942382a348c05dc72a3356f7fce7c2f14aaf6e6cf507559a98af37`; `PolicyFingerprintSha256=802e62ec8e12a9480a3d53fcf5dc33dddcbe3d6f5a4666d35ebfd41c4ba088d5`; `DeclaredGridSha256=a7ae55f0b219702f4bf0c1d7f308e1db92f1c0d9dd09e244b7d9c706dcddf5ab`.
- Universo da seleção: 9.240 observações de 5.003 pessoas-base, 3.369 fontes com identidade já representada. `FinalistCount=3`, `ExecutedTrialCount=12`, `TrainOnly=true`, `IndependentlyCertified=false`, `OperationallyActivated=false`, `TestUsed=false`. Os três valores 0,05/0,5/0,7 saíram do seletor V6 **antes** da medição incremental, não foram impostos como oráculo de TEST.

### Valores físicos por ordem

| Política | Pendências baseline/reversa/intercalada | Criações falsas (mesma ordem) | Pares Gold falsos (mesma ordem) | F1 B-cubed *coverage-aware* (mesma ordem) |
| --- | --- | --- | --- | --- |
| CURRENT | 2.788 / 2.856 / 2.761 | 1 / 2 / 1 | 3 / 0 / 6 | 0,697587 / 0,690381 / 0,700641 |
| **KNEE 0,05** | **26 / 28 / 24** | **4 / 5 / 5** | **3 / 0 / 6** | **0,996338 / 0,996230 / 0,996569** |
| FINALIST_2 0,5 | 8 / 7 / 7 | 14 / 14 / 12 | 3 / 0 / 6 | 0,997866 / 0,998069 / 0,998084 |
| FINALIST_3 0,7 | 5 / 6 / 5 | 17 / 17 / 16 | 3 / 0 / 6 | 0,998037 / 0,997991 / 0,998042 |

**O que isso mede:** quase toda a melhoria de F1 do joelho é redução das pendências **neste corpus**; não confundir F1 B-cubed de partição com certificação da FDR por vínculo aceito. A FDR pontual dos vínculos aceitos no joelho foi ~0,0007114 / 0 / ~0,0007109 nas três ordens, **sem certificação estatística independente**. A ordem ainda altera falsos pares e impureza. As três ordens não criam três populações independentes: partilham o mesmo conjunto de pessoas. Criações físicas excedem a prévia estática de TRAIN; não tomar a prévia como execução.

### Candidato preferencial, sem decisão operacional

**Registrar 0,05 somente como candidato prioritário congelado da próxima validação externa**, em função do joelho normalizado já encontrado pela fronteira de Pareto e do trade-off observado: pendências máximas 28 vs 2.856 no controle, com falsas criações máximas 5 vs 2. Não impor 0,05 no algoritmo, no bootstrap, nos snapshots `ref` ATIVOS ou na migration canônica. `0,5` e `0,7` permanecem pontos alternativos históricos de TRAIN, não novas opções a escolher com o holdout.

O método puro `GoldTrainThreeOrderProtocol.FreezeKneeForIndependentValidation` é guardião de **elegibilidade do pacote de evidência**, não seleção de parâmetros ou execução de validação. Recebe o limiar candidato como dado, confere política KNEE real, todas as ordens, mesmo universo, SHA e estado não certificado; retorna `IndependentlyValidated=false`, `TestUsed=false`, `Certified=false`, `OperationallyActivated=false`. Não lê SQL nem escreve `ref`.

## 2. Próxima rodada — protocolo externo com valores congelados

**Pré-registro de intenção (a execução NÃO foi realizada neste registro):**

1. **Fonte/modelo fixos:** variante Gold fitted de SHA `6d93690d...`, normalização, catálogos, passes, m/u, TF, prior, limiar superior 0,8671875 e demais parâmetros **idênticos** ao ensaio V6, sem refit. Comparar **somente** CURRENT contra política KNEE `LOWER_NONMATCH=0.05`, com três ordens fixas `G0-G1-G2-G3`, reversa e intercalada; sem consultar resultados intermediários para escolha.
2. **Nova massa sintética:** geração `demographic-primary`, perfil `clean`, 30.000 pessoas, semente **proposta** `20261025001`, ainda a confirmar contra o catálogo de seeds já consumidas. Registrar *antes* de calcular métricas os novos fingerprints de input/CSV, manifesto e partição de avaliação; não reutilizar a seed42 nem 43/44/20261019001. **Não acessar as linhas TEST ou VALIDATION previamente seladas**, não fazer seleção dos parâmetros no holdout.
3. **Identidade e sobreposição:** o gerador atribui identificadores locais sequenciais `P0000000...` em **todas** as seeds; igualdade desses identificadores entre seeds **não indica reutilização da mesma pessoa sintética**. Estabelecer namespace por `seed + fingerprint de geração + chave local` em qualquer confronto e atestar a independência dos sorteios e do desenho amostral; simples diferença de SHA não prova ausência de características correlacionadas.
4. **Bloqueio técnico real:** o Runner Gold existente `V9GoldHarnessCommand.RunInternalAsync` lê as `TRAIN_SELECTION` do envelope histórico e **recusa CSV cujo SHA não coincida com `originalObservationsSha256`**. Portanto, não é permitido substituí-lo por CSV de seed inédita ou falsificar um envelope `TRAIN` para fazê-lo passar. Antes do experimento externo implementar **contrato separado de holdout de validação**, com dataset/escopo independentes, decisão original Rank→Resolve, fit pinado, proof of provenance, mesmas travas LocalDB, três ordens, publicação agregada e `validationReopened=false`. O resultado deve declarar `INDEPENDENT_HOLDOUT`, nunca `TRAIN_SELECTION` nem `TEST`.
5. **Aceite pré-declarado de integridade:** duas políticas × três ordens = **seis corridas físicas distintas** em LocalDB novos, com fit idêntico, leitura **somente agregada** e confirmação dos hashes. Medir pendências, falsas criações como eventos físicos, aceites falsos e FDR com denominador correto, pares falsos/impureza/fragmentação, B-cubed coverage-aware e latência. Relatar pior ordem e incerteza por **identidade independente**, não por pares como IID. Se protocolo/gates falharem, registrar falha; **não** tentar outras seeds ou retunar 0,05 para melhorar placar.
6. **Separar perito e implantador:** catalogar causas agregadas dos 0/3/6 pares falsos e 4/5/5 criações falsas no TRAIN, depois fazer perícia com acesso controlado aos casos individuais no ambiente autorizado, **sem anexar UUIDs/labels de origem aos artefatos públicos**. Não deduzir que todas as ocorrências são o mesmo erro nem que a falsa criação é sempre corrigível por CPF tardio.
7. **Decisão posterior:** só após evidência externa satisfatória e laudo de erros propor migration canônica DT-10 **distinta** do patch experimental SQL17 (que existe apenas em LocalDB descartável); construir contrato de regressão, rollback, proteção do `SEM_CANDIDATO`, governança DT-15 e aprovação humana. TEST lacrado final e release institucional são etapas independentes, não automáticas.

**Dois bloqueios que impedem declarar V9 operacional:** issue [#1089](https://github.com/lucianox777/Jornada/issues/1089) (Calibrador C# único completo, congelamento e engenharia reversa ainda aberta) e a falta de comprovação externa Gold sobre nova massa com *holdout* e perícia. Não criar valor fixo definitivo para o calibrador.

## 3. Procedimento para auditar a prova original, sem novo experimento

1. Baixar o ZIP do artifact #12590550181 e verificar SHA256 `79b2...9056c`.
2. Extrair exatamente `v9-gold-study-report/lower-train-pareto.json` e `v9-gold-matrix-evidence/gold-12-trial-matrix.json`; verificar seus SHA256 completos citados acima.
3. Conferir a versão V6, `DescriptiveSuggestedLowerThreshold=0.05`, finalistas `[0.05,0.5,0.7]`, SHA de corpus/fit, flags `TrainOnly=true` e `TestUsed=false`, e 12 triplas política/ordem com `StablePartitionSha256` válidos. Divergência deve reprovar, não reescrever a evidência congelada.
4. Os valores medidos e seus limites são os do relatório original e da [issue #1180](https://github.com/lucianox777/Jornada/issues/1180), não estimativas de qualidade municipal.

**Estado editorial:** este arquivo acrescenta decisão de **prioridade de verificação externa**, não uma aprovação operacional de 0,05 nem alegação de que a validação já ocorreu.
