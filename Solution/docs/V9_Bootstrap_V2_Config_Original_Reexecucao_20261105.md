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

## Evidência física histórica sem workflow excepcional

A reprodução física já executada da V2, **run 42087910027 no SHA 51fcc50ef4d8eae92860a2e1574ded515d6cef54**, apresentou paridade do estudo com a referência V2 original (`study.json` SHA `7499ee63…`), nas 135 medições e no snapshot `8bdf08af…`. Seu estado de elegibilidade continua `NO_ELIGIBLE_CONFIGURATION`. Esses resultados são **históricos**, não mudam de commit nem de hash quando este código for integrado.

O workflow ad hoc `v9-v2-calibration-chat-approved-once.yml` e seu JSON declaratório de autorização **foram excluídos da integração**. Atestação produzida pelo próprio agente não equivale a autorização independente. Também não foi incorporada a variante `workflow_call` nem o parâmetro `replay_frozen_v2` ao workflow oficial `v9-physical-seed42-probe.yml`.

A política vigente autoriza execução automática do **gate técnico de regressão 30k seed42** para PRs impactadas, conforme #1285. O gate oficial testa sua grade fixa; ele **não** reexecuta automaticamente este ensaio histórico V2 parametrizado. Para reproduzi-lo de novo, é necessária integração explícita ao fluxo oficial, com plano externo congelado, hashes e nova evidência no SHA exato, sem reutilizar `chat-approved-once`.

O original baixado é evidência, não um segundo plano de configuração. A prova de paridade e o bundle permanecem disponíveis, sem sobrescrever snapshots, TEST, DT-15 ou publicação operacional. O run físico histórico da V2 é **evidência técnica não confirmatória**.

## Separação do bundle de calibração

O bundle de evidências continua com três componentes **funcionais** (plano, resultado, relatório) e manifesto SHA, mais o snapshot do fit como anexo ao resultado. O `plan.json` é o único plano configurável. O arquivo histórico `plan-tf.json` do run original continua imutável no acervo, com seu SHA próprio, e não integra a estrutura versionada como segundo plano ativo.

## Limites

A igualdade tipada do C# prova que os defaults declarados reproduzem a configuração **efetiva** do original segundo o contrato atual. Não prova, isoladamente, constantes internas não modeladas. A execução física V2 histórica teve paridade; não estender sua evidência a outro SHA ou campanha científica. Integrar apenas após CI e gate físico da PR no SHA atual; publicação e ativação permanecem vedadas.
