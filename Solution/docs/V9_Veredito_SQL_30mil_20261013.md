# V9 — veredito do censo SQL físico: 3 × 30 mil (13/10/2026)

## Proveniência verificável

- PR diagnóstica original: #998, HEAD executada `7f310869cedfb336cbd6bcbac9e6905962e6361c`.
- CI: `jornada-ci` #10465, run `38451933667`, job `harness-smoke`.
- Artefato: [v9-physical-sql-active-candidate-wrong-link-30k](https://github.com/lucianox777/Jornada/actions/runs/38451933667/artifacts/11840892682). Contém os relatórios de sementes 42, 43 e 44 e o agregado TEST.
- Três conjuntos independentes de **30.000 indivíduos-base sintéticos cada**, cada conjunto dividido em TRAIN/VALIDATION/TEST. Modelo FS C# V9, evidência conjunta pessoa × mãe + nascimento, parâmetros e threshold **congelados a partir do proxy da seed 42**, reusados sem refit nas demais seeds.
- O SQL Server **físico** da base temporária `JornadaHarness` executou a união de regras do ruleset ativo do **CI** (dois passes no ensaio) e cinco passes C1, indexados, com `DISTINCT`. Recuperou pares do **frame sem CPF, inter-Gestor, por split, com nascimento presente**. É SQL verdadeiro, mas sobre chaves atuais; **não inclui histórico de aliases `VERSIONED_ALIAS`**. Nenhum índice de `JornadaLocal` nem modelo ativo de produção foi alterado.

## Resultado TEST histórico — agregação correta de numeradores e denominadores

**Estado metodológico após 15/10/2026:** este TEST foi aberto e seus TP/FP/FDR foram publicados; portanto está **consumido como holdout histórico**. Preservar os números e artefatos imutáveis para regressão/auditoria, mas não reutilizar essas mesmas identidades/partições como TEST final para escolher ou certificar a V9 recalibrada.

| Métrica | Seed 42 | Seed 43 | Seed 44 | Total |
| --- | ---: | ---: | ---: | ---: |
| Verdadeiros M elegíveis antes do blocking | 5.931 | 6.056 | 6.049 | **18.036** |
| Verdadeiros M recuperados por SQL | 2.585 | 2.593 | 2.608 | **7.786** |
| Falsos candidatos U retornados por SQL | 4.181 | 3.806 | 4.328 | **12.315** |
| M perdidos antes do scorer | 3.346 | 3.463 | 3.441 | **10.250** |
| Corretos aceitos (TP) | 2.584 | 2.592 | 2.608 | **7.784** |
| Errados aceitos (FP) | 270 | 263 | 254 | **787** |

No total, **1,5817 candidatos falsos por verdadeiro recuperado** (`U_sql / M_sql = 12.315 / 7.786`). A fração de pares falsos entre todos os candidatos recuperados é **61,27%** (`U_sql / (U_sql + M_sql)`). Ambas são métricas **da exposição do blocking**, não taxas de vínculos erroneamente aceitos.

O mesmo modelo congelado aceitou **8.571 pares**, dos quais **787 errados**. Portanto a **participação observada de vínculos errados dentre os aceitos** é `FP/(TP+FP) = 787/8.571 = 9,1821%` (**9,4604%**, **9,2119%** e **8,8749%** nas seeds 42, 43 e 44). O FPR condicional **a U já recuperado** é `787/12.315 = 6,3906%`, e a razão de erros por acerto é `787/7.784 = 0,1011`. Estes denominadores são diferentes e não devem ser confundidos.

O **recall de ponta a ponta neste frame** é `TP/M_elegível = 7.784/18.036 = 43,1581%`. A perda de blocking (`10.250` pares M) explica quase todas as falsas negativas (`18.036 - 7.784 = 10.252`), portanto alterar só threshold ou TF não recupera automaticamente os pares que SQL não apresentou ao scorer.

## Veredito de qualidade

**REPROVADO como evidência para qualquer ativação decisória V9.** Os 787 vínculos errados não são uma variação numérica trivial do teste proxy; a amostragem de U **realmente recuperados** pelo SQL difere da amostragem de pares adjacentes por inicial. As taxas de erro são medidas nos pares deste corpus sintético; não podem ser extrapoladas como FDR municipal/por cidadão, nem como risco após deduplicação em clusters e prevenção de conflitos de CPF. Não mascarar os dados com `PASS` de CI: os checks atuais validam contrato, execução integral, aritmética e proveniência, **não certificam baixo risco estatístico**. A integração de código apenas diagnóstico continua possível após os gates técnicos sem permitir `Rank/Resolve` V9 nem DT-15.

Duas limitações impedem interpretar isto como experimento da **V9 corretamente recalibrada**: (1) `m/u`, prior e threshold vêm de um modelo ajustado em U **proxy por inicial**, não no universo U do próprio SQL, e (2) a contribuição **TF foi explicitamente zero**. Nenhuma dessas lacunas autoriza ajustar pesos ad hoc ou reduzir a exigência de segurança.

## Próximo gate: issue #999 — calibração sem vazamento

A ordem aprovada é usar **o mesmo conjunto SQL de candidatos** para condicionar `m/u` conjuntos, nascimento e prior a TRAIN; materializar frequências `f(value)` em **uma pessoa-base por indivíduo no universo TRAIN** (não em pontas de pares candidatos) para `FullName`, `FirstLastJoint`, `FirstName` e `LastContentSurname`, pessoa e mãe separadamente; estimar um **baseline U da mesma unidade e do mesmo frame**; depois selecionar peso TF e threshold **somente em VALIDATION**, usando suporte/abstenção explícitos. Congelar snapshot e avaliar em **novo TEST não observado**, congelado antes da execução e distinto do TEST histórico das três seeds aqui publicado. A prova matemática independente deve conferir contagens e score C# nos mesmos pares, sem publicar dados pessoais.

A PR #1000 mede apenas **histogramas agregados TRAIN de unidades TF por pessoa-base**, com SHA-256 do corpus e segregação dos splits. É um pré-requisito da calibração, **não** calibração do peso ou valor U. Células raras, valores desconhecidos e ausência de campo não podem virar automaticamente TF neutro: `w=0` é válido somente como escolha modelada/documentada, distinta de Bayes factor neutro `1` e de parâmetro ausente. Caso os 30 mil não forneçam suporte por unidade/estrato/grupo, declarar inconclusão e redimensionar a massa por evidência, sem escolher `200 mil` ou `1,5 milhão` arbitrariamente.

**Proteções preservadas:** `JornadaLocal`, referência IBGE original, NODE/NAS, HML/PROD, histórico V8, Gold e decisões DT-15 não são tocados por esses ensaios. O núcleo `cpf/nome/nome_mae/nascimento` continua progressivo e opcional; campos exigidos dependem do contrato versionado da origem. O frame restrito com nascimento presente **não implica** nascimento obrigatório em todas as novas identidades.
