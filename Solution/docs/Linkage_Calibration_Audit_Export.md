# Exportação de auditoria da calibração de Linkage

## Finalidade

`Jornada.Linkage.Evaluation` possui um modo **somente leitura** para exportar o estado de calibração de um modelo sem participar do runtime e sem publicar/ativar modelo.

Uso:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --export-calibration ./linkage-calibration-audit.json \
  --connection-string "<SQL Server>"
```

Sem `--model-id`, o exportador seleciona o modelo `ATIVO`. Com `--model-id`, somente modelos `ATIVO` ou `VALIDADO` podem ser exportados; `RASCUNHO`, `GERANDO`, `INATIVO` e `FALHOU` são recusados fail-closed. Para inspecionar um candidato já validado:

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


## Semântica de intercâmbio

O artefato declara explicitamente:

- `uProbabilitySemantics=CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION`: descreve o **u empírico** medido no universo deduplicado de candidatos sobreviventes ao ruleset;
- `nominalNameUSource` e `nominalMotherNameUSource`: registram separadamente a fonte nominal efetivamente aplicada, derivada dos parâmetros persistidos — `BLOCKING_CONDITIONED`, `IBGE_BOOTSTRAP` ou `NAO_DECLARADO`;
- `splinkDefaultRandomPairUEquivalent=false`: o artefato não afirma equivalência ao u de pares aleatórios assumido por outra ferramenta;
- `comparisonStateMapping.complete=false`, com os estados semânticos de nascimento que não devem ser colapsados silenciosamente.

Essa declaração transforma o JSON em contrato de auditoria/intercâmbio da Jornada. Ela **não** reivindica compatibilidade Splink completa nem reproduz o EM de uma segunda implementação. Qualquer adapter externo precisa declarar mapeamento explícito para estados não bijetivos.


## Round-trip C# obrigatório

A exportação é agora materializada como `LinkageCalibrationAuditDocument` tipado. Antes de gravar o JSON, o próprio executável:

1. serializa o documento tipado;
2. reimporta o JSON por `LinkageCalibrationAuditRoundTrip.Import`;
3. valida invariantes do envelope e da proveniência;
4. compara campo a campo o documento original e o reimportado;
5. só grava o arquivo quando o round-trip está `CONFORME`.

O método corrente é `JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1`.

O importador é fail-closed: rejeita membros JSON desconhecidos, `schemaVersion/nature/purpose` divergentes, modelo fora de `ATIVO|VALIDADO`, diferença entre `statusAtExport` e o status do modelo, declaração de equivalência ao u aleatório padrão do Splink, mapeamento semântico indevidamente marcado como completo, lista de estados não bijetivos diferente do contrato, TF marcada como habilitada, proveniência nominal incompatível e passes associados a rulesets ausentes.

Rejeitar membros desconhecidos é deliberado: o round-trip não pode parecer conforme descartando silenciosamente um campo que o C# não entende. Como consequência, dados pessoais ou qualquer extensão não versionada inserida no JSON não são absorvidos silenciosamente pelo contrato v1.

Esse round-trip prova **fidelidade do formato de intercâmbio da Jornada**. Ele não mede paridade de scorer, não valida comparadores a partir de dados brutos, não estima qualidade estatística e não autoriza promoção do modelo.
