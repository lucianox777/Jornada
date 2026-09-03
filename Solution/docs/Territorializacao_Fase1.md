# Territorialização da Pessoa - Fase 1 v3.39

## Decisão

A territorialização necessária ao BI é responsabilidade do **Gestor/origem**. A Jornada não faz chamada online Pessoa-a-Pessoa à PRODAM no caminho normal da ingestão da Fase 1. Na v3.39 a restrição é também refletida no DDL: novas referências resolvidas aceitam `origem_geografia=ORIGEM` apenas.

Para `ENDERECO_RESIDENCIAL` e `REFERENCIA_TERRITORIAL`, cada atributo deve declarar `situacaoGeografia`:

- `RESOLVIDA`: exige `geografia.distritoCodigo`, `distritoNome`, `subprefeituraCodigo`, `subprefeituraNome` e `referenciaMalha`;
- `FORA_MUNICIPIO`;
- `SEM_ENDERECO_APTO`;
- `NAO_RESOLVIDA_ORIGEM`.

Nos três últimos estados, `geografia` deve ser `null`. Isso distingue ausência territorial legítima de falha de qualidade da origem.

A Jornada valida códigos contra seus catálogos de Distrito/Subprefeitura e persiste a `referenciaMalha` enviada. `origem_geografia=ORIGEM` identifica o responsável pela resolução. O campo `resolvido_em` usa `manifest.dataReferencia` como marco do snapshot recebido.

## Carga inicial massiva

A primeira carga pode ser executada em `controle.modo_carga_inicial`. Enquanto esse modo estiver ativo, `Linkage.Runner` e `Linkage.Parameters.Worker` no modo `GENERATE_DRAFT` recusam execução; o Processor drena a fila e `serving.v_bi_carga_inicial` mede Pessoas processadas por hora.

A aceitação de Produção depende de ensaio HML com volumetria representativa. Se a projeção para a carga integral for institucionalmente inadequada, deve-se desenhar um modo bulk específico; não se deve introduzir chamadas geográficas online no Processor para resolver capacidade.


## Modo de carga inicial

`controle.modo_carga_inicial` é **não preemptivo**: impede o início de novos `GENERATE_DRAFT`/Runner, mas não cancela um job analítico que já tenha começado. A sequência operacional é aguardar jobs existentes, ativar o modo, drenar/medir a carga e só então desativá-lo.
