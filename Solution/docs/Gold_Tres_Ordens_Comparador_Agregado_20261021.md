# Gold incremental — comparar três ordens por agregados, sem exportar identidades

**21/10/2026 — CLI diagnóstica, TRAIN_SELECTION.** Esta entrega
oferece a comparação offline segura dos **três relatórios
`gold-runner-study.json` produzidos pelo Runner real** no protocolo
`GoldTrainThreeOrderProtocol`. Pressupõe a integração anterior
da PR #1171, que adiciona `goldThreeOrderTrial.stablePartitionSha256`.
A PR #1172 é uma variante paralela sobre os mesmos arquivos de
harness: **não mesclar ambas sem reconciliar a implementação**.

## Objetivo e contrato da evidência

As mesmas observações TRAIN e mesmo fitted snapshot FS devem ser
submetidos ao mesmo Runner incremental físico, em **três bancos
LocalDB JornadaV9Gold exclusivos**, novos, inicialmente vazios.
Ordens pré-registradas:

1. `BASELINE_G0_G1_G2_G3`
2. `REVERSE_G3_G2_G1_G0`
3. `INTERLEAVED_G1_G3_G0_G2`

Cada execução deve publicar as quatro ondas e realizar a
reavaliação final. O `gold-runner-study.json` deve trazer
`goldEndToEnd`, `goldBcubed`, `publication`,
`pendingReasonBreakdown` e `goldThreeOrderTrial`,
incluindo o digest canônico da partição final por **ID estável
do CSV original**, e não pelo IDENTITY alocado pelo SQL ou bytes
dos UUIDs aleatórios.

**Este comando NÃO inicia bancos nem executa as três rodadas.**
Se só existe uma execução, a comparação recusa os dados.

## CLI agregada

```text
--v9-gold-compare-aggregates \
  <baseline/gold-runner-study.json> \
  <reverse/gold-runner-study.json> \
  <interleaved/gold-runner-study.json> \
  <saida-comparacao-nova.json>
```

Os três inputs devem ser de diretórios distintos, com nome
`gold-runner-study.json`, e a saída precisa ser inédita. A CLI
não abre SQL, não grava UUIDs, não usa rótulos e não lê TEST.
A execução é impedida antes da escrita se faltar qualquer
invariante de proveniência ou alguma das três ordens.

Valida, por relatório:

- protocolo e contrato de ID estável do CSV; SHA-256 de corpus,
  snapshot, tentativa isolada e partição; três tentativas distintas;
  `scope=TRAIN_SELECTION`, todas as ondas publicadas,
  `testUsed=false`, `activated=false`,
  `truthExported=false`, `uuidExported=false`;
- mesmo corpus, fitted snapshot e universo de fontes/identidades
  em todas as ordens; ordem exata declarada e observada;
- reconciliação entre `goldEndToEnd`, `goldBcubed`,
  `publication` e soma das categorias de pendência;
- FDR das associações calculada pelos contadores próprios,
  **nunca `1-pairPrecision`**.

O comparativo contém apenas agregados: recall e precisão por
pares Gold, B-cubed convencional e coverage-aware, pendências,
fragmentação, referências impuras, falsas associações, FDR e
a equivalência da partição completa por igualdade do digest
canônico, sem exportar rótulos, observações individuais ou
UUIDs Gold. É um compromisso criptográfico contra diferenças
da partição, não uma atestação externa do isolamento dos bancos.

## O que o resultado NÃO demonstra

- Três `isolatedRunFingerprintSha256` diferentes **não são prova
  independente** de SQL fisicamente isolado; verificar também os
  marcadores residentes e a preparação de cada LocalDB.
- Esta comparação **não realiza** a calibração do limiar inferior:
  ainda é uma hipótese do calibrador TRAIN e a criação de UUID
  por score baixo continua proibida até medir duplicatas.
- CI verde deste comando **não significa** que as três rodadas
  físicas já ocorreram e tampouco certifica a população municipal.
- O ensaio usa TRAIN_SELECTION, não reabre VALIDATION nem TEST,
  não autoriza promoção de `ref` ou DT-15.

Código: `GoldTrainThreeOrderAggregateCommand.cs`, roteado em
`Jornada.Linkage.Runner/Program.cs`. Testes sintéticos puros
`GoldTrainThreeOrderAggregateCommandTests.cs`.
Arquivos `V9*.cs`, FS/TF, Gold real, DB compartilhada,
IBGE original e pipeline produtivo permanecem inalterados.
