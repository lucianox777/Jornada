# Exportação de auditoria da calibração de Linkage

## Finalidade

`Jornada.Linkage.Evaluation` possui um modo **somente leitura** para exportar o estado de calibração de um modelo sem participar do runtime e sem publicar/ativar modelo.

Uso:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --export-calibration ./linkage-calibration-audit.json \
  --connection-string "<SQL Server>"
```

Sem `--model-id`, o exportador seleciona o modelo `ATIVO`. Para inspecionar um modelo específico:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --export-calibration ./linkage-calibration-audit.json \
  --model-id "<uuid>" \
  --connection-string "<SQL Server>"
```

## Conteúdo

O JSON inclui:

- metadados persistidos do modelo;
- parâmetros e estatísticas do Fellegi–Sunter;
- ruleset de blocking e seus passes;
- snapshot/versionamento da referência de frequências nominais e resumo de cobertura;
- quantidade de linhas eventualmente materializadas em `identidade.frequencia_linkage`;
- vetores sintéticos de conformidade de `SPLINK_TERM_FREQUENCY_V1`.

O exportador não copia a tabela nominal de frequências linha a linha e não exporta observações de pessoas. O objetivo é permitir conferência dos parâmetros, proveniência e matemática declarada sem criar uma segunda implementação de decisão.

## Term frequency

Os vetores TF são produzidos pela mesma implementação C# catalogada em `SplinkCompatibleTermFrequency`, incluindo casos comum/comum, raro/raro, raro/comum, piso e peso zero.

A presença dos vetores **não habilita TF no scorer**. O JSON declara `runtimeEnabled=false`. Ativação operacional de term frequency continua sujeita à calibração no universo candidato, TRAIN/VALIDATION/TEST, avaliação por estrato e ao gate estatístico da issue #31.

## Fronteira de segurança

Este modo:

- executa apenas `SELECT`;
- não cria `linkage_run`;
- não grava `identity_map`, `vinculo_fonte` ou Gold;
- não altera status de modelo;
- não substitui relatório estatístico representativo;
- não reivindica auditoria externa por Splink/Python.

Ele recupera a **auditabilidade dos parâmetros e da proveniência**, sem reintroduzir o antigo runner Python nem um resolvedor paralelo.
