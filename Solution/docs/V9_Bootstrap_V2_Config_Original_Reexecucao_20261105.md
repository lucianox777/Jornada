# V9 — configuração única do Calibrador para a V2 congelada (05/11/2026)

## Decisão confirmada

A configuração do Calibrador contém **um único arquivo versionado:**
`Solution/data/calibration/v9/v2-one-mf-frozen/plan.json`.

Este `plan.json` é o antigo plano *efetivo*: conserva os parâmetros da campanha física V2 e **torna explícitos** os valores que o C# havia preenchido por default. O `plan-effective.json` redundante foi eliminado. Não há segundo plano do usuário no repositório.

A equivalência é **semântica, não de bytes**: o plano original da campanha (arquivo `plan-tf.json`, SHA-256 `b6f5f5197074f7ecedf004745f763df3b66a0496f9355589242062f9f1d33ee8`) foi preservado como artefato histórico externo no GitHub Actions run **41900357031**, commit `935f704edd2bceb6a125ecfe532ad5b71f9c89b9`. Aquele SHA histórico **não** é o hash do novo `plan.json`, porque agora ele inclui campos que antes eram implícitos.

O ensaio físico V2 possui 45 opções TF e as margens 0,5, 2 e 4 (41 opções selecionáveis; quatro apenas diagnósticas). Foi adicionada a declaração de **233 ocorrências de valores padrão históricos**: 6 propriedades principais, 2 de descoberta, 1 do blocking, 1 de execução e 223 ocorrências entre opções. Não foram alterados os valores de parâmetros efetivamente utilizados na V2.

## Prova de equivalência sem segundo plano versionado

O workflow `v9-tf-frozen-artifact-schema-audit.yml` recupera o plano original por run e SHA. Em diretório temporário, o C# deserializa:

1. O original histórico baixado `$RUNNER_TEMP/v9-tf-frozen-source/plan-tf.json`.
2. **O único plano versionado** `Solution/data/calibration/v9/v2-one-mf-frozen/plan.json`.

Ambos passam pela mesma classe `V9AdaptiveCalibrationPlan`, pelo mesmo `ValidatePlan` e pelo mesmo serializador do Calibrador. A comparação exige objetos efetivos semanticamente idênticos e rejeita diferenças. A execução anterior de comparação original × efetivo retornou `V2_EFFECTIVE_PLAN_IDENTICAL`; a nova comparação deve permanecer verde no SHA atual, agora usando o original baixado.

Para impedir adulteração do original, a ferramenta valida o SHA original contra o registro externo `Solution/data/reference/v9-frozen-artifacts/references.json`.

## Nova calibração V2 somente por disparo manual

No workflow `v9-physical-seed42-probe.yml` selecionar:

- `mode=execute_30k`;
- `replay_frozen_v2=true`;
- `expected_sha` = commit exato da branch aprovada;
- `pinned_ibge_json` = vazio, porque a referência deriva de `plan.json`.

Antes de executar o C# físico, o workflow recupera a campanha histórica para validar o plano original, o estudo e seus hashes; verifica que o único `plan.json` tem a mesma semântica efetiva do original; recria o corpus com seed 42 e valida os hashes de observações, manifesto e IBGE. O bootstrap `v9-v2-frozen-bootstrap.py` então copia **somente o `plan.json` versionado** para o diretório temporário de execução e invoca o Calibrador C# já existente por `--v9-adaptive-study`. Depois compara métricas, fits e opções com a execução histórica.

O original baixado é **evidência**, não um segundo plano de configuração. Não há sobrescrita dos snapshots históricos, nem TEST, DT-15, publicação operacional ou execução de 30 mil automática por PR/push.

## Separação do bundle de calibração

O bundle de evidências continua com três componentes **funcionais** (plano, resultado, relatório) e manifesto SHA, mais o snapshot do fit como anexo ao resultado. O `plan.json` é o único plano configurável. O arquivo histórico `plan-tf.json` do run original continua imutável no acervo, com seu SHA próprio, e não integra a estrutura versionada como segundo plano ativo.

## Limites

A igualdade tipada do C# prova que os defaults declarados reproduzem a configuração **efetiva** do original segundo o contrato atual. Não substitui a nova execução física nem, por si só, atesta integralmente qualquer constante interna de algoritmo não modelada no contrato C#. O dossiê de regressão deverá registrar os resultados físicos antes de declarar encerrada a reprodução. **Sem merge antes da autorização do responsável.**
