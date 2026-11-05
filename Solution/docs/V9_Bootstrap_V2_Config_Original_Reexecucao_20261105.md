# V9 — bootstrap/reexecução da calibração V2 por plano de usuário congelado — 05/11/2026

## Objetivo e decisão

O Calibrador C# deve receber **os parâmetros de exploração e tolerâncias do arquivo versionado** que efetivamente produziu a V2, sem reconstruir essa configuração por constantes de scripts. Não mudar o scorer, nem os parâmetros descobertos na V2. Rodar de novo somente com autorização manual e em banco SQL descartável, sobre o mesmo corpus e referências, para verificar reprodutibilidade e regressão.

## Fonte de verdade

- Plano de usuário efetivo V2: Solution/data/calibration/v9/v2-one-mf-frozen/plan.json — cópia dos **bytes originais** de plan-tf.json da execução física #41900357031.
- SHA-256 do plano original: b6f5f5197074f7ecedf004745f763df3b66a0496f9355589242062f9f1d33ee8.
- Snapshot da V2 ONE_MF (margem 0,5): 8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f.
- Commit original: 935f704edd2bceb6a125ecfe532ad5b71f9c89b9.
- Arquivo de referência de runs e artefatos: Solution/data/reference/v9-frozen-artifacts/references.json.

O plano físico V2 tem 45 opções TF, três margens 0,5/2/4, 41 opções selecionáveis e quatro diagnósticos de sobrenome, todas registradas **no arquivo**, sem sintetizar novamente a grade a partir dos defaults Python. Contém ainda SearchOptions, SelectionPolicy, Budget, Execution, ObservationCapDeclaration, fingerprints de fonte, corpus e IBGE, mais TF por unidade/lado/peso/modo. Os valores históricos do limite de recall e FDR permanecem no plano V2; não são reimpostos como novos parâmetros universais.

## Nova execução de calibração (somente manual)

No GitHub Actions, executar o workflow **v9-physical-seed42-probe** selecionando a branch desta PR e:

1. mode = execute_30k.
2. expected_sha = SHA **exato** da branch no momento do disparo.
3. replay_frozen_v2 = true.
4. pinned_ibge_json = vazio: o workflow deriva os quatro campos do próprio plano V2 e rejeita override.

Em modo replay_frozen_v2 o workflow: confere SHA da branch; prepara banco JornadaHarness descartável; recria IBGE e a massa clean seed 42; **aborta antes de calibrar** se qualquer hash de observações, manifesto de geração ou pin IBGE divergir do V2; copia os **bytes originais versionados** para o arquivo de entrada do C#; executa o único Calibrador por --v9-adaptive-study; recupera estudo físico original por run+SHA; compara inputs, opções, métricas, snapshots e SHA V2; registra dossiê de regressão e preserva artefatos. O comparativo retorna REPRODUCED_EXACTLY ou REGRESSION_DETECTED_REVIEW_REQUIRED. A divergência resulta em gate vermelho, jamais em sobrescrita/correção silenciosa.

Esse fluxo é **manual mesmo com a PR aberta**: uma alteração ou novo commit não dispara automaticamente os 30 mil. O workflow existente mantém a execução normal do plano gerado para outros ensaios quando replay_frozen_v2=false.

## Integridade/independência

1. O SHA do arquivo plano (bytes) e Study.PlanSha256 (C# normalizado) pertencem a domínios diferentes; armazenar ambos.
2. O Calibrador já faz deserialize tipado de V9AdaptiveCalibrationPlan diretamente do JSON e rejeita campos desconhecidos. Nenhuma segunda implementação estatística é criada.
3. O programa v9-v2-frozen-bootstrap.py só autentica, copia e compara. Não abre SQL, não lê TEST, não treina ou publica.
4. O resultado novo é um **novo diretório**, preservando intactos o run, o estudo e o snapshot originais.
5. Não converter uma nova concordância física em certificação de FDR, suficiência populacional, DT-15, ativação ou merge automático.

## Auditoria de parâmetros ainda necessária

A reexecução V2 demonstrará se o plano original continua reproduzível quando lido diretamente do Git. Isso **não prova automaticamente** a externalização de *todas* as constantes e defaults do C# usados na versão original. A matriz de atributos do bundle da PR #1283 identifica campos documentados, presentes e dependentes de valores padrão; para fechamento definitivo ainda é preciso comparar a tipagem C# com a versão efetiva do arquivo e os defaults da versão de código do run original.

**Estado desta PR:** plano original versionado e SHA-verificado; código de reprodução e regressão implementado; testes CI leves; execução física 30 mil somente por disparo manual explicitamente autorizado, no SHA exato. Não fazer merge sem autorização.
